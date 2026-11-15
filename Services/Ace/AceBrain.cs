using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nimbus.Services.Ace
{
    /// <summary>
    /// Ace's brain. Uses LFM3-Omni-2B (llama.cpp) first and falls back to Qwen3.5 4B (Ollama)
    /// if Omni isn't installed, won't start, or errors before answering. Keeps the chat history.
    /// </summary>
    public sealed class AceBrain : IDisposable
    {
        public const string OmniName = "LFM3 Omni 2B";

        /// <summary>One brain for the whole app, so the model stays loaded when you switch tabs.</summary>
        public static AceBrain Shared => _shared.Value;
        private static readonly Lazy<AceBrain> _shared = new(() =>
        {
            var config = AceModelConfig.Load();
            if (!System.IO.File.Exists(AceModelConfig.ConfigPath)) config.Save();   // so Storm has a file to edit
            return new AceBrain(config);
        });

        public AceModelConfig Config { get; }
        private readonly OmniClient _omni;
        private readonly OllamaFallback _ollama;
        private readonly LlamaServerHost _host;
        private readonly List<ChatTurn> _history = new();
        private readonly SemaphoreSlim _startLock = new(1, 1);
        private bool _omniTried;
        private string? _omniProblem;
        private DateTime _lastStartTry = DateTime.MinValue;

        /// <summary>Most recent turns kept for context. 8K tokens of context is roughly 24K characters.</summary>
        private const int MaxHistoryTurns = 20;
        private const int MaxHistoryChars = 16000;

        public string ModelName { get; private set; } = OmniName;
        public string Detail { get; private set; } = "not started";
        public bool UsingFallback { get; private set; }
        public event Action? StatusChanged;

        public AceBrain(AceModelConfig config)
        {
            Config = config;
            _omni = new OmniClient(config);
            _ollama = new OllamaFallback(config);
            _host = new LlamaServerHost(config, _omni);
        }

        public string FallbackName => Config.FallbackModel switch
        {
            "qwen3.5:4b" => "Qwen3.5 4B",
            var other => other,
        };

        private string Where => Config.UseGpu ? "GPU" : "CPU+RAM";

        private void SetStatus(string model, string detail, bool fallback)
        {
            ModelName = model;
            Detail = detail;
            UsingFallback = fallback;
            StatusChanged?.Invoke();
        }

        /// <summary>Starts LFM3-Omni in the background. Safe to call often. Returns null when Omni is ready.</summary>
        public async Task<string?> WarmUpAsync(CancellationToken ct = default)
        {
            await _startLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_omniTried && _omniProblem == null && await _omni.IsReadyAsync(ct).ConfigureAwait(false)) return null;
                // Omni just failed: use the backup right away instead of waiting on another start for every question.
                if (_omniProblem != null && DateTime.UtcNow - _lastStartTry < TimeSpan.FromSeconds(60)) return _omniProblem;
                _lastStartTry = DateTime.UtcNow;

                SetStatus(OmniName, "starting on " + Where + "…", false);
                if (Config.UseGpu && Config.FreeOllamaVramOnStart && !await _omni.IsReadyAsync(ct).ConfigureAwait(false))
                    await _ollama.UnloadAllAsync(ct).ConfigureAwait(false);

                _omniProblem = await _host.EnsureRunningAsync(ct).ConfigureAwait(false);
                _omniTried = true;
                if (_omniProblem == null) SetStatus(OmniName, Where, false);
                else SetStatus(FallbackName, "backup · " + _omniProblem, true);
                return _omniProblem;
            }
            finally { _startLock.Release(); }
        }

        /// <summary>True when LFM3 Omni is already up (doesn't start it).</summary>
        public async Task<bool> IsOmniRunningAsync(CancellationToken ct = default)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(2));
                return await _omni.IsReadyAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }
            catch (Exception) when (!ct.IsCancellationRequested) { return false; }
        }

        /// <summary>Asks Ace. Streams text pieces to onText (on a background thread) and returns the full reply.</summary>
        public async Task<AceReply> AskAsync(ChatTurn question, Action<string> onText, CancellationToken ct = default)
        {
            List<ChatTurn> snapshot;
            lock (_history)
            {
                _history.Add(question);
                TrimHistory();
                snapshot = _history.ToList();
            }

            string? why = await WarmUpAsync(ct).ConfigureAwait(false);
            if (why == null)
            {
                var got = new StringBuilder();
                try
                {
                    string text = await _omni.ChatAsync(snapshot, p => { got.Append(p); onText(p); }, ct).ConfigureAwait(false);
                    return Finish(text, OmniName, false);
                }
                catch (OperationCanceledException) { Finish(got.ToString(), OmniName, false); throw; }
                catch (Exception ex) when (got.Length > 0)
                {
                    return Finish(got + " … (cut off: " + ex.Message + ")", OmniName, false);
                }
                catch (Exception ex)
                {
                    why = ex.Message;
                    _omniProblem = why;   // the backup answers for a minute, then Omni gets checked again
                    _lastStartTry = DateTime.UtcNow;
                }
            }

            SetStatus(FallbackName, "backup · " + why, true);
            try
            {
                string text = await _ollama.ChatAsync(snapshot, onText, ct).ConfigureAwait(false);
                return Finish(text, FallbackName, true);
            }
            catch (OperationCanceledException) { lock (_history) _history.Remove(question); throw; }
            catch (Exception ex)
            {
                lock (_history) _history.Remove(question);
                throw new InvalidOperationException(
                    $"{OmniName} couldn't answer ({why}) and neither could {FallbackName} ({ex.Message}).", ex);
            }
        }

        private AceReply Finish(string raw, string engine, bool fallback)
        {
            string text = OmniClient.Clean(raw).Trim();
            lock (_history)
            {
                // The question stays in history without its picture or clip, so they aren't sent again every turn.
                for (int i = 0; i < _history.Count; i++)
                    if (_history[i].HasMedia) _history[i] = _history[i].WithoutMedia();
                if (text.Length > 0) _history.Add(ChatTurn.Assistant(text));
            }
            return new AceReply(text, engine, fallback);
        }

        /// <summary>
        /// A one-off request that doesn't touch the chat history (Ace V4 uses it to reword answers and to turn
        /// recorded speech into text). Uses Omni, or the backup when Omni can't answer. Audio needs Omni.
        /// </summary>
        public async Task<string> CompleteAsync(IReadOnlyList<ChatTurn> turns, string? system, Action<string>? onText, CancellationToken ct = default)
        {
            Action<string> sink = onText ?? (_ => { });
            bool hasAudio = turns.Any(t => t.Audio != null);
            string? why = await WarmUpAsync(ct).ConfigureAwait(false);
            if (why == null)
            {
                try { return OmniClient.Clean(await _omni.ChatAsync(turns, sink, ct, system).ConfigureAwait(false)).Trim(); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    why = ex.Message;
                    _omniProblem = why;
                    _lastStartTry = DateTime.UtcNow;
                }
            }
            if (hasAudio) throw new InvalidOperationException("Listening needs " + OmniName + " (" + why + ").");
            return OmniClient.Clean(await _ollama.ChatAsync(turns, sink, ct, system).ConfigureAwait(false)).Trim();
        }

        /// <summary>Adds a spoken exchange to the text history, so typed follow-ups know what was said.</summary>
        public void AddVoiceExchange(string youSaid, string aceSaid)
        {
            lock (_history)
            {
                _history.Add(ChatTurn.User(string.IsNullOrWhiteSpace(youSaid) ? "[said something out loud]" : youSaid));
                if (!string.IsNullOrWhiteSpace(aceSaid)) _history.Add(ChatTurn.Assistant(OmniClient.Clean(aceSaid).Trim()));
                TrimHistory();
            }
        }

        public void NewChat() { lock (_history) _history.Clear(); }

        /// <summary>Call only while holding the history lock.</summary>
        private void TrimHistory()
        {
            while (_history.Count > MaxHistoryTurns) _history.RemoveAt(0);
            while (_history.Count > 1 && _history.Sum(t => t.Text.Length) > MaxHistoryChars) _history.RemoveAt(0);
        }

        /// <summary>The GPU / CPU+RAM switch. Restarts Omni with the new setting if Nimbus is running it.</summary>
        public async Task<string?> SetUseGpuAsync(bool useGpu, CancellationToken ct = default)
        {
            if (Config.UseGpu == useGpu) return null;
            Config.UseGpu = useGpu;
            Config.Save();
            if (!_omniTried) return null;   // not started yet; the next start uses the new setting

            await _startLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                SetStatus(OmniName, "moving to " + Where + "…", false);
                if (useGpu && Config.FreeOllamaVramOnStart) await _ollama.UnloadAllAsync(ct).ConfigureAwait(false);
                _omniProblem = await _host.RestartAsync(ct).ConfigureAwait(false);
                _lastStartTry = DateTime.UtcNow;
                if (_omniProblem == null) SetStatus(OmniName, Where, false);
                else SetStatus(FallbackName, "backup · " + _omniProblem, true);
                return _omniProblem;
            }
            finally { _startLock.Release(); }
        }

        /// <summary>Opens a live voice chat. Needs Omni running (voice has no backup).</summary>
        public async Task<AceVoice> StartVoiceAsync(CancellationToken ct = default)
        {
            string? why = await WarmUpAsync(ct).ConfigureAwait(false);
            if (why != null) throw new InvalidOperationException("Voice needs " + OmniName + " (" + why + ").");
            return await AceVoice.ConnectAsync(Config, ct).ConfigureAwait(false);
        }

        public void Dispose()
        {
            _host.Dispose();   // stops llama.cpp if Nimbus started it
            _omni.Dispose();
            _ollama.Dispose();
        }

        /// <summary>Call from App.OnExit. Does nothing if Ace was never opened.</summary>
        public static void ShutdownShared()
        {
            if (_shared.IsValueCreated) _shared.Value.Dispose();
        }
    }
}
