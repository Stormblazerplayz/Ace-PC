using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nimbus.AceV4.Brain
{
    /// <summary>
    /// Puts Ace's answers into words: fills in {user}, {time}, {date}, {day}, picks a different wording each time,
    /// and joins answers to several questions into one reply.
    /// </summary>
    public static class Composer
    {
        private static readonly Random Rng = new();
        private static readonly Dictionary<string, List<int>> Recent = new();
        private static Dictionary<string, List<string>>? _replies;
        private static readonly Regex Token = new(@"\{(\w+)\}");
        private static readonly HashSet<string> FilledHere = new() { "user", "time", "date", "day", "month", "year", "greeting" };

        public static string UserName = "Storm";

        /// <summary>The extra wordings in Data/replies.json (about 20 per reply). Missing or broken file = just the built-in ones.</summary>
        private static Dictionary<string, List<string>> Replies
        {
            get
            {
                if (_replies != null) return _replies;
                var loaded = new Dictionary<string, List<string>>();
                try
                {
                    string path = System.IO.Path.Combine(AppContext.BaseDirectory, "Data", "replies.json");
                    if (System.IO.File.Exists(path))
                        loaded = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<string>>>(System.IO.File.ReadAllText(path)) ?? loaded;
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("replies.json: " + ex.Message); }
                return _replies = loaded;
            }
        }

        private static HashSet<string> TokensOf(string s) =>
            Token.Matches(s).Select(m => m.Groups[1].Value).Where(t => !FilledHere.Contains(t)).ToHashSet();

        /// <summary>
        /// All the ways to say a reply: the built-in ones plus the ones in replies.json that use the same blanks
        /// (so a wording never needs a blank the skill doesn't fill, and never leaves out one every built-in wording has).
        /// </summary>
        public static IReadOnlyList<string> Wordings(string key, IReadOnlyList<string> options)
        {
            if (!Replies.TryGetValue(key, out var extra) || extra.Count == 0) return options;
            var allowed = new HashSet<string>(options.SelectMany(TokensOf));
            var needed = options.Count == 0 ? new HashSet<string>() : options.Select(TokensOf).Aggregate((a, b) => a.Intersect(b).ToHashSet());
            var all = new List<string>(options);
            foreach (var t in extra)
            {
                if (string.IsNullOrEmpty(t)) { all.Add(t); continue; }
                var tokens = TokensOf(t);
                if (tokens.IsSubsetOf(allowed) && needed.IsSubsetOf(tokens) && !all.Contains(t)) all.Add(t);
            }
            return all;
        }

        /// <summary>Picks one of the wordings, avoiding the last five used for the same key.</summary>
        public static string Pick(string key, IReadOnlyList<string> options)
        {
            var pool = Wordings(key, options);
            if (pool.Count == 0) return "";
            if (pool.Count == 1) return Fill(pool[0]);
            lock (Recent)
            {
                if (!Recent.TryGetValue(key, out var recent)) Recent[key] = recent = new List<int>();
                int avoid = Math.Min(5, pool.Count - 1);
                var recentSet = recent.Skip(Math.Max(0, recent.Count - avoid)).ToHashSet();
                var choices = Enumerable.Range(0, pool.Count).Where(i => !recentSet.Contains(i)).ToList();
                int pick = choices[Rng.Next(choices.Count)];
                recent.Add(pick);
                if (recent.Count > 10) recent.RemoveAt(0);
                return Fill(pool[pick]);
            }
        }

        public static string Pick(params string[] options) => Pick(string.Join("|", options.Take(2)), options);

        public static string Fill(string text)
        {
            var now = DateTime.Now;
            return text.Replace("{user}", UserName)
                       .Replace("{time}", now.ToString("h:mm tt", CultureInfo.CurrentCulture))
                       .Replace("{date}", now.ToString("MMMM d", CultureInfo.CurrentCulture))
                       .Replace("{day}", now.ToString("dddd", CultureInfo.CurrentCulture))
                       .Replace("{month}", now.ToString("MMMM", CultureInfo.CurrentCulture))
                       .Replace("{year}", now.Year.ToString(CultureInfo.InvariantCulture))
                       .Replace("{greeting}", Greeting(now));
        }

        public static string Greeting(DateTime now) => now.Hour switch
        {
            < 5 => "Hey, night owl",
            < 12 => "Good morning",
            < 17 => "Good afternoon",
            < 22 => "Good evening",
            _ => "Hey",
        };

        /// <summary>Joins several answers: "The capital of France is Paris. It uses the euro."</summary>
        public static string Join(IReadOnlyList<string> parts)
        {
            var clean = parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();
            if (clean.Count == 0) return "";
            if (clean.Count == 1) return clean[0];
            return string.Join(" ", clean.Select(p => Regex.IsMatch(p, @"[.!?)]$") ? p : p + "."));
        }

        /// <summary>A short "I'm not sure" line that fits how unsure Ace is.</summary>
        public static string Hedge(double confidence) => confidence switch
        {
            >= 0.8 => "",
            >= 0.6 => Pick("hedge.mid", new[] { "I'm fairly sure about this one. ", "Here's what I found. ", "I think this is right. " }),
            _ => Pick("hedge.low", new[] { "I'm not totally sure, but here's my best guess. ", "I could be wrong here. ", "This is my best guess. " }),
        };

        private static readonly System.Text.RegularExpressions.Regex CommonFirstWord = new(@"^(The|A|An|It|It's|Its|There|This|That|These|Those|They|You|Your|Yes|No|About|Around|Most|Many|Some|Each|Every|In|On|At|For|When|If|Because|Plants|People|Water|Air|Light)\b");

        /// <summary>Lower-cases the first letter only for ordinary words, so names stay capitalized ("Paris is…").</summary>
        public static string Lower(string s) => s.Length > 1 && CommonFirstWord.IsMatch(s) ? char.ToLowerInvariant(s[0]) + s[1..] : s;

        public static string Capital(string s) => s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;

        /// <summary>A list in words: "a, b and c".</summary>
        public static string List(IReadOnlyList<string> items, string and = "and")
        {
            if (items.Count == 0) return "";
            if (items.Count == 1) return items[0];
            return string.Join(", ", items.Take(items.Count - 1)) + " " + and + " " + items[^1];
        }

        public static string Plural(int n, string one, string many) => n == 1 ? "1 " + one : n + " " + many;

        /// <summary>Shortens text for speaking: first sentences up to maxChars, no lists or links.</summary>
        public static string ForSpeech(string text, int maxChars = 320)
        {
            string s = Regex.Replace(text, @"https?://\S+", "");
            s = Regex.Replace(s, @"^\s*[-•*\d]+[.)]?\s+", "", RegexOptions.Multiline);
            s = Regex.Replace(s, @"\s+", " ").Trim();
            if (s.Length <= maxChars) return s;
            var sentences = Regex.Split(s, @"(?<=[.!?])\s+");
            string outText = "";
            foreach (var sentence in sentences)
            {
                if (outText.Length + sentence.Length > maxChars && outText.Length > 0) break;
                outText += (outText.Length > 0 ? " " : "") + sentence;
            }
            return outText.Length > 0 ? outText : s[..maxChars];
        }
    }
}
