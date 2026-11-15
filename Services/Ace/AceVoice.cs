using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nimbus.Services.Ace
{
    /// <summary>
    /// Push-to-talk for Ace: tap the mic, talk, tap again, and Ace answers out loud with the words on screen.
    /// Talking while Ace is still speaking cuts Ace off, like a real conversation.
    /// </summary>
    public sealed class AceVoice : IAsyncDisposable
    {
        private readonly OmniVoiceSession _session;
        private readonly VoiceAudio _audio = new();

        public event Action<string>? AceText;          // words of Ace's spoken answer, as they arrive
        public event Action<string>? HeardYou;         // what Ace heard, if the server reports it
        public event Action? AnswerDone;
        public event Action<string>? Problem;
        public event Action? Disconnected;

        public bool IsListening => _audio.IsListening;
        public bool IsOpen => _session.IsOpen;

        private AceVoice(AceModelConfig config)
        {
            _session = new OmniVoiceSession(config);
            // While Storm is talking, anything still arriving belongs to the answer that was cut off, so drop it.
            _session.TextDelta += t => { if (!_audio.IsListening) AceText?.Invoke(t); };
            _session.AudioDelta += pcm => { if (!_audio.IsListening) _audio.Play(pcm); };
            _session.UserTranscript += t => HeardYou?.Invoke(t);
            _session.ResponseDone += () => { if (!_audio.IsListening) AnswerDone?.Invoke(); };
            _session.Error += e => Problem?.Invoke(e);
            _session.Closed += () => Disconnected?.Invoke();
            _audio.MicData += (buffer, count) => _session.AppendAudio(buffer, count);
        }

        /// <summary>Connects to the already-running llama.cpp server. Call AceBrain.WarmUpAsync first.</summary>
        public static async Task<AceVoice> ConnectAsync(AceModelConfig config, CancellationToken ct = default)
        {
            var voice = new AceVoice(config);
            try
            {
                await voice._session.ConnectAsync(ct).ConfigureAwait(false);
                voice._audio.OpenSpeaker(voice._session.OutputRate);
                return voice;
            }
            catch
            {
                await voice.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>Starts listening. If Ace is mid-answer, it stops talking first.</summary>
        public void StartListening()
        {
            if (_session.ResponseActive || _audio.IsSpeaking)
            {
                _session.CancelResponse();
                _audio.Hush();
            }
            _audio.StartMic(_session.InputRate);
        }

        /// <summary>Stops listening and asks Ace to answer what it heard.</summary>
        public async Task StopListeningAndAnswerAsync()
        {
            await _audio.StopMicAsync().ConfigureAwait(false);
            _session.CreateResponse();
        }

        /// <summary>Ask something by typing, and hear the answer.</summary>
        public void Say(string text) => _session.SendText(text);

        public void Hush()
        {
            _session.CancelResponse();
            _audio.Hush();
        }

        public async ValueTask DisposeAsync()
        {
            _audio.Dispose();
            await _session.DisposeAsync().ConfigureAwait(false);
        }
    }
}
