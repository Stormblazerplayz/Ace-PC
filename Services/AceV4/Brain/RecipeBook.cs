using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nimbus.AceV4.Brain
{
    /// <summary>One recipe: from Ace's built-in cookbook (Data/recipes.json) or read from a recipe website.</summary>
    public sealed class Recipe
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<string> Aliases { get; set; } = new();
        /// <summary>breakfast, lunch, dinner, side, snack, dessert or drink.</summary>
        public string Kind { get; set; } = "";
        public string Time { get; set; } = "";
        public string Serves { get; set; } = "";
        /// <summary>Uses the oven, the stove or a sharp knife, so a grown-up should help.</summary>
        public bool GrownUp { get; set; }
        public List<string> Ingredients { get; set; } = new();
        public List<string> Steps { get; set; } = new();
        /// <summary>Where it came from: "Ace's cookbook" or a website's name.</summary>
        public string Source { get; set; } = "Ace's cookbook";
        public string Url { get; set; } = "";
        public string ImageUrl { get; set; } = "";
    }

    /// <summary>
    /// Ace's built-in cookbook, so recipes work with no internet. Finds a recipe by name ("mac n cheese",
    /// "guac", "pancaks"), by what you have ("eggs and milk") or by kind ("a dessert").
    /// </summary>
    public sealed class RecipeBook
    {
        private sealed class FileShape { public int Version { get; set; } public List<Recipe> Recipes { get; set; } = new(); }

        public List<Recipe> All { get; private set; } = new();
        public int Count => All.Count;

        public static string FilePath => Path.Combine(AppContext.BaseDirectory, "Data", "recipes.json");

        public static RecipeBook Load(string? path = null)
        {
            var book = new RecipeBook();
            try
            {
                string p = path ?? FilePath;
                if (File.Exists(p))
                {
                    var shape = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(p), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (shape?.Recipes != null) book.All = shape.Recipes.Where(r => r.Name.Length > 0 && r.Steps.Count > 0).ToList();
                }
            }
            catch { }
            return book;
        }

        /// <summary>Every word used in the cookbook's names (for the typo fixer).</summary>
        public IEnumerable<string> Names() => All.SelectMany(r => new[] { r.Name }.Concat(r.Aliases));

        private static string Plain(string s)
        {
            s = s.ToLowerInvariant().Replace("&", " and ").Replace(" n ", " and ");
            s = Regex.Replace(s, @"[^a-z0-9 ]+", " ");
            s = Regex.Replace(s, @"\b(a|an|the|some|my|homemade|home made|easy|simple|quick|good|best|yummy|tasty|classic|recipe|recipes|for|of|to|make|from scratch|scratch)\b", " ");
            return Regex.Replace(s, @"\s+", " ").Trim();
        }

        private static string Single(string w) => Lexical.Stem(w);

        /// <summary>The recipe that best matches a dish name, or null when nothing is close.</summary>
        public Recipe? Find(string dish)
        {
            string want = Plain(dish);
            if (want.Length < 2) return null;
            var wantWords = want.Split(' ').Select(Single).ToList();
            Recipe? best = null;
            double bestScore = 0;
            foreach (var r in All)
            {
                foreach (var name in new[] { r.Name, r.Id.Replace('_', ' ') }.Concat(r.Aliases))
                {
                    string have = Plain(name);
                    if (have.Length == 0) continue;
                    double score;
                    if (have == want) score = 1.0;
                    else
                    {
                        var haveWords = have.Split(' ').Select(Single).ToList();
                        int hit = wantWords.Count(w => haveWords.Any(h => h == w || (w.Length >= 5 && h.Length >= 5 && Lexical.Close(w, h))));
                        if (hit == 0) continue;
                        // Every word of the name found ("chocolate chip cookies" in "soft chocolate chip cookies") counts most.
                        double recall = (double)haveWords.Count(h => wantWords.Any(w => h == w || (w.Length >= 5 && h.Length >= 5 && Lexical.Close(w, h)))) / haveWords.Count;
                        double precision = (double)hit / wantWords.Count;
                        score = 0.65 * recall + 0.35 * precision;
                    }
                    if (score > bestScore) { bestScore = score; best = r; }
                }
            }
            return bestScore >= 0.6 ? best : null;
        }

        /// <summary>Recipes you can make with some ingredients, best first ("eggs and milk" → pancakes, French toast...).</summary>
        public List<Recipe> With(string have)
        {
            var items = Regex.Split(have.ToLowerInvariant(), @",|\band\b|\bor\b|\bplus\b|&")
                .Select(x => Regex.Replace(x, @"\b(a|an|some|the|my|i have|i got|only|just|left( over)?|leftover)\b", " ").Trim())
                .Where(x => x.Length > 1).ToList();
            if (items.Count == 0) return new();
            var scored = new List<(Recipe R, int Hits, int Size)>();
            foreach (var r in All)
            {
                string all = " " + string.Join(" ", r.Ingredients).ToLowerInvariant() + " " + Plain(r.Name) + " ";
                int hits = items.Count(i => i.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => Regex.IsMatch(all, @"\b" + Regex.Escape(Single(w)) + @"(s|es)?\b")));
                if (hits > 0) scored.Add((r, hits, r.Ingredients.Count));
            }
            return scored.OrderByDescending(x => x.Hits).ThenBy(x => x.Size).Select(x => x.R).ToList();
        }

        public List<Recipe> OfKind(string kind) => All.Where(r => r.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>A wait read from a recipe step: how long, and a short name for the timer ("bake", "cool").</summary>
    public sealed record StepWait(TimeSpan Length, string Label);

    /// <summary>
    /// Reads how long a recipe step takes, so it can get a timer button: "Bake for 10 to 12 minutes" → 12 minutes,
    /// "Chill for 1 1/2 hours" → 90 minutes, "Simmer 1 hour and 15 mins" → 75 minutes, "Leave it overnight" → 8 hours.
    /// Ranges use the high end. "30-second bursts" and "every 2 seconds" aren't waits.
    /// </summary>
    public static class StepTimer
    {
        private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        private static readonly string[] Verbs =
        {
            "bake", "chill", "cool", "rest", "rise", "proof", "simmer", "marinate", "soak", "freeze", "boil", "fry", "roast", "grill", "toast",
            "microwave", "steep", "set", "refrigerate", "cook", "heat", "blend", "beat", "whisk", "knead", "stir-fry", "steam", "broil", "sit", "wait", "leave", "brew", "thaw", "melt", "stir", "scramble", "push", "flip",
        };

        private static readonly Dictionary<string, double> Words = new(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = 1, ["an"] = 1, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8,
            ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["fifteen"] = 15, ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40,
            ["forty-five"] = 45, ["fifty"] = 50, ["sixty"] = 60, ["ninety"] = 90, ["a couple of"] = 2, ["a couple"] = 2, ["couple of"] = 2,
        };

        private const string Num = @"(?:\d+(?:\.\d+)?(?:\s+\d/\d)?|\d/\d|½|a couple(?: of)?|couple of|an?|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|fifteen|twenty|thirty|forty-five|forty|fifty|sixty|ninety)";
        private const string Unit = @"(?:hours?|hrs?|h|minutes?|mins?|m|seconds?|secs?|s)";

        // "1 hour 15 minutes", "1 hour and 15 mins", "1 1/2 hours", "10 to 12 minutes", "10-12 min", "between 20 and 25 minutes", "half an hour"
        private const string NotAWait = @"(?!\s*(?:bursts?|apart|intervals?|each|at a time|of\b))";

        // Hours first, so "1 hour and 15 minutes" isn't read as a range. Then ranges ("10 to 12 minutes", "10-12 min",
        // "between 20 and 25 minutes"), then one amount ("25 minutes", "a minute"), "half an hour" and "overnight".
        private static readonly Regex Span = new(
            @"(?<![\w/.])(?<h>" + Num + @")\s*[- ]?(?:hours?|hrs?|h)\b(?:,?\s*(?:and\s+)?(?<hm>\d+)\s*[- ]?(?:minutes?|mins?|m)\b)?" + NotAWait +
            @"|(?<![\w/.])(?:between\s+)?(?<a>" + Num + @")(?:\s*(?<ua>" + Unit + @"))?\s*(?:-|–|—|to|or|and)\s*(?<b>" + Num + @")\s*[- ]?(?<ub>" + Unit + @")\b" + NotAWait +
            @"|(?<![\w/.])(?<n>" + Num + @")\s*[- ]?(?<u>" + Unit + @")\b" + NotAWait +
            @"|\b(?<half>half an? hour|half hour)\b" +
            @"|\b(?<night>overnight)\b",
            Opt);

        public static StepWait? Read(string step)
        {
            if (string.IsNullOrWhiteSpace(step)) return null;
            string text = step.Replace('½', ' ').Replace("1/2", "½").Replace("½", "1/2");
            text = Regex.Replace(text, @"\bevery\s+(?:few|couple of|\d+)\s+\w+", "", Opt);   // "shake every few seconds"
            foreach (Match m in Span.Matches(text))
            {
                double seconds;
                if (m.Groups["night"].Success) seconds = 8 * 3600;
                else if (m.Groups["half"].Success) seconds = 1800;
                else if (m.Groups["b"].Success)
                {
                    double b = Number(m.Groups["b"].Value);
                    seconds = b * UnitSeconds(m.Groups["ub"].Value);
                }
                else if (m.Groups["h"].Success)
                {
                    seconds = Number(m.Groups["h"].Value) * 3600;
                    if (m.Groups["hm"].Success) seconds += Number(m.Groups["hm"].Value) * 60;
                }
                else seconds = Number(m.Groups["n"].Value) * UnitSeconds(m.Groups["u"].Value);

                // A bare letter unit ("5 m", "2 s") is only a time right after a number with "for/about".
                if (m.Groups["u"].Value.Length == 1 && !Regex.IsMatch(text[..m.Index], @"(for|about|after|another|bake|cook|wait)\s*$", Opt)) continue;
                if (seconds < 45 || seconds > 48 * 3600) continue;   // a pause, not a wait
                return new StepWait(TimeSpan.FromSeconds(Math.Round(seconds)), LabelFor(text));
            }
            return null;
        }

        /// <summary>The cooking word in the step ("bake", "cool"), or "timer".</summary>
        public static string LabelFor(string step)
        {
            string l = step.ToLowerInvariant();
            if (Regex.IsMatch(l, @"\bin the fridge\b|\brefrigerat")) return "chill";
            if (Regex.IsMatch(l, @"\bfreezer\b")) return "freeze";
            int bestAt = int.MaxValue;
            string best = "timer";
            foreach (var v in Verbs)
            {
                var m = Regex.Match(l, @"\b" + Regex.Escape(v) + @"(s|es|ed|ing)?\b");
                if (m.Success && m.Index < bestAt) { bestAt = m.Index; best = v; }
            }
            return best switch { "sit" or "wait" or "leave" or "set" => "rest", "proof" => "rise", "stir" or "scramble" or "push" or "flip" => "cook", _ => best };
        }

        public static string Say(TimeSpan t)
        {
            if (t.TotalSeconds < 60) return $"{(int)t.TotalSeconds} seconds";
            if (t.TotalMinutes < 60) return t.TotalMinutes % 1 == 0 ? Plural((int)t.TotalMinutes, "minute") : $"{t.TotalMinutes:0.#} minutes";
            int h = (int)t.TotalHours, m = t.Minutes;
            return m == 0 ? Plural(h, "hour") : $"{Plural(h, "hour")} {Plural(m, "minute")}";
        }

        public static string Short(TimeSpan t)
        {
            if (t.TotalSeconds < 60) return $"{(int)t.TotalSeconds} s";
            if (t.TotalMinutes < 60) return $"{t.TotalMinutes:0.#} min";
            return t.Minutes == 0 ? $"{(int)t.TotalHours} h" : $"{(int)t.TotalHours} h {t.Minutes} min";
        }

        private static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

        private static double UnitSeconds(string unit)
        {
            string u = unit.ToLowerInvariant();
            if (u.StartsWith("h")) return 3600;
            if (u.StartsWith("m")) return 60;
            if (u.StartsWith("s")) return 1;
            return 60;
        }

        private static double Number(string text)
        {
            string t = text.Trim().ToLowerInvariant();
            if (Words.TryGetValue(t, out double w)) return w;
            var mixed = Regex.Match(t, @"^(\d+)\s+(\d)/(\d)$");
            if (mixed.Success) return int.Parse(mixed.Groups[1].Value) + double.Parse(mixed.Groups[2].Value) / double.Parse(mixed.Groups[3].Value);
            var frac = Regex.Match(t, @"^(\d)/(\d)$");
            if (frac.Success) return double.Parse(frac.Groups[1].Value) / double.Parse(frac.Groups[2].Value);
            return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
        }
    }
}
