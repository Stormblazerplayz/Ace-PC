using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Nimbus.Services.Ace
{
    /// <summary>
    /// Live voice chat with LFM3-Omni over llama.cpp's /v1/realtime WebSocket.
    /// Send mic audio (16-bit mono PCM), ask for a response, get spoken audio and its text back.
    /// No sound devices here: VoiceAudio does the mic and speakers, AceVoice glues the two.
    /// </summary>
    public sealed class OmniVoiceSession : IAsyncDisposable
    {
        private readonly AceModelConfig _config;
        private readonly ClientWebSocket _ws = new();
        private readonly Channel<string> _outbox = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        private readonly CancellationTokenSource _life = new();
        private readonly TaskCompletionSource<bool> _sessionInfo = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<string?>? _updateAck;
        private Task? _sendLoop, _receiveLoop;
        private bool _textThisResponse;

        /// <summary>Mic sample rate the server wants (read from the session; 16 kHz for LFM3).</summary>
        public int InputRate { get; private set; } = 16000;
        /// <summary>Speech sample rate the server sends (24 kHz for LFM3).</summary>
        public int OutputRate { get; private set; } = 24000;
        public bool ResponseActive { get; private set; }
        public bool IsOpen => _ws.State == WebSocketState.Open;

        public event Action<string>? TextDelta;        // Ace's words, as they come
        public event Action<byte[]>? AudioDelta;       // Ace's voice, PCM16 mono at OutputRate
        public event Action<string>? UserTranscript;   // what the server heard you say, if it reports it
        public event Action? ResponseDone;
        public event Action<string>? Error;
        public event Action? Closed;

        public OmniVoiceSession(AceModelConfig config) => _config = config;

        public async Task ConnectAsync(CancellationToken ct = default)
        {
            var uri = new Uri($"ws://{_config.Host}:{_config.Port}/v1/realtime");
            await _ws.ConnectAsync(uri, ct).ConfigureAwait(false);
            _receiveLoop = Task.Run(ReceiveLoopAsync);
            _sendLoop = Task.Run(SendLoopAsync);

            // The server opens with the session info (audio formats). Don't hang forever if it doesn't.
            await Task.WhenAny(_sessionInfo.Task, Task.Delay(5000, ct)).ConfigureAwait(false);

            _updateAck = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Enqueue(new JsonObject
            {
                ["type"] = "session.update",
                ["session"] = new JsonObject
                {
                    ["type"] = "realtime",
                    ["instructions"] = $"{_config.SystemPrompt} Speak with a {_config.Voice} voice. Respond with interleaved text and audio.",
                    ["output_modalities"] = new JsonArray("audio", "text"),   // spoken answer plus its words
                },
            });
            var done = await Task.WhenAny(_updateAck.Task, Task.Delay(5000, ct)).ConfigureAwait(false);
            if (done == _updateAck.Task && _updateAck.Task.Result is string problem)
                throw new InvalidOperationException("Voice setup failed: " + problem);
        }

        /// <summary>Queue a chunk of mic audio (PCM16 mono at InputRate).</summary>
        public void AppendAudio(byte[] buffer, int count)
        {
            if (count <= 0) return;
            Enqueue(new JsonObject { ["type"] = "input_audio_buffer.append", ["audio"] = Convert.ToBase64String(buffer, 0, count) });
        }

        /// <summary>"I'm done talking, answer me."</summary>
        public void CreateResponse()
        {
            ResponseActive = true;
            _textThisResponse = false;
            Enqueue(new JsonObject { ["type"] = "response.create" });
        }

        /// <summary>Stop Ace mid-sentence (when Storm starts talking over it).</summary>
        public void CancelResponse()
        {
            if (!ResponseActive) return;
            Enqueue(new JsonObject { ["type"] = "response.cancel" });
        }

        /// <summary>Type a message inside the voice chat and hear the answer.</summary>
        public void SendText(string text)
        {
            Enqueue(new JsonObject
            {
                ["type"] = "conversation.item.create",
                ["item"] = new JsonObject
                {
                    ["type"] = "message",
                    ["role"] = "user",
                    ["content"] = new JsonArray(new JsonObject { ["type"] = "input_text", ["text"] = text }),
                },
            });
            CreateResponse();
        }

        private void Enqueue(JsonObject evt) => _outbox.Writer.TryWrite(evt.ToJsonString());

        // ClientWebSocket allows one send at a time, so every send goes through this one loop.
        private async Task SendLoopAsync()
        {
            try
            {
                await foreach (string json in _outbox.Reader.ReadAllAsync(_life.Token).ConfigureAwait(false))
                {
                    if (_ws.State != WebSocketState.Open) break;
                    await _ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, _life.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Error?.Invoke("Voice send failed: " + ex.Message); }
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[64 * 1024];
            using var message = new MemoryStream();
            try
            {
                while (_ws.State == WebSocketState.Open && !_life.IsCancellationRequested)
                {
                    message.SetLength(0);
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _ws.ReceiveAsync(buffer, _life.Token).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        message.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType != WebSocketMessageType.Text) continue;
                    Handle(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException ex) { if (!_life.IsCancellationRequested) Error?.Invoke("Voice connection dropped: " + ex.Message); }
            finally
            {
                ResponseActive = false;
                _sessionInfo.TrySetResult(false);
                _updateAck?.TrySetResult("the connection closed");
                Closed?.Invoke();
            }
        }

        /// <summary>Reacts to one server event. Public so it can be tested without a server.</summary>
        public void Handle(string json)
        {
            JsonNode? evt;
            try { evt = JsonNode.Parse(json); }
            catch (JsonException) { return; }
            string type = evt?["type"]?.GetValue<string>() ?? "";

            if (!_sessionInfo.Task.IsCompleted && evt?["session"] is JsonNode session)
            {
                InputRate = ReadRate(session["audio"]?["input"]?["format"]?["rate"], InputRate);
                OutputRate = ReadRate(session["audio"]?["output"]?["format"]?["rate"], OutputRate);
                _sessionInfo.TrySetResult(true);
                if (type != "session.updated") return;
            }

            switch (type)
            {
                case "session.updated":
                    _updateAck?.TrySetResult(null);
                    break;
                case "response.created":
                    ResponseActive = true;
                    _textThisResponse = false;
                    break;
                case "response.output_text.delta":
                    _textThisResponse = true;
                    TextDelta?.Invoke(evt?["delta"]?.GetValue<string>() ?? "");
                    break;
                case "response.output_audio_transcript.delta":
                    if (!_textThisResponse) TextDelta?.Invoke(evt?["delta"]?.GetValue<string>() ?? "");   // only if no plain text came
                    break;
                case "response.output_audio.delta":
                    string? b64 = evt?["delta"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(b64))
                    {
                        try { AudioDelta?.Invoke(Convert.FromBase64String(b64)); } catch (FormatException) { }
                    }
                    break;
                case "conversation.item.input_audio_transcription.completed":
                    UserTranscript?.Invoke(evt?["transcript"]?.GetValue<string>() ?? "");
                    break;
                case "response.done":
                    ResponseActive = false;
                    ResponseDone?.Invoke();
                    break;
                case "error":
                    string msg = evt?["error"]?["message"]?.GetValue<string>() ?? "unknown error";
                    if (_updateAck is { Task.IsCompleted: false }) _updateAck.TrySetResult(msg);
                    else Error?.Invoke(msg);
                    break;
            }
        }

        private static int ReadRate(JsonNode? node, int fallback)
        {
            try { return node != null && node.GetValue<int>() is > 0 and var r ? r : fallback; }
            catch { return fallback; }
        }

        public async ValueTask DisposeAsync()
        {
            _outbox.Writer.TryComplete();   // the send loop finishes what's queued, then ends
            if (_sendLoop != null) await Task.WhenAny(_sendLoop, Task.Delay(1500)).ConfigureAwait(false);
            try
            {
                if (_ws.State == WebSocketState.Open)
                {
                    // CloseOutputAsync only sends; the receive loop sees the server's reply and exits.
                    using var cts = new CancellationTokenSource(1500);
                    await _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", cts.Token).ConfigureAwait(false);
                }
            }
            catch { }
            if (_receiveLoop != null) await Task.WhenAny(_receiveLoop, Task.Delay(1500)).ConfigureAwait(false);
            _life.Cancel();
            try { if (_receiveLoop != null) await _receiveLoop.ConfigureAwait(false); } catch { }
            try { if (_sendLoop != null) await _sendLoop.ConfigureAwait(false); } catch { }
            _ws.Dispose();
            _life.Dispose();
        }
    }
}
