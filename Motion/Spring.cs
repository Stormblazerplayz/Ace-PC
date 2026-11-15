using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Nimbus.Motion
{
    /// <summary>
    /// All of Nimbus's bouncy motion lives here, so the whole app feels the same.
    /// Change Oscillations/Springiness to make everything more or less bouncy
    /// (keep NimbusSpring in Themes/NimbusTheme.xaml matching).
    /// </summary>
    public static class Spring
    {
        /// <summary>Turn off to make every animation instant (Game mode does this).</summary>
        public static bool Enabled { get; set; } = true;

        public static int Oscillations { get; private set; } = 2;      // more = more wobbles
        public static double Springiness { get; private set; } = 4.7;  // lower = bouncier
        public const double DurationMs = 650;
        public const double StaggerMs = 55;         // delay between items popping in

        private static IEasingFunction? _ease, _medium, _soft;

        /// <summary>The main spring: small things like pops, bumps and buttons.</summary>
        public static IEasingFunction Ease => _ease ??= Make(Oscillations, Springiness);

        /// <summary>One wobble, for things that travel further (the tab highlight).</summary>
        public static IEasingFunction Medium => _medium ??= Make(1, Springiness * 0.85);

        /// <summary>A gentle settle, for big moves (the window shrinking into the island).</summary>
        public static IEasingFunction Soft => _soft ??= Make(1, Springiness + 3.3);

        private static IEasingFunction Make(int oscillations, double springiness)
        {
            var e = new ElasticEase { Oscillations = oscillations, Springiness = springiness, EasingMode = EasingMode.EaseOut };
            e.Freeze();
            return e;
        }

        /// <summary>
        /// 0 = calm, 1 = super bouncy. Used by the Motion slider in Settings.
        /// (XAML styles keep NimbusSpring from the theme; this changes the code-driven motion.)
        /// </summary>
        public static void SetBounce(double amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            Oscillations = amount < 0.3 ? 1 : amount < 0.8 ? 2 : 3;
            Springiness = 9 - 7 * amount;
            _ease = _medium = _soft = null;
        }

        /// <summary>Spring a property from where it is now to a new value.</summary>
        public static void To(DependencyObject target, DependencyProperty prop, double to, double durationMs = DurationMs,
                              IEasingFunction? ease = null, Action? done = null)
        {
            var current = (double)target.GetValue(prop);
            if (double.IsNaN(current)) current = to;
            Run(target, prop, current, to, 0, durationMs, ease ?? Ease, done);
        }

        /// <summary>
        /// Plays from -> to after a delay. The value sits at "from" during the delay,
        /// so nothing flashes before it animates. When it ends the real value is "to".
        /// </summary>
        public static void Run(DependencyObject target, DependencyProperty prop, double from, double to,
                               double delayMs, double durationMs, IEasingFunction? ease, Action? done = null)
        {
            if (target is not IAnimatable anim) { done?.Invoke(); return; }
            anim.BeginAnimation(prop, null);
            target.SetValue(prop, to);
            if (!Enabled || durationMs <= 0) { done?.Invoke(); return; }

            var frames = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            if (delayMs > 0)
                frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delayMs))));
            frames.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delayMs + durationMs)), ease));
            if (done != null) frames.Completed += (_, _) => done();
            anim.BeginAnimation(prop, frames, HandoffBehavior.SnapshotAndReplace);
        }

        /// <summary>
        /// Gives an element its own Scale + Translate transforms to animate.
        /// Returns null for buttons that already get their bounce from a Style.
        /// </summary>
        public static (ScaleTransform Scale, TranslateTransform Shift)? Parts(UIElement el, Point? origin = null)
        {
            if (el is ButtonBase) return null;
            if (el.RenderTransform is TransformGroup g && !g.IsFrozen && g.Children.Count == 2
                && g.Children[0] is ScaleTransform s && g.Children[1] is TranslateTransform t)
            {
                if (origin.HasValue) el.RenderTransformOrigin = origin.Value;
                return (s, t);
            }
            var scale = new ScaleTransform(1, 1);
            var shift = new TranslateTransform(0, 0);
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(shift);
            el.RenderTransform = group;
            el.RenderTransformOrigin = origin ?? new Point(0.5, 0.5);
            return (scale, shift);
        }

        /// <summary>Fade in, rise up and grow with a bounce. index staggers a list.</summary>
        public static void PopIn(UIElement el, int index = 0)
        {
            double delay = index * StaggerMs;
            Run(el, UIElement.OpacityProperty, 0, 1, delay, 220, null);
            var parts = Parts(el);
            if (parts == null) return;
            var (scale, shift) = parts.Value;
            Run(shift, TranslateTransform.YProperty, 18, 0, delay, DurationMs, Ease);
            Run(scale, ScaleTransform.ScaleXProperty, 0.92, 1, delay, DurationMs, Ease);
            Run(scale, ScaleTransform.ScaleYProperty, 0.92, 1, delay, DurationMs, Ease);
        }

        /// <summary>Pops in every visible child of a panel, one after another.</summary>
        public static void PopChildren(Panel panel)
        {
            int i = 0;
            foreach (UIElement child in panel.Children)
            {
                if (child.Visibility == Visibility.Visible) PopIn(child, i++);
            }
        }

        /// <summary>A quick squish-and-spring, for something that just changed.</summary>
        public static void Bump(UIElement el)
        {
            var parts = Parts(el);
            if (parts == null) return;
            var (scale, _) = parts.Value;
            Run(scale, ScaleTransform.ScaleXProperty, 0.9, 1, 0, DurationMs, Ease);
            Run(scale, ScaleTransform.ScaleYProperty, 0.9, 1, 0, DurationMs, Ease);
        }

        /// <summary>Cartoon jump with squash and stretch (used by Pebble, Nova and Ace).</summary>
        public static void Jump(UIElement el, double height = 34)
        {
            if (!Enabled) return;
            var parts = Parts(el, new Point(0.5, 1));
            if (parts == null) return;
            var (scale, shift) = parts.Value;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Keys(1, (100, 1.16), (280, 0.88), (450, 1.08), (560, 0.97), (720, 1)));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Keys(1, (100, 0.80), (280, 1.14), (450, 0.92), (560, 1.03), (720, 1)));
            shift.BeginAnimation(TranslateTransform.YProperty, Keys(0, (100, 0), (280, -height), (450, 0), (720, 0)));
        }

        /// <summary>Shrinks, slides right and fades out, then calls done (for dismissing).</summary>
        public static void Out(UIElement el, Action done)
        {
            var parts = Parts(el);
            if (!Enabled || parts == null) { done(); return; }
            var (scale, shift) = parts.Value;
            var ease = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseIn };
            var dur = new Duration(TimeSpan.FromMilliseconds(320));
            var fade = new DoubleAnimation(0, dur) { EasingFunction = ease };
            fade.Completed += (_, _) => done();
            shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(40, dur) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.85, dur) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.85, dur) { EasingFunction = ease });
            el.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private static DoubleAnimationUsingKeyFrames Keys(double start, params (double Ms, double Value)[] keys)
        {
            var a = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            a.KeyFrames.Add(new DiscreteDoubleKeyFrame(start, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            foreach (var (ms, value) in keys)
                a.KeyFrames.Add(new SplineDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), new KeySpline(0.3, 0, 0.4, 1)));
            return a;
        }
    }
}
