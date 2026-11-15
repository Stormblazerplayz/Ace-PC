using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Nimbus.Services.Ace;

namespace Nimbus.AceV4.Audio
{
    /// <summary>Ace's speaking voice (Windows' built-in voices, works offline).</summary>
    public sealed class AceSpeech : IDisposable
    {
        private SpeechSynthesizer? _synth;
        private readonly object _gate = new();
        public event Action<bool>? Speaking;      // true when talking starts, false when it stops
        public event Action<string>? Word;        // the word being said, for the eyes to "talk"
        public string Problem { get; private set; } = "";

        public static List<string> Voices()
        {
            try
            {
                using var s = new SpeechSynthesizer();
                return s.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo.Name).ToList();
            }
            catch { return new List<string>(); }
        }

        private SpeechSynthesizer? Synth(AceV4Config config)
        {
            if (_synth != null) return _synth;
            try
            {
                var s = new SpeechSynthesizer();
                s.SetOutputToDefaultAudioDevice();
                if (config.TtsVoice.Length > 0)
                {
                    try { s.SelectVoice(config.TtsVoice); } catch { }
                }
                else
                {
                    // Prefer a friendly-sounding voice when one is installed.
                    var names = s.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo.Name).ToList();
                    var pick = names.FirstOrDefault(n => n.Contains("Aria")) ?? names.FirstOrDefault(n => n.Contains("Zira")) ?? names.FirstOrDefault(n => n.Contains("Jenny"));
                    if (pick != null) try { s.SelectVoice(pick); } catch { }
                }
                s.SpeakStarted += (_, _) => Speaking?.Invoke(true);
                s.SpeakCompleted += (_, _) => Speaking?.Invoke(false);
                s.SpeakProgress += (_, e) => Word?.Invoke(e.Text);
                _synth = s;
            }
            catch (Exception ex) { Problem = "Windows speech isn't available: " + ex.Message; }
            return _synth;
        }

        /// <summary>Says something, cutting off anything Ace was still saying.</summary>
        public void Say(string text, AceV4Config config)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            lock (_gate)
            {
                var s = Synth(config);
                if (s == null) return;
                try
                {
                    s.SpeakAsyncCancelAll();
                    s.Rate = Math.Clamp(config.TtsRate, -10, 10);
                    s.Volume = 100;
                    s.SpeakAsync(text);
                }
                catch (Exception ex) { Problem = ex.Message; }
            }
        }

        public void Hush()
        {
            lock (_gate) { try { _synth?.SpeakAsyncCancelAll(); } catch { } }
        }

        public void Dispose()
        {
            lock (_gate) { try { _synth?.SpeakAsyncCancelAll(); _synth?.Dispose(); } catch { } _synth = null; }
        }
    }

    /// <summary>
    /// Voice dictation for Ace V4: records until you stop talking, then turns it into text.
    /// Uses LFM3 Omni when it's already running (best accuracy), otherwise Windows speech recognition (offline).
    /// </summary>
    public sealed class Dictation : IDisposable
    {
        private const int Rate = 16000;
        private WaveInEvent? _mic;
        private TaskCompletionSource<bool>? _stop;
        public event Action<double>? Level;               // 0..1, for the mic meter
        public event Action<string>? Stage;               // "Listening…", "Got it, working it out…"
        public bool IsListening => _mic != null;

        /// <summary>Stops recording now (the mic button was pressed again) and keeps what was said.</summary>
        public void StopNow() => _stop?.TrySetResult(true);

        /// <summary>Records one request and returns the words (or "" if nothing was said).</summary>
        public async Task<string> ListenAsync(AceV4Config config, CancellationToken ct)
        {
            if (_mic != null) return "";
            var audio = new MemoryStream();
            var stop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _stop = stop;
            bool heard = false;
            double noise = 0.006, lastLoud = 0, elapsed = 0;
            int frames = 0;
            double silence = Math.Clamp(config.DictationSilenceSeconds, 0.6, 5);
            double max = Math.Clamp(config.DictationMaxSeconds, 3, 60);

            var mic = new WaveInEvent { DeviceNumber = -1, WaveFormat = new WaveFormat(Rate, 16, 1), BufferMilliseconds = 50 };
            mic.DataAvailable += (_, e) =>
            {
                if (e.BytesRecorded <= 0) return;
                audio.Write(e.Buffer, 0, e.BytesRecorded);
                double sum = 0;
                int n = e.BytesRecorded / 2;
                for (int i = 0; i < n; i++)
                {
                    double v = BitConverter.ToInt16(e.Buffer, i * 2) / 32768.0;
                    sum += v * v;
                }
                double rms = Math.Sqrt(sum / Math.Max(1, n));
                elapsed += n / (double)Rate;
                frames++;
                // The first quarter second tells us how loud the room is.
                if (frames <= 5) noise = Math.Max(noise, rms);
                double threshold = Math.Max(0.012, noise * 2.6);
                if (rms > threshold) { heard = true; lastLoud = elapsed; }
                Level?.Invoke(Math.Clamp(rms * 9, 0, 1));
                if (heard && elapsed - lastLoud > silence) stop.TrySetResult(true);
                else if (!heard && elapsed > 7) stop.TrySetResult(false);   // nobody spoke
                else if (elapsed > max) stop.TrySetResult(true);
            };
            try
            {
                _mic = mic;
                mic.StartRecording();
            }
            catch (Exception ex)
            {
                _mic = null;
                mic.Dispose();
                throw new InvalidOperationException("I couldn't open the microphone: " + ex.Message);
            }
            Stage?.Invoke("Listening…");
            using (ct.Register(() => stop.TrySetCanceled()))
            {
                try { await stop.Task.ConfigureAwait(false); }
                finally
                {
                    var stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    mic.RecordingStopped += (_, _) => stopped.TrySetResult(true);
                    try { mic.StopRecording(); } catch { }
                    await Task.WhenAny(stopped.Task, Task.Delay(800)).ConfigureAwait(false);
                    mic.Dispose();
                    _mic = null;
                    Level?.Invoke(0);
                }
            }
            if (!heard) return "";
            Stage?.Invoke("Turning that into words…");
            byte[] wav = Wav(audio.ToArray());
            return await TranscribeAsync(wav, config, ct).ConfigureAwait(false);
        }

        private static byte[] Wav(byte[] pcm)
        {
            using var ms = new MemoryStream();
            using (var w = new WaveFileWriter(ms, new WaveFormat(Rate, 16, 1))) w.Write(pcm, 0, pcm.Length);
            return ms.ToArray();
        }

        /// <summary>Omni first if it's already running; Windows dictation otherwise (or if Omni fails).</summary>
        public static async Task<string> TranscribeAsync(byte[] wav, AceV4Config config, CancellationToken ct)
        {
            bool tryOmni = config.DictationEngine switch
            {
                "omni" => true,
                "windows" => false,
                _ => await AceBrain.Shared.IsOmniRunningAsync(ct).ConfigureAwait(false),
            };
            if (tryOmni)
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(TimeSpan.FromSeconds(25));
                    var turn = new ChatTurn { Role = "user", Text = "Write down exactly what is said in this recording.", Audio = wav, AudioFormat = "wav" };
                    string text = await AceBrain.Shared.CompleteAsync(new[] { turn },
                        "You are a speech-to-text tool. Reply with only the exact words spoken, with normal punctuation. No quotes, no notes. If nothing is said, reply with nothing.",
                        null, cts.Token).ConfigureAwait(false);
                    text = text.Trim().Trim('"');
                    if (text.Length > 0) return text;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch { }
            }
            return await Task.Run(() => WindowsDictation(wav), ct).ConfigureAwait(false);
        }

        private static string WindowsDictation(byte[] wav)
        {
            try
            {
                using var engine = new SpeechRecognitionEngine(new CultureInfo("en-US"));
                engine.LoadGrammar(new DictationGrammar());
                using var stream = new MemoryStream(wav);
                engine.SetInputToWaveStream(stream);
                var words = new List<string>();
                while (true)
                {
                    var result = engine.Recognize();
                    if (result == null) break;
                    if (!string.IsNullOrWhiteSpace(result.Text)) words.Add(result.Text);
                }
                return string.Join(" ", words).Trim();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Speech recognition isn't working (" + ex.Message + "). In Windows Settings, add the English speech pack under Time & language > Speech.");
            }
        }

        public void Dispose()
        {
            try { _mic?.StopRecording(); } catch { }
            _mic?.Dispose();
            _mic = null;
        }
    }

    /// <summary>Listens in the background for "Hey Ace" using Windows' offline recognizer. Only a tiny grammar, so it's cheap.</summary>
    public sealed class WakeWord : IDisposable
    {
        private SpeechRecognitionEngine? _engine;
        private DateTime _lastHeard = DateTime.MinValue;
        public event Action? Heard;
        public string Problem { get; private set; } = "";
        public bool Running => _engine != null;

        public bool Start()
        {
            if (_engine != null) return true;
            try
            {
                var engine = new SpeechRecognitionEngine(new CultureInfo("en-US"));
                var phrases = new Choices("hey ace", "hi ace", "okay ace", "ok ace", "yo ace", "hey ace are you there");
                engine.LoadGrammar(new Grammar(new GrammarBuilder(phrases)) { Name = "wake" });
                engine.SetInputToDefaultAudioDevice();
                engine.SpeechRecognized += (_, e) =>
                {
                    if (_paused || e.Result.Confidence < 0.82) return;
                    if (DateTime.UtcNow - _lastHeard < TimeSpan.FromSeconds(3)) return;
                    _lastHeard = DateTime.UtcNow;
                    Heard?.Invoke();
                };
                engine.RecognizeAsync(RecognizeMode.Multiple);
                _engine = engine;
                Problem = "";
                return true;
            }
            catch (Exception ex)
            {
                Problem = "\"Hey Ace\" needs Windows speech recognition: " + ex.Message;
                return false;
            }
        }

        /// <summary>Pauses while Ace is listening to you or talking, so it doesn't wake itself.</summary>
        public void Pause() => _paused = true;
        public void Resume() => _paused = false;
        private volatile bool _paused;

        public void Stop()
        {
            try { _engine?.RecognizeAsyncCancel(); _engine?.Dispose(); } catch { }
            _engine = null;
        }

        public void Dispose() => Stop();
    }
}
