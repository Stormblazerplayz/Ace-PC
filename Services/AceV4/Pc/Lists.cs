using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nimbus.AceV4.Pc
{
    // ============================================================================ Notes

    public sealed class Note
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Text { get; set; } = "";
        public DateTime Created { get; set; } = DateTime.Now;
    }

    /// <summary>Quick notes ("take a note: buy new headphones"), saved in notes.json on this PC.</summary>
    public static class NoteStore
    {
        private static readonly object Gate = new();
        private static List<Note>? _all;
        private static string FilePath => Path.Combine(AceV4Config.Folder, "notes.json");
        /// <summary>Raised (on any thread) when a note is added or removed.</summary>
        public static event Action? Changed;

        private static List<Note> All
        {
            get
            {
                if (_all != null) return _all;
                try { if (File.Exists(FilePath)) _all = JsonSerializer.Deserialize<List<Note>>(File.ReadAllText(FilePath)); } catch { }
                return _all ??= new List<Note>();
            }
        }

        /// <summary>Newest first.</summary>
        public static List<Note> List() { lock (Gate) return All.OrderByDescending(n => n.Created).ToList(); }

        public static int Count { get { lock (Gate) return All.Count; } }

        public static Note Add(string text)
        {
            var n = new Note { Text = text.Trim() };
            lock (Gate) { All.Add(n); Save(); }
            Changed?.Invoke();
            return n;
        }

        public static bool Remove(string id)
        {
            bool ok;
            lock (Gate) { ok = All.RemoveAll(n => n.Id == id) > 0; if (ok) Save(); }
            if (ok) Changed?.Invoke();
            return ok;
        }

        /// <summary>The note that best matches some words ("the one about grandma"), or null.</summary>
        public static Note? Find(string words)
        {
            var want = Words(words);
            if (want.Count == 0) return null;
            lock (Gate)
                return All.Select(n => (n, score: Words(n.Text).Count(w => want.Contains(w))))
                          .Where(x => x.score > 0).OrderByDescending(x => x.score).ThenByDescending(x => x.n.Created)
                          .Select(x => x.n).FirstOrDefault();
        }

        public static int Clear()
        {
            int n;
            lock (Gate) { n = All.Count; All.Clear(); Save(); }
            Changed?.Invoke();
            return n;
        }

        private static readonly HashSet<string> Small = new() { "the", "a", "an", "to", "my", "about", "that", "one", "note", "of", "on", "in", "for", "and", "is", "it" };
        /// <summary>Lowercase word stems ("studying" → "study"), small words left out.</summary>
        public static HashSet<string> Words(string s) =>
            Regex.Matches(s.ToLowerInvariant(), @"[a-z0-9']+").Select(m => Stem(m.Value)).Where(w => w.Length > 1 && !Small.Contains(w)).ToHashSet();

        public static string Stem(string w)
        {
            if (w.Length > 5 && w.EndsWith("ying")) return w[..^4] + "y";
            if (w.Length > 5 && w.EndsWith("ing")) return w[..^3];
            if (w.Length > 4 && w.EndsWith("ies")) return w[..^3] + "y";
            if (w.Length > 4 && w.EndsWith("ed")) return w[..^2];
            if (w.Length > 3 && w.EndsWith("s") && !w.EndsWith("ss")) return w[..^1];
            return w;
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(AceV4Config.Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(All));
            }
            catch { }
        }
    }

    // ============================================================================ Shopping list

    public sealed class ShopItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Name { get; set; } = "";
        /// <summary>How much (2 for "2 cups"), or null when no amount was given.</summary>
        public double? Amount { get; set; }
        /// <summary>Singular unit ("cup"), or "".</summary>
        public string Unit { get; set; } = "";
        public bool Checked { get; set; }
        /// <summary>The recipe it came from, if any.</summary>
        public string Recipe { get; set; } = "";

        public override string ToString() => ShoppingList.Describe(this);
    }

    /// <summary>
    /// A shopping list (shopping.json). "2 cups of flour" plus "1 cup of flour" becomes "3 cups flour",
    /// and a recipe's ingredients can be added in one go.
    /// </summary>
    public static class ShoppingList
    {
        private static readonly object Gate = new();
        private static List<ShopItem>? _all;
        private static string FilePath => Path.Combine(AceV4Config.Folder, "shopping.json");
        public static event Action? Changed;

        private static List<ShopItem> All
        {
            get
            {
                if (_all != null) return _all;
                try { if (File.Exists(FilePath)) _all = JsonSerializer.Deserialize<List<ShopItem>>(File.ReadAllText(FilePath)); } catch { }
                return _all ??= new List<ShopItem>();
            }
        }

        public static List<ShopItem> Items() { lock (Gate) return All.ToList(); }
        public static int Count { get { lock (Gate) return All.Count(i => !i.Checked); } }

        private static readonly Dictionary<string, string> UnitWords = new(StringComparer.OrdinalIgnoreCase)
        {
            ["cup"] = "cup", ["cups"] = "cup", ["c"] = "cup",
            ["tablespoon"] = "tablespoon", ["tablespoons"] = "tablespoon", ["tbsp"] = "tablespoon", ["tbs"] = "tablespoon",
            ["teaspoon"] = "teaspoon", ["teaspoons"] = "teaspoon", ["tsp"] = "teaspoon",
            ["ounce"] = "ounce", ["ounces"] = "ounce", ["oz"] = "ounce",
            ["pound"] = "pound", ["pounds"] = "pound", ["lb"] = "pound", ["lbs"] = "pound",
            ["gram"] = "gram", ["grams"] = "gram", ["g"] = "gram", ["kg"] = "kilogram", ["kilogram"] = "kilogram", ["kilograms"] = "kilogram",
            ["ml"] = "milliliter", ["milliliter"] = "milliliter", ["milliliters"] = "milliliter",
            ["liter"] = "liter", ["liters"] = "liter", ["litre"] = "liter", ["litres"] = "liter",
            ["pinch"] = "pinch", ["pinches"] = "pinch", ["dash"] = "dash",
            ["can"] = "can", ["cans"] = "can", ["package"] = "package", ["packages"] = "package", ["pack"] = "pack", ["packs"] = "pack",
            ["stick"] = "stick", ["sticks"] = "stick", ["slice"] = "slice", ["slices"] = "slice", ["clove"] = "clove", ["cloves"] = "clove",
            ["bag"] = "bag", ["bags"] = "bag", ["box"] = "box", ["boxes"] = "box", ["bottle"] = "bottle", ["bottles"] = "bottle",
            ["jar"] = "jar", ["jars"] = "jar", ["loaf"] = "loaf", ["loaves"] = "loaf", ["dozen"] = "dozen", ["carton"] = "carton", ["cartons"] = "carton",
            ["bunch"] = "bunch", ["bunches"] = "bunch", ["head"] = "head", ["heads"] = "head", ["gallon"] = "gallon", ["gallons"] = "gallon",
        };

        private static readonly Dictionary<string, double> NumberWords = new()
        {
            ["a"] = 1, ["an"] = 1, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6,
            ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["twelve"] = 12, ["half a"] = 0.5, ["a couple of"] = 2, ["a few"] = 3,
        };

        /// <summary>Reads "2 cups of flour", "1 1/2 cups milk", "½ tsp salt", "three apples" or "bananas".</summary>
        public static ShopItem Parse(string text, string recipe = "")
        {
            string t = Regex.Replace(text.Trim(), @"\s+", " ").Trim(' ', '.', ',', '-');
            t = t.Replace("½", " 1/2").Replace("¼", " 1/4").Replace("¾", " 3/4").Replace("⅓", " 1/3").Replace("⅔", " 2/3").Replace("⅛", " 1/8").Trim();
            t = Regex.Replace(t, @"(\d)\s+(?=\d/\d)", "$1+");   // "1 1/2" → "1+1/2"
            double? amount = null;
            var num = Regex.Match(t, @"^(\d+\+\d+/\d+|\d+/\d+|\d+(?:\.\d+)?)\b\s*");
            if (num.Success)
            {
                amount = num.Groups[1].Value.Split('+').Sum(ParseNumber);
                t = t[num.Length..];
                // "2-3 apples": take the bigger number.
                var range = Regex.Match(t, @"^(?:-|to)\s*(\d+(?:\.\d+)?)\s*");
                if (range.Success) { amount = double.Parse(range.Groups[1].Value, CultureInfo.InvariantCulture); t = t[range.Length..]; }
            }
            else
            {
                foreach (var (w, v) in NumberWords.OrderByDescending(kv => kv.Key.Length))
                {
                    var m = Regex.Match(t, @"^" + Regex.Escape(w) + @"\s+", RegexOptions.IgnoreCase);
                    if (!m.Success) continue;
                    string after = t[m.Length..];
                    // "a pinch of salt" and "a bag of chips" keep the unit; "a banana" is 1 banana.
                    amount = v; t = after; break;
                }
            }
            string unit = "";
            var u = Regex.Match(t, @"^\(?([a-z]+)\.?\)?\s+(?:of\s+)?", RegexOptions.IgnoreCase);
            if (u.Success && UnitWords.TryGetValue(u.Groups[1].Value, out var unitName) && t.Length > u.Length)
            {
                unit = unitName;
                t = t[u.Length..];
                amount ??= 1;
            }
            t = Regex.Replace(t, @"^of\s+", "", RegexOptions.IgnoreCase);
            // "(about 2 cups)", ", softened", "to taste": keep the name only.
            t = Regex.Replace(t, @"\s*\(.*?\)", "");
            t = Regex.Replace(t, @",.*$|\s+(to taste|for (serving|topping|garnish|frying|greasing).*|as needed|optional)$", "", RegexOptions.IgnoreCase).Trim();
            if (t.Length == 0) t = text.Trim();
            return new ShopItem { Name = t, Amount = amount, Unit = unit, Recipe = recipe };
        }

        private static double ParseNumber(string s)
        {
            if (s.Contains('/'))
            {
                var p = s.Split('/');
                return double.Parse(p[1], CultureInfo.InvariantCulture) == 0 ? 0 : double.Parse(p[0], CultureInfo.InvariantCulture) / double.Parse(p[1], CultureInfo.InvariantCulture);
            }
            return double.Parse(s, CultureInfo.InvariantCulture);
        }

        /// <summary>The name used to match the same thing ("Eggs" = "egg", "all-purpose flour" ≠ "flour").</summary>
        public static string Key(string name)
        {
            string k = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9 ]", " ");
            k = Regex.Replace(k, @"^(some|fresh|large|small|medium|big|extra)\s+", "");
            k = Regex.Replace(k, @"\s+", " ").Trim();
            if (k.EndsWith("oes")) k = k[..^2];
            else if (k.EndsWith("ies")) k = k[..^3] + "y";
            else if (k.EndsWith("es") && Regex.IsMatch(k, @"(ch|sh|x|ss)es$")) k = k[..^2];
            else if (k.EndsWith("s") && !k.EndsWith("ss") && k.Length > 3) k = k[..^1];
            return k;
        }

        /// <summary>Adds an item, or adds to the amount of the same item already on the list. Returns the item as it is now.</summary>
        public static ShopItem Add(ShopItem item)
        {
            ShopItem result;
            lock (Gate)
            {
                var same = All.FirstOrDefault(i => !i.Checked && Key(i.Name) == Key(item.Name) && i.Unit == item.Unit);
                if (same != null)
                {
                    if (same.Amount != null && item.Amount != null) same.Amount += item.Amount;
                    else same.Amount ??= item.Amount;
                    if (same.Recipe.Length == 0) same.Recipe = item.Recipe;
                    result = same;
                }
                else
                {
                    All.Add(item);
                    result = item;
                }
                Save();
            }
            Changed?.Invoke();
            return result;
        }

        public static ShopItem Add(string text, string recipe = "") => Add(Parse(text, recipe));

        /// <summary>Things nobody needs to buy.</summary>
        public static bool Skip(string ingredient) => Regex.IsMatch(ingredient, @"^\s*(?:\d[\d/ .]*\s*(?:cups?|tbsp|tablespoons?|tsp|teaspoons?|ml|liters?)?\s*(?:of\s+)?)?(?:cold |warm |hot |boiling |lukewarm |ice )?(?:tap )?water\b|^\s*(?:ice cubes?|ice)\s*$", RegexOptions.IgnoreCase);

        /// <summary>Removes the item(s) matching a name. Returns what was removed.</summary>
        public static List<ShopItem> Remove(string name)
        {
            string key = Key(Regex.Replace(name, @"^(the|some|all the|my)\s+", "", RegexOptions.IgnoreCase));
            List<ShopItem> gone;
            lock (Gate)
            {
                gone = All.Where(i => Key(i.Name) == key).ToList();
                if (gone.Count == 0) gone = All.Where(i => Key(i.Name).Contains(key) || key.Contains(Key(i.Name))).ToList();
                foreach (var g in gone) All.Remove(g);
                if (gone.Count > 0) Save();
            }
            if (gone.Count > 0) Changed?.Invoke();
            return gone;
        }

        public static bool RemoveId(string id)
        {
            bool ok;
            lock (Gate) { ok = All.RemoveAll(i => i.Id == id) > 0; if (ok) Save(); }
            if (ok) Changed?.Invoke();
            return ok;
        }

        public static void SetChecked(string id, bool on)
        {
            lock (Gate) { var i = All.FirstOrDefault(x => x.Id == id); if (i == null) return; i.Checked = on; Save(); }
            Changed?.Invoke();
        }

        public static int Clear(bool onlyChecked = false)
        {
            int n;
            lock (Gate) { n = All.RemoveAll(i => !onlyChecked || i.Checked); Save(); }
            Changed?.Invoke();
            return n;
        }

        /// <summary>"3 cups flour", "2 eggs", "bananas".</summary>
        public static string Describe(ShopItem i)
        {
            if (i.Amount is not double a) return i.Name;
            string n = Amount(a);
            if (i.Unit.Length == 0) return $"{n} {i.Name}";
            string unit = a > 1 ? PluralUnit(i.Unit) : i.Unit;
            return $"{n} {unit} {i.Name}";
        }

        private static string PluralUnit(string u) => u switch
        {
            "pinch" => "pinches", "dash" => "dashes", "box" => "boxes", "bunch" => "bunches", "loaf" => "loaves", "dozen" => "dozen",
            _ => u + "s",
        };

        /// <summary>2 → "2", 1.5 → "1 1/2", 0.333 → "1/3".</summary>
        public static string Amount(double a)
        {
            double whole = Math.Floor(a + 1e-9);
            double frac = a - whole;
            (double v, string s)[] fracs = { (0, ""), (0.125, "1/8"), (0.25, "1/4"), (1.0 / 3, "1/3"), (0.5, "1/2"), (2.0 / 3, "2/3"), (0.75, "3/4"), (1, "") };
            var best = fracs.OrderBy(f => Math.Abs(f.v - frac)).First();
            if (Math.Abs(best.v - frac) > 0.04) return Math.Round(a, 2).ToString(CultureInfo.InvariantCulture);
            if (best.v >= 1) { whole += 1; best.s = ""; }
            if (whole == 0) return best.s.Length > 0 ? best.s : "0";
            return best.s.Length > 0 ? $"{whole:0} {best.s}" : $"{whole:0}";
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(AceV4Config.Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(All));
            }
            catch { }
        }
    }
}
