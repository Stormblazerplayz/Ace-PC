using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using Nimbus.AceV4;
using Nimbus.AceV4.Audio;
using Nimbus.AceV4.Brain;
using Nimbus.AceV4.Pc;
using Nimbus.Controls;
using Nimbus.Motion;
using Nimbus.Services;
using Nimbus.Services.Calendar;

namespace Nimbus.Overlay
{
    /// <summary>
    /// Ace for PC, laid out like the iPad app: the orb sits high in the middle of the top bar, the chat is under it
    /// and the "Talk to Ace" bar is at the bottom. Click the orb for Settings, hold it (or press Ctrl+Alt+Space)
    /// to talk, or say "Hey Ace". The calendar is on the left of the orb and "check my PC" on the right.
    /// </summary>
    public sealed class AceWindow : Window
    {
        private static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");
        private static readonly Color Blue = AceOrb.AceBlue;

        private readonly AceRuntime _rt = AceRuntime.Current;
        private readonly Grid _root = new();
        private readonly Grid _content = new();
        private readonly AceOrb _orb = new() { OrbSize = 52, ExpandedWidth = 330, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        private readonly FrameworkElement _calendarButton, _checksButton;
        private readonly WrapPanel _timers = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(12, 0, 12, 4) };
        private readonly StackPanel _convo = new();
        private readonly ScrollViewer _scroll;
        private readonly FrameworkElement _empty;
        private readonly Border _thinkingBox;
        private readonly TextBlock _thinkingLine;
        private readonly StackPanel _thinkingSteps = new();
        private readonly TextBox _input = new();
        private readonly TextBlock _placeholder;
        private readonly Button _mic, _send;
        private readonly TextBlock _micGlyph;
        private readonly Grid _sheetLayer = new() { Visibility = Visibility.Collapsed };
        private readonly Border _sheetDim, _sheet;
        private readonly TextBlock _sheetTitle;
        private readonly ContentControl _sheetBody = new();
        private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(100) };
        private string _lastUserLine = "", _sheetName = "";
        private bool _sheetClosing;
        private bool _busy, _talking;
        private OrbMood _flash = OrbMood.Idle;
        private string _flashStatus = "";
        private DateTime _flashUntil;
        private int _ticks;

        public AceWindow()
        {
            Title = "Ace";
            Width = 560;
            Height = 820;
            MinWidth = 400;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            Background = new LinearGradientBrush(AceOrb.C(0x0E0C1F), AceOrb.C(0x06060B), 90);
            Foreground = new SolidColorBrush(AceOrb.Fg);
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false,
            });
            try
            {
                var icon = System.IO.Path.Combine(AppContext.BaseDirectory, "Ace.ico");
                if (System.IO.File.Exists(icon)) Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(icon));
            }
            catch { }

            _content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // top bar with the orb
            _content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // timers
            _content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // chat
            _content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // talk bar

            // ================= Top bar: calendar · orb · checks, and the window buttons
            var top = new Grid { Height = 92, Background = Brushes.Transparent };
            top.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2) { ToggleMaximize(); return; }
                try { DragMove(); } catch { }
            };
            top.Children.Add(_orb);

            _calendarButton = RoundIcon("", "Calendar", () => OpenSheet("calendar"));
            _calendarButton.HorizontalAlignment = HorizontalAlignment.Left;
            _calendarButton.VerticalAlignment = VerticalAlignment.Center;
            _calendarButton.Margin = new Thickness(18, 8, 0, 0);
            top.Children.Add(_calendarButton);

            _checksButton = RoundIcon("", "Check my PC", () => Ask("check my pc"));
            _checksButton.HorizontalAlignment = HorizontalAlignment.Right;
            _checksButton.VerticalAlignment = VerticalAlignment.Center;
            _checksButton.Margin = new Thickness(0, 8, 18, 0);
            top.Children.Add(_checksButton);

            var winButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            winButtons.Children.Add(WindowButton("", "Minimize", () => WindowState = WindowState.Minimized, danger: false));
            winButtons.Children.Add(WindowButton("", "Close", Close, danger: true));
            top.Children.Add(winButtons);
            Grid.SetRow(top, 0);
            _content.Children.Add(top);

            _orb.Tapped += () => { _rt.Play(Sfx.Pop, 0.5); OpenSheet("settings"); };
            _orb.Hold += held =>
            {
                if (held) { _rt.Play(Sfx.Tick, 0.6); if (!_rt.Listening) _ = _rt.ListenAndAskAsync("orb", showOnStage: false); }
                else if (_rt.Listening) _rt.Dictation.StopNow();
            };

            // ================= Kitchen timers and the focus round
            Grid.SetRow(_timers, 1);
            _content.Children.Add(_timers);

            // ================= Chat
            var column = new StackPanel { MaxWidth = 820, Margin = new Thickness(16, 6, 16, 12) };
            _empty = EmptyState();
            column.Children.Add(_empty);
            column.Children.Add(_convo);

            _thinkingLine = AceCardViews.Text("Thinking…", 12.5, "AmberBrush", bold: true, wrap: false);
            var bars = new WaveBars { Bars = 4, BarHeight = 12, BarBrush = new SolidColorBrush(AceOrb.Amber), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var thinkRow = new StackPanel { Orientation = Orientation.Horizontal };
            thinkRow.Children.Add(bars);
            thinkRow.Children.Add(_thinkingLine);
            var thinkStack = new StackPanel { Margin = new Thickness(14, 10, 14, 10) };
            thinkStack.Children.Add(thinkRow);
            _thinkingSteps.Margin = new Thickness(20, 4, 0, 0);
            thinkStack.Children.Add(_thinkingSteps);
            var dashed = new Rectangle { RadiusX = 16, RadiusY = 16, Stroke = new SolidColorBrush(AceOrb.WithA(AceOrb.Amber, 0.75)), StrokeThickness = 1.4, StrokeDashArray = new DoubleCollection { 4, 3 }, Fill = new SolidColorBrush(AceOrb.WithA(AceOrb.Amber, 0.06)) };
            var thinkGrid = new Grid();
            thinkGrid.Children.Add(dashed);
            thinkGrid.Children.Add(thinkStack);
            _thinkingBox = new Border { Child = thinkGrid, Margin = new Thickness(36, 6, 40, 6), HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
            column.Children.Add(_thinkingBox);

            _scroll = new ScrollViewer { Content = column, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            // Messages fade out under the top bar, like on the iPad.
            _scroll.OpacityMask = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
                GradientStops = { new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Black, 0.045), new GradientStop(Colors.Black, 1) },
            };
            Grid.SetRow(_scroll, 2);
            _content.Children.Add(_scroll);

            // ================= "Talk to Ace" bar
            var plus = CircleButton("", "Quick things to ask", 40, transparent: true);
            plus.Click += (_, _) => ShowQuickMenu(plus);
            _input.AcceptsReturn = true;
            _input.TextWrapping = TextWrapping.Wrap;
            _input.MaxHeight = 140;
            _input.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _input.Background = Brushes.Transparent;
            _input.BorderThickness = new Thickness(0);
            _input.Foreground = new SolidColorBrush(AceOrb.Fg);
            _input.CaretBrush = new SolidColorBrush(Blue);
            _input.SelectionBrush = new SolidColorBrush(Blue);
            _input.FontSize = 14.5;
            _input.Padding = new Thickness(2, 9, 2, 9);
            _input.VerticalContentAlignment = VerticalAlignment.Center;
            _input.SetResourceReference(FontFamilyProperty, "BodyFont");
            _input.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { e.Handled = true; _ = SendAsync(); }
                else if (e.Key == Key.Up && _input.Text.Length == 0 && _sentHistory.Count > 0) { e.Handled = true; _input.Text = _sentHistory[^1]; _input.CaretIndex = _input.Text.Length; }
            };
            _placeholder = AceCardViews.Text("Talk to Ace", 14.5, "MutedBrush", wrap: false);
            _placeholder.IsHitTestVisible = false;
            _placeholder.VerticalAlignment = VerticalAlignment.Center;
            _placeholder.Margin = new Thickness(4, 0, 0, 0);
            _input.TextChanged += (_, _) =>
            {
                _placeholder.Visibility = _input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                UpdateSend();
            };
            var field = new Grid();
            field.Children.Add(_input);
            field.Children.Add(_placeholder);

            _mic = CircleButton("", "Talk: click, talk, it stops when you stop (Ctrl+Alt+Space)", 34, transparent: true);
            _micGlyph = (TextBlock)_mic.Content;
            _mic.Click += (_, _) => _ = _rt.ListenAndAskAsync("mic", showOnStage: false);
            _send = CircleButton("", "Send (Enter)", 34, transparent: false);
            _send.Click += (_, _) => _ = SendAsync();

            var bar = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(_send, Dock.Right);
            DockPanel.SetDock(_mic, Dock.Right);
            bar.Children.Add(_send);
            bar.Children.Add(_mic);
            bar.Children.Add(field);
            var fieldBox = new Border
            {
                Child = bar,
                CornerRadius = new CornerRadius(22),
                Padding = new Thickness(14, 2, 5, 2),
                MinHeight = 44,
                Background = new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
            };
            _input.GotKeyboardFocus += (_, _) => fieldBox.BorderBrush = new SolidColorBrush(AceOrb.WithA(Blue, 0.6));
            _input.LostKeyboardFocus += (_, _) => fieldBox.BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            var talkBar = new DockPanel { MaxWidth = 820, Margin = new Thickness(12, 4, 16, 14) };
            DockPanel.SetDock(plus, Dock.Left);
            plus.Margin = new Thickness(0, 0, 6, 0);
            plus.VerticalAlignment = VerticalAlignment.Bottom;
            talkBar.Children.Add(plus);
            talkBar.Children.Add(fieldBox);
            Grid.SetRow(talkBar, 3);
            _content.Children.Add(talkBar);

            _root.Children.Add(_content);

            // ================= Sheets (Settings, Calendar, Memories, Scripts) slide up over the chat
            _sheetDim = new Border { Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), Opacity = 0 };
            _sheetDim.MouseLeftButtonDown += (_, _) => CloseSheet();
            _sheetLayer.Children.Add(_sheetDim);
            _sheetTitle = AceCardViews.Text("", 17, "FgBrush", bold: true, wrap: false, font: "DisplayFont");
            _sheetTitle.VerticalAlignment = VerticalAlignment.Center;
            var sheetClose = CircleButton("", "Close (Esc)", 32, transparent: true);
            ((TextBlock)sheetClose.Content).FontSize = 11;
            sheetClose.Click += (_, _) => CloseSheet();
            var sheetHead = new DockPanel { Margin = new Thickness(20, 6, 12, 8) };
            DockPanel.SetDock(sheetClose, Dock.Right);
            sheetHead.Children.Add(sheetClose);
            sheetHead.Children.Add(_sheetTitle);
            var grabber = new Border { Width = 38, Height = 5, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(0, 8, 0, 2), HorizontalAlignment = HorizontalAlignment.Center };
            var sheetScroll = new ScrollViewer { Content = _sheetBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Padding = new Thickness(20, 0, 20, 20) };
            var sheetDock = new DockPanel();
            DockPanel.SetDock(grabber, Dock.Top);
            DockPanel.SetDock(sheetHead, Dock.Top);
            sheetDock.Children.Add(grabber);
            sheetDock.Children.Add(sheetHead);
            sheetDock.Children.Add(sheetScroll);
            _sheet = new Border
            {
                Child = sheetDock,
                MaxWidth = 560,
                Margin = new Thickness(0, 70, 0, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
                CornerRadius = new CornerRadius(22, 22, 0, 0),
                Background = new SolidColorBrush(AceOrb.WithA(AceOrb.Ink, 0.98)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1, 1, 1, 0),
                RenderTransform = new TranslateTransform(),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 0, Opacity = 0.6, Color = Colors.Black },
            };
            _sheet.MouseLeftButtonDown += (_, e) => e.Handled = true;   // clicks on the sheet don't close it
            _sheetLayer.Children.Add(_sheet);
            _root.Children.Add(_sheetLayer);

            Content = _root;

            // ================= Wiring
            _rt.Answered += (q, a) => Dispatcher.BeginInvoke(() => OnAnswered(q, a));
            _rt.Stage += e => Dispatcher.BeginInvoke(() => OnStage(e));
            _rt.Changed += () => Dispatcher.BeginInvoke(UpdateMic);
            _rt.Speech.Speaking += talking => Dispatcher.BeginInvoke(() => _talking = talking);
            _ = _rt.WhenLoaded.ContinueWith(_ => Dispatcher.BeginInvoke(() =>
            {
                _rt.Brain.Thought += step => Dispatcher.BeginInvoke(() => AddThinkingStep(step));
                RefreshEmpty();
            }));
            AceCardViews.CardSaid += said => Dispatcher.BeginInvoke(() => AddAce(AceAnswer.Say(said, "ui")));
            AceCardViews.AskAce += text => Dispatcher.BeginInvoke(() => { ShowUser(text); ShowThinking("Thinking…"); });
            ReminderStore.Changed += () => Dispatcher.BeginInvoke(RefreshTimers);
            FocusTimer.Changed += () => Dispatcher.BeginInvoke(RefreshTimers);
            CalendarStore.Changed += () => Dispatcher.BeginInvoke(() => { if (_sheetName == "calendar") OpenSheet("calendar"); });
            Hub.NavigateRequested += OnNavigate;

            _tick.Tick += (_, _) => OnTick();
            _tick.Start();

            SourceInitialized += (_, _) =>
            {
                _rt.AttachWindow(this);
                RoundCorners();
            };
            Activated += (_, _) => UpdateVisible();
            Deactivated += (_, _) => UpdateVisible();
            StateChanged += (_, _) =>
            {
                UpdateVisible();
                _orb.Paused = WindowState == WindowState.Minimized;
                // A maximized borderless window hangs over the screen edge a little; pad it back in.
                _root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            };
            Loaded += (_, _) => { RefreshTimers(); UpdateSend(); _input.Focus(); };
            Closed += (_, _) =>
            {
                _tick.Stop();
                Hub.NavigateRequested -= OnNavigate;
            };
            PreviewKeyDown += OnKey;
            SizeChanged += (_, _) => { if (_sheetLayer.Visibility == Visibility.Visible) _sheet.MaxHeight = Math.Max(300, ActualHeight - 70); };
        }

        private readonly List<string> _sentHistory = new();

        // ===================================================================== Asking

        private void Ask(string text)
        {
            if (_busy) return;
            _input.Text = text;
            _ = SendAsync();
        }

        private async Task SendAsync()
        {
            string text = _input.Text.Trim();
            if (text.Length == 0 || _busy) return;
            _input.Clear();
            _sentHistory.Add(text);
            if (_sentHistory.Count > 50) _sentHistory.RemoveAt(0);
            ShowUser(text);
            ShowThinking("Thinking…");
            try { await _rt.AskAsync(text); }
            catch (Exception ex) { HideThinking(); AddAce(AceAnswer.Say("Something went wrong: " + ex.Message, "error", 0)); }
        }

        private void ShowThinking(string line)
        {
            _busy = true;
            _thinkingSteps.Children.Clear();
            _thinkingLine.Text = line;
            _thinkingBox.Visibility = Visibility.Visible;
            Spring.PopIn(_thinkingBox);
            UpdateSend();
            ScrollToEnd();
        }

        private void HideThinking()
        {
            _busy = false;
            _thinkingBox.Visibility = Visibility.Collapsed;
            _thinkingSteps.Children.Clear();
            UpdateSend();
        }

        private void AddThinkingStep(ThoughtStep step)
        {
            if (!_busy) return;
            var t = AceCardViews.Text("· " + step.Label + (step.Detail.Length > 0 ? ": " + step.Detail : ""), 11.5, "MutedBrush", wrap: false);
            _thinkingSteps.Children.Add(t);
            while (_thinkingSteps.Children.Count > 5) _thinkingSteps.Children.RemoveAt(0);
            Spring.PopIn(t);
            ScrollToEnd();
        }

        private void OnStage(StageEvent e)
        {
            switch (e.Kind)
            {
                case StageKind.Status when _busy && e.Text.Length > 0:
                    _thinkingLine.Text = e.Text;
                    break;
                case StageKind.Status when e.Text.Length > 0 && !_rt.Listening:
                    Flash(OrbMood.Checking, e.Text, 2.5);
                    break;
                case StageKind.Level:
                    _orb.Level = Math.Clamp(e.Value, 0, 1);
                    break;
                case StageKind.Heard:
                    _orb.Level = 0;
                    ShowUser(e.Text);
                    ShowThinking("Thinking…");
                    break;
                case StageKind.Wake when e.LookAround:
                    Flash(OrbMood.Checking, "Checking things", 25);
                    break;
                case StageKind.Wake when e.Text.Length > 0 && !e.Text.StartsWith("Listening", StringComparison.OrdinalIgnoreCase):
                    Flash(e.Text.Contains("Bedtime", StringComparison.OrdinalIgnoreCase) ? OrbMood.Alert : OrbMood.Happy, e.Text.TrimEnd('…', '.'), 4);
                    break;
                case StageKind.Sleep:
                    _orb.Level = 0;
                    if (_flash == OrbMood.Checking) _flashUntil = DateTime.Now;
                    break;
            }
        }

        private void OnAnswered(string question, AceAnswer a)
        {
            HideThinking();
            _orb.Level = 0;
            if (question.Length > 0 && !string.Equals(question, _lastUserLine, StringComparison.OrdinalIgnoreCase)) ShowUser(question);
            _lastUserLine = "";
            AddAce(a);
            if (a.Source == "checks" || a.Mood == "alert")
                Flash(a.Mood == "alert" ? OrbMood.Alert : OrbMood.Happy, a.Mood == "alert" ? "Heads up" : "All good", 3.5);
            else if (_flash == OrbMood.Checking)
                _flashUntil = DateTime.Now;
            if (!IsActive && WindowState != WindowState.Minimized) FlashTaskbar();
        }

        // ===================================================================== Messages

        private void ShowUser(string text)
        {
            _lastUserLine = text;
            HideEmpty();
            var bubble = new Border
            {
                CornerRadius = new CornerRadius(18, 18, 6, 18),
                Padding = new Thickness(14, 9, 14, 9),
                Margin = new Thickness(70, 6, 0, 6),
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new SolidColorBrush(Blue),
            };
            var t = new TextBlock { Text = text, FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Black };
            t.SetResourceReference(TextBlock.FontFamilyProperty, "BodyFont");
            bubble.Child = t;
            bubble.ContextMenu = CopyMenu(text);
            _convo.Children.Add(bubble);
            Spring.PopIn(bubble);
            Trim();
            ScrollToEnd();
        }

        private void AddAce(AceAnswer a)
        {
            HideEmpty();
            var stack = new StackPanel();

            if (!string.IsNullOrWhiteSpace(a.Text))
            {
                var t = AceCardViews.Text(a.Text, 14, "FgBrush");
                t.LineHeight = 20;
                var bubble = new Border
                {
                    Child = t,
                    CornerRadius = new CornerRadius(6, 18, 18, 18),
                    Padding = new Thickness(14, 10, 14, 10),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    BorderThickness = new Thickness(1),
                    ContextMenu = CopyMenu(a.Text),
                };
                bubble.SetResourceReference(Border.BackgroundProperty, "CardBrush");
                bubble.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
                if (a.Source == "error") bubble.BorderBrush = new SolidColorBrush(AceOrb.WithA(AceOrb.C(0xFF5A4E), 0.6));
                stack.Children.Add(bubble);
            }

            foreach (var card in a.Cards.Take(6))
            {
                var view = AceCardViews.Card(card, compact: false);
                if (view == null) continue;
                view.Margin = new Thickness(0, 8, 0, 0);
                view.MaxWidth = 560;
                view.HorizontalAlignment = HorizontalAlignment.Left;
                stack.Children.Add(view);
            }

            // Things to tap next.
            var chips = a.Suggestions.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().Take(4).ToList();
            if (a.FollowUp.Length > 0 && !chips.Contains(a.FollowUp)) chips.Insert(0, a.FollowUp);
            if (chips.Count > 0)
            {
                var next = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                foreach (var s in chips.Take(4))
                {
                    var chip = new Button { Content = s, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(11, 4, 11, 4), FontSize = 11.5 };
                    chip.SetResourceReference(StyleProperty, s == a.FollowUp ? "HotPillButton" : "SmallPillButton");
                    string say = s;
                    chip.Click += (_, _) => Ask(say);
                    next.Children.Add(chip);
                }
                stack.Children.Add(next);
            }

            // "How I got this": the steps Ace took and where the answer came from, hidden until clicked.
            if (a.Thoughts.Count > 0 || (a.Source.Length > 0 && a.Source != "ui"))
            {
                var steps = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(4, 4, 0, 0) };
                int i = 1;
                foreach (var s in a.Thoughts)
                    steps.Children.Add(AceCardViews.Text($"{i++}. {s.Label}" + (s.Detail.Length > 0 ? ": " + s.Detail : ""), 11.5, "MutedBrush"));
                if (a.Source.Length > 0 && a.Source != "ui")
                {
                    string sure = a.Confidence >= 0.8 ? "sure" : a.Confidence >= 0.55 ? "fairly sure" : "not sure";
                    var src = AceCardViews.Text($"{SourceName(a.Source)} · {sure}", 11, "AceBrush", wrap: false);
                    src.Margin = new Thickness(0, 4, 0, 0);
                    steps.Children.Add(src);
                }
                var toggle = new Button { Content = "How I got this  ›", Padding = new Thickness(4, 1, 4, 1), FontSize = 11, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand };
                toggle.SetResourceReference(StyleProperty, "BareButton");
                toggle.SetResourceReference(ForegroundProperty, "MutedBrush");
                toggle.Click += (_, _) =>
                {
                    bool open = steps.Visibility != Visibility.Visible;
                    steps.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                    toggle.Content = open ? "How I got this  ⌄" : "How I got this  ›";
                    if (open) Spring.PopChildren(steps);
                };
                stack.Children.Add(toggle);
                stack.Children.Add(steps);
            }

            var avatar = new OrbStill { Width = 26, Height = 26, Margin = new Thickness(0, 4, 10, 0), VerticalAlignment = VerticalAlignment.Top, Mood = a.Source == "error" ? OrbMood.Alert : OrbMood.Idle };
            var row = new DockPanel { Margin = new Thickness(0, 6, 30, 6) };
            DockPanel.SetDock(avatar, Dock.Left);
            row.Children.Add(avatar);
            row.Children.Add(stack);
            _convo.Children.Add(row);
            Spring.PopIn(row);
            Trim();
            ScrollToEnd();
        }

        private static string SourceName(string source) => source switch
        {
            "bank" => "From what I know",
            "learned" => "From something I learned",
            "web" => "From the web",
            "helper" => "From LFM3 Omni",
            "fallback" => "Best guess",
            "error" => "Problem",
            "checks" => "PC check",
            "memory" => "From what you told me",
            _ => "Skill: " + source,
        };

        private ContextMenu CopyMenu(string text)
        {
            var menu = new ContextMenu();
            var copy = new MenuItem { Header = "Copy" };
            copy.Click += (_, _) => { try { Clipboard.SetText(text); } catch { } };
            menu.Items.Add(copy);
            return menu;
        }

        private void Trim()
        {
            while (_convo.Children.Count > 150) _convo.Children.RemoveAt(0);
        }

        private void ScrollToEnd() => Dispatcher.BeginInvoke(() => _scroll.ScrollToEnd(), DispatcherPriority.Loaded);

        private void ClearChat()
        {
            _convo.Children.Clear();
            HideThinking();
            _empty.Visibility = Visibility.Visible;
            RefreshEmpty();
            Spring.PopIn(_empty);
        }

        // ===================================================================== Empty state

        private TextBlock? _hello;

        private FrameworkElement EmptyState()
        {
            var stack = new StackPanel { Margin = new Thickness(0, 26, 0, 10), HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 520 };
            _hello = AceCardViews.Text("Hi, I'm Ace.", 28, "FgBrush", bold: true, wrap: true, font: "DisplayFont");
            _hello.TextAlignment = TextAlignment.Center;
            stack.Children.Add(_hello);
            var sub = AceCardViews.Text("Ask me anything, or try one of these.", 14, "MutedBrush");
            sub.TextAlignment = TextAlignment.Center;
            sub.Margin = new Thickness(0, 6, 0, 18);
            stack.Children.Add(sub);
            var grid = new UniformGrid { Columns = 2 };
            foreach (var (glyph, label, ask) in new[]
            {
                ("", "What's on today?", "what's on my calendar today"),
                ("", "Help with math homework", "help me solve 3x + 7 = 22"),
                ("", "Kill brick script", "give me a kill brick script"),
                ("", "Pancake recipe", "how do I make pancakes"),
                ("", "Set a 10 minute timer", "set a timer for 10 minutes"),
                ("", "Check my PC", "check my pc"),
                ("", "Play trivia", "let's play trivia"),
                ("", "What's new?", "what's new"),
            })
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var g = new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 15, Foreground = new SolidColorBrush(Blue), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                row.Children.Add(g);
                var l = AceCardViews.Text(label, 13, "FgBrush", wrap: false);
                l.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(l);
                var b = new Button { Content = row, Margin = new Thickness(4), Padding = new Thickness(14, 12, 14, 12), HorizontalContentAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand, Template = TileTemplate() };
                string say = ask;
                b.Click += (_, _) => Ask(say);
                grid.Children.Add(b);
            }
            stack.Children.Add(grid);
            var hint = AceCardViews.Text("Click the orb for Settings · hold it to talk · or say \"Hey Ace\"", 11.5, "MutedBrush");
            hint.TextAlignment = TextAlignment.Center;
            hint.Margin = new Thickness(0, 16, 0, 0);
            stack.Children.Add(hint);
            return stack;
        }

        private void RefreshEmpty()
        {
            if (_hello != null) _hello.Text = $"Hi {_rt.Config.UserName}, I'm Ace.";
        }

        private void HideEmpty()
        {
            if (_empty.Visibility == Visibility.Visible) _empty.Visibility = Visibility.Collapsed;
        }

        /// <summary>A soft rounded tile that lights up when you point at it.</summary>
        private static ControlTemplate TileTemplate()
        {
            var t = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border), "bd");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(16));
            border.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)));
            border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(cp);
            t.VisualTree = border;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x1E, 0x5B, 0xB6, 0xFF)), "bd"));
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(0x66, 0x5B, 0xB6, 0xFF)), "bd"));
            t.Triggers.Add(hover);
            var press = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            press.Setters.Add(new Setter(UIElement.OpacityProperty, 0.75, "bd"));
            t.Triggers.Add(press);
            return t;
        }

        // ===================================================================== The orb's mood, timers and buttons (10 times a second)

        private void OnTick()
        {
            _ticks++;
            OrbMood mood;
            string status = "";
            if (_rt.Listening) mood = OrbMood.Listening;
            else if (_busy) mood = OrbMood.Thinking;
            else if (_talking) mood = OrbMood.Talking;
            else if (DateTime.Now < _flashUntil) { mood = _flash; status = _flashStatus; }
            else if (_input.IsKeyboardFocused && _input.Text.Length > 0) mood = OrbMood.Typing;
            else if (_rt.Away) mood = OrbMood.Sleeping;
            else mood = OrbMood.Idle;
            _orb.Mood = mood;
            _orb.Status = status;
            _orb.FocusProgress = FocusTimer.Running ? FocusTimer.ProgressAt(DateTime.Now) : -1;

            // The calendar and checks buttons step aside when the orb stretches out with its eyes.
            double side = AceOrb.ShowsEyes(mood) ? 0 : 1;
            _calendarButton.Opacity += (side - _calendarButton.Opacity) * 0.35;
            _checksButton.Opacity = _calendarButton.Opacity;
            _calendarButton.IsHitTestVisible = _checksButton.IsHitTestVisible = side > 0;

            if (_ticks % 10 == 0) UpdateTimerText();
        }

        private void Flash(OrbMood mood, string status, double seconds)
        {
            _flash = mood;
            _flashStatus = status;
            _flashUntil = DateTime.Now.AddSeconds(seconds);
        }

        private void UpdateMic()
        {
            bool on = _rt.Listening;
            _micGlyph.Text = on ? "" : "";
            _mic.Background = on ? new SolidColorBrush(AceOrb.C(0xFF5A4E)) : Brushes.Transparent;
            _micGlyph.Foreground = on ? Brushes.White : new SolidColorBrush(AceOrb.WithA(AceOrb.Fg, 0.8));
            _mic.ToolTip = on ? "Stop listening" : "Talk: click, talk, it stops when you stop (Ctrl+Alt+Space)";
        }

        private void UpdateSend()
        {
            bool ready = _input.Text.Trim().Length > 0 && !_busy;
            _send.IsEnabled = ready;
            _send.Opacity = ready ? 1 : 0.35;
        }

        private readonly Dictionary<string, TextBlock> _timerText = new();

        private void RefreshTimers()
        {
            _timers.Children.Clear();
            _timerText.Clear();
            foreach (var r in ReminderStore.Pending().Where(r => r.IsTimer).OrderBy(r => r.Due).Take(6))
                _timers.Children.Add(TimerChip("t" + r.Id, "", r.Text.Length > 0 ? r.Text : "Timer", AceOrb.Amber, () => Ask("cancel the " + (r.Text.Length > 0 ? r.Text + " " : "") + "timer")));
            if (FocusTimer.Running)
                _timers.Children.Add(TimerChip("focus", "", FocusTimer.PhaseName, AceOrb.FocusGreen, () => Ask("stop focus")));
            _timers.Visibility = _timers.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateTimerText();
        }

        private FrameworkElement TimerChip(string key, string glyph, string label, Color color, Action stop)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 12, Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var name = AceCardViews.Text(label, 12, "FgBrush", wrap: false);
            name.MaxWidth = 140;
            name.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(name);
            var time = new TextBlock { FontSize = 12.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(color), Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            time.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            row.Children.Add(time);
            var x = new Button { Content = new TextBlock { Text = "", FontFamily = Icons, FontSize = 9 }, ToolTip = "Stop", Cursor = Cursors.Hand, Padding = new Thickness(4), Margin = new Thickness(2, 0, 0, 0) };
            x.SetResourceReference(StyleProperty, "BareButton");
            x.SetResourceReference(ForegroundProperty, "MutedBrush");
            x.Click += (_, _) => stop();
            row.Children.Add(x);
            _timerText[key] = time;
            return new Border
            {
                Child = row,
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(10, 4, 6, 4),
                Margin = new Thickness(3),
                Background = new SolidColorBrush(AceOrb.WithA(color, 0.10)),
                BorderBrush = new SolidColorBrush(AceOrb.WithA(color, 0.35)),
                BorderThickness = new Thickness(1),
            };
        }

        private void UpdateTimerText()
        {
            var now = DateTime.Now;
            foreach (var r in ReminderStore.Pending().Where(r => r.IsTimer))
                if (_timerText.TryGetValue("t" + r.Id, out var t)) t.Text = Clock(r.Due - now);
            if (_timerText.TryGetValue("focus", out var f)) f.Text = (FocusTimer.Paused ? "paused " : "") + Clock(FocusTimer.LeftAt(now));
        }

        private static string Clock(TimeSpan left)
        {
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            return left.TotalHours >= 1 ? $"{(int)left.TotalHours}:{left.Minutes:00}:{left.Seconds:00}" : $"{left.Minutes}:{left.Seconds:00}";
        }

        private void ShowQuickMenu(FrameworkElement anchor)
        {
            var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Top };
            foreach (var (label, ask) in new[]
            {
                ("Check my PC", "check my pc"),
                ("Start a focus round", "start a focus round"),
                ("Set a timer for 5 minutes", "set a timer for 5 minutes"),
                ("What's on my screen?", "what's on my screen"),
                ("Show my shopping list", "show my shopping list"),
                ("Play trivia", "let's play trivia"),
                ("Tell me a joke", "tell me a joke"),
            })
            {
                var item = new MenuItem { Header = label };
                string say = ask;
                item.Click += (_, _) => Ask(say);
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            foreach (var (label, sheet) in new[] { ("Scripts library", "scripts"), ("What I remember", "memories"), ("Calendar", "calendar"), ("Settings", "settings") })
            {
                var item = new MenuItem { Header = label };
                string s = sheet;
                item.Click += (_, _) => OpenSheet(s);
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        // ===================================================================== Sheets

        private void OpenSheet(string name)
        {
            FrameworkElement body;
            string title;
            switch (name)
            {
                case "settings": title = "Settings"; body = SettingsSheet(); break;
                case "calendar": title = "Calendar"; body = CalendarSheet(); break;
                case "memories": title = "What I remember"; body = MemoriesSheet(); break;
                case "scripts": title = "Scripts library"; body = ScriptsSheet(""); break;
                default: return;
            }
            bool wasOpen = _sheetLayer.Visibility == Visibility.Visible && !_sheetClosing;
            _sheetClosing = false;
            _sheetName = name;
            _sheetTitle.Text = title;
            _sheetBody.Content = body;
            if (wasOpen) { Spring.PopIn(body); return; }
            _sheetLayer.Visibility = Visibility.Visible;
            _sheet.MaxHeight = Math.Max(300, ActualHeight - 70);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            ((TranslateTransform)_sheet.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(Math.Min(420, ActualHeight * 0.6), 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
            _sheet.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            _sheetDim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        }

        private void CloseSheet()
        {
            if (_sheetLayer.Visibility != Visibility.Visible || _sheetClosing) return;
            _sheetName = "";
            _sheetClosing = true;
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var slide = new DoubleAnimation(0, Math.Min(420, ActualHeight * 0.6), TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };
            slide.Completed += (_, _) =>
            {
                if (!_sheetClosing) return;   // opened again while closing
                _sheetClosing = false;
                _sheetLayer.Visibility = Visibility.Collapsed;
                _sheetBody.Content = null;
            };
            ((TranslateTransform)_sheet.RenderTransform).BeginAnimation(TranslateTransform.YProperty, slide);
            _sheetDim.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220)));
            _input.Focus();
        }

        private FrameworkElement SettingsSheet()
        {
            var stack = new StackPanel();
            var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            void Add(string label, Action click, bool hot = false)
            {
                var b = new Button { Content = label, Margin = new Thickness(0, 0, 6, 6) };
                b.SetResourceReference(StyleProperty, hot ? "HotPillButton" : "SmallPillButton");
                b.Click += (_, _) => click();
                buttons.Children.Add(b);
            }
            Add("What I remember", () => OpenSheet("memories"));
            Add("Scripts library", () => OpenSheet("scripts"));
            Add("What's new", () => { CloseSheet(); Ask("what's new"); });
            Add("Clear chat", () => { CloseSheet(); ClearChat(); });
            stack.Children.Add(buttons);
            stack.Children.Add(AceSettingsView.Build());
            var about = AceCardViews.Text($"Ace {AutoUpdater.CurrentText} for Windows", 11, "MutedBrush");
            about.Margin = new Thickness(0, 14, 0, 0);
            stack.Children.Add(about);
            return stack;
        }

        private FrameworkElement MemoriesSheet()
        {
            var stack = new StackPanel();
            var b = _rt.Brain;
            if (b == null || !b.Ready)
            {
                stack.Children.Add(AceCardViews.Text("Still loading my brain…", 13, "MutedBrush"));
                return stack;
            }
            var hint = AceCardViews.Text("Things you told me. Tell me more like \"my dog's name is Max\" or \"remember that my favorite game is Minecraft\". Click ✕ to make me forget one.", 12, "MutedBrush");
            hint.Margin = new Thickness(0, 0, 0, 10);
            stack.Children.Add(hint);
            var card = AceCardViews.Card(b.NewMemoryCard(), compact: false);
            if (card != null) stack.Children.Add(card);
            return stack;
        }

        private FrameworkElement CalendarSheet()
        {
            var stack = new StackPanel();
            var add = new Button { Content = "Add something", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) };
            add.SetResourceReference(StyleProperty, "HotPillButton");
            add.Click += (_, _) =>
            {
                CloseSheet();
                _input.Text = "add ";
                _input.CaretIndex = _input.Text.Length;
                _input.Focus();
            };
            stack.Children.Add(add);
            var tip = AceCardViews.Text("Like \"add soccer practice tomorrow at 5\" or \"remind me to feed the dog at 6\".", 11.5, "MutedBrush");
            tip.Margin = new Thickness(0, 0, 0, 8);
            stack.Children.Add(tip);

            int shown = 0;
            for (int d = 0; d < 7; d++)
            {
                var day = DateTime.Today.AddDays(d);
                var events = CalendarStore.OnDay(day);
                if (events.Count == 0 && d > 0) continue;
                var head = AceCardViews.Text(d == 0 ? "TODAY · " + day.ToString("ddd MMM d").ToUpperInvariant() : d == 1 ? "TOMORROW" : day.ToString("dddd MMM d").ToUpperInvariant(), 10.5, "AceBrush", bold: true, wrap: false);
                head.Margin = new Thickness(0, 12, 0, 4);
                stack.Children.Add(head);
                if (events.Count == 0) stack.Children.Add(AceCardViews.Text("Nothing planned.", 12.5, "MutedBrush"));
                foreach (var e in events) { stack.Children.Add(AceCardViews.EventRow(e, showDay: false)); shown++; }
            }
            if (shown == 0)
            {
                var none = AceCardViews.Text("Nothing in the next 7 days.", 12.5, "MutedBrush");
                none.Margin = new Thickness(0, 6, 0, 0);
                stack.Children.Add(none);
            }

            var reminders = ReminderStore.Pending().Where(r => !r.IsTimer).OrderBy(r => r.Due).Take(10).ToList();
            if (reminders.Count > 0)
            {
                var head = AceCardViews.Text("REMINDERS", 10.5, "AmberBrush", bold: true, wrap: false);
                head.Margin = new Thickness(0, 16, 0, 4);
                stack.Children.Add(head);
                foreach (var r in reminders)
                    stack.Children.Add(AceCardViews.Text($"{r.Due:ddd h:mm tt}  ·  {r.Text}", 12.5, "FgBrush"));
            }
            return stack;
        }

        private FrameworkElement ScriptsSheet(string filter)
        {
            var stack = new StackPanel();
            var all = ScriptLibrary.All;
            int lua = all.Count(s => s.Lang == "lua");
            stack.Children.Add(AceCardViews.Text(all.Count == 0 ? "The script files are missing. Reinstall Ace to get them back." : $"{lua} Roblox Lua scripts · {all.Count - lua} C++ scripts · works offline", 12, "MutedBrush"));

            var chips = new WrapPanel { Margin = new Thickness(0, 8, 0, 6) };
            var list = new StackPanel();
            var search = new TextBox { Margin = new Thickness(0, 0, 0, 8), Tag = "Search: kill brick, servo, door…" };
            search.SetResourceReference(StyleProperty, "PillTextBox");
            string lang = filter;

            void Fill()
            {
                list.Children.Clear();
                IEnumerable<ScriptEntry> pool = ScriptLibrary.InLang(lang);
                string q = search.Text.Trim();
                List<ScriptEntry> shown;
                if (q.Length == 0) shown = pool.OrderBy(s => s.Lang).ThenBy(s => s.Category).ThenBy(s => s.Name).ToList();
                else
                {
                    var allowed = pool.ToHashSet();
                    shown = ScriptLibrary.Search(q, 60).Where(h => h.Score >= 0.2 && allowed.Contains(h.Script)).Select(h => h.Script).ToList();
                    foreach (var s in pool)
                        if (!shown.Contains(s) && (s.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || s.Summary.Contains(q, StringComparison.OrdinalIgnoreCase)))
                            shown.Add(s);
                }
                if (shown.Count == 0) { list.Children.Add(AceCardViews.Text(all.Count == 0 ? "" : "No scripts match that.", 12.5, "MutedBrush")); return; }
                string lastGroup = "";
                foreach (var s in shown)
                {
                    string group = q.Length > 0 ? "Best matches" : (s.Lang == "lua" ? "Roblox · " : s.Kind + " · ") + s.Category;
                    if (group != lastGroup)
                    {
                        var h = AceCardViews.Text(group.ToUpperInvariant(), 10.5, "MutedBrush", bold: true, wrap: false);
                        h.Margin = new Thickness(0, list.Children.Count == 0 ? 0 : 10, 0, 4);
                        list.Children.Add(h);
                        lastGroup = group;
                    }
                    var script = s;
                    list.Children.Add(AceCardViews.ScriptRow(s, compact: true, _ => ShowScript(script)));
                }
            }

            foreach (var (label, key) in new[] { ("All", ""), ("Roblox Lua", "lua"), ("Arduino", "arduino"), ("C++ games", "console"), ("Unreal", "unreal") })
            {
                var chip = new RadioButton { Content = label, GroupName = "acescripts", Margin = new Thickness(0, 0, 4, 4), IsChecked = key == lang };
                chip.SetResourceReference(StyleProperty, "FilterChip");
                string k = key;
                chip.Checked += (_, _) => { lang = k; Fill(); };
                chips.Children.Add(chip);
            }
            search.TextChanged += (_, _) => Fill();
            stack.Children.Add(chips);
            stack.Children.Add(search);
            stack.Children.Add(list);
            Fill();
            return stack;
        }

        private void ShowScript(ScriptEntry s)
        {
            var stack = new StackPanel();
            var back = new Button { Content = "‹  All scripts", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
            back.SetResourceReference(StyleProperty, "SmallPillButton");
            back.Click += (_, _) => { _sheetTitle.Text = "Scripts library"; _sheetBody.Content = ScriptsSheet(""); };
            stack.Children.Add(back);
            stack.Children.Add(AceCardViews.Code(s, compact: false));
            _sheetTitle.Text = s.Name;
            _sheetBody.Content = stack;
            Spring.PopIn(stack);
        }

        // ===================================================================== Window

        private void OnNavigate(string key)
        {
            Dispatcher.BeginInvoke(() =>
            {
                BringBack();
                switch (key)
                {
                    case "calendar": OpenSheet("calendar"); break;
                    case "scripts": OpenSheet("scripts"); break;
                    case "settings": OpenSheet("settings"); break;
                    case "memories": OpenSheet("memories"); break;
                }
            });
        }

        /// <summary>Shows the window again (from the pop-up, the taskbar or a second start of Ace).</summary>
        public void BringBack()
        {
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            _input.Focus();
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _sheetLayer.Visibility == Visibility.Visible) { e.Handled = true; CloseSheet(); }
            else if (e.Key == Key.OemComma && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { e.Handled = true; OpenSheet("settings"); }
            else if (e.Key == Key.L && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { e.Handled = true; ClearChat(); }
        }

        private void UpdateVisible() => _rt.PageVisible = IsVisible && IsActive && WindowState != WindowState.Minimized;

        private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private FrameworkElement RoundIcon(string glyph, string tip, Action click)
        {
            var b = CircleButton(glyph, tip, 40, transparent: false);
            b.Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
            b.BorderBrush = new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));
            ((TextBlock)b.Content).Foreground = new SolidColorBrush(AceOrb.WithA(AceOrb.Fg, 0.85));
            b.Click += (_, _) => click();
            return b;
        }

        /// <summary>A round icon button. The send button (not transparent) is Ace blue with a black arrow, like the iPad.</summary>
        private static Button CircleButton(string glyph, string tip, double size, bool transparent)
        {
            var text = new TextBlock
            {
                Text = glyph, FontFamily = Icons, FontSize = size * 0.4,
                Foreground = transparent ? new SolidColorBrush(AceOrb.WithA(AceOrb.Fg, 0.8)) : Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                FontWeight = transparent ? FontWeights.Normal : FontWeights.Bold,
            };
            var b = new Button
            {
                Content = text, ToolTip = tip, Width = size, Height = size, Cursor = Cursors.Hand, Focusable = false,
                Background = transparent ? Brushes.Transparent : new SolidColorBrush(Blue),
                BorderBrush = Brushes.Transparent,
                Margin = new Thickness(3, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Template = CircleTemplate(),
            };
            return b;
        }

        private static ControlTemplate CircleTemplate()
        {
            var t = new ControlTemplate(typeof(Button));
            var grid = new FrameworkElementFactory(typeof(Grid));
            var circle = new FrameworkElementFactory(typeof(Border), "bd");
            circle.SetValue(Border.CornerRadiusProperty, new CornerRadius(999));
            circle.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            circle.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            circle.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            grid.AppendChild(circle);
            var hoverGlow = new FrameworkElementFactory(typeof(Border), "hv");
            hoverGlow.SetValue(Border.CornerRadiusProperty, new CornerRadius(999));
            hoverGlow.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)));
            hoverGlow.SetValue(UIElement.OpacityProperty, 0.0);
            grid.AppendChild(hoverGlow);
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            grid.AppendChild(cp);
            t.VisualTree = grid;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0, "hv"));
            t.Triggers.Add(hover);
            var press = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            press.Setters.Add(new Setter(UIElement.RenderTransformProperty, new ScaleTransform(0.92, 0.92), "bd"));
            press.Setters.Add(new Setter(UIElement.RenderTransformOriginProperty, new Point(0.5, 0.5), "bd"));
            t.Triggers.Add(press);
            return t;
        }

        private static Button WindowButton(string glyph, string tip, Action click, bool danger)
        {
            var text = new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 10, Foreground = new SolidColorBrush(AceOrb.WithA(AceOrb.Fg, 0.75)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var b = new Button { Content = text, ToolTip = tip, Width = 44, Height = 32, Focusable = false, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent };
            var t = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border), "bd");
            bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            t.VisualTree = bd;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, danger ? new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C)) : new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)), "bd"));
            t.Triggers.Add(hover);
            b.Template = t;
            b.Click += (_, _) => click();
            return b;
        }

        // ---------------------------------------------------------------- Windows 11 touches

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern bool FlashWindowEx(ref FLASHWINFO info);

        [StructLayout(LayoutKind.Sequential)]
        private struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }

        private void RoundCorners()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int round = 2;   // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));   // DWMWA_WINDOW_CORNER_PREFERENCE (Windows 11)
                int dark = 1;
                DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));    // DWMWA_USE_IMMERSIVE_DARK_MODE
            }
            catch { }
        }

        private void FlashTaskbar()
        {
            try
            {
                var info = new FLASHWINFO { hwnd = new WindowInteropHelper(this).Handle, dwFlags = 2 | 12, uCount = 3, dwTimeout = 0 };   // tray, until focused
                info.cbSize = (uint)Marshal.SizeOf(info);
                FlashWindowEx(ref info);
            }
            catch { }
        }
    }
}
