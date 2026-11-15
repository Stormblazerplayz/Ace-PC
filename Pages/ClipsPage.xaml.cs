using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Nimbus.Motion;

namespace Nimbus.Pages
{
    public sealed record ClipItem(string Name, string Meta);

    public partial class ClipsPage : UserControl, INimbusPage
    {
        public ObservableCollection<ClipItem> Clips { get; } = new()
        {
            new("clip_20261029_151034.mp4", "3h ago · 12 MB"),
            new("clip_20261004_165013.mp4", "Oct 4 · 11 MB"),
            new("clip_20261004_164333.mp4", "Oct 4 · 11 MB"),
            new("clip_20261004_164133.mp4", "Oct 4 · 12 MB"),
            new("clip_20261004_163645.mp4", "Oct 4 · 10 MB"),
            new("clip_20261004_163440.mp4", "Oct 4 · 12 MB"),
        };

        private bool _open;
        private string _buf = "25s", _q = "720p", _fps = "60fps";

        public ClipsPage()
        {
            InitializeComponent();
            ClipGrid.ItemsSource = Clips;
            var pulse = new DoubleAnimation(1, 0.25, new Duration(TimeSpan.FromSeconds(0.7))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            LiveDot.BeginAnimation(OpacityProperty, pulse);
        }

        public Panel PopRoot => Root;

        public void OnShown() { }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // Hook your replay buffer here. This adds a sample clip so you can see the pop.
            var name = "clip_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4";
            var clip = new ClipItem(name, "just now · 9 MB");
            Clips.Insert(0, clip);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ClipGrid.ItemContainerGenerator.ContainerFromItem(clip) is UIElement el) Spring.PopIn(el);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void Shot_Click(object sender, RoutedEventArgs e)
        {
            // Hook your screenshot code here.
            Spring.Bump(BufText);
        }

        private void Folder_Click(object sender, RoutedEventArgs e)
        {
            var path = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true }); }
            catch { }
        }

        private void CaptureToggle_Click(object sender, RoutedEventArgs e)
        {
            _open = !_open;
            Spring.To(CaretTurn, RotateTransform.AngleProperty, _open ? 90 : 0);
            if (_open)
            {
                CaptureBody.Visibility = Visibility.Visible;
                Spring.Run(CaptureScale, ScaleTransform.ScaleYProperty, 0.6, 1, 0, Spring.DurationMs, Spring.Ease);
                Spring.Run(CaptureBody, OpacityProperty, 0, 1, 0, 200, null);
            }
            else
            {
                Spring.Run(CaptureBody, OpacityProperty, 1, 0, 0, 140, null, () =>
                {
                    if (!_open) CaptureBody.Visibility = Visibility.Collapsed;
                    CaptureBody.Opacity = 1;
                });
            }
        }

        private void Setting_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton { Tag: string v } rb || BufText == null) return;
            switch (rb.GroupName)
            {
                case "ClipBuf": _buf = v; SaveButton.Content = "Save last " + v; break;
                case "ClipQ": _q = v; break;
                case "ClipFps": _fps = v; break;
            }
            BufText.Text = $"Buffering {_buf} · {_q} · {_fps}";
            if (IsLoaded) Spring.Bump(BufText);
        }
    }
}
