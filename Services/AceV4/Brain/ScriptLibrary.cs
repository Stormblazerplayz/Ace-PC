using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Nimbus.AceV4.Brain
{
    /// <summary>One ready-made script (Data/scripts/index.json).</summary>
    public sealed class ScriptEntry
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        /// <summary>"lua" or "cpp".</summary>
        [JsonPropertyName("lang")] public string Lang { get; set; } = "";
        /// <summary>Script, LocalScript, ModuleScript, Arduino, Console or Unreal.</summary>
        [JsonPropertyName("kind")] public string Kind { get; set; } = "";
        [JsonPropertyName("category")] public string Category { get; set; } = "";
        [JsonPropertyName("file")] public string File { get; set; } = "";
        [JsonPropertyName("summary")] public string Summary { get; set; } = "";
        [JsonPropertyName("where")] public string Where { get; set; } = "";
        [JsonPropertyName("aliases")] public List<string> Aliases { get; set; } = new();

        private string? _code;
        /// <summary>The script itself (read from disk the first time).</summary>
        [JsonIgnore]
        public string Code
        {
            get
            {
                if (_code != null) return _code;
                try { _code = System.IO.File.ReadAllText(Path.Combine(ScriptLibrary.Folder, File.Replace('/', Path.DirectorySeparatorChar))); }
                catch (Exception ex) { _code = "-- Couldn't read this script: " + ex.Message; }
                return _code;
            }
        }

        public string LangName => Lang == "lua" ? "Lua (Roblox)" : "C++";
        /// <summary>"Roblox Script", "Arduino sketch", "Unreal C++".</summary>
        public string KindName => Kind switch
        {
            "Script" => "Roblox Script",
            "LocalScript" => "Roblox LocalScript",
            "ModuleScript" => "Roblox ModuleScript",
            "Arduino" => "Arduino sketch",
            "Console" => "C++ program",
            "Unreal" => "Unreal Engine C++",
            _ => Kind,
        };
        public string Extension => Lang == "lua" ? ".lua" : Kind == "Arduino" ? ".ino" : ".cpp";
        public string Platform => Lang == "lua" ? "roblox" : Kind.ToLowerInvariant();
    }

    /// <summary>
    /// Ace's built-in script library: tested Roblox Lua scripts (kill bricks, moving parts, leaderstats...) and
    /// C++ (Arduino, console games, Unreal). Works offline.
    /// </summary>
    public static class ScriptLibrary
    {
        public static string Folder => Path.Combine(AppContext.BaseDirectory, "Data", "scripts");
        private static List<ScriptEntry>? _all;

        public static IReadOnlyList<ScriptEntry> All
        {
            get
            {
                if (_all != null) return _all;
                try
                {
                    string index = Path.Combine(Folder, "index.json");
                    _all = System.IO.File.Exists(index) ? JsonSerializer.Deserialize<List<ScriptEntry>>(System.IO.File.ReadAllText(index)) : null;
                }
                catch { }
                return _all ??= new List<ScriptEntry>();
            }
        }

        public static ScriptEntry? Get(string id) => All.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        /// <summary>Which language or platform some words ask for: "lua", "cpp", "arduino", "console", "unreal", or "".</summary>
        public static string LangOf(string text)
        {
            string l = text.ToLowerInvariant();
            if (Regex.IsMatch(l, @"\barduino\b|\bsketch\b|\besp32\b|\bservo\b.*\bcode\b")) return "arduino";
            if (Regex.IsMatch(l, @"\bunreal\b|\bue5\b|\bue4\b")) return "unreal";
            if (Regex.IsMatch(l, @"\b(lua|luau|roblox)\b")) return "lua";
            if (Regex.IsMatch(l, @"c\+\+|\bcpp\b|\bc plus plus\b|\bconsole\b|\bterminal\b")) return "cpp";
            return "";
        }

        public static IEnumerable<ScriptEntry> InLang(string lang) => lang switch
        {
            "lua" => All.Where(s => s.Lang == "lua"),
            "cpp" => All.Where(s => s.Lang == "cpp"),
            "arduino" => All.Where(s => s.Kind == "Arduino"),
            "unreal" => All.Where(s => s.Kind == "Unreal"),
            "console" => All.Where(s => s.Kind == "Console"),
            _ => All,
        };

        private static readonly HashSet<string> Filler = new()
        {
            "a", "an", "the", "me", "my", "i", "for", "to", "that", "which", "with", "of", "in", "on", "script", "scripts", "code", "lua", "luau", "roblox",
            "c", "cpp", "arduino", "unreal", "make", "makes", "making", "write", "give", "show", "need", "want", "how", "do", "can", "you", "please", "some",
            "simple", "basic", "game", "studio", "program", "sketch", "it", "is", "and", "create", "build", "get", "one", "what", "would", "could", "engine", "ue5",
            "plus", "your", "this", "using", "use", "where", "put", "example", "tutorial", "copy", "paste", "player", "players", "part", "parts", "thing",
        };

        private static string Stem(string w)
        {
            if (w.Length > 5 && w.EndsWith("ing")) return w[..^3];
            if (w.Length > 3 && w.EndsWith("es") && Regex.IsMatch(w, @"(ch|sh|x|ss)es$")) return w[..^2];
            if (w.Length > 3 && w.EndsWith("s") && !w.EndsWith("ss")) return w[..^1];
            return w;
        }

        private static List<string> Words(string s) =>
            Regex.Matches(s.ToLowerInvariant().Replace("c++", " cpp "), @"[a-z0-9]+").Select(m => Stem(m.Value)).ToList();

        /// <summary>
        /// The scripts that best match a request ("kill brick script", "arduino code for a servo"), best first,
        /// with a score from 0 to 1. An exact name or alias inside the request scores highest.
        /// </summary>
        public static List<(ScriptEntry Script, double Score)> Search(string text, int max = 5)
        {
            string lang = LangOf(text);
            string low = " " + Regex.Replace(text.ToLowerInvariant().Replace("c++", "cpp"), @"[^a-z0-9 ]", " ") + " ";
            low = Regex.Replace(low, @"\s+", " ");
            var want = Words(text).Where(w => !Filler.Contains(w)).Distinct().ToList();
            var results = new List<(ScriptEntry, double)>();
            foreach (var s in All)
            {
                if (lang.Length > 0 && !InLang(lang).Contains(s)) continue;
                double score = 0;
                foreach (var phrase in s.Aliases.Append(s.Name).Append(s.Id.Replace('-', ' ')))
                {
                    string p = " " + Regex.Replace(phrase.ToLowerInvariant().Replace("c++", "cpp"), @"[^a-z0-9 ]", " ").Trim() + " ";
                    p = Regex.Replace(p, @"\s+", " ");
                    if (p.Trim().Length < 3) continue;
                    if (low.Contains(p)) score = Math.Max(score, 0.75 + Math.Min(0.25, p.Length / 80.0));
                    else
                    {
                        // Stems: "doors that open" matches "opening door".
                        var pw = Words(phrase).Where(w => !Filler.Contains(w)).Distinct().ToList();
                        if (pw.Count == 0) continue;
                        double hit = pw.Count(w => want.Contains(w)) / (double)pw.Count;
                        double cover = want.Count == 0 ? 0 : want.Count(w => pw.Contains(w)) / (double)want.Count;
                        score = Math.Max(score, hit * 0.55 + cover * 0.2);
                    }
                }
                // Words in the summary help break ties.
                if (score > 0 && want.Count > 0)
                {
                    var sw = Words(s.Summary + " " + s.Category).ToHashSet();
                    score += 0.05 * want.Count(w => sw.Contains(w)) / want.Count;
                }
                if (score > 0) results.Add((s, Math.Min(1, score)));
            }
            return results.OrderByDescending(r => r.Item2).ThenBy(r => r.Item1.Name.Length).Take(max).ToList();
        }

        /// <summary>Categories in a language, in order, with their scripts.</summary>
        public static List<(string Category, List<ScriptEntry> Scripts)> Groups(string lang = "") =>
            InLang(lang).GroupBy(s => (s.Lang == "lua" ? "Roblox · " : s.KindName + " · ") + s.Category)
                .Select(g => (g.Key, g.OrderBy(s => s.Name).ToList())).OrderBy(g => g.Key).ToList();

        /// <summary>Lua and C++ syntax pieces for the code view's colors.</summary>
        public enum Tok { Plain, Keyword, Type, String, Comment, Number, Builtin, Preproc }

        private static readonly HashSet<string> LuaKeywords = new()
        {
            "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "if", "in", "local", "nil", "not", "or", "repeat", "return",
            "then", "true", "until", "while", "continue", "export", "type",
        };
        private static readonly HashSet<string> LuaBuiltins = new()
        {
            "game", "workspace", "script", "task", "print", "warn", "error", "pcall", "xpcall", "require", "math", "string", "table", "os", "tick",
            "Instance", "Vector3", "CFrame", "Color3", "UDim2", "UDim", "Enum", "TweenInfo", "BrickColor", "NumberSequence", "ColorSequence", "Ray",
            "RaycastParams", "OverlapParams", "ipairs", "pairs", "tostring", "tonumber", "typeof", "type", "next", "select", "unpack", "coroutine", "utf8", "self",
        };
        private static readonly HashSet<string> CppKeywords = new()
        {
            "if", "else", "for", "while", "do", "switch", "case", "default", "break", "continue", "return", "class", "struct", "public", "private", "protected",
            "virtual", "override", "const", "constexpr", "static", "new", "delete", "this", "true", "false", "nullptr", "using", "namespace", "template",
            "typename", "enum", "auto", "try", "catch", "throw", "inline", "explicit", "operator", "sizeof", "friend", "volatile", "extern", "goto", "super",
        };
        private static readonly HashSet<string> CppTypes = new()
        {
            "int", "long", "short", "char", "bool", "float", "double", "void", "unsigned", "signed", "byte", "boolean", "size_t", "uint8_t", "uint16_t",
            "uint32_t", "int32", "int64", "uint8", "float32", "FString", "FVector", "FRotator", "FName", "FHitResult", "AActor", "UPrimitiveComponent",
            "UStaticMeshComponent", "UBoxComponent", "USceneComponent", "APawn", "ACharacter", "string", "vector", "map", "Servo", "Adafruit_NeoPixel", "DHT",
        };

        /// <summary>Splits one line of code into colored pieces. inComment carries a /* */ or --[[ ]] comment across lines.</summary>
        public static List<(string Text, Tok Kind)> Colorize(string line, string lang, ref bool inComment)
        {
            var parts = new List<(string, Tok)>();
            bool lua = lang == "lua";
            int i = 0;
            var plain = new System.Text.StringBuilder();
            void Flush() { if (plain.Length > 0) { parts.Add((plain.ToString(), Tok.Plain)); plain.Clear(); } }
            while (i < line.Length)
            {
                if (inComment)
                {
                    string close = lua ? "]]" : "*/";
                    int end = line.IndexOf(close, i, StringComparison.Ordinal);
                    if (end < 0) { parts.Add((line[i..], Tok.Comment)); return parts; }
                    parts.Add((line[i..(end + 2)], Tok.Comment));
                    i = end + 2;
                    inComment = false;
                    continue;
                }
                char c = line[i];
                string rest = line[i..];
                if (lua ? rest.StartsWith("--[[") || rest.StartsWith("--[=[") : rest.StartsWith("/*"))
                {
                    Flush(); inComment = true;
                    int skip = lua ? rest.IndexOf('[', 3) + 1 : 2;
                    parts.Add((rest[..skip], Tok.Comment));
                    i += skip;
                    continue;
                }
                if (lua ? rest.StartsWith("--") : rest.StartsWith("//")) { Flush(); parts.Add((rest, Tok.Comment)); return parts; }
                if (!lua && c == '#' && line[..i].Trim().Length == 0) { Flush(); parts.Add((rest, Tok.Preproc)); return parts; }
                if (c is '"' or '\'' || (lua && c == '`'))
                {
                    Flush();
                    int j = i + 1;
                    while (j < line.Length && line[j] != c) { if (line[j] == '\\') j++; j++; }
                    j = Math.Min(line.Length, j + 1);
                    parts.Add((line[i..j], Tok.String));
                    i = j;
                    continue;
                }
                if (char.IsDigit(c) && (i == 0 || !char.IsLetterOrDigit(line[i - 1]) && line[i - 1] != '_'))
                {
                    Flush();
                    int j = i;
                    while (j < line.Length && (char.IsLetterOrDigit(line[j]) || line[j] == '.')) j++;
                    parts.Add((line[i..j], Tok.Number));
                    i = j;
                    continue;
                }
                if (char.IsLetter(c) || c == '_')
                {
                    int j = i;
                    while (j < line.Length && (char.IsLetterOrDigit(line[j]) || line[j] == '_')) j++;
                    string word = line[i..j];
                    Tok kind = lua
                        ? LuaKeywords.Contains(word) ? Tok.Keyword : LuaBuiltins.Contains(word) ? Tok.Builtin : Tok.Plain
                        : CppKeywords.Contains(word) ? Tok.Keyword : CppTypes.Contains(word) || Regex.IsMatch(word, @"^(U|A|F|E|T)[A-Z][A-Za-z]+$") ? Tok.Type
                        : Regex.IsMatch(word, @"^(UCLASS|UPROPERTY|UFUNCTION|GENERATED_BODY|USTRUCT|UENUM|Serial|std|cout|cin|endl|HIGH|LOW|INPUT|OUTPUT|INPUT_PULLUP|LED_BUILTIN)$") ? Tok.Builtin : Tok.Plain;
                    if (kind == Tok.Plain) plain.Append(word);
                    else { Flush(); parts.Add((word, kind)); }
                    i = j;
                    continue;
                }
                plain.Append(c);
                i++;
            }
            Flush();
            return parts;
        }
    }
}
