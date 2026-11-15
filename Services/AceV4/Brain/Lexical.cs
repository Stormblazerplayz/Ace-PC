using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nimbus.AceV4.Brain
{
    /// <summary>
    /// Word matching. Works with no model at all, and keeps the meaning-matcher honest about names
    /// ("capital of Germany" must not match the France answer just because it means something similar).
    /// </summary>
    public sealed class Lexical
    {
        private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
        {
            "a","an","the","is","are","was","were","be","been","am","do","does","did","to","of","in","on","at","for","and","or",
            "it","its","it's","this","that","these","those","me","my","i","you","your","yours","we","us","our","can","could",
            "would","should","will","please","ace","hey","tell","what","whats","what's","which","who","whos","how","hows",
            "about","with","by","from","as","so","just","like","know","want","wanna","gonna","some","any","there","here",
            "im","i'm","u","ur","r","if","pls","plz","okay","ok","um","uh","hmm","really","very","get","got","give","show",
        };

        /// <summary>Lowercase words without filler, with plurals and -ing trimmed, so "files" matches "file".</summary>
        public static List<string> Tokens(string text)
        {
            string t = Phrases(text.ToLowerInvariant());
            var words = Regex.Matches(t, @"[a-z0-9]+(?:'[a-z]+)?|[+\-*/^=%]");
            var list = new List<string>();
            foreach (Match m in words)
            {
                string w = m.Value.Replace("'s", "");
                if (Stop.Contains(w)) continue;
                w = Stem(w);
                if (Same.TryGetValue(w, out var canon)) w = canon;
                list.Add(w);
            }
            return list;
        }

        /// <summary>Phrases that mean one thing: "united states" → "america", "made out of" → "made of", "go around" → "orbit".</summary>
        private static readonly (Regex From, string To)[] PhraseTable =
        {
            (new Regex(@"\b(the )?(united states( of america)?|u\.s\.a?\.?|usa)\b", RegexOptions.Compiled), "america"),
            // "in the us" means America, not "us" the word (which is filler and gets dropped).
            (new Regex(@"\b(in|of|across|around|from) the us\b", RegexOptions.Compiled), "$1 america"),
            (new Regex(@"\bus (states?|presidents?|capital|flag|army|money|dollar|history)\b", RegexOptions.Compiled), "america $1"),
            (new Regex(@"\bmade out of\b", RegexOptions.Compiled), "made of"),
            (new Regex(@"\b(go|goes|going|circle|circles|travel|travels|spin|spins|move|moves) (around|round)\b", RegexOptions.Compiled), "orbit"),
            (new Regex(@"\b(planet earth|our planet|our world|the world)\b", RegexOptions.Compiled), "earth"),
            (new Regex(@"\bgrown[ -]?ups?\b", RegexOptions.Compiled), "adult"),
            (new Regex(@"\bhuman body\b|\bperson'?s body\b|\byour body\b|\bmy body\b|\bthe body\b", RegexOptions.Compiled), "body"),
            (new Regex(@"\bnumber (\d+|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty)\b(?! of)", RegexOptions.Compiled), "$1"),
            (new Regex(@"\bin the world\b|\bon earth\b|\bof all\b|\bever\b", RegexOptions.Compiled), " "),
            (new Regex(@"\b(turn|turns|turning) (in)?to ice\b", RegexOptions.Compiled), "freeze"),
            (new Regex(@"\b(start|starts) to (boil|freeze|melt)\b", RegexOptions.Compiled), "$2"),
            (new Regex(@"\b(highest|hottest|warmest) temperature\b", RegexOptions.Compiled), "hottest"),
            (new Regex(@"\b(lowest|coldest) temperature\b", RegexOptions.Compiled), "coldest"),
            (new Regex(@"\b(short form|shortened form|short version|short way of writing|acronym|initials)\b", RegexOptions.Compiled), "abbreviation"),
            (new Regex(@"\bthe (guy|person|dude|someone|somebody|one) (who|that)\b", RegexOptions.Compiled), "who"),
        };

        private static string Phrases(string t)
        {
            foreach (var (from, to) in PhraseTable) t = from.Replace(t, to);
            return t;
        }

        /// <summary>Words that mean the same thing for matching (after stemming). Both questions and the bank go through this.</summary>
        private static readonly Dictionary<string, string> Same = BuildSame();

        private static Dictionary<string, string> BuildSame()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            void Group(string canon, params string[] words) { foreach (var w in words) d[Stem(w)] = canon; }
            Group("biggest", "largest", "hugest", "giantest", "bigest");
            Group("bigger", "larger");
            Group("fastest", "quickest", "speediest");
            Group("fast", "quick", "speed", "speedy", "quickly", "rapid");
            Group("tallest", "highest");
            Group("smallest", "tiniest", "littlest");
            Group("adult", "grownup", "grown");
            Group("kid", "child", "children", "kiddo");
            Group("ocean", "sea");
            Group("color", "colour", "colored", "coloured");
            Group("favorite", "favourite", "fave", "fav");
            Group("made", "creator", "maker", "built", "build", "builder", "created", "create", "programmed", "coded");
            Group("orbit", "revolve", "circle");
            Group("america", "american", "usa", "us");
            Group("money", "currency", "cash");
            Group("country", "nation");
            Group("teeth", "tooth");
            Group("film", "movie");
            Group("start", "begin", "beginning");
            Group("pc", "computer", "laptop");
            Group("photo", "picture", "pic", "image");
            Group("old", "age");
            Group("happen", "occur");
            Group("walk", "step", "stepped", "stepping", "walked", "land", "landed");
            Group("mean", "meaning", "definition");
            Group("person", "guy", "dude", "someone", "somebody", "human");
            Group("president", "presidential", "presidency");
            Group("abbreviation", "abbreviate", "abbreviated", "abbr", "abbrev");
            // Number words and ordinals → digits, so "sixteenth", "16th" and "number sixteen" all match.
            string[] ones = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty" };
            string[] nths = { "zeroth", "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth", "eleventh", "twelfth", "thirteenth", "fourteenth", "fifteenth", "sixteenth", "seventeenth", "eighteenth", "nineteenth", "twentieth" };
            for (int i = 0; i < ones.Length; i++) { d[ones[i]] = i.ToString(); d[nths[i]] = i.ToString(); }
            for (int i = 1; i <= 60; i++)
            {
                string suffix = (i % 100) is 11 or 12 or 13 ? "th" : (i % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
                d[i + suffix] = i.ToString();
            }
            return d;
        }

        /// <summary>Real English words (set once at load from Data/english-words.txt). Empty means "don't know".</summary>
        public static HashSet<string> English { get; set; } = new(StringComparer.Ordinal);

        public static string Stem(string w)
        {
            if (w.Length > 5 && w.EndsWith("ing")) w = w[..^3];
            else if (w.Length > 5 && w.EndsWith("ed") && !w.EndsWith("eed"))
            {
                // "walked" → "walk", "stepped" → "step"
                w = w[..^2];
                if (w.Length > 3 && w[^1] == w[^2] && "bdgmnprt".Contains(w[^1])) w = w[..^1];
            }
            else if (w.Length > 4 && w.EndsWith("ies")) w = w[..^3] + "y";
            else if (w.Length > 4 && w.EndsWith("es") && (w.EndsWith("ches") || w.EndsWith("shes") || w.EndsWith("xes") || w.EndsWith("sses"))) w = w[..^2];
            else if (w.Length > 3 && w.EndsWith("s") && !w.EndsWith("ss") && !w.EndsWith("us") && !w.EndsWith("is")) w = w[..^1];
            return w;
        }

        private readonly Dictionary<string, int> _docFreq = new(StringComparer.Ordinal);
        private int _docs;

        /// <summary>Learn how rare each word is across every question in the bank.</summary>
        public void Build(IEnumerable<IReadOnlyCollection<string>> documents)
        {
            _docFreq.Clear();
            _docs = 0;
            foreach (var doc in documents)
            {
                _docs++;
                foreach (var w in doc.Distinct()) _docFreq[w] = _docFreq.TryGetValue(w, out int n) ? n + 1 : 1;
            }
        }

        /// <summary>Rare words weigh more. A word the bank never saw weighs the most.</summary>
        public double Idf(string w)
        {
            int df = _docFreq.TryGetValue(w, out int n) ? n : 0;
            return Math.Log(1 + (_docs - df + 0.5) / (df + 0.5));
        }

        public bool Known(string w) => _docFreq.ContainsKey(w);

        /// <summary>
        /// 0 to 1: how well two word lists cover each other, rare words counting more (a weighted F1).
        /// Close spellings ("minecraf" / "minecraft") count as most of a match.
        /// </summary>
        public double Match(IReadOnlyList<string> query, IReadOnlyList<string> doc, double beta = 1.0)
        {
            if (query.Count == 0 || doc.Count == 0) return 0;
            double qTotal = 0, qHit = 0, dTotal = 0, dHit = 0;
            var docSet = new HashSet<string>(doc);
            var querySet = new HashSet<string>(query);
            foreach (var w in querySet)
            {
                double idf = Idf(w);
                qTotal += idf;
                if (docSet.Contains(w)) qHit += idf;
                else if (w.Length >= 5 && docSet.Any(d => d.Length >= 5 && Close(w, d))) qHit += idf * 0.8;
            }
            foreach (var w in docSet)
            {
                double idf = Idf(w);
                dTotal += idf;
                if (querySet.Contains(w)) dHit += idf;
                else if (w.Length >= 5 && querySet.Any(q => q.Length >= 5 && Close(w, q))) dHit += idf * 0.8;
            }
            if (qTotal <= 0 || dTotal <= 0) return 0;
            double p = dHit / dTotal, r = qHit / qTotal;
            // beta > 1 cares more that every word of the question is covered ("capital of the moon" is not "what is the moon").
            double b2 = beta * beta;
            return p + r <= 0 ? 0 : (1 + b2) * p * r / (b2 * p + r);
        }

        /// <summary>One typo apart (Damerau-Levenshtein distance 1).</summary>
        public static bool Close(string a, string b)
        {
            if (Math.Abs(a.Length - b.Length) > 1) return false;
            return Distance(a, b, 1) <= 1;
        }

        public static int Distance(string a, string b, int max = int.MaxValue)
        {
            int n = a.Length, m = b.Length;
            var d = new int[n + 1, m + 1];
            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;
            for (int i = 1; i <= n; i++)
            {
                int rowMin = int.MaxValue;
                for (int j = 1; j <= m; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    int v = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) v = Math.Min(v, d[i - 2, j - 2] + 1);
                    d[i, j] = v;
                    rowMin = Math.Min(rowMin, v);
                }
                if (rowMin > max) return rowMin;
            }
            return d[n, m];
        }

        /// <summary>
        /// Picks the sentences of a long text that best answer a question, in their original order.
        /// This is how Ace sums up a web page without an LLM.
        /// </summary>
        public static List<string> BestSentences(string question, string text, int count, Embedder? embedder = null)
        {
            var sentences = Regex.Split(text, @"(?<=[.!?])\s+(?=[A-Z0-9""'(])")
                .Select(s => Regex.Replace(s, @"\s+", " ").Trim())
                .Where(s => s.Length >= 30 && s.Length <= 400 && !s.Contains("cookie", StringComparison.OrdinalIgnoreCase)
                            && !s.Contains("subscribe", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(s, @"[a-z]"))
                .Distinct()
                .Take(400)
                .ToList();
            if (sentences.Count == 0) return new List<string>();

            var q = Tokens(question);
            var lex = new Lexical();
            lex.Build(sentences.Select(s => (IReadOnlyCollection<string>)Tokens(s)));
            var scores = new double[sentences.Count];
            for (int i = 0; i < sentences.Count; i++)
            {
                var st = Tokens(sentences[i]);
                double overlap = q.Count == 0 ? 0 : q.Count(w => st.Contains(w)) / (double)q.Count;
                scores[i] = overlap + 0.3 * lex.Match(q, st) + (i < 5 ? 0.08 * (5 - i) / 5.0 : 0);   // early sentences tend to be the summary
            }
            if (embedder?.IsReady == true)
            {
                var qv = embedder.Embed(question);
                var sv = embedder.EmbedMany(sentences.Take(160).ToList());
                if (qv != null && sv != null)
                    for (int i = 0; i < sv.Length; i++) scores[i] += 1.2 * Embedder.Dot(qv, sv[i]);
            }
            return Enumerable.Range(0, sentences.Count)
                .OrderByDescending(i => scores[i])
                .Take(count)
                .Where(i => scores[i] > 0.15)
                .OrderBy(i => i)
                .Select(i => sentences[i])
                .ToList();
        }
    }
}
