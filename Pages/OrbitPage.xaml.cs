using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Nimbus.Motion;

namespace Nimbus.Pages
{
    /// <summary>
    /// Orbit setup and jobs. This is a mockup: copying, picking a model and running jobs are simulated.
    /// Replace the Task.Delay parts with your real Orbit calls.
    /// </summary>
    public partial class OrbitPage : UserControl, INimbusPage
    {
        private bool _imageReady, _modelReady, _copying;

        public OrbitPage()
        {
            InitializeComponent();
            Log("Runtime started");
            Log("Waiting for disk image and model");
        }

        public Panel PopRoot => Root;

        public void OnShown() { }

        private void Log(string text)
        {
            var line = new TextBlock { Margin = new Thickness(0, 0, 0, 5), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
            line.Inlines.Add(new Run(DateTime.Now.ToString("h:mm tt") + "  ")
            {
                FontFamily = (FontFamily)FindResource("MonoFont"),
                Foreground = (Brush)FindResource("MutedBrush"),
            });
            line.Inlines.Add(new Run(text));
            LogList.Children.Add(line);
            if (IsLoaded) Spring.PopIn(line);
            LogScroll.ScrollToEnd();
        }

        private async void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (_copying || _imageReady) return;
            _copying = true;
            CopyButton.IsEnabled = false;
            CopyTrack.Visibility = Visibility.Visible;
            Log("Copying the 60 GB disk image from Lightning");
            var rng = new Random();
            double pct = 0;
            while (pct < 1)
            {
                await Task.Delay(260);                      // replace with real copy progress
                pct = Math.Min(1, pct + 0.04 + rng.NextDouble() * 0.09);
                Spring.To(CopyScale, ScaleTransform.ScaleXProperty, pct, 400, Spring.Soft);
            }
            await Task.Delay(400);
            CopyTrack.Visibility = Visibility.Collapsed;
            CopyButton.Content = "Copied";
            _imageReady = true;
            Tick(ImageCheck, ImageTick);
            Log("Disk image copied (60 GB)");
            CheckReady();
        }

        private void Model_Click(object sender, RoutedEventArgs e)
        {
            if (_modelReady) return;
            _modelReady = true;
            ModelButton.IsEnabled = false;
            ModelButton.Content = "Using lfm2.5-8b";
            Tick(ModelCheck, ModelTick);
            Log("Picked lfm2.5-8b as Orbit's model");
            CheckReady();
        }

        private void Tick(Border check, TextBlock tick)
        {
            check.Background = (Brush)FindResource("GoodBrush");
            check.BorderThickness = new Thickness(0);
            tick.Visibility = Visibility.Visible;
            Spring.Bump(check);
        }

        private void CheckReady()
        {
            if (!_imageReady || !_modelReady) return;
            PcState.Text = "Ready";
            StateFlagText.Text = "Ready for jobs";
            StartButton.IsEnabled = true;
            Spring.Bump(StateFlag);
            Spring.Bump(PcState);
            Log("Orbit PC is ready");
        }

        private void JobInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { e.Handled = true; Start(); }
        }

        private void Start_Click(object sender, RoutedEventArgs e) => Start();

        private async void Start()
        {
            if (!StartButton.IsEnabled) return;
            string job = JobInput.Text.Trim();
            if (job.Length == 0) job = "tidy my Downloads folder";
            JobInput.Clear();
            Log("Started: " + job);
            await Task.Delay(900);
            Log("Opened File Explorer on the Orbit PC");
            await Task.Delay(1300);
            Log("Done. Sorted 42 files into folders");
        }
    }
}
