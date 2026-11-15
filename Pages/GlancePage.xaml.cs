using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Nimbus.Services;

namespace Nimbus.Pages
{
    public partial class GlancePage : UserControl, INimbusPage
    {
        private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
        private bool _aiPaused;

        public GlancePage()
        {
            InitializeComponent();
            DataContext = Hub.Stats;
            _clock.Tick += (_, _) => UpdateClock();
            Loaded += (_, _) => { UpdateClock(); _clock.Start(); Hub.Stats.PropertyChanged += Stats_Changed; DrawSpark(); };
            Unloaded += (_, _) => { _clock.Stop(); Hub.Stats.PropertyChanged -= Stats_Changed; };
        }

        public Panel PopRoot => Root;

        public void OnShown() => DrawSpark();

        private void UpdateClock()
        {
            var now = DateTime.Now;
            ClockText.Text = now.ToString("h:mm tt");
            DateRun.Text = now.ToString("dddd, MMMM d");
        }

        private void Stats_Changed(object? sender, PropertyChangedEventArgs e) => DrawSpark();

        private void SparkBox_SizeChanged(object sender, SizeChangedEventArgs e) => DrawSpark();

        /// <summary>Draws the last 40 download speeds as a little line with a soft fill.</summary>
        private void DrawSpark()
        {
            double w = SparkBox.ActualWidth, h = SparkBox.ActualHeight;
            if (w <= 0 || h <= 0) return;
            var data = Hub.Stats.DownHistory;
            double max = Math.Max(1, data.Max()) * 1.15;
            var line = new PointCollection();
            for (int i = 0; i < data.Count; i++)
                line.Add(new Point(i * w / Math.Max(1, data.Count - 1), h - data[i] / max * (h - 4) - 2));
            SparkLine.Points = line;
            var fill = new PointCollection(line) { new Point(w, h), new Point(0, h) };
            SparkFill.Points = fill;
        }

        // ---------- buttons ----------
        // Only the safe ones are wired up. Hook the rest (AFK, Keep awake, Code, Empty Bin) to your own code.

        private void Launch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: string target }) return;
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch { /* not available on this PC */ }
        }

        private void Downloads_Click(object sender, RoutedEventArgs e)
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true }); }
            catch { }
        }

        private void Lock_Click(object sender, RoutedEventArgs e) => LockWorkStation();

        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string page }) Hub.Navigate(page);
        }

        private void ClearAlerts_Click(object sender, RoutedEventArgs e)
        {
            Hub.ClearAlerts();
            Nova.Jump();
        }

        private void PauseAi_Click(object sender, RoutedEventArgs e)
        {
            // Hook your "pause the model" code here.
            _aiPaused = !_aiPaused;
            PauseAi.Content = _aiPaused ? "Resume AI" : "Pause AI";
            AceBuddy.Jump();
        }

        [DllImport("user32.dll")]
        private static extern bool LockWorkStation();
    }
}
