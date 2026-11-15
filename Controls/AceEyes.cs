using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace Nimbus.Controls
{
    /// <summary>
    /// Ace V4's two digital eyes. Drawn in code and animated with springs every frame:
    /// they look around, blink, squint while thinking, go wide when something needs you, turn into happy arcs or
    /// hearts, droop when it's late, and pulse with your voice while Ace listens. When Ace isn't busy they follow your
    /// mouse around the screen, and while the focus timer runs a bar under them fills up (green for focus, blue for a break).
    /// </summary>
    public sealed class AceEyes : FrameworkElement
    {
        public enum Moods { Normal, Happy, Think, Alert, Sleepy, Love, Confused, Listen, Talk }

        /// <summary>Raised when the eyes dart somewhere new while wandering (x is -1 left … 1 right). Used for the servo sound.</summary>
        public event Action<double>? Looked;
        /// <summary>Raised on each blink (for the blink sound).</summary>
        public event Action? Blinked;

        private Moods _mood = Moods.Normal;
        private bool _wander;
        private double _level;
        private readonly Random _rng = new();
        private TimeSpan _last = TimeSpan.Zero;
        private bool _hooked;

        // Spring-driven values: current, velocity, target.
        private Sp _lookX = new(0), _lookY = new(0), _open = new(1), _lidTop = new(0), _lidBot = new(0),
                   _scaleL = new(1), _scaleR = new(1), _arc = new(0), _heart = new(0), _tilt = new(0),
                   _r = new(0x5B), _g = new(0xB6), _b = new(0xFF), _glow = new(0.55), _bounce = new(0);
        private double _nextWander, _nextBlink = 2, _blinkT = -1, _time, _talkT, _mouseT;
        private int _lastMouseX = int.MinValue, _lastMouseY = int.MinValue;
        private double _mouseStill;
        private bool _following;

        [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint p);

        /// <summary>Follow the mouse when Ace isn't busy (also needs "Eyes follow the mouse" on in settings).</summary>
        public bool FollowMouse { get; set; } = true;

        public AceEyes()
        {
            IsHitTestVisible = false;
            Loaded += (_, _) => Hook(true);
            Unloaded += (_, _) => Hook(false);
            IsVisibleChanged += (_, _) => Hook(IsVisible && IsLoaded);
        }

        public Moods Mood
        {
            get => _mood;
            set { if (_mood != value) { _mood = value; ApplyMood(); } }
        }

        /// <summary>Darting around "checking stuff".</summary>
        public bool Wander
        {
            get => _wander;
            set { _wander = value; _nextWander = 0; if (!value) LookAt(0, 0); }
        }

        /// <summary>Mic loudness 0..1 while listening.</summary>
        public double Level { get => _level; set => _level = Math.Clamp(value, 0, 1); }

        /// <summary>Where the eyes look now, -1 … 1.</summary>
        public double LookX => _lookX.V;

        public void LookAt(double x, double y)
        {
            _lookX.T = Math.Clamp(x, -1, 1);
            _lookY.T = Math.Clamp(y, -1, 1);
        }

        public void Blink() { _blinkT = 0; Blinked?.Invoke(); }

        /// <summary>A little hop, for "got it!".</summary>
        public void Hop() => _bounce.Vel -= 16;

        /// <summary>One "word" of talking: a small squash of the eyes.</summary>
        public void TalkPulse() => _talkT = 0.12;

        private void ApplyMood()
        {
            // Defaults, then each mood changes a few.
            double lidTop = 0, lidBot = 0, sl = 1, sr = 1, arc = 0, heart = 0, tilt = 0, glow = 0.55;
            Color c = Color.FromRgb(0x5B, 0xB6, 0xFF);
            switch (_mood)
            {
                case Moods.Happy: arc = 1; c = Color.FromRgb(0x7C, 0xD8, 0xFF); glow = 0.7; break;
                case Moods.Think: lidTop = 0.32; sl = 0.92; sr = 1.04; tilt = -0.06; c = Color.FromRgb(0x8F, 0x9C, 0xFF); LookAt(0.55, -0.6); break;
                case Moods.Alert: sl = sr = 1.14; c = Color.FromRgb(0xFF, 0xB0, 0x20); glow = 0.85; break;
                case Moods.Sleepy: lidTop = 0.55; lidBot = 0.05; c = Color.FromRgb(0x6F, 0x8F, 0xD8); glow = 0.35; break;
                case Moods.Love: heart = 1; c = Color.FromRgb(0xFF, 0x6F, 0xA8); glow = 0.8; break;
                case Moods.Confused: sl = 0.82; sr = 1.12; tilt = 0.08; c = Color.FromRgb(0xB8, 0x9C, 0xFF); break;
                case Moods.Listen: c = Color.FromRgb(0x9F, 0xF0, 0xFF); glow = 0.75; break;
                case Moods.Talk: c = Color.FromRgb(0x6F, 0xC8, 0xFF); glow = 0.65; break;
            }
            if (_mood != Moods.Think && !_wander) LookAt(0, 0);
            _lidTop.T = lidTop; _lidBot.T = lidBot; _scaleL.T = sl; _scaleR.T = sr; _arc.T = arc; _heart.T = heart; _tilt.T = tilt;
            _r.T = c.R; _g.T = c.G; _b.T = c.B; _glow.T = glow;
        }

        private void Hook(bool on)
        {
            if (on && !_hooked) { CompositionTarget.Rendering += OnFrame; _hooked = true; _last = TimeSpan.Zero; }
            else if (!on && _hooked) { CompositionTarget.Rendering -= OnFrame; _hooked = false; }
        }

        private void OnFrame(object? sender, EventArgs e)
        {
            var now = e is RenderingEventArgs r ? r.RenderingTime : TimeSpan.Zero;
            double dt = _last == TimeSpan.Zero ? 1 / 60.0 : Math.Clamp((now - _last).TotalSeconds, 0, 0.05);
            if (dt <= 0) return;
            _last = now;
            _time += dt;

            // Looking around while checking.
            if (_wander)
            {
                _nextWander -= dt;
                if (_nextWander <= 0)
                {
                    double x = _rng.NextDouble() < 0.15 ? 0 : (_rng.NextDouble() * 2 - 1) * 0.95;
                    double y = (_rng.NextDouble() * 2 - 1) * 0.55;
                    LookAt(x, y);
                    Looked?.Invoke(x);
                    _nextWander = 0.55 + _rng.NextDouble() * 0.9;
                    if (_rng.NextDouble() < 0.18) _nextBlink = 0.2;
                }
            }

            // Following the mouse while idle.
            bool canFollow = FollowMouse && !_wander && (_mood is Moods.Normal or Moods.Happy) && _talkT <= 0 && IsVisible && FollowEnabled();
            if (canFollow)
            {
                _mouseT -= dt;
                if (_mouseT <= 0) { _mouseT = 1 / 30.0; TrackMouse(); }
            }
            else if (_following) { _following = false; if (!_wander && _mood != Moods.Think) LookAt(0, 0); }

            // Focus timer: a determined squint while focusing.
            if (_mood == Moods.Normal) _lidTop.T = Nimbus.AceV4.Pc.FocusTimer.Phase == Nimbus.AceV4.Pc.FocusPhase.Focus && !Nimbus.AceV4.Pc.FocusTimer.Paused ? 0.14 : 0;

            // Blinking.
            _nextBlink -= dt;
            if (_nextBlink <= 0 && _blinkT < 0 && _mood is not (Moods.Happy or Moods.Love))
            {
                Blink();
                _nextBlink = _mood == Moods.Sleepy ? 1.5 + _rng.NextDouble() * 2 : 2.8 + _rng.NextDouble() * 3.5;
            }
            if (_blinkT >= 0)
            {
                _blinkT += dt;
                double b = _blinkT / 0.16;
                _open.T = b < 0.5 ? 0.06 : 1;
                if (b >= 1.4) { _blinkT = -1; _open.T = 1; }
            }

            // Talking: tiny squashes; listening: pulse with the voice.
            if (_talkT > 0) _talkT -= dt;
            double extra = _mood == Moods.Listen ? _level * 0.28 : 0;
            if (_mood == Moods.Alert) _tilt.T = Math.Sin(_time * 30) * 0.015;   // a tiny jitter

            const double stiff = 170, damp = 18;
            _lookX.Step(dt, stiff, damp); _lookY.Step(dt, stiff, damp);
            _open.Step(dt, 900, 40);
            _lidTop.Step(dt, stiff, damp); _lidBot.Step(dt, stiff, damp);
            _scaleL.T = (_mood == Moods.Confused ? 0.82 : _mood == Moods.Alert ? 1.14 : _mood == Moods.Think ? 0.92 : 1) + extra;
            _scaleR.T = (_mood == Moods.Confused ? 1.12 : _mood == Moods.Alert ? 1.14 : _mood == Moods.Think ? 1.04 : 1) + extra;
            _scaleL.Step(dt, 260, 16); _scaleR.Step(dt, 260, 16);
            _arc.Step(dt, 140, 20); _heart.Step(dt, 140, 18); _tilt.Step(dt, 120, 14);
            _r.Step(dt, 60, 16); _g.Step(dt, 60, 16); _b.Step(dt, 60, 16); _glow.Step(dt, 60, 16);
            _bounce.Step(dt, 300, 14);
            InvalidateVisual();
        }

        private static bool FollowEnabled()
        {
            try { return Nimbus.AceV4.AceRuntime.Current.Config.EyesFollowMouse; } catch { return true; }
        }

        /// <summary>Points the eyes at the mouse. After 6 seconds of a still mouse they drift back to the middle.</summary>
        private void TrackMouse()
        {
            if (!GetCursorPos(out var p)) return;
            Point center;
            try { center = PointToScreen(new Point(ActualWidth / 2, ActualHeight / 2)); }
            catch (InvalidOperationException) { return; }   // not on screen yet
            bool moved = Math.Abs(p.X - _lastMouseX) + Math.Abs(p.Y - _lastMouseY) > 2;
            _lastMouseX = p.X; _lastMouseY = p.Y;
            _mouseStill = moved ? 0 : _mouseStill + 1 / 30.0;
            if (_mouseStill > 6) { if (_following) { _following = false; LookAt(0, 0); } return; }
            if (!moved && !_following) return;
            _following = true;
            // Device pixels from the eyes to the mouse; tanh keeps it soft near the middle and capped far away.
            double x = Math.Tanh((p.X - center.X) / 650.0);
            double y = Math.Tanh((p.Y - center.Y) / 520.0) * 0.8;
            LookAt(x, y);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 4 || h < 4) return;
            var color = Color.FromRgb(Clamp(_r.V), Clamp(_g.V), Clamp(_b.V));
            double eyeW = w * 0.26, eyeH = h * 0.66;
            double dx = _lookX.V * w * 0.11, dy = _lookY.V * h * 0.14 + _bounce.V;
            double talk = _talkT > 0 ? 0.88 : 1;
            double breathe = _mood == Moods.Sleepy ? 1 + Math.Sin(_time * 1.6) * 0.04 : 1 + Math.Sin(_time * 2.2) * 0.012;

            for (int side = 0; side < 2; side++)
            {
                double cx = w * (side == 0 ? 0.3 : 0.7) + dx;
                double cy = h * 0.5 + dy;
                double scale = (side == 0 ? _scaleL.V : _scaleR.V) * breathe;
                // The eye on the side Ace is looking toward gets a bit bigger, like it turned its head.
                scale *= 1 + (side == 0 ? -_lookX.V : _lookX.V) * 0.06;
                double ew = eyeW * scale, eh = eyeH * scale * Math.Max(0.04, _open.V) * talk;

                dc.PushTransform(new RotateTransform((side == 0 ? -1 : 1) * _tilt.V * 57.3, cx, cy));

                // Soft glow behind the eye.
                var glow = new RadialGradientBrush(Color.FromArgb((byte)(Math.Clamp(_glow.V, 0, 1) * 150), color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B));
                dc.DrawEllipse(glow, null, new Point(cx, cy), ew * 1.25, eyeH * scale * 0.95);

                double shape = Math.Max(_arc.V, _heart.V);
                if (shape < 0.98)
                {
                    dc.PushOpacity(1 - shape);
                    DrawEye(dc, color, cx, cy, ew, eh);
                    dc.Pop();
                }
                if (_arc.V > 0.02)
                {
                    dc.PushOpacity(Math.Min(1, _arc.V));
                    // Happy "^" arcs.
                    var pen = new Pen(new SolidColorBrush(color), Math.Max(2, ew * 0.26)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                    var g = new StreamGeometry();
                    using (var s = g.Open())
                    {
                        s.BeginFigure(new Point(cx - ew * 0.55, cy + eyeH * 0.12), false, false);
                        s.QuadraticBezierTo(new Point(cx, cy - eyeH * 0.55), new Point(cx + ew * 0.55, cy + eyeH * 0.12), true, true);
                    }
                    g.Freeze();
                    dc.DrawGeometry(null, pen, g);
                    dc.Pop();
                }
                if (_heart.V > 0.02)
                {
                    dc.PushOpacity(Math.Min(1, _heart.V));
                    double s = ew * 0.62 * (1 + Math.Sin(_time * 7) * 0.05);
                    dc.DrawGeometry(new SolidColorBrush(color), null, Heart(cx, cy, s));
                    dc.Pop();
                }
                dc.Pop();
            }

            // Focus timer bar under the eyes.
            var focus = Nimbus.AceV4.Pc.FocusTimer.Phase;
            if (focus != Nimbus.AceV4.Pc.FocusPhase.Off)
            {
                double progress = Nimbus.AceV4.Pc.FocusTimer.Progress;
                bool rest = focus != Nimbus.AceV4.Pc.FocusPhase.Focus;
                var barColor = rest ? Color.FromRgb(0x5B, 0xB6, 0xFF) : Color.FromRgb(0x4A, 0xDE, 0x80);
                double barH = Math.Max(2.5, h * 0.07), left = w * 0.2, width = w * 0.6, top = h - barH - 1;
                double r2 = barH / 2;
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(55, barColor.R, barColor.G, barColor.B)), null, new Rect(left, top, width, barH), r2, r2);
                double filled = Math.Max(barH, width * progress);
                byte alpha = (byte)(Nimbus.AceV4.Pc.FocusTimer.Paused ? 120 + 80 * Math.Abs(Math.Sin(_time * 2)) : 230);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(alpha, barColor.R, barColor.G, barColor.B)), null, new Rect(left, top, filled, barH), r2, r2);
            }
        }

        private void DrawEye(DrawingContext dc, Color color, double cx, double cy, double ew, double eh)
        {
            var rect = new Rect(cx - ew / 2, cy - eh / 2, ew, eh);
            double radius = Math.Min(ew, eh) * 0.38;
            // Lids: clip away the top (and bottom) of the eye.
            double top = rect.Top + eh * Math.Clamp(_lidTop.V, 0, 0.9), bottom = rect.Bottom - eh * Math.Clamp(_lidBot.V, 0, 0.9);
            dc.PushClip(new RectangleGeometry(new Rect(rect.Left - 2, top, rect.Width + 4, Math.Max(0.5, bottom - top))));
            var fill = new LinearGradientBrush(Lighter(color, 0.35), color, 90);
            dc.DrawRoundedRectangle(fill, null, rect, radius, radius);
            // Digital scanlines.
            var line = new SolidColorBrush(Color.FromArgb(38, 0, 0, 30));
            for (double y = rect.Top + 2; y < rect.Bottom; y += 4) dc.DrawRectangle(line, null, new Rect(rect.Left, y, rect.Width, 1.2));
            // Shine.
            var shine = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
            double sw = ew * 0.26, sh = Math.Min(eh * 0.22, ew * 0.3);
            if (eh > 6) dc.DrawRoundedRectangle(shine, null, new Rect(rect.Left + ew * 0.18, rect.Top + eh * 0.14, sw, sh), sw * 0.4, sw * 0.4);
            dc.Pop();
            if (_lidTop.V > 0.05)
            {
                // A soft lid line so a squint reads clearly.
                var lid = new Pen(new SolidColorBrush(Color.FromArgb(160, color.R, color.G, color.B)), 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawLine(lid, new Point(rect.Left + 1, top), new Point(rect.Right - 1, top));
            }
        }

        private static Geometry Heart(double cx, double cy, double s)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(cx, cy + s * 0.95), true, true);
                c.BezierTo(new Point(cx - s * 1.5, cy + s * 0.1), new Point(cx - s * 0.9, cy - s * 1.05), new Point(cx, cy - s * 0.35), true, true);
                c.BezierTo(new Point(cx + s * 0.9, cy - s * 1.05), new Point(cx + s * 1.5, cy + s * 0.1), new Point(cx, cy + s * 0.95), true, true);
            }
            g.Freeze();
            return g;
        }

        private static Color Lighter(Color c, double amount) => Color.FromRgb(
            (byte)(c.R + (255 - c.R) * amount), (byte)(c.G + (255 - c.G) * amount), (byte)(c.B + (255 - c.B) * amount));

        private static byte Clamp(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

        /// <summary>A damped spring: value, velocity and target.</summary>
        private struct Sp
        {
            public double V, Vel, T;
            public Sp(double v) { V = v; T = v; Vel = 0; }
            public void Step(double dt, double stiffness, double damping)
            {
                double a = stiffness * (T - V) - damping * Vel;
                Vel += a * dt;
                V += Vel * dt;
            }
        }
    }
}
