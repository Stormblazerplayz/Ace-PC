using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Nimbus.Motion;

namespace Nimbus.Pages
{
    /// <summary>One row in the mixer. Hook Volume/Muted to Windows' per-app volume (NAudio or CoreAudio).</summary>
    public sealed class AppVolume : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public string Name { get; init; } = "";
        public string Letter => Name.Length > 0 ? Name[..1] : "?";
        public Brush Color { get; init; } = Brushes.Gray;

        private double _volume = 100;
        public double Volume
        {
            get => _volume;
            set { _volume = value; Changed(nameof(Volume)); }
        }

        private bool _muted;
        public bool Muted
        {
            get => _muted;
            set { _muted = value; Changed(nameof(Muted)); Changed(nameof(MuteIcon)); }
        }
        public string MuteIcon => Muted ? "🔇" : "🔊";

        private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class AudioPage : UserControl, INimbusPage
    {
        public ObservableCollection<AppVolume> Apps { get; } = new();
        private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
        private bool _playing = true;
        private int _pos = 188;
        private const int Length = 359;   // 5:59
        private double _masterBefore = 100;

        private const string PauseIcon = "M6.5,4.5 L10.5,4.5 L10.5,19.5 L6.5,19.5 Z M13.5,4.5 L17.5,4.5 L17.5,19.5 L13.5,19.5 Z";
        private const string PlayIconData = "M7,4.5 L7,19.5 L19,12 Z";

        public AudioPage()
        {
            InitializeComponent();
            Apps.Add(new AppVolume { Name = "Nimbus", Color = Hex("#5B6070") });
            Apps.Add(new AppVolume { Name = "Vivaldi", Color = Hex("#EF3939") });
            Apps.Add(new AppVolume { Name = "Discord", Color = Hex("#5865F2") });
            Apps.Add(new AppVolume { Name = "Norton 360", Color = Hex("#F5C518") });
            AppList.ItemsSource = Apps;
            _tick.Tick += (_, _) => Advance();
            Loaded += (_, _) => _tick.Start();
            Unloaded += (_, _) => _tick.Stop();
            ShowPosition(false);
        }

        public Panel PopRoot => Root;

        public void OnShown() { }

        private static Brush Hex(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

        // ---------- now playing (sample track; hook to your media session code) ----------

        private void Advance()
        {
            if (!_playing) return;
            _pos = _pos >= Length ? 0 : _pos + 1;
            ShowPosition(true);
        }

        private void ShowPosition(bool animate)
        {
            CurText.Text = $"{_pos / 60}:{_pos % 60:00}";
            LenText.Text = $"{Length / 60}:{Length % 60:00}";
            double target = (double)_pos / Length;
            if (animate) Spring.To(ProgressScale, ScaleTransform.ScaleXProperty, target, 400, Spring.Soft);
            else ProgressScale.ScaleX = target;
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            _playing = !_playing;
            Wave.IsPlaying = _playing;
            PlayIcon.Data = Geometry.Parse(_playing ? PauseIcon : PlayIconData);
            PlayButton.ToolTip = _playing ? "Pause" : "Play";
        }

        private void Prev_Click(object sender, RoutedEventArgs e) { _pos = 0; ShowPosition(true); }

        private void Next_Click(object sender, RoutedEventArgs e) { _pos = 0; ShowPosition(true); Spring.Bump(TrackTitle); }

        // ---------- volume ----------

        private void MasterMute_Click(object sender, RoutedEventArgs e)
        {
            if (MasterVolume.Value > 0) { _masterBefore = MasterVolume.Value; Spring.To(MasterVolume, RangeBase.ValueProperty, 0, 500, Spring.Soft); MasterMute.Content = "🔇"; }
            else { Spring.To(MasterVolume, RangeBase.ValueProperty, _masterBefore, 500, Spring.Soft); MasterMute.Content = "🔊"; }
        }

        private void AppMute_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is AppVolume app) app.Muted = !app.Muted;
        }

        private void Output_Checked(object sender, RoutedEventArgs e)
        {
            // Hook your "switch default output device" code here.
            if (IsLoaded) Spring.Bump(Wave);
        }
    }
}
