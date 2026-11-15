using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Nimbus.Controls
{
    /// <summary>
    /// Dancing music bars (island and Audio tab). Set IsPlaying to false to freeze them.
    /// Example: &lt;c:WaveBars Bars="22" BarHeight="26" IsPlaying="{Binding Playing}"/&gt;
    /// </summary>
    public class WaveBars : StackPanel
    {
        private static readonly Random Rng = new();
        private readonly List<AnimationClock> _clocks = new();

        public WaveBars()
        {
            Orientation = Orientation.Horizontal;
            Loaded += (_, _) => Build();
            Unloaded += (_, _) => Clear();
        }

        public int Bars { get; set; } = 8;
        public double BarHeight { get; set; } = 18;
        public double BarWidth { get; set; } = 3;
        public Brush? BarBrush { get; set; }

        public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(
            nameof(IsPlaying), typeof(bool), typeof(WaveBars),
            new PropertyMetadata(true, (d, e) => ((WaveBars)d).Apply((bool)e.NewValue)));

        public bool IsPlaying { get => (bool)GetValue(IsPlayingProperty); set => SetValue(IsPlayingProperty, value); }

        private void Build()
        {
            Clear();
            Height = BarHeight;
            var brush = BarBrush ?? (Brush)FindResource("PinkTextBrush");
            for (int i = 0; i < Bars; i++)
            {
                var scale = new ScaleTransform(1, 0.3);
                var bar = new Border
                {
                    Width = BarWidth,
                    Height = BarHeight,
                    Margin = new Thickness(1.5, 0, 1.5, 0),
                    CornerRadius = new CornerRadius(BarWidth / 2),
                    Background = brush,
                    RenderTransformOrigin = new Point(0.5, 1),
                    RenderTransform = scale,
                };
                Children.Add(bar);
                var anim = new DoubleAnimation(0.2 + Rng.NextDouble() * 0.2, 0.6 + Rng.NextDouble() * 0.4,
                    new Duration(TimeSpan.FromMilliseconds(320 + Rng.Next(520))))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                    BeginTime = TimeSpan.FromMilliseconds(Rng.Next(400)),
                };
                var clock = anim.CreateClock();
                scale.ApplyAnimationClock(ScaleTransform.ScaleYProperty, clock);
                _clocks.Add(clock);
            }
            Apply(IsPlaying);
        }

        private void Apply(bool playing)
        {
            foreach (var c in _clocks)
            {
                if (c.Controller == null) continue;
                if (playing) c.Controller.Resume(); else c.Controller.Pause();
            }
        }

        private void Clear()
        {
            foreach (var c in _clocks) c.Controller?.Stop();
            _clocks.Clear();
            Children.Clear();
        }
    }
}
