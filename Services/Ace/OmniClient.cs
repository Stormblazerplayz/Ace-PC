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
    /// Talks to llama.cpp's OpenAI-style /v1/chat/completions with LFM3-Omni-2B loaded.
    /// Sends text, pictures and audio clips, and streams the text answer back word by word.
    /// </summary>
    public sealed class OmniClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly AceModelConfig _config;

        public OmniClient(AceModelConfig config)
        {
            _config = config;
            _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };   // streaming replies can run long
        }

        /// <summary>True when llama.cpp is up and the model has finished loading.</summary>
        public async Task<bool> IsReadyAsync(CancellationToken ct = default)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(2));
                using var res = await _http.GetAsync(_config.BaseUrl + "/health", cts.Token).ConfigureAwait(false);
                return res.IsSuccessStatusCode;   // 503 while the model is still loading
            }
            catch { return false; }
        }

        /// <summary>Sends the chat and streams text pieces to onText. Returns the full answer.</summary>
        public async Task<string> ChatAsync(IReadOnlyList<ChatTurn> history, Action<string> onText, CancellationToken ct, string? system = null)
        {
            string body = BuildRequest(history, system).ToJsonString();
            using var req = new HttpRequestMessage(HttpMethod.Post, _config.BaseUrl + "/v1/chat/completions")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                string err = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new HttpRequestException($"LFM3-Omni said {(int)res.StatusCode}: {Short(err)}");
            }

            var full = new StringBuilder();
            using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (true)
            {
                string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line == null) break;                         // server closed the stream
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                string data = line.Substring(5).Trim();
                if (data == "[DONE]") break;
                if (data.Length == 0) continue;

                JsonNode? node;
                try { node = JsonNode.Parse(data); }
                catch (JsonException) { continue; }             // skip a broken line rather than die

                if (node?["error"] is JsonNode error)
                    throw new HttpRequestException("LFM3-Omni error: " + Short(error["message"]?.ToString() ?? error.ToJsonString()));

                string? piece = node?["choices"]?[0]?["delta"]?["content"]?.GetValue<string>();
                if (string.IsNullOrEmpty(piece)) continue;
                full.Append(piece);
                onText(piece);
            }
            return full.ToString();
        }

        /// <summary>Builds the JSON body. Only the newest user turn carries its picture or clip.</summary>
        public JsonObject BuildRequest(IReadOnlyList<ChatTurn> history, string? system = null)
        {
            var messages = new JsonArray
            {
                // "Respond with text." keeps the model from starting a spoken answer here; voice uses OmniVoiceSession.
                new JsonObject { ["role"] = "system", ["content"] = (system ?? _config.SystemPrompt) + " Respond with text." },
            };

            for (int i = 0; i < history.Count; i++)
            {
                ChatTurn turn = history[i];
                bool newest = i == history.Count - 1;
                if (!newest || !turn.HasMedia)
                {
                    ChatTurn plain = turn.HasMedia ? turn.WithoutMedia() : turn;
                    messages.Add(new JsonObject { ["role"] = plain.Role, ["content"] = plain.Text });
                    continue;
                }

                var parts = new JsonArray();
                if (turn.Image != null)
                {
                    parts.Add(new JsonObject
                    {
                        ["type"] = "image_url",
                        ["image_url"] = new JsonObject { ["url"] = $"data:{turn.ImageMime};base64,{Convert.ToBase64String(turn.Image)}" },
                    });
                }
                if (turn.Audio != null)
                {
                    parts.Add(new JsonObject
                    {
                        ["type"] = "input_audio",
                        ["input_audio"] = new JsonObject { ["data"] = Convert.ToBase64String(turn.Audio), ["format"] = turn.AudioFormat },
                    });
                }
                string text = turn.Text.Trim();
                if (text.Length == 0) text = turn.Audio != null ? "Answer what I said in the clip." : "What's in this picture?";
                parts.Add(new JsonObject { ["type"] = "text", ["text"] = text });
                messages.Add(new JsonObject { ["role"] = turn.Role, ["content"] = parts });
            }

            return new JsonObject
            {
                ["model"] = "lfm3-omni",       // llama.cpp serves one model and ignores the name
                ["messages"] = messages,
                ["stream"] = true,
                ["temperature"] = 0,           // Liquid recommends greedy decoding for text
                ["max_tokens"] = _config.MaxReplyTokens,
            };
        }

        /// <summary>
        /// Hides think blocks. LFM3 opens every answer with an empty one, and Qwen can write a full one.
        /// An unfinished block (still streaming) is hidden too.
        /// </summary>
        public static string Clean(string text) => Nimbus.AceV4.Brain.ThinkTags.Strip(text);

        private static string Short(string s) => s.Length > 300 ? s.Substring(0, 300) + "…" : s;

        public void Dispose() => _http.Dispose();
    }
}
