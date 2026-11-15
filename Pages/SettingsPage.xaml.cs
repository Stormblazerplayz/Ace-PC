using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nimbus.Motion;

namespace Nimbus.Pages
{
    public partial class SettingsPage : UserControl, INimbusPage
    {
        // Rows with no body yet. Put your real settings inside them the same way as Motion and Island.
        private static readonly string[] MoreRows =
        {
            "Clicker, recording & remote", "Monitors", "Name & theme", "Notifications", "Updates",
            "Wallpaper & screenshots", "General", "Assistant & models", "Windows", "VPN",
        };

        public SettingsPage()
        {
            InitializeComponent();
            foreach (var title in MoreRows) Root.Children.Add(MakeRow(title));
            Root.Children.Insert(0, AceRow());
        }

        /// <summary>Ace V4's settings: sounds, wake-up checks, voice, sleep goal.</summary>
        private Border AceRow()
        {
            var row = MakeRow("Ace V4");
            row.Tag = "Ace V4 assistant sounds volume wake checks voice hey ace dictation sleep bedtime start with windows";
            var body = Controls.AceSettingsView.Build();
            body.Visibility = Visibility.Collapsed;
            body.Margin = new Thickness(14, 0, 14, 12);
            ((StackPanel)row.Child).Children.Add(body);
            return row;
        }

        public Panel PopRoot => Root;

        public void OnShown() => Search.Focus();

        private Border MakeRow(string title)
        {
            var arrow = new TextBlock
            {
                Text = "›", FontSize = 18, Foreground = (Brush)FindResource("MutedBrush"),
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(0),
            };
            DockPanel.SetDock(arrow, Dock.Right);
            var head = new DockPanel();
            head.Children.Add(arrow);
            head.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.ExtraBold, FontSize = 14 });
            var button = new Button { Content = head, Style = (Style)FindResource("FlatRowButton") };
            button.Click += Head_Click;
            var stack = new StackPanel();
            stack.Children.Add(button);
            return new Border
            {
                Style = (Style)FindResource("Card"), Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 8),
                Tag = title, Child = stack,
            };
        }

        /// <summary>Opens or closes a row. Rows with nothing inside just bounce.</summary>
        private void Head_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Parent: StackPanel stack } button || stack.Parent is not Border row) return;
            var body = stack.Children.OfType<StackPanel>().FirstOrDefault();
            if (body == null) { Spring.Bump(row); return; }

            bool open = body.Visibility != Visibility.Visible;
            body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (open) Spring.PopIn(body);
            if (button.Content is DockPanel dock && dock.Children.OfType<TextBlock>().FirstOrDefault()?.RenderTransform is RotateTransform turn)
                Spring.To(turn, RotateTransform.AngleProperty, open ? 90 : 0);
        }

        private void Search_TextChanged(object sender, TextChangedEventArgs e)
        {
            string q = Search.Text.Trim();
            foreach (var row in Root.Children.OfType<Border>())
            {
                string words = (row.Tag as string) ?? "";
                bool show = q.Length == 0 || words.Contains(q, StringComparison.OrdinalIgnoreCase);
                row.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void Bounce_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Spring.SetBounce(e.NewValue / 100);
            if (MotionRow != null && IsLoaded) Spring.Bump(MotionRow);
        }

        private void MotionOn_Changed(object sender, RoutedEventArgs e)
        {
            if (MotionOn != null) Spring.Enabled = MotionOn.IsChecked == true;
        }
    }
}
