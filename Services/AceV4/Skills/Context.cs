using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Nimbus.AceV4.Brain;
using Nimbus.AceV4.Pc;

namespace Nimbus.AceV4
{
    /// <summary>
    /// Follow-ups: "what about Germany?", "and its currency?", "another one", "close it", "what about tomorrow",
    /// "divide that by 4", "when was he president". Ace keeps track of what the last question was about
    /// (a country, planet, element, month, number...), what the answer named, the last app and the last number.
    /// </summary>
    public sealed partial class AceV4Brain
    {

        // ===================================================== Things Ace knows the kind of

        /// <summary>Names of countries, states, planets, elements, months and presidents, with their kind. Longest names first.</summary>
        private List<(string Name, string Kind)> _entities = new();

        private static readonly string[] Months = { "january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december" };
        private static readonly string[] Days = { "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday" };
        private const string TimeWords = @"today|tonight|tomorrow|tomorrow night|tomorrow morning|this weekend|the weekend|next week|this week|this morning|this afternoon|this evening|right now|now|later|monday|tuesday|wednesday|thursday|friday|saturday|sunday|next (?:monday|tuesday|wednesday|thursday|friday|saturday|sunday)|on (?:monday|tuesday|wednesday|thursday|friday|saturday|sunday)";

        private void BuildEntities()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in Bank.All())
            {
                var parts = e.Id.Split('.');
                if (parts.Length < 3) continue;
                string name = parts[2].Replace('_', ' ');
                string? kind = (parts[0], parts[1]) switch
                {
                    ("geo", "capital" or "continent" or "currency" or "language") => "country",
                    ("us", "capital" or "abbr" or "nickname") => "state",
                    ("chem", "symbol" or "number") => "element",
                    ("pres", "when") => "person",
                    ("cal", "days_in" or "month_number") => "month",
                    ("space", var body) when body is not ("planets" or "v4") && parts.Length >= 3 => "space",
                    _ => null,
                };
                if (kind == "space") name = parts[1];
                if (kind == null || name.Length < 3 || Regex.IsMatch(name, @"\d")) continue;
                map.TryAdd(name, kind);
            }
            foreach (var m in Months) map[m] = "month";
            foreach (var d in Days) map[d] = "day";
            map.TryAdd("the sun", "space"); map.TryAdd("the moon", "space");
            _entities = map.Select(kv => (kv.Key, kv.Value)).OrderByDescending(x => x.Key.Length).ToList();
        }

        /// <summary>The first known thing named in some text, with its kind ("japan", "country").</summary>
        private (string Name, string Kind, int Index)? FindEntity(string text, string? kind = null)
        {
            string l = " " + text.ToLowerInvariant() + " ";
            (string, string, int)? best = null;
            foreach (var (name, k) in _entities)
            {
                if (kind != null && k != kind) continue;
                var m = Regex.Match(l, @"(?<![a-z])" + Regex.Escape(name) + @"(?![a-z])");
                if (!m.Success) continue;
                if (best == null || m.Index < best.Value.Item3) best = (name, k, m.Index - 1);
            }
            return best;
        }

        private string? KindOf(string phrase)
        {
            string p = Regex.Replace(phrase.ToLowerInvariant().Trim(), @"^(the|a|an)\s+", "");
            if (Regex.IsMatch(p, @"^\d+(st|nd|rd|th)$|^(first|second|third|fourth|fifth|sixth|seventh|eighth|ninth|tenth|\w+teenth|\w+tieth|\w+ty-\w+)$")) return "ordinal";
            if (Regex.IsMatch(p, @"^-?\d+(\.\d+)?(\s+[a-z]+)?$")) return "number";
            if (Regex.IsMatch(p, @"^(\w+est|most \w+|least \w+)$")) return "superlative";
            var e = _entities.FirstOrDefault(x => x.Name.Equals(p, StringComparison.OrdinalIgnoreCase) || x.Name.Equals("the " + p, StringComparison.OrdinalIgnoreCase));
            return e.Name != null ? e.Kind : null;
        }

        // ===================================================== Remembering what things were about

        /// <summary>Remembers what a question was about, for "what about…" and "it".</summary>
        private void NoteSubject(string question)
        {
            Talk.LastKnowledgeQuestion = question;
            Talk.LastSubjectAt = DateTime.Now;
            // A known thing first (a country, planet, element...), else the words after "of/is/does...".
            var known = FindEntity(question);
            if (known != null) { Talk.LastSubject = known.Value.Name; return; }
            var m = Regex.Match(question, @"\b(?:of|in|for|about|is|are|was|does|did|do)\s+(?:the\s+|a\s+|an\s+)?([A-Za-z][A-Za-z .'-]{1,40}?)\s*\??$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                string s = Regex.Replace(m.Groups[1].Value.Trim(), @"\s+(in|on|at|from|to|for|with|about|of|have|has|made of|use|located)$", "", RegexOptions.IgnoreCase);
                if (!Regex.IsMatch(s, @"^(it|that|this|you|me|there|they|them)$", RegexOptions.IgnoreCase)) Talk.LastSubject = s;
            }
        }

        /// <summary>Called after every answer: the name it gave ("Abraham Lincoln", "Tokyo"), the last number, the last app.</summary>
        private void NoteAnswer(string question, AceAnswer answer, string route)
        {
            if (answer.Source is "bank" or "web" or "learned" or "planner" or "helper")
            {
                string said = answer.Opener.Length > 0 && answer.Text.StartsWith(answer.Opener, StringComparison.Ordinal) ? answer.Text[answer.Opener.Length..] : answer.Text;
                Talk.LastAnswerName = NameIn(said, question) ?? "";
                Talk.LastAnswerWho = Regex.IsMatch(question, @"^\s*who\b", RegexOptions.IgnoreCase);
                Talk.LastAnswerIsThing = Regex.IsMatch(question, @"^\s*(who|which)\b|\b(\w+est|most|least)\b", RegexOptions.IgnoreCase);
            }
            if (route is "calc" && Regex.Match(answer.Text, @"-?\d[\d,]*(\.\d+)?") is { Success: true } num
                && double.TryParse(num.Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                Talk.LastNumber = v;
            if (route.StartsWith("app.", StringComparison.Ordinal) || route == "browser.open")
            {
                var app = Regex.Match(question, @"^(?:open|launch|start|run|fire up|bring up|pull up|load|go to|visit|close|quit|exit|kill|minimi[sz]e|maximi[sz]e|hide|restore|snap|resize|move)\s+(?:the\s+|my\s+)?(.+?)(?:\s+(?:app|window|program))?(?:\s+to\s+.+)?$", RegexOptions.IgnoreCase);
                if (app.Success && !Regex.IsMatch(app.Groups[1].Value, @"^(it|that|this|everything|all)$", RegexOptions.IgnoreCase)) Talk.LastApp = app.Groups[1].Value;
            }
            if (route is "weather" or "calendar.list" or "time" or "sleep.bedtime" or "system.status" or "storage")
            {
                Talk.LastIntent = route;
                Talk.LastIntentText = question;
                Talk.LastIntentAt = DateTime.Now;
            }
        }

        /// <summary>The first name in an answer that isn't in the question ("The capital of Japan is Tokyo." → "Tokyo").</summary>
        private static string? NameIn(string answer, string question)
        {
            string q = question.ToLowerInvariant();
            foreach (Match m in Regex.Matches(answer, @"\b([A-Z][a-z'.]+(?:\s+(?:[A-Z][a-z'.]*|of|the|de|da|von)){0,4})"))
            {
                string name = Regex.Replace(m.Value.Trim(), @"\s+(of|the|de|da|von)$", "").Trim();
                if (name.Length < 3) continue;
                bool sentenceStart = m.Index == 0 || Regex.IsMatch(answer[..m.Index], @"[.!?]\s*$");
                if (sentenceStart && !name.Contains(' ') && Regex.IsMatch(name, @"^(The|A|An|It|It's|Its|There|This|That|These|Those|They|You|Your|Yes|No|About|Around|Most|Many|Some|Each|Every|In|On|At|For|When|If|Because|Here|So|Well|Over|Only|Usually|Kids|Adults|People|Number|President|Probably|Actually|Then|After|Before|Today|Right|Sure|Yep|Nope|Nobody|Everyone|Something|Nothing|Just|Also|Even|Not|All|Both|One|Two|Three|First|Last|Back|Long|Way|Lots|Plenty|Roughly|Nearly|Almost|Exactly|Since|During|Until|Like|With|From|To|By|As|Or|And|But|Now)$")) continue;
                name = Regex.Replace(name, @"^(The|A|An)\s+", "");
                if (q.Contains(name.ToLowerInvariant())) continue;
                if (Regex.IsMatch(name, @"^(I|I'm|Ace|Earth|Sun|Moon|United States|America|Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday)$")) continue;
                return name;
            }
            return null;
        }

        // ===================================================== Follow-ups

        private async Task<AceAnswer?> FollowUpAsync(Turn turn)
        {
            // Ace asked something ("When is it?"); this message may be the answer.
            if (Talk.PendingFollow is { } follow)
            {
                Talk.PendingFollow = null;
                if (DateTime.Now - Talk.PendingFollowAt < TimeSpan.FromMinutes(3))
                {
                    var got = await follow(turn).ConfigureAwait(false);
                    if (got != null) { Step("That answers my question"); return got; }
                }
            }

            string l = turn.Lower;
            bool fresh = DateTime.Now - Talk.LastSubjectAt < TimeSpan.FromMinutes(10);

            // "the second one", "number 3": open something from the last list.
            var pick = PickResult(Regex.Replace(l, @"^(open|show|play|launch)\s+", ""));
            if (pick != null && (Regex.IsMatch(l, @"^(open|show|play|launch)\b") || Regex.IsMatch(l, @"^(the\s+)?(first|second|third|fourth|fifth|last|1st|2nd|3rd)\b|^number\s*\d")))
            {
                Step("Opening what I found", pick.Title);
                if (DryRunActions) return AceAnswer.Say("[dry run] open " + pick.Title, "files");
                if (pick.Target.Length > 0) Pc.AppLauncher.Start(pick.Target);
                return AceAnswer.Say("Opening " + pick.Title + ".", "files");
            }

            // "another one", "one more", "another joke": a different answer from the same family.
            if (Regex.IsMatch(l, @"^(another( one)?|one more( please)?|again|tell me another( one)?|another (joke|riddle|fact|fun fact|one please)|more (jokes|riddles|facts)|give me another( one)?|do another( one)?)\??$") && Talk.LastEntry is { } le)
            {
                string family = le.Id.Contains('.') ? le.Id[..le.Id.LastIndexOf('.')] : le.Id;
                var others = Bank.Family(family).Where(e => e.Id != le.Id && e.Topic == le.Topic).ToList();
                if (others.Count > 0 && !family.StartsWith("learned", StringComparison.Ordinal))
                {
                    var next = others[Rng.Next(others.Count)];
                    Step("Another one", family);
                    return BankAnswer(new BankHit(next, 1, 0, 1, next.Questions[0]), 0.95);
                }
                if (le.Answers.Count > 1)
                {
                    Step("Another way to say it");
                    var a = AceAnswer.Say(Composer.Pick(le.Id, le.Answers), "bank", 0.9);
                    a.BankId = le.Id;
                    return a;
                }
            }

            // "tell me more", "go on", "more"
            if (Regex.IsMatch(l, @"^(tell me more|more|go on|keep going|continue|and\??|more please|what else|more info|elaborate|tell me more about (it|that))$"))
            {
                if (Talk.LastPageText.Length > 0)
                {
                    var more = Lexical.BestSentences(Talk.LastKnowledgeQuestion, Talk.LastPageText, Talk.LastPageUsed + 2, Embedder)
                        .Skip(Talk.LastPageUsed).ToList();
                    if (more.Count > 0)
                    {
                        Talk.LastPageUsed += more.Count;
                        Step("Reading more of the page", HostOf(Talk.LastPageUrl));
                        return AceAnswer.Say(string.Join(" ", more), "web", 0.7);
                    }
                }
                if (Talk.LastEntry is { } entry)
                {
                    // The other facts in the same entry first, then a related question.
                    var extra = entry.Answers.Skip(1).Where(x => !SameFacts(entry.Answers[0], x)).FirstOrDefault(x => !Talk.Recent.Any(r => r.Ace.Contains(x, StringComparison.Ordinal)));
                    if (extra != null) { Step("More from my notes"); var a = AceAnswer.Say(Composer.Fill(extra), "bank", 0.9); a.BankId = entry.Id; return a; }
                    if (entry.Followups.Count > 0)
                    {
                        var next = entry.Followups[0];
                        var hit = Bank.Search(next, 1).FirstOrDefault();
                        if (hit != null) { Step("Following on", next); return BankAnswer(hit, hit.Score); }
                    }
                }
                if (Talk.LastKnowledgeQuestion.Length > 0)
                {
                    turn.Text = Talk.LastKnowledgeQuestion;
                    turn.Lower = turn.Text.ToLowerInvariant();
                    var r = await ResearchAsync(turn.Text, turn, forceWeb: true).ConfigureAwait(false);
                    if (r != null && r.Answer.Length > 0) return AceAnswer.Say(r.Answer, "web", r.Confidence);
                }
                return AceAnswer.Say("That's all I've got on that one.", "chat", 0.5);
            }

            // "why?" after an answer.
            if (Regex.IsMatch(l, @"^(why|how come|but why)\??$") && Talk.LastKnowledgeQuestion.Length > 0)
            {
                Rewrite(turn, "why " + Regex.Replace(Talk.LastKnowledgeQuestion, @"^(what|who|when|where|which|how|is|are|does|do)\s+", "", RegexOptions.IgnoreCase), "You asked why");
                return null;
            }

            // "close it", "minimize that": the last app.
            var appCmd = Regex.Match(l, @"^(close|quit|exit|kill|minimi[sz]e|maximi[sz]e|hide|restore|open|reopen|snap|move|resize)\s+(it|that|that app|that one|that window|this app|it back up|it again)\b(.*)$");
            if (appCmd.Success && Talk.LastApp.Length > 0)
            {
                Rewrite(turn, appCmd.Groups[1].Value + " " + Talk.LastApp + appCmd.Groups[3].Value, "\"It\" means " + Talk.LastApp);
                return null;
            }

            // "divide that by 4", "times 3", "plus 10": keep going from the last math answer.
            if (Talk.LastNumber is double last)
            {
                string n = last.ToString(CultureInfo.InvariantCulture);
                bool relative = Regex.IsMatch(l, @"^(times|plus|minus|divided by|multiplied by|over|squared|cubed|add|subtract|take away|double it|half (of )?it)\b")
                                && !Regex.IsMatch(l, @"^(add|subtract|take away)\s+-?[\d.]+\s+(to|from)\s+-?\d");
                bool refers = Regex.IsMatch(l, @"\b(that|it|the answer|the result|this)\b")
                              && Regex.IsMatch(l, @"\b(divide|divided|times|multiply|multiplied|plus|minus|add|subtract|squared|cubed|percent|half|double|over|take away)\b");
                if (relative || refers)
                {
                    string t = Regex.Replace(l, @"\b(that|it|the answer|the result|this)\b", n);
                    t = Regex.Replace(t, @"^(times|plus|minus|divided by|multiplied by|over|squared|cubed)\b", n + " $1");
                    t = Regex.Replace(t, @"^divide\s+(-?[\d.]+)\s+by\s+", "$1 divided by ");
                    t = Regex.Replace(t, @"^multiply\s+(-?[\d.]+)\s+by\s+", "$1 times ");
                    t = Regex.Replace(t, @"^add\s+(-?[\d.]+)\s+to\s+(-?[\d.]+)", "$2 plus $1");
                    t = Regex.Replace(t, @"^(?:subtract|take away)\s+(-?[\d.]+)\s+from\s+(-?[\d.]+)", "$2 minus $1");
                    t = Regex.Replace(t, @"^(?:subtract|take away)\s+(-?[\d.]+)$", n + " minus $1");
                    t = Regex.Replace(t, @"^add\s+(-?[\d.]+)$", n + " plus $1");
                    if (Calculator.ToExpression(t) != null) { Rewrite(turn, t, "Using the last answer, " + n); return null; }
                }
            }

            // "and its currency?", "what about their language", "also its capital": a new question about the same thing.
            var prop = Regex.Match(l, @"^(?:and\s+|also\s+|what about\s+|how about\s+|and what about\s+)*(?:its|their|his|her)\s+(.+?)\??$");
            if (prop.Success && Talk.LastSubject.Length > 0 && fresh)
            {
                string who = Regex.IsMatch(l, @"\b(his|her)\b") && Talk.LastAnswerName.Length > 0 ? Talk.LastAnswerName : Talk.LastSubject;
                Rewrite(turn, $"what is the {prop.Groups[1].Value.Trim()} of {who}", "Same thing, new question");
                return null;
            }

            // "what about tomorrow?" after the weather or the calendar.
            var when = Regex.Match(l, @"^(?:and\s+|so\s+)?(?:what about|how about|and|what's it like|how's it look(?:ing)?)?\s*(" + TimeWords + @")\??$");
            if (when.Success && Talk.LastIntentText.Length > 0 && DateTime.Now - Talk.LastIntentAt < TimeSpan.FromMinutes(10))
            {
                string baseText = Regex.Replace(Talk.LastIntentText, @"\b(" + TimeWords + @")\b", "", RegexOptions.IgnoreCase).Trim();
                Rewrite(turn, Regex.Replace(baseText + " " + when.Groups[1].Value, @"\s+", " "), "Same thing, " + when.Groups[1].Value);
                return null;
            }

            // "what about Germany?" / "and Japan?" / "how about mars" / "and the smallest?" / "and 10 miles?" / "in a leap year?"
            var about = Regex.Match(l, @"^(?:and\s+)?(?:what|how)\s+about\s+(.+?)\??$|^and\s+(?!(?:open|close|launch|start|set|add|remind|search|find|play|turn|take|show|then)\b)(?:what about\s+)?(.+?)\??$|^((?:in|on|at|during|for|with|without)\s+.+?)\?$");
            string lastQ = Talk.LastClause.Length > 0 ? Talk.LastClause : Talk.LastKnowledgeQuestion;
            if (about.Success && lastQ.Length > 0)
            {
                string newPart = (about.Groups[1].Success ? about.Groups[1].Value : about.Groups[2].Success ? about.Groups[2].Value : about.Groups[3].Value).Trim();
                string bare = Regex.Replace(newPart, @"^(the|a|an)\s+", "");
                string? rewritten = SwapLike(lastQ, newPart);
                if (rewritten == null && Regex.IsMatch(newPart, @"^(in|on|at|during|for|with|without)\s"))
                    rewritten = lastQ.TrimEnd('?', ' ') + " " + newPart;
                if (rewritten == null && Talk.LastSubject.Length > 0 && fresh)
                    rewritten = Regex.Replace(lastQ, Regex.Escape(Talk.LastSubject), bare, RegexOptions.IgnoreCase);
                if (rewritten != null && !rewritten.Equals(lastQ, StringComparison.OrdinalIgnoreCase))
                {
                    Rewrite(turn, rewritten, "Same question, new subject");
                    return null;
                }
            }

            // "how big is it?", "when was he president?", "what currency do they use there?": fill in who or what.
            if (fresh && (Talk.LastSubject.Length > 0 || Talk.LastAnswerName.Length > 0) && LooksFactual(l)
                && Regex.IsMatch(l, @"\b(it|its|it's|there|they|their|them|he|she|him|his|her)\b") && l.Split(' ').Length <= 10
                && !Regex.IsMatch(l, @"\b(what|which) (time|day|date|year|month) is it\b|\b(is it|it is|it's)\s+(raining|snowing|cold|hot|warm|sunny|time|going to|late|early|bedtime|done|over|safe|okay|ok)\b|^(what|who|how|where) is it\??$|\bhow('?s| is) it going\b|\bthere (is|are)\b|^(is|are) there\b")
                && !Rules.Any(r => r.Pattern.IsMatch(l)))
            {
                bool person = Regex.IsMatch(l, @"\b(he|she|him|his|her)\b");
                string it = person
                    ? (Talk.LastAnswerName.Length > 0 && Talk.LastAnswerWho ? Talk.LastAnswerName : Talk.LastSubject)
                    : (Talk.LastAnswerIsThing && Talk.LastAnswerName.Length > 0 ? Talk.LastAnswerName : Talk.LastSubject);
                if (it.Length > 0)
                {
                    string r = turn.Text;
                    r = Regex.Replace(r, @"\b(its|their|his|her)\b", it + "'s", RegexOptions.IgnoreCase);
                    r = Regex.Replace(r, @"\bthere\b", "in " + it, RegexOptions.IgnoreCase);
                    r = Regex.Replace(r, @"\b(it|they|them|he|she|him)\b", it, RegexOptions.IgnoreCase);
                    Rewrite(turn, r, "\"" + Regex.Match(l, @"\b(it|its|it's|there|they|their|them|he|she|him|his|her)\b").Value + "\" means " + it);
                }
            }
            return null;
        }

        private void Rewrite(Turn turn, string text, string why)
        {
            Step(why, text);
            turn.Text = text;
            turn.Lower = text.ToLowerInvariant();
        }

        /// <summary>
        /// Swaps the thing in the last question that's the same kind as the new one:
        /// "how long is a year on mars" + "venus" → "how long is a year on venus";
        /// "who was the 1st president" + "the 2nd" → "who was the 2nd president".
        /// </summary>
        private string? SwapLike(string lastQ, string newPart)
        {
            string bare = Regex.Replace(newPart.Trim(), @"^(?:in|on|at|during|for|with)\s+", "");
            bare = Regex.Replace(bare, @"^(the|a|an)\s+", "");
            string? kind = KindOf(bare);
            switch (kind)
            {
                case null:
                    return null;
                case "ordinal":
                    {
                        var m = Regex.Match(lastQ, @"\b(\d+(st|nd|rd|th)|first|second|third|fourth|fifth|sixth|seventh|eighth|ninth|tenth|\w+teenth|\w+tieth)\b", RegexOptions.IgnoreCase);
                        return m.Success ? lastQ.Remove(m.Index, m.Length).Insert(m.Index, bare) : null;
                    }
                case "number":
                    {
                        var nm = Regex.Match(bare, @"^(-?\d+(?:\.\d+)?)(?:\s+([a-z]+))?$");
                        string unit = nm.Groups[2].Value;
                        var m = unit.Length > 0
                            ? Regex.Match(lastQ, @"-?\d+(?:\.\d+)?\s+" + Regex.Escape(Lexical.Stem(unit)) + @"[a-z]*", RegexOptions.IgnoreCase)
                            : Regex.Match(lastQ, @"-?\d+(?:\.\d+)?", RegexOptions.IgnoreCase);
                        if (!m.Success && unit.Length > 0) m = Regex.Match(lastQ, @"-?\d+(?:\.\d+)?\s+[a-z]+", RegexOptions.IgnoreCase);
                        return m.Success ? lastQ.Remove(m.Index, m.Length).Insert(m.Index, bare) : null;
                    }
                case "superlative":
                    {
                        var m = Regex.Match(lastQ, @"\b(\w+est|most \w+|least \w+)\b", RegexOptions.IgnoreCase);
                        return m.Success ? lastQ.Remove(m.Index, m.Length).Insert(m.Index, bare) : null;
                    }
                default:
                    {
                        var old = FindEntity(lastQ, kind);
                        if (old == null) return null;
                        var (name, _, _) = old.Value;
                        var m = Regex.Match(lastQ, @"(?<![a-z])" + Regex.Escape(name) + @"(?![a-z])", RegexOptions.IgnoreCase);
                        string replacement = Regex.Replace(bare, @"^the\s+", "");
                        if (name.StartsWith("the ", StringComparison.Ordinal) && !replacement.StartsWith("the ", StringComparison.Ordinal) && kind == "space") replacement = "the " + replacement;
                        return m.Success ? lastQ.Remove(m.Index, m.Length).Insert(m.Index, replacement) : null;
                    }
            }
        }

        /// <summary>Two answers that state the same thing (same numbers and names), so either can be used.</summary>
        public static bool SameFacts(string a, string b)
        {
            var ka = KeyFacts(a);
            if (ka.Count == 0) return false;
            var kb = KeyFacts(b);
            return ka.All(kb.Contains);
        }

        /// <summary>The numbers and names in an answer: "Water freezes at 0 C, which is 32 F." → {0, 32, C, F}.</summary>
        public static HashSet<string> KeyFacts(string text)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(text, @"\d[\d,.]*")) set.Add(m.Value.TrimEnd('.', ',').Replace(",", ""));
            foreach (Match m in Regex.Matches(text, @"(?<!^)(?<![.!?]\s)\b[A-Z][a-zA-Z]+")) set.Add(m.Value);
            return set;
        }
    }
}
