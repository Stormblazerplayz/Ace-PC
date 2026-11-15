using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Nimbus.AceV4;
using Nimbus.AceV4.Audio;
using Nimbus.AceV4.Pc;
using Nimbus.Controls;
using Nimbus.Motion;
using Nimbus.Services.Calendar;

namespace Nimbus.Pages
{
    /// <summary>
    /// The Ace V4 tab: Ace's eyes, the conversation (with recipes, results and other cards), Ace's live thinking,
    /// voice dictation, and a side panel with the next wake-up check, today's calendar, sleep and settings.
    /// </summary>
    public sealed class AcePage : UserControl, INimbusPage
    {
        private readonly Grid _root = new();
        private readonly AceEyes _eyes = new() { Width = 96, Height = 38 };
        private readonly TextBlock _title, _subtitle;
        private readonly StackPanel _convo = new() { Margin = new Thickness(14, 10, 14, 10) };
        private readonly ScrollViewer _convoScroll;
        private readonly TextBox _input = new();
        private readonly Button _mic, _send;
        private readonly Border _micLevel = new() { Height = 3, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        private readonly StackPanel _thinking = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 4) };
        private readonly TextBlock _thinkingLine;
        private readonly StackPanel _thinkingSteps = new();
        private readonly TextBlock _nextCheck;
        private readonly StackPanel _today = new();
        private readonly StackPanel _sleep = new();
        private readonly StackPanel _memory = new();
        private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
        private string _lastUserLine = "";
        private bool _busy;

        public AcePage()
        {
            var rt = AceRuntime.Current;

            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(262) });

            // ================= Left: header, conversation, input
            var left = Card(new Thickness(0));
            left.Margin = new Thickness(0, 4, 10, 0);
            var leftDock = new DockPanel();
            left.Child = leftDock;

            // Header
            _title = AceCardViews.Text("Ace V4", 18, "FgBrush", bold: true, wrap: false, font: "DisplayFont");
            _subtitle = AceCardViews.Text("Waking up…", 11.5, "MutedBrush", wrap: false);
            var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            titleRow.Children.Add(_title);
            var flag = new Border { Margin = new Thickness(8, 0, 0, 0), Child = AceCardViews.Text("RULES + NEURAL", 9.5, "InkBrush", bold: true, wrap: false) };
            flag.SetResourceReference(StyleProperty, "Flag");
            flag.SetResourceReference(Border.BackgroundProperty, "AceBrush");
            titleRow.Children.Add(flag);
            titleStack.Children.Add(titleRow);
            titleStack.Children.Add(_subtitle);
            var header = new DockPanel { Margin = new Thickness(14, 12, 14, 6) };
            var eyesBox = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(6, 3, 6, 3), Background = new SolidColorBrush(Color.FromRgb(0x05, 0x05, 0x09)), Child = _eyes };
            DockPanel.SetDock(eyesBox, Dock.Left);
            header.Children.Add(eyesBox);
            var clearBtn = SmallButton("Clear");
            clearBtn.Click += (_, _) => { _convo.Children.Clear(); AddHello(); };
            var oldChat = SmallButton("Omni chat");
            oldChat.ToolTip = "The old LFM3 Omni chat";
            oldChat.Click += (_, _) => Services.Hub.Navigate("acechat");
            var headerButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            headerButtons.Children.Add(oldChat);
            headerButtons.Children.Add(clearBtn);
            DockPanel.SetDock(headerButtons, Dock.Right);
            header.Children.Add(headerButtons);
            header.Children.Add(titleStack);
            DockPanel.SetDock(header, Dock.Top);
            leftDock.Children.Add(header);

            // Input bar
            _input.SetResourceReference(StyleProperty, "PillTextBox");
            _input.Background = Brushes.Transparent;
            _input.BorderThickness = new Thickness(0);
            _input.Tag = "Ask Ace anything, or tell it to do something";
            _input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; _ = SendAsync(); } };
            _send = IconButton("➤", "Send (Enter)");
            _send.SetResourceReference(BackgroundProperty, "AccentBrush");
            _send.Foreground = new SolidColorBrush(Color.FromRgb(0x12, 0x0C, 0x2E));
            _send.Click += (_, _) => _ = SendAsync();
            _mic = IconButton("🎤", "Talk to Ace: tap, talk, it stops when you stop (Ctrl+Alt+Space)");
            _mic.Click += (_, _) => _ = rt.ListenAndAskAsync("page", showOnStage: false);
            var inputDock = new DockPanel();
            DockPanel.SetDock(_send, Dock.Right);
            DockPanel.SetDock(_mic, Dock.Right);
            inputDock.Children.Add(_send);
            inputDock.Children.Add(_mic);
            inputDock.Children.Add(_input);
            var inputBox = new Border { Margin = new Thickness(12, 4, 12, 12), Padding = new Thickness(6), CornerRadius = new CornerRadius(99), BorderThickness = new Thickness(1.5), Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)), Child = inputDock };
            _micLevel.SetResourceReference(Border.BackgroundProperty, "AceBrush");
            _micLevel.Margin = new Thickness(26, 0, 26, 0);
            var bottom = new StackPanel();
            bottom.Children.Add(Suggestions());
            bottom.Children.Add(_micLevel);
            bottom.Children.Add(inputBox);
            DockPanel.SetDock(bottom, Dock.Bottom);
            leftDock.Children.Add(bottom);

            // Live thinking
            _thinkingLine = AceCardViews.Text("Thinking…", 12, "AceBrush", bold: true, wrap: false);
            var dots = new WaveBars { Bars = 4, BarHeight = 12, BarBrush = new SolidColorBrush(Color.FromRgb(0x5B, 0xB6, 0xFF)), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var thinkRow = new StackPanel { Orientation = Orientation.Horizontal };
            thinkRow.Children.Add(dots);
            thinkRow.Children.Add(_thinkingLine);
            _thinking.Children.Add(thinkRow);
            _thinkingSteps.Margin = new Thickness(18, 4, 0, 0);
            _thinking.Children.Add(_thinkingSteps);

            // Conversation
            var convoAndThinking = new StackPanel();
            convoAndThinking.Children.Add(_convo);
            var thinkWrap = new Border { Margin = new Thickness(14, 0, 14, 10), Child = _thinking };
            convoAndThinking.Children.Add(thinkWrap);
            _convoScroll = new ScrollViewer { Content = convoAndThinking, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            leftDock.Children.Add(_convoScroll);
            _root.Children.Add(left);

            // ================= Right: next check, today, sleep, settings
            var side = new StackPanel { Margin = new Thickness(0, 4, 4, 0) };
            var sideScroll = new ScrollViewer { Content = side, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
            Grid.SetColumn(sideScroll, 1);
            _root.Children.Add(sideScroll);

            // Next check
            var checkStack = new StackPanel();
            checkStack.Children.Add(Header("Next wake-up check"));
            _nextCheck = AceCardViews.Text("–", 22, "FgBrush", bold: true, wrap: false, font: "DisplayFont");
            checkStack.Children.Add(_nextCheck);
            checkStack.Children.Add(AceCardViews.Text("Ace checks your PC, apps, notifications, files and storage every 15–30 min.", 11.5, "MutedBrush"));
            var checkNow = HotButton("Check now");
            checkNow.Margin = new Thickness(0, 8, 0, 0);
            checkNow.HorizontalAlignment = HorizontalAlignment.Left;
            checkNow.Click += async (_, _) =>
            {
                checkNow.IsEnabled = false;
                ShowUser("check my pc");
                ShowThinking("Checking everything…");
                try { await rt.AskAsync("check my pc"); }
                finally { checkNow.IsEnabled = true; }
            };
            checkStack.Children.Add(checkNow);
            side.Children.Add(Card(checkStack));

            // Today
            var todayStack = new StackPanel();
            var todayHead = new DockPanel();
            var openCal = SmallButton("Calendar");
            openCal.Margin = new Thickness(0);
            openCal.Click += (_, _) => Services.Hub.Navigate("calendar");
            DockPanel.SetDock(openCal, Dock.Right);
            todayHead.Children.Add(openCal);
            todayHead.Children.Add(Header("Today · " + DateTime.Now.ToString("ddd MMM d")));
            todayStack.Children.Add(todayHead);
            todayStack.Children.Add(_today);
            side.Children.Add(Card(todayStack));

            // Sleep
            var sleepStack = new StackPanel();
            sleepStack.Children.Add(Header("Sleep"));
            sleepStack.Children.Add(_sleep);
            side.Children.Add(Card(sleepStack));

            // What Ace remembers about you
            var memoryStack = new StackPanel();
            var memoryHead = new DockPanel();
            var seeAll = SmallButton("See all");
            seeAll.Margin = new Thickness(0);
            seeAll.Click += (_, _) =>
            {
                var b = AceRuntime.Current.Brain;
                if (b == null) return;
                var a = AceAnswer.Say("Here's everything you've told me. Tap ✕ to make me forget something.", "memory");
                a.Cards.Add(b.NewMemoryCard());
                AddAce(a);
            };
            DockPanel.SetDock(seeAll, Dock.Right);
            memoryHead.Children.Add(seeAll);
            memoryHead.Children.Add(Header("What I remember"));
            memoryStack.Children.Add(memoryHead);
            memoryStack.Children.Add(_memory);
            side.Children.Add(Card(memoryStack));

            // Settings
            var settings = AceSettingsView.Build();
            settings.Visibility = Visibility.Collapsed;
            var settingsToggle = new Button { Content = "Ace settings  ›", HorizontalContentAlignment = HorizontalAlignment.Left };
            settingsToggle.SetResourceReference(StyleProperty, "FlatRowButton");
            settingsToggle.FontWeight = FontWeights.ExtraBold;
            settingsToggle.Padding = new Thickness(4, 2, 4, 2);
            settingsToggle.Click += (_, _) =>
            {
                bool open = settings.Visibility != Visibility.Visible;
                settings.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                settingsToggle.Content = open ? "Ace settings  ⌄" : "Ace settings  ›";
                if (open) Spring.PopIn(settings);
            };
            var settingsStack = new StackPanel();
            settingsStack.Children.Add(settingsToggle);
            settingsStack.Children.Add(settings);
            side.Children.Add(Card(settingsStack));

            Content = _root;

            // ================= Wiring
            rt.Answered += (q, a) => Dispatcher.BeginInvoke(() => OnAnswered(q, a));
            rt.Stage += e => Dispatcher.BeginInvoke(() => OnStage(e));
            rt.Changed += () => Dispatcher.BeginInvoke(RefreshSide);
            if (rt.Brain != null) rt.Brain.Thought += step => Dispatcher.BeginInvoke(() => AddThinkingStep(step));
            rt.Speech.Word += _ => Dispatcher.BeginInvoke(() => { if (IsVisible) _eyes.TalkPulse(); });
            AceCardViews.CardSaid += said => Dispatcher.BeginInvoke(() => { if (IsVisible) AddAceLine(said); });
            CalendarStore.Changed += () => Dispatcher.BeginInvoke(RefreshToday);
            _eyes.Looked += x => { if (IsVisible) { rt.EyePan = x; rt.Play(Sfx.Look, 0.25); } };

            _clock.Tick += (_, _) => UpdateClock();
            IsVisibleChanged += (_, _) =>
            {
                rt.PageVisible = IsVisible;
                if (IsVisible) { _clock.Start(); RefreshSide(); } else _clock.Stop();
            };
            AddHello();
            _ = rt.WhenLoaded.ContinueWith(_ => Dispatcher.BeginInvoke(RefreshSide));
        }

        public Panel PopRoot => _root;

        public void OnShown()
        {
            AceRuntime.Current.PageVisible = true;
            RefreshSide();
            _eyes.Mood = AceEyes.Moods.Happy;
            _eyes.Hop();
            var calm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
            calm.Tick += (_, _) => { calm.Stop(); if (!_busy) _eyes.Mood = AceEyes.Moods.Normal; };
            calm.Start();
            _input.Focus();
        }

        // ===================================================================== Asking

        private async Task SendAsync()
        {
            string text = _input.Text.Trim();
            if (text.Length == 0 || _busy) return;
            _input.Clear();
            ShowUser(text);
            ShowThinking("Thinking…");
            try { await AceRuntime.Current.AskAsync(text); }
            catch (Exception ex) { HideThinking(); AddAceLine("Something went wrong: " + ex.Message); }
        }

        private void ShowThinking(string line)
        {
            _busy = true;
            _send.IsEnabled = false;
            _thinkingSteps.Children.Clear();
            _thinkingLine.Text = line;
            _thinking.Visibility = Visibility.Visible;
            _eyes.Mood = AceEyes.Moods.Think;
            ScrollToEnd();
        }

        private void HideThinking()
        {
            _busy = false;
            _send.IsEnabled = true;
            _thinking.Visibility = Visibility.Collapsed;
            _thinkingSteps.Children.Clear();
        }

        private void AddThinkingStep(ThoughtStep step)
        {
            if (!_busy || !IsVisible) return;
            var t = AceCardViews.Text("· " + step.Label + (step.Detail.Length > 0 ? "  —  " + step.Detail : ""), 11.5, "MutedBrush", wrap: false);
            _thinkingSteps.Children.Add(t);
            while (_thinkingSteps.Children.Count > 6) _thinkingSteps.Children.RemoveAt(0);
            Spring.PopIn(t);
            ScrollToEnd();
        }

        private void OnStage(StageEvent e)
        {
            if (!IsVisible) return;
            switch (e.Kind)
            {
                case StageKind.Status when _busy && e.Text.Length > 0:
                    _thinkingLine.Text = e.Text;
                    break;
                case StageKind.Status when AceRuntime.Current.Listening:
                    _subtitle.Text = e.Text;
                    break;
                case StageKind.Level:
                    _eyes.Mood = AceEyes.Moods.Listen;
                    _eyes.Level = e.Value;
                    double w = Math.Max(0, (_root.ActualWidth - 262 - 60) * e.Value);
                    _micLevel.Width = w;
                    break;
                case StageKind.Heard:
                    _micLevel.Width = 0;
                    ShowUser(e.Text);
                    ShowThinking("Thinking…");
                    break;
                case StageKind.Wake when e.LookAround:
                    _eyes.Wander = true;
                    break;
                case StageKind.Sleep:
                    _eyes.Wander = false;
                    _micLevel.Width = 0;
                    if (!_busy) _eyes.Mood = AceEyes.Moods.Normal;
                    break;
            }
        }

        private void OnAnswered(string question, AceAnswer a)
        {
            HideThinking();
            _micLevel.Width = 0;
            _eyes.Wander = false;
            _eyes.Level = 0;
            if (question.Length > 0 && !string.Equals(question, _lastUserLine, StringComparison.OrdinalIgnoreCase)) ShowUser(question);
            _lastUserLine = "";
            AddAce(a);
            _eyes.Mood = a.Mood switch { "alert" => AceEyes.Moods.Alert, "sleepy" => AceEyes.Moods.Sleepy, "love" => AceEyes.Moods.Love, "confused" => AceEyes.Moods.Confused, "think" => AceEyes.Moods.Think, _ => AceEyes.Moods.Happy };
            _eyes.Hop();
            var calm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
            calm.Tick += (_, _) => { calm.Stop(); if (!_busy && _eyes.Mood != AceEyes.Moods.Alert) _eyes.Mood = AceEyes.Moods.Normal; };
            calm.Start();
            RefreshSide();
        }

        // ===================================================================== Conversation bubbles

        private void AddHello()
        {
            string name = AceRuntime.Current.Config.UserName;
            AddAceLine($"Hi {name}! I'm Ace V4. Ask me anything, help with homework step by step, translate, play trivia or 20 questions, grab a Roblox or C++ script, keep notes and a shopping list, read your screen, or open apps and check your PC. Tap the mic or press Ctrl+Alt+Space to talk.");
        }

        private void ShowUser(string text)
        {
            _lastUserLine = text;
            var bubble = new Border
            {
                CornerRadius = new CornerRadius(16, 16, 4, 16),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(80, 6, 0, 6),
                HorizontalAlignment = HorizontalAlignment.Right,
                Child = AceCardViews.Text(text, 13.5, "FgBrush"),
            };
            bubble.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
            _convo.Children.Add(bubble);
            Spring.PopIn(bubble);
            Trim();
            ScrollToEnd();
        }

        private void AddAceLine(string text) => AddAce(AceAnswer.Say(text, "ui"));

        private void AddAce(AceAnswer a)
        {
            var stack = new StackPanel();
            stack.Children.Add(AceCardViews.Answer(a, compact: false, maxCards: 6));

            // A question to keep the topic going, and things to tap next.
            if (a.FollowUp.Length > 0 || a.Suggestions.Count > 0)
            {
                var next = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                if (a.FollowUp.Length > 0)
                {
                    var lead = AceCardViews.Text("💡 Want to know?", 11.5, "MutedBrush", wrap: false);
                    lead.VerticalAlignment = VerticalAlignment.Center;
                    lead.Margin = new Thickness(0, 0, 6, 6);
                    next.Children.Add(lead);
                }
                foreach (var s in a.Suggestions.Distinct().Take(4))
                {
                    var chip = new Button { Content = s, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 3, 10, 3), FontSize = 11 };
                    chip.SetResourceReference(StyleProperty, s == a.FollowUp ? "HotPillButton" : "SmallPillButton");
                    string say = s;
                    chip.Click += (_, _) => { if (_busy) return; _input.Text = say; _ = SendAsync(); };
                    next.Children.Add(chip);
                }
                stack.Children.Add(next);
            }

            // "How I thought": the steps Ace took, hidden until tapped.
            if (a.Thoughts.Count > 0 || (a.Source.Length > 0 && a.Source != "ui"))
            {
                var meta = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
                var steps = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
                if (a.Thoughts.Count > 0)
                {
                    var toggle = new Button { Content = $"🧠 Thought in {a.Thoughts.Count} step{(a.Thoughts.Count == 1 ? "" : "s")}", Padding = new Thickness(8, 2, 8, 2), FontSize = 11 };
                    toggle.SetResourceReference(StyleProperty, "SmallPillButton");
                    toggle.Click += (_, _) =>
                    {
                        bool open = steps.Visibility != Visibility.Visible;
                        steps.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                        if (open) Spring.PopChildren(steps);
                    };
                    meta.Children.Add(toggle);
                    int i = 1;
                    foreach (var s in a.Thoughts)
                    {
                        var line = AceCardViews.Text($"{i++}. {s.Label}" + (s.Detail.Length > 0 ? "  —  " + s.Detail : ""), 11.5, "MutedBrush");
                        steps.Children.Add(line);
                    }
                }
                if (a.Source.Length > 0 && a.Source != "ui")
                {
                    string sure = a.Confidence >= 0.8 ? "sure" : a.Confidence >= 0.55 ? "fairly sure" : "not sure";
                    var src = AceCardViews.Text($"{SourceName(a.Source)} · {sure}", 11, "MutedBrush", wrap: false);
                    src.VerticalAlignment = VerticalAlignment.Center;
                    src.Margin = new Thickness(4, 0, 0, 0);
                    meta.Children.Add(src);
                }
                stack.Children.Add(meta);
                stack.Children.Add(steps);
            }

            var dot = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 2, 10, 0), VerticalAlignment = VerticalAlignment.Top, Child = AceCardViews.Text("A", 12, "InkBrush", bold: true, wrap: false) };
            ((TextBlock)dot.Child).HorizontalAlignment = HorizontalAlignment.Center;
            ((TextBlock)dot.Child).VerticalAlignment = VerticalAlignment.Center;
            dot.SetResourceReference(Border.BackgroundProperty, "AceBrush");
            var row = new DockPanel { Margin = new Thickness(0, 6, 40, 6) };
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot);
            row.Children.Add(stack);
            _convo.Children.Add(row);
            Spring.PopIn(row);
            Trim();
            ScrollToEnd();
        }

        private static string SourceName(string source) => source switch
        {
            "bank" => "From my Q&A bank",
            "learned" => "From something I learned",
            "web" => "From the web",
            "helper" => "With help from LFM3 Omni",
            "fallback" => "Guessing",
            "error" => "Problem",
            "checks" => "PC check",
            _ => "Skill: " + source,
        };

        private void Trim()
        {
            while (_convo.Children.Count > 120) _convo.Children.RemoveAt(0);
        }

        private void ScrollToEnd() => Dispatcher.BeginInvoke(() => _convoScroll.ScrollToEnd(), DispatcherPriority.Loaded);

        // ===================================================================== Side panel

        private void RefreshSide()
        {
            UpdateClock();
            RefreshToday();
            RefreshSleep();
            RefreshMemory();
            var rt = AceRuntime.Current;
            _mic.Content = rt.Listening ? "■" : "🎤";
            if (rt.Listening) _mic.SetResourceReference(BackgroundProperty, "BadBrush"); else _mic.ClearValue(BackgroundProperty);
            if (!rt.Listening)
            {
                var b = rt.Brain;
                _subtitle.Text = b == null || !b.Ready ? "Loading my brain…" : $"{b.Bank.Count:N0} topics · {(b.Embedder.IsReady ? "neural meaning on" : "word matching")} · {(rt.Config.WakeWord ? "say \"Hey Ace\"" : "Ctrl+Alt+Space")}";
            }
        }

        private void RefreshMemory()
        {
            _memory.Children.Clear();
            var b = AceRuntime.Current.Brain;
            if (b == null || !b.Ready) { _memory.Children.Add(AceCardViews.Text("Loading…", 12, "MutedBrush")); return; }
            var facts = b.PersonalFacts();
            if (facts.Count == 0)
            {
                _memory.Children.Add(AceCardViews.Text("Nothing yet. Say \"remember that my favorite game is Minecraft\".", 11.5, "MutedBrush"));
                return;
            }
            foreach (var f in facts.Take(4)) _memory.Children.Add(AceCardViews.Text("• " + (f.Answers.FirstOrDefault() ?? ""), 12, "FgBrush"));
            if (facts.Count > 4) _memory.Children.Add(AceCardViews.Text($"+{facts.Count - 4} more", 11, "MutedBrush"));
        }

        private void UpdateClock()
        {
            var rt = AceRuntime.Current;
            if (!rt.Config.WakeChecks) { _nextCheck.Text = "Off"; return; }
            var left = rt.NextCheck - DateTime.Now;
            if (rt.NextCheck == default) _nextCheck.Text = "–";
            else if (left.TotalSeconds <= 0) _nextCheck.Text = "Any second…";
            else _nextCheck.Text = left.TotalMinutes >= 1 ? $"in {(int)left.TotalMinutes}m {left.Seconds:00}s" : $"in {left.Seconds}s";
        }

        private void RefreshToday()
        {
            _today.Children.Clear();
            var events = CalendarStore.OnDay(DateTime.Today);
            foreach (var e in events.Take(6)) _today.Children.Add(AceCardViews.EventRow(e, showDay: false));
            if (events.Count == 0) _today.Children.Add(AceCardViews.Text("Nothing planned. Try \"add soccer practice tomorrow at 5\".", 11.5, "MutedBrush"));
            var reminders = ReminderStore.Pending();
            if (reminders.Count > 0)
            {
                var r = AceCardViews.Text($"⏰ {reminders.Count} reminder{(reminders.Count == 1 ? "" : "s")} waiting · next {reminders.Min(x => x.Due):h:mm tt}", 11.5, "AmberBrush");
                r.Margin = new Thickness(0, 6, 0, 0);
                _today.Children.Add(r);
            }
        }

        private void RefreshSleep()
        {
            _sleep.Children.Clear();
            var s = ActivityTracker.Summary(AceRuntime.Current.Config.SleepGoalHours);
            if (s == null)
            {
                _sleep.Children.Add(AceCardViews.Text("Ace learns your sleep from when you use your PC. Give it a few days.", 11.5, "MutedBrush"));
                return;
            }
            _sleep.Children.Add(AceCardViews.Text("Bed around " + ActivityTracker.Clock(s.SuggestBed), 17, "FgBrush", bold: true, wrap: false, font: "DisplayFont"));
            _sleep.Children.Add(AceCardViews.Text($"Usually {ActivityTracker.Clock(s.AvgBed)} to {ActivityTracker.Clock(s.AvgWake)} ({ActivityTracker.Hours(s.AvgSleep)}), {s.Nights} nights.", 11.5, "MutedBrush"));
        }

        // ===================================================================== Bits

        private Border Card(UIElement child) => Card(new Thickness(14, 12, 14, 12), child);

        private Border Card(Thickness padding, UIElement? child = null)
        {
            var b = new Border { Padding = padding, Margin = new Thickness(0, 0, 0, 8), Child = child };
            b.SetResourceReference(StyleProperty, "Card");
            b.Padding = padding;
            return b;
        }

        private static TextBlock Header(string text)
        {
            var t = AceCardViews.Text(text.ToUpperInvariant(), 10.5, "AceBrush", bold: true, wrap: false);
            t.Margin = new Thickness(0, 0, 0, 6);
            t.VerticalAlignment = VerticalAlignment.Center;
            return t;
        }

        private static Button SmallButton(string label)
        {
            var b = new Button { Content = label, Margin = new Thickness(6, 0, 0, 0) };
            b.SetResourceReference(StyleProperty, "SmallPillButton");
            return b;
        }

        private static Button HotButton(string label)
        {
            var b = new Button { Content = label };
            b.SetResourceReference(StyleProperty, "HotPillButton");
            return b;
        }

        private static Button IconButton(string glyph, string tip)
        {
            var b = new Button { Content = glyph, ToolTip = tip, Margin = new Thickness(4, 0, 0, 0) };
            b.SetResourceReference(StyleProperty, "IconButton");
            return b;
        }

        private WrapPanel Suggestions()
        {
            var wrap = new WrapPanel { Margin = new Thickness(14, 0, 14, 2) };
            foreach (var s in new[] { "Help with my math homework", "Give me a kill brick script", "Let's play trivia", "What's playing?", "What's on my screen?", "Show my shopping list" })
            {
                var b = new Button { Content = s, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 3, 10, 3), FontSize = 11 };
                b.SetResourceReference(StyleProperty, "SmallPillButton");
                b.Click += (_, _) => { _input.Text = s; _ = SendAsync(); };
                wrap.Children.Add(b);
            }
            return wrap;
        }
    }
}
