using System;
using System.Threading.Tasks;
using NAudio.Wave;

namespace Nimbus.Services.Ace
{
    /// <summary>Mic in and speakers out for Ace's voice, using the Windows default devices.</summary>
    public sealed class VoiceAudio : IDisposable
    {
        private WaveInEvent? _mic;
        private WaveOutEvent? _speaker;
        private BufferedWaveProvider? _speech;

        /// <summary>A chunk of mic audio (PCM16 mono). Raised on an audio thread.</summary>
        public event Action<byte[], int>? MicData;

        public bool IsListening => _mic != null;

        /// <summary>True while Ace still has words queued to say.</summary>
        public bool IsSpeaking => _speech != null && _speech.BufferedDuration > TimeSpan.FromMilliseconds(60);

        /// <summary>Opens the speakers at the rate the model talks in (24 kHz for LFM3-Omni).</summary>
        public void OpenSpeaker(int sampleRate)
        {
            if (_speech != null && _speech.WaveFormat.SampleRate == sampleRate) return;
            CloseSpeaker();
            _speech = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, 1))
            {
                BufferDuration = TimeSpan.FromMinutes(3),   // long answers arrive faster than they're spoken
                DiscardOnBufferOverflow = true,
                ReadFully = true,                           // play silence while waiting, instead of stopping
            };
            _speaker = new WaveOutEvent { DeviceNumber = -1, DesiredLatency = 150 };   // -1 = Windows default output
            _speaker.Init(_speech);
            _speaker.Play();
        }

        public void Play(byte[] pcm16)
        {
            if (pcm16.Length > 0) _speech?.AddSamples(pcm16, 0, pcm16.Length);
        }

        /// <summary>Drops whatever Ace hasn't said yet.</summary>
        public void Hush() => _speech?.ClearBuffer();

        public void StartMic(int sampleRate)
        {
            if (_mic != null) return;
            var mic = new WaveInEvent
            {
                DeviceNumber = -1,                                   // -1 = Windows default mic
                WaveFormat = new WaveFormat(sampleRate, 16, 1),      // Windows converts from the mic's own rate
                BufferMilliseconds = 100,
            };
            mic.DataAvailable += (_, e) =>
            {
                if (e.BytesRecorded <= 0) return;
                var copy = new byte[e.BytesRecorded];               // NAudio reuses its buffer
                Buffer.BlockCopy(e.Buffer, 0, copy, 0, e.BytesRecorded);
                MicData?.Invoke(copy, copy.Length);
            };
            _mic = mic;
            mic.StartRecording();
        }

        /// <summary>Stops the mic and waits for the last chunk to come through.</summary>
        public async Task StopMicAsync()
        {
            var mic = _mic;
            if (mic == null) return;
            _mic = null;
            var stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            mic.RecordingStopped += (_, _) => stopped.TrySetResult(true);
            mic.StopRecording();
            await Task.WhenAny(stopped.Task, Task.Delay(1500)).ConfigureAwait(false);
            mic.Dispose();
        }

        private void CloseSpeaker()
        {
            _speaker?.Stop();
            _speaker?.Dispose();
            _speaker = null;
            _speech = null;
        }

        public void Dispose()
        {
            if (_mic != null) { try { _mic.StopRecording(); } catch { } _mic.Dispose(); _mic = null; }
            CloseSpeaker();
        }
    }
}
