using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Nimbus.AceV4.Brain;
using Nimbus.AceV4.Web;
using Nimbus.Services.Ace;

namespace Nimbus.AceV4
{
    public sealed class Research
    {
        public string Answer = "";
        public double Confidence;
        public List<WebResult> Sources = new();
        public string PageText = "";
        public string PageUrl = "";
        public int SentencesUsed;
    }

    /// <summary>The thinking loop: bank, planner, comparisons, web research, double-checking, learning and the optional helper.</summary>
    public sealed partial class AceV4Brain
    {
        /// <summary>A bank match this good is trusted as is.</summary>
        private double HighScore => Bank.UsesMeaning ? 0.80 : 0.72;
        /// <summary>A bank match this good is a decent draft that may get checked.</summary>
        private double MidScore => Bank.UsesMeaning ? 0.62 : 0.50;

        private static readonly string[] ChatTopics = { "smalltalk", "ace", "nimbus", "personal", "learned", "pc", "howto_life", "games_making" };

        private static bool TimeSensitive(string q) =>
            Regex.IsMatch(q, @"\b(today|tonight|now|right now|current(ly)?|latest|newest|recent(ly)?|this (week|month|year)|news|score|won|winning|price|cost|stock|released?|release date|update|patch|coming out|upcoming|20[2-3]\d|still|yet|anymore|live (score|scores|stream|now|results?|updates?)|trending|who is the (president|ceo|king|queen|prime minister)|how old is (?!(the )?(universe|earth|sun|moon|world|milky way|solar system|pyramids?|grand canyon|dinosaurs?|ocean|planet)\b))\b", RegexOptions.IgnoreCase)
            // History questions ("who won world war 2") aren't news.
            && !Regex.IsMatch(q, @"\b(war|ww ?[12]|battle|revolution|ancient|in (1\d{3}|[1-9]\d{0,2}) ?(bc|ad)?)\b", RegexOptions.IgnoreCase);

        private static bool LooksFactual(string q) =>
            Regex.IsMatch(q, @"^(who|what|when|where|which|why|how|is|are|was|were|does|do|did|can|could|will|should|tell me (about|what|who|why|how)|explain|describe|define|name)\b", RegexOptions.IgnoreCase)
            && !Regex.IsMatch(q, @"\b(you|your|yourself|ace)\b", RegexOptions.IgnoreCase)
            && !Regex.IsMatch(q, @"^(how are|what'?s up|how'?s it going|what are you)", RegexOptions.IgnoreCase);

        private AceAnswer BankAnswer(BankHit hit, double confidence)
        {
            Bank.MarkUsed(hit.Entry);
            Talk.LastEntry = hit.Entry;
            var a = AceAnswer.Say(Composer.Pick(hit.Entry.Id, CoreAnswers(hit.Entry)), hit.Entry.Learned != null ? "learned" : "bank", confidence);
            a.BankId = hit.Entry.Id;
            if (hit.Entry.Source is { Length: > 0 } src && hit.Entry.Learned != null && src.StartsWith("http"))
                a.Cards.Add(new InfoCard { Title = "Where I learned this", Body = hit.Entry.Questions[0], Source = HostOf(src), Url = src });
            return a;
        }

        /// <summary>
        /// The answers that actually answer the question. For facts, the extra answers are often side notes
        /// ("Baby teeth start falling out around age 6"), so only answers with the same key facts as the first count.
        /// Chat answers are all fine (they're just different ways of saying it).
        /// </summary>
        private static IReadOnlyList<string> CoreAnswers(QaEntry entry)
        {
            if (entry.Answers.Count <= 1 || ChatTopics.Contains(entry.Topic) || entry.Id.StartsWith("talk.", StringComparison.Ordinal) || entry.Id.StartsWith("ace.", StringComparison.Ordinal))
                return entry.Answers;
            var first = entry.Answers[0];
            var firstFacts = KeyFacts(first);
            // Same key facts both ways ("12 people walked on it" isn't "Neil Armstrong was first").
            var same = entry.Answers.Where(a => a == first || (SameFacts(a, first) && firstFacts.All(KeyFacts(a).Contains))).ToList();
            return same.Count > 0 ? same : new List<string> { first };
        }

        /// <summary>"Capital of the Moon", "population of Jupiter", "atomic number of France": things that don't have that.</summary>
        private AceAnswer? Misfit(string q)
        {
            var m = Regex.Match(q, @"\b(capital( city)?|population|currency|money|flag|national anthem|official language|president|prime minister|king|queen|atomic number|chemical symbol|atomic mass)\s+(?:of|in|for)\s+(.+)$", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            string what = m.Groups[1].Value.ToLowerInvariant();
            var thing = FindEntity(m.Groups[3].Value);
            if (thing == null) return null;
            string kind = thing.Value.Kind;
            string name = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(thing.Value.Name);
            if (name.StartsWith("The ", StringComparison.Ordinal)) name = "the " + name[4..];
            bool element = what is "atomic number" or "chemical symbol" or "atomic mass";
            if (element && kind == "element") return null;
            if (!element && kind is "country" or "state") return null;
            if (!element && kind is not ("space" or "element" or "month" or "day")) return null;
            string why = element ? "only elements on the periodic table have one" :
                kind == "space" ? "nobody lives there, so it isn't a country" :
                kind == "element" ? "it's a chemical element, not a country" : "it's a time, not a place";
            string text = Composer.Pick("misfit", new[]
            {
                "{Name} doesn't have a {what}, because {why}.",
                "Trick question! {Name} doesn't have a {what}. {Why}.",
                "There isn't one. {Name} doesn't have a {what}, since {why}.",
            }).Replace("{Name}", Composer.Capital(name)).Replace("{what}", what).Replace("{Why}", Composer.Capital(why)).Replace("{why}", why);
            return AceAnswer.Say(text, "chat", 0.9);
        }

        /// <summary>Questions about your own life that only you know ("who is my teacher"), unless you told Ace.</summary>
        private AceAnswer? PersonalUnknown(string q, BankHit? best)
        {
            string l = q.ToLowerInvariant();
            var mine = Regex.Match(l, @"^(?:who|what|when|where|which)(?:'s|s| is| are| was| were)\s+my\s+(.+)$");
            bool didI = Regex.IsMatch(l, @"^(?:what|who|when|where|which \w+)\s+(?:did|have)\s+i\s+(?!(?:need|have) to\b)\w+") || Regex.IsMatch(l, @"\b(?:am i|i'?m) thinking\b|\bwhat am i (?:doing|wearing|holding)\b");
            if (!mine.Success && !didI) return null;
            // A memory you gave me, or a real answer about "my ..." (like "what's my name"), wins.
            if (best != null && best.Entry.Learned != null && best.Score >= MidScore) return null;
            if (best != null && best.Score >= HighScore && Regex.IsMatch(" " + best.MatchedQuestion.ToLowerInvariant() + " ", @"\b(my|i|me)\b")) return null;
            if (mine.Success && Regex.IsMatch(mine.Groups[1].Value, @"^(name|ip|ip address|battery|storage|disk|computer|pc|wifi|internet|cpu|gpu|ram|screen|calendar|schedule|reminders?|timers?|notifications)\b")) return null;
            string hint = mine.Success ? "remember my " + Regex.Replace(mine.Groups[1].Value, @"\s+(is|are)$", "") + " is ..." : "remember ...";
            var a = AceAnswer.Say(Composer.Pick("personal.unknown", new[]
            {
                "I don't know that one yet. It's about you! Tell me by saying \"{hint}\" and I'll remember it.",
                "Only you know that! Say \"{hint}\" and I won't forget it.",
                "I don't know that about you yet. If you tell me (\"{hint}\"), I'll keep it in my memory.",
            }).Replace("{hint}", hint), "memory", 0.4);
            a.Mood = "confused";
            return a;
        }

        private static string HostOf(string url) { try { return new Uri(url).Host.Replace("www.", ""); } catch { return url; } }

        // ===================================================== Thinking

        private async Task<AceAnswer> ThinkAsync(Turn turn)
        {
            string q = turn.Text.Trim().TrimEnd('?', '.', '!').Trim();
            bool factual = LooksFactual(q);
            if (factual) NoteSubject(q);

            // Pass 1: what do I already know?
            _passes++;
            Step("Checking what I know", $"{Bank.Count:N0} answers");
            var hits = Bank.Search(q, 5);
            var best = hits.FirstOrDefault();
            if (best != null) Step("Best match", $"\"{Short(best.MatchedQuestion, 60)}\" · {best.Score:0.00}");

            // "What's the capital of the Moon?": a question that doesn't fit the thing it's about.
            var misfit = Misfit(q);
            if (misfit != null) { Step("That doesn't fit", misfit.Text); return misfit; }

            // "Who is my teacher?" / "What did I eat for breakfast?": only you know, unless you told me.
            var personal = PersonalUnknown(q, best);
            if (personal != null) { Step("That's about you, and you haven't told me yet"); return personal; }

            // LFM3 Omni picked as the brain: it answers questions (things you taught me still come from my memory).
            if (Config.Brain == "model" && !(best != null && best.Entry.Learned != null && best.Score >= HighScore))
            {
                var fromModel = await HelperAsync(q, null, turn.Ct).ConfigureAwait(false);
                if (fromModel != null) return fromModel;
                Step("LFM3 Omni isn't running, so I answered myself");
            }

            AceAnswer? draft = null;
            if (best != null && best.Score >= MidScore) draft = BankAnswer(best, Math.Min(0.99, best.Score + (best.Score >= HighScore ? 0.15 : 0)));

            // "Do you like Minecraft?" / "What's your favorite food?": Ace's own take, unless the bank has a real answer.
            var opinion = Regex.Match(q, @"^(?:do you (?:like|love|enjoy|play|hate)|what do you think (?:of|about)|how do you feel about|are you a fan of)\s+(.+)$", RegexOptions.IgnoreCase);
            if (opinion.Success && (best == null || best.Score < HighScore))
            {
                string thing = opinion.Groups[1].Value.Trim();
                Step("That's asking what I think");
                return AceAnswer.Say(Composer.Pick("opinion", new[]
                {
                    "I can't play or taste things, but {thing} sounds really cool to me. What do you like about it?",
                    "I'm a computer helper, so I don't get to try {thing} myself. But I'd love to hear what you think of it!",
                    "If I could, I'd probably be into {thing}. What's your favorite part?",
                }).Replace("{thing}", thing), "chat", 0.7);
            }

            bool chatty = best != null && ChatTopics.Contains(best.Entry.Topic);
            bool live = TimeSensitive(q);

            // Pass 2: break it down. "Capital and currency of Japan", "Which is bigger, Mars or Earth?"
            // (Before trusting a match, since a bank answer only ever covers one half.)
            if (!OutOfTime && (best == null || best.Score < 1.0))
            {
                var compare = await CompareAsync(q, turn).ConfigureAwait(false);
                if (compare != null) return compare;
                var parts = await CompoundAsync(q, turn).ConfigureAwait(false);
                if (parts != null) return parts;
            }

            // A sure answer to a chat or non-changing question: done.
            if (draft != null && best!.Score >= HighScore && (!live || best.Entry.Learned == null && chatty))
            {
                Step("I'm sure about this one");
                // A sure fact sometimes gets a little opener ("Ooh, I know this."), so Ace doesn't sound the same every time.
                bool chat = chatty || ChatTopics.Contains(best.Entry.Topic) || best.Entry.Id.StartsWith("talk.", StringComparison.Ordinal) || best.Entry.Id.StartsWith("ace.", StringComparison.Ordinal);
                if (!live && !chat && best.Entry.Learned == null && draft.Confidence >= 0.8 && !Regex.IsMatch(draft.Text, @"^(Yes|No|Yep|Nope|Yeah)\b"))
                {
                    draft.Opener = Composer.Pick("bank.sure", new[] { "" });
                    draft.Text = draft.Opener + draft.Text;
                }
                return draft;
            }

            // A decent bank answer to a chat-type question is fine without the web.
            if (draft != null && (chatty || !factual) && !live)
            {
                Step("Good enough match", best!.Entry.Topic);
                return draft;
            }

            // Pass 3: research it.
            Research? found = null;
            if (!OutOfTime && (factual || live || draft == null))
            {
                _passes++;
                found = await ResearchAsync(q, turn, forceWeb: live).ConfigureAwait(false);
            }

            // Pass 4: double-check the draft against the web.
            if (draft != null && found != null && found.Answer.Length > 0)
            {
                _passes++;
                double agree = Agreement(draft.Text, found.Answer);
                Step("Double-checking my answer against the web", $"agreement {agree:P0}");
                if (agree >= 0.34 || !live)
                {
                    draft.Confidence = Math.Min(0.99, draft.Confidence + (agree >= 0.34 ? 0.15 : 0));
                    if (agree < 0.34 && live) draft.Text += " (The web might have newer info: " + Short(found.Answer, 160) + ")";
                    if (found.Sources.Count > 0) draft.Cards.Add(WebCard("Sources", found.Sources.Take(3)));
                    return draft;
                }
                // It changes over time and the web disagrees: the web is newer.
                return WebAnswer(q, found, learn: false);
            }
            if (draft != null && (found == null || found.Answer.Length == 0))
            {
                Step("Going with what I know");
                if (live && best!.Score < 0.9)
                {
                    // It changes over time, the web couldn't be checked, and my match is only so-so: don't guess.
                    Step("That changes over time and my match isn't close enough");
                    if (found == null)
                        return AceAnswer.Say("That one changes over time, so I need the internet to answer it, and I can't reach the web right now. Try again in a bit!", "fallback", 0.3);
                    var unsure = AceAnswer.Say("That one changes over time, and I couldn't find a clear answer online. Want me to open a web search for it?", "fallback", 0.3);
                    string liveQuery = q;
                    unsure.Cards.Add(new ConfirmCard
                    {
                        Question = "Search the web for \"" + Short(liveQuery, 50) + "\"?",
                        YesLabel = "Search",
                        NoLabel = "No thanks",
                        OnYes = () => { Pc.BrowserControl.Open(Pc.BrowserControl.SearchUrl(liveQuery, "", Config.SearchEngine)); return Task.FromResult("Opened a search in your browser."); },
                        OnNo = () => "Okay!",
                    });
                    unsure.Mood = "confused";
                    return unsure;
                }
                if (live)
                {
                    // It changes over time and the web couldn't be checked: say so.
                    draft.Confidence = Math.Min(draft.Confidence, 0.5);
                    draft.Text = (found == null ? "I can't check the web right now, so this might be out of date: " : "I couldn't find anything newer, so this might be out of date: ") + draft.Text;
                    return draft;
                }
                // "I'm fairly sure about this one. I can't know that yet" sounds silly, so answers that are
                // already about not knowing get no hedge.
                draft.Opener = Regex.IsMatch(draft.Text, @"^(I can'?t|Nobody knows|No one knows|That's a myth)", RegexOptions.IgnoreCase) ? "" : Composer.Hedge(draft.Confidence);
                draft.Text = draft.Opener + draft.Text;
                return draft;
            }
            if (found != null && found.Answer.Length > 0 && found.Confidence >= 0.5)
                return WebAnswer(q, found, learn: !live);

            // Pass 5: ask the helper model, if it's there.
            if (Config.UseLlmHelper && !OutOfTime)
            {
                var helped = await HelperAsync(q, found, turn.Ct).ConfigureAwait(false);
                if (helped != null) return helped;
            }

            if (found != null && found.Answer.Length > 0) return WebAnswer(q, found, learn: false);

            // Nothing good: say so, and offer a browser search.
            Step("Couldn't find a good answer");
            // A weak match is usually about something else, so it isn't shown as an answer.
            var a = AceAnswer.Say(Composer.Pick("idk", new[] { "Hmm, I'm not sure about that one. Want me to search the web for it?", "I don't know that yet. Should I open a web search?", "That one's beyond me right now. Want me to look it up in your browser?" }), "fallback", 0.2);
            string query = q;
            a.Cards.Add(new ConfirmCard
            {
                Question = "Search the web for \"" + Short(query, 50) + "\"?",
                YesLabel = "Search",
                NoLabel = "No thanks",
                OnYes = () => { Pc.BrowserControl.Open(Pc.BrowserControl.SearchUrl(query, "", Config.SearchEngine)); return Task.FromResult("Opened a search in your browser."); },
                OnNo = () => "Okay! Teach me with \"remember that\" if you find out.",
            });
            a.Mood = "confused";
            return a;
        }

        private AceAnswer WebAnswer(string q, Research r, bool learn)
        {
            string text = Composer.Hedge(r.Confidence) + Composer.Capital(r.Answer);
            var a = AceAnswer.Say(text, "web", r.Confidence);
            if (r.Sources.Count > 0) a.Cards.Add(WebCard("Sources", r.Sources.Take(4)));
            Talk.LastPageText = r.PageText;
            Talk.LastPageUrl = r.PageUrl;
            Talk.LastPageUsed = r.SentencesUsed;
            if (learn && r.Confidence >= 0.6 && r.Answer.Length < 600)
            {
                var e = Bank.Learn(new[] { q }, r.Answer, r.PageUrl.Length > 0 ? r.PageUrl : "web", "web");
                Talk.LastLearnedId = e.Id;
                Talk.LastEntry = e;
                Step("Learned this for next time");
            }
            return a;
        }

        /// <summary>How much two answers share their important words (0 to 1).</summary>
        private double Agreement(string a, string b)
        {
            var ta = Lexical.Tokens(a).Where(w => w.Length > 2).Distinct().ToList();
            var tb = Lexical.Tokens(b).Where(w => w.Length > 2).Distinct().ToList();
            if (ta.Count == 0 || tb.Count == 0) return 0;
            // Numbers and names matter most.
            double shared = ta.Sum(w => tb.Contains(w) ? (Regex.IsMatch(w, @"\d") ? 2 : 1) * Math.Max(1, Bank.Words.Idf(w) / 3) : 0);
            double total = ta.Sum(w => (Regex.IsMatch(w, @"\d") ? 2 : 1) * Math.Max(1, Bank.Words.Idf(w) / 3));
            double sim = shared / total;
            if (Embedder.IsReady && Embedder.Embed(a) is { } va && Embedder.Embed(b) is { } vb) sim = 0.5 * sim + 0.5 * Embedder.Dot(va, vb);
            return sim;
        }

        // ===================================================== Planner: parts and comparisons

        private async Task<AceAnswer?> CompoundAsync(string q, Turn turn)
        {
            // "what's the capital and currency of japan", "population, capital and language of brazil"
            var m = Regex.Match(q, @"^(?:what(?:'s| is| are)\s+|tell me\s+|give me\s+)?(?:the\s+)?((?:[a-z]+(?:\s[a-z]+)?\s*,\s*)*[a-z]+(?:\s[a-z]+)?)\s+and\s+(?:the\s+)?([a-z]+(?:\s[a-z]+)?)\s+of\s+(.+)$", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            var things = m.Groups[1].Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            things.Add(m.Groups[2].Value.Trim());
            if (things.Count < 2 || things.Count > 5) return null;
            string subject = m.Groups[3].Value.Trim();
            _passes++;
            Step("Breaking it into parts", string.Join(" · ", things.Select(x => x + " of " + subject)));
            var answers = new List<string>();
            double conf = 1;
            foreach (var thing in things)
            {
                if (OutOfTime) break;
                string sub = $"what is the {thing} of {subject}";
                var hit = Bank.Search(sub, 1).FirstOrDefault();
                if (hit != null && hit.Score >= MidScore)
                {
                    Step("Part: " + thing, "from my notes");
                    answers.Add(Composer.Pick(hit.Entry.Id, hit.Entry.Answers));
                    conf = Math.Min(conf, hit.Score + 0.1);
                    continue;
                }
                var r = await ResearchAsync(sub, turn, forceWeb: false, quiet: true).ConfigureAwait(false);
                if (r != null && r.Answer.Length > 0)
                {
                    Step("Part: " + thing, "from the web");
                    answers.Add(Short(r.Answer, 220));
                    conf = Math.Min(conf, r.Confidence);
                }
                else answers.Add($"I couldn't find the {thing}.");
            }
            NoteSubject("what is the " + things[0] + " of " + subject);
            return AceAnswer.Say(Composer.Join(answers), "planner", conf);
        }

        private static readonly Dictionary<string, (string Ask, bool BiggerWins)> CompareWords = new()
        {
            ["bigger"] = ("how big is {0}", true), ["larger"] = ("how big is {0}", true), ["smaller"] = ("how big is {0}", false),
            ["heavier"] = ("how heavy is {0}", true), ["lighter"] = ("how heavy is {0}", false),
            ["taller"] = ("how tall is {0}", true), ["shorter"] = ("how tall is {0}", false),
            ["older"] = ("how old is {0}", true), ["younger"] = ("how old is {0}", false),
            ["faster"] = ("how fast is {0}", true), ["slower"] = ("how fast is {0}", false),
            ["hotter"] = ("how hot is {0}", true), ["colder"] = ("how hot is {0}", false),
            ["farther"] = ("how far is {0} from the sun", true), ["further"] = ("how far is {0} from the sun", true), ["closer"] = ("how far is {0} from the sun", false),
            ["longer"] = ("how long is {0}", true), ["deeper"] = ("how deep is {0}", true),
            ["more populated"] = ("what is the population of {0}", true), ["more people"] = ("what is the population of {0}", true),
        };

        private async Task<AceAnswer?> CompareAsync(string q, Turn turn)
        {
            string words = string.Join("|", CompareWords.Keys.Select(Regex.Escape));
            var m = Regex.Match(q, @"^(?:which|what|who)(?:'s| is| has)?\s+(" + words + @")[,:]?\s+(?:the\s+)?(.+?)\s+or\s+(?:the\s+)?(.+)$", RegexOptions.IgnoreCase);
            string adj, x, y;
            if (m.Success) { adj = m.Groups[1].Value.ToLowerInvariant(); x = m.Groups[2].Value; y = m.Groups[3].Value; }
            else
            {
                m = Regex.Match(q, @"^(?:is|are)\s+(?:the\s+)?(.+?)\s+(" + words + @")\s+than\s+(?:the\s+)?(.+)$", RegexOptions.IgnoreCase);
                if (!m.Success) return null;
                adj = m.Groups[2].Value.ToLowerInvariant(); x = m.Groups[1].Value; y = m.Groups[3].Value;
            }
            var (ask, biggerWins) = CompareWords[adj];
            _passes++;
            Step("Comparing two things", $"{x} vs {y} ({adj})");
            var facts = new List<(string Name, string Text, double? Number)>();
            foreach (var name in new[] { x.Trim(), y.Trim() })
            {
                string sub = string.Format(ask, name);
                string text = "";
                // Any bank answer (for a few ways of asking) that actually has a number in it.
                foreach (var way in AskWays(ask, name))
                {
                    var hit = Bank.Search(way, 1).FirstOrDefault();
                    if (hit == null || hit.Score < MidScore || !Lexical.Tokens(hit.MatchedQuestion).Any(w => Lexical.Tokens(name).Contains(w))) continue;
                    text = hit.Entry.Answers.FirstOrDefault(x => FirstNumber(x) != null) ?? "";
                    if (text.Length > 0) break;
                }
                if (text.Length == 0)
                {
                    var r = await ResearchAsync(sub, turn, forceWeb: false, quiet: true).ConfigureAwait(false);
                    if (r != null) text = r.Answer;
                }
                double? n = FirstNumber(text);
                Step("Looked up " + name, n != null ? n.Value.ToString("N0", CultureInfo.CurrentCulture) : "no number found");
                facts.Add((name, text, n));
            }
            if (facts.All(f => f.Number != null))
            {
                bool firstWins = biggerWins ? facts[0].Number > facts[1].Number : facts[0].Number < facts[1].Number;
                var win = firstWins ? facts[0] : facts[1];
                var lose = firstWins ? facts[1] : facts[0];
                string text = $"{Composer.Capital(win.Name)} is {adj}. {Short(win.Text, 200)} {Short(lose.Text, 200)}";
                return AceAnswer.Say(text.Trim(), "planner", 0.75);
            }
            if (facts.Any(f => f.Text.Length > 0))
                return AceAnswer.Say("I couldn't find numbers to compare them exactly, but here's what I found: " + string.Join(" ", facts.Where(f => f.Text.Length > 0).Select(f => Short(f.Text, 200))), "planner", 0.45);
            return null;
        }

        private static IEnumerable<string> AskWays(string ask, string name)
        {
            yield return string.Format(ask, name);
            if (ask.StartsWith("how big"))
            {
                yield return $"how wide is {name}";
                yield return $"what is the diameter of {name}";
                yield return $"what is the size of {name}";
                yield return $"what is the area of {name}";
            }
            else if (ask.StartsWith("how tall")) yield return $"what is the height of {name}";
            else if (ask.StartsWith("how heavy")) { yield return $"how much does {name} weigh"; yield return $"what is the mass of {name}"; }
            else if (ask.StartsWith("how fast")) yield return $"what is the top speed of {name}";
            else if (ask.StartsWith("how long")) yield return $"what is the length of {name}";
            else if (ask.StartsWith("how hot")) yield return $"what is the temperature of {name}";
        }

        /// <summary>The first real quantity in a sentence: "about 6,779 km across" → 6779, "4.6 billion years" → 4.6e9.</summary>
        public static double? FirstNumber(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            foreach (Match m in Regex.Matches(text, @"(\d{1,3}(?:,\d{3})+|\d+(?:\.\d+)?)\s*(thousand|million|billion|trillion)?", RegexOptions.IgnoreCase))
            {
                if (!double.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) continue;
                // Skip years in "in 1969" style when a bigger quantity follows.
                if (v >= 1000 && v <= 2100 && Regex.IsMatch(text.Substring(Math.Max(0, m.Index - 4), Math.Min(4, m.Index)), @"\bin\s*$")) continue;
                v *= m.Groups[2].Value.ToLowerInvariant() switch { "thousand" => 1e3, "million" => 1e6, "billion" => 1e9, "trillion" => 1e12, _ => 1 };
                return v;
            }
            return null;
        }

        // ===================================================== Web research

        private async Task<Research?> ResearchAsync(string question, Turn turn, bool forceWeb, bool quiet = false)
        {
            var ct = turn.Ct;
            if (!await WebClient.OnlineAsync(ct).ConfigureAwait(false))
            {
                if (!quiet) Step("You're offline, so I can't check the web");
                return null;
            }
            if (!quiet) { Step("Searching the web", question); Say("Searching the web…"); }

            var definitional = Regex.Match(question, @"^(?:who|what)\s+(?:is|was|are|were)\s+(?:a\s+|an\s+|the\s+)?(.+)$", RegexOptions.IgnoreCase);
            var instTask = WebClient.InstantAnswerAsync(question, ct);
            var searchTask = WebClient.SearchAsync(question, 8, ct);
            Task<WikiSummary?> wikiTask = definitional.Success && !TimeSensitive(question)
                ? WebClient.WikipediaAsync(definitional.Groups[1].Value, ct) : Task.FromResult<WikiSummary?>(null);
            try { await Task.WhenAll(instTask, searchTask, wikiTask).ConfigureAwait(false); } catch { }

            var research = new Research();
            var candidates = new List<(string Text, double Score, string Url, string Page)>();
            var inst = instTask.IsCompletedSuccessfully ? instTask.Result : null;
            var results = searchTask.IsCompletedSuccessfully ? searchTask.Result : new List<WebResult>();
            var wiki = wikiTask.IsCompletedSuccessfully ? wikiTask.Result : null;
            research.Sources = results;

            if (inst != null)
            {
                if (!quiet) Step("Got a quick answer", inst.Value.Source);
                var sentences = Lexical.BestSentences(question, inst.Value.Text, 2, Embedder);
                string text = sentences.Count > 0 ? string.Join(" ", sentences) : inst.Value.Text;
                candidates.Add((text, 0.72 + Relevance(question, text) * 0.25, inst.Value.Url, inst.Value.Text));
            }
            if (wiki != null && wiki.Extract.Length > 40)
            {
                if (!quiet) Step("Read Wikipedia", wiki.Title);
                var sentences = Lexical.BestSentences(question, wiki.Extract, 2, Embedder);
                string text = sentences.Count > 0 ? string.Join(" ", sentences) : wiki.Extract;
                candidates.Add((text, 0.7 + Relevance(question, text) * 0.25, wiki.Url, wiki.Extract));
                if (wiki.Url.Length > 0 && !results.Any(r => r.Url == wiki.Url)) research.Sources.Insert(0, new WebResult(wiki.Title + " - Wikipedia", wiki.Url, wiki.Description));
            }

            // Read the top pages (in parallel) and pull out the sentences that answer the question.
            bool readPages = results.Count > 0 && (candidates.Count == 0 || candidates.Max(c => c.Score) < 0.85 || forceWeb);
            if (readPages && !OutOfTime)
            {
                var toRead = results.Where(r => !Regex.IsMatch(r.Host, @"youtube\.com|tiktok\.com|instagram\.com|facebook\.com|x\.com|twitter\.com|pinterest\.")).Take(3).ToList();
                if (!quiet) Step("Reading " + toRead.Count + " pages", string.Join(", ", toRead.Select(r => r.Host)));
                var reads = toRead.Select(r => WebClient.ReadPageAsync(r.Url, ct)).ToList();
                try { await Task.WhenAll(reads).ConfigureAwait(false); } catch { }
                for (int i = 0; i < reads.Count; i++)
                {
                    var page = reads[i].IsCompletedSuccessfully ? reads[i].Result : null;
                    if (page == null || page.Value.Text.Length < 200) continue;
                    var best = Lexical.BestSentences(question, page.Value.Text, 2, Embedder);
                    if (best.Count == 0) continue;
                    string text = string.Join(" ", best);
                    double score = 0.45 + Relevance(question, text) * 0.45 - i * 0.03;
                    candidates.Add((text, score, toRead[i].Url, page.Value.Text));
                }
                // Snippets are short but often right on target.
                foreach (var r in results.Take(4).Where(r => r.Snippet.Length > 40))
                    candidates.Add((r.Snippet, 0.35 + Relevance(question, r.Snippet) * 0.4, r.Url, r.Snippet));
            }

            if (candidates.Count == 0)
            {
                if (!quiet) Step("No page had a clear answer");
                return research;
            }
            var top = candidates.OrderByDescending(c => c.Score).First();
            // Two sources saying the same thing makes Ace surer.
            int agreeing = candidates.Count(c => c.Url != top.Url && Agreement(top.Text, c.Text) > 0.4);
            double confidence = Math.Clamp(top.Score + 0.06 * agreeing, 0, 0.95);
            if (!quiet) Step("Picked the best answer", $"{HostOf(top.Url)} · {confidence:P0} sure" + (agreeing > 0 ? $" · {agreeing} other source{(agreeing > 1 ? "s" : "")} agree" : ""));
            research.Answer = CleanAnswer(top.Text);
            research.Confidence = confidence;
            research.PageText = top.Page;
            research.PageUrl = top.Url;
            research.SentencesUsed = 2;
            return research;
        }

        private static string CleanAnswer(string text)
        {
            string s = Regex.Replace(text, @"\s*\([^)]*(pronounced|listen|/|ⓘ)[^)]*\)", "");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s.Length > 600 ? s[..600].TrimEnd() + "…" : s;
        }

        /// <summary>How well a sentence answers a question, 0 to 1 (meaning net if there, words otherwise).</summary>
        private double Relevance(string question, string text)
        {
            if (Embedder.IsReady && Embedder.Embed(question) is { } qv && Embedder.Embed(Short(text, 500)) is { } tv)
                return Math.Clamp((Embedder.Dot(qv, tv) - 0.15) / 0.6, 0, 1);
            var qt = Lexical.Tokens(question);
            var tt = Lexical.Tokens(text);
            return qt.Count == 0 ? 0 : qt.Count(w => tt.Contains(w)) / (double)qt.Count;
        }

        // ===================================================== Helper model (optional)

        private async Task<AceAnswer?> HelperAsync(string question, Research? notes, CancellationToken ct)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(30));
                Step("Asking my helper model", AceBrain.OmniName);
                Say("Asking the helper model…");
                string context = notes != null && notes.PageText.Length > 0
                    ? "\n\nNotes from the web:\n" + string.Join(" ", Lexical.BestSentences(question, notes.PageText, 5, Embedder))
                    : "";
                string system = $"You are Ace, a friendly assistant app on a Windows PC, talking to {Config.UserName}, who is 12. Answer in 1 to 3 short, plain sentences. If you aren't sure, say so. Use the notes if they help.";
                string text = await AceBrain.Shared.CompleteAsync(new[] { ChatTurn.User(question + context) }, system, null, cts.Token).ConfigureAwait(false);
                text = ThinkTags.Strip(text);
                text = Regex.Replace(text, @"^\s*(ace|assistant)\s*:\s*", "", RegexOptions.IgnoreCase).Trim();
                if (string.IsNullOrWhiteSpace(text)) return null;
                Step("The helper answered");
                var a = AceAnswer.Say(text, "helper", Config.Brain == "model" ? 0.75 : 0.55);
                if (notes?.Sources.Count > 0) a.Cards.Add(WebCard("Sources", notes.Sources.Take(3)));
                return a;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                Step("Helper model isn't available", Short(ex.Message, 60));
                return null;
            }
        }
    }
}
