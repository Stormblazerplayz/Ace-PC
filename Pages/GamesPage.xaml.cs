using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nimbus.Motion;
using Nimbus.Services;

namespace Nimbus.Pages
{
    public partial class GamesPage : UserControl, INimbusPage
    {
        // Sample library (name, two cover colors). Swap in your real Steam/Roblox scan.
        private static readonly (string Name, string C1, string C2)[] Games =
        {
            ("ROBLOX", "#2C5A44", "#3E7A5C"), ("No Man's Sky", "#13786F", "#F07A3A"), ("Automation", "#B88A5E", "#7B3B2A"),
            ("PEAK", "#7A3FD1", "#F39C6B"), ("Trailmakers", "#38A6D8", "#F2A93B"), ("Ant Keeping Simulator", "#C7B49A", "#8A6B4F"),
            ("BeamNG.drive", "#2F6FB5", "#D9E6F2"), ("Wobbly Life", "#4CAE4F", "#C4E7FF"), ("Rhythm Run", "#E255A1", "#4FD1FF"),
            ("Town Builder", "#5B7FA6", "#B8D0E6"),
        };

        private bool _freeing;

        public GamesPage()
        {
            InitializeComponent();
            DataContext = Hub.Stats;
            foreach (var g in Games) Library.Children.Add(Cover(g.Name, g.C1, g.C2));
            Loaded += (_, _) => { ShowFree(); Hub.Stats.PropertyChanged += Stats_Changed; };
            Unloaded += (_, _) => Hub.Stats.PropertyChanged -= Stats_Changed;
        }

        private void Stats_Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (!_freeing) ShowFree();
        }

        public Panel PopRoot => Root;

        public void OnShown() => ShowFree();

        private void ShowFree() => FreeRun.Text = Hub.Stats.MemFreeGb.ToString("0.0");

        private static Button Cover(string name, string c1, string c2)
        {
            var face = new Grid { Width = 128, Height = 168 };
            face.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(16),
                Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(c1), (Color)ColorConverter.ConvertFromString(c2), 60),
            });
            face.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(0, 0, 16, 16), VerticalAlignment = VerticalAlignment.Bottom, Height = 64,
                Background = new LinearGradientBrush(Colors.Transparent, Color.FromArgb(0xB0, 0, 0, 0), 90),
            });
            if (name == "ROBLOX")
                face.Children.Add(new Border
                {
                    Width = 38, Height = 38, Background = Brushes.White, CornerRadius = new CornerRadius(5),
                    RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(15),
                    Child = new Border { Width = 12, Height = 12, Background = new SolidColorBrush(Color.FromRgb(0x2C, 0x5A, 0x44)) },
                    Margin = new Thickness(0, 0, 0, 30),
                });
            face.Children.Add(new TextBlock
            {
                Text = name, Margin = new Thickness(10, 0, 10, 10), VerticalAlignment = VerticalAlignment.Bottom,
                FontWeight = FontWeights.ExtraBold, FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White,
            });
            return new Button
            {
                Content = face, Margin = new Thickness(0, 0, 10, 10), ToolTip = name,
                Style = (Style)Application.Current.FindResource("BareButton"),
            };
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            // Pop the cover inside each button (the button's own scale belongs to its hover style).
            int i = 0;
            foreach (var child in Library.Children)
                if (child is Button { Content: UIElement face }) Spring.PopIn(face, i++);
        }

        private async void Free_Click(object sender, RoutedEventArgs e)
        {
            if (_freeing) return;
            _freeing = true;
            FreeButton.Content = "Freeing…";
            double before = Hub.Stats.MemFreeGb;
            double mb = await Task.Run(() => Processes.TrimBackground());
            double target = before + mb / 1024;
            // count up to the new number
            for (int i = 1; i <= 12; i++)
            {
                FreeRun.Text = (before + (target - before) * (1 - Math.Pow(1 - i / 12.0, 3))).ToString("0.0");
                await Task.Delay(40);
            }
            FreeButton.Content = mb >= 100 ? $"Freed {mb / 1024:0.0} GB" : "Already lean";
            Spring.Bump(GameTitle);
            await Task.Delay(2600);
            FreeButton.Content = "Free memory now";
            ShowFree();
            _freeing = false;
        }
    }
}
