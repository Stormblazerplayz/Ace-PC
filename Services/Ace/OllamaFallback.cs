using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Nimbus.Services.Ace
{
    /// <summary>
    /// Ace's backup brain: Qwen3.5 4B (or whatever FallbackModel says) through Ollama's /api/chat.
    /// Used when LFM3-Omni isn't installed, won't start, or fails mid-question.
    /// </summary>
    public sealed class OllamaFallback : IDisposable
    {
        private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
        private readonly AceModelConfig _config;
        private bool _thinkFlagWorks = true;   // older Ollama builds reject "think"; we remember that

        public OllamaFallback(AceModelConfig config) => _config = config;

        public async Task<bool> IsUpAsync(CancellationToken ct = default)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(2));
                using var res = await _http.GetAsync(_config.OllamaUrl + "/api/tags", cts.Token).ConfigureAwait(false);
                return res.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public async Task<string> ChatAsync(IReadOnlyList<ChatTurn> history, Action<string> onText, CancellationToken ct, string? system = null)
        {
            bool includeImage = true;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    return await SendAsync(history, onText, includeImage, ct, system).ConfigureAwait(false);
                }
                catch (OllamaRejectedException ex) when (_thinkFlagWorks && ex.Message.Contains("think", StringComparison.OrdinalIgnoreCase))
                {
                    _thinkFlagWorks = false;   // this Ollama or model doesn't take "think"; ask again without it
                }
                catch (OllamaRejectedException) when (includeImage && history.Count > 0 && history[^1].Image != null)
                {
                    includeImage = false;      // the backup model can't see pictures; ask again with words only
                }
            }
            return await SendAsync(history, onText, includeImage, ct, system).ConfigureAwait(false);
        }

        private async Task<string> SendAsync(IReadOnlyList<ChatTurn> history, Action<string> onText, bool includeImage, CancellationToken ct, string? system = null)
        {
            var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = system ?? _config.SystemPrompt } };
            for (int i = 0; i < history.Count; i++)
            {
                ChatTurn turn = history[i];
                bool newest = i == history.Count - 1;
                var msg = new JsonObject { ["role"] = turn.Role };
                if (newest && includeImage && turn.Image != null)
                {
                    string text = turn.Text.Trim().Length > 0 ? turn.Text : "What's in this picture?";
                    if (turn.Audio != null) text += " (I also sent an audio clip, but this backup model can't hear it.)";
                    msg["content"] = text;
                    msg["images"] = new JsonArray { Convert.ToBase64String(turn.Image) };
                }
                else
                {
                    msg["content"] = turn.HasMedia ? turn.WithoutMedia().Text : turn.Text;
                }
                messages.Add(msg);
            }

            var body = new JsonObject
            {
                ["model"] = _config.FallbackModel,
                ["messages"] = messages,
                ["stream"] = true,
                ["options"] = new JsonObject { ["num_ctx"] = _config.ContextSize },
            };
            if (_thinkFlagWorks) body["think"] = false;   // Qwen3.5 thinks by default; Ace should just answer

            using var req = new HttpRequestMessage(HttpMethod.Post, _config.OllamaUrl + "/api/chat")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if ((int)res.StatusCode == 400)
                throw new OllamaRejectedException(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"Ollama said {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false)}");

            var full = new StringBuilder();
            using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (true)
            {
                string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line == null) break;
                if (line.Trim().Length == 0) continue;
                JsonNode? node;
                try { node = JsonNode.Parse(line); }
                catch (JsonException) { continue; }

                if (node?["error"] is JsonNode error)
                {
                    // An error before any text is usually a request problem (think flag, images): let ChatAsync retry.
                    if (full.Length == 0) throw new OllamaRejectedException(error.ToString());
                    throw new HttpRequestException("Ollama error: " + error);
                }

                string? piece = node?["message"]?["content"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(piece))
                {
                    full.Append(piece);
                    onText(piece);
                }
                if (node?["done"]?.GetValue<bool>() == true) break;
            }
            return full.ToString();
        }

        /// <summary>Unloads every model Ollama has in memory so LFM3-Omni gets the GPU to itself.</summary>
        public async Task UnloadAllAsync(CancellationToken ct = default)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                string json = await _http.GetStringAsync(_config.OllamaUrl + "/api/ps", cts.Token).ConfigureAwait(false);
                if (JsonNode.Parse(json)?["models"] is not JsonArray models) return;
                foreach (JsonNode? m in models)
                {
                    string? name = m?["name"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(name)) continue;
                    var unload = new JsonObject { ["model"] = name, ["keep_alive"] = 0 };
                    using var content = new StringContent(unload.ToJsonString(), Encoding.UTF8, "application/json");
                    using var _ = await _http.PostAsync(_config.OllamaUrl + "/api/generate", content, cts.Token).ConfigureAwait(false);
                }
            }
            catch { /* Ollama not running is fine */ }
        }

        public void Dispose() => _http.Dispose();

        private sealed class OllamaRejectedException : Exception
        {
            public OllamaRejectedException(string message) : base(message) { }
        }
    }
}
