using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nimbus.Controls
{
    /// <summary>What Ace is doing right now. The orb's colors, swirl speed and extras come from it (same as the iPad app).</summary>
    public enum OrbMood { Idle, Typing, Listening, Thinking, Talking, Checking, Alert, Happy, Sleeping }

    /// <summary>
    /// Ace at the top of the window: a shaded glowing orb with colors swirling inside. When Ace looks at something
    /// it stretches into a pill and two digital eyes come out. Listening shows rings, thinking a spinning gold
    /// ring, talking little bars, typing dots, and a focus round fills a green ring around it.
    /// Click it for Settings; press and hold to talk.
    /// </summary>
    public sealed class AceOrb : FrameworkElement
    {
        // ------------------------------------------------------------------ Colors (from the iPad app)

        public static readonly Color AceBlue = C(0x5BB6FF), Purple = C(0x8F7CFF), Deep = C(0x0B0A24), Ink = C(0x14121F),
                                     Fg = C(0xECEBF5), Good = C(0x45D483), Amber = C(0xF5B83D), Nova = C(0xFF5F8F), FocusGreen = C(0x3DF59A);

        public static Color Tint(OrbMood m) => m switch
        {
            OrbMood.Listening => C(0x3D8BFF),
            OrbMood.Thinking => C(0xFFC83D),
            OrbMood.Talking => C(0x7CC8FF),
            OrbMood.Alert => Amber,
            OrbMood.Happy => Good,
            OrbMood.Sleeping => C(0x4A6FA8),
            _ => AceBlue,
        };

        public static Color Tint2(OrbMood m) => m switch
        {
            OrbMood.Listening => C(0x6FE3FF),
            OrbMood.Thinking => C(0xFF8A3D),
            OrbMood.Alert => Nova,
            OrbMood.Happy => C(0x6FE3FF),
            OrbMood.Sleeping => C(0x2A3A78),
            _ => Purple,
        };

        public static double Energy(OrbMood m) => m switch
        {
            OrbMood.Typing => 0.4,
            OrbMood.Listening => 0.8,
            OrbMood.Thinking => 1,
            OrbMood.Talking => 0.7,
            OrbMood.Checking or OrbMood.Alert => 0.5,
            OrbMood.Happy => 0.4,
            OrbMood.Sleeping => 0.05,
            _ => 0.25,
        };

        private static double Speed(OrbMood m) => m switch { OrbMood.Thinking => 3.2, OrbMood.Talking => 7, OrbMood.Sleeping => 0.8, _ => 1.4 };

        /// <summary>The eyes come out (and the orb stretches into a pill) only when Ace is looking at something.</summary>
        public static bool ShowsEyes(OrbMood m) => m is OrbMood.Checking or OrbMood.Alert or OrbMood.Happy;

        public static string Label(OrbMood m) => m switch
        {
            OrbMood.Typing => "Reading along",
            OrbMood.Listening => "Listening",
            OrbMood.Thinking => "Thinking",
            OrbMood.Talking => "Talking",
            OrbMood.Checking => "Checking things",
            OrbMood.Alert => "Heads up",
            OrbMood.Happy => "All good",
            OrbMood.Sleeping => "Resting",
            _ => "Ready",
        };

        // ------------------------------------------------------------------ Settings

        /// <summary>The orb's size. The pill is a little taller than it.</summary>
        public double OrbSize { get; set; } = 58;
        public double ExpandedWidth { get; set; } = 330;

        private OrbMood _mood = OrbMood.Idle;
        public OrbMood Mood
        {
            get => _mood;
            set { if (_mood == value) return; _mood = value; _moodChanged = _t; }
        }

        /// <summary>How loud you are while Ace listens (0 to 1), so the rings grow when you talk.</summary>
        public double Level { get; set; }
        /// <summary>What shows next to the eyes in the pill ("Checking things").</summary>
        public string Status { get; set; } = "";
        /// <summary>How far the focus round is (0 to 1), or below 0 for no focus round.</summary>
        public double FocusProgress { get; set; } = -1;
        /// <summary>Stops the animation (the window is minimized), so it doesn't use the CPU.</summary>
        public bool Paused { get => _paused; set { _paused = value; if (!value) InvalidateVisual(); } }

        /// <summary>A quick click.</summary>
        public event Action? Tapped;
        /// <summary>Press and hold: true when the hold starts, false when you let go.</summary>
        public event Action<bool>? Hold;

        // ------------------------------------------------------------------ State

        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _t, _last, _moodChanged;
        private double _expand, _holdScale = 1, _pressScale = 1;
        private Color _tint = AceBlue, _tint2 = Purple;
        private double _energy = 0.25;
        private bool _paused, _hooked, _holding;
        private readonly DispatcherTimer _holdTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
        private Point _look;
        private double _nextBlink = 2.5, _nextGlance = 1;
        private Point _lookTarget;

        public AceOrb()
        {
            Focusable = true;
            Cursor = Cursors.Hand;
            FocusVisualStyle = null;
            ToolTip = "Click for Settings · Hold to talk";
            Loaded += (_, _) => Hook(true);
            Unloaded += (_, _) => Hook(false);
            _holdTimer.Tick += (_, _) =>
            {
                _holdTimer.Stop();
                if (!IsMouseCaptured) return;
                _holding = true;
                Hold?.Invoke(true);
            };
        }

        private void Hook(bool on)
        {
            if (on == _hooked) return;
            _hooked = on;
            if (on) CompositionTarget.Rendering += OnFrame; else CompositionTarget.Rendering -= OnFrame;
        }

        private void OnFrame(object? sender, EventArgs e)
        {
            if (_paused || !IsVisible) return;
            InvalidateVisual();
        }

        // ------------------------------------------------------------------ Mouse: click for Settings, hold to talk

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (!HitsOrb(e.GetPosition(this))) return;
            e.Handled = true;
            Focus();
            CaptureMouse();
            _holding = false;
            _pressScale = 0.94;
            _holdTimer.Start();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (!IsMouseCaptured) return;
            e.Handled = true;
            ReleaseMouseCapture();
            _holdTimer.Stop();
            _pressScale = 1;
            if (_holding) { _holding = false; Hold?.Invoke(false); }
            else if (HitsOrb(e.GetPosition(this))) Tapped?.Invoke();
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            _holdTimer.Stop();
            _pressScale = 1;
            if (_holding) { _holding = false; Hold?.Invoke(false); }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key is Key.Enter or Key.Space) { e.Handled = true; Tapped?.Invoke(); }
        }

        private bool HitsOrb(Point p)
        {
            var r = PillRect();
            r.Inflate(6, 6);
            return r.Contains(p);
        }

        protected override HitTestResult? HitTestCore(PointHitTestParameters p) =>
            HitsOrb(p.HitPoint) ? new PointHitTestResult(this, p.HitPoint) : null;

        // ------------------------------------------------------------------ Layout

        private double PillHeight => OrbSize + 8;

        protected override Size MeasureOverride(Size available)
        {
            double w = double.IsInfinity(available.Width) ? ExpandedWidth : Math.Min(available.Width, Math.Max(ExpandedWidth, PillHeight));
            return new Size(w, PillHeight + 16);
        }

        private double MaxPill => Math.Max(PillHeight, Math.Min(ExpandedWidth, ActualWidth - 8));

        private Rect PillRect()
        {
            double h = PillHeight;
            double w = h + (MaxPill - h) * Ease(_expand);
            return new Rect(ActualWidth / 2 - w / 2, ActualHeight / 2 - h / 2, w, h);
        }

        private static double Ease(double x) => x * x * (3 - 2 * x);

        // ------------------------------------------------------------------ Drawing

        protected override void OnRender(DrawingContext dc)
        {
            _t = _clock.Elapsed.TotalSeconds;
            double dt = Math.Clamp(_t - _last, 0, 0.1);
            _last = _t;

            // Ease everything toward the mood, so changes glide instead of jumping.
            double k = Math.Min(1, dt * 6);
            _tint = Lerp(_tint, Tint(_mood), k);
            _tint2 = Lerp(_tint2, Tint2(_mood), k);
            _energy += (Energy(_mood) - _energy) * k;
            _expand += ((ShowsEyes(_mood) ? 1 : 0) - _expand) * Math.Min(1, dt * 7);
            if (Math.Abs(_expand - (ShowsEyes(_mood) ? 1 : 0)) < 0.002) _expand = ShowsEyes(_mood) ? 1 : 0;
            double hold = _holding ? 1.12 : _pressScale;
            _holdScale += (hold - _holdScale) * Math.Min(1, dt * 14);

            double breathe = (Math.Sin(_t * Speed(_mood)) + 1) / 2;
            var pill = PillRect();
            double e = Ease(_expand);
            double h = PillHeight;
            var center = new Point(pill.Left + h / 2, pill.Top + h / 2);
            double d = OrbSize - 8 + 8 * (1 - e);   // the orb shrinks a touch inside the pill

            // The pill (only when stretched out), with a soft glow under it.
            if (e > 0.01)
            {
                var glow = pill;
                glow.Inflate(6, 6);
                dc.DrawRoundedRectangle(new SolidColorBrush(WithA(_tint, 0.10 * e)), null, glow, (h + 12) / 2, (h + 12) / 2);
                dc.DrawRoundedRectangle(new SolidColorBrush(WithA(Ink, 0.96 * e)), new Pen(new SolidColorBrush(WithA(_tint, 0.45 * e)), 1.2), pill, h / 2, h / 2);
            }
            // Invisible hit area over the pill.
            dc.DrawRoundedRectangle(Brushes.Transparent, null, pill, h / 2, h / 2);

            dc.PushTransform(new ScaleTransform(_holdScale, _holdScale, center.X, center.Y));
            DrawExtrasBehind(dc, center, d);
            double glowAmt = _mood == OrbMood.Sleeping ? 0.3 : Math.Min(1, 0.5 + 0.35 * breathe + 0.2 * _energy);
            DrawOrb(dc, center, d * (1 + 0.04 * breathe), _tint, _tint2, glowAmt, _t, _energy, bloom: true);
            DrawExtrasFront(dc, center, d);
            dc.Pop();

            // The eyes and the status, when the pill is out.
            if (e > 0.05)
            {
                UpdateEyes(dt);
                double eyesH = h * 0.62, eyesW = 96;
                var eyesCenter = new Point(pill.Left + h + 6 + eyesW / 2, center.Y);
                dc.PushOpacity(Math.Clamp((e - 0.3) / 0.7, 0, 1));
                DrawEyes(dc, eyesCenter, eyesH, _look, Blink(), _mood == OrbMood.Happy, _tint);
                if (Status.Length > 0 && pill.Width >= 290)
                {
                    var ft = new FormattedText(Status, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                        new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                        13, new SolidColorBrush(WithA(Fg, 0.85)), VisualTreeHelper.GetDpi(this).PixelsPerDip)
                    {
                        MaxTextWidth = Math.Max(20, pill.Right - (eyesCenter.X + eyesW / 2 + 8) - 16),
                        MaxLineCount = 1,
                        Trimming = TextTrimming.CharacterEllipsis,
                    };
                    dc.DrawText(ft, new Point(eyesCenter.X + eyesW / 2 + 8, center.Y - ft.Height / 2));
                }
                dc.Pop();
            }
        }

        private void DrawExtrasBehind(DrawingContext dc, Point c, double d)
        {
            double r = d / 2;
            if (FocusProgress >= 0)
            {
                // A focus round fills this ring as the time goes by.
                double fr = r * 1.32;
                dc.DrawEllipse(null, new Pen(new SolidColorBrush(WithA(FocusGreen, 0.18)), 3), c, fr, fr);
                var arc = Arc(c, fr, -90, -90 + 360 * Math.Clamp(Math.Max(0.01, FocusProgress), 0, 0.9999));
                dc.DrawGeometry(null, new Pen(new SolidColorBrush(FocusGreen), 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arc);
            }
            if (_mood == OrbMood.Listening)
            {
                for (int i = 0; i < 3; i++)
                {
                    double phase = (_t * 0.9 + i / 3.0) % 1;
                    double rr = r * (1 + phase * (0.9 + Level * 0.8));
                    dc.DrawEllipse(null, new Pen(new SolidColorBrush(WithA(_tint, 0.6 * (1 - phase))), 2), c, rr, rr);
                }
            }
        }

        private void DrawExtrasFront(DrawingContext dc, Point c, double d)
        {
            double r = d / 2, s = d / 50;
            switch (_mood)
            {
                case OrbMood.Thinking:
                {
                    double start = _t * 220;
                    var ring = new Pen(new SolidColorBrush(C(0xFFE08A)), 2.4) { DashStyle = new DashStyle(new[] { 5 / 2.4, 6 / 2.4 }, 0), StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, DashCap = PenLineCap.Round };
                    dc.DrawGeometry(null, ring, Arc(c, r * 1.22, start, start + 360 * 0.72));
                    double s2 = -_t * 300;
                    dc.DrawGeometry(null, new Pen(new SolidColorBrush(WithA(Colors.White, 0.7)), 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Arc(c, r * 0.5, s2, s2 + 108));
                    break;
                }
                case OrbMood.Talking:
                {
                    var fill = new SolidColorBrush(WithA(Colors.White, 0.85));
                    double w = 3.5 * s, gap = 3 * s, total = 4 * w + 3 * gap;
                    for (int i = 0; i < 4; i++)
                    {
                        double bh = (6 + 12 * Math.Abs(Math.Sin(_t * 9 + i * 1.3))) * s;
                        var rect = new Rect(c.X - total / 2 + i * (w + gap), c.Y - bh / 2, w, bh);
                        dc.DrawRoundedRectangle(fill, null, rect, w / 2, w / 2);
                    }
                    break;
                }
                case OrbMood.Typing:
                {
                    double dot = 5 * s, gap = 4 * s;
                    for (int i = 0; i < 3; i++)
                    {
                        double a = 0.4 + 0.5 * (Math.Sin(_t * 6 - i) + 1) / 2;
                        dc.DrawEllipse(new SolidColorBrush(WithA(Colors.White, a)), null, new Point(c.X + (i - 1) * (dot + gap), c.Y), dot / 2, dot / 2);
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// The glowing ball: a deep sphere with colors swirling inside, an inner glow, a shiny highlight, a rim light and
        /// a soft bloom around it. time is in seconds (0 for a still picture).
        /// </summary>
        public static void DrawOrb(DrawingContext dc, Point c, double d, Color tint, Color tint2, double glow, double time, double energy, bool bloom)
        {
            double r = d / 2;
            if (bloom)
            {
                double br = d * 1.8 / 2;
                var bloomBrush = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(WithA(tint, 0.18 + 0.45 * glow), 0),
                        new GradientStop(WithA(tint, 0.14 + 0.32 * glow), r / br * 0.85),
                        new GradientStop(WithA(tint2, 0.16 * glow), r / br * 1.25),
                        new GradientStop(WithA(tint2, 0), 1),
                    },
                };
                dc.DrawEllipse(bloomBrush, null, c, br, br);
            }

            var circle = new EllipseGeometry(c, r, r);
            dc.PushClip(circle);

            // Deep base color, lit from the top left.
            dc.DrawEllipse(new RadialGradientBrush
            {
                Center = new Point(0.42, 0.38), GradientOrigin = new Point(0.42, 0.38), RadiusX = 0.62, RadiusY = 0.62,
                GradientStops = { new GradientStop(tint, 0), new GradientStop(tint2, 0.55), new GradientStop(Deep, 1) },
            }, null, c, r, r);

            // Colors swirling inside: soft blobs going round, two of them turning the other way.
            double spin = time * (0.25 + 1.2 * energy);
            Blob(dc, c, r, spin, 0.42, 0.78, tint, 0.85);
            Blob(dc, c, r, spin + 2.1, 0.40, 0.72, tint2, 0.8);
            Blob(dc, c, r, spin + 4.2, 0.30, 0.48, Colors.White, 0.45);
            Blob(dc, c, r, -spin * 1.4 + 1, 0.35, 0.62, tint2, 0.55);
            Blob(dc, c, r, -spin * 1.4 + 4, 0.38, 0.6, tint, 0.5);

            // Shade the far edge so it looks round, not flat.
            dc.DrawEllipse(new RadialGradientBrush
            {
                Center = new Point(0.38, 0.32), GradientOrigin = new Point(0.38, 0.32), RadiusX = 0.74, RadiusY = 0.74,
                GradientStops = { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0), new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.5), new GradientStop(Color.FromArgb(150, 0, 0, 0), 1) },
            }, null, c, r, r);

            // A warm glow from the core that drifts a little.
            var core = new Point(c.X + d * 0.07 * Math.Cos(time * 0.7), c.Y + d * 0.06 + d * 0.06 * Math.Sin(time * 0.9));
            dc.DrawEllipse(new RadialGradientBrush
            {
                GradientStops = { new GradientStop(WithA(Colors.White, 0.28 + 0.3 * energy), 0), new GradientStop(WithA(tint, 0.25), 0.5), new GradientStop(WithA(tint, 0), 1) },
            }, null, core, d * 0.36, d * 0.36);

            // Shiny highlight, like light on glass.
            var hl = new Point(c.X - d * 0.13, c.Y - d * 0.22);
            dc.PushTransform(new RotateTransform(-28, hl.X, hl.Y));
            dc.DrawEllipse(new LinearGradientBrush(WithA(Colors.White, 0.8), WithA(Colors.White, 0), 90), null, hl, d * 0.23, d * 0.135);
            dc.Pop();
            dc.DrawEllipse(new SolidColorBrush(WithA(Colors.White, 0.9)), null, new Point(c.X - d * 0.21, c.Y - d * 0.15), d * 0.035, d * 0.035);
            dc.Pop();

            // Rim light that slowly travels around the edge.
            double ang = -time * (14 + 70 * energy) * 0.5;
            var rimBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0.5 + 0.5 * Math.Cos(ang * Math.PI / 180), 0.5 + 0.5 * Math.Sin(ang * Math.PI / 180)),
                EndPoint = new Point(0.5 - 0.5 * Math.Cos(ang * Math.PI / 180), 0.5 - 0.5 * Math.Sin(ang * Math.PI / 180)),
                GradientStops =
                {
                    new GradientStop(WithA(Colors.White, 0.55), 0), new GradientStop(WithA(Colors.White, 0), 0.3),
                    new GradientStop(WithA(tint, 0.75), 0.65), new GradientStop(WithA(tint, 0), 0.85), new GradientStop(WithA(Colors.White, 0.4), 1),
                },
            };
            double rim = Math.Max(1, d * 0.025);
            dc.DrawEllipse(null, new Pen(rimBrush, rim), c, r - rim / 2, r - rim / 2);
        }

        private static void Blob(DrawingContext dc, Point c, double r, double angle, double dist, double size, Color color, double alpha)
        {
            var p = new Point(c.X + Math.Cos(angle) * r * dist, c.Y + Math.Sin(angle) * r * dist);
            var brush = new RadialGradientBrush { GradientStops = { new GradientStop(WithA(color, alpha), 0), new GradientStop(WithA(color, alpha * 0.45), 0.5), new GradientStop(WithA(color, 0), 1) } };
            dc.DrawEllipse(brush, null, p, r * size, r * size);
        }

        // ------------------------------------------------------------------ Eyes

        private void UpdateEyes(double dt)
        {
            // Glance somewhere new every second or two, and ease toward it.
            if (_t >= _nextGlance)
            {
                var rnd = Random.Shared;
                _lookTarget = new Point(rnd.NextDouble() * 1.8 - 0.9, rnd.NextDouble() * 1.2 - 0.6);
                if (_mood == OrbMood.Happy) _lookTarget = new Point(_lookTarget.X * 0.3, -0.2);
                _nextGlance = _t + 0.8 + rnd.NextDouble() * 1.6;
            }
            double k = Math.Min(1, dt * 9);
            _look = new Point(_look.X + (_lookTarget.X - _look.X) * k, _look.Y + (_lookTarget.Y - _look.Y) * k);
        }

        private double Blink()
        {
            if (_t >= _nextBlink + 0.16) _nextBlink = _t + 2.2 + Random.Shared.NextDouble() * 2.8;
            if (_t < _nextBlink) return 1;
            double x = (_t - _nextBlink) / 0.16;   // 0..1 through the blink
            return Math.Abs(1 - 2 * x);
        }

        /// <summary>Two digital eyes. look moves them (-1 to 1 each way), open is 0 (shut) to 1.</summary>
        public static void DrawEyes(DrawingContext dc, Point center, double h, Point look, double open, bool happy, Color color)
        {
            double w = h * 0.47, gap = h * 0.5;
            double dx = look.X * h * 0.28, dy = look.Y * h * 0.16;
            var brush = new SolidColorBrush(color);
            for (int i = 0; i < 2; i++)
            {
                double cx = center.X + dx + (i == 0 ? -(gap / 2 + w / 2) : gap / 2 + w / 2);
                double cy = center.Y + dy;
                if (happy)
                {
                    // An upside-down U for a smiling eye (^ ^).
                    double aw = w * 1.3, ah = h * 0.45;
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        ctx.BeginFigure(new Point(cx - aw / 2, cy + ah / 2), false, false);
                        ctx.QuadraticBezierTo(new Point(cx, cy + ah / 2 - ah * 1.6), new Point(cx + aw / 2, cy + ah / 2), true, true);
                    }
                    dc.DrawGeometry(null, new Pen(brush, w * 0.42) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, g);
                    continue;
                }
                double eh = Math.Max(w * 0.25, h * open);
                dc.DrawRoundedRectangle(brush, null, new Rect(cx - w / 2, cy - eh / 2, w, eh), w / 2, w / 2);
                if (open > 0.5)
                    dc.DrawEllipse(new SolidColorBrush(WithA(Colors.White, 0.92)), null, new Point(cx - w * 0.12, cy - h * open * 0.3), w * 0.16, w * 0.16);
            }
        }

        // ------------------------------------------------------------------ Helpers

        private static PathGeometry Arc(Point c, double r, double fromDeg, double toDeg)
        {
            double a0 = fromDeg * Math.PI / 180, a1 = toDeg * Math.PI / 180;
            var start = new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0));
            var end = new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1));
            bool large = Math.Abs(toDeg - fromDeg) > 180;
            var fig = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
            fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, large, SweepDirection.Clockwise, true));
            var geo = new PathGeometry();
            geo.Figures.Add(fig);
            return geo;
        }

        public static Color C(uint hex) => Color.FromRgb((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);
        public static Color WithA(Color c, double a) => Color.FromArgb((byte)Math.Round(Math.Clamp(a, 0, 1) * 255), c.R, c.G, c.B);

        private static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
            (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    /// <summary>A small still Ace orb, for next to Ace's chat bubbles.</summary>
    public sealed class OrbStill : FrameworkElement
    {
        public OrbMood Mood { get; set; } = OrbMood.Idle;

        protected override void OnRender(DrawingContext dc)
        {
            double d = Math.Min(ActualWidth, ActualHeight);
            if (d <= 0) return;
            AceOrb.DrawOrb(dc, new Point(ActualWidth / 2, ActualHeight / 2), d * 0.92, AceOrb.Tint(Mood), AceOrb.Tint2(Mood), 0.6, 0, AceOrb.Energy(Mood), bloom: false);
        }
    }
}
