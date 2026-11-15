using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Nimbus.AceV4;
using Nimbus.Motion;
using Nimbus.Overlay;
using Nimbus.Pages;
using Nimbus.Services;

namespace Nimbus
{
    public partial class MainWindow : Window
    {
        // Gap between the see-through window edge and the visible Shell (room for the shadow and the bounce).
        private const double Pad = 20;
        private const double Overshoot = 40;
        private const double IslandWidth = 480, IslandHeight = 50;

        private readonly Dictionary<string, Func<UserControl>> _factories = new()
        {
            ["glance"] = () => new GlancePage(),
            ["ace"] = () => new AcePage(),
            ["scripts"] = () => new ScriptsPage(),
            ["acechat"] = () => new AceChatPage(),
            ["calendar"] = () => new CalendarPage(),
            ["alerts"] = () => new AlertsPage(),
            ["audio"] = () => new AudioPage(),
            ["clips"] = () => new ClipsPage(),
            ["memory"] = () => new MemoryPage(),
            ["games"] = () => new GamesPage(),
            ["orbit"] = () => new OrbitPage(),
            ["settings"] = () => new SettingsPage(),
        };
        private readonly Dictionary<string, UserControl> _pages = new();
        private string _current = "";

        private Rect _normalShell;          // where the full window sits on screen (Shell only, no padding)
        private bool _islanded, _animating;
        private bool _wasTopmost;
        private AceOverlay? _overlay;

        /// <summary>True while the window is the little island pill at the top of the screen.</summary>
        public bool IsIslanded => _islanded;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = Hub.Stats;
            Hub.Start();
            AceRuntime.Current.Start();   // Ace V4: wake-up checks, greeting, bedtime, "Hey Ace", sounds
            Hub.NavigateRequested += Navigate;
            Hub.AlertsChanged += UpdateBadge;
            SourceInitialized += (_, _) => AceRuntime.Current.AttachWindow(this);
            Closed += (_, _) =>
            {
                Hub.NavigateRequested -= Navigate;
                Hub.AlertsChanged -= UpdateBadge;
                _overlay?.Close();
            };
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            NavGlance.IsChecked = true;
            _ = GetPage("alerts");   // so the badge shows the real alert count right away
            UpdateBadge();

            // Ace V4's eyes pod, which slides out of the island when Ace wakes up.
            _overlay = new AceOverlay { IsIslanded = () => _islanded };

            // Started with Windows ("--background"): go straight to the island instead of opening the full window.
            if (Environment.GetCommandLineArgs().Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase)))
            {
                Opacity = 0;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ToIsland();
                    var show = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
                    show.Tick += (_, _) => { show.Stop(); Opacity = 1; };
                    show.Start();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }

        // ================= Tabs =================

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton tab || tab.Tag is not string key) return;
            ShowPage(key);
            MoveIndicator(tab);
        }

        private void MoveIndicator(RadioButton tab)
        {
            double y = tab.TranslatePoint(new Point(0, 0), NavList).Y;
            if (NavIndicator.Opacity < 0.5)
            {
                NavShift.Y = y;   // coming back from Settings: appear in place, then pop
                Spring.To(NavIndicator, OpacityProperty, 1, 200);
                Spring.Bump(NavIndicator);
            }
            else Spring.To(NavShift, TranslateTransform.YProperty, y, Spring.DurationMs, Spring.Medium);
        }

        private void ShowPage(string key)
        {
            if (key == _current) return;
            _current = key;
            var page = GetPage(key);
            PageHost.Content = page;
            if (page is INimbusPage p)
            {
                Spring.PopChildren(p.PopRoot);
                p.OnShown();
            }
        }

        private UserControl GetPage(string key)
        {
            if (!_pages.TryGetValue(key, out var page))
            {
                page = _factories[key]();
                _pages[key] = page;
            }
            return page;
        }

        /// <summary>Opens a tab by name. "settings" opens Settings with no tab highlighted.</summary>
        public void Navigate(string key)
        {
            if (_islanded) FromIsland();
            var tab = NavList.Children.OfType<RadioButton>().FirstOrDefault(t => (string)t.Tag == key);
            if (tab != null) { tab.IsChecked = true; return; }
            if (!_factories.ContainsKey(key)) return;   // "settings" and "acechat" have no tab of their own
            foreach (var t in NavList.Children.OfType<RadioButton>()) t.IsChecked = false;
            Spring.To(NavIndicator, OpacityProperty, 0, 160);
            ShowPage(key);
        }

        private void UpdateBadge()
        {
            int n = Hub.AlertCount;
            AlertBadgeText.Text = n.ToString();
            AlertBadge.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (n > 0) Spring.Bump(AlertBadge);
        }

        // ================= Title bar =================

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1 && !_islanded && !_animating) DragMove();
        }

        private void Gear_Click(object sender, RoutedEventArgs e) => Navigate("settings");

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void RecButton_Click(object sender, RoutedEventArgs e)
        {
            // Hook your recorder here: RecButton.IsChecked == true means "recording".
            Spring.Bump(RecButton);
        }

        private void Camera_Click(object sender, RoutedEventArgs e)
        {
            // Hook your screenshot code here. This just flashes the window like a camera.
            Spring.Run(Flash, OpacityProperty, 0.35, 0, 0, 380, null);
        }

        private void Shell_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double r = Math.Min(21, Math.Min(Shell.ActualWidth, Shell.ActualHeight) / 2 - 1);
            ShellClip.Rect = new Rect(0, 0, Math.Max(0, Shell.ActualWidth - 2), Math.Max(0, Shell.ActualHeight - 2));
            ShellClip.RadiusX = ShellClip.RadiusY = Math.Max(0, r);
        }

        // ================= Island =================
        // The window grows to cover both the full window and the pill, Shell springs between them,
        // then the window shrinks to fit the new shape. Clicks pass through the see-through parts.

        private void Collapse_Click(object sender, RoutedEventArgs e) => ToIsland();

        private void Island_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => FromIsland();

        private void ToIsland()
        {
            if (_islanded || _animating) return;
            _animating = true;
            _normalShell = new Rect(Left + Pad, Top + Pad, Shell.Width, Shell.Height);
            var work = SystemParameters.WorkArea;
            var pill = new Rect(work.Left + (work.Width - IslandWidth) / 2, work.Top + 10, IslandWidth, IslandHeight);

            SpringShell(_normalShell, pill, () =>
            {
                MainContent.Visibility = Visibility.Collapsed;
                _wasTopmost = Topmost;
                Topmost = true;
                _islanded = true;
                _animating = false;
            });
            Spring.Run(MainContent, OpacityProperty, 1, 0, 0, 140, null);
            IslandContent.Visibility = Visibility.Visible;
            Spring.Run(IslandContent, OpacityProperty, 0, 1, 220, 260, null);
            Shell.CornerRadius = new CornerRadius(IslandHeight / 2);
        }

        private void FromIsland()
        {
            if (!_islanded || _animating) return;
            _animating = true;
            var pill = new Rect(Left + Pad, Top + Pad, Shell.Width, Shell.Height);
            MainContent.Visibility = Visibility.Visible;
            Spring.Run(MainContent, OpacityProperty, 0, 1, 160, 240, null);
            Spring.Run(IslandContent, OpacityProperty, 1, 0, 0, 120, null);
            Topmost = _wasTopmost;

            SpringShell(pill, _normalShell, () =>
            {
                IslandContent.Visibility = Visibility.Collapsed;
                Shell.CornerRadius = new CornerRadius(22);
                _islanded = false;
                _animating = false;
                if (PageHost.Content is INimbusPage p) Spring.PopChildren(p.PopRoot);
            });
        }

        /// <summary>Springs Shell from one on-screen rectangle to another.</summary>
        private void SpringShell(Rect from, Rect to, Action done)
        {
            var union = Rect.Union(from, to);
            union.Inflate(Pad + Overshoot, Pad + Overshoot);
            MoveWindow(union);

            Canvas.SetLeft(Shell, from.X - union.X);
            Canvas.SetTop(Shell, from.Y - union.Y);
            Shell.Width = from.Width;
            Shell.Height = from.Height;

            double ms = 720;
            var ease = Spring.Soft;
            Spring.Run(Shell, Canvas.LeftProperty, from.X - union.X, to.X - union.X, 0, ms, ease);
            Spring.Run(Shell, Canvas.TopProperty, from.Y - union.Y, to.Y - union.Y, 0, ms, ease);
            Spring.Run(Shell, WidthProperty, from.Width, to.Width, 0, ms, ease);
            Spring.Run(Shell, HeightProperty, from.Height, to.Height, 0, ms, ease, () =>
            {
                var final = to;
                final.Inflate(Pad, Pad);
                MoveWindow(final);
                Canvas.SetLeft(Shell, Pad);
                Canvas.SetTop(Shell, Pad);
                done();
            });
        }

        /// <summary>Moves and resizes the window in one step, so it doesn't flicker.</summary>
        private void MoveWindow(Rect dip)
        {
            var source = PresentationSource.FromVisual(this);
            var hwnd = new WindowInteropHelper(this).Handle;
            if (source?.CompositionTarget == null || hwnd == IntPtr.Zero)
            {
                Left = dip.X; Top = dip.Y; Width = dip.Width; Height = dip.Height;
                return;
            }
            var m = source.CompositionTarget.TransformToDevice;
            SetWindowPos(hwnd, IntPtr.Zero,
                (int)Math.Round(dip.X * m.M11), (int)Math.Round(dip.Y * m.M22),
                (int)Math.Round(dip.Width * m.M11), (int)Math.Round(dip.Height * m.M22),
                SWP_NOZORDER | SWP_NOACTIVATE);
        }

        private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }
}
