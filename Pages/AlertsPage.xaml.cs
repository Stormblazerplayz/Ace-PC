using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nimbus.Motion;
using Nimbus.Services;

namespace Nimbus.Pages
{
    /// <summary>One alert. Kind is used by the filter chips: build, print, system or clip.</summary>
    public sealed class AlertItem
    {
        public string Title { get; init; } = "";
        public string Detail { get; init; } = "";
        public string When { get; init; } = "";
        public string Action { get; init; } = "Open";
        public string Kind { get; init; } = "system";
        public Brush Tone { get; init; } = Brushes.Gray;
        /// <summary>Tab to open when the action button is pressed, or null.</summary>
        public string? Opens { get; init; }
    }

    public partial class AlertsPage : UserControl, INimbusPage
    {
        /// <summary>Add your real alerts here (call Add from anywhere on the UI thread).</summary>
        public ObservableCollection<AlertItem> Items { get; } = new();
        private string _filter = "all";

        public AlertsPage()
        {
            InitializeComponent();
            AlertList.ItemsSource = Items;
            Items.CollectionChanged += (_, _) => Refresh();
            // Sample alerts, matching the design
            Add(new AlertItem { Title = "Farside build failed", Detail = "All jobs stopped in 4 seconds on main", When = "2d ago", Action = "Logs", Kind = "build", Tone = Res("BadBrush") });
            Add(new AlertItem { Title = "Committed memory at 83%", Detail = "A game plus clips and models may start to page", When = "5m ago", Action = "Memory", Kind = "system", Tone = Res("WarnBrush"), Opens = "memory" });
            Add(new AlertItem { Title = "ShipHull_v2 is 68% done", Detail = "About 1h 12m left on the Bambu A1", When = "now", Action = "Camera", Kind = "print", Tone = Res("AceBrush") });
            Add(new AlertItem { Title = "Clip saved", Detail = "clip_20261029_151034.mp4 · 12 MB", When = "3h ago", Action = "Open", Kind = "clip", Tone = Res("GoodBrush"), Opens = "clips" });
            Hub.ClearAlertsRequested += ClearAll;
        }

        public Panel PopRoot => Root;

        public void OnShown() { }

        private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

        public void Add(AlertItem item)
        {
            Items.Insert(0, item);
            if (IsLoaded) Nova.Jump();
        }

        private void Refresh()
        {
            int n = Items.Count;
            CountText.Text = n == 1 ? "1 waiting · Nova sorts them for you" : $"{n} waiting · Nova sorts them for you";
            EmptyState.Visibility = n == 0 ? Visibility.Visible : Visibility.Collapsed;
            Root.Visibility = n == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (n == 0 && IsLoaded) Spring.PopIn(EmptyState);
            Hub.SetAlertCount(n);
        }

        private void Filter_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: string f } || AlertList == null) return;
            _filter = f;
            AlertList.Items.Filter = o => _filter == "all" || ((AlertItem)o).Kind == _filter;
            // pop the ones that are left, once WPF has made their rows
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                int i = 0;
                foreach (var item in AlertList.Items)
                    if (AlertList.ItemContainerGenerator.ContainerFromItem(item) is UIElement el) Spring.PopIn(el, i++);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void Dismiss_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not AlertItem item) return;
            if (AlertList.ItemContainerGenerator.ContainerFromItem(item) is UIElement el)
                Spring.Out(el, () => Items.Remove(item));
            else Items.Remove(item);
        }

        private void Action_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is AlertItem { Opens: string page }) Hub.Navigate(page);
            // Hook "Logs" and "Camera" to your own code here.
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e) => ClearAll();

        private void ClearAll()
        {
            var all = Items.ToList();
            if (all.Count == 0) return;
            for (int i = 0; i < all.Count; i++)
            {
                var item = all[i];
                var el = AlertList.ItemContainerGenerator.ContainerFromItem(item) as UIElement;
                void Done() { Items.Remove(item); }
                if (el == null || !IsLoaded) { Done(); continue; }
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(70 * i) };
                timer.Tick += (_, _) => { timer.Stop(); Spring.Out(el, Done); };
                timer.Start();
            }
        }
    }
}
