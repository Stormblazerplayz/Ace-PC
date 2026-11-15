using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Nimbus.Motion;

namespace Nimbus.Controls
{
    /// <summary>
    /// A crew member. Example: &lt;c:CrewBuddy Label="Pebble" BuddyBrush="{StaticResource PebbleBrush}" IsHot="{Binding CpuHot}"/&gt;
    /// </summary>
    public partial class CrewBuddy : UserControl
    {
        private static readonly Random Rng = new();
        private readonly DispatcherTimer _blink = new();
        private readonly DispatcherTimer _fidget = new() { Interval = TimeSpan.FromSeconds(4) };

        public CrewBuddy()
        {
            InitializeComponent();
            _blink.Tick += (_, _) => Blink();
            _fidget.Tick += (_, _) => Spring.Jump(Body, 12 * Size);   // a nervous hop while hot
            Loaded += (_, _) => { NextBlink(); _blink.Start(); if (IsHot) _fidget.Start(); };
            Unloaded += (_, _) => { _blink.Stop(); _fidget.Stop(); };
        }

        public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(CrewBuddy),
            new PropertyMetadata("", (d, e) =>
            {
                var b = (CrewBuddy)d;
                b.NameBlock.Text = (string)e.NewValue;
                b.NameBlock.Visibility = string.IsNullOrEmpty((string)e.NewValue) ? Visibility.Collapsed : Visibility.Visible;
            }));
        public static readonly DependencyProperty BuddyBrushProperty = DependencyProperty.Register(nameof(BuddyBrush), typeof(Brush), typeof(CrewBuddy),
            new PropertyMetadata(null, (d, e) => ((CrewBuddy)d).Blob.Background = (Brush)e.NewValue));
        public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(nameof(Size), typeof(double), typeof(CrewBuddy),
            new PropertyMetadata(1.0, (d, e) =>
            {
                var b = (CrewBuddy)d;
                double s = (double)e.NewValue;
                b.Body.Width = 44 * s;
                b.Body.Height = 40 * s;
                b.Eyes.LayoutTransform = new ScaleTransform(s, s);
            }));
        public static readonly DependencyProperty IsHotProperty = DependencyProperty.Register(nameof(IsHot), typeof(bool), typeof(CrewBuddy),
            new PropertyMetadata(false, (d, e) => ((CrewBuddy)d).SetHot((bool)e.NewValue)));

        public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
        public Brush? BuddyBrush { get => (Brush?)GetValue(BuddyBrushProperty); set => SetValue(BuddyBrushProperty, value); }
        /// <summary>1 = normal, 1.5 = bigger.</summary>
        public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
        public bool IsHot { get => (bool)GetValue(IsHotProperty); set => SetValue(IsHotProperty, value); }

        /// <summary>Make the buddy jump (call it when something happens, like a new alert).</summary>
        public void Jump() => Spring.Jump(Body, 34 * Size);

        private void Body_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => Jump();

        private void SetHot(bool hot)
        {
            Sweat.Visibility = hot ? Visibility.Visible : Visibility.Collapsed;
            if (hot && IsLoaded) _fidget.Start(); else _fidget.Stop();
        }

        private void NextBlink() => _blink.Interval = TimeSpan.FromMilliseconds(2200 + Rng.Next(3200));

        private void Blink()
        {
            NextBlink();
            if (!Spring.Enabled) return;
            var a = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(0.1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70))));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160))));
            EyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
    }
}
