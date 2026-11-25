using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Nimbus.AceV4.Pc;

namespace Nimbus.AceV4
{
    /// <summary>
    /// Ace's global shortcuts by voice or typing: "what's the shortcut to open you?", "change my shortcut to
    /// Ctrl+Shift+A", "set the talk shortcut to Ctrl+Alt+T", "turn off the talk hotkey", "reset my shortcuts".
    /// </summary>
    public sealed partial class AceV4Brain
    {
        /// <summary>Matches questions and changes about Ace's own shortcuts (not "make a desktop shortcut to Minecraft").</summary>
        internal const string ShortcutPattern =
            @"^(?!.*\b(?:desktop|start menu|taskbar|folder|file)\b)(?:.*\b(?:hot ?keys?|keyboard shortcuts?|shortcut keys?|global shortcuts?|key ?(?:combo|combination)s?)\b" +
            @"|.*\bshortcuts?\b.*\b(?:open (?:you|ace)|show (?:you|ace)|bring (?:you|ace) up|bring up (?:you|ace)|talk(?:ing)? to (?:you|ace)|for (?:you|ace)|to talk)\b" +
            @"|.*\b(?:ace'?s|your|ace) (?:open |talk |opening |talking )?shortcuts?\b" +
            @"|(?:what'?s|whats|what is|what are|show me|tell me) (?:my|the|your) (?:open |talk )?shortcuts?\??$" +
            @"|(?:change|set|reset|turn off|turn on|disable|enable|remove) (?:my|the|your|both) (?:open |talk |opening |talking )?shortcuts?\b).*$";

        private Task<AceAnswer?> ShortcutAsync(Turn t)
        {
            var rt = AceRuntime.Current;
            bool talk = t.Has(@"\b(?:talk|talking|listen|listening|voice|mic|speak)\b");
            bool both = !talk && t.Has(@"\b(?:both|all|shortcuts|hot ?keys|key ?combos)\b");
            var action = talk ? HotkeyAction.Talk : HotkeyAction.ShowAce;
            string defaultCombo = talk ? AceV4Config.DefaultTalkHotkey : AceV4Config.DefaultShowHotkey;

            // Turn off.
            if (t.Has(@"^(?:please )?(?:turn off|switch off|disable|remove|delete|get rid of|stop|clear)\b"))
            {
                if (both)
                {
                    rt.SetHotkey(HotkeyAction.ShowAce, "");
                    rt.SetHotkey(HotkeyAction.Talk, "");
                    Step("Turned off both shortcuts");
                    return ShortcutSay("Okay, both shortcuts are off. Turn them back on in Settings > Shortcuts, or say \"reset my shortcuts\".");
                }
                var (offOk, offMsg) = rt.SetHotkey(action, "");
                Step("Turned off the " + GlobalHotkeys.Describe(action) + " shortcut");
                return ShortcutSay(offOk ? $"Okay, the shortcut to {GlobalHotkeys.Describe(action)} is off. Say \"reset the {(talk ? "talk " : "")}shortcut\" to bring back {defaultCombo}." : offMsg);
            }

            // Back to the default.
            if (t.Has(@"\b(?:reset|default|defaults|back to (?:normal|the default|how it was))\b") || t.Has(@"^(?:turn on|switch on|enable)\b"))
            {
                if (both)
                {
                    var (a, am) = rt.SetHotkey(HotkeyAction.ShowAce, AceV4Config.DefaultShowHotkey);
                    var (b, bm) = rt.SetHotkey(HotkeyAction.Talk, AceV4Config.DefaultTalkHotkey);
                    Step("Reset both shortcuts");
                    if (a && b) return ShortcutSay($"Done! {AceV4Config.DefaultShowHotkey} opens me and {AceV4Config.DefaultTalkHotkey} starts talking, from any app.");
                    return ShortcutSay(string.Join(" ", a ? "" : am, b ? "" : bm).Trim());
                }
                var (ok, msg) = rt.SetHotkey(action, defaultCombo);
                Step("Set the " + GlobalHotkeys.Describe(action) + " shortcut to " + defaultCombo);
                return ShortcutSay(msg);
            }

            // Change it: "change my shortcut to ctrl shift a", "set the talk hotkey as alt+t".
            var m = t.Match(@"^.*\b(?:to|as|into)\s+(?:be\s+)?[""']?(?<combo>[a-z0-9 +\-=,./;'`\[\]\\]+?)[""'.!?]*$");
            if (m.Success && t.Has(@"^(?:please |can you |could you |will you )?(?:change|set|make|use|switch|swap|put|update|move)\b"))
            {
                string text = NormalizeComboWords(m.Groups["combo"].Value);
                if (!HotkeyCombo.TryParse(text, out var combo, out string problem) || combo.IsNone)
                    return ShortcutSay(problem.Length > 0 ? problem : "Tell me the keys, like \"change my shortcut to Ctrl+Shift+A\".");
                var (ok, msg) = rt.SetHotkey(action, combo.ToString());
                Step((ok ? "Set" : "Couldn't set") + " the " + GlobalHotkeys.Describe(action) + " shortcut to " + combo);
                return ShortcutSay(msg);
            }

            // Otherwise: what are my shortcuts?
            string show = rt.HotkeyText(HotkeyAction.ShowAce), talkKey = rt.HotkeyText(HotkeyAction.Talk);
            string Line(HotkeyAction a, string key) => key.Length == 0
                ? $"There's no shortcut to {GlobalHotkeys.Describe(a)} right now."
                : rt.Hotkeys[a].Working || !rt.Hotkeys.Attached
                    ? $"{key} {(a == HotkeyAction.ShowAce ? "opens me" : "starts talking to me")} from any app."
                    : $"{key} is set to {GlobalHotkeys.Describe(a)}, but another app already uses it, so it isn't working.";
            string answer = talk ? Line(HotkeyAction.Talk, talkKey)
                          : both ? Line(HotkeyAction.ShowAce, show) + " " + Line(HotkeyAction.Talk, talkKey)
                          : Line(HotkeyAction.ShowAce, show) + (talkKey.Length > 0 ? $" And {talkKey} starts talking." : "");
            return ShortcutSay(answer + " To change one, say \"change my shortcut to Ctrl+Shift+A\", or click the orb > Settings > Shortcuts.");
        }

        private static Task<AceAnswer?> ShortcutSay(string text) => Task.FromResult<AceAnswer?>(AceAnswer.Say(text, "hotkey"));

        /// <summary>"control shift a" / "ctrl plus alt plus t" / "alt and f2" become "control+shift+a", "ctrl+alt+t", "alt+f2".</summary>
        internal static string NormalizeComboWords(string words)
        {
            string s = words.Trim().ToLowerInvariant();
            s = Regex.Replace(s, @"\b(?:the|key|keys|button)\b", " ");
            s = Regex.Replace(s, @"\s*(?:\bplus\b|\band\b|\bthen\b|,|\+)\s*", "+");
            s = Regex.Replace(s, @"\bwindows key\b|\bwindows\b", "win");
            s = Regex.Replace(s, @"\bpage up\b", "pageup");
            s = Regex.Replace(s, @"\bpage down\b", "pagedown");
            s = Regex.Replace(s, @"\bspace ?bar\b", "space");
            s = Regex.Replace(s, @"\bprint screen\b", "printscreen");
            s = Regex.Replace(s, @"\bscroll lock\b", "scrolllock");
            s = Regex.Replace(s, @"\s+", " ").Trim(' ', '+');
            if (!s.Contains('+')) s = s.Replace(' ', '+');
            else s = string.Join("+", s.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            return s;
        }
    }
}
