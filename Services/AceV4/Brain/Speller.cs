using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Nimbus.AceV4.Brain
{
    /// <summary>
    /// Fixes typos before Ace reads a request ("wat is the captial of frace" → "what is the capital of france").
    /// It learns its words from the question bank, the answers and the skill examples, then swaps a word it
    /// doesn't know for the closest common word it does (one letter off for short words, two for long ones).
    /// Names after "open", "find", "search for", "remember" and so on are only fixed to app and site names,
    /// so a file called "farside" isn't "corrected".
    /// </summary>
    public sealed class Speller
    {
        /// <summary>Kid-style and texting spellings that aren't one letter off (or would pick the wrong word).</summary>
        private static readonly Dictionary<string, string> Common = new(StringComparer.Ordinal)
        {
            ["wat"] = "what", ["wut"] = "what", ["waht"] = "what", ["wht"] = "what", ["whta"] = "what", ["whats"] = "what's", ["wats"] = "what's",
            ["hw"] = "how", ["hwo"] = "how", ["howw"] = "how", ["whos"] = "who's", ["wheres"] = "where's", ["whens"] = "when's",
            ["tel"] = "tell", ["tellme"] = "tell me", ["jok"] = "joke", ["joek"] = "joke", ["opn"] = "open", ["tak"] = "take", ["mak"] = "make", ["sho"] = "show", ["oepn"] = "open", ["opne"] = "open",
            ["tims"] = "times", ["x"] = "times", ["devided"] = "divided", ["divded"] = "divided", ["pluss"] = "plus", ["minis"] = "minus",
            ["u"] = "you", ["ur"] = "your", ["r"] = "are", ["y"] = "why", ["pls"] = "please", ["plz"] = "please", ["thx"] = "thanks", ["ty"] = "thanks",
            ["cuz"] = "because", ["bc"] = "because", ["wanna"] = "want to", ["gonna"] = "going to", ["gotta"] = "got to", ["rn"] = "right now",
            ["abt"] = "about", ["tmrw"] = "tomorrow", ["tmr"] = "tomorrow", ["tomorow"] = "tomorrow", ["tommorow"] = "tomorrow", ["tommorrow"] = "tomorrow",
            ["2day"] = "today", ["2nite"] = "tonight", ["tonite"] = "tonight", ["nite"] = "night", ["b4"] = "before", ["l8r"] = "later",
            ["minits"] = "minutes", ["minuts"] = "minutes", ["mintues"] = "minutes", ["minuets"] = "minutes", ["minites"] = "minutes", ["mins"] = "minutes",
            ["secs"] = "seconds", ["hrs"] = "hours", ["hr"] = "hour",
            ["alot"] = "a lot", ["dont"] = "don't", ["cant"] = "can't", ["wont"] = "won't", ["im"] = "i'm", ["ive"] = "i've", ["youre"] = "you're",
            ["isnt"] = "isn't", ["doesnt"] = "doesn't", ["didnt"] = "didn't", ["thats"] = "that's", ["theres"] = "there's", ["lets"] = "let's",
            ["sceenshot"] = "screenshot", ["screnshot"] = "screenshot", ["screeenshot"] = "screenshot", ["screanshot"] = "screenshot",
            ["wether"] = "weather", ["whether"] = "weather", ["wheather"] = "weather", ["weahter"] = "weather",
            ["calender"] = "calendar", ["calandar"] = "calendar", ["calander"] = "calendar",
            ["manny"] = "many", ["meny"] = "many", ["fastes"] = "fastest", ["biggist"] = "biggest", ["bigest"] = "biggest", ["smalest"] = "smallest", ["tallist"] = "tallest", ["febuary"] = "february", ["feburary"] = "february", ["wensday"] = "wednesday", ["wendsday"] = "wednesday", ["definately"] = "definitely", ["recipie"] = "recipe", ["reciepe"] = "recipe", ["remmber"] = "remember", ["rember"] = "remember",
        };

        /// <summary>Words that start a free-text name: only app and site names are fixed after them.</summary>
        private static readonly Regex NameStarter = new(@"^(open|launch|start|run|close|quit|exit|kill|minimi[sz]e|maximi[sz]e|find|search|look up|google|remember|note|learn|remind me to|define|spell|call|name|rename|play)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly Dictionary<string, int> _freq = new(StringComparer.Ordinal);
        private readonly HashSet<string> _names = new(StringComparer.Ordinal);
        private Dictionary<int, List<string>> _byLength = new();
        /// <summary>Real English words (Data/english-words.txt). These are never "fixed", even when Ace doesn't know them.</summary>
        private readonly HashSet<string> _english = new(StringComparer.Ordinal);

        public int Count => _freq.Count;

        /// <summary>Adds the words of some text. Call Seal() when done.</summary>
        public void Learn(string text, int weight = 1)
        {
            foreach (Match m in Regex.Matches(text.ToLowerInvariant(), @"[a-z][a-z']*[a-z]|[a-z]"))
            {
                string w = m.Value.Replace("'s", "");
                if (w.Length < 2 || w.Length > 24) continue;
                _freq[w] = _freq.TryGetValue(w, out int n) ? n + weight : weight;
            }
        }

        /// <summary>App and website names (fixed even after "open").</summary>
        public void LearnName(string name)
        {
            foreach (var part in Regex.Matches(name.ToLowerInvariant(), @"[a-z][a-z0-9]+").Select(m => m.Value))
            {
                if (part.Length < 3) continue;
                _names.Add(part);
                _freq[part] = _freq.TryGetValue(part, out int n) ? n + 5 : 5;
            }
        }

        public void Protect(IEnumerable<string> realWords)
        {
            foreach (var w in realWords) if (w.Length > 0) _english.Add(w.Trim().ToLowerInvariant());
            Lexical.English = _english;
        }

        public void Seal() => _byLength = _freq.Keys.GroupBy(w => w.Length).ToDictionary(g => g.Key, g => g.ToList());

        public bool Knows(string word) => _freq.ContainsKey(word);

        /// <summary>Returns the fixed text, and the changes made ("frace → france").</summary>
        public (string Text, List<string> Changes) Fix(string text)
        {
            var changes = new List<string>();
            if (string.IsNullOrWhiteSpace(text) || _freq.Count == 0) return (text, changes);

            // "are their" / "is their" at the end means "there".
            string t = Regex.Replace(text, @"\b(are|is)\s+their\s*\??$", m => { changes.Add("their → there"); return m.Groups[1].Value + " there?"; }, RegexOptions.IgnoreCase);

            var sb = new StringBuilder();
            int pos = 0;
            bool namesOnly = false;
            string lowerSoFar = "";
            foreach (Match m in Regex.Matches(t, @"[A-Za-z][A-Za-z']*"))
            {
                sb.Append(t, pos, m.Index - pos);
                pos = m.Index + m.Length;
                string word = m.Value;
                string lower = word.ToLowerInvariant();
                string fixedWord = word;

                if (Common.TryGetValue(lower, out var common) && !(lower == "x" && !Regex.IsMatch(t, @"\d\s*x\s*\d", RegexOptions.IgnoreCase)) && !(lower == "whether" && Regex.IsMatch(t, @"\bwhether\b.+\bor\b", RegexOptions.IgnoreCase)) && !(lower is "r" or "u" or "y" && t.Trim().Length <= 2))
                    fixedWord = common;
                else if (Closest(lower, namesOnly) is { } better) fixedWord = better;

                if (!string.Equals(fixedWord, word, StringComparison.OrdinalIgnoreCase))
                {
                    changes.Add(lower + " → " + fixedWord);
                    if (char.IsUpper(word[0]) && fixedWord.Length > 0) fixedWord = char.ToUpperInvariant(fixedWord[0]) + fixedWord[1..];
                }
                sb.Append(fixedWord);

                lowerSoFar = (lowerSoFar + " " + fixedWord.ToLowerInvariant()).Trim();
                // After "open"/"find"/"remember"... the rest is a name: only fix it to known app/site names.
                if (!namesOnly && NameStarter.IsMatch(lowerSoFar.Split(' ').Length <= 3 ? lowerSoFar : "")) namesOnly = true;
            }
            sb.Append(t, pos, t.Length - pos);
            return (sb.ToString(), changes);
        }

        /// <summary>The best known word one or two typos away, or null if the word is fine or nothing is close.</summary>
        public string? Closest(string lower, bool namesOnly = false)
        {
            if (lower.Length < 3 || lower.Contains('\'')) return null;
            if (_freq.ContainsKey(lower) || _english.Contains(lower)) return null;
            // "mooon" → "moon", "helllo" → "hello"
            string squeezed = Regex.Replace(lower, @"(.)\1{2,}", "$1$1");
            if (squeezed != lower && _freq.ContainsKey(squeezed) && (!namesOnly || _names.Contains(squeezed))) return squeezed;
            string single = Regex.Replace(lower, @"(.)\1+", "$1");
            if (single != lower && _freq.ContainsKey(single) && (!namesOnly || _names.Contains(single))) return single;

            int maxDist = lower.Length <= 4 ? 1 : lower.Length <= 7 ? (namesOnly ? 1 : 2) : 2;
            string? best = null;
            double bestScore = double.MaxValue;
            for (int len = lower.Length - maxDist; len <= lower.Length + maxDist; len++)
            {
                if (len < 2 || !_byLength.TryGetValue(len, out var words)) continue;
                foreach (var w in words)
                {
                    if (namesOnly && !_names.Contains(w)) continue;
                    // Short words must keep their first letter ("tak" → "take", not "oak").
                    if (lower.Length <= 5 && w[0] != lower[0]) continue;
                    int d = Lexical.Distance(lower, w, maxDist);
                    if (d > maxDist) continue;
                    // Fewer typos first, then the same first letter, then the more common word.
                    double score = d * 10 - (w[0] == lower[0] ? 3 : 0) - (w[^1] == lower[^1] ? 1 : 0) - Math.Log(1 + _freq[w]);
                    if (score < bestScore) { bestScore = score; best = w; }
                }
            }
            if (best == null) return null;
            // Two typos in a word of 5 to 7 letters only when the start matches, so names don't become random words.
            if (lower.Length <= 7 && Lexical.Distance(lower, best, 2) == 2 && !best.StartsWith(lower[..2], StringComparison.Ordinal)) return null;
            return best;
        }
    }
}
