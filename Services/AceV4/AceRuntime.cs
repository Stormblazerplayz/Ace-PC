using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using Nimbus.AceV4.Audio;
using Nimbus.AceV4.Brain;
using Nimbus.AceV4.Pc;
using Nimbus.Services;
using Nimbus.Services.Calendar;

namespace Nimbus.AceV4
{
    public enum StageKind { Wake, Status, Mood, Heard, Answer, Level, Sleep }

    /// <summary>Something for Ace's eyes overlay to show. Raised on any thread.</summary>
    public sealed record StageEvent(StageKind Kind, string Text = "", AceAnswer? Answer = null, double Value = 0, bool LookAround = false);

    /// <summary>One thing a wake-up check noticed. Importance: 0 = just info, 1 = worth saying, 2 = needs you.</summary>
    public sealed record Finding(string Text, int Importance, string Key = "");

    /// <summary>
    /// Keeps Ace V4 alive in the background: wakes every 15-30 minutes (random) to check the PC, greets you the
    /// first time you use the PC each day, nudges you toward bed, fires reminders, listens for "Hey Ace" and
    /// Ctrl+Alt+Space, and plays the sounds. The Ace window listens to it.
    /// </summary>
    public sealed class AceRuntime : IDisposable
    {
        public static AceRuntime Current { get; } = new();

        public AceV4Config Config { get; private set; } = new();
        public AceV4State State { get; private set; } = new();
        public AceV4Brain Brain { get; private set; } = null!;
        public SoundEngine Sounds => SoundEngine.Shared;
        public AceSpeech Speech { get; } = new();
        public Dictation Dictation { get; } = new();
        public WakeWord WakeWord { get; } = new();

        /// <summary>For the Ace window (orb moods, status and answers).</summary>
        public event Action<StageEvent>? Stage;
        /// <summary>Every answer (asked or found by a check), for the Ace page's conversation.</summary>
        public event Action<string, AceAnswer>? Answered;
        /// <summary>Something about the runtime changed (next check time, listening, loaded…).</summary>
        public event Action? Changed;

        public DateTime NextCheck { get; private set; }
        public DateTime LastCheck => State.LastWakeCheck;
        public bool Started { get; private set; }
        public bool Listening => Dictation.IsListening;
        public bool ScreenOn { get; private set; } = true;
        public bool Away { get; private set; }
        public bool PageVisible { get; set; }
        /// <summary>Where the eyes are looking, -1 (left) to 1 (right). Sounds follow it.</summary>
        public double EyePan { get; set; }

        private readonly TaskCompletionSource<bool> _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Random _rng = new();
        private Timer? _tick, _checkTimer;
        private int _checking, _ticks, _checksDone;
        private DateTime _lastActivitySave = DateTime.MinValue;
        private HwndSource? _hwnd;
        private IntPtr _powerNotify;
        private const int HotkeyId = 0x4ACE;
        private CancellationTokenSource? _listenCts;

        public Task WhenLoaded => _loaded.Task;

        // ===================================================================== Start up

        /// <summary>Call once when Nimbus starts (on the UI thread).</summary>
        public void Start()
        {
            if (Started) return;
            Started = true;
            Config = AceV4Config.Load();
            State = AceV4State.Load();
            Brain = new AceV4Brain(Config, State);
            Brain.Status += s => { if (s == "hide-for-screenshot") Raise(new StageEvent(StageKind.Sleep)); else if (s.Length > 0) Raise(new StageEvent(StageKind.Status, s)); };
            Brain.RunChecks = ct => RunChecksAsync(manual: true, ct);
            Brain.CheckForUpdates = ct => CheckForUpdatesAsync(manual: true, ct);
            // Tell the updater this version started fine (if it's waiting), then look for a newer one in a bit.
            AutoUpdater.MarkStartedOk();
            _ = Task.Run(async () => { await Task.Delay(TimeSpan.FromSeconds(45)).ConfigureAwait(false); if (Config.AutoUpdate) await CheckForUpdatesAsync(manual: false, CancellationToken.None).ConfigureAwait(false); });
            // Card buttons (trivia answers, "Yes" in 20 questions) talk to Ace like typing would.
            Nimbus.Controls.AceCardViews.AskAce += text => _ = AskAsync(text);
            ApplySoundSettings();
            Sounds.Start();

            Task.Run(() =>
            {
                try { ActivityTracker.BackfillFromEventLog(); } catch { }
                Brain.Load();
                _loaded.TrySetResult(true);
                Changed?.Invoke();
            });

            ReminderStore.Changed += () => Changed?.Invoke();
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerMode;

            _tick = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            ScheduleNextCheck();
            ApplyAutostart();
            if (Config.WakeWord) StartWakeWord();
            Speech.Speaking += talking => { if (talking) WakeWord.Pause(); else WakeWord.Resume(); };

            // Say hi a few seconds after start, if this is the first time today.
            _ = Task.Run(async () =>
            {
                await Task.Delay(4000).ConfigureAwait(false);
                await MaybeGreetAsync("start").ConfigureAwait(false);
                // Right after an update: show what's new, once.
                await Task.Delay(2500).ConfigureAwait(false);
                await MaybeShowWhatsNewAsync().ConfigureAwait(false);
            });
        }

        /// <summary>After an update, Ace shows the "What's new" notes once (you can always ask "what's new").</summary>
        public async Task MaybeShowWhatsNewAsync()
        {
            try
            {
                await _loaded.Task.ConfigureAwait(false);
                string from = AutoUpdater.PendingWhatsNewFrom;
                if (from.Length == 0) return;
                var a = Brain.WhatsNewAnswer(from, AutoUpdater.CurrentText);
                AutoUpdater.MarkWhatsNewShown();
                Play(Sfx.Success, 0.8);
                Raise(new StageEvent(StageKind.Answer, "", a));
                Answered?.Invoke("", a);
            }
            catch (Exception ex) { Debug.WriteLine("Ace what's new: " + ex.Message); }
        }

        /// <summary>Hooks the main window for the Ctrl+Alt+Space hotkey and screen on/off messages.</summary>
        public void AttachWindow(Window window)
        {
            var helper = new WindowInteropHelper(window);
            if (helper.Handle == IntPtr.Zero) return;
            _hwnd = HwndSource.FromHwnd(helper.Handle);
            _hwnd?.AddHook(WndProc);
            try { Win32.RegisterHotKey(helper.Handle, HotkeyId, Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_NOREPEAT, (uint)Win32.VK_SPACE); } catch { }
            try
            {
                var guid = Win32.GUID_CONSOLE_DISPLAY_STATE;
                _powerNotify = Win32.RegisterPowerSettingNotification(helper.Handle, ref guid, 0);
            }
            catch { }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Win32.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                handled = true;
                _ = ListenAndAskAsync("hotkey");
            }
            else if (msg == Win32.WM_POWERBROADCAST && wParam.ToInt32() == Win32.PBT_POWERSETTINGCHANGE && lParam != IntPtr.Zero)
            {
                try
                {
                    // POWERBROADCAST_SETTING: a GUID, a length, then the data (0 = off, 1 = on, 2 = dimmed).
                    var setting = System.Runtime.InteropServices.Marshal.PtrToStructure<Guid>(lParam);
                    if (setting == Win32.GUID_CONSOLE_DISPLAY_STATE)
                    {
                        int state = System.Runtime.InteropServices.Marshal.ReadInt32(lParam, 20);
                        bool on = state != 0;
                        if (on != ScreenOn)
                        {
                            ScreenOn = on;
                            Changed?.Invoke();
                            if (on) _ = Task.Run(() => MaybeGreetAsync("screen on"));
                        }
                    }
                }
                catch { }
            }
            return IntPtr.Zero;
        }

        private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
        {
            if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon or SessionSwitchReason.ConsoleConnect)
                _ = Task.Run(async () => { await Task.Delay(2500).ConfigureAwait(false); await MaybeGreetAsync("unlock").ConfigureAwait(false); });
            if (e.Reason == SessionSwitchReason.SessionLock) ActivityTracker.Save();
        }

        private void OnPowerMode(object? sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend) { ActivityTracker.Save(); State.Save(); }
            if (e.Mode == PowerModes.Resume)
            {
                ScheduleNextCheck();
                _ = Task.Run(async () => { await Task.Delay(5000).ConfigureAwait(false); await MaybeGreetAsync("resume").ConfigureAwait(false); });
            }
        }

        public void ApplySoundSettings()
        {
            Sounds.Enabled = Config.Sounds;
            Sounds.Volume = Config.SoundVolume;
        }

        public void SaveConfig()
        {
            Config.Save();
            ApplySoundSettings();
            ApplyAutostart();
            if (Config.WakeWord) StartWakeWord(); else WakeWord.Stop();
            ScheduleNextCheck();
        }

        private void StartWakeWord()
        {
            if (WakeWord.Running) return;
            WakeWord.Heard -= OnWakeWord;
            WakeWord.Heard += OnWakeWord;
            if (!WakeWord.Start()) Debug.WriteLine(WakeWord.Problem);
        }

        private void OnWakeWord() => _ = ListenAndAskAsync("wake word");

        /// <summary>Starts Ace with Windows (current user only, no admin needed). Older versions used the name "Nimbus".</summary>
        private void ApplyAutostart()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                if (key == null) return;
                if (key.GetValue("Nimbus") is string old && old.Contains("--background")) key.DeleteValue("Nimbus", false);
                string? exe = Environment.ProcessPath;
                if (Config.StartWithWindows && exe != null && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    key.SetValue("Ace", "\"" + exe + "\" --background");
                else if (!Config.StartWithWindows && key.GetValue("Ace") != null)
                    key.DeleteValue("Ace", false);
            }
            catch { }
        }

        // ===================================================================== Sounds and stage

        public void Play(Sfx s, double gain = 1) => Sounds.Play(s, Config.SpatialSound ? EyePan : 0, gain);

        private void Raise(StageEvent e)
        {
            try { Stage?.Invoke(e); } catch { }
        }

        // ===================================================================== Updates

        /// <summary>
        /// Checks GitHub for a newer Ace and installs it. A check you ask for installs right away; an automatic one
        /// waits until you've stepped away for 2 minutes (and never during a full-screen game or a focus round).
        /// Returns what Ace says about it.
        /// </summary>
        public async Task<string> CheckForUpdatesAsync(bool manual, CancellationToken ct)
        {
            bool CanRestart()
            {
                if (manual) return true;
                if (Config.QuietDuringFullScreen && Win32.FullScreenAppInFront()) return false;
                if (FocusTimer.Phase == FocusPhase.Focus && !FocusTimer.Paused) return false;
                return !ScreenOn || Win32.IdleSeconds() >= 120;
            }
            var (outcome, message) = await AutoUpdater.CheckAndInstallAsync(CanRestart, async label =>
            {
                Raise(new StageEvent(StageKind.Wake, label));
                Play(Sfx.Wake, 0.6);
                await Task.Delay(2500, ct).ConfigureAwait(false);
            }, ct, beta: Config.BetaChannel).ConfigureAwait(false);
            Changed?.Invoke();
            if (outcome == AutoUpdater.Outcome.Restarting)
                Application.Current?.Dispatcher.BeginInvoke(() => Application.Current.Shutdown());
            return message;
        }

        /// <summary>Should Ace stay quiet right now (full-screen game or video, screen off, or you're away)?</summary>
        public bool ShouldStayQuiet(out string why)
        {
            why = "";
            if (!ScreenOn) { why = "screen off"; return true; }
            if (Config.QuietDuringFullScreen && Win32.FullScreenAppInFront()) { why = "full screen"; return true; }
            if (Win32.IdleSeconds() > Config.AwayMinutes * 60) { why = "away"; return true; }
            if (FocusTimer.Phase == FocusPhase.Focus && !FocusTimer.Paused) { why = "focusing"; return true; }
            return false;
        }

        private void SpeakIfWanted(AceAnswer a, bool spoken)
        {
            if (!spoken && !Config.SpeakReplies) return;
            if (!spoken && PageVisible) return;   // typed on the Ace page: just show it
            Speech.Say(a.Speak ?? Composer.ForSpeech(a.Text), Config);
        }

        // ===================================================================== Asking

        /// <summary>Asks Ace V4. Plays the thinking hum and the answer sound, speaks when asked out loud, and runs countdown sounds.</summary>
        public async Task<AceAnswer> AskAsync(string text, bool spoken = false, bool showOnStage = false, CancellationToken ct = default)
        {
            await _loaded.Task.ConfigureAwait(false);
            Speech.Hush();
            if (showOnStage) Raise(new StageEvent(StageKind.Mood, "think"));
            var hum = Sounds.Enabled ? Sounds.Loop(Sfx.Think, 0.9) : new LoopHandle();
            AceAnswer a;
            try { a = await Brain.AskAsync(text, ct, spoken).ConfigureAwait(false); }
            finally { hum.Stop(350); }

            Play(a.Mood == "alert" ? Sfx.Alert : a.Source is "error" ? Sfx.Error : a.Confidence < 0.35 ? Sfx.Pop : Sfx.Success, 0.85);
            foreach (var card in a.Cards.OfType<CountdownCard>()) WatchCountdown(card);
            if (showOnStage) Raise(new StageEvent(StageKind.Answer, text, a));
            SpeakIfWanted(a, spoken);
            Answered?.Invoke(text, a);
            return a;
        }

        private void WatchCountdown(CountdownCard card)
        {
            card.Tick += left =>
            {
                if (left > 0) Play(left <= 3 ? Sfx.CountdownFinal : Sfx.Countdown, left <= 3 ? 0.6 : 0.9);
            };
            card.Finished += () => { if (!card.Cancelled) Play(Sfx.Shutdown); else Play(Sfx.Pop); };
        }

        /// <summary>Mic button, "Hey Ace" or Ctrl+Alt+Space: listen, then answer out loud. Pressing again stops listening.</summary>
        public async Task<AceAnswer?> ListenAndAskAsync(string from, bool showOnStage = true)
        {
            if (Dictation.IsListening) { Dictation.StopNow(); return null; }
            await _loaded.Task.ConfigureAwait(false);
            WakeWord.Pause();
            Speech.Hush();
            _listenCts?.Cancel();
            _listenCts = new CancellationTokenSource();
            var ct = _listenCts.Token;
            void OnLevel(double v) => Raise(new StageEvent(StageKind.Level, Value: v));
            void OnStage(string s) => Raise(new StageEvent(StageKind.Status, s));
            Dictation.Level += OnLevel;
            Dictation.Stage += OnStage;
            try
            {
                if (showOnStage && !PageVisible) Raise(new StageEvent(StageKind.Wake, "Listening…"));
                Play(Sfx.ListenStart);
                Changed?.Invoke();
                string heard;
                try { heard = await Dictation.ListenAsync(Config, ct).ConfigureAwait(false); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Play(Sfx.Error);
                    var problem = AceAnswer.Say(ex.Message, "error", 0);
                    Raise(new StageEvent(StageKind.Answer, "", problem));
                    Answered?.Invoke("", problem);
                    return problem;
                }
                Play(Sfx.ListenStop);
                Changed?.Invoke();
                if (heard.Length == 0)
                {
                    Raise(new StageEvent(StageKind.Status, "I didn't hear anything."));
                    await Task.Delay(1500, ct).ConfigureAwait(false);
                    if (!PageVisible) Raise(new StageEvent(StageKind.Sleep));
                    return null;
                }
                Raise(new StageEvent(StageKind.Heard, heard));
                return await AskAsync(heard, spoken: true, showOnStage: showOnStage && !PageVisible, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return null; }
            finally
            {
                Dictation.Level -= OnLevel;
                Dictation.Stage -= OnStage;
                WakeWord.Resume();
                Changed?.Invoke();
            }
        }

        // ===================================================================== The heartbeat

        private void Tick()
        {
            try
            {
                var now = DateTime.Now;
                double idle = Win32.IdleSeconds();
                _ticks++;

                // Activity, for working out sleep times.
                if (idle < 120 && ScreenOn)
                {
                    ActivityTracker.Record(now);
                    if (now - _lastActivitySave > TimeSpan.FromMinutes(10)) { ActivityTracker.Save(); _lastActivitySave = now; }
                }

                // Away and back.
                bool awayNow = idle > Config.AwayMinutes * 60 || !ScreenOn;
                if (awayNow != Away)
                {
                    Away = awayNow;
                    Changed?.Invoke();
                    if (!awayNow) _ = Task.Run(WelcomeBackAsync);
                }

                FireReminders(now);
                FocusTick(now);
                if (ScreenOn) ScreenTime.Tick(now, idle < 120 ? ScreenTime.ForegroundApp() : null, idle);
                if (_ticks % 12 == 3) ScreenTimeHeadsUp(now);                               // every minute
                if (_ticks % 12 == 0) _ = Task.Run(() => MaybeBedtimeNudgeAsync(now));     // every minute
                if (_ticks % 12 == 6) WatchApps(now, idle);                                // every minute
                if (_ticks % 120 == 3) SampleHealth(now, idle);                            // every 10 minutes
                if (_ticks % 60 == 9) _ = Task.Run(() => MaybeWeeklyReportAsync(now));     // every 5 minutes
                if (_ticks % 60 == 21 && Config.AutoUpdate && !AutoUpdater.Busy && (AutoUpdater.CheckDue || AutoUpdater.ReadyToInstall))
                    _ = Task.Run(() => CheckForUpdatesAsync(manual: false, CancellationToken.None));   // every 6 hours, or when a ready update can go in
                // The check timer can miss while the PC sleeps; catch up.
                if (Config.WakeChecks && NextCheck != default && now > NextCheck.AddMinutes(2)) ScheduleNextCheck(soon: true);
            }
            catch (Exception ex) { Debug.WriteLine("Ace tick: " + ex.Message); }
        }

        public void ScheduleNextCheck(bool soon = false)
        {
            int min = Math.Clamp(Config.WakeMinMinutes, 5, 240), max = Math.Clamp(Config.WakeMaxMinutes, min, 240);
            double minutes = soon ? 0.5 + _rng.NextDouble() : min + _rng.NextDouble() * (max - min);
            NextCheck = DateTime.Now.AddMinutes(minutes);
            _checkTimer?.Dispose();
            if (Config.WakeChecks)
                _checkTimer = new Timer(_ => { _ = RunScheduledCheckAsync(); }, null, TimeSpan.FromMinutes(minutes), Timeout.InfiniteTimeSpan);
            Changed?.Invoke();
        }

        private async Task RunScheduledCheckAsync()
        {
            try
            {
                await _loaded.Task.ConfigureAwait(false);
                await RunChecksAsync(manual: false, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) { Debug.WriteLine("Ace check: " + ex.Message); }
            finally { ScheduleNextCheck(); }
        }

        // ===================================================================== Wake-up checks

        /// <summary>
        /// Looks at everything: resources, time, open apps, notifications, documents, storage, big files, battery,
        /// calendar, screen and whether you're here. On a schedule it only speaks up when something matters, and
        /// saves things for later when you're away, the screen's off, or a full-screen game is running.
        /// </summary>
        public async Task<AceAnswer> RunChecksAsync(bool manual, CancellationToken ct)
        {
            if (Interlocked.Exchange(ref _checking, 1) == 1)
                return AceAnswer.Say("I'm already checking. One sec!", "checks", 0.6);
            try
            {
                bool quiet = !manual && ShouldStayQuiet(out _);
                bool showEyes = !quiet && !(manual && PageVisible);
                if (showEyes)
                {
                    Raise(new StageEvent(StageKind.Wake, "Checking things…", LookAround: true));
                    Play(Sfx.Wake, 0.7);
                    await Task.Delay(350, ct).ConfigureAwait(false);
                    Play(Sfx.EyesOut);
                }
                var since = State.LastWakeCheck == default ? DateTime.Now.AddHours(-2) : State.LastWakeCheck;
                var findings = new List<Finding>();
                var stats = new StatsCard { Title = "PC check · " + DateTime.Now.ToString("h:mm tt") };

                async Task Part(string label, Action work)
                {
                    if (showEyes) Raise(new StageEvent(StageKind.Status, label));
                    Brain.Step(label);
                    var sw = Stopwatch.StartNew();
                    try { await Task.Run(work, ct).ConfigureAwait(false); } catch (OperationCanceledException) { throw; } catch (Exception ex) { Debug.WriteLine(label + ": " + ex.Message); }
                    // Give the eyes time to look around, so it feels like Ace is actually checking.
                    if (showEyes && sw.ElapsedMilliseconds < 650) await Task.Delay(650 - (int)sw.ElapsedMilliseconds, ct).ConfigureAwait(false);
                }

                await Part("Checking CPU, memory and temps…", () => CheckResources(findings, stats));
                await Part("Looking at the time…", () => CheckTime(findings, manual));
                await Part("Seeing what's open…", () => CheckApps(findings, stats, manual));
                var notes = await NotificationReader.RecentAsync(DateTime.Now - since + TimeSpan.FromMinutes(1)).ConfigureAwait(false);
                await Part("Reading notifications…", () => CheckNotifications(findings, notes));
                await Part("Looking at your documents…", () => CheckDocuments(findings, since, manual));
                await Part("Checking storage…", () => CheckStorage(findings, stats));
                if (manual || _checksDone % 3 == 0)
                    await Part("Looking for big files…", () => CheckBigFiles(findings, ct));
                await Part("Checking your calendar and reminders…", () => CheckCalendar(findings, manual));
                await Part("Checking battery and screen…", () => CheckPower(findings, stats, manual));
                _checksDone++;

                State.LastWakeCheck = DateTime.Now;
                State.Save();

                var important = findings.Where(f => f.Importance >= 1).OrderByDescending(f => f.Importance).ToList();
                AceAnswer answer;
                if (important.Count == 0)
                {
                    answer = AceAnswer.Say(Composer.Pick("check.ok", new[]
                    {
                        "I checked everything. All good, {user}!",
                        "Quick check done. Nothing needs you right now.",
                        "All clear! Your PC's happy and nothing new needs you.",
                    }), "checks", 1);
                    if (manual) foreach (var f in findings.Where(f => f.Importance == 0).Take(4)) answer.Text += " " + f.Text;
                }
                else
                {
                    string lead = important.Any(f => f.Importance >= 2) ? Composer.Pick("check.alert", new[] { "Heads up, {user}!", "Hey {user}, a few things need you.", "Quick heads up:" })
                                                                         : Composer.Pick("check.some", new[] { "I took a look around.", "Checked in on things.", "Here's what I noticed." });
                    answer = AceAnswer.Say(lead + " " + string.Join(" ", important.Take(manual ? 6 : 4).Select(f => f.Text)), "checks", 1);
                    answer.Speak = lead + " " + string.Join(" ", important.Take(2).Select(f => f.Text));
                    if (important.Any(f => f.Importance >= 2)) answer.Mood = "alert";
                }
                answer.Text = Composer.Fill(answer.Text);
                if (answer.Speak != null) answer.Speak = Composer.Fill(answer.Speak);
                answer.Cards.Add(stats);

                if (manual) return answer;   // the Ask path plays sounds and shows it

                if (quiet)
                {
                    // Save it for when you're back.
                    foreach (var f in important)
                        if (!State.AwayFindings.Contains(f.Text)) State.AwayFindings.Add(f.Text);
                    while (State.AwayFindings.Count > 12) State.AwayFindings.RemoveAt(0);
                    State.Save();
                    return answer;
                }

                if (important.Count == 0)
                {
                    Raise(new StageEvent(StageKind.Status, "All good!"));
                    Play(Sfx.Blink, 0.8);
                    await Task.Delay(1600, ct).ConfigureAwait(false);
                    Raise(new StageEvent(StageKind.Sleep));
                    Play(Sfx.EyesIn, 0.8);
                }
                else
                {
                    Play(answer.Mood == "alert" ? Sfx.Alert : Sfx.Notify);
                    Raise(new StageEvent(StageKind.Answer, "", answer));
                    if (answer.Mood == "alert") SpeakIfWanted(answer, spoken: false);
                    Answered?.Invoke("", answer);
                }
                return answer;
            }
            finally
            {
                Interlocked.Exchange(ref _checking, 0);
                Changed?.Invoke();
            }
        }

        private static void CheckResources(List<Finding> f, StatsCard stats)
        {
            var s = Hub.Stats;
            stats.Rows.Add(new StatRow("CPU", $"{s.Cpu:0}%", s.Cpu, s.Cpu > 85));
            stats.Rows.Add(new StatRow("Memory", $"{s.MemUsedGb:0.0} of {s.MemTotalGb:0} GB", s.MemPercent, s.MemPercent > 88));
            if (s.Gpu.HasValue) stats.Rows.Add(new StatRow("GPU", $"{s.Gpu:0}%", s.Gpu.Value, false));
            if (s.CpuTemp.HasValue) stats.Rows.Add(new StatRow("CPU temp", $"{s.CpuTemp:0}°C", Math.Min(100, s.CpuTemp.Value), s.CpuTemp >= 90));
            if (s.GpuTemp.HasValue) stats.Rows.Add(new StatRow("GPU temp", $"{s.GpuTemp:0}°C", Math.Min(100, s.GpuTemp.Value), s.GpuTemp >= 88));

            if (s.Cpu > 85)
            {
                var top = PcScan.TopCpu(1).FirstOrDefault();
                f.Add(new Finding($"Your CPU is working hard at {s.Cpu:0}%{(top.Name != null ? ", mostly " + WindowControl.AppName(top.Name) : "")}.", 1, "cpu"));
            }
            if (s.MemPercent > 88)
            {
                var top = PcScan.TopApps(1).FirstOrDefault();
                f.Add(new Finding($"Memory is {s.MemPercent:0}% full{(top != null ? $". {WindowControl.AppName(top.Name)} is using {top.RamMb / 1024:0.0} GB" : "")}. Closing apps you're not using would help.", 1, "ram"));
            }
            if (s.CpuTemp >= 90) f.Add(new Finding($"Your CPU is hot, {s.CpuTemp:0}°C. Make sure the vents aren't blocked.", 2, "cputemp"));
            if (s.GpuTemp >= 88) f.Add(new Finding($"Your GPU is hot, {s.GpuTemp:0}°C.", 2, "gputemp"));
        }

        private void CheckTime(List<Finding> f, bool manual)
        {
            var now = DateTime.Now;
            if (manual) f.Add(new Finding($"It's {now:h:mm tt}.", 0));
            var sleep = ActivityTracker.Summary(Config.SleepGoalHours);
            if (sleep == null) return;
            if (PastBedtime(now, sleep.SuggestBed, out _))
                f.Add(new Finding($"It's {now:h:mm tt}, past your bedtime of {ActivityTracker.Clock(sleep.SuggestBed)}. Time to start winding down?", 1, "late"));
        }

        /// <summary>True between the suggested bedtime and 4 AM.</summary>
        private static bool PastBedtime(DateTime now, TimeSpan bed, out TimeSpan by)
        {
            var t = now.TimeOfDay;
            // Treat times after midnight (before 4 AM) as "late in the evening" so the math wraps.
            double nowH = t.TotalHours < 4 ? t.TotalHours + 24 : t.TotalHours;
            double bedH = bed.TotalHours < 4 ? bed.TotalHours + 24 : bed.TotalHours;
            by = TimeSpan.FromHours(nowH - bedH);
            return nowH >= bedH;
        }

        private static void CheckApps(List<Finding> f, StatsCard stats, bool manual)
        {
            var windows = WindowControl.TopWindows();
            var names = windows.Select(w => WindowControl.AppName(w.Process)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            int count = names.Count;
            stats.Rows.Add(new StatRow("Open apps", count.ToString(), Math.Min(100, count * 6), count > 15));
            if (manual && count > 0) f.Add(new Finding($"You have {Composer.Plural(count, "app", "apps")} open: {Composer.List(names.Take(5).ToList())}{(count > 5 ? " and more" : "")}.", 0));
            if (count > 15) f.Add(new Finding($"You've got {count} apps open. Want me to close some?", 1, "manyapps"));
            foreach (var w in windows.GroupBy(w => w.Pid).Select(g => g.First()))
            {
                try
                {
                    using var p = Process.GetProcessById((int)w.Pid);
                    if (!p.Responding)
                    {
                        f.Add(new Finding($"{WindowControl.AppName(w.Process)} isn't responding. Say \"close {WindowControl.AppName(w.Process)}\" if it stays stuck.", 2, "hang:" + w.Process));
                        break;
                    }
                }
                catch { }
            }
        }

        private void CheckNotifications(List<Finding> f, List<PcNotification> notes)
        {
            var fresh = notes.Where(n => n.NeedsAttention && !State.SeenNotifications.Contains(n.Id)).OrderByDescending(n => n.When).ToList();
            foreach (var n in notes) if (!State.SeenNotifications.Contains(n.Id)) State.SeenNotifications.Add(n.Id);
            while (State.SeenNotifications.Count > 400) State.SeenNotifications.RemoveAt(0);
            if (fresh.Count == 0) return;
            bool serious = fresh.Any(n => System.Text.RegularExpressions.Regex.IsMatch((n.Title + " " + n.Body).ToLowerInvariant(), @"security|threat|virus|failed|urgent|low battery|out of space"));
            string lines = string.Join(" ", fresh.Take(3).Select(n => n.Line + "."));
            f.Add(new Finding(fresh.Count == 1 ? "You got a notification that might need you: " + lines
                                               : $"You got {fresh.Count} notifications that might need you. " + lines, serious ? 2 : 1, "notes"));
        }

        private void CheckDocuments(List<Finding> f, DateTime since, bool manual)
        {
            var docs = PcScan.RecentDocuments(DateTime.Now - since + TimeSpan.FromMinutes(2), 20);
            var fresh = docs.Where(d => !State.KnownDocuments.Contains(d.Path)).ToList();
            foreach (var d in fresh) State.KnownDocuments.Add(d.Path);
            while (State.KnownDocuments.Count > 400) State.KnownDocuments.RemoveAt(0);
            if (fresh.Count > 0)
                f.Add(new Finding($"You saved {Composer.Plural(fresh.Count, "document", "documents")} recently: {Composer.List(fresh.Take(3).Select(d => d.Name).ToList())}.", 0));
            // A document saved in Downloads or on the Desktop is easy to lose; say so once.
            var loose = fresh.FirstOrDefault(d => d.Path.Contains(@"\Downloads\", StringComparison.OrdinalIgnoreCase));
            if (loose != null) f.Add(new Finding($"{loose.Name} is saved in Downloads. Want it moved to Documents so it doesn't get lost?", manual ? 0 : 1, "loose"));
        }

        private static void CheckStorage(List<Finding> f, StatsCard stats)
        {
            foreach (var d in PcScan.Drives())
            {
                stats.Rows.Add(new StatRow("Drive " + d.Name.TrimEnd('\\'), $"{d.FreeGb:0} GB free", d.UsedPercent, d.Low));
                if (d.Low) f.Add(new Finding($"Drive {d.Name.TrimEnd('\\')} is almost full, only {d.FreeGb:0} GB left. Say \"free up space\" and I'll find big files.", d.FreeGb < 5 ? 2 : 1, "disk:" + d.Name));
            }
        }

        private void CheckBigFiles(List<Finding> f, CancellationToken ct)
        {
            var big = PcScan.LargeFiles(Math.Max(200, Config.BigFileMb) * 1024L * 1024, 6, ct);
            var fresh = big.Where(b => !State.SeenBigFiles.Contains(b.Path)).ToList();
            foreach (var b in fresh) State.SeenBigFiles.Add(b.Path);
            while (State.SeenBigFiles.Count > 300) State.SeenBigFiles.RemoveAt(0);
            if (fresh.Count == 0) return;
            var first = fresh[0];
            string where = first.Path.Contains(@"\Downloads\", StringComparison.OrdinalIgnoreCase) ? " in Downloads" : "";
            f.Add(new Finding(fresh.Count == 1 ? $"There's a big file{where}: {first.Name} ({first.SizeText})."
                                               : $"I found {fresh.Count} big files, like {first.Name} ({first.SizeText}){where}.", 1, "big"));
        }

        private static void CheckCalendar(List<Finding> f, bool manual)
        {
            var now = DateTime.Now;
            foreach (var e in CalendarStore.Between(now, now.AddMinutes(90)).Where(e => !e.AllDay))
            {
                int mins = (int)Math.Round((e.Start - now).TotalMinutes);
                f.Add(new Finding($"\"{e.Title}\" starts at {e.Start:h:mm tt}, in {Composer.Plural(Math.Max(1, mins), "minute", "minutes")}.", 1, "cal:" + e.Id));
            }
            if (manual)
            {
                int today = CalendarStore.OnDay(now).Count(e => e.End > now);
                f.Add(new Finding(today == 0 ? "Nothing else on your calendar today." : $"{Composer.Plural(today, "more event", "more events")} today.", 0));
            }
            var soon = ReminderStore.Pending().Where(r => r.Due < now.AddMinutes(30)).ToList();
            if (soon.Count > 0 && manual) f.Add(new Finding($"{Composer.Plural(soon.Count, "reminder", "reminders")} coming up soon.", 0));
        }

        private void CheckPower(List<Finding> f, StatsCard stats, bool manual)
        {
            var (has, pct, charging) = PcScan.Battery();
            if (has)
            {
                stats.Rows.Add(new StatRow("Battery", pct + "%" + (charging ? " charging" : ""), pct, pct < 20 && !charging));
                if (pct < 20 && !charging) f.Add(new Finding($"Battery's at {pct}%. Plug in soon!", pct < 10 ? 2 : 1, "battery"));
            }
            if (manual)
            {
                double idle = Win32.IdleSeconds();
                f.Add(new Finding(ScreenOn ? (idle > 60 ? $"Your screen's on, and you've been idle for {Math.Round(idle / 60)} minutes." : "Your screen's on and you're here.") : "Your screen's off.", 0));
            }
        }

        // ===================================================================== Greeting, welcome back, bedtime, reminders

        /// <summary>The first time you use the PC each day, Ace pops out and says hi with your day's plan.</summary>
        public async Task MaybeGreetAsync(string why)
        {
            try
            {
                await _loaded.Task.ConfigureAwait(false);
                var now = DateTime.Now;
                string today = ActivityTracker.Key(ActivityTracker.LogicalDay(now));
                if (State.LastGreetedDay == today) return;
                if (!ScreenOn || Win32.IdleSeconds() > 90 || (Config.QuietDuringFullScreen && Win32.FullScreenAppInFront())) return;
                State.LastGreetedDay = today;
                State.Save();

                var parts = new List<string> { Composer.Greeting(now) + ", " + Config.UserName + "!" };
                parts.Add(Composer.Pick("greet.day", new[] { "Happy {day}.", "It's {day}, {date}.", "Welcome to {day}!" }));
                var events = CalendarStore.OnDay(now).Where(e => e.End > now).ToList();
                if (events.Count > 0)
                    parts.Add(events.Count == 1 ? $"You have \"{events[0].Title}\" at {events[0].TimeText}." : $"You have {events.Count} things on your calendar, starting with \"{events[0].Title}\" at {events[0].TimeText}.");
                else parts.Add("Your calendar's clear today.");
                var reminders = ReminderStore.Pending().Where(r => r.Due.Date == now.Date && !r.IsTimer).ToList();
                if (reminders.Count > 0) parts.Add($"I'll remind you about {Composer.List(reminders.Take(2).Select(r => r.Text).ToList())}.");
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var w = await Web.WebClient.WeatherAsync(Config.HomeCity, cts.Token).ConfigureAwait(false);
                    if (w != null) parts.Add($"Outside it's {w.TempNow:0}°{(w.Fahrenheit ? "F" : "C")} and {w.Sky.ToLowerInvariant()}.");
                }
                catch { }
                if (State.AwayFindings.Count > 0) { parts.Add("Also, while you were gone: " + string.Join(" ", State.AwayFindings.Take(3))); State.AwayFindings.Clear(); State.Save(); }

                var a = AceAnswer.Say(string.Join(" ", parts), "greeting", 1);
                a.Mood = "love";
                var cal = new CalendarCard { Title = "Today" };
                cal.Events.AddRange(events.Take(4));
                if (cal.Events.Count > 0) a.Cards.Add(cal);

                Raise(new StageEvent(StageKind.Wake, "Good to see you!"));
                Play(Sfx.Greet);
                await Task.Delay(600).ConfigureAwait(false);
                Play(Sfx.EyesOut, 0.8);
                await Task.Delay(500).ConfigureAwait(false);
                Raise(new StageEvent(StageKind.Answer, "", a));
                SpeakIfWanted(a, spoken: false);
                Answered?.Invoke("", a);
            }
            catch (Exception ex) { Debug.WriteLine("Ace greet: " + ex.Message); }
        }

        private async Task WelcomeBackAsync()
        {
            try
            {
                if (State.AwayFindings.Count == 0) { await MaybeGreetAsync("back").ConfigureAwait(false); return; }
                await Task.Delay(1500).ConfigureAwait(false);
                if (Config.QuietDuringFullScreen && Win32.FullScreenAppInFront()) return;
                var items = State.AwayFindings.ToList();
                State.AwayFindings.Clear();
                State.Save();
                var a = AceAnswer.Say(Composer.Pick("back", new[] { "Welcome back, {user}!", "Hey, you're back!", "Oh hi again!" }) + " While you were away: " + string.Join(" ", items.Take(4)), "checks");
                a.Text = Composer.Fill(a.Text);
                Raise(new StageEvent(StageKind.Wake, "Welcome back!"));
                Play(Sfx.EyesOut);
                await Task.Delay(500).ConfigureAwait(false);
                Play(Sfx.Notify);
                Raise(new StageEvent(StageKind.Answer, "", a));
                SpeakIfWanted(a, spoken: false);
                Answered?.Invoke("", a);
            }
            catch (Exception ex) { Debug.WriteLine("Ace welcome back: " + ex.Message); }
        }

        private async Task MaybeBedtimeNudgeAsync(DateTime now)
        {
            try
            {
                var sleep = ActivityTracker.Summary(Config.SleepGoalHours);
                if (sleep == null) return;
                string today = ActivityTracker.Key(ActivityTracker.LogicalDay(now));
                if (State.LastBedtimeNudge == today) return;
                // From 15 minutes before the suggested bedtime until 2 hours after it.
                if (!PastBedtime(now.AddMinutes(15), sleep.SuggestBed, out var by) || by > TimeSpan.FromHours(2.25)) return;
                if (Win32.IdleSeconds() > 120 || !ScreenOn) return;
                if (Config.QuietDuringFullScreen && Win32.FullScreenAppInFront()) return;
                State.LastBedtimeNudge = today;
                State.Save();
                await _loaded.Task.ConfigureAwait(false);

                var wake = sleep.AvgWake;
                string text = Composer.Pick("bed", new[]
                {
                    "Hey {user}, it's {time}. You usually get up around {wake}, so heading to bed by {bedtime} gets you {hours} hours of sleep.",
                    "It's almost bedtime! Going to bed at {bedtime} gives you about {hours} hours before you usually wake up.",
                    "Bedtime check: on average you sleep {avg}. Try for bed by {bedtime} tonight.",
                }).Replace("{wake}", ActivityTracker.Clock(wake)).Replace("{bedtime}", ActivityTracker.Clock(sleep.SuggestBed))
                  .Replace("{hours}", Config.SleepGoalHours.ToString("0.#")).Replace("{avg}", ActivityTracker.Hours(sleep.AvgSleep));
                var a = AceAnswer.Say(text, "sleep");
                a.Mood = "sleepy";
                a.Cards.Add(new SleepCard
                {
                    AvgBed = ActivityTracker.Clock(sleep.AvgBed), AvgWake = ActivityTracker.Clock(sleep.AvgWake),
                    AvgSleep = ActivityTracker.Hours(sleep.AvgSleep), Suggest = ActivityTracker.Clock(sleep.SuggestBed), Nights = sleep.Nights,
                });
                Raise(new StageEvent(StageKind.Wake, "Bedtime soon"));
                Play(Sfx.Sleep, 0.7);
                await Task.Delay(400).ConfigureAwait(false);
                Raise(new StageEvent(StageKind.Answer, "", a));
                SpeakIfWanted(a, spoken: false);
                Answered?.Invoke("", a);
            }
            catch (Exception ex) { Debug.WriteLine("Ace bedtime: " + ex.Message); }
        }

        private void FireReminders(DateTime now)
        {
            foreach (var r in ReminderStore.TakeDue(now))
            {
                string what = r.Text.Trim();
                var a = AceAnswer.Say(r.IsTimer
                    ? Composer.Pick("timer.done", new[] { "Time's up!", "Ding! Your timer's done.", "Timer done, {user}!" }) + (what.Length > 0 ? $" ({what})" : "")
                    : "Reminder: " + what + ".", "reminder");
                a.Text = Composer.Fill(a.Text);
                a.Mood = "alert";
                // Other timers still running stay on screen with their countdowns.
                if (r.IsTimer && ReminderStore.Pending().Any(x => x.IsTimer)) a.Cards.Add(new TimersCard { Title = "Still running" });
                ShowNow(a, Sfx.Alert, speak: true);
                Toast.Show(r.IsTimer ? "Timer done" + (what.Length > 0 ? ": " + what : "") : "Reminder", r.IsTimer ? "Time's up!" + (what.Length > 0 ? " " + Composer.Capital(what) + " is done." : "") : Composer.Capital(what));
            }
            foreach (var e in CalendarStore.Between(now.AddMinutes(-5), now.AddHours(25)))
            {
                if (e.Reminded || e.AllDay || e.RemindMinutes < 0) continue;
                if (e.Start - TimeSpan.FromMinutes(e.RemindMinutes) > now) continue;
                CalendarStore.MarkReminded(e.Id);
                int mins = (int)Math.Round((e.Start - now).TotalMinutes);
                var a = AceAnswer.Say(mins <= 0 ? $"\"{e.Title}\" is starting now!" : $"\"{e.Title}\" starts in {Composer.Plural(mins, "minute", "minutes")}, at {e.Start:h:mm tt}.", "calendar");
                a.Mood = "alert";
                a.Cards.Add(new CalendarCard { Title = "Coming up", Events = { e } });
                ShowNow(a, Sfx.Notify, speak: true);
            }
        }

        // ===================================================================== Focus timer, health log, app habits

        private void FocusTick(DateTime now)
        {
            string? say = FocusTimer.Advance(now);
            if (say == null) return;
            var a = AceV4Brain.FocusAnswer(say);
            a.Mood = FocusTimer.OnBreak ? "love" : "happy";
            ShowNow(a, FocusTimer.OnBreak ? Sfx.Notify : Sfx.Wake, speak: true);
            Toast.Show(FocusTimer.OnBreak ? "Break time!" : "Back to focus", say);
            // Things saved up while you focused come out on the break.
            if (FocusTimer.OnBreak && State.AwayFindings.Count > 0) _ = Task.Run(HeldDuringFocusAsync);
        }

        private async Task HeldDuringFocusAsync()
        {
            try
            {
                await Task.Delay(9000).ConfigureAwait(false);
                if (!FocusTimer.OnBreak || State.AwayFindings.Count == 0) return;
                var items = State.AwayFindings.ToList();
                State.AwayFindings.Clear();
                State.Save();
                var a = AceAnswer.Say("While you were focusing: " + string.Join(" ", items.Take(4)), "checks");
                a.Text = Composer.Fill(a.Text);
                ShowNow(a, Sfx.Notify, speak: false);
            }
            catch (Exception ex) { Debug.WriteLine("Ace focus hold: " + ex.Message); }
        }

        /// <summary>A quick look at the PC's health for the weekly report (only while you're using it).</summary>
        private void SampleHealth(DateTime now, double idle)
        {
            try
            {
                if (!ScreenOn || idle > 600) return;
                var s = Hub.Stats;
                if (s.Cpu <= 0 && s.MemPercent <= 0) return;   // stats aren't running yet
                var drives = PcScan.Drives().Where(d => d.TotalGb > 0).ToList();
                var tight = drives.OrderBy(d => d.FreeGb / d.TotalGb).FirstOrDefault();
                HealthLog.Add(new HealthSample
                {
                    Time = now,
                    Cpu = Math.Round(s.Cpu, 1),
                    Mem = Math.Round(s.MemPercent, 1),
                    CpuTemp = s.CpuTemp,
                    GpuTemp = s.GpuTemp,
                    FreeGb = tight == null ? 0 : Math.Round(tight.FreeGb, 1),
                    FreePct = tight == null ? 100 : Math.Round(tight.FreeGb / tight.TotalGb * 100, 1),
                    UptimeHours = Math.Round(Environment.TickCount64 / 3_600_000.0, 2),
                    Apps = WindowControl.TopWindows().Select(w => w.Process).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                });
            }
            catch (Exception ex) { Debug.WriteLine("Health sample: " + ex.Message); }
        }

        /// <summary>Sundays from 10 AM: the week's PC report card, once.</summary>
        private async Task MaybeWeeklyReportAsync(DateTime now)
        {
            try
            {
                if (!Config.WeeklyReport || now.DayOfWeek != DayOfWeek.Sunday || now.Hour < 10 || now.Hour >= 21) return;
                string today = now.ToString("yyyy-MM-dd");
                if (State.LastWeeklyReport == today || ShouldStayQuiet(out _)) return;
                await _loaded.Task.ConfigureAwait(false);
                var report = HealthLog.Report(now);
                if (report == null) return;
                State.LastWeeklyReport = today;
                State.Save();
                var a = AceV4Brain.HealthReportAnswer(report, weekly: true);
                ShowNow(a, a.Mood == "alert" ? Sfx.Alert : Sfx.Notify, speak: true);
                Toast.Show("Weekly PC report: " + report.Grade, a.Text.Length > 140 ? a.Text[..140] + "…" : a.Text);
            }
            catch (Exception ex) { Debug.WriteLine("Weekly report: " + ex.Message); }
        }

        /// <summary>Learns which apps you open when, and offers one when it's about that time.</summary>
        private void WatchApps(DateTime now, double idle)
        {
            try
            {
                if (!ScreenOn) return;
                var windows = WindowControl.TopWindows();
                var names = windows.Select(w => WindowControl.AppName(w.Process)).Where(n => n.Length > 0 && n != "a Windows app").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                AppHabits.Record(now, names, app =>
                {
                    var w = windows.FirstOrDefault(x => string.Equals(WindowControl.AppName(x.Process), app, StringComparison.OrdinalIgnoreCase));
                    if (w == null) return null;
                    using var p = Process.GetProcessById(w.Pid);
                    return p.MainModule?.FileName;
                });
                if (!Config.AppSuggestions || idle > 120 || now.Minute > 50 || ShouldStayQuiet(out _) || !_loaded.Task.IsCompleted) return;
                var habit = AppHabits.Suggest(now, names);
                if (habit == null) return;
                AppHabits.MarkOffered(habit.App, now);
                var a = AceV4Brain.HabitOffer(habit);
                Brain.Offer(a);
                ShowNow(a, Sfx.Notify, speak: false);
            }
            catch (Exception ex) { Debug.WriteLine("App habits: " + ex.Message); }
        }

        /// <summary>Gentle screen-time heads-ups (15 minutes left, at the limit, 30 minutes over). Nothing is ever closed.</summary>
        private void ScreenTimeHeadsUp(DateTime now)
        {
            try
            {
                foreach (var say in ScreenTime.Warnings(now))
                {
                    var a = AceAnswer.Say(say, "screentime");
                    a.Mood = "sleepy";
                    a.Cards.Add(new ScreenTimeCard());
                    ShowNow(a, Sfx.Notify, speak: true);
                }
            }
            catch (Exception ex) { Debug.WriteLine("Screen time: " + ex.Message); }
        }

        private void ShowNow(AceAnswer a, Sfx sound, bool speak)
        {
            Raise(new StageEvent(StageKind.Wake, ""));
            Play(sound);
            Raise(new StageEvent(StageKind.Answer, "", a));
            if (speak) SpeakIfWanted(a, spoken: false);
            Answered?.Invoke("", a);
        }

        public void Dispose()
        {
            try
            {
                ActivityTracker.Save();
                ScreenTime.Save();
                State.Save();
                _tick?.Dispose();
                _checkTimer?.Dispose();
                SystemEvents.SessionSwitch -= OnSessionSwitch;
                SystemEvents.PowerModeChanged -= OnPowerMode;
                if (_hwnd != null)
                {
                    Win32.UnregisterHotKey(_hwnd.Handle, HotkeyId);
                    if (_powerNotify != IntPtr.Zero) Win32.UnregisterPowerSettingNotification(_powerNotify);
                    _hwnd.RemoveHook(WndProc);
                }
                WakeWord.Dispose();
                Dictation.Dispose();
                Speech.Dispose();
                Sounds.Dispose();
            }
            catch { }
        }
    }
}
