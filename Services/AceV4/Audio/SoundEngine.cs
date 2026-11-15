using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Nimbus.AceV4.Audio
{
    /// <summary>
    /// Plays Ace's sounds through one always-open output, mixed together, so many sounds can overlap smoothly.
    /// Each play picks one of a few slightly different versions (pitch and details), and can be panned left/right
    /// to follow where Ace's eyes are looking.
    /// </summary>
    public sealed class SoundEngine : IDisposable
    {
        public static SoundEngine Shared { get; } = new();

        private const int Variants = 4;
        private readonly ConcurrentDictionary<Sfx, StereoBuffer[]> _bank = new();
        private readonly WaveFormat _format = WaveFormat.CreateIeeeFloatWaveFormat(SoundSynth.Rate, 2);
        private readonly object _gate = new();
        private readonly Random _rng = new();
        private MixingSampleProvider? _mixer;
        private WaveOutEvent? _out;
        private volatile float _volume = 0.7f;
        private volatile bool _muted;
        private readonly Dictionary<Sfx, DateTime> _lastPlayed = new();

        public string Problem { get; private set; } = "";
        public bool Enabled { get; set; } = true;

        /// <summary>0 to 1.</summary>
        public double Volume { get => _volume; set => _volume = (float)Math.Clamp(value, 0, 1); }
        public bool Muted { get => _muted; set => _muted = value; }

        /// <summary>Makes every sound in the background (well under a second) and opens the speakers.</summary>
        public void Start()
        {
            Task.Run(() =>
            {
                foreach (Sfx s in Enum.GetValues(typeof(Sfx))) Get(s);
            });
            EnsureOutput();
        }

        private StereoBuffer[] Get(Sfx s) => _bank.GetOrAdd(s, key =>
        {
            var list = new StereoBuffer[SoundSynth.Loops(key) ? 1 : Variants];
            for (int i = 0; i < list.Length; i++)
            {
                double pitch = i == 0 ? 1 : 1 + ((i % 2 == 0 ? 1 : -1) * 0.012 * ((i + 1) / 2));   // 1, 0.988, 1.012, 0.976…
                list[i] = SoundSynth.Make(key, pitch, 11 + i * 17);
            }
            return list;
        });

        private bool EnsureOutput()
        {
            lock (_gate)
            {
                if (_out != null) return true;
                try
                {
                    _mixer = new MixingSampleProvider(_format) { ReadFully = true };
                    _out = new WaveOutEvent { DesiredLatency = 90, NumberOfBuffers = 3 };
                    _out.Init(new MasterVolume(_mixer, this));
                    _out.PlaybackStopped += (_, e) =>
                    {
                        // The device went away (headphones unplugged): reopen on the next sound.
                        lock (_gate) { try { _out?.Dispose(); } catch { } _out = null; _mixer = null; }
                        if (e.Exception != null) Problem = e.Exception.Message;
                    };
                    _out.Play();
                    Problem = "";
                    return true;
                }
                catch (Exception ex)
                {
                    Problem = "No sound device: " + ex.Message;
                    try { _out?.Dispose(); } catch { }
                    _out = null;
                    _mixer = null;
                    return false;
                }
            }
        }

        /// <summary>Plays a sound. pan -1 (left) … 1 (right). Repeats of the same sound within 40 ms are skipped.</summary>
        public void Play(Sfx s, double pan = 0, double gain = 1)
        {
            if (!Enabled || _muted || _volume <= 0) return;
            lock (_lastPlayed)
            {
                if (_lastPlayed.TryGetValue(s, out var last) && DateTime.UtcNow - last < TimeSpan.FromMilliseconds(40)) return;
                _lastPlayed[s] = DateTime.UtcNow;
            }
            Task.Run(() =>
            {
                try
                {
                    var versions = Get(s);
                    StereoBuffer b;
                    lock (_rng) b = versions[_rng.Next(versions.Length)];
                    if (!EnsureOutput()) return;
                    lock (_gate) _mixer?.AddMixerInput(new Voice(b, pan, gain, loop: false));
                }
                catch (Exception ex) { Problem = ex.Message; }
            });
        }

        /// <summary>Starts a looping sound (the thinking hum). Dispose or Stop the handle to fade it out.</summary>
        public LoopHandle Loop(Sfx s, double gain = 1, double fadeInMs = 400)
        {
            var handle = new LoopHandle();
            if (!Enabled || _muted || _volume <= 0) return handle;
            try
            {
                var b = Get(s)[0];
                if (!EnsureOutput()) return handle;
                var v = new Voice(b, 0, gain, loop: true, fadeInMs: fadeInMs);
                handle.Voice = v;
                lock (_gate) _mixer?.AddMixerInput(v);
            }
            catch (Exception ex) { Problem = ex.Message; }
            return handle;
        }

        /// <summary>Saves every sound as a WAV file (the "Export sounds" button).</summary>
        public static int ExportAll(string folder)
        {
            Directory.CreateDirectory(folder);
            int n = 0;
            foreach (Sfx s in Enum.GetValues(typeof(Sfx)))
            {
                SoundSynth.WriteWav(SoundSynth.Make(s, 1, 11), Path.Combine(folder, "ace-" + s.ToString().ToLowerInvariant() + ".wav"));
                n++;
            }
            return n;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                try { _out?.Stop(); } catch { }
                _out?.Dispose();
                _out = null;
            }
        }

        // ================================================================ Mixer pieces

        /// <summary>Applies the master volume and mute to everything.</summary>
        private sealed class MasterVolume : ISampleProvider
        {
            private readonly ISampleProvider _src;
            private readonly SoundEngine _engine;
            private float _current;
            public MasterVolume(ISampleProvider src, SoundEngine engine) { _src = src; _engine = engine; _current = engine._volume; }
            public WaveFormat WaveFormat => _src.WaveFormat;
            public int Read(float[] buffer, int offset, int count)
            {
                int n = _src.Read(buffer, offset, count);
                float target = _engine._muted ? 0 : _engine._volume * _engine._volume;   // squared feels more even on a slider
                for (int i = 0; i < n; i++)
                {
                    _current += (target - _current) * 0.002f;   // glide, so moving the slider never clicks
                    buffer[offset + i] *= _current;
                }
                return n;
            }
        }

        /// <summary>One sound playing: reads from a buffer with pan, gain and fades. Returns less than asked when done, which removes it from the mixer.</summary>
        internal sealed class Voice : ISampleProvider
        {
            private readonly StereoBuffer _b;
            private readonly float _gl, _gr;
            private readonly bool _loop;
            private int _pos;
            private float _fade = 1, _fadeStep;
            private volatile bool _stopping;
            private int _fadeOutLeft = -1, _fadeOutTotal;

            public Voice(StereoBuffer b, double pan, double gain, bool loop, double fadeInMs = 0)
            {
                _b = b;
                _loop = loop;
                double p = Math.Clamp(pan, -1, 1);
                // Balance: the far side gets quieter, the near side stays full.
                _gl = (float)(gain * (p > 0 ? 1 - p * 0.75 : 1));
                _gr = (float)(gain * (p < 0 ? 1 + p * 0.75 : 1));
                if (fadeInMs > 0) { _fade = 0; _fadeStep = 1f / (float)(fadeInMs / 1000 * SoundSynth.Rate); }
            }

            public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SoundSynth.Rate, 2);

            public void FadeOut(double ms)
            {
                _fadeOutTotal = Math.Max(1, (int)(ms / 1000 * SoundSynth.Rate));
                _fadeOutLeft = _fadeOutTotal;
                _stopping = true;
            }

            public int Read(float[] buffer, int offset, int count)
            {
                int frames = count / 2, written = 0;
                for (int f = 0; f < frames; f++)
                {
                    if (_pos >= _b.Length)
                    {
                        if (!_loop) break;
                        _pos = 0;
                    }
                    if (_stopping)
                    {
                        if (_fadeOutLeft <= 0) break;
                        _fadeOutLeft--;
                    }
                    if (_fade < 1) _fade = Math.Min(1, _fade + _fadeStep);
                    float g = _fade * (_stopping ? _fadeOutLeft / (float)_fadeOutTotal : 1);
                    buffer[offset + written++] = _b.L[_pos] * _gl * g;
                    buffer[offset + written++] = _b.R[_pos] * _gr * g;
                    _pos++;
                }
                return written;
            }
        }
    }

    /// <summary>A running loop. Stop fades it out.</summary>
    public sealed class LoopHandle : IDisposable
    {
        internal SoundEngine.Voice? Voice;
        public void Stop(double fadeMs = 500) { Voice?.FadeOut(fadeMs); Voice = null; }
        public void Dispose() => Stop();
    }
}
