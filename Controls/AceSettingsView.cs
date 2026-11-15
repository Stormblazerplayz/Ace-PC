using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nimbus.AceV4;
using Nimbus.AceV4.Audio;
using Nimbus.Motion;

namespace Nimbus.Controls
{
    /// <summary>Ace V4's settings, built in code. Shown on the Ace page and in Settings. Every change saves right away.</summary>
    public static class AceSettingsView
    {
        private static int _group;

        public static StackPanel Build()
        {
            var rt = AceRuntime.Current;
            var c = rt.Config;
            var stack = new StackPanel();

            void Save() => rt.SaveConfig();

            // ---------------- Brain (same choices as the iPad app)
            stack.Children.Add(Section("Brain"));
            stack.Children.Add(Labeled("Answer with", Chips(new[] { ("Normal Ace", "ace"), ("LFM3 Omni", "model") }, c.Brain == "model" ? "model" : "ace", v => { c.Brain = v; Save(); })));
            var brainNote = AceCardViews.Text("Normal Ace runs by itself, no internet or model needed. LFM3 Omni answers with the AI model when it's running on this PC, and Ace answers if it isn't.", 11.5, "MutedBrush");
            brainNote.Margin = new Thickness(0, 0, 0, 4);
            stack.Children.Add(brainNote);
            stack.Children.Add(Toggle("When Normal Ace doesn't know, ask LFM3 Omni (if it's running)", c.UseLlmHelper, v => { c.UseLlmHelper = v; Save(); }));

            // ---------------- Sound
            stack.Children.Add(Section("Sound"));
            stack.Children.Add(Toggle("Sound effects", c.Sounds, v => { c.Sounds = v; Save(); if (v) rt.Play(Sfx.Success); }));
            var volume = new Slider { Minimum = 0, Maximum = 100, Value = c.SoundVolume * 100, Margin = new Thickness(0, 2, 0, 6) };
            volume.SetResourceReference(FrameworkElement.StyleProperty, "NimbusSlider");
            DateTime lastBlip = DateTime.MinValue;
            volume.ValueChanged += (_, e) =>
            {
                c.SoundVolume = e.NewValue / 100;
                rt.ApplySoundSettings();
                if (DateTime.UtcNow - lastBlip > TimeSpan.FromMilliseconds(180)) { lastBlip = DateTime.UtcNow; rt.Play(Sfx.Tick); }
            };
            volume.LostMouseCapture += (_, _) => Save();
            stack.Children.Add(Labeled("Volume", volume));
            stack.Children.Add(Toggle("3D sound (follows Ace's eyes)", c.SpatialSound, v => { c.SpatialSound = v; Save(); }));
            var soundButtons = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            var test = Button("▶ Play the sounds");
            test.Click += async (_, _) =>
            {
                foreach (var s in new[] { Sfx.Wake, Sfx.EyesOut, Sfx.Look, Sfx.Blink, Sfx.Success, Sfx.Notify, Sfx.Greet })
                {
                    rt.Sounds.Play(s, s == Sfx.Look ? (Environment.TickCount % 2 == 0 ? -0.8 : 0.8) : 0);
                    await System.Threading.Tasks.Task.Delay(s == Sfx.Wake ? 700 : 520);
                }
            };
            var export = Button("Save sounds as WAV");
            export.Click += (_, _) =>
            {
                try
                {
                    string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Ace Sounds");
                    int n = SoundEngine.ExportAll(folder);
                    export.Content = $"Saved {n} sounds ✓";
                    Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })?.Dispose();
                }
                catch (Exception ex) { export.Content = "Couldn't save: " + ex.Message; }
            };
            soundButtons.Children.Add(test);
            soundButtons.Children.Add(export);
            stack.Children.Add(soundButtons);

            // ---------------- Waking up
            stack.Children.Add(Section("Waking up"));
            stack.Children.Add(Toggle("Wake up on its own to check things", c.WakeChecks, v => { c.WakeChecks = v; Save(); }));
            stack.Children.Add(Labeled("Check every", Chips(new[] { ("5–10 min", "5-10"), ("15–30 min", "15-30"), ("30–60 min", "30-60"), ("1–2 hours", "60-120") },
                $"{c.WakeMinMinutes}-{c.WakeMaxMinutes}", v =>
                {
                    var parts = v.Split('-');
                    c.WakeMinMinutes = int.Parse(parts[0]);
                    c.WakeMaxMinutes = int.Parse(parts[1]);
                    Save();
                })));
            stack.Children.Add(Toggle("Stay quiet during full-screen games and videos", c.QuietDuringFullScreen, v => { c.QuietDuringFullScreen = v; Save(); }));
            stack.Children.Add(Toggle("Start Ace with Windows (greets you each day)", c.StartWithWindows, v => { c.StartWithWindows = v; Save(); }));

            // ---------------- Screen time
            stack.Children.Add(Section("Screen time"));
            stack.Children.Add(Labeled("Daily limit", Chips(new[] { ("Off", "0"), ("1 hour", "60"), ("2 hours", "120"), ("3 hours", "180"), ("4 hours", "240"), ("6 hours", "360") },
                Nimbus.AceV4.Pc.ScreenTime.LimitMinutes.ToString(), v => Nimbus.AceV4.Pc.ScreenTime.SetLimit(int.Parse(v)))));
            var stNote = new TextBlock { Text = "Ace gives a gentle heads-up 15 minutes before and when it's up. It never closes anything. Say \"limit Minecraft to 1 hour\" for a single app.", FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
            stNote.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            stack.Children.Add(stNote);

            // ---------------- Voice
            stack.Children.Add(Section("Voice"));
            stack.Children.Add(Toggle("Listen for \"Hey Ace\"", c.WakeWord, v => { c.WakeWord = v; Save(); }));
            stack.Children.Add(Toggle("Say answers out loud", c.SpeakReplies, v => { c.SpeakReplies = v; Save(); }));
            stack.Children.Add(Labeled("Dictation", Chips(new[] { ("Auto", "auto"), ("LFM3 Omni", "omni"), ("Windows", "windows") }, c.DictationEngine, v => { c.DictationEngine = v; Save(); })));
            var hint = AceCardViews.Text("Shortcut: Ctrl+Alt+Space talks to Ace from anywhere.", 11.5, "MutedBrush");
            hint.Margin = new Thickness(0, 2, 0, 0);
            stack.Children.Add(hint);

            // ---------------- You
            stack.Children.Add(Section("You"));
            var name = new TextBox { Text = c.UserName, Margin = new Thickness(0, 2, 0, 6), Tag = "Your name" };
            name.SetResourceReference(FrameworkElement.StyleProperty, "PillTextBox");
            name.LostKeyboardFocus += (_, _) =>
            {
                string n = name.Text.Trim();
                if (n.Length > 0 && n != c.UserName) { c.UserName = n; Save(); AceV4.Brain.Composer.UserName = n; }
            };
            stack.Children.Add(Labeled("Name", name));
            stack.Children.Add(Labeled("Sleep goal", Chips(new[] { ("8 h", "8"), ("8.5 h", "8.5"), ("9 h", "9"), ("9.5 h", "9.5"), ("10 h", "10") },
                c.SleepGoalHours.ToString(System.Globalization.CultureInfo.InvariantCulture),
                v => { c.SleepGoalHours = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture); Save(); })));

            // ---------------- Extras
            stack.Children.Add(Section("Extras"));
            stack.Children.Add(Labeled("Focus round", Chips(new[] { ("15 min", "15"), ("25 min", "25"), ("30 min", "30"), ("45 min", "45"), ("50 min", "50") },
                c.FocusMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                v => { c.FocusMinutes = int.Parse(v, System.Globalization.CultureInfo.InvariantCulture); Save(); })));
            stack.Children.Add(Toggle("Eyes follow my mouse when Ace isn't busy", c.EyesFollowMouse, v => { c.EyesFollowMouse = v; Save(); }));
            stack.Children.Add(Toggle("Learn which apps I open when, and offer to open them", c.AppSuggestions, v => { c.AppSuggestions = v; Save(); }));
            stack.Children.Add(Toggle("Show a PC health report card every Sunday", c.WeeklyReport, v => { c.WeeklyReport = v; Save(); }));

            // ---------------- Updates
            stack.Children.Add(Section("Updates"));
            stack.Children.Add(Toggle("Update Ace by itself from GitHub", c.AutoUpdate, v => { c.AutoUpdate = v; Save(); }));
            stack.Children.Add(Toggle("Get beta updates (new stuff first, might be a bit buggy)", c.BetaChannel, v => { c.BetaChannel = v; Save(); }));
            var upText = AceCardViews.Text("", 11.5, "MutedBrush");
            upText.Margin = new Thickness(0, 2, 0, 6);
            var checkNow = Button("Check now");
            checkNow.HorizontalAlignment = HorizontalAlignment.Left;
            void ShowUpdate()
            {
                var lines = new System.Collections.Generic.List<string> { $"Version {Nimbus.AceV4.Pc.AutoUpdater.CurrentText} · from github.com/{Nimbus.AceV4.Pc.AutoUpdater.Repo}" };
                string from = Nimbus.AceV4.Pc.AutoUpdater.JustUpdatedFrom;
                if (from.Length > 0) lines.Add($"Just updated from {from}.");
                if (Nimbus.AceV4.Pc.AutoUpdater.Status.Length > 0) lines.Add(Nimbus.AceV4.Pc.AutoUpdater.Status);
                else if (Nimbus.AceV4.Pc.AutoUpdater.LastCheck != default) lines.Add("Last checked " + Nimbus.AceV4.Pc.AutoUpdater.LastCheck.ToString("MMM d, h:mm tt") + ".");
                if (Nimbus.AceV4.Pc.AutoUpdater.RunningFromSource)
                    lines.Add("You're running from the source code (dotnet run), so it only tells you about new versions. The copy from the release zip installs them by itself.");
                else
                    lines.Add("It checks when Ace starts and every 6 hours, and installs while you're away. It keeps a backup and goes back to it if the new version won't start.");
                upText.Text = string.Join("\n", lines);
                checkNow.IsEnabled = !Nimbus.AceV4.Pc.AutoUpdater.Busy;
            }
            checkNow.Click += async (_, _) =>
            {
                checkNow.IsEnabled = false;
                try { await rt.CheckForUpdatesAsync(manual: true, System.Threading.CancellationToken.None); }
                finally { ShowUpdate(); }
            };
            Action upChanged = () => Application.Current?.Dispatcher.BeginInvoke(ShowUpdate);
            upText.Loaded += (_, _) => { Nimbus.AceV4.Pc.AutoUpdater.Changed -= upChanged; Nimbus.AceV4.Pc.AutoUpdater.Changed += upChanged; ShowUpdate(); };
            upText.Unloaded += (_, _) => Nimbus.AceV4.Pc.AutoUpdater.Changed -= upChanged;
            stack.Children.Add(upText);
            stack.Children.Add(checkNow);

            // ---------------- Status
            stack.Children.Add(Section("How Ace is doing"));
            var status = AceCardViews.Text("Loading…", 11.5, "MutedBrush");
            stack.Children.Add(status);
            void Refresh()
            {
                var b = rt.Brain;
                var lines = new System.Collections.Generic.List<string>();
                if (b == null || !b.Ready) lines.Add("Ace's brain is still loading…");
                else
                {
                    lines.Add($"Q&A bank: {b.Bank.Count:N0} topics, {b.Bank.QuestionCount:N0} ways to ask, {b.Bank.LearnedCount:N0} learned by itself.");
                    lines.Add(b.Embedder.IsReady ? "Meaning net (MiniLM): on." : "Meaning net (MiniLM): off. Run Setup-AceV4.ps1 to turn it on.");
                }
                if (rt.WakeWord.Problem.Length > 0) lines.Add(rt.WakeWord.Problem);
                if (rt.Sounds.Problem.Length > 0) lines.Add("Sound: " + rt.Sounds.Problem);
                if (rt.Speech.Problem.Length > 0) lines.Add(rt.Speech.Problem);
                status.Text = string.Join("\n", lines);
            }
            stack.Loaded += (_, _) => Refresh();
            _ = rt.WhenLoaded.ContinueWith(_ => Application.Current?.Dispatcher.BeginInvoke(Refresh));
            return stack;
        }

        // ===================================================================== Bits

        internal static TextBlock Section(string title)
        {
            var t = AceCardViews.Text(title.ToUpperInvariant(), 10.5, "AceBrush", bold: true, wrap: false);
            t.Margin = new Thickness(0, 14, 0, 6);
            return t;
        }

        internal static CheckBox Toggle(string label, bool value, Action<bool> changed)
        {
            var cb = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 3, 0, 3) };
            cb.SetResourceReference(FrameworkElement.StyleProperty, "ToggleSwitch");
            cb.Checked += (_, _) => changed(true);
            cb.Unchecked += (_, _) => changed(false);
            return cb;
        }

        internal static Button Button(string label)
        {
            var b = new Button { Content = label, Margin = new Thickness(0, 0, 6, 6) };
            b.SetResourceReference(FrameworkElement.StyleProperty, "SmallPillButton");
            return b;
        }

        internal static FrameworkElement Labeled(string label, FrameworkElement control)
        {
            var dock = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var t = AceCardViews.Text(label, 12, "MutedBrush", wrap: false);
            t.Width = 86;
            t.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(t, Dock.Left);
            dock.Children.Add(t);
            dock.Children.Add(control);
            return dock;
        }

        internal static WrapPanel Chips((string Label, string Value)[] options, string current, Action<string> picked)
        {
            string group = "aceset" + (++_group);
            var wrap = new WrapPanel();
            foreach (var (label, value) in options)
            {
                var rb = new RadioButton { Content = label, GroupName = group, IsChecked = value == current, Margin = new Thickness(0, 0, 4, 4) };
                rb.SetResourceReference(FrameworkElement.StyleProperty, "FilterChip");
                string v = value;
                rb.Checked += (_, _) => { picked(v); Spring.Bump(wrap); };
                wrap.Children.Add(rb);
            }
            return wrap;
        }
    }
}
