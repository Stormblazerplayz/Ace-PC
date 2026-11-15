using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Nimbus.AceV4.Brain
{
    /// <summary>
    /// Turns other ways of saying a command into the way Ace's rules know it:
    /// "fire up minecraft" → "open minecraft", "ping me in an hour" → "remind me in an hour",
    /// "toss dentist on my calendar" → "add dentist to my calendar", "shut discord" → "close discord".
    /// Only whole commands are changed, never questions about facts.
    /// </summary>
    public static class Rephrase
    {
        private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;
        private const string CalendarThing = @"(?:meeting|appointment|event|practice|class|party|lesson|game|session|sleepover|playdate|dentist|doctor|checkup|recital|rehearsal|tutoring|camp|trip|movie night|hangout)";

        private static readonly (Regex From, string To)[] Table =
        {
            // Small talk at the start: "now close it", "ok so open spotify", "um what time is it"
            (new Regex(@"^(?:now|ok(?:ay)?|so|um+|uh+|alright|right|then|and then|also)[,\s]+(?=\S)", Opt), ""),
            // "can you tell me..." / "could you explain..." (not "can you hear me", "can you sing")
            (new Regex(@"^(?:can|could|would|will)\s+you\s+(?:please\s+)?(?!hear|see|feel|think|like|love|be\b|rather|marry|speak|talk|read|count|dance|sing|play|swim|cook|eat|sleep|dream|learn\b|understand|remember\b)(?=[a-z])", Opt), ""),
            (new Regex(@"^(?:i\s+(?:want|need|would like|'d like)\s+(?:you\s+)?to|please)\s+", Opt), ""),
            // "for me" at the end of a command
            (new Regex(@"^((?:open|close|launch|start|set|add|find|search|take|grab|make|put|play|turn|check|look up)\b.*?)\s+for me(?: please)?[.!?]*$", Opt), "$1"),
            // Opening apps and sites
            (new Regex(@"^(?:fire|boot|power|spin|crank|load|start)\s+up\s+(?!the (?:pc|computer))(.+)$", Opt), "open $1"),
            (new Regex(@"^(?:take|bring|get|send)\s+me\s+(?:over\s+|back\s+)?to\s+(.+)$", Opt), "go to $1"),
            (new Regex(@"^(?:pop|throw|put)\s+(?:open|up)\s+(.+)$", Opt), "open $1"),
            // Closing: "shut discord" (not "shut down", "shut up", "shut off")
            (new Regex(@"^shut\s+(?!down\b|off\b|up\b|it down|everything|the (?:pc|computer|laptop))(.+)$", Opt), "close $1"),
            (new Regex(@"^(?:get rid of|ditch|nuke|end)\s+(?!.*\b" + CalendarThing + @"\b)(?:the\s+)?(\w+(?:\s\w+)?)\s+(?:app|window|program)$", Opt), "close $1"),
            // Reminders
            (new Regex(@"^(?:ping|nudge|buzz|poke)\s+me\s+(?=(?:in|at|on|tomorrow|tonight|later|when|after|to|about|every)\b)", Opt), "remind me "),
            (new Regex(@"^(?:don'?t let me forget|make sure i remember)\s+(?:to\s+)?(.+)$", Opt), "remind me to $1"),
            // Calendar
            (new Regex(@"^(?:toss|throw|stick|pop|chuck|slap|jot|put|pencil|write|plop|drop)\s+(.+?)\s+(?:on|in|into|onto)\s+(?:my|the)\s+(?:calendar|schedule|agenda)\b(.*)$", Opt), "add $1 to my calendar$2"),
            (new Regex(@"^(?:scrap|ditch|drop|nuke|axe|scratch|bin|call off|kill)\s+(.*\b" + CalendarThing + @"\b.*)$", Opt), "cancel $1"),
            (new Regex(@"^(?:what does|how does|how's|hows)\s+my\s+(day|schedule|calendar|week|agenda|weekend)\s+look(?:\s+like)?\b(.*)$", Opt), "what's on my calendar$2"),
            (new Regex(@"^(?:do i have|have i got)\s+anything\s+(on\s+)?(.+)$", Opt), "what do i have $2"),
            // Windows
            (new Regex(@"^make\s+(.+?)\s+(?:fill|take up|cover|use)\s+the\s+(?:whole\s+|entire\s+|full\s+)?screen$", Opt), "maximize $1"),
            (new Regex(@"^(?:blow up|enlarge|full ?screen)\s+(.+)$", Opt), "maximize $1"),
            (new Regex(@"^(?:throw|shove|chuck|drag|stick|toss|slide|push|send)\s+(.+?)\s+(?:onto|on|to|over to|into)\s+the\s+(left|right)(?:\s+side|\s+half)?(?:\s+of\s+(?:my|the)\s+(?:screen|monitor))?$", Opt), "snap $1 to the $2 half"),
            (new Regex(@"^(?:shrink|tuck away)\s+(.+)$", Opt), "minimize $1"),
            (new Regex(@"^(?:what|which)\s+(?:apps|programs|windows|stuff)\s+(?:do i have|have i got|i have)\s+(?:open|running)\b.*$", Opt), "what apps are open"),
            (new Regex(@"^(?:close|shut)\s+(this|the|that|my)\s+(?:browser|chrome|edge|firefox)\s+tab$", Opt), "close $1 tab"),
            // Sleep
            (new Regex(@"\b(?:head|get|go|hop)\s+(?:off\s+)?to\s+bed\b|\bhit the (?:hay|sack|pillow)\b|\bgo to sleep\b", Opt), "go to bed"),
            // Weather
            (new Regex(@"^(?:what'?s|what is|how'?s|how is)\s+it\s+(?:like\s+)?(?:outside|out there|out)\b(.*)$", Opt), "what's the weather$1"),
            (new Regex(@"^(?:is|will)\s+it\s+(?:going to|gonna)?\s*be\s+(?:cold|hot|warm|sunny|rainy|windy|cloudy|stormy|nice|chilly|freezing)\b(.*)$", Opt), "what's the weather$1"),
            (new Regex(@"^(?:do i need|should i (?:bring|take|wear))\s+(?:a|an|my)?\s*(?:jacket|coat|umbrella|sunscreen|hat|sweater|hoodie)\b(.*)$", Opt), "what's the weather$1"),
            // Storage
            (new Regex(@"\b(?:running|run|am i)\s+(?:out|low)\s+(?:of|on)\s+(?:disk |drive |hard drive |ssd )?(?:space|storage|room)\b.*$", Opt), "how much storage do i have"),
            (new Regex(@"^(?:is|how)\s+(?:my|the)\s+(?:disk|drive|ssd|hard drive|storage)\s+(?:almost\s+)?full\b.*$", Opt), "how much storage do i have"),
            // Screenshots
            (new Regex(@"^(?:snap|grab|get|capture)\s+(?:a\s+)?(?:pic|picture|photo|shot|image)\s+of\s+(?:my|the)\s+screen$", Opt), "take a screenshot"),
            // Volume
            (new Regex(@"^(?:make it|make the sound|make the music)\s+(louder|quieter|softer)$", Opt), "$1"),
            (new Regex(@"^(?:crank|pump)\s+(?:it|the volume|the music)\s+up$", Opt), "volume up"),
        };

        /// <summary>Returns the rewritten request (or the same text).</summary>
        public static string Apply(string text)
        {
            string t = text.Trim();
            for (int pass = 0; pass < 2; pass++)
            {
                string before = t;
                foreach (var (from, to) in Table) t = from.Replace(t, to).Trim();
                if (t == before) break;
            }
            return t;
        }
    }
}
