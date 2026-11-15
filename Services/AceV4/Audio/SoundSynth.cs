using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Nimbus.AceV4.Audio
{
    /// <summary>Every Ace V4 sound. All of them are made from scratch in code (no sound files needed).</summary>
    public enum Sfx
    {
        Wake, EyesOut, EyesIn, Blink, Look, Think, Success, Alert, ListenStart, ListenStop,
        Tick, Greet, Countdown, CountdownFinal, Sleep, Error, Pop, Notify, Shutdown,
    }

    /// <summary>A stereo sound: left and right channels at 48 kHz, -1 to 1.</summary>
    public sealed class StereoBuffer
    {
        public float[] L;
        public float[] R;
        public int Length => L.Length;
        public double Seconds => L.Length / (double)SoundSynth.Rate;
        public StereoBuffer(int frames) { L = new float[frames]; R = new float[frames]; }
    }

    /// <summary>
    /// The sound designer. Layers oscillators (sine, triangle, saw, FM bells), filtered noise, servo whirs and clicks,
    /// places each layer in stereo, then adds a small room reverb so the sounds feel like they're in the space around you.
    /// Every call can take a pitch nudge and a seed, so the same sound is never quite identical twice.
    /// </summary>
    public static class SoundSynth
    {
        public const int Rate = 48000;

        public static bool Loops(Sfx s) => s == Sfx.Think;

        /// <summary>Makes one sound. pitch 1 = normal; seed picks the random details.</summary>
        public static StereoBuffer Make(Sfx s, double pitch = 1, int seed = 1)
        {
            var rng = new Random(seed);
            var m = s switch
            {
                Sfx.Wake => Wake(pitch, rng),
                Sfx.EyesOut => EyesOut(pitch, rng),
                Sfx.EyesIn => EyesIn(pitch, rng),
                Sfx.Blink => Blink(pitch, rng),
                Sfx.Look => Look(pitch, rng),
                Sfx.Think => Think(rng),
                Sfx.Success => Success(pitch, rng),
                Sfx.Alert => Alert(pitch, rng),
                Sfx.ListenStart => Listen(pitch, rng, up: true),
                Sfx.ListenStop => Listen(pitch, rng, up: false),
                Sfx.Tick => Tick(pitch, rng),
                Sfx.Greet => Greet(pitch, rng),
                Sfx.Countdown => Countdown(pitch, rng),
                Sfx.CountdownFinal => CountdownFinal(pitch, rng),
                Sfx.Sleep => SleepSound(pitch, rng),
                Sfx.Error => Error(pitch, rng),
                Sfx.Pop => Pop(pitch, rng),
                Sfx.Notify => Notify(pitch, rng),
                Sfx.Shutdown => Shutdown(pitch, rng),
                _ => new Mix(0.1),
            };
            var b = m.Finish(s == Sfx.Think);
            Normalize(b, s switch
            {
                Sfx.Think => 0.28, Sfx.Tick => 0.3, Sfx.Blink => 0.35, Sfx.Look => 0.4, Sfx.Pop => 0.4,
                Sfx.ListenStart or Sfx.ListenStop => 0.45, Sfx.Countdown => 0.5, Sfx.Greet or Sfx.Wake => 0.7, _ => 0.6,
            });
            return b;
        }

        /// <summary>Scales a sound so its loudest point is at the given level, so every sound sits well together.</summary>
        private static void Normalize(StereoBuffer b, double target)
        {
            float peak = 0;
            for (int i = 0; i < b.Length; i++) peak = Math.Max(peak, Math.Max(Math.Abs(b.L[i]), Math.Abs(b.R[i])));
            if (peak < 1e-5f) return;
            float g = (float)(target / peak);
            for (int i = 0; i < b.Length; i++) { b.L[i] *= g; b.R[i] *= g; }
        }

        // ===================================================================== The sounds

        private static Mix Wake(double p, Random rng)
        {
            var m = new Mix(2.0, reverb: 0.32);
            m.Add(0, Osc.Sub(58 * p, 0.35, 0.55));                                          // soft thump you feel
            m.Add(0.02, Noise.Sweep(rng, 0.65, 300, 7000, q: 1.6, gain: 0.22), -0.25, width: 0.6);   // air rushing up
            double[] notes = { 523.25, 659.25, 783.99, 1046.5 };                            // C E G C, rising
            for (int i = 0; i < notes.Length; i++)
            {
                double pan = -0.45 + i * 0.3;
                m.Add(0.12 + i * 0.085, Osc.Bell(notes[i] * p, 1.0, 0.22, ratio: 2.0, index: 2.2, decay: 3.2), pan);
                m.Add(0.12 + i * 0.085, Osc.Tone(notes[i] * p * 1.004, 0.9, 0.06, Wave.Triangle, a: 0.01, d: 0.9), -pan);
            }
            m.Add(0.5, Osc.Shimmer(rng, 2093 * p, 1.2, 0.07), 0, width: 1);
            return m;
        }

        private static Mix EyesOut(double p, Random rng)
        {
            var m = new Mix(0.95, reverb: 0.18);
            m.Add(0, Noise.Sweep(rng, 0.42, 900, 3800, q: 3, gain: 0.16), 0, width: 0.5);       // sliding out
            m.Add(0.02, Osc.Servo(rng, 0.38, 210 * p, 300 * p, 0.2), -0.35);
            m.Add(0.05, Osc.Servo(rng, 0.36, 214 * p, 306 * p, 0.2), 0.35);
            m.Add(0.42, Osc.Click(rng, 0.16, 1600 * p), -0.55);                                // left eye locks in
            m.Add(0.47, Osc.Click(rng, 0.16, 1750 * p), 0.55);                                 // right eye locks in
            m.Add(0.5, Osc.Tone(1318.5 * p, 0.35, 0.07, Wave.Sine, a: 0.005, d: 0.3), 0, width: 0.4);   // "online" ping
            return m;
        }

        private static Mix EyesIn(double p, Random rng)
        {
            var m = new Mix(0.8, reverb: 0.15);
            m.Add(0, Osc.Servo(rng, 0.32, 300 * p, 190 * p, 0.2), -0.3);
            m.Add(0.03, Osc.Servo(rng, 0.3, 306 * p, 192 * p, 0.2), 0.3);
            m.Add(0, Noise.Sweep(rng, 0.35, 3200, 600, q: 3, gain: 0.14), 0, width: 0.5);
            m.Add(0.34, Osc.Clunk(rng, 0.25), 0);
            return m;
        }

        private static Mix Blink(double p, Random rng)
        {
            var m = new Mix(0.22, reverb: 0.08);
            m.Add(0, Osc.Tone(2600 * p, 0.03, 0.09, Wave.Sine, a: 0.001, d: 0.03), -0.2);
            m.Add(0.06, Osc.Tone(2900 * p, 0.03, 0.08, Wave.Sine, a: 0.001, d: 0.03), 0.2);
            m.Add(0, Noise.Sweep(rng, 0.1, 5000, 2500, q: 2, gain: 0.05), 0);
            return m;
        }

        private static Mix Look(double p, Random rng)
        {
            var m = new Mix(0.5, reverb: 0.12);
            double up = 1 + rng.NextDouble() * 0.25;
            m.Add(0, Osc.Servo(rng, 0.34, 240 * p, 240 * p * up, 0.17), 0, width: 0.2);
            m.Add(0.33, Osc.Click(rng, 0.07, 2200 * p), 0);
            return m;
        }

        /// <summary>A calm 4-second "processing" bed that loops without a seam.</summary>
        private static Mix Think(Random rng)
        {
            const double len = 4.0;
            var m = new Mix(len, reverb: 0.25);
            // Frequencies that fit a whole number of cycles in 4 s, so the loop point is invisible.
            m.Add(0, Osc.Pad(len, 110, 0.12, lfoHz: 0.5), -0.3);
            m.Add(0, Osc.Pad(len, 165, 0.08, lfoHz: 0.25, lfoPhase: 0.5), 0.3);
            m.Add(0, Osc.Pad(len, 220.25, 0.035, lfoHz: 0.75), 0, width: 0.8);
            // Little data blips drifting around the stereo field.
            int blips = 9;
            for (int i = 0; i < blips; i++)
            {
                double at = 0.15 + i * (len - 0.5) / blips + rng.NextDouble() * 0.12;
                double f = new[] { 1760.0, 2093.0, 2349.3, 2637.0, 3136.0 }[rng.Next(5)];
                m.Add(at, Osc.Tone(f, 0.09, 0.035, Wave.Sine, a: 0.003, d: 0.08), rng.NextDouble() * 1.6 - 0.8);
            }
            m.Add(0, Noise.Breath(rng, len, 0.03), 0, width: 1);
            return m;
        }

        private static Mix Success(double p, Random rng)
        {
            var m = new Mix(1.6, reverb: 0.3);
            m.Add(0, Osc.Bell(1318.5 * p, 1.2, 0.2, ratio: 3.5, index: 1.4, decay: 4), -0.3);   // E6
            m.Add(0.09, Osc.Bell(1975.5 * p, 1.2, 0.18, ratio: 3.5, index: 1.2, decay: 4), 0.3); // B6
            m.Add(0.09, Osc.Tone(987.8 * p, 0.8, 0.05, Wave.Triangle, a: 0.01, d: 0.7), 0, width: 0.5);
            m.Add(0.1, Osc.Shimmer(rng, 3951 * p, 0.6, 0.04), 0, width: 1);
            return m;
        }

        private static Mix Alert(double p, Random rng)
        {
            var m = new Mix(1.1, reverb: 0.2);
            for (int i = 0; i < 2; i++)
            {
                double t = i * 0.26;
                m.Add(t, Osc.Tone(880 * p, 0.12, 0.17, Wave.Triangle, a: 0.005, d: 0.12, endHz: 870 * p), -0.15);
                m.Add(t + 0.12, Osc.Tone(659.25 * p, 0.16, 0.17, Wave.Triangle, a: 0.005, d: 0.16), 0.15);
            }
            m.Add(0, Osc.Sub(70 * p, 0.25, 0.25));
            m.Add(0.02, Noise.Sweep(rng, 0.2, 2500, 1200, q: 4, gain: 0.05), 0, width: 0.6);
            return m;
        }

        private static Mix Listen(double p, Random rng, bool up)
        {
            var m = new Mix(0.45, reverb: 0.15);
            double a = up ? 620 : 1240, b = up ? 1240 : 620;
            m.Add(0, Osc.Tone(a * p, 0.16, 0.16, Wave.Sine, a: 0.004, d: 0.16, endHz: b * p), 0, width: 0.3);
            m.Add(0.005, Osc.Tone(a * p * 2.003, 0.12, 0.04, Wave.Sine, a: 0.004, d: 0.12, endHz: b * p * 2.003), 0, width: 0.6);
            m.Add(up ? 0.15 : 0, Osc.Click(rng, 0.05, up ? 2600 : 1800), 0);
            return m;
        }

        private static Mix Tick(double p, Random rng)
        {
            var m = new Mix(0.06, reverb: 0);
            m.Add(0, Osc.Click(rng, 0.09, (2400 + rng.NextDouble() * 900) * p), rng.NextDouble() * 0.4 - 0.2);
            return m;
        }

        private static Mix Greet(double p, Random rng)
        {
            var m = new Mix(3.0, reverb: 0.38);
            m.Add(0, Osc.Sub(65 * p, 0.6, 0.4));
            m.Add(0, Noise.Sweep(rng, 0.9, 200, 6000, q: 1.4, gain: 0.16), 0, width: 0.8);
            double[] arp = { 392.0, 523.25, 659.25, 783.99, 1046.5 };   // G C E G C
            for (int i = 0; i < arp.Length; i++)
                m.Add(0.18 + i * 0.11, Osc.Bell(arp[i] * p, 0.9, 0.16, ratio: 2, index: 1.8, decay: 3.5), -0.5 + i * 0.25);
            double[] chord = { 261.63, 329.63, 392.0, 523.25 };          // big warm C chord to finish
            foreach (var f in chord)
            {
                m.Add(0.75, Osc.Tone(f * p, 2.0, 0.05, Wave.Saw, a: 0.25, d: 1.75, lowpass: 2400), -0.4, width: 0.2);
                m.Add(0.75, Osc.Tone(f * p * 1.006, 2.0, 0.05, Wave.Saw, a: 0.25, d: 1.75, lowpass: 2400), 0.4, width: 0.2);
            }
            m.Add(0.8, Osc.Shimmer(rng, 2093 * p, 1.8, 0.06), 0, width: 1);
            return m;
        }

        private static Mix Countdown(double p, Random rng)
        {
            var m = new Mix(0.3, reverb: 0.2);
            m.Add(0, Osc.Tone(1180 * p, 0.09, 0.2, Wave.Sine, a: 0.001, d: 0.08), 0);
            m.Add(0, Osc.Tone(2360 * p, 0.05, 0.05, Wave.Sine, a: 0.001, d: 0.04), 0);
            m.Add(0, Osc.Click(rng, 0.1, 3000), 0);
            return m;
        }

        private static Mix CountdownFinal(double p, Random rng)
        {
            var m = new Mix(1.3, reverb: 0.3);
            m.Add(0, Osc.Tone(587.3 * p, 0.8, 0.2, Wave.Triangle, a: 0.005, d: 0.8), -0.2);
            m.Add(0, Osc.Tone(440 * p, 0.9, 0.18, Wave.Triangle, a: 0.005, d: 0.9), 0.2);
            m.Add(0, Osc.Sub(55 * p, 0.6, 0.5));
            return m;
        }

        private static Mix SleepSound(double p, Random rng)
        {
            var m = new Mix(1.7, reverb: 0.3);
            m.Add(0, Noise.Sweep(rng, 1.0, 4500, 250, q: 1.8, gain: 0.2), 0, width: 0.8);
            m.Add(0.05, Osc.Tone(784 * p, 0.9, 0.11, Wave.Sine, a: 0.02, d: 0.9, endHz: 196 * p), -0.25);
            m.Add(0.12, Osc.Tone(523.25 * p, 0.9, 0.08, Wave.Sine, a: 0.02, d: 0.9, endHz: 131 * p), 0.25);
            m.Add(0.95, Osc.Clunk(rng, 0.2), 0);
            return m;
        }

        private static Mix Error(double p, Random rng)
        {
            var m = new Mix(0.6, reverb: 0.15);
            m.Add(0, Osc.Tone(440 * p, 0.14, 0.14, Wave.Square, a: 0.003, d: 0.14, lowpass: 1500), -0.1);
            m.Add(0.15, Osc.Tone(311 * p, 0.22, 0.14, Wave.Square, a: 0.003, d: 0.22, lowpass: 1200), 0.1);
            return m;
        }

        private static Mix Pop(double p, Random rng)
        {
            var m = new Mix(0.18, reverb: 0.1);
            m.Add(0, Osc.Tone(320 * p, 0.07, 0.22, Wave.Sine, a: 0.002, d: 0.07, endHz: 980 * p), 0, width: 0.3);
            return m;
        }

        private static Mix Notify(double p, Random rng)
        {
            var m = new Mix(0.9, reverb: 0.25);
            m.Add(0, Osc.Marimba(783.99 * p, 0.5, 0.2), -0.25);
            m.Add(0.13, Osc.Marimba(1174.7 * p, 0.6, 0.2), 0.25);
            return m;
        }

        private static Mix Shutdown(double p, Random rng)
        {
            var m = new Mix(2.0, reverb: 0.3);
            m.Add(0, Osc.Tone(440 * p, 1.4, 0.18, Wave.Triangle, a: 0.01, d: 1.4, endHz: 50 * p), 0, width: 0.4);
            m.Add(0, Osc.Tone(660 * p, 1.2, 0.08, Wave.Sine, a: 0.01, d: 1.2, endHz: 70 * p), 0, width: 0.6);
            m.Add(0, Noise.Sweep(rng, 1.3, 3000, 150, q: 2, gain: 0.14), 0, width: 0.8);
            m.Add(1.35, Osc.Clunk(rng, 0.3), 0);
            return m;
        }

        // ===================================================================== WAV export

        /// <summary>Writes the sound as a 16-bit stereo WAV (for the preview files, or to use as a Windows sound).</summary>
        public static void WriteWav(StereoBuffer b, string path)
        {
            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs, Encoding.ASCII);
            int bytes = b.Length * 4;
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)2);
            w.Write(Rate); w.Write(Rate * 4); w.Write((short)4); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes);
            for (int i = 0; i < b.Length; i++)
            {
                w.Write((short)Math.Round(Math.Clamp(b.L[i], -1f, 1f) * 32767));
                w.Write((short)Math.Round(Math.Clamp(b.R[i], -1f, 1f) * 32767));
            }
        }
    }

    // ========================================================================= Building blocks

    internal enum Wave { Sine, Triangle, Saw, Square }

    /// <summary>A mono layer: samples plus where it sits in the mix.</summary>
    internal sealed class Layer
    {
        public float[] S;
        public Layer(int n) => S = new float[n];
        public Layer(float[] s) => S = s;
    }

    /// <summary>A stereo mix being built: layers are added at a time and pan, then reverb and a soft limiter finish it.</summary>
    internal sealed class Mix
    {
        private readonly float[] _l, _r;
        private readonly double _reverb;
        public Mix(double seconds, double reverb = 0.2)
        {
            int n = (int)(seconds * SoundSynth.Rate);
            _l = new float[n];
            _r = new float[n];
            _reverb = reverb;
        }

        /// <summary>Adds a layer. pan -1 left … 1 right. width spreads it with a tiny delay on one side (feels wider, like it's in the room).</summary>
        public void Add(double atSeconds, Layer layer, double pan = 0, double width = 0)
        {
            int start = (int)(atSeconds * SoundSynth.Rate);
            double angle = (Math.Clamp(pan, -1, 1) + 1) * Math.PI / 4;   // constant-power panning
            float gl = (float)Math.Cos(angle), gr = (float)Math.Sin(angle);
            int haas = (int)(width * 0.012 * SoundSynth.Rate);           // up to 12 ms
            var s = layer.S;
            for (int i = 0; i < s.Length; i++)
            {
                int li = start + i, ri = start + i + haas;
                if (li >= 0 && li < _l.Length) _l[li] += s[i] * gl;
                if (ri >= 0 && ri < _r.Length) _r[ri] += s[i] * gr;
            }
        }

        public StereoBuffer Finish(bool loop)
        {
            if (_reverb > 0) Reverb.Apply(_l, _r, _reverb, loop);
            var b = new StereoBuffer(_l.Length);
            // Short fades at both ends so nothing clicks; a soft limiter so layers never clip.
            int fade = loop ? 0 : Math.Min(240, _l.Length / 4);
            for (int i = 0; i < _l.Length; i++)
            {
                float g = 1;
                if (fade > 0 && i < 24) g = i / 24f;
                if (fade > 0 && i > _l.Length - fade) g = (_l.Length - i) / (float)fade;
                b.L[i] = SoftClip(_l[i] * g);
                b.R[i] = SoftClip(_r[i] * g);
            }
            if (loop) Seam(b);
            return b;
        }

        private static float SoftClip(float x) => (float)Math.Tanh(x * 1.1) / 1.1f * 1.0f;

        /// <summary>Crossfades the last 150 ms into the first 150 ms, so a loop has no click where it wraps.</summary>
        private static void Seam(StereoBuffer b)
        {
            int n = Math.Min(SoundSynth.Rate * 15 / 100, b.Length / 4);
            int end = b.Length - n;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)n;
                float a = (float)Math.Sqrt(t), c = (float)Math.Sqrt(1 - t);
                b.L[i] = b.L[i] * a + b.L[end + i] * c;
                b.R[i] = b.R[i] * a + b.R[end + i] * c;
            }
            Array.Resize(ref b.L, end);
            Array.Resize(ref b.R, end);
        }
    }

    internal static class Osc
    {
        private const int Rate = SoundSynth.Rate;

        /// <summary>A note with a glide (endHz), attack/decay envelope and optional low-pass.</summary>
        public static Layer Tone(double hz, double seconds, double gain, Wave wave, double a = 0.005, double d = 0.2, double endHz = 0, double lowpass = 0)
        {
            int n = (int)(seconds * Rate);
            var l = new Layer(n);
            double phase = 0, end = endHz > 0 ? endHz : hz;
            double lp = 0, lpA = lowpass > 0 ? 1 - Math.Exp(-2 * Math.PI * lowpass / Rate) : 1;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                double f = hz * Math.Pow(end / hz, t / seconds);   // smooth exponential glide
                phase += f / Rate;
                phase -= Math.Floor(phase);
                double v = wave switch
                {
                    Wave.Sine => Math.Sin(2 * Math.PI * phase),
                    Wave.Triangle => 1 - 4 * Math.Abs(phase - 0.5),
                    Wave.Saw => 2 * phase - 1 - PolyBlep(phase, f / Rate),
                    _ => (phase < 0.5 ? 1 : -1) + PolyBlep(phase, f / Rate) - PolyBlep((phase + 0.5) % 1, f / Rate),
                };
                if (lowpass > 0) { lp += lpA * (v - lp); v = lp; }
                l.S[i] = (float)(v * gain * Env(t, seconds, a, d));
            }
            return l;
        }

        /// <summary>Attack, then an exponential fall over d seconds, then a short release to zero at the end.</summary>
        private static double Env(double t, double total, double a, double d)
        {
            double e = t < a ? t / a : Math.Exp(-4.0 * (t - a) / Math.Max(0.001, d));
            double tail = total - t;
            if (tail < 0.01) e *= Math.Max(0, tail / 0.01);
            return e;
        }

        private static double PolyBlep(double t, double dt)
        {
            if (dt <= 0) return 0;
            if (t < dt) { t /= dt; return t + t - t * t - 1; }
            if (t > 1 - dt) { t = (t - 1) / dt; return t * t + t + t + 1; }
            return 0;
        }

        /// <summary>FM bell: shiny, glassy notes.</summary>
        public static Layer Bell(double hz, double seconds, double gain, double ratio = 2, double index = 2, double decay = 3)
        {
            int n = (int)(seconds * Rate);
            var l = new Layer(n);
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                double env = Math.Min(1, t / 0.003) * Math.Exp(-decay * t);
                double mod = index * env * Math.Sin(2 * Math.PI * hz * ratio * t);
                double tail = seconds - t;
                l.S[i] = (float)(gain * env * Math.Sin(2 * Math.PI * hz * t + mod) * (tail < 0.02 ? tail / 0.02 : 1));
            }
            return l;
        }

        /// <summary>A wooden mallet note.</summary>
        public static Layer Marimba(double hz, double seconds, double gain)
        {
            int n = (int)(seconds * Rate);
            var l = new Layer(n);
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                double v = Math.Sin(2 * Math.PI * hz * t) * Math.Exp(-7 * t)
                         + 0.35 * Math.Sin(2 * Math.PI * hz * 4 * t) * Math.Exp(-20 * t)
                         + 0.15 * Math.Sin(2 * Math.PI * hz * 9.2 * t) * Math.Exp(-40 * t);
                double tail = seconds - t;
                l.S[i] = (float)(gain * v * Math.Min(1, t / 0.002) * (tail < 0.02 ? tail / 0.02 : 1));
            }
            return l;
        }

        /// <summary>A deep sine "thump" with a quick pitch drop.</summary>
        public static Layer Sub(double hz, double seconds, double gain)
        {
            int n = (int)(seconds * Rate);
            var l = new Layer(n);
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                double f = hz * (1 + 1.5 * Math.Exp(-25 * t));
                phase += f / Rate;
                double env = Math.Min(1, t / 0.004) * Math.Exp(-6 * t / seconds);
                double tail = seconds - t;
                l.S[i] = (float)(gain * env * Math.Sin(2 * Math.PI * phase) * (tail < 0.02 ? tail / 0.02 : 1));
            }
            return l;
        }

        /// <summary>A little motor: buzzy saw through a resonant filter, with wobble and noise.</summary>
        public static Layer Servo(Random rng, double seconds, double fromHz, double toHz, double gain)
        {
            int n = (int)(seconds * Rate);
            var l = new Layer(n);
            var svf = new Svf();
            double phase = 0, wob = rng.NextDouble() * 6;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate, x = t / seconds;
                double f = fromHz + (toHz - fromHz) * (x * x * (3 - 2 * x)) + 6 * Math.Sin(2 * Math.PI * 23 * t + wob);
                phase += f / Rate;
                phase -= Math.Floor(phase);
                double v = (2 * phase - 1) * 0.7 + (rng.NextDouble() * 2 - 1) * 0.3;
                v = svf.BandPass(v, f * 4.5, 2.2);
                double env = Math.Sin(Math.PI * Math.Min(1, x)) * 0.85 + 0.15 * (1 - x);
                l.S[i] = (float)(gain * v * env * Math.Min(1, t / 0.01) * Math.Min(1, (seconds - t) / 0.02));
            }
            return l;
        }

        /// <summary>A crisp mechanical click.</summary>
        public static Layer Click(Random rng, double gain, double hz)
        {
            int n = (int)(0.025 * Rate);
            var l = new Layer(n);
            var svf = new Svf();
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                double v = svf.BandPass(rng.NextDouble() * 2 - 1, hz, 3) * 1.8 + Math.Sin(2 * Math.PI * hz * 0.5 * t) * 0.4;
                l.S[i] = (float)(gain * v * Math.Exp(-t * 260));
            }
            return l;
        }

        /// <summary>A soft, heavy "clunk" (something docking).</summary>
        public static Layer Clunk(Random rng, double gain)
        {
            int n = (int)(0.22 * Rate);
            var l = new Layer(n);
            var svf = new Svf();
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                double body = Math.Sin(2 * Math.PI * (90 + 120 * Math.Exp(-40 * t)) * t) * Math.Exp(-18 * t);
                double knock = svf.LowPass(rng.NextDouble() * 2 - 1, 900, 0.8) * Math.Exp(-60 * t);
                l.S[i] = (float)(gain * (body + knock * 0.8) * Math.Min(1, (0.22 - t) / 0.02));
            }
            return l;
        }

        /// <summary>A sparkly cluster of tiny high bells, scattered in time.</summary>
        public static Layer Shimmer(Random rng, double hz, double seconds, double gain)
        {
            int n = (int)(seconds * Rate);
            var l = new Layer(n);
            int grains = (int)(seconds * 18);
            for (int g = 0; g < grains; g++)
            {
                int start = (int)(rng.NextDouble() * (n - 0.15 * Rate));
                double f = hz * Math.Pow(2, rng.Next(0, 4) * 3 / 12.0) * (1 + (rng.NextDouble() - 0.5) * 0.004);
                double fadeOut = 1 - start / (double)n;
                for (int i = 0; i < 0.15 * Rate && start + i < n; i++)
                {
                    double t = i / (double)Rate;
                    l.S[start + i] += (float)(gain * fadeOut * Math.Sin(2 * Math.PI * f * t) * Math.Exp(-30 * t) * Math.Min(1, t / 0.002));
                }
            }
            return l;
        }

        /// <summary>A slow breathing pad (for the loop): sine plus a little second harmonic, with a swell.</summary>
        public static Layer Pad(double seconds, double hz, double gain, double lfoHz, double lfoPhase = 0)
        {
            int n = (int)(seconds * Rate);
            var l = new Layer(n);
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                double swell = 0.6 + 0.4 * Math.Sin(2 * Math.PI * (lfoHz * t + lfoPhase));
                double v = Math.Sin(2 * Math.PI * hz * t) + 0.25 * Math.Sin(2 * Math.PI * hz * 2 * t);
                l.S[i] = (float)(gain * swell * v);
            }
            return l;
        }
    }

    internal static class Noise
    {
        private const int Rate = SoundSynth.Rate;

        /// <summary>Noise through a band-pass whose center glides from one pitch to another: whooshes and air.</summary>
        public static Layer Sweep(Random rng, double seconds, double fromHz, double toHz, double q, double gain)
        {
            int n = (int)(seconds * Rate);
            var l = new Layer(n);
            var svf = new Svf();
            for (int i = 0; i < n; i++)
            {
                double x = i / (double)n;
                double f = fromHz * Math.Pow(toHz / fromHz, x);
                double env = Math.Sin(Math.PI * x);
                l.S[i] = (float)(gain * env * svf.BandPass(rng.NextDouble() * 2 - 1, f, q) * 2.5);
            }
            return l;
        }

        /// <summary>Very soft filtered noise that rises and falls, like a machine breathing.</summary>
        public static Layer Breath(Random rng, double seconds, double gain)
        {
            int n = (int)(seconds * Rate);
            var l = new Layer(n);
            var svf = new Svf();
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                double swell = 0.5 + 0.5 * Math.Sin(2 * Math.PI * 0.5 * t);
                l.S[i] = (float)(gain * swell * svf.LowPass(rng.NextDouble() * 2 - 1, 700, 0.7) * 2);
            }
            return l;
        }
    }

    /// <summary>State-variable filter (low, band and high pass in one).</summary>
    internal sealed class Svf
    {
        private double _low, _band;
        private void Step(double input, double hz, double q, out double low, out double band, out double high)
        {
            double f = 2 * Math.Sin(Math.PI * Math.Min(hz, SoundSynth.Rate / 6.0) / SoundSynth.Rate);
            double damp = 1 / Math.Max(0.5, q);
            _low += f * _band;
            high = input - _low - damp * _band;
            _band += f * high;
            low = _low;
            band = _band;
        }
        public double BandPass(double x, double hz, double q) { Step(x, hz, q, out _, out var b, out _); return b; }
        public double LowPass(double x, double hz, double q) { Step(x, hz, q, out var lo, out _, out _); return lo; }
    }

    /// <summary>A small stereo room (Freeverb-style: comb filters into all-pass filters).</summary>
    internal static class Reverb
    {
        private static readonly int[] Combs = { 1116, 1188, 1277, 1356, 1422, 1491 };
        private static readonly int[] Alls = { 556, 441, 341 };
        private const int Spread = 23;

        public static void Apply(float[] l, float[] r, double wet, bool loop)
        {
            var outL = Run(l, 0, loop);
            var outR = Run(r, Spread, loop);
            float w = (float)wet, dry = (float)(1 - wet * 0.35);
            for (int i = 0; i < l.Length; i++)
            {
                // A little of each side's echo goes to the other side, so the room surrounds you.
                float a = outL[i], b = outR[i];
                l[i] = l[i] * dry + (a * 0.8f + b * 0.2f) * w;
                r[i] = r[i] * dry + (b * 0.8f + a * 0.2f) * w;
            }
        }

        private static float[] Run(float[] input, int spread, bool loop)
        {
            int n = input.Length;
            var sum = new float[n];
            // For a loop, run twice so the tail from the end carries into the start.
            int passes = loop ? 2 : 1;
            foreach (int baseLen in Combs)
            {
                int len = baseLen + spread;
                var buf = new float[len];
                int idx = 0;
                float store = 0;
                const float feedback = 0.80f, damp = 0.25f;
                for (int pass = 0; pass < passes; pass++)
                    for (int i = 0; i < n; i++)
                    {
                        float o = buf[idx];
                        store = o * (1 - damp) + store * damp;
                        buf[idx] = input[i] * 0.015f + store * feedback;
                        if (++idx >= len) idx = 0;
                        if (pass == passes - 1) sum[i] += o;
                    }
            }
            foreach (int baseLen in Alls)
            {
                int len = baseLen + spread;
                var buf = new float[len];
                int idx = 0;
                for (int i = 0; i < n; i++)
                {
                    float b = buf[idx];
                    float o = -sum[i] + b;
                    buf[idx] = sum[i] + b * 0.5f;
                    if (++idx >= len) idx = 0;
                    sum[i] = o;
                }
            }
            return sum;
        }
    }
}
