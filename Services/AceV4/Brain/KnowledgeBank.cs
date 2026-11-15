using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nimbus.AceV4.Brain
{
    /// <summary>One question-and-answer entry. q holds ways people ask it; a holds ways Ace can answer.</summary>
    public sealed class QaEntry
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("topic")] public string Topic { get; set; } = "";
        [JsonPropertyName("q")] public List<string> Questions { get; set; } = new();
        [JsonPropertyName("a")] public List<string> Answers { get; set; } = new();
        [JsonPropertyName("tags")] public List<string> Tags { get; set; } = new();
        [JsonPropertyName("followups")] public List<string> Followups { get; set; } = new();
        /// <summary>Where a learned answer came from (a web page, or "you").</summary>
        [JsonPropertyName("source")] public string? Source { get; set; }
        [JsonPropertyName("learned")] public DateTime? Learned { get; set; }
        /// <summary>Times Ace used this answer and nobody said it was wrong. Helps ties.</summary>
        [JsonPropertyName("uses")] public int Uses { get; set; }
    }

    public sealed class QaFile
    {
        [JsonPropertyName("version")] public int Version { get; set; } = 1;
        [JsonPropertyName("entries")] public List<QaEntry> Entries { get; set; } = new();
    }

    public sealed record BankHit(QaEntry Entry, double Score, double Meaning, double Words, string MatchedQuestion);

    /// <summary>
    /// Thousands of questions and answers Ace can pick from, plus everything it learned
    /// (saved in %LOCALAPPDATA%\Nimbus\ace-v4\learned.json, which wins over the built-in bank).
    /// Finds answers by meaning (the MiniLM neural net) and by words (rare-word matching), blended.
    /// </summary>
    public sealed class KnowledgeBank
    {
        public static string BankPath => Path.Combine(AppContext.BaseDirectory, "Data", "qa-bank.json");
        public static string LearnedPath => Path.Combine(AceV4Config.Folder, "learned.json");
        private static string CachePath => Path.Combine(AceV4Config.Folder, "bank-vectors.bin");

        private readonly Embedder _embedder;
        private readonly object _gate = new();
        private List<QaEntry> _entries = new();
        private List<(int Entry, string Text, List<string> Words)> _questions = new();
        private float[][]? _vectors;
        private readonly Lexical _lex = new();
        private List<QaEntry> _learned = new();
        /// <summary>Every question in plain form ("whats up ace?" → "what is up"), for instant exact matches.</summary>
        private Dictionary<string, int> _exact = new();
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public int Count { get { lock (_gate) return _entries.Count; } }
        public int LearnedCount { get { lock (_gate) return _learned.Count; } }
        public int QuestionCount { get { lock (_gate) return _questions.Count; } }
        public bool UsesMeaning => _vectors != null;
        public Lexical Words => _lex;

        public KnowledgeBank(Embedder embedder) => _embedder = embedder;

        /// <summary>Loads the bank and the learned answers, and works out the meaning vectors (cached on disk).</summary>
        public void Load()
        {
            var bank = ReadFile(BankPath);
            var learned = ReadFile(LearnedPath);
            lock (_gate)
            {
                _learned = learned;
                // Learned entries go first, so on a tie they win.
                _entries = learned.Concat(bank).Where(e => e.Questions.Count > 0 && e.Answers.Count > 0).ToList();
                Rebuild();
            }
        }

        private static List<QaEntry> ReadFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<QaFile>(File.ReadAllText(path))?.Entries ?? new List<QaEntry>();
            }
            catch { }
            return new List<QaEntry>();
        }

        /// <summary>Call while holding the gate.</summary>
        private void Rebuild()
        {
            _questions = new List<(int, string, List<string>)>();
            for (int i = 0; i < _entries.Count; i++)
                foreach (var q in _entries[i].Questions.Where(q => !string.IsNullOrWhiteSpace(q)).Distinct())
                    _questions.Add((i, q, Lexical.Tokens(q)));
            _lex.Build(_questions.Select(q => (IReadOnlyCollection<string>)q.Words));
            BuildExact();
            _vectors = null;
            _oldTexts = _questions.Select(q => q.Text).ToList();
            if (!_embedder.IsReady) return;

            // Working out vectors for ~15,000 questions takes a minute the first time, so they're saved.
            string key = Hash(string.Join("\n", _questions.Select(q => q.Text)));
            _vectors = ReadCache(key, _questions.Count);
            if (_vectors != null) return;
            _vectors = _embedder.EmbedMany(_questions.Select(q => q.Text).ToList(), 64);
            if (_vectors != null) WriteCache(key, _vectors);
        }

        private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

        private static float[][]? ReadCache(string key, int count)
        {
            try
            {
                if (!File.Exists(CachePath)) return null;
                using var br = new BinaryReader(File.OpenRead(CachePath));
                if (br.ReadString() != key) return null;
                int n = br.ReadInt32(), dim = br.ReadInt32();
                if (n != count || dim != Embedder.Dim) return null;
                var all = new float[n][];
                for (int i = 0; i < n; i++)
                {
                    var v = new float[dim];
                    for (int k = 0; k < dim; k++) v[k] = br.ReadSingle();
                    all[i] = v;
                }
                return all;
            }
            catch { return null; }
        }

        private static void WriteCache(string key, float[][] vectors)
        {
            try
            {
                Directory.CreateDirectory(AceV4Config.Folder);
                using var bw = new BinaryWriter(File.Create(CachePath));
                bw.Write(key);
                bw.Write(vectors.Length);
                bw.Write(Embedder.Dim);
                foreach (var v in vectors) foreach (var x in v) bw.Write(x);
            }
            catch { }
        }

        /// <summary>
        /// Best entries for a question. Score blends meaning (0.6) and words (0.4) when the neural net is there,
        /// and is words only otherwise. Scores near 1 are near-certain; under ~0.55 is a guess.
        /// </summary>
        public List<BankHit> Search(string question, int take = 5, string? topic = null)
        {
            var words = Lexical.Tokens(question);
            string plain = Plain(question);
            string qType = QuestionType(question);
            float[]? qv = _vectors != null ? _embedder.Embed(question) : null;
            lock (_gate)
            {
                var best = new Dictionary<int, BankHit>();
                // Asked exactly like a stored question: that's the one.
                if (_exact.TryGetValue(plain, out int exactIndex) && (topic == null || _entries[exactIndex].Topic == topic))
                    best[exactIndex] = new BankHit(_entries[exactIndex], 1.0, 1.0, 1.0, question);

                // Only small words ("how are you", "what's up"): compare every word, small ones included.
                if (words.Count == 0 && plain.Length > 0)
                {
                    var all = plain.Split(' ');
                    for (int i = 0; i < _questions.Count; i++)
                    {
                        var (entryIndex, text, _) = _questions[i];
                        if (best.ContainsKey(entryIndex) && best[entryIndex].Score >= 1.0) continue;
                        var entry = _entries[entryIndex];
                        if (topic != null && entry.Topic != topic) continue;
                        var other = Plain(text).Split(' ');
                        int shared = all.Intersect(other).Count();
                        if (shared == 0) continue;
                        double score = 0.92 * shared / all.Union(other).Count();
                        if (!best.TryGetValue(entryIndex, out var old) || old.Score < score)
                            best[entryIndex] = new BankHit(entry, score, 0, score, text);
                    }
                    return best.Values.OrderByDescending(h => h.Score).ThenByDescending(h => h.Entry.Uses).Take(take).ToList();
                }

                for (int i = 0; i < _questions.Count; i++)
                {
                    var (entryIndex, text, qWords) = _questions[i];
                    var entry = _entries[entryIndex];
                    if (topic != null && entry.Topic != topic) continue;
                    double w = _lex.Match(words, qWords, _vectors == null ? 1.5 : 1.0);
                    double m = qv != null && _vectors != null && i < _vectors.Length ? Embedder.Dot(qv, _vectors[i]) : -1;
                    if (m < 0 && w < 0.2) continue;

                    double score;
                    if (m >= 0)
                    {
                        score = 0.6 * m + 0.4 * w;
                        // A rare word in the question that this entry never mentions (a different country, game, app) is a big hint it's the wrong one.
                        if (MissingRareWord(words, qWords, entry)) score -= 0.12;
                        if (TypeClash(qType, QuestionType(text))) score -= 0.08;
                        if (UnknownWord(words, qWords)) score -= 0.10;
                    }
                    else
                    {
                        score = w;
                        // Words only: a rare word the entry never mentions ("Peru" when the entry is about Chile) costs more.
                        if (w < 1.0 && MissingRareWord(words, qWords, entry)) score *= 0.85;
                        // "Who is my teacher?" is not "Should I ask my teacher for help?"
                        if (w < 1.0 && TypeClash(qType, QuestionType(text))) score *= 0.85;
                        // A word Ace has never seen ("how much does a glorp weigh") means it can't really know.
                        if (UnknownWord(words, qWords)) score *= 0.75;
                    }
                    if (entry.Learned != null) score += 0.02;
                    if (!best.TryGetValue(entryIndex, out var old) || old.Score < score)
                        best[entryIndex] = new BankHit(entry, score, Math.Max(0, m), w, text);
                }
                return best.Values.OrderByDescending(h => h.Score).ThenByDescending(h => h.Entry.Uses).Take(take).ToList();
            }
        }

        /// <summary>Call while holding the gate, after _questions changes.</summary>
        private void BuildExact()
        {
            var map = new Dictionary<string, int>();
            foreach (var (entry, text, _) in _questions)
            {
                string plainKey = Plain(text);
                if (plainKey.Length > 0) map.TryAdd(plainKey, entry);   // learned entries come first, so they win
            }
            _exact = map;
        }

        /// <summary>Lower case, contractions spelled out, no punctuation, no "Ace"/"please": "What's up, Ace?" → "what is up".</summary>
        public static string Plain(string text)
        {
            string s = (text ?? "").ToLowerInvariant().Replace('’', '\'');
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\b(what|how|who|where|when|why|that|there|it|he|she|here)'s\b", "$1 is");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\bi'm\b", "i am");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\bcan't\b", "can not");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\bwon't\b", "will not");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"n't\b", " not");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"'re\b", " are");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"'ll\b", " will");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"'ve\b", " have");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"'d\b", " would");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"[^a-z0-9 ]+", " ");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\b(ace|please|pls|plz|um+|uh+)\b", " ");
            return System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
        }

        /// <summary>The kind of question: who, when, where, why, amount, yesno, what, or "" for commands and statements.</summary>
        public static string QuestionType(string text)
        {
            string q = Plain(text);
            q = System.Text.RegularExpressions.Regex.Replace(q, @"^(hey|ok|okay|so|um|and|but|also|now)\s+", "");
            q = System.Text.RegularExpressions.Regex.Replace(q, @"^(do|does) (you|anyone|anybody) know |^(can|could) you (tell|show) me |^tell me |^i (want|need) to know |^i wonder ", "");
            if (System.Text.RegularExpressions.Regex.IsMatch(q, @"^(who|whose|whom)\b")) return "who";
            if (System.Text.RegularExpressions.Regex.IsMatch(q, @"^when\b|^what (year|time|day|date|month|century)\b")) return "when";
            if (System.Text.RegularExpressions.Regex.IsMatch(q, @"^where\b")) return "where";
            if (System.Text.RegularExpressions.Regex.IsMatch(q, @"^why\b|^how come\b")) return "why";
            if (System.Text.RegularExpressions.Regex.IsMatch(q, @"^how (many|much|long|far|old|big|tall|fast|heavy|hot|cold|deep|high|wide|small|often)\b")) return "amount";
            if (System.Text.RegularExpressions.Regex.IsMatch(q, @"^(is|are|was|were|do|does|did|can|could|should|will|would|have|has|am)\b")) return "yesno";
            if (System.Text.RegularExpressions.Regex.IsMatch(q, @"^(what|which|how)\b")) return "what";
            return "";
        }

        /// <summary>Two question kinds that can't be asking the same thing ("what" goes with anything but yes/no).</summary>
        private static bool TypeClash(string a, string b)
        {
            if (a.Length == 0 || b.Length == 0 || a == b) return false;
            if (a == "yesno" || b == "yesno") return true;
            if (a == "what" || b == "what") return false;
            if ((a == "amount" && b == "when") || (a == "when" && b == "amount")) return false;   // "how long ago" / "when"
            return true;
        }

        private bool UnknownWord(List<string> query, List<string> doc)
        {
            foreach (var w in query)
                if (w.Length >= 4 && !char.IsDigit(w[0]) && !_lex.Known(w) && !doc.Contains(w) && !doc.Any(d => d.Length >= 5 && Lexical.Close(w, d))) return true;
            return false;
        }

        private bool MissingRareWord(List<string> query, List<string> doc, QaEntry entry)
        {
            foreach (var w in query)
            {
                if (w.Length < 4 || char.IsDigit(w[0])) continue;
                // Rare in the bank, or a real word the bank has never used at all ("server").
                if (!_lex.Known(w) || _lex.Idf(w) < 5.0) continue;
                if (doc.Contains(w)) continue;
                if (entry.Tags.Any(t => Lexical.Stem(t) == w)) continue;
                return true;
            }
            return false;
        }

        /// <summary>Every entry whose id starts with the prefix ("talk.joke" → all the jokes).</summary>
        public List<QaEntry> Family(string prefix)
        {
            lock (_gate) return _entries.Where(e => e.Id.StartsWith(prefix + ".", StringComparison.Ordinal)).ToList();
        }

        /// <summary>All entries (a copy), for building word lists.</summary>
        public List<QaEntry> All()
        {
            lock (_gate) return _entries.ToList();
        }

        public QaEntry? Get(string id)
        {
            lock (_gate) return _entries.FirstOrDefault(e => e.Id == id);
        }

        public void MarkUsed(QaEntry e) { lock (_gate) e.Uses++; }

        // ================= Learning =================

        /// <summary>
        /// Saves a new answer. If a learned entry already covers the question, its answer is replaced,
        /// so a correction ("no, it's ...") sticks.
        /// </summary>
        public QaEntry Learn(IEnumerable<string> questions, string answer, string source, string topic = "learned", IEnumerable<string>? tags = null)
        {
            var qs = questions.Where(q => !string.IsNullOrWhiteSpace(q)).Select(q => q.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            lock (_gate)
            {
                var existing = _learned.FirstOrDefault(e => e.Questions.Any(q => qs.Contains(q, StringComparer.OrdinalIgnoreCase)));
                QaEntry entry;
                if (existing != null)
                {
                    existing.Answers = new List<string> { answer };
                    existing.Source = source;
                    existing.Learned = DateTime.Now;
                    foreach (var q in qs) if (!existing.Questions.Contains(q, StringComparer.OrdinalIgnoreCase)) existing.Questions.Add(q);
                    entry = existing;
                }
                else
                {
                    entry = new QaEntry
                    {
                        Id = "learned." + DateTime.Now.ToString("yyyyMMddHHmmssfff"),
                        Topic = topic,
                        Questions = qs,
                        Answers = new List<string> { answer },
                        Tags = (tags ?? Lexical.Tokens(string.Join(" ", qs)).Distinct().Take(8)).ToList(),
                        Source = source,
                        Learned = DateTime.Now,
                    };
                    _learned.Insert(0, entry);
                }
                _entries = _learned.Concat(_entries.Where(e => e.Learned == null)).ToList();
                SaveLearned();
                RebuildSoon();
                return entry;
            }
        }

        /// <summary>Removes a learned entry. Built-in answers can't be deleted, but a learned one can override them.</summary>
        public bool Forget(string id)
        {
            lock (_gate)
            {
                int n = _learned.RemoveAll(e => e.Id == id);
                if (n == 0) return false;
                _entries.RemoveAll(e => e.Id == id);
                SaveLearned();
                RebuildSoon();
                return true;
            }
        }

        public List<QaEntry> LearnedEntries() { lock (_gate) return _learned.ToList(); }

        /// <summary>
        /// Rebuilding the vectors for every question would take seconds, so a new entry only gets its own vectors
        /// added at the end of the list. Call while holding the gate.
        /// </summary>
        private void RebuildSoon()
        {
            _questions = new List<(int, string, List<string>)>();
            for (int i = 0; i < _entries.Count; i++)
                foreach (var q in _entries[i].Questions.Where(q => !string.IsNullOrWhiteSpace(q)).Distinct())
                    _questions.Add((i, q, Lexical.Tokens(q)));
            _lex.Build(_questions.Select(q => (IReadOnlyCollection<string>)q.Words));
            BuildExact();
            if (_vectors == null || !_embedder.IsReady) return;
            // Questions moved around, so match old vectors up by text, and only work out the new ones.
            var known = new Dictionary<string, float[]>(StringComparer.Ordinal);
            var oldTexts = _oldTexts ?? new List<string>();
            for (int i = 0; i < oldTexts.Count && i < _vectors.Length; i++) known[oldTexts[i]] = _vectors[i];
            var missing = _questions.Select(q => q.Text).Where(t => !known.ContainsKey(t)).Distinct().ToList();
            if (missing.Count > 0)
            {
                var fresh = _embedder.EmbedMany(missing);
                if (fresh != null) for (int i = 0; i < missing.Count; i++) known[missing[i]] = fresh[i];
            }
            _vectors = _questions.Select(q => known.TryGetValue(q.Text, out var v) ? v : new float[Embedder.Dim]).ToArray();
            _oldTexts = _questions.Select(q => q.Text).ToList();
        }

        private List<string>? _oldTexts;

        /// <summary>Call while holding the gate.</summary>
        private void SaveLearned()
        {
            try
            {
                Directory.CreateDirectory(AceV4Config.Folder);
                string tmp = LearnedPath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(new QaFile { Entries = _learned }, JsonOptions));
                File.Move(tmp, LearnedPath, true);
            }
            catch { }
        }

        /// <summary>Keeps the old question order so RebuildSoon can reuse vectors. Called after Load.</summary>
        public void RememberOrder()
        {
            lock (_gate) _oldTexts = _questions.Select(q => q.Text).ToList();
        }
    }
}
