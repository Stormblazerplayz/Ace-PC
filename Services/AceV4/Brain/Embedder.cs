using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Nimbus.AceV4.Brain
{
    /// <summary>
    /// The BERT "WordPiece" tokenizer that all-MiniLM-L6-v2 was trained with (lowercase, accents removed).
    /// </summary>
    public sealed class WordPiece
    {
        private readonly Dictionary<string, int> _vocab;
        public int Cls { get; }
        public int Sep { get; }
        public int Pad { get; }
        public int Unk { get; }

        public WordPiece(string vocabPath)
        {
            _vocab = new Dictionary<string, int>(StringComparer.Ordinal);
            int i = 0;
            foreach (var line in File.ReadLines(vocabPath))
            {
                string token = line.TrimEnd('\r', '\n');
                if (!_vocab.ContainsKey(token)) _vocab[token] = i;
                i++;
            }
            Cls = Id("[CLS]"); Sep = Id("[SEP]"); Pad = Id("[PAD]"); Unk = Id("[UNK]");
        }

        private int Id(string t) => _vocab.TryGetValue(t, out int id) ? id : 0;

        /// <summary>Token ids with [CLS] at the start and [SEP] at the end, cut to maxLen.</summary>
        public List<int> Encode(string text, int maxLen = 128)
        {
            var ids = new List<int> { Cls };
            foreach (var word in Basic(text))
            {
                foreach (var piece in Pieces(word))
                {
                    if (ids.Count >= maxLen - 1) break;
                    ids.Add(piece);
                }
                if (ids.Count >= maxLen - 1) break;
            }
            ids.Add(Sep);
            return ids;
        }

        /// <summary>Lowercase, strip accents, split on spaces and punctuation (BERT's BasicTokenizer).</summary>
        public static List<string> Basic(string text)
        {
            var sb = new StringBuilder(text.Length + 16);
            foreach (char c in text.ToLowerInvariant().Normalize(NormalizationForm.FormD))
            {
                var cat = CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat == UnicodeCategory.NonSpacingMark) continue;                 // accents
                if (c == 0 || c == 0xFFFD || (char.IsControl(c) && !char.IsWhiteSpace(c))) continue;
                if (char.IsWhiteSpace(c)) { sb.Append(' '); continue; }
                if (IsPunct(c) || IsCjk(c)) { sb.Append(' ').Append(c).Append(' '); continue; }
                sb.Append(c);
            }
            return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        private static bool IsPunct(char c)
        {
            if ((c >= 33 && c <= 47) || (c >= 58 && c <= 64) || (c >= 91 && c <= 96) || (c >= 123 && c <= 126)) return true;
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            return cat is UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation
                or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation
                or UnicodeCategory.OtherPunctuation;
        }

        private static bool IsCjk(char c) =>
            (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF) || (c >= 0xF900 && c <= 0xFAFF);

        /// <summary>Greedy longest-match-first, with "##" on pieces that continue a word.</summary>
        private IEnumerable<int> Pieces(string word)
        {
            if (word.Length > 100) { yield return Unk; yield break; }
            var output = new List<int>();
            int start = 0;
            while (start < word.Length)
            {
                int end = word.Length;
                int found = -1;
                while (start < end)
                {
                    string sub = word.Substring(start, end - start);
                    if (start > 0) sub = "##" + sub;
                    if (_vocab.TryGetValue(sub, out int id)) { found = id; break; }
                    end--;
                }
                if (found < 0) { output.Clear(); output.Add(Unk); break; }
                output.Add(found);
                start = end;
            }
            foreach (var id in output) yield return id;
        }
    }

    /// <summary>
    /// A small neural net that turns a sentence into 384 numbers, so sentences that mean the same thing
    /// land close together ("pull up youtube" ≈ "open youtube"). Runs all-MiniLM-L6-v2 with ONNX Runtime on the CPU.
    /// If the model files aren't downloaded yet, IsReady is false and Ace uses word matching only.
    /// </summary>
    public sealed class Embedder : IDisposable
    {
        public static string ModelFolder => Path.Combine(AceV4Config.Folder, "minilm");
        public static string ModelPath => Path.Combine(ModelFolder, "model.onnx");
        public static string VocabPath => Path.Combine(ModelFolder, "vocab.txt");
        public const int Dim = 384;

        private readonly InferenceSession? _session;
        private readonly WordPiece? _tokens;
        private readonly bool _needsTypeIds;
        private readonly object _gate = new();

        public bool IsReady => _session != null && _tokens != null;
        public string Problem { get; } = "";

        public Embedder()
        {
            try
            {
                if (!File.Exists(ModelPath) || !File.Exists(VocabPath))
                {
                    Problem = "the MiniLM model isn't downloaded yet (run Setup-AceV4.ps1)";
                    return;
                }
                var options = new SessionOptions
                {
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                    IntraOpNumThreads = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2)),
                };
                _session = new InferenceSession(ModelPath, options);
                _tokens = new WordPiece(VocabPath);
                _needsTypeIds = _session.InputMetadata.ContainsKey("token_type_ids");
            }
            catch (Exception ex)
            {
                _session?.Dispose();
                _session = null;
                Problem = "the MiniLM model didn't load: " + ex.Message;
            }
        }

        /// <summary>One sentence to a unit-length vector, or null if the model isn't there.</summary>
        public float[]? Embed(string text) => EmbedMany(new[] { text })?[0];

        /// <summary>Many sentences at once (faster). Returns null if the model isn't there.</summary>
        public float[][]? EmbedMany(IReadOnlyList<string> texts, int batchSize = 32)
        {
            if (_session == null || _tokens == null) return null;
            var result = new float[texts.Count][];
            for (int b = 0; b < texts.Count; b += batchSize)
            {
                int n = Math.Min(batchSize, texts.Count - b);
                var encoded = new List<int>[n];
                int len = 2;
                for (int i = 0; i < n; i++)
                {
                    encoded[i] = _tokens.Encode(texts[b + i]);
                    len = Math.Max(len, encoded[i].Count);
                }

                var ids = new DenseTensor<long>(new[] { n, len });
                var mask = new DenseTensor<long>(new[] { n, len });
                var types = new DenseTensor<long>(new[] { n, len });
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < len; j++)
                    {
                        bool real = j < encoded[i].Count;
                        ids[i, j] = real ? encoded[i][j] : _tokens.Pad;
                        mask[i, j] = real ? 1 : 0;
                        types[i, j] = 0;
                    }

                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("input_ids", ids),
                    NamedOnnxValue.CreateFromTensor("attention_mask", mask),
                };
                if (_needsTypeIds) inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", types));

                lock (_gate)
                {
                    using var outputs = _session.Run(inputs);
                    var tensor = outputs.First().AsTensor<float>();
                    var dims = tensor.Dimensions;
                    for (int i = 0; i < n; i++)
                    {
                        var v = new float[Dim];
                        if (dims.Length == 3)
                        {
                            // Mean of the token vectors (only the real tokens), like sentence-transformers does.
                            int count = 0;
                            for (int j = 0; j < len; j++)
                            {
                                if (mask[i, j] == 0) continue;
                                count++;
                                for (int k = 0; k < Dim && k < dims[2]; k++) v[k] += tensor[i, j, k];
                            }
                            if (count > 0) for (int k = 0; k < Dim; k++) v[k] /= count;
                        }
                        else
                        {
                            for (int k = 0; k < Dim && k < dims[1]; k++) v[k] = tensor[i, k];
                        }
                        Normalize(v);
                        result[b + i] = v;
                    }
                }
            }
            return result;
        }

        public static void Normalize(float[] v)
        {
            double sum = 0;
            foreach (var x in v) sum += x * x;
            double len = Math.Sqrt(sum);
            if (len < 1e-9) return;
            for (int i = 0; i < v.Length; i++) v[i] = (float)(v[i] / len);
        }

        /// <summary>Both vectors are unit length, so this is the cosine similarity (-1 to 1).</summary>
        public static float Dot(float[] a, float[] b)
        {
            float s = 0;
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++) s += a[i] * b[i];
            return s;
        }

        public void Dispose() => _session?.Dispose();
    }
}
