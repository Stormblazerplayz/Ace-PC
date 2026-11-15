using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Nimbus.AceV4;
using Nimbus.AceV4.Audio;
using Nimbus.Controls;
using Nimbus.Motion;

namespace Nimbus.Overlay
{
    /// <summary>
    /// The always-on-top pod at the top of the screen. When Ace wakes up, the pod slides down out of the island with
    /// two digital eyes that look around while it checks things, then shows what it found (or just blinks and goes
    /// back in when everything's fine). It never steals focus from what you're doing.
    /// </summary>
    public sealed class AceOverlay : Window
    {
        private const double PodWidth = 420;
        private readonly Border _pod;
        private readonly AceEyes _eyes;
        private readonly TextBlock _status;
        private readonly TextBlock _heard;
        private readonly StackPanel _answerHost;
        private readonly ScrollViewer _answerScroll;
        private readonly TranslateTransform _podShift = new();
        private readonly ScaleTransform _podScale = new(1, 1);
        private readonly DispatcherTimer _hideTimer;
        private bool _shown, _hiding;
        private AceAnswer? _current;

        /// <summary>Tells the overlay whether the main window is the island right now, so the pod sits under it.</summary>
        public Func<bool> IsIslanded { get; set; } = () => false;

        public AceOverlay()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;
            Width = PodWidth + 60;
            Height = 640;
            Title = "Ace";
            SetResourceReference(FontFamilyProperty, "BodyFont");
            SetResourceReference(ForegroundProperty, "FgBrush");

            _eyes = new AceEyes { Width = 132, Height = 50, Margin = new Thickness(0, 4, 0, 2), HorizontalAlignment = HorizontalAlignment.Center };
            _eyes.Looked += x =>
            {
                AceRuntime.Current.EyePan = x;
                AceRuntime.Current.Play(Sfx.Look, 0.35);
            };
            _eyes.Blinked += () => { if (_shown) AceRuntime.Current.Play(Sfx.Blink, 0.18); };

            _status = AceCardViews.Text("", 12.5, "MutedBrush", bold: true);
            _status.HorizontalAlignment = HorizontalAlignment.Center;
            _status.TextAlignment = TextAlignment.Center;
            _status.Margin = new Thickness(0, 2, 0, 4);

            _heard = AceCardViews.Text("", 12.5, "AceBrush");
            _heard.FontStyle = FontStyles.Italic;
            _heard.HorizontalAlignment = HorizontalAlignment.Center;
            _heard.TextAlignment = TextAlignment.Center;
            _heard.Visibility = Visibility.Collapsed;
            _heard.Margin = new Thickness(0, 0, 0, 6);

            _answerHost = new StackPanel();
            _answerScroll = new ScrollViewer
            {
                Content = _answerHost,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 430,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 2, 0, 0),
            };

            // Top row: a little "open" and "close" button on each side of the eyes.
            var open = Mini("↗", "Open Ace");
            open.Click += (_, _) => { OpenAcePage(); HidePod(); };
            var close = Mini("✕", "Hide");
            close.Click += (_, _) => HidePod();
            var mic = Mini("🎤", "Talk to Ace (Ctrl+Alt+Space)");
            mic.Click += (_, _) => _ = AceRuntime.Current.ListenAndAskAsync("overlay");
            var left = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            left.Children.Add(mic);
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            right.Children.Add(open);
            right.Children.Add(close);
            var top = new Grid();
            top.Children.Add(_eyes);
            top.Children.Add(left);
            top.Children.Add(right);

            var content = new StackPanel();
            content.Children.Add(top);
            content.Children.Add(_status);
            content.Children.Add(_heard);
            content.Children.Add(_answerScroll);

            _pod = new Border
            {
                Width = PodWidth,
                CornerRadius = new CornerRadius(26),
                Padding = new Thickness(12, 8, 12, 12),
                Background = new SolidColorBrush(Color.FromArgb(0xF4, 0x08, 0x08, 0x0E)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x5B, 0xB6, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 6, 0, 30),
                Child = content,
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.5, Color = Colors.Black },
                RenderTransformOrigin = new Point(0.5, 0),
                Opacity = 0,
            };
            var group = new TransformGroup();
            group.Children.Add(_podScale);
            group.Children.Add(_podShift);
            _pod.RenderTransform = group;
            _pod.MouseEnter += (_, _) => _hideTimer?.Stop();
            _pod.MouseLeave += (_, _) => ArmHide(4);

            Content = new Grid { Children = { _pod } };

            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); if (!HasPending() && !_pod.IsMouseOver) HidePod(); else ArmHide(5); };

            AceRuntime.Current.Stage += OnStage;
            AceRuntime.Current.Speech.Word += _ => Dispatcher.BeginInvoke(() => _eyes.TalkPulse());
            AceCardViews.CardSaid += said => Dispatcher.BeginInvoke(() =>
            {
                if (!_shown) return;
                _status.Text = said;
                _status.Visibility = Visibility.Visible;
                Spring.Bump(_status);
                ArmHide(5);
            });
            Closed += (_, _) => AceRuntime.Current.Stage -= OnStage;
            SourceInitialized += (_, _) => MakeToolWindow();
            Visibility = Visibility.Hidden;
        }

        private static Button Mini(string glyph, string tip)
        {
            var b = new Button { Content = glyph, ToolTip = tip, Width = 30, Height = 30, Padding = new Thickness(0), FontSize = 12, Margin = new Thickness(2, 0, 2, 0) };
            b.SetResourceReference(StyleProperty, "IconButton");
            return b;
        }

        // ===================================================================== Stage events

        private void OnStage(StageEvent e) => Dispatcher.BeginInvoke(() => Handle(e));

        private void Handle(StageEvent e)
        {
            switch (e.Kind)
            {
                case StageKind.Wake:
                    if (AceRuntime.Current.PageVisible) return;   // the Ace window is open and in front: its orb shows it
                    _current = null;
                    _answerHost.Children.Clear();
                    _answerScroll.Visibility = Visibility.Collapsed;
                    _heard.Visibility = Visibility.Collapsed;
                    SetStatus(e.Text);
                    _eyes.Mood = AceEyes.Moods.Normal;
                    _eyes.Wander = e.LookAround;
                    ShowPod();
                    _hideTimer.Stop();
                    break;

                case StageKind.Status:
                    if (!_shown) return;
                    SetStatus(e.Text);
                    // While the eyes look around, a check is still running: stay out until it's done.
                    ArmHide(_eyes.Wander ? 90 : e.Text.Length > 0 ? 10 : 3);
                    break;

                case StageKind.Mood:
                    if (!_shown) return;
                    _eyes.Mood = ToMood(e.Text);
                    if (_eyes.Mood == AceEyes.Moods.Think) { _eyes.Wander = false; SetStatus("Thinking…"); }
                    _hideTimer.Stop();
                    break;

                case StageKind.Heard:
                    if (!_shown) { if (AceRuntime.Current.PageVisible) return; ShowPod(); }
                    _heard.Text = "“" + e.Text + "”";
                    _heard.Visibility = Visibility.Visible;
                    Spring.PopIn(_heard);
                    _eyes.Mood = AceEyes.Moods.Think;
                    _eyes.Level = 0;
                    SetStatus("Thinking…");
                    _hideTimer.Stop();
                    break;

                case StageKind.Level:
                    if (!_shown) { if (AceRuntime.Current.PageVisible) return; ShowPod(); }
                    _eyes.Wander = false;
                    _eyes.Mood = AceEyes.Moods.Listen;
                    _eyes.Level = e.Value;
                    _hideTimer.Stop();
                    break;

                case StageKind.Answer:
                    if (e.Answer == null) return;
                    if (!_shown) { if (AceRuntime.Current.PageVisible) return; ShowPod(); }
                    ShowAnswer(e.Answer);
                    break;

                case StageKind.Sleep:
                    if (!_shown) return;
                    _eyes.Wander = false;
                    _eyes.Blink();
                    // A beat to see "All good!", then back into the island.
                    ArmHide(_status.Text.Length > 0 ? 1.6 : 0.3);
                    break;
            }
        }

        private void SetStatus(string text)
        {
            _status.Text = text;
            _status.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (text.Length > 0) Spring.Bump(_status);
        }

        private void ShowAnswer(AceAnswer a)
        {
            _current = a;
            _eyes.Wander = false;
            _eyes.Level = 0;
            _eyes.Mood = ToMood(a.Mood);
            _eyes.Hop();
            SetStatus("");
            _answerHost.Children.Clear();
            var view = AceCardViews.Answer(a, compact: true);
            _answerHost.Children.Add(view);
            _answerScroll.Visibility = Visibility.Visible;
            _answerScroll.ScrollToTop();
            Spring.PopIn(view);
            // Longer answers stay up longer. Cards that wait for you stay until they're answered.
            double seconds = Math.Clamp(6 + a.Text.Length * 0.05 + a.Cards.Count * 4, 6, 30);
            ArmHide(seconds);
            // Back to calm eyes after a moment.
            var calm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            calm.Tick += (_, _) => { calm.Stop(); if (_current == a && _eyes.Mood is AceEyes.Moods.Happy or AceEyes.Moods.Love) _eyes.Mood = AceEyes.Moods.Normal; };
            calm.Start();
        }

        private bool HasPending() =>
            _current != null && (_current.Cards.OfType<ConfirmCard>().Any(c => !c.Answered) || _current.Cards.OfType<CountdownCard>().Any(c => !c.Done))
            || AceRuntime.Current.Listening;

        private void ArmHide(double seconds)
        {
            _hideTimer.Stop();
            _hideTimer.Interval = TimeSpan.FromSeconds(Math.Max(0.2, seconds));
            _hideTimer.Start();
        }

        private static AceEyes.Moods ToMood(string mood) => mood switch
        {
            "think" => AceEyes.Moods.Think,
            "alert" => AceEyes.Moods.Alert,
            "sleepy" => AceEyes.Moods.Sleepy,
            "love" => AceEyes.Moods.Love,
            "confused" => AceEyes.Moods.Confused,
            "listen" => AceEyes.Moods.Listen,
            "normal" => AceEyes.Moods.Normal,
            _ => AceEyes.Moods.Happy,
        };

        // ===================================================================== Showing and hiding

        private void PlaceAtTop()
        {
            var work = SystemParameters.WorkArea;
            Left = work.Left + (work.Width - Width) / 2;
            Top = work.Top + (IsIslanded() ? 66 : 4);
            Height = Math.Min(640, work.Height - (Top - work.Top));
        }

        public void ShowPod()
        {
            _hiding = false;
            PlaceAtTop();
            if (!IsVisible) Show();
            if (_shown) { Spring.Bump(_pod); return; }
            _shown = true;
            _pod.BeginAnimation(OpacityProperty, null);
            // Slides down out of the island: starts squished up at the top, then springs open.
            Spring.Run(_pod, OpacityProperty, 0, 1, 0, 180, null);
            Spring.Run(_podShift, TranslateTransform.YProperty, -46, 0, 0, 640, Spring.Ease);
            Spring.Run(_podScale, ScaleTransform.ScaleXProperty, 0.35, 1, 0, 640, Spring.Ease);
            Spring.Run(_podScale, ScaleTransform.ScaleYProperty, 0.12, 1, 0, 640, Spring.Ease);
        }

        public void HidePod()
        {
            _hideTimer.Stop();
            if (!_shown || _hiding) return;
            _hiding = true;
            _eyes.Wander = false;
            AceRuntime.Current.Play(Sfx.EyesIn, 0.6);
            Spring.Run(_podScale, ScaleTransform.ScaleXProperty, 1, 0.35, 0, 260, null);
            Spring.Run(_podScale, ScaleTransform.ScaleYProperty, 1, 0.1, 0, 260, null);
            Spring.Run(_podShift, TranslateTransform.YProperty, 0, -40, 0, 260, null);
            Spring.Run(_pod, OpacityProperty, 1, 0, 60, 200, null, () =>
            {
                if (!_hiding) return;   // it was shown again mid-way
                _shown = false;
                _hiding = false;
                _current = null;
                _answerHost.Children.Clear();
                Hide();
            });
        }

        private static void OpenAcePage()
        {
            var main = Application.Current?.MainWindow;
            if (main != null)
            {
                if (!main.IsVisible) main.Show();
                if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
                main.Activate();
            }
            Services.Hub.Navigate("ace");
        }

        // ===================================================================== Never steal focus

        private const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

        private void MakeToolWindow()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
                SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
            }
            catch { }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            // Clicking the pod's background (not a button or a card) opens the Ace page.
            if (e.OriginalSource == _pod || e.OriginalSource is AceEyes) { OpenAcePage(); HidePod(); }
        }
    }
}
