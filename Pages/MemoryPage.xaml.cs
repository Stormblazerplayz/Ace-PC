using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Nimbus.Motion;
using Nimbus.Services;

namespace Nimbus.Pages
{
    /// <summary>A row on the app list (wraps AppGroup so Trim/Close can update it).</summary>
    public sealed class AppRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public AppRow(AppGroup g) { Group = g; }
        public AppGroup Group { get; private set; }
        public string Name => Group.Name;
        public string CountText => Group.Count > 1 ? $"×{Group.Count}" : "";
        public string CommitText => $"{Group.CommitGb:0.0} GB";
        public string RamText => Group.RamMb >= 1000 ? $"{Group.RamMb / 1024:0.0} GB in RAM" : $"{Group.RamMb:0} MB in RAM";
        public bool HasWindow => Group.HasWindow;

        private bool _sure;
        /// <summary>First click shows "Sure?", second click closes.</summary>
        public bool Sure { get => _sure; set { _sure = value; Changed(nameof(CloseLabel)); } }
        public string CloseLabel => Sure ? "Sure?" : "Close";

        public void Update(AppGroup g) { Group = g; Changed(string.Empty); }
        private void Changed(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public partial class MemoryPage : UserControl, INimbusPage
    {
        public ObservableCollection<AppRow> Rows { get; } = new();
        private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(5) };
        private readonly DropShadowEffect _glow = new() { Color = Color.FromRgb(0x45, 0xD4, 0x83), BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9 };
        private bool _loading;

        public MemoryPage()
        {
            InitializeComponent();
            DataContext = Hub.Stats;
            AppList.ItemsSource = Rows;
            _refresh.Tick += (_, _) => _ = LoadApps();
            Loaded += (_, _) => { Hub.Stats.PropertyChanged += Stats_Changed; _refresh.Start(); Stats_Changed(null, null); };
            Unloaded += (_, _) => { Hub.Stats.PropertyChanged -= Stats_Changed; _refresh.Stop(); };
        }

        public Panel PopRoot => Root;

        public void OnShown() => _ = LoadApps();

        // ---------- live bars and graph ----------

        private void Stats_Changed(object? sender, PropertyChangedEventArgs? e)
        {
            var s = Hub.Stats;
            SpringBar(CommitScale, s.CommitPercent);
            SpringBar(RamScale, s.MemPercent);
            CommitFill.Background = (Brush)FindResource(s.CommitHigh ? "WarnBrush" : "GoodBrush");
            CommitNote.Visibility = s.CommitHigh ? Visibility.Visible : Visibility.Collapsed;
            DrawGraph();
        }

        private static void SpringBar(ScaleTransform bar, double percent)
        {
            double t = Math.Max(0, Math.Min(1, percent / 100));
            if (Math.Abs(bar.ScaleX - t) > 0.004) Spring.To(bar, ScaleTransform.ScaleXProperty, t, Spring.DurationMs, Spring.Medium);
        }

        private void Graph_SizeChanged(object sender, SizeChangedEventArgs e) => DrawGraph();

        private void KeepFree_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (KeepText == null || KeepValue == null || Graph == null) return;   // still loading
            KeepText.Text = $"Keeping {KeepFree.Value:0.0} GB free";
            KeepValue.Text = $"{KeepFree.Value:0.0} GB";
            DrawGraph();
            // Hook your memory keeper here: it should trim when free RAM drops under KeepFree.Value.
        }

        private void DrawGraph()
        {
            double w = Graph.ActualWidth, h = Graph.ActualHeight;
            if (w <= 0 || h <= 0) return;
            double max = Math.Max(Hub.Stats.MemTotalGb, 1);
            var data = Hub.Stats.FreeHistory;
            double Y(double gb) => h - gb / max * (h - 4) - 2;
            var pts = new PointCollection();
            for (int i = 0; i < data.Count; i++) pts.Add(new Point(i * w / Math.Max(1, data.Count - 1), Y(data[i])));
            GraphLine.Points = pts;
            GraphFill.Points = new PointCollection(pts) { new Point(w, h), new Point(0, h) };
            TargetLine.X2 = w;
            TargetLine.Y1 = TargetLine.Y2 = Y(KeepFree.Value);
            bool under = data.Count > 0 && data[^1] > 0 && data[^1] < KeepFree.Value;
            GraphLine.Effect = under ? _glow : null;
        }

        // ---------- app list ----------

        private async Task LoadApps()
        {
            if (_loading) return;
            _loading = true;
            try
            {
                var groups = await Task.Run(() => Processes.TopGroups(8));
                bool first = Rows.Count == 0;
                // update rows in place so nothing jumps around
                for (int i = 0; i < groups.Count; i++)
                {
                    var match = Rows.FirstOrDefault(r => r.Name == groups[i].Name);
                    if (match != null) { match.Update(groups[i]); int from = Rows.IndexOf(match); if (from != i) Rows.Move(from, i); }
                    else Rows.Insert(i, new AppRow(groups[i]));
                }
                while (Rows.Count > groups.Count) Rows.RemoveAt(Rows.Count - 1);
                if (first) PopRows();
            }
            catch { /* reading processes failed this time; try again next tick */ }
            finally { _loading = false; }
        }

        private void PopRows()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                int i = 0;
                foreach (var r in Rows)
                    if (AppList.ItemContainerGenerator.ContainerFromItem(r) is UIElement el) Spring.PopIn(el, i++);
            }), DispatcherPriority.Loaded);
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            Rows.Clear();
            _ = LoadApps();
        }

        private async void Trim_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not AppRow row) return;
            double mb = await Task.Run(() => Processes.Trim(row.Name));
            GaveBack.Text = mb >= 1
                ? $"Gave memory back just now: {row.Name} {(mb >= 1000 ? $"{mb / 1024:0.0} GB" : $"{mb:0} MB")}"
                : $"{row.Name} didn't have spare memory to give back.";
            Spring.Bump(GaveBack);
            await LoadApps();
        }

        private async void Close_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not AppRow row) return;
            if (!row.Sure)
            {
                row.Sure = true;   // ask once, then reset after 3 seconds
                await Task.Delay(3000);
                row.Sure = false;
                return;
            }
            row.Sure = false;
            await Task.Run(() => Processes.CloseWindows(row.Name));
            await Task.Delay(800);
            await LoadApps();
        }
    }
}
