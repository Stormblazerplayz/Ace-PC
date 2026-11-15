using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nimbus.AceV4.Brain
{
    /// <summary>One part of a script and what it does in plain words.</summary>
    public sealed class ExplainStep
    {
        public int FromLine;
        public int ToLine;
        public string Code = "";
        public string Plain = "";
        public string Lines => FromLine == ToLine ? $"Line {FromLine}" : $"Lines {FromLine}–{ToLine}";
    }

    /// <summary>
    /// Explains a script part by part, offline. The script's own comments come first, then a big list of
    /// patterns that know what Roblox Lua, Arduino, C++ and Unreal lines do ("part.Touched:Connect(onTouched)" →
    /// "Whenever something touches part, run onTouched.").
    /// </summary>
    public static class ScriptExplainer
    {
        private static readonly Dictionary<string, string> LuaEvents = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Touched"] = "something touches it",
            ["TouchEnded"] = "something stops touching it",
            ["PlayerAdded"] = "a player joins the game",
            ["PlayerRemoving"] = "a player leaves the game",
            ["CharacterAdded"] = "the player's character spawns",
            ["CharacterRemoving"] = "the player's character is removed",
            ["Heartbeat"] = "the game updates (about 60 times a second)",
            ["RenderStepped"] = "the screen draws a frame (about 60 times a second)",
            ["Stepped"] = "the physics updates (about 60 times a second)",
            ["MouseClick"] = "someone clicks it",
            ["Activated"] = "the player clicks while holding the tool",
            ["Equipped"] = "the player takes out the tool",
            ["Unequipped"] = "the player puts the tool away",
            ["Triggered"] = "someone uses the prompt",
            ["Died"] = "the character dies",
            ["Changed"] = "a value changes",
            ["InputBegan"] = "a key, mouse button or touch starts",
            ["InputEnded"] = "a key, mouse button or touch ends",
            ["JumpRequest"] = "the player presses jump",
            ["StateChanged"] = "the character changes state (like jumping or falling)",
            ["OnServerEvent"] = "a player's game sends this message to the server",
            ["OnClientEvent"] = "the server sends this message to the player",
            ["MouseButton1Click"] = "the button is clicked",
            ["Completed"] = "the animation finishes",
            ["ChildAdded"] = "something is put inside it",
            ["DescendantAdded"] = "something is put anywhere inside it",
            ["Ended"] = "the sound finishes",
        };

        private static readonly Dictionary<string, string> LuaServices = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Players"] = "the list of everyone in the game",
            ["TweenService"] = "smooth animations (tweens)",
            ["DataStoreService"] = "saving data so it's still there next time the player joins",
            ["RunService"] = "running code every frame",
            ["ReplicatedStorage"] = "a shared folder that both the server and players can see",
            ["ServerStorage"] = "a folder only the server can see",
            ["ServerScriptService"] = "where server scripts live",
            ["UserInputService"] = "the keyboard, mouse and touch screen",
            ["ContextActionService"] = "binding keys and phone buttons to actions",
            ["Lighting"] = "the sky, sun and time of day",
            ["SoundService"] = "sounds and music",
            ["Debris"] = "deleting things automatically after a while",
            ["MarketplaceService"] = "game passes and purchases",
            ["PathfindingService"] = "finding a path around walls for NPCs",
            ["Teams"] = "the teams in the game",
            ["CollectionService"] = "tags you can put on parts",
            ["StarterGui"] = "the on-screen menus",
            ["Workspace"] = "everything in the 3D world",
        };

        private static readonly Dictionary<string, string> LuaNew = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Folder"] = "a folder",
            ["IntValue"] = "a value that holds a whole number",
            ["NumberValue"] = "a value that holds a number",
            ["StringValue"] = "a value that holds text",
            ["BoolValue"] = "a value that holds true or false",
            ["Part"] = "a new part",
            ["Sound"] = "a sound",
            ["Attachment"] = "an attachment (a point stuck to a part)",
            ["LinearVelocity"] = "a force that pushes at a set speed",
            ["BodyVelocity"] = "a force that pushes at a set speed",
            ["AlignPosition"] = "a force that pulls it to a spot",
            ["Highlight"] = "a glowing outline",
            ["PointLight"] = "a light that shines in every direction",
            ["ProximityPrompt"] = "a \"press E\" prompt",
            ["BillboardGui"] = "a label that floats in the 3D world",
            ["TextLabel"] = "a text label",
            ["ScreenGui"] = "an on-screen menu",
            ["Explosion"] = "an explosion",
            ["Tool"] = "a tool the player can hold",
            ["RemoteEvent"] = "a message line between players and the server",
            ["Weld"] = "a weld that sticks two parts together",
            ["WeldConstraint"] = "a weld that sticks two parts together",
            ["Model"] = "a model",
            ["Animation"] = "an animation",
            ["ClickDetector"] = "a click detector so players can click it",
            ["ParticleEmitter"] = "a particle effect",
            ["Fire"] = "fire",
            ["Sparkles"] = "sparkles",
        };

        private static readonly Dictionary<string, string> CppIncludes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["iostream"] = "printing text and reading what you type",
            ["string"] = "text (strings)",
            ["vector"] = "lists that can grow",
            ["cstdlib"] = "random numbers and other handy tools",
            ["ctime"] = "the clock (used to make random numbers different each time)",
            ["random"] = "random numbers",
            ["algorithm"] = "sorting and searching",
            ["map"] = "lookup tables",
            ["cctype"] = "checking letters (upper case, lower case, digits)",
            ["limits"] = "the biggest and smallest numbers",
            ["fstream"] = "reading and writing files",
            ["iomanip"] = "lining up printed numbers",
            ["thread"] = "pausing and running things at the same time",
            ["chrono"] = "measuring time",
            ["Arduino.h"] = "the basic Arduino commands (most sketches get it without asking)",
            ["Servo.h"] = "controlling servo motors",
            ["Adafruit_NeoPixel.h"] = "NeoPixel LED strips",
            ["DHT.h"] = "the DHT temperature and humidity sensor",
            ["Wire.h"] = "talking to chips over I2C",
            ["SPI.h"] = "talking to chips over SPI",
        };

        /// <summary>Splits the script into parts and explains each one.</summary>
        public static List<ExplainStep> Explain(ScriptEntry s) => Explain(s.Code, s.Lang);

        public static List<ExplainStep> Explain(string code, string lang)
        {
            bool lua = lang == "lua";
            var lines = code.Replace("\r\n", "\n").Split('\n');
            var steps = new List<ExplainStep>();
            int i = 0;

            // The big comment at the top is the "about" part; the card shows the summary instead.
            i = SkipHeader(lines, lua);

            var comment = new List<string>();
            ExplainStep? cur = null;
            var curCode = new List<string>();
            var curRules = new List<string>();
            int blockDepth = 0;

            void Close()
            {
                if (cur == null) return;
                cur.Code = string.Join("\n", curCode).TrimEnd();
                var said = new List<string>();
                foreach (var c in cur.Plain.Length > 0 ? new[] { cur.Plain } : Array.Empty<string>()) said.Add(c);
                foreach (var r in curRules.Distinct())
                    if (said.Count < 4 && !said.Any(x => x.Equals(r, StringComparison.OrdinalIgnoreCase))) said.Add(r);
                cur.Plain = string.Join(" ", said).Trim();
                if (cur.Plain.Length == 0 && steps.Count > 0)
                {
                    // Only "end" or "}" lines: they belong to the part above.
                    var prev = steps[^1];
                    prev.ToLine = cur.ToLine;
                    prev.Code = (prev.Code + "\n" + cur.Code).TrimEnd();
                }
                else
                {
                    if (cur.Plain.Length == 0) cur.Plain = "The start of the script.";
                    steps.Add(cur);
                }
                cur = null;
                curCode.Clear();
                curRules.Clear();
            }

            for (; i < lines.Length; i++)
            {
                string raw = lines[i].Replace("\t", "    ");
                string t = raw.Trim();

                // A block comment in the middle (--[[ ]] or /* */) reads like a normal comment.
                if (blockDepth > 0 || (lua ? t.StartsWith("--[[") : t.StartsWith("/*")))
                {
                    if (blockDepth == 0) Close();
                    blockDepth = 1;
                    string inner = Regex.Replace(t, lua ? @"^--\[\[|\]\]$" : @"^/\*+|\*+/$|^\*\s?", "").Trim();
                    if (inner.Length > 0) AddComment(comment, inner);
                    if (lua ? t.Contains("]]") : t.Contains("*/")) blockDepth = 0;
                    continue;
                }

                if (t.Length == 0) { Close(); continue; }

                bool isComment = lua ? t.StartsWith("--") : t.StartsWith("//");
                if (isComment)
                {
                    if (curCode.Count > 0) Close();
                    string c = Regex.Replace(t, lua ? @"^--+\s*" : @"^//+\s*", "").Trim();
                    c = Regex.Replace(c, @"^[-=*_#~]{3,}\s*|\s*[-=*_#~]{3,}$", "").Trim();
                    if (Regex.IsMatch(c, @"^[\w/]+\.(?:h|hpp|cpp|ino|lua)$")) c = $"This is the start of the file {c}.";
                    if (c.Length > 0) AddComment(comment, c);
                    continue;
                }

                if (cur == null)
                {
                    cur = new ExplainStep { FromLine = i + 1, ToLine = i + 1 };
                    if (comment.Count > 0) { cur.Plain = Sentence(string.Join(" ", comment)); comment.Clear(); }
                }
                cur.ToLine = i + 1;
                curCode.Add(raw.TrimEnd());

                // A comment at the end of the line ("local SPEED = 50 -- how fast it goes").
                string codePart = t;
                var tail = Regex.Match(t, lua ? @"^(.*?[^-])\s--\s*(.+)$" : @"^(.*?)\s//\s*(.+)$");
                if (tail.Success && !InString(tail.Groups[1].Value)) { codePart = tail.Groups[1].Value.Trim(); }
                string? rule = lua ? LuaLine(codePart) : CppLine(codePart);
                if (tail.Success && !InString(tail.Groups[1].Value) && rule != null && Regex.IsMatch(rule, @"^A setting (?:you can change|that never changes)"))
                    rule = rule + " " + Sentence(Regex.Replace(tail.Groups[2].Value, @"^(?:true|false|\d+)\s*=\s*", m => m.Value.Trim().TrimEnd('=').Trim() + " means "));
                else if (tail.Success && !InString(tail.Groups[1].Value) && rule == null)
                    rule = Sentence(tail.Groups[2].Value);
                if (rule != null) curRules.Add(rule);

                // Long parts are split so each step stays short.
                if (curCode.Count >= 8) Close();
            }
            Close();
            return steps;
        }

        /// <summary>Adds a comment line; a new sentence starts when the line before didn't end one.</summary>
        private static void AddComment(List<string> comment, string c)
        {
            if (comment.Count > 0 && char.IsUpper(c[0]) && !Regex.IsMatch(comment[^1], @"[.!?:,;]$"))
                comment[^1] += ".";
            comment.Add(c);
        }

        /// <summary>The step that holds a line number, for "what does line 12 do".</summary>
        public static ExplainStep? StepFor(List<ExplainStep> steps, int line) =>
            steps.FirstOrDefault(s => line >= s.FromLine && line <= s.ToLine)
            ?? steps.LastOrDefault(s => s.FromLine <= line);

        private static int SkipHeader(string[] lines, bool lua)
        {
            int i = 0;
            while (i < lines.Length && lines[i].Trim().Length == 0) i++;
            if (i >= lines.Length) return i;
            string t = lines[i].Trim();
            if (lua ? t.StartsWith("--[[") : t.StartsWith("/*"))
            {
                string end = lua ? "]]" : "*/";
                for (; i < lines.Length; i++) if (lines[i].Contains(end)) return i + 1;
                return lines.Length;
            }
            // A run of // or -- lines at the very top is a header too, when it's followed by a blank line.
            int start = i;
            while (i < lines.Length && (lua ? lines[i].Trim().StartsWith("--") : lines[i].Trim().StartsWith("//"))) i++;
            if (i - start >= 3 && i < lines.Length && lines[i].Trim().Length == 0) return i;
            return start;
        }

        private static bool InString(string before) => before.Count(c => c == '"') % 2 == 1 || before.Count(c => c == '\'') % 2 == 1;

        private static string Sentence(string s)
        {
            s = Regex.Replace(s.Trim(), @"\s+", " ");
            if (s.Length == 0) return s;
            if (s.Length > 260) s = s[..257].TrimEnd() + "…";
            s = char.ToUpperInvariant(s[0]) + s[1..];
            if (!Regex.IsMatch(s, @"[.!?…:]$")) s += ".";
            return s;
        }

        private static string Name(string s) => s.Length > 40 ? s[..37] + "…" : s;

        // ========================================================================= Lua

        private static string? LuaLine(string t)
        {
            Match m;
            if (Regex.IsMatch(t, @"^end\)?\s*;?$|^\}\)?$|^\)$|^else$")) return t == "else" ? "Otherwise (if the check above was false), it does this instead." : null;

            if ((m = Regex.Match(t, @"^local (\w+)\s*=\s*script\.Parent$")).Success) return $"Grabs the thing this script is inside and names it {m.Groups[1].Value}.";
            if ((m = Regex.Match(t, @"^local (\w+)\s*=\s*script\.Parent\.Parent$")).Success) return $"Grabs the thing two levels up from this script and names it {m.Groups[1].Value}.";
            if ((m = Regex.Match(t, @"^local (\w+)\s*=\s*game:GetService\(""(\w+)""\)")).Success)
                return $"Gets the {m.Groups[2].Value} service" + (LuaServices.TryGetValue(m.Groups[2].Value, out var sv) ? $", which handles {sv}." : ".");
            if ((m = Regex.Match(t, @"^local (\w+)\s*=\s*require\((.+)\)$")).Success) return $"Loads the ModuleScript {Name(m.Groups[2].Value)} so this script can use its functions.";
            if ((m = Regex.Match(t, @"^(?:local )?(\w+)\s*=\s*(?:Players|game\.Players)\.LocalPlayer$")).Success) return "Gets the player on this computer (this only works in a LocalScript).";
            if ((m = Regex.Match(t, @"^local (\w+)\s*=\s*.*:WaitForChild\(""([^""]+)""")).Success) return $"Waits until {m.Groups[2].Value} exists, then grabs it as {m.Groups[1].Value}.";
            if ((m = Regex.Match(t, @"^local (\w+)\s*=\s*.*:FindFirstChildOfClass\(""Humanoid""\)")).Success) return "Looks for the Humanoid, the part of a character that holds its health and walk speed.";
            if ((m = Regex.Match(t, @"^local (\w+)\s*=\s*.*:FindFirstAncestorOfClass\(""Model""\)")).Success) return "Finds the character (the Model) that the touching body part belongs to.";
            if ((m = Regex.Match(t, @"GetPlayerFromCharacter\(")).Success) return "Checks which player owns that character. NPCs don't have one, so it's nil for them.";
            if ((m = Regex.Match(t, @"^local (\w+)\s*=\s*.*:GetDataStore\(""([^""]+)""")).Success) return $"Opens the saved-data store called \"{m.Groups[2].Value}\".";
            if ((m = Regex.Match(t, @"^local (\w+)\s*=\s*Instance\.new\(""(\w+)""")).Success)
                return $"Creates {(LuaNew.TryGetValue(m.Groups[2].Value, out var nw) ? nw : "a new " + m.Groups[2].Value)} and calls it {m.Groups[1].Value}.";
            if ((m = Regex.Match(t, @"^local (\w+)\s*=\s*TweenInfo\.new\(\s*([\d.]+)")).Success) return $"Says how the animation moves. It takes {m.Groups[2].Value} seconds.";
            if ((m = Regex.Match(t, @"^local (\w+)\s*=\s*\w+:Create\(")).Success) return "Sets up a smooth animation (a tween) but doesn't start it yet.";
            if ((m = Regex.Match(t, @"^local (\w*(?:debounce|cooldown|busy|touching|canUse|ready)\w*)\s*=\s*(true|false|\{\})", RegexOptions.IgnoreCase)).Success) return "A debounce: it stops this from firing lots of times from one touch or click.";
            if ((m = Regex.Match(t, @"^local ([A-Z][A-Z0-9_]+)\s*=\s*(.+)$")).Success) return $"A setting you can change: {m.Groups[1].Value} starts as {Name(m.Groups[2].Value)}.";
            if ((m = Regex.Match(t, @"^local function (\w+)\((.*)\)$")).Success || (m = Regex.Match(t, @"^function (\w[\w.:]*)\((.*)\)$")).Success)
            {
                string args = m.Groups[2].Value.Trim();
                return $"Makes a function called {m.Groups[1].Value}. The code inside only runs when something calls it" + (args.Length > 0 && args != "..." ? $", and it gets {Name(args)}." : ".");
            }
            if ((m = Regex.Match(t, @"^(?:local \w+\s*=\s*)?([\w.:\[\]""]+?)[.:](\w+):Connect\((\w+)\)$")).Success)
                return $"Whenever {Event(m.Groups[2].Value, m.Groups[1].Value)}, run {m.Groups[3].Value}.";
            if ((m = Regex.Match(t, @"([\w.:\[\]""]+?)[.:](\w+):Connect\(function")).Success)
                return $"Whenever {Event(m.Groups[2].Value, m.Groups[1].Value)}, run the code below.";
            if ((m = Regex.Match(t, @"^while true do$|^while task\.wait\(.*\) do$|^while wait\(.*\) do$")).Success) return "Repeats the code below forever.";
            if ((m = Regex.Match(t, @"^while (.+) do$")).Success) return $"Keeps repeating the code below while {Name(m.Groups[1].Value)} is true.";
            if ((m = Regex.Match(t, @"^repeat$")).Success) return "Repeats the code below until the check at the end is true.";
            if ((m = Regex.Match(t, @"^until (.+)$")).Success) return $"Stops repeating once {Name(m.Groups[1].Value)}.";
            if ((m = Regex.Match(t, @"^for (\w+)\s*=\s*([^,]+),\s*([^,]+?)(?:,\s*(.+))? do$")).Success) return $"Counts {m.Groups[1].Value} from {m.Groups[2].Value.Trim()} to {Name(m.Groups[3].Value.Trim())}, running the code below each time.";
            if ((m = Regex.Match(t, @"^for (.+) in (?:i?pairs\()?(.+?)\)?(?::GetChildren\(\)|:GetDescendants\(\)|:GetPlayers\(\))?\)? do$")).Success)
            {
                string what = t.Contains("GetPlayers") ? "every player" : t.Contains("GetDescendants") ? "everything inside " + Name(Regex.Replace(m.Groups[2].Value, @":Get\w+\(\)?\)?$", "")) : t.Contains("GetChildren") ? "everything inside " + Name(Regex.Replace(m.Groups[2].Value, @":Get\w+\(\)?\)?$", "")) : "every item in " + Name(m.Groups[2].Value);
                return $"Goes through {what}, one at a time.";
            }
            if ((m = Regex.Match(t, @"^if not (\w+) then return end$")).Success) return $"If there's no {m.Groups[1].Value}, it stops right here.";
            if ((m = Regex.Match(t, @"^if (.+) then return end$")).Success) return $"If {Name(m.Groups[1].Value)}, it stops right here.";
            if ((m = Regex.Match(t, @"^if not (\w+) then$")).Success) return $"Checks if {m.Groups[1].Value} is missing. The code below only runs if it is.";
            if ((m = Regex.Match(t, @"^if (.+) then$")).Success) return $"Checks if {Name(m.Groups[1].Value)}. The code below only runs when that's true.";
            if ((m = Regex.Match(t, @"^elseif (.+) then$")).Success) return $"Otherwise, checks if {Name(m.Groups[1].Value)}.";
            if (t == "return") return "Stops here and leaves the function.";
            if ((m = Regex.Match(t, @"^return (\w+)$")).Success && char.IsUpper(m.Groups[1].Value[0])) return $"Hands the {m.Groups[1].Value} table to any script that requires this module.";
            if (Regex.IsMatch(t, @"\.Health\s*=\s*0\b")) return "Sets health to 0, which knocks the character out.";
            if (Regex.IsMatch(t, @":TakeDamage\(")) return "Takes away health (a ForceField protects from this).";
            if (Regex.IsMatch(t, @"\.Health\s*=")) return "Changes the character's health.";
            if (Regex.IsMatch(t, @"\.WalkSpeed\s*=")) return "Changes how fast the character walks (16 is normal).";
            if (Regex.IsMatch(t, @"\.Jump(?:Power|Height)\s*=")) return "Changes how high the character can jump.";
            if (Regex.IsMatch(t, @"\.Name\s*=\s*""leaderstats""")) return "Names the folder leaderstats. Roblox shows everything inside it on the leaderboard.";
            if ((m = Regex.Match(t, @"\.Name\s*=\s*""([^""]+)""")).Success) return $"Names it \"{m.Groups[1].Value}\".";
            if ((m = Regex.Match(t, @"\.Value\s*=\s*.*\.Value\s*\+\s*(.+)$")).Success) return $"Adds {Name(m.Groups[1].Value)} to the value.";
            if ((m = Regex.Match(t, @"\.Value\s*=\s*.*\.Value\s*-\s*(.+)$")).Success) return $"Takes {Name(m.Groups[1].Value)} away from the value.";
            if ((m = Regex.Match(t, @"\.Value\s*\+=\s*(.+)$")).Success) return $"Adds {Name(m.Groups[1].Value)} to the value.";
            if ((m = Regex.Match(t, @"\.Value\s*-=\s*(.+)$")).Success) return $"Takes {Name(m.Groups[1].Value)} away from the value.";
            if ((m = Regex.Match(t, @"\.Value\s*=\s*(.+)$")).Success) return $"Sets the value to {Name(m.Groups[1].Value)}.";
            if ((m = Regex.Match(t, @"\.Parent\s*=\s*(.+)$")).Success) return m.Groups[1].Value.Trim() == "nil" ? "Takes it out of the game." : $"Puts it inside {Name(m.Groups[1].Value.Trim())} so it shows up in the game.";
            if (Regex.IsMatch(t, @"(?:task\.)?wait\(\s*\)")) return "Waits a tiny moment (one frame).";
            if ((m = Regex.Match(t, @"^(?:task\.)?wait\(\s*([\w.]+)\s*\)$")).Success) return $"Waits {m.Groups[1].Value} seconds before going on.";
            if ((m = Regex.Match(t, @"^task\.delay\(\s*([\w.]+)")).Success) return $"Runs the code after {m.Groups[1].Value} seconds, without making the rest of the script wait.";
            if (Regex.IsMatch(t, @"^task\.spawn\(|^coroutine\.wrap\(")) return "Runs the code on its own, so the rest of the script doesn't have to wait for it.";
            if (Regex.IsMatch(t, @"\bpcall\(")) return "Runs it safely: if it fails (like Roblox's servers being busy), the script keeps going instead of breaking.";
            if (Regex.IsMatch(t, @":SetAsync\(|:UpdateAsync\(")) return "Saves the data on Roblox's servers.";
            if (Regex.IsMatch(t, @":GetAsync\(")) return "Loads the saved data.";
            if (Regex.IsMatch(t, @":Play\(\)")) return "Starts it playing.";
            if (Regex.IsMatch(t, @":Stop\(\)")) return "Stops it.";
            if (Regex.IsMatch(t, @":Clone\(\)")) return "Makes a copy.";
            if (Regex.IsMatch(t, @":Destroy\(\)")) return "Deletes it from the game.";
            if (Regex.IsMatch(t, @"Debris:AddItem\(")) return "Deletes it automatically after a few seconds.";
            if (Regex.IsMatch(t, @":FireServer\(")) return "Sends a message from this player's game to the server.";
            if (Regex.IsMatch(t, @":FireAllClients\(")) return "Sends a message from the server to every player.";
            if (Regex.IsMatch(t, @":FireClient\(")) return "Sends a message from the server to one player.";
            if (Regex.IsMatch(t, @":MoveTo\(")) return "Tells the character to walk to that spot.";
            if (Regex.IsMatch(t, @":PivotTo\(|:SetPrimaryPartCFrame\(")) return "Moves the whole model to a new spot.";
            if (Regex.IsMatch(t, @":ChangeState\(.*Jumping")) return "Makes the character jump.";
            if (Regex.IsMatch(t, @"\.CFrame\s*=")) return "Moves or turns it using a CFrame (a position plus a rotation).";
            if (Regex.IsMatch(t, @"\.Position\s*=")) return "Moves it to a new position.";
            if (Regex.IsMatch(t, @"AssemblyLinearVelocity\s*=|\.Velocity\s*=")) return "Gives it a push: a speed in a direction.";
            if (Regex.IsMatch(t, @"\.Anchored\s*=\s*true")) return "Anchors it so it stays put and doesn't fall.";
            if (Regex.IsMatch(t, @"\.Anchored\s*=\s*false")) return "Unanchors it so physics can move it.";
            if (Regex.IsMatch(t, @"\.CanCollide\s*=\s*false")) return "Lets players walk through it.";
            if (Regex.IsMatch(t, @"\.CanCollide\s*=\s*true")) return "Makes it solid again.";
            if (Regex.IsMatch(t, @"\.Transparency\s*=")) return "Changes how see-through it is (0 is solid, 1 is invisible).";
            if (Regex.IsMatch(t, @"\.Color\s*=\s*Color3")) return "Changes its color (red, green and blue from 0 to 255).";
            if (Regex.IsMatch(t, @"Enum\.Material\.Neon")) return "Makes it glow with the Neon material.";
            if (Regex.IsMatch(t, @"\.Material\s*=")) return "Changes what it's made of (its material).";
            if (Regex.IsMatch(t, @"\.ClockTime\s*=")) return "Changes the time of day (0 to 24, like a clock).";
            if (Regex.IsMatch(t, @"\.Text\s*=")) return "Changes the text it shows.";
            if (Regex.IsMatch(t, @"\.Team\s*=|\.TeamColor")) return "Checks or changes the player's team.";
            if (Regex.IsMatch(t, @"\.Size\s*=")) return "Changes its size.";
            if (Regex.IsMatch(t, @"^print\(|^warn\(")) return "Writes a message in the Output window, which is handy for testing.";
            if (Regex.IsMatch(t, @"math\.random\(")) return "Picks a random number.";
            if (Regex.IsMatch(t, @"os\.clock\(\)|tick\(\)|os\.time\(\)|workspace:GetServerTimeNow\(\)")) return "Reads the clock, so it can tell how much time has passed.";
            if (Regex.IsMatch(t, @"^table\.insert\(")) return "Adds an item to the list.";
            if (Regex.IsMatch(t, @"^table\.remove\(")) return "Removes an item from the list.";
            if (Regex.IsMatch(t, @":BindAction\(")) return "Connects a key (and a phone button) to an action.";
            if (Regex.IsMatch(t, @":LoadAnimation\(")) return "Loads an animation so it can be played.";
            if ((m = Regex.Match(t, @"^local (\w+)\s*=\s*(true|false)$")).Success) return $"Makes a true/false switch called {m.Groups[1].Value}, starting as {m.Groups[2].Value}.";
            if ((m = Regex.Match(t, @"^local (\w+)\s*=\s*(-?[\d.]+)$")).Success) return $"Makes a number called {m.Groups[1].Value}, starting at {m.Groups[2].Value}.";
            if ((m = Regex.Match(t, @"^local (\w+)\s*=\s*\{\s*\}$")).Success) return $"Makes an empty table (a list) called {m.Groups[1].Value}.";
            if ((m = Regex.Match(t, @"^local (\w+)\s*=\s*\{")).Success) return $"Makes a table (a list of things) called {m.Groups[1].Value}.";
            if ((m = Regex.Match(t, @"^local (\w+)\s*=\s*(.+)$")).Success) return $"Saves {Name(m.Groups[2].Value)} in a variable called {m.Groups[1].Value}.";
            return null;
        }

        private static string Event(string evt, string who)
        {
            who = Regex.Replace(who, @"^(?:game\.|game:GetService\(""\w+""\)\.?)", "");
            if (who.Length == 0 || who.Length > 30) who = "it";
            return LuaEvents.TryGetValue(evt, out var what)
                ? (what.Contains(" it") || what.StartsWith("a value") ? what.Replace(" it", " " + who) : what)
                : $"the {evt} event happens on {who}";
        }

        // ========================================================================= C++ (Arduino, console, Unreal)

        private static string? CppLine(string t)
        {
            Match m;
            if (Regex.IsMatch(t, @"^\}\s*;?$|^\};?$|^\{$|^public:$|^private:$|^protected:$")) return null;
            if (Regex.IsMatch(t, @"^\}?\s*else\s*\{?$")) return "Otherwise (if the check above was false), it does this instead.";

            if ((m = Regex.Match(t, @"^#include\s*<([\w./]+)>")).Success)
                return CppIncludes.TryGetValue(m.Groups[1].Value, out var inc) ? $"Adds the {m.Groups[1].Value} library, for {inc}." : $"Adds the {m.Groups[1].Value} library so we can use its tools.";
            if ((m = Regex.Match(t, @"^#include\s*""([\w./]+)""")).Success)
                return m.Groups[1].Value.EndsWith(".generated.h") ? "Unreal makes this file by itself. It has to be the last include." : $"Brings in {m.Groups[1].Value}, which this code needs.";
            if ((m = Regex.Match(t, @"^#define (\w+)\s+(.+)$")).Success) return $"Gives the name {m.Groups[1].Value} to {Name(m.Groups[2].Value)}.";
            if (t.StartsWith("#pragma once")) return "Makes sure this file is only read once.";
            if (Regex.IsMatch(t, @"^using namespace std;")) return "Lets us write cout instead of std::cout.";
            if ((m = Regex.Match(t, @"^(?:const |constexpr |static const )+\w+ (\w*(?:PIN|Pin|pin)\w*)\s*=\s*(\w+)")).Success) return $"Says which pin {m.Groups[1].Value} uses: pin {m.Groups[2].Value}.";
            if ((m = Regex.Match(t, @"^(?:const |constexpr |static const )+[\w:<>]+ ([A-Z][A-Z0-9_]+|k[A-Z]\w*)\s*=\s*(.+?);")).Success) return $"A setting that never changes: {m.Groups[1].Value} is {Name(m.Groups[2].Value)}.";
            if ((m = Regex.Match(t, @"^(?:const |constexpr |static const )+[\w:<>]+&? (\w+)\s*=\s*(.+?);")).Success) return $"Works out {m.Groups[1].Value} = {Name(m.Groups[2].Value)}.";
            if (Regex.IsMatch(t, @"^void setup\(\)")) return "setup() runs once, when the Arduino turns on or resets.";
            if (Regex.IsMatch(t, @"^void loop\(\)")) return "loop() runs over and over, forever, after setup.";
            if (Regex.IsMatch(t, @"^int main\(")) return "main() is where the program starts.";
            if ((m = Regex.Match(t, @"pinMode\(\s*(\w+),\s*(OUTPUT|INPUT_PULLUP|INPUT)\s*\)")).Success)
                return m.Groups[2].Value switch
                {
                    "OUTPUT" => $"Sets {m.Groups[1].Value} as an output, so it can send power out.",
                    "INPUT_PULLUP" => $"Sets {m.Groups[1].Value} as an input for a button. It reads HIGH until the button is pressed, then LOW.",
                    _ => $"Sets {m.Groups[1].Value} as an input, so it can read a signal.",
                };
            if ((m = Regex.Match(t, @"digitalWrite\(\s*(\w+),\s*HIGH\s*\)")).Success) return $"Turns {m.Groups[1].Value} on (5 volts).";
            if ((m = Regex.Match(t, @"digitalWrite\(\s*(\w+),\s*LOW\s*\)")).Success) return $"Turns {m.Groups[1].Value} off.";
            if ((m = Regex.Match(t, @"digitalWrite\(\s*(\w+),")).Success) return $"Turns {m.Groups[1].Value} on or off.";
            if ((m = Regex.Match(t, @"digitalRead\(\s*(\w+)\s*\)")).Success) return $"Reads whether {m.Groups[1].Value} is on (HIGH) or off (LOW).";
            if ((m = Regex.Match(t, @"analogRead\(\s*(\w+)\s*\)")).Success) return $"Reads a number from 0 to 1023 on {m.Groups[1].Value}, like how far a knob is turned.";
            if ((m = Regex.Match(t, @"analogWrite\(\s*(\w+),")).Success) return $"Sets the brightness or speed on {m.Groups[1].Value}, from 0 to 255.";
            if ((m = Regex.Match(t, @"^delay\(\s*(\d+)\s*\)")).Success) return $"Waits {m.Groups[1].Value} milliseconds ({FormatSeconds(int.Parse(m.Groups[1].Value))}).";
            if ((m = Regex.Match(t, @"^delay\(\s*(\w+)\s*\)")).Success) return $"Waits {m.Groups[1].Value} milliseconds.";
            if ((m = Regex.Match(t, @"Serial\.begin\(\s*(\d+)\s*\)")).Success) return $"Starts talking to the computer over USB at {m.Groups[1].Value} speed. Open the Serial Monitor to see it.";
            if (Regex.IsMatch(t, @"Serial\.print")) return "Sends text to the Serial Monitor.";
            if (Regex.IsMatch(t, @"\bnoTone\(")) return "Stops the buzzer.";
            if (Regex.IsMatch(t, @"\btone\(")) return "Plays a note on the buzzer.";
            if (Regex.IsMatch(t, @"\bmap\(")) return "Changes a number from one range to another (like 0–1023 into 0–180).";
            if (Regex.IsMatch(t, @"\bconstrain\(")) return "Keeps the number between a lowest and highest value.";
            if (Regex.IsMatch(t, @"\bmillis\(\)")) return "Reads how many milliseconds have passed since the Arduino started, so it can time things without freezing.";
            if (Regex.IsMatch(t, @"\bpulseIn\(")) return "Times how long the echo takes to come back.";
            if (Regex.IsMatch(t, @"delayMicroseconds\(")) return "Waits a tiny bit (microseconds are millionths of a second).";
            if ((m = Regex.Match(t, @"(\w+)\.attach\(")).Success) return $"Connects {m.Groups[1].Value} to its pin.";
            if ((m = Regex.Match(t, @"(\w+)\.write\(\s*(\w+)\s*\)")).Success) return $"Turns {m.Groups[1].Value} to {m.Groups[2].Value} degrees.";
            if ((m = Regex.Match(t, @"(\w+)\.begin\(\)")).Success) return $"Starts {m.Groups[1].Value} up.";
            if (Regex.IsMatch(t, @"\.setPixelColor\(")) return "Sets one LED's color.";
            if (Regex.IsMatch(t, @"\.show\(\)")) return "Sends the new colors to the LEDs.";
            if (Regex.IsMatch(t, @"\.readTemperature\(")) return "Reads the temperature from the sensor.";
            if (Regex.IsMatch(t, @"\.readHumidity\(")) return "Reads the humidity (how wet the air is).";
            if (Regex.IsMatch(t, @"\bisnan\(")) return "Checks if the reading failed (NaN means \"not a number\").";
            if (Regex.IsMatch(t, @"^srand\(")) return "Shuffles the random numbers so every game is different.";
            if ((m = Regex.Match(t, @"rand\(\)\s*%\s*(\d+)\s*\+\s*1")).Success) return $"Picks a random number from 1 to {m.Groups[1].Value}.";
            if ((m = Regex.Match(t, @"rand\(\)\s*%\s*(\w+)")).Success) return $"Picks a random number from 0 up to {m.Groups[1].Value} minus 1.";
            if (Regex.IsMatch(t, @"\brandom\((\w+)")) return "Picks a random number.";
            if (Regex.IsMatch(t, @"^(?:std::)?getline\(")) return "Waits for the player to type a whole line, then saves it.";
            if (Regex.IsMatch(t, @"^(?:std::)?cin\s*>>|\bcin\s*>>")) return "Waits for the player to type something, then saves it.";
            if (Regex.IsMatch(t, @"\bcin\.(?:clear|ignore)\(")) return "Cleans up after a wrong answer so typing works again.";
            if (Regex.IsMatch(t, @"^(?:std::)?cout\s*<<|\bcout\s*<<")) return "Prints text on the screen.";
            if (Regex.IsMatch(t, @"^return 0;")) return "Ends the program and says it worked fine.";
            if (Regex.IsMatch(t, @"^return;")) return "Stops here and leaves the function.";
            if ((m = Regex.Match(t, @"^return (.+);")).Success) return $"Gives back {Name(m.Groups[1].Value)} to whoever called this function.";
            if ((m = Regex.Match(t, @"^(?:class|struct) (\w+);$")).Success) return $"Tells the compiler that {m.Groups[1].Value} exists. Its full code is somewhere else.";
            if ((m = Regex.Match(t, @"^(?:class|struct) (?:\w+_API )?(\w+)")).Success) return $"Makes a blueprint called {m.Groups[1].Value}. It says what data and actions each {m.Groups[1].Value} has.";
            if (Regex.IsMatch(t, @"^UCLASS\(")) return "Tells Unreal this class can be used in the editor.";
            if (Regex.IsMatch(t, @"^GENERATED_BODY\(\)")) return "Unreal fills in hidden code here.";
            if (Regex.IsMatch(t, @"^UPROPERTY\(.*Edit")) return "Makes the next value a setting you can change in Unreal's Details panel.";
            if (Regex.IsMatch(t, @"^UPROPERTY\(")) return "Lets Unreal see the next value.";
            if (Regex.IsMatch(t, @"^UFUNCTION\(")) return "Lets Unreal (and Blueprints) call the next function.";
            if (Regex.IsMatch(t, @"PrimaryActorTick\.bCanEverTick\s*=\s*true")) return "Turns on Tick, so this actor can run code every frame.";
            if (Regex.IsMatch(t, @"::BeginPlay\(\)|^virtual void BeginPlay|^void BeginPlay")) return "BeginPlay runs once, when the game starts.";
            if (Regex.IsMatch(t, @"::Tick\(|^virtual void Tick|^void Tick")) return "Tick runs every frame. DeltaTime is how long the last frame took, so things move at the same speed on any PC.";
            if (Regex.IsMatch(t, @"Super::\w+\(")) return "Runs Unreal's own version of this function first.";
            if (Regex.IsMatch(t, @"CreateDefaultSubobject<")) return "Creates a part (a component) of this actor.";
            if (Regex.IsMatch(t, @"OnComponentBeginOverlap|OnActorBeginOverlap")) return "Runs a function when something walks into it.";
            if (Regex.IsMatch(t, @"AddActorLocalRotation|AddActorWorldRotation")) return "Turns the actor a little bit.";
            if (Regex.IsMatch(t, @"SetActorLocation|AddActorWorldOffset|AddActorLocalOffset")) return "Moves the actor.";
            if (Regex.IsMatch(t, @"GetActorLocation\(\)")) return "Reads where the actor is right now.";
            if (Regex.IsMatch(t, @"->Destroy\(\)|\bDestroy\(\);")) return "Removes it from the game.";
            if (Regex.IsMatch(t, @"UE_LOG\(|GEngine->AddOnScreenDebugMessage")) return "Writes a message you can see while testing.";
            if (Regex.IsMatch(t, @"FMath::Sin\(")) return "Uses a sine wave to move smoothly back and forth.";
            if (Regex.IsMatch(t, @"Cast<")) return "Checks what kind of thing it is (like a player character).";
            if ((m = Regex.Match(t, @"^for\s*\(\s*(?:int |size_t |auto )?(\w+)\s*=\s*([^;]+);\s*\w+\s*(<=?)\s*([^;]+);")).Success)
                return $"Counts {m.Groups[1].Value} from {m.Groups[2].Value.Trim()} up to {(m.Groups[3].Value == "<=" ? "" : "just under ")}{Name(m.Groups[4].Value.Trim())}, running the code below each time.";
            if ((m = Regex.Match(t, @"^for\s*\(\s*(?:const )?(?:auto|[\w:<>]+)&?\s+(\w+)\s*:\s*(\w+)\s*\)")).Success) return $"Goes through every item in {m.Groups[2].Value}, one at a time.";
            if (Regex.IsMatch(t, @"^for\s*\(")) return "Repeats the code below a set number of times.";
            if (Regex.IsMatch(t, @"^while\s*\(\s*(?:true|1)\s*\)")) return "Repeats the code below forever (until something breaks out).";
            if ((m = Regex.Match(t, @"^while\s*\((.+)\)\s*\{?$")).Success) return $"Keeps repeating the code below while {Name(m.Groups[1].Value.Trim())}.";
            if (Regex.IsMatch(t, @"^do\s*\{?$")) return "Runs the code below once, then keeps repeating it while the check at the end is true.";
            if ((m = Regex.Match(t, @"^\}?\s*else if\s*\((.+)\)\s*\{?$")).Success) return $"Otherwise, checks if {Name(m.Groups[1].Value.Trim())}.";
            if ((m = Regex.Match(t, @"^if\s*\((.+)\)\s*\{?$")).Success) return $"Checks if {Name(m.Groups[1].Value.Trim())}. The code below only runs when that's true.";
            if ((m = Regex.Match(t, @"^if\s*\((.+?)\)\s*(.+);$")).Success) return $"If {Name(m.Groups[1].Value.Trim())}, it does: {Name(m.Groups[2].Value.Trim())}.";
            if ((m = Regex.Match(t, @"^switch\s*\((.+)\)")).Success) return $"Picks what to do based on {m.Groups[1].Value.Trim()}.";
            if ((m = Regex.Match(t, @"^case (.+):")).Success) return $"What to do when it's {m.Groups[1].Value.Trim()}.";
            if (Regex.IsMatch(t, @"^default:")) return "What to do for anything else.";
            if (Regex.IsMatch(t, @"^break;")) return "Jumps out of the loop or switch.";
            if (Regex.IsMatch(t, @"^continue;")) return "Skips to the next time around the loop.";
            if ((m = Regex.Match(t, @"^(?:std::)?vector<(.+)>\s+(\w+)")).Success) return $"Makes a list called {m.Groups[2].Value} that can grow.";
            if ((m = Regex.Match(t, @"\.push_back\(")).Success) return "Adds an item to the end of the list.";
            if ((m = Regex.Match(t, @"\.erase\(")).Success) return "Removes an item from the list.";
            if ((m = Regex.Match(t, @"^(?:static |inline |virtual )*(?:void|int|bool|char|float|double|long|unsigned \w+|(?:std::)?string|auto|[A-Z]\w*)\*?\s+([\w:]+)\s*\(([^;]*)\)\s*(?:const\s*)?(?:override\s*)?\{?$")).Success
                && !Regex.IsMatch(m.Groups[1].Value, @"^(if|for|while|switch|return)$"))
            {
                string name = m.Groups[1].Value.Contains("::") ? m.Groups[1].Value.Split("::")[^1] : m.Groups[1].Value;
                return $"Makes a function called {name}. The code inside runs whenever something calls {name}().";
            }
            if ((m = Regex.Match(t, @"^(?:int|long|float|double|bool|char|unsigned \w+|(?:std::)?string|auto)\s+(\w+)\s*=\s*(.+);$")).Success) return $"Makes a variable called {m.Groups[1].Value}, starting as {Name(m.Groups[2].Value)}.";
            if ((m = Regex.Match(t, @"^(?:int|long|float|double|bool|char|unsigned \w+|(?:std::)?string)\s+(\w+(?:\s*,\s*\w+)*);$")).Success) return $"Makes a variable called {m.Groups[1].Value} to fill in later.";
            if ((m = Regex.Match(t, @"^(\w+)\s*\+\+;|^\+\+(\w+);")).Success) return $"Adds 1 to {(m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : m.Groups[2].Value)}.";
            if ((m = Regex.Match(t, @"^(\w+)\s*--;|^--(\w+);")).Success) return $"Takes 1 away from {(m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : m.Groups[2].Value)}.";
            if ((m = Regex.Match(t, @"^(\w+)\s*\+=\s*(.+);")).Success) return $"Adds {Name(m.Groups[2].Value)} to {m.Groups[1].Value}.";
            if ((m = Regex.Match(t, @"^(\w+)\s*-=\s*(.+);")).Success) return $"Takes {Name(m.Groups[2].Value)} away from {m.Groups[1].Value}.";
            return null;
        }

        private static string FormatSeconds(int ms) =>
            ms == 1000 ? "1 second" : ms % 1000 == 0 ? $"{ms / 1000} seconds" : ms < 1000 ? $"{ms / 1000.0:0.###} of a second" : $"{ms / 1000.0:0.##} seconds";
    }
}
