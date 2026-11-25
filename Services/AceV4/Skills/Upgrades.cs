using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Nimbus.AceV4.Brain;
using Nimbus.AceV4.Pc;

namespace Nimbus.AceV4
{
    /// <summary>
    /// The big upgrade: homework steps, translating, trivia and 20 questions, memory you can see, notes,
    /// a shopping list, what's playing, reading the screen, screen time, money and "convert everything",
    /// follow-up questions, and the built-in Lua and C++ script library.
    /// </summary>
    public sealed partial class AceV4Brain
    {
        /// <summary>The upgrade rules, checked before the older rules (order matters).</summary>
        private List<Rule> UpgradeRules() => new()
        {
            new("homework", R(@"^(?:can you |could you |will you |please )?(?:help|helping) me (?:with|do|on|solve) (?:my |some |this |a |the )?(?:math|maths|algebra|fractions?|homework|equations?|math homework|maths homework|math problems?|math problem|problem|problems)(?: homework| problems?)?$|^(?:i need help with|i'?m stuck on|help with) (?:my |a |this )?(?:math |maths )?(?:homework|problem|question)$|^homework help(?:er)?$|^(?:let'?s do|start) (?:my )?(?:math )?homework$"), HomeworkStartAsync),
            new("hotkey", R(ShortcutPattern), ShortcutAsync),
            new("trivia.scores", R(@"^(?:reset|clear|delete|erase|wipe) (?:my |all |all my |the )?(?:trivia|quiz) (?:high ?)?(?:scores?|stats|records?)$|\b(?:trivia|quiz)\b.*\b(?:high ?scores?|best scores?|scores|records?|leaderboard|stats|best streak|streaks?)\b|\b(?:high ?scores?|best scores?|leaderboard|best streak)\b.*\b(?:trivia|quiz)\b|^(?:what'?s|whats|what is|what are|show|show me|tell me|see|check) (?:me )?my (?:high ?scores?|best scores?|best streak)\??$|^(?:my )?high ?scores?$|^leaderboard$|^how many (?:trivia|quiz) (?:games|questions)\b"), TriviaScoresAsync),
            new("trivia", R(@"\btrivia\b|^quiz me\b|\bquiz me on\b|^(?:let'?s |can we |i want to |wanna )?(?:play|do|start|take) (?:a |some )?quiz(?:zes)?\b|^give me a quiz\b|^test me on\b"), TriviaAsync),
            new("twentyq", R(@"\b(?:20|twenty) questions\b|\bguess what i'?m thinking\b|\bguess (?:my|the) (?:animal|thing|object)\b"), TwentyQAsync),
            new("memory.list", R(@"^(?:what do you (?:know|remember) about me|what have i told you(?: about me)?|what do you remember|what'?s in your memory|show (?:me )?(?:my|your) memor(?:y|ies)|(?:my|your) memories|list (?:my|your) memories|open (?:my |your )?memor(?:y|ies)|what things do you remember)\??$"), MemoryListAsync),
            new("memory.clear", R(@"^(?:forget|delete|erase|clear|wipe) (?:everything|all|all of it)(?: you know| i told you)?(?: about me)?$|^(?:clear|delete|erase|wipe|forget) (?:all )?(?:of )?(?:my|your) memor(?:y|ies)$"), MemoryClearAsync),
            new("notes", R(@"^(?:take|make|add|write|create|jot)(?: down)?(?: me)?(?: a| an)?(?: quick| new)? notes?\b|^(?:write|jot|put) (?:this |that |it )?down\b|^note to self\b|^(?:read|show|list|open|see|check|what are|what'?s in|tell me|whats in)(?: me)?(?: all)?(?: of)? my notes\b|^(?:my )?notes$|^what did i (?:write down|note down|jot down)\b|^(?:delete|remove|erase|clear|get rid of)(?: all)?(?: of)? (?:the |my |that |last |the last )?(?:notes?|note about .+)$|^add .+ to my notes$"), NotesAsync),
            new("shopping", R(@"\b(?:shopping|grocery|groceries|groceries)(?: list)?\b|^what do i need to (?:buy|get)(?: at the store)?\b"), ShoppingAsync),
            new("reminder.cancel", R(@"^(?:cancel|delete|remove|clear|stop|forget|get rid of)\b.*\breminders?\b"), CancelReminderAsync),
            new("music", R(@"^(?:what|which)(?:'?s| is)? (?:the )?(?:song|music|track|tune)(?: is)?(?: that'?s| that is)? (?:playing|on)\b|^what'?s playing\b|^whats playing\b|^what is playing\b|^what am i (?:listening|playing) to\b|^now playing$|\bname (?:of )?(?:this|that|the) song\b|^who (?:sings|made|is singing) (?:this|that)(?: song)?\b|^what song is (?:this|that|on)\b|^show (?:me )?(?:the )?(?:music|song|now playing)(?: card| player)?$"), MusicAsync),
            new("screen.read", R(@"\b(?:what'?s|whats|what is|read|look at|see|tell me what'?s|describe|summari[sz]e|copy|scan)\b.*\b(?:on|in) (?:my|the) screen\b|\bsolve .* on (?:my|the) screen\b|^what am i looking at\b|^can you see my screen\b|^read (?:my|the) screen\b|^read (?:this|that|it) (?:out )?(?:to me|for me)$"), ScreenReadAsync),
            new("screentime", R(@"\bscreen ?time\b|\bhow (?:long|much time) (?:have|did) i (?:been )?(?:on|using|use|used|play|played|playing|spen[dt](?: on)?) (?:my |the )?(?!for\b|with\b|outside\b)\w+|\bhow long have i been on (?:my |the )?(?:computer|pc|laptop)\b|^(?:limit|cap) .+ (?:to|at) (?:\d+|an?|one|two|three|four|five|half) .*(?:hours?|minutes?|mins?)|\btime limits?\b|\bapp limits?\b"), ScreenTimeAsync),
            new("translate", R(@"\btranslat|\bhow (?:do|would|can|should) (?:you|i|we) say\b|\bhow to say\b|^say .+ in [a-z]+$|^what (?:does|do) .+ mean\b|\b(?:" + LangPattern + @") (?:word|phrase|way to say|for)\b|\bin (?:" + LangPattern + @")\??$|\bin english\??$"), TranslateAsync),
            new("update", R(@"^(?:(?:please |can you |could you |go |hey ace,? )?(?:check for|look for|get|install|download|find) (?:any |an |the |a )?(?:new )?(?:ace )?(?:updates?|new version|newest version|latest version)|^(?:update|upgrade) (?:yourself|ace|nimbus|you)\b|^(?:are there|is there|any) (?:any )?(?:new )?(?:updates?|new versions?)\b|^(?:what|which) version (?:are you|is (?:this|ace|nimbus))\b|^what'?s your version\b|^are you up to date\b|^(?:is )?(?:ace|nimbus) up to date\b)"), UpdateAsync),
            new("whatsnew", R(@"^(?:what'?s|whats|what is) new(?: (?:in|with) (?:this|the|ace|you|nimbus|your)(?: (?:update|version|ace))?| (?:in|with) (?:the |this )?(?:update|version|new version|latest version))?\??$|^(?:show|tell) me (?:the |your )?(?:update|patch|release) notes\??$|^(?:the )?(?:patch|update|release) notes\??$|^(?:the )?change ?log\??$|^what (?:changed|did you change|got updated|did the update (?:change|add))(?: in (?:this|the|your) (?:update|version))?\??$|^what'?s (?:in|new in) (?:the|this|your) (?:update|version|new version)\??$"), WhatsNewAsync),
            new("update.beta", R(@"^(?:please |can you |could you )?(?:turn on|turn off|switch on|switch off|enable|disable|join|leave|quit|get|stop getting|opt (?:in|out)(?: of| to| for)?|sign me up for|i want)(?: the| ace| ace'?s)? beta(?: updates?| versions?| channel| builds?| testing| program)?$|^(?:use|switch to|go back to)(?: the)? (?:stable|normal|regular)(?: updates?| versions?| channel)$|^(?:am i|are you|is ace) (?:on|in|getting) (?:the )?beta(?: updates?| channel| versions?)?\??$"), BetaAsync),
            new("script.explain", R(@"^(?:can you |could you |would you |will you |please |pls |now )?(?:explain|break down|walk me through|go through|go over|help me understand)\b|^what does (?:this|that|the|it|each|every|line \d+|this line|that line)\b.*\bdo\??$|^how does (?:this|that|the|it)\b.*\bwork\??$|^what (?:is|does|'?s) (?:on |in |happening (?:on|in) )?line \d+\b|^i don'?t (?:get|understand) (?:this|that|it|the)\b(?!.*\b(?:joke|question|answer|recipe)\b)"), ScriptExplainAsync),
            new("script", R(@"\b(?:scripts?|lua|luau|cpp|arduino|unreal|sketch|code|coding)\b|\bc\+\+|\broblox\b"), ScriptAsync),
        };

        private static readonly string LangPattern = string.Join("|", Translator.Languages.Keys.Where(k => !k.Equals("english", StringComparison.OrdinalIgnoreCase)).OrderByDescending(k => k.Length).Select(Regex.Escape));

        private static readonly Regex YesWord = new(@"^(?:yes|yeah|yep|yup|ya|yea|sure|ok(?:ay)?|please|go on|go ahead|tell me|why not|alright|of course|definitely|i do|i'?d like that|sounds good|uh huh|y)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // ===================================================== Hooks into the main flow

        /// <summary>Checked before unit conversions and math: homework problems and money.</summary>
        private async Task<AceAnswer?> BeforeMathAsync(Turn t)
        {
            if (MathSteps.Wants(t.Text))
            {
                var work = MathSteps.Solve(t.Text);
                if (work != null) { Routes.Add("homework"); return Done(HomeworkAnswer(work)); }
            }
            var money = await CurrencyAsync(t).ConfigureAwait(false);
            if (money != null) { Routes.Add("currency"); return Done(money); }
            return null;
        }

        /// <summary>Checked after math: "convert 10 kilograms" (no unit to convert to) shows it in every unit.</summary>
        private AceAnswer? ConvertAllAnswer(Turn t)
        {
            var all = Units.ConvertAll(t.Text);
            if (all == null) return null;
            var (amount, from, rows) = all.Value;
            Step("Converting to every unit", from);
            var top = rows.Take(3).Select(r => r.Value + " " + r.Unit).ToList();
            var a = AceAnswer.Say($"{amount} {from} is {Composer.List(top, "or")}.", "convert");
            a.Cards.Add(new ConvertCard { Title = $"{amount} {from}", Rows = rows });
            Routes.Add("convert.all");
            return Done(a);
        }

        /// <summary>After each answer: offer a related question to keep the topic going ("What currency does France use?").</summary>
        private void OfferFollowUp(AceAnswer a)
        {
            Talk.OfferedFollowUp = "";
            if (a.Source != "bank" || a.BankId.Length == 0) return;
            var entry = Bank.All().FirstOrDefault(e => e.Id == a.BankId);
            if (entry == null || entry.Followups.Count == 0) { Talk.OfferedFollowUp = ""; return; }
            foreach (var f in entry.Followups)
            {
                // Skip ones that just ask the same thing backwards ("Paris is the capital of which country?").
                if (Talk.LastAnswerName.Length > 0 && f.Contains(Talk.LastAnswerName, StringComparison.OrdinalIgnoreCase)) continue;
                var hit = Bank.Search(f, 1).FirstOrDefault();
                if (hit == null || hit.Entry.Id == entry.Id || hit.Score < 0.6) continue;
                if (Talk.Recent.Any(r => r.Ace.Contains(hit.Entry.Answers[0], StringComparison.Ordinal))) continue;
                a.FollowUp = f;
                if (!a.Suggestions.Contains(f)) a.Suggestions.Insert(0, f);
                a.Speak = (string.IsNullOrWhiteSpace(a.Speak) ? Composer.ForSpeech(a.Text) : a.Speak) + " Want to know " + char.ToLowerInvariant(f[0]) + f[1..].TrimEnd('?') + "?";
                Talk.OfferedFollowUp = f;
                Talk.OfferedAt = DateTime.Now;
                return;
            }
            Talk.OfferedFollowUp = "";
        }

        /// <summary>"Yes" after Ace offered a follow-up question answers it.</summary>
        private AceAnswer? TakeOfferedFollowUp(Turn t)
        {
            if (Talk.OfferedFollowUp.Length == 0 || DateTime.Now - Talk.OfferedAt > TimeSpan.FromMinutes(5)) return null;
            string l = t.Lower.Trim().TrimEnd('.', '!', '?');
            if (l.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 5 || !YesWord.IsMatch(l)) return null;
            string q = Talk.OfferedFollowUp;
            Talk.OfferedFollowUp = "";
            var hit = Bank.Search(q, 1).FirstOrDefault();
            if (hit == null) return null;
            Step("You wanted to know", q);
            NoteSubject(q);
            var a = BankAnswer(hit, Math.Max(0.8, hit.Score));
            return a;
        }

        // ===================================================== Homework

        private AceAnswer HomeworkAnswer(MathWork work)
        {
            Step("Working it out step by step", work.Problem);
            var a = AceAnswer.Say(work.Say + " Here's how:", "homework");
            a.Speak = work.Say + " I put the steps on screen.";
            a.Cards.Add(new StepsCard { Work = work });
            a.Mood = "happy";
            if (work.Answer.Length > 0 && double.TryParse(Regex.Replace(work.Answer, @"[^\d.\-]", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && Regex.IsMatch(work.Answer, @"^-?[\d,]+(\.\d+)?$"))
                Talk.LastNumber = v;
            return a;
        }

        private Task<AceAnswer?> HomeworkStartAsync(Turn t)
        {
            var a = AceAnswer.Say(Composer.Pick("homework.start", new[]
            {
                "Sure! What's the problem? Type or say it, like 2x + 3 = 11 or 3/4 + 1/2.",
                "Let's do it! What's the problem? Something like 5x - 7 = 2x + 8 works.",
                "Happy to help. What's the first problem?",
            }), "homework");
            Talk.PendingFollow = HomeworkProblemAsync;
            Talk.PendingFollowAt = DateTime.Now;
            a.Suggestions.AddRange(new[] { "2x + 3 = 11", "1/2 + 1/3", "Is 91 a prime number?" });
            return Task.FromResult<AceAnswer?>(a);
        }

        private Task<AceAnswer?> HomeworkProblemAsync(Turn t)
        {
            if (!Regex.IsMatch(t.Lower, @"\d")) return Task.FromResult<AceAnswer?>(null);
            var work = MathSteps.Solve(t.Text);
            if (work == null)
            {
                string? expr = Calculator.ToExpression(t.Text);
                if (expr != null && Calculator.Evaluate(expr) is double v)
                    return Task.FromResult<AceAnswer?>(AceAnswer.Say($"That's {Calculator.Format(v)}.", "homework"));
                return Task.FromResult<AceAnswer?>(AceAnswer.Say("Hmm, I can't work that one out step by step yet. I can do equations, fractions, percents, long division, factors and area. Try writing it like 3x + 5 = 20.", "homework", 0.5));
            }
            var a = HomeworkAnswer(work);
            // Keep going: the next message can be the next problem.
            Talk.PendingFollow = HomeworkProblemAsync;
            Talk.PendingFollowAt = DateTime.Now;
            return Task.FromResult<AceAnswer?>(a);
        }

        // ===================================================== Translate

        private async Task<AceAnswer?> TranslateAsync(Turn t)
        {
            var ask = Translator.Parse(t.Text);
            if (ask == null) return null;
            if (ask.ToEnglish)
            {
                var back = Translator.Back(ask.Phrase);
                if (back == null)
                {
                    // "What does photosynthesis mean" is for the dictionary, not the translator.
                    if (!t.Has(@"^translate\b|\bin english\??$")) return null;
                    var web = await Translator.WebAsync(ask.Phrase, "autodetect", "en", t.Ct).ConfigureAwait(false);
                    if (web != null && !web.Equals(ask.Phrase, StringComparison.OrdinalIgnoreCase))
                        return TranslateAnswer(ask.Phrase, web, "", $"\"{Composer.Capital(ask.Phrase)}\" means \"{web}\" in English.", offline: false);
                    return AceAnswer.Say($"I don't know what \"{ask.Phrase}\" means yet{(Web.WebClient.Disabled ? "" : ", and I couldn't find it online")}.", "translate", 0.4);
                }
                Step("Translating to English", back.Value.Language);
                return TranslateAnswer(ask.Phrase, back.Value.English, back.Value.Language,
                    $"\"{Composer.Capital(ask.Phrase)}\" means \"{back.Value.English}\" in {back.Value.Language}.", offline: true);
            }
            if (!Translator.Languages.TryGetValue(ask.LangWord, out var lang))
            {
                string some = Composer.List(new[] { "Spanish", "French", "German", "Italian", "Japanese" }, "and");
                return AceAnswer.Say($"Sorry, I don't know {Composer.Capital(ask.LangWord)} yet. I can do lots of languages, like {some}.", "translate", 0.4);
            }
            if (lang.Code == "en") return AceAnswer.Say($"That's already English: \"{ask.Phrase}\".", "translate");
            string? said = Translator.Offline(ask.Phrase, lang.Code);
            bool offline = said != null;
            if (said == null)
            {
                Step("Asking a translator online", lang.Name);
                said = await Translator.WebAsync(ask.Phrase, "en", lang.Code, t.Ct).ConfigureAwait(false);
            }
            else Step("Found it in my phrase book", lang.Name);
            if (said == null)
                return AceAnswer.Say($"I don't know how to say that in {lang.Name} without the internet. I know about a hundred common phrases offline, like \"hello\", \"thank you\" and \"where is the bathroom\".", "translate", 0.4);
            return TranslateAnswer(ask.Phrase, said, lang.Name, $"In {lang.Name}, \"{ask.Phrase}\" is \"{said}\".", offline);
        }

        private AceAnswer TranslateAnswer(string from, string to, string language, string text, bool offline)
        {
            var a = AceAnswer.Say(text, "translate");
            a.Cards.Add(new TranslateCard { From = from, To = to, Language = language, Offline = offline });
            return a;
        }

        // ===================================================== Trivia

        private Task<AceAnswer?> TriviaAsync(Turn t)
        {
            if (t.Has(@"^(?:stop|end|quit)\b")) return Task.FromResult<AceAnswer?>(EndTrivia());
            string cat = Trivia.CategoryOf(t.Text);
            var game = new TriviaGame(cat);
            Talk.Trivia = game;
            string first = game.Next();
            game.Marked = false;
            string lead = cat.Length > 0
                ? $"Let's play {cat} trivia! I'll ask 10 questions."
                : Composer.Pick("trivia.start", new[] { "Let's play trivia! I'll ask 10 questions.", "Trivia time! 10 questions, here we go.", "Ooh, trivia! Ten questions. Ready?" });
            Talk.PendingFollow = TriviaAnswerAsync;
            Talk.PendingFollowAt = DateTime.Now;
            var a = AceAnswer.Say(lead + " " + first, "trivia");
            a.Speak = lead + " " + first;
            a.Cards.Add(new TriviaCard { Game = game });
            a.Mood = "happy";
            return Task.FromResult<AceAnswer?>(a);
        }

        private AceAnswer EndTrivia()
        {
            var game = Talk.Trivia;
            Talk.Trivia = null;
            if (game == null) return AceAnswer.Say("We're not playing trivia right now. Say \"let's play trivia\" to start!", "trivia", 0.6);
            game.End();
            int answered = game.Asked - (game.Marked ? 0 : 1);
            string score = answered <= 0 ? "We'll play again sometime!" : $"You got {game.Score} out of {answered}.";
            if (answered > 0 && !game.Saved)
            {
                game.Saved = true;
                var rec = TriviaScores.Record(game.Category, game.Score, answered, game.BestStreak, full: false);
                if (rec.NewStreak) score += $" And that's a new best streak: {game.BestStreak} right in a row!";
            }
            var stopped = AceAnswer.Say("Good game! " + score, "trivia");
            stopped.Cards.Add(new TriviaScoresCard());
            return stopped;
        }

        private Task<AceAnswer?> TriviaAnswerAsync(Turn t)
        {
            var game = Talk.Trivia;
            if (game == null || game.Over) return Task.FromResult<AceAnswer?>(null);
            string l = t.Lower.Trim().TrimEnd('.', '!', '?');
            void KeepGoing() { Talk.PendingFollow = TriviaAnswerAsync; Talk.PendingFollowAt = DateTime.Now; }
            if (Regex.IsMatch(l, @"^(stop|quit|end|i'?m done|done|no more|enough|that'?s enough|stop (the )?(game|trivia|quiz)|end (the )?(game|trivia|quiz)|quit (the )?(game|trivia|quiz)|let'?s stop)$"))
                return Task.FromResult<AceAnswer?>(EndTrivia());
            if (Regex.IsMatch(l, @"^(?:what'?s|whats|what is|tell me) (?:my|the) score\b|^score$|^how am i doing\b|^how many (?:have i got|did i get) right\b"))
            {
                KeepGoing();
                var best = TriviaScores.Best(game.Category);
                string sc = $"You've got {game.ScoreText} so far" + (game.Streak >= 2 ? $", with {game.Streak} right in a row" : "") + "." + (best != null ? $" Your high score is {best.Score} out of {best.OutOf}." : "");
                return Task.FromResult<AceAnswer?>(AceAnswer.Say(sc + " " + game.AskText(), "trivia"));
            }
            if (Regex.IsMatch(l, @"^(repeat|say (it|that) again|what was the question|again)$"))
            {
                KeepGoing();
                return Task.FromResult<AceAnswer?>(AceAnswer.Say(game.AskText(), "trivia"));
            }
            int? choice = game.ChoiceOf(t.Text);
            if (choice == null) { KeepGoing(); return Task.FromResult<AceAnswer?>(null); }
            var (right, said) = game.Mark(choice.Value);
            game.Marked = true;
            Step(right ? "Right answer" : "Not the answer", game.Current?.Answer ?? "");
            string next;
            if (game.Asked >= 10 || !game.HasMore)
            {
                Talk.Trivia = null;
                game.End();
                int s = game.Score, n = game.Asked;
                string cheer = s == n ? "A perfect score! You're a genius!" : s >= n * 0.7 ? "Awesome job!" : s >= n * 0.4 ? "Nice work!" : "Good try! You'll get more next time.";
                next = $" That's the end! You got {s} out of {n}. {cheer}";
                if (!game.Saved)
                {
                    game.Saved = true;
                    var rec = TriviaScores.Record(game.Category, s, n, game.BestStreak, full: true);
                    string kind = game.Category.Length > 0 ? game.Category + " trivia" : "trivia";
                    if (rec.NewBest && rec.OldBest != null) next += $" New {kind} high score! Your old best was {rec.OldBest.Score} out of {rec.OldBest.OutOf}.";
                    else if (rec.FirstBest) next += $" That's your first {kind} high score. Try to beat it next time!";
                    else if (rec.OldBest != null) next += $" Your {kind} high score is still {rec.OldBest.Score} out of {rec.OldBest.OutOf}.";
                    if (rec.NewStreak) next += $" Plus a new best streak: {game.BestStreak} right in a row!";
                }
                var done = AceAnswer.Say(said + next, "trivia");
                done.Mood = s >= n * 0.7 ? "love" : "happy";
                done.Suggestions.Add("Let's play trivia again");
                done.Suggestions.Add("Show my trivia high scores");
                done.Cards.Add(new TriviaScoresCard());
                return Task.FromResult<AceAnswer?>(done);
            }
            next = " " + game.Next();
            game.Marked = false;
            KeepGoing();
            var a = AceAnswer.Say(said + next, "trivia");
            a.Mood = right ? "love" : "happy";
            a.Cards.Add(new TriviaCard { Game = game });
            return Task.FromResult<AceAnswer?>(a);
        }

        // ===================================================== Updates

        private async Task<AceAnswer?> UpdateAsync(Turn t)
        {
            string version = AutoUpdater.CurrentText;
            if (t.Has(@"\bversion\b") && !t.Has(@"\b(?:new|newest|latest|check|update)\b"))
            {
                string from = AutoUpdater.JustUpdatedFrom;
                return AceAnswer.Say($"I'm Ace version {version}." + (from.Length > 0 ? $" I just updated from {from}." : "") + (Config.AutoUpdate ? " I update myself from GitHub." : " Auto-update is off in Settings."), "update");
            }
            if (CheckForUpdates == null) return AceAnswer.Say($"I'm on version {version}. I can't check for updates right now.", "update", 0.6);
            Step("Checking GitHub for a new version", AutoUpdater.Repo);
            string said = await CheckForUpdates(CancellationToken.None).ConfigureAwait(false);
            return AceAnswer.Say(said, "update");
        }

        private Task<AceAnswer?> WhatsNewAsync(Turn t)
        {
            string from = AutoUpdater.JustUpdatedFrom;
            return Task.FromResult<AceAnswer?>(WhatsNewAnswer(from, AutoUpdater.CurrentText));
        }

        /// <summary>
        /// "What's new" as an answer with a card: every version since from (after an update), or just this version's
        /// notes when from is empty.
        /// </summary>
        public AceAnswer WhatsNewAnswer(string from, string current)
        {
            var groups = from.Length > 0
                ? WhatsNew.Between(from, current)
                : new List<(Version Version, string Name, List<WhatsNewNote> Notes)>();
            if (groups.Count == 0)
            {
                var notes = WhatsNew.For(current);
                if (notes.Count == 0) return AceAnswer.Say($"I'm on version {current}. I don't have any notes for this version.", "update", 0.8);
                groups.Add((AutoUpdater.ParseVersion(current)!, current, notes));
                from = "";
            }
            string text = from.Length > 0
                ? $"I just updated from {from} to {current}! Here's what's new."
                : $"Here's what's new in Ace {current}.";
            var a = AceAnswer.Say(text, "update");
            foreach (var g in groups.Take(3))
            {
                var card = new ResultsCard { Title = "New in " + g.Name };
                foreach (var n in g.Notes.Take(10)) card.Items.Add(new ResultItem { Title = n.Title, Subtitle = n.Text });
                a.Cards.Add(card);
            }
            var first = groups[0].Notes.Take(2).Select(n => n.Title).Where(x => x.Length > 0).ToList();
            a.Speak = text + (first.Count > 0 ? " " + string.Join(". ", first) + "." : "");
            a.Mood = "love";
            return a;
        }

        private Task<AceAnswer?> BetaAsync(Turn t)
        {
            if (t.Has(@"^(?:am i|are you|is ace)\b"))
                return Task.FromResult<AceAnswer?>(AceAnswer.Say(Config.BetaChannel
                    ? "Yes, you get beta updates. They come out first but might have bugs. Say \"turn off beta\" to go back."
                    : "No, you get the normal updates. Say \"turn on beta updates\" to get new stuff first.", "update"));
            bool on = !t.Has(@"\b(?:turn off|switch off|disable|leave|quit|stop getting|opt out|stable|normal|regular)\b");
            if (Config.BetaChannel == on)
                return Task.FromResult<AceAnswer?>(AceAnswer.Say(on ? "Beta updates are already on." : "Beta updates are already off.", "update"));
            Config.BetaChannel = on;
            Config.Save();
            Step(on ? "Turned on beta updates" : "Turned off beta updates");
            return Task.FromResult<AceAnswer?>(AceAnswer.Say(on
                ? "Done! You'll get beta versions as soon as they're out. They might have a few bugs, but I keep a backup in case one breaks."
                : "Okay, back to the normal updates. You'll keep this version until the next normal one comes out.", "update"));
        }

        // ===================================================== Trivia high scores

        private Task<AceAnswer?> TriviaScoresAsync(Turn t)
        {
            if (t.Has(@"^(?:reset|clear|delete|erase|wipe)\b"))
            {
                int n = TriviaScores.Clear();
                return Task.FromResult<AceAnswer?>(AceAnswer.Say(n == 0
                    ? "There aren't any trivia scores to clear yet."
                    : $"Cleared {Composer.Plural(n, "trivia game", "trivia games")} from your scores. Fresh start!", "trivia.scores"));
            }
            string cat = Trivia.CategoryOf(Regex.Replace(t.Text, @"\b(?:trivia|quiz) games?\b|\bgames? (?:of trivia|played)\b|\bhow many (?:trivia |quiz )?games\b", " ", RegexOptions.IgnoreCase));
            AceAnswer a;
            if (TriviaScores.Games == 0)
            {
                a = AceAnswer.Say("You haven't played any trivia yet, so there's no high score. Say \"let's play trivia\" to set your first one!", "trivia.scores");
                a.Suggestions.Add("Let's play trivia");
                return Task.FromResult<AceAnswer?>(a);
            }
            string text;
            if (cat.Length > 0)
            {
                var best = TriviaScores.Best(cat);
                text = best == null
                    ? $"You haven't finished {(Regex.IsMatch(cat, "^[aeiou]") ? "an" : "a")} {cat} trivia game yet. Say \"quiz me on {cat}\" to set a high score!"
                    : $"Your {cat} trivia high score is {best.Score} out of {best.OutOf}, set {DayPhrase(best.When)}.";
            }
            else
            {
                var bests = TriviaScores.Bests();
                if (bests.Count == 0) text = "You haven't finished a whole game yet. Answer all 10 questions to set a high score!";
                else
                {
                    var top = bests[0];
                    string where = top.Category.Length == 0 ? "mixed trivia" : top.Category + " trivia";
                    text = $"Your best is {top.Score} out of {top.OutOf} in {where}.";
                    if (bests.Count > 1) text += $" You have high scores in {Composer.Plural(bests.Count, "category", "categories")}.";
                }
            }
            int streak = TriviaScores.BestStreak;
            if (streak >= 2) text += $" Best streak: {streak} right in a row.";
            text += $" You've played {Composer.Plural(TriviaScores.Games, "game", "games")}.";
            a = AceAnswer.Say(text, "trivia.scores");
            a.Cards.Add(new TriviaScoresCard());
            a.Suggestions.Add(cat.Length > 0 ? $"Quiz me on {cat}" : "Let's play trivia");
            return Task.FromResult<AceAnswer?>(a);
        }

        /// <summary>"today", "yesterday", "on Monday" or "on March 3".</summary>
        public static string DayPhrase(DateTime when)
        {
            var d = (DateTime.Today - when.Date).Days;
            return d <= 0 ? "today" : d == 1 ? "yesterday" : d < 7 ? "on " + when.ToString("dddd", CultureInfo.InvariantCulture) : "on " + when.ToString("MMMM d", CultureInfo.InvariantCulture);
        }

        // ===================================================== Explain a script

        private Task<AceAnswer?> ScriptExplainAsync(Turn t)
        {
            if (ScriptLibrary.All.Count == 0) return Task.FromResult<AceAnswer?>(null);
            string l = t.Lower.Trim().TrimEnd('.', '!', '?');
            var lineM = Regex.Match(l, @"\bline (\d+)\b");
            bool mentionsCode = Regex.IsMatch(l, @"\b(?:scripts?|code|sketch|program|lua|luau|arduino|unreal|line \d+|line by line|each line|every line|this line|that line)\b|c\+\+");
            // What's left once the "explain" words are gone names the script ("explain the kill brick script").
            string rest = Regex.Replace(l, @"^(?:can you |could you |would you |will you |please |pls |now )?(?:explain|break down|walk me through|go through|go over|help me understand|what does|how does|what is|what'?s|i don'?t (?:get|understand))\b", " ");
            rest = Regex.Replace(rest, @"\b(?:line by line|step by step|each line|every line|this line|that line|line \d+|to me|for me|please|how it works|what it does|do|does|work|works|working|on|in|of|happening|the|this|that|it|its|a|an|whole|my|your|script|scripts|code|sketch|program|lua|roblox|c\+\+|cpp|arduino|unreal|again|more|simply|simple|like i'?m \d+|like im \d+|so i understand|mean|means)\b", " ");
            rest = Regex.Replace(rest, @"c\+\+|[^a-z0-9 ]", " ");
            rest = Regex.Replace(rest, @"\s+", " ").Trim();

            ScriptEntry? s = null;
            if (rest.Length > 0)
            {
                var hit = ScriptLibrary.Search(rest).FirstOrDefault();
                if (hit.Script != null && hit.Score >= (mentionsCode ? 0.5 : 0.75)) s = hit.Script;
            }
            bool justShown = Talk.LastScript != null && Talk.LastScriptAnswer != null && ReferenceEquals(Talk.LastAnswer, Talk.LastScriptAnswer);
            if (s == null && Talk.LastScript != null && (mentionsCode || (justShown && rest.Length == 0))) s = Talk.LastScript;
            if (s == null)
            {
                // "explain how gravity works" isn't about code.
                if (!mentionsCode) return Task.FromResult<AceAnswer?>(null);
                var ask = AceAnswer.Say("Which script should I explain? Pick one below, or say something like \"explain the kill brick script\".", "script.explain", 0.6);
                ask.Cards.Add(new ScriptListCard { Title = "Scripts I can explain", Scripts = ScriptLibrary.All.Where(x => x.Lang == "lua").Take(4).Concat(ScriptLibrary.All.Where(x => x.Lang != "lua").Take(2)).ToList() });
                return Task.FromResult<AceAnswer?>(ask);
            }
            return Task.FromResult<AceAnswer?>(ExplainAnswer(s, lineM.Success ? int.Parse(lineM.Groups[1].Value) : 0));
        }

        public AceAnswer ExplainAnswer(ScriptEntry s, int line = 0)
        {
            var steps = ScriptExplainer.Explain(s);
            Talk.LastScript = s;
            string name = s.Name.ToLowerInvariant();
            Step("Read the script part by part", $"{s.Name} · {Composer.Plural(steps.Count, "part", "parts")}");
            AceAnswer a;
            if (line > 0)
            {
                int count = s.Code.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Length;
                if (line > count)
                {
                    a = AceAnswer.Say($"The {name} script only has {count} lines. Try a line from 1 to {count}.", "script.explain", 0.7);
                    Talk.LastScriptAnswer = a;
                    return a;
                }
                var step = steps.FirstOrDefault(x => line >= x.FromLine && line <= x.ToLine);
                string codeLine = s.Code.Replace("\r\n", "\n").Split('\n')[line - 1].Trim();
                string plain = step?.Plain ?? "";
                if (codeLine.Length == 0) a = AceAnswer.Say($"Line {line} is just an empty line, to keep the code easy to read.", "script.explain");
                else if (step == null && steps.Count > 0 && line < steps[0].FromLine)
                    a = AceAnswer.Say($"Line {line} is part of the note at the top of the script. The computer skips it. It's there to tell you what the script does and how to use it.", "script.explain");
                else if (step == null || Regex.IsMatch(codeLine, s.Lang == "lua" ? @"^--" : @"^//|^/\*|^\*"))
                    a = AceAnswer.Say($"Line {line} is a comment, a note for people. The computer skips it.", "script.explain");
                else if (Regex.IsMatch(codeLine, @"^(?:end\)?|\}\)?;?)$"))
                    a = AceAnswer.Say($"Line {line} ({codeLine}) closes the part that starts on line {step?.FromLine ?? line}. {plain}", "script.explain");
                else a = AceAnswer.Say($"Line {line} ({(codeLine.Length > 60 ? codeLine[..57] + "…" : codeLine)}) is part of this: {plain}", "script.explain");
                a.Cards.Add(new ExplainCard { Script = s, Steps = steps, Focus = line });
            }
            else
            {
                a = AceAnswer.Say($"Here's how the {name} script works, part by part. {(steps.Count > 0 ? "First: " + steps[0].Plain : "")}", "script.explain");
                a.Speak = $"Here's how the {name} script works. " + string.Join(" Then, ", steps.Take(3).Select(x => x.Plain)) + (steps.Count > 3 ? " The rest is on screen." : "");
                a.Cards.Add(new ExplainCard { Script = s, Steps = steps });
            }
            a.Suggestions.Add("Show me the code");
            if (steps.Count > 1) a.Suggestions.Add($"What does line {steps[Math.Min(1, steps.Count - 1)].FromLine} do?");
            Talk.LastScriptAnswer = a;
            return a;
        }

        // ===================================================== 20 questions

        private Task<AceAnswer?> TwentyQAsync(Turn t)
        {
            bool youGuess = t.Has(@"\byou (think of|pick|choose)\b|\bi(?:'ll| will| want to| wanna)? guess\b|\byou think\b");
            var game = youGuess ? TwentyQuestions.YouGuessing() : TwentyQuestions.AceGuessing();
            Talk.TwentyQ = game;
            AceAnswer a;
            if (youGuess)
            {
                a = AceAnswer.Say("Okay, I've thought of something! Ask me yes or no questions, like \"is it an animal?\" You get 20. Say \"hint\" if you're stuck.", "twentyq");
                Talk.PendingFollow = TwentyQYouAsync;
            }
            else
            {
                string first = game.NextMove();
                a = AceAnswer.Say("Think of something: an animal, a food, a thing, anything! I'll guess it with yes or no questions. " + first, "twentyq");
                Talk.PendingFollow = TwentyQAceAsync;
                a.Suggestions.AddRange(new[] { "Yes", "No", "Sometimes" });
            }
            Talk.PendingFollowAt = DateTime.Now;
            a.Cards.Add(new TwentyQCard { Game = game });
            return Task.FromResult<AceAnswer?>(a);
        }

        private static readonly Regex StopGame = new(@"^(stop|quit|end|i'?m done|no more|stop (the )?game|end (the )?game|quit (the )?game|let'?s stop)$", RegexOptions.Compiled);

        private Task<AceAnswer?> TwentyQAceAsync(Turn t)
        {
            var game = Talk.TwentyQ;
            if (game == null) return Task.FromResult<AceAnswer?>(null);
            string l = t.Lower.Trim().TrimEnd('.', '!', '?');
            if (StopGame.IsMatch(l)) { Talk.TwentyQ = null; return Task.FromResult<AceAnswer?>(AceAnswer.Say("Okay, game over! That was fun.", "twentyq")); }
            if (game.Over)
            {
                // Ace gave up and asked what it was.
                if (l.Length == 0 || l.Split(' ').Length > 6 || Regex.IsMatch(l, @"^(no|nope|nothing|never ?mind)$")) { Talk.TwentyQ = null; return Task.FromResult<AceAnswer?>(null); }
                Talk.TwentyQ = null;
                return Task.FromResult<AceAnswer?>(AceAnswer.Say(game.Learn(t.Text), "twentyq"));
            }
            double? v = TwentyQuestions.YesNo(l);
            if (v == null)
            {
                Talk.PendingFollow = TwentyQAceAsync; Talk.PendingFollowAt = DateTime.Now;
                return Task.FromResult<AceAnswer?>(null);
            }
            string say = game.Answer(v.Value);
            if (!game.Over || say.Contains("What were you thinking of", StringComparison.Ordinal)) { Talk.PendingFollow = TwentyQAceAsync; Talk.PendingFollowAt = DateTime.Now; }
            else Talk.TwentyQ = null;
            var a = AceAnswer.Say(say, "twentyq");
            if (!game.Over) a.Suggestions.AddRange(new[] { "Yes", "No", "Sometimes" });
            a.Cards.Add(new TwentyQCard { Game = game });
            return Task.FromResult<AceAnswer?>(a);
        }

        private Task<AceAnswer?> TwentyQYouAsync(Turn t)
        {
            var game = Talk.TwentyQ;
            if (game == null || game.Over) return Task.FromResult<AceAnswer?>(null);
            string l = t.Lower.Trim().TrimEnd('.', '!', '?');
            void KeepGoing() { Talk.PendingFollow = TwentyQYouAsync; Talk.PendingFollowAt = DateTime.Now; }
            if (StopGame.IsMatch(l) || Regex.IsMatch(l, @"^(i give up|give up|tell me|what is it|what was it)$"))
            {
                Talk.TwentyQ = null;
                return Task.FromResult<AceAnswer?>(AceAnswer.Say($"It was {TwentyQuestions.Article(game.Secret!.Name)}! Good game.", "twentyq"));
            }
            if (Regex.IsMatch(l, @"^(hint|give me a hint|a hint|clue|give me a clue)$")) { KeepGoing(); return Task.FromResult<AceAnswer?>(AceAnswer.Say(game.Hint(), "twentyq")); }
            string? said = game.AnswerQuestion(t.Text, out bool won);
            if (said == null)
            {
                KeepGoing();
                if (!l.EndsWith("?") && !Regex.IsMatch(l, @"^(is|does|do|can|are|would|will|has|have)\b")) return Task.FromResult<AceAnswer?>(null);
                return Task.FromResult<AceAnswer?>(AceAnswer.Say("Hmm, I can't answer that one. Try asking about what it is, like \"does it have fur?\" or \"can you eat it?\"", "twentyq", 0.6));
            }
            if (game.Over) Talk.TwentyQ = null; else KeepGoing();
            var a = AceAnswer.Say(said, "twentyq");
            if (won) a.Mood = "love";
            a.Cards.Add(new TwentyQCard { Game = game });
            return Task.FromResult<AceAnswer?>(a);
        }

        // ===================================================== Memory you can see

        /// <summary>Things you told Ace about yourself (newest first).</summary>
        public List<QaEntry> PersonalFacts() =>
            Bank.LearnedEntries().Where(e => e.Source is "you told me" or "you corrected me" || e.Topic == "personal").OrderByDescending(e => e.Learned).ToList();

        public MemoryCard NewMemoryCard() => new()
        {
            Items = () => PersonalFacts().Select(e => (e.Id, e.Answers.FirstOrDefault() ?? "")).ToList(),
            Forget = id => Bank.Forget(id),
        };

        private Task<AceAnswer?> MemoryListAsync(Turn t)
        {
            var facts = PersonalFacts();
            AceAnswer a;
            if (facts.Count == 0)
                a = AceAnswer.Say("You haven't told me anything about yourself yet. Try \"remember that my favorite color is green\".", "memory", 0.6);
            else
            {
                var some = facts.Take(5).Select(f => f.Answers[0].TrimEnd('.')).ToList();
                string more = facts.Count > 5 ? $" And {Composer.Plural(facts.Count - 5, "more thing", "more things")}." : "";
                a = AceAnswer.Say($"Here's what you've told me: {string.Join(". ", some)}.{more}", "memory");
                a.Speak = facts.Count == 1 ? some[0] + "." : $"I remember {Composer.Plural(facts.Count, "thing", "things")} about you. They're on screen, and you can delete any of them.";
            }
            a.Cards.Add(NewMemoryCard());
            a.Mood = "love";
            return Task.FromResult<AceAnswer?>(a);
        }

        private Task<AceAnswer?> MemoryClearAsync(Turn t)
        {
            var facts = PersonalFacts();
            var learned = Bank.LearnedEntries();
            if (learned.Count == 0) return Task.FromResult<AceAnswer?>(AceAnswer.Say("My memory's already empty.", "memory"));
            var a = AceAnswer.Say($"Forget everything you've told me and taught me ({Composer.Plural(learned.Count, "thing", "things")})? This can't be undone.", "memory");
            a.Cards.Add(new ConfirmCard
            {
                Question = "Forget everything?",
                YesLabel = "Forget all",
                NoLabel = "Keep it",
                OnYes = () =>
                {
                    int n = 0;
                    foreach (var e in Bank.LearnedEntries()) if (Bank.Forget(e.Id)) n++;
                    Talk.LastLearnedId = "";
                    return Task.FromResult($"Done. I forgot {Composer.Plural(n, "thing", "things")}.");
                },
                OnNo = () => "Okay, I'll keep remembering.",
            });
            return Task.FromResult<AceAnswer?>(a);
        }

        // ===================================================== Notes

        private Task<AceAnswer?> NotesAsync(Turn t)
        {
            string l = t.Lower.Trim().TrimEnd('.', '!', '?');
            AceAnswer? Result(AceAnswer a) => a;

            // Delete
            if (Regex.IsMatch(l, @"^(?:clear|delete|erase|remove|get rid of) (?:all )?(?:of )?(?:my |the )?notes$"))
            {
                int n = NoteStore.Count;
                if (n == 0) return Task.FromResult<AceAnswer?>(AceAnswer.Say("You don't have any notes.", "notes"));
                var a = AceAnswer.Say($"Delete all {Composer.Plural(n, "note", "notes")}?", "notes");
                a.Cards.Add(new ConfirmCard { Question = "Delete all your notes?", YesLabel = "Delete", NoLabel = "Keep them", OnYes = () => Task.FromResult($"Deleted {Composer.Plural(NoteStore.Clear(), "note", "notes")}."), OnNo = () => "Okay, your notes are safe." });
                return Task.FromResult<AceAnswer?>(a);
            }
            var del = Regex.Match(l, @"^(?:delete|remove|erase|get rid of) (?:the |my |that |last |the last )?note(?: about (.+))?$");
            if (del.Success)
            {
                var note = del.Groups[1].Success ? NoteStore.Find(del.Groups[1].Value) : NoteStore.List().FirstOrDefault();
                if (note == null) return Task.FromResult<AceAnswer?>(AceAnswer.Say(del.Groups[1].Success ? "I couldn't find a note about that." : "You don't have any notes.", "notes", 0.6));
                NoteStore.Remove(note.Id);
                return Task.FromResult<AceAnswer?>(AceAnswer.Say($"Deleted the note \"{note.Text}\".", "notes"));
            }

            // Read
            if (Regex.IsMatch(l, @"^(?:read|show|list|open|see|check|what are|what'?s in|whats in|tell me)(?: me)?(?: all)?(?: of)? my notes|^(?:my )?notes$|^what did i (?:write down|note down|jot down)"))
            {
                var notes = NoteStore.List();
                AceAnswer a;
                if (notes.Count == 0) a = AceAnswer.Say("You don't have any notes yet. Say \"take a note\" and what to write.", "notes", 0.7);
                else
                {
                    var some = notes.Take(8).Select(n => n.Text.TrimEnd('.')).ToList();
                    a = AceAnswer.Say(notes.Count == 1 ? $"You have 1 note: {some[0]}." : $"You have {notes.Count} notes: {string.Join("; ", some)}{(notes.Count > 8 ? "; and more" : "")}.", "notes");
                }
                a.Cards.Add(new NotesCard());
                return Task.FromResult<AceAnswer?>(a);
            }

            // Add
            var add = Regex.Match(t.Text, @"^(?:take|make|add|write|create|jot)(?: down)?(?: me)?(?: a| an)?(?: quick| new)? notes?(?: that| saying| to self| about| of| for me)?[:,]?\s*(.*)$|^(?:write|jot|put) (?:this |that |it )?down(?: that)?[:,]?\s*(.*)$|^note to self[:,]?\s*(.*)$|^add (.+) to my notes$", RegexOptions.IgnoreCase);
            if (!add.Success) return Task.FromResult<AceAnswer?>(null);
            string text = new[] { 1, 2, 3, 4 }.Select(i => add.Groups[i]).Where(g => g.Success).Select(g => g.Value).FirstOrDefault() ?? "";
            text = Regex.Replace(text.Trim(), @"^(that|saying|to|:)\s+", "", RegexOptions.IgnoreCase).Trim().TrimEnd('.');
            if (text.Length == 0)
            {
                Talk.PendingFollow = t2 =>
                {
                    string said = t2.Text.Trim().TrimEnd('.');
                    if (said.Length == 0 || Regex.IsMatch(said, @"^(never ?mind|cancel|nothing|no)$", RegexOptions.IgnoreCase)) return Task.FromResult<AceAnswer?>(AceAnswer.Say("Okay, no note.", "notes"));
                    return Task.FromResult<AceAnswer?>(SavedNote(said));
                };
                Talk.PendingFollowAt = DateTime.Now;
                return Task.FromResult<AceAnswer?>(AceAnswer.Say("Sure! What should the note say?", "notes"));
            }
            return Task.FromResult<AceAnswer?>(Result(SavedNote(text)));
        }

        private AceAnswer SavedNote(string text)
        {
            var note = NoteStore.Add(Composer.Capital(text));
            Step("Saved a note", note.Text);
            var a = AceAnswer.Say(Composer.Pick("note.saved", new[] { "Saved a note: {0}.", "Got it. Note saved: {0}.", "Noted! \"{0}\"." }).Replace("{0}", note.Text.TrimEnd('.')), "notes");
            a.Cards.Add(new NotesCard());
            return a;
        }

        // ===================================================== Shopping list

        private static readonly string[] KeepTogether = { "mac and cheese", "macaroni and cheese", "salt and pepper", "peanut butter and jelly", "bread and butter", "fish and chips", "chips and salsa", "rice and beans", "pb and j", "cookies and cream", "half and half", "sweet and sour sauce" };

        private Task<AceAnswer?> ShoppingAsync(Turn t)
        {
            string l = t.Lower.Trim().TrimEnd('.', '!', '?');
            const string List = @"(?:my |the |our )?(?:shopping|grocery|groceries)(?: list)?";
            AceAnswer Card(AceAnswer a) { a.Cards.Add(new ShoppingCard()); return a; }

            // Clear
            if (Regex.IsMatch(l, @"^(?:clear|empty|delete|erase|reset|wipe|clean)(?: out| off)?(?: all)?(?: of)? " + List + "$|^(?:clear|delete|remove) (?:everything|all items) (?:from|on|off) " + List + "$"))
            {
                int n = ShoppingList.Clear();
                return Task.FromResult<AceAnswer?>(AceAnswer.Say(n == 0 ? "Your shopping list is already empty." : $"Cleared your shopping list ({Composer.Plural(n, "item", "items")}).", "shopping"));
            }
            if (Regex.IsMatch(l, @"^(?:clear|remove|delete) (?:the )?(?:checked|ticked|done|crossed off) (?:items|things|ones)"))
            {
                int n = ShoppingList.Clear(onlyChecked: true);
                return Task.FromResult<AceAnswer?>(Card(AceAnswer.Say($"Removed {Composer.Plural(n, "checked item", "checked items")}.", "shopping")));
            }

            // Remove
            var rm = Regex.Match(l, @"^(?:remove|delete|take|cross|erase|scratch)(?: off)? (.+?) (?:off|from|out of) " + List + "$|^(?:cross|check|tick) off (.+?)(?: (?:on|from) " + List + ")?$");
            if (rm.Success)
            {
                string what = rm.Groups[1].Success ? rm.Groups[1].Value : rm.Groups[2].Value;
                var gone = ShoppingList.Remove(what);
                if (gone.Count == 0) return Task.FromResult<AceAnswer?>(AceAnswer.Say($"I couldn't find {what} on your shopping list.", "shopping", 0.6));
                return Task.FromResult<AceAnswer?>(Card(AceAnswer.Say($"Removed {Composer.List(gone.Select(g => g.Name).ToList())} from your shopping list.", "shopping")));
            }

            // A recipe's ingredients: "add the ingredients to my shopping list", "put that on my grocery list", "add the pancake ingredients".
            var recipeAdd = Regex.Match(l, @"^(?:add|put|save|send)(?: all)? (?:the |those |these |that |this |them|it|everything)?\s*(?:(.+?) )?(?:ingredients|stuff|things|recipe|that|those|these|it|them|everything)?(?: for (.+?))? (?:to|on|onto|in|into) " + List + "$");
            bool aboutRecipe = Regex.IsMatch(l, @"\b(ingredients|recipe|that|those|these|it|them|everything|the stuff|what i need)\b") && recipeAdd.Success;
            if (aboutRecipe)
            {
                string named = recipeAdd.Groups[2].Success ? recipeAdd.Groups[2].Value : recipeAdd.Groups[1].Success ? Regex.Replace(recipeAdd.Groups[1].Value, @"^(the|those|these|that|this)\s+", "") : "";
                Recipe? recipe = named.Length > 0 && !Regex.IsMatch(named, @"^(the|that|those|these|this|it|them|all)$") ? Cookbook.Find(named) : null;
                recipe ??= Talk.Cooking?.Recipe ?? (Talk.LastRecipe != null && DateTime.Now - Talk.LastRecipeAt < TimeSpan.FromHours(3) ? Talk.LastRecipe : null);
                if (recipe == null) return Task.FromResult<AceAnswer?>(AceAnswer.Say("Which recipe? Ask me for one first, like \"how do I make pancakes\", then say \"add the ingredients to my shopping list\".", "shopping", 0.5));
                int n = AddRecipeToList(recipe);
                Step("Added a recipe's ingredients", recipe.Name);
                return Task.FromResult<AceAnswer?>(Card(AceAnswer.Say($"Added {Composer.Plural(n, "ingredient", "ingredients")} for {recipe.Name} to your shopping list.", "shopping")));
            }

            // Add items
            var add = Regex.Match(t.Text.Trim().TrimEnd('.', '!'), @"^(?:add|put|stick|throw|write|save|i need to buy|i need|we need|buy)\s+(.+?)\s+(?:to|on|onto|in|into)\s+(?:my |the |our )?(?:shopping|grocery|groceries)(?: list)?$", RegexOptions.IgnoreCase);
            if (add.Success)
            {
                var items = SplitItems(add.Groups[1].Value);
                if (items.Count == 0) return Task.FromResult<AceAnswer?>(null);
                var added = items.Select(i => ShoppingList.Add(i)).ToList();
                Step("Added to the shopping list", string.Join(", ", items));
                string names = Composer.List(added.Select(i => i.Name).ToList());
                var merged = added.Where(i => i.Amount != null && items.All(x => !x.Equals(ShoppingList.Describe(i), StringComparison.OrdinalIgnoreCase))).ToList();
                string now = added.Count == 1 && added[0].Amount != null ? $" You need {ShoppingList.Describe(added[0])} now." : "";
                return Task.FromResult<AceAnswer?>(Card(AceAnswer.Say($"Added {names} to your shopping list.{now}", "shopping")));
            }

            // Show
            if (Regex.IsMatch(l, @"^(?:what'?s|whats|what is|show|read|open|check|see|list|tell me|what do i have)(?: me)?(?: what'?s)? (?:on |in )?" + List + "$|^" + List.Replace("(?:my |the |our )?", "(?:my |the |our )") + "$|^what do i need to (?:buy|get)|^(?:show|read|open)(?: me)? " + List) || Regex.IsMatch(l, @"^(?:my )?(?:shopping|grocery) list$"))
            {
                var items = ShoppingList.Items().Where(i => !i.Checked).ToList();
                AceAnswer a;
                if (items.Count == 0) a = AceAnswer.Say("Your shopping list is empty. Say \"add milk to my shopping list\" to start one.", "shopping");
                else
                {
                    var shown = items.Take(30).Select(ShoppingList.Describe).ToList();
                    a = AceAnswer.Say($"You've got {Composer.Plural(items.Count, "thing", "things")} on your shopping list: {Composer.List(shown)}{(items.Count > 30 ? ", and more" : "")}.", "shopping");
                    a.Speak = items.Count <= 6 ? a.Text : $"You've got {items.Count} things on your shopping list, like {Composer.List(items.Take(3).Select(i => i.Name).ToList())}. The whole list is on screen.";
                }
                return Task.FromResult<AceAnswer?>(Card(a));
            }
            return Task.FromResult<AceAnswer?>(null);
        }

        /// <summary>Adds every ingredient of a recipe (not water). Returns how many.</summary>
        public int AddRecipeToList(Recipe recipe)
        {
            int n = 0;
            foreach (var ing in recipe.Ingredients)
            {
                if (ShoppingList.Skip(ing)) continue;
                ShoppingList.Add(ing, recipe.Name);
                n++;
            }
            return n;
        }

        private static List<string> SplitItems(string text)
        {
            string s = text.Trim();
            var keep = new Dictionary<string, string>();
            int k = 0;
            foreach (var phrase in KeepTogether)
            {
                var m = Regex.Match(s, Regex.Escape(phrase), RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                string token = "§" + (k++) + "§";
                keep[token] = m.Value;
                s = s.Replace(m.Value, token);
            }
            var parts = Regex.Split(s, @"\s*,\s*(?:and\s+)?|\s+and\s+|\s*&\s*", RegexOptions.IgnoreCase)
                .Select(p => keep.Aggregate(p, (acc, kv) => acc.Replace(kv.Key, kv.Value)).Trim())
                .Select(p => Regex.Replace(p, @"^(some|more)\s+", "", RegexOptions.IgnoreCase))
                .Where(p => p.Length > 0 && p.Length < 60).ToList();
            return parts;
        }

        // ===================================================== Reminders

        private Task<AceAnswer?> CancelReminderAsync(Turn t)
        {
            var pending = ReminderStore.Pending().Where(r => !r.IsTimer).ToList();
            if (pending.Count == 0) return Task.FromResult<AceAnswer?>(AceAnswer.Say("You don't have any reminders set.", "reminder"));
            if (t.Has(@"\b(all|every|everything)\b"))
            {
                foreach (var r in pending) ReminderStore.Cancel(r.Id);
                return Task.FromResult<AceAnswer?>(AceAnswer.Say($"Cancelled {Composer.Plural(pending.Count, "reminder", "reminders")}.", "reminder"));
            }
            var about = t.Match(@"\breminders?\s+(?:about|to|for|that|of)\s+(.+)$");
            Reminder? pick = null;
            if (about.Success)
            {
                var want = NoteStore.Words(about.Groups[1].Value);
                pick = pending.Select(r => (r, score: NoteStore.Words(r.Text).Count(w => want.Contains(w)))).Where(x => x.score > 0)
                              .OrderByDescending(x => x.score).Select(x => x.r).FirstOrDefault();
                if (pick == null) return Task.FromResult<AceAnswer?>(AceAnswer.Say($"I couldn't find a reminder about {about.Groups[1].Value.Trim()}.", "reminder", 0.6));
            }
            else if (pending.Count == 1 || t.Has(@"\b(that|the last|my last|this)\b"))
                pick = pending.Count == 1 ? pending[0] : pending.OrderByDescending(r => r.Id).First();
            if (pick == null)
                return Task.FromResult<AceAnswer?>(AceAnswer.Say($"Which one? You have {Composer.Plural(pending.Count, "reminder", "reminders")}: {Composer.List(pending.Take(5).Select(r => r.Text).ToList())}.", "reminder", 0.6));
            ReminderStore.Cancel(pick.Id);
            Step("Cancelled a reminder", pick.Text);
            return Task.FromResult<AceAnswer?>(AceAnswer.Say($"Cancelled your reminder to {pick.Text}.", "reminder"));
        }

        // ===================================================== What's playing

        private async Task<AceAnswer?> MusicAsync(Turn t)
        {
            Step("Checking what's playing");
            var song = await NowPlaying.GetAsync().ConfigureAwait(false);
            AceAnswer a;
            if (song == null) a = AceAnswer.Say("Nothing's playing right now. Start a song in Spotify, YouTube or any music app and ask me again.", "music", 0.8);
            else
            {
                string by = song.Artist.Length > 0 ? " by " + song.Artist : "";
                a = AceAnswer.Say($"{(song.Playing ? "That's" : "Paused on")} \"{song.Title}\"{by}{(song.App.Length > 0 ? " in " + song.App : "")}.", "music");
            }
            a.Cards.Add(new MusicCard { Song = song });
            return a;
        }

        // ===================================================== What's on my screen

        private async Task<AceAnswer?> ScreenReadAsync(Turn t)
        {
            Step("Looking at your screen");
            Status?.Invoke("hide-for-screenshot");
            await Task.Delay(450, t.Ct).ConfigureAwait(false);   // let the overlay get out of the way
            ScreenText screen;
            try { screen = await ScreenReader.ReadAsync().ConfigureAwait(false); }
            catch (Exception ex) { return AceAnswer.Say("I couldn't read your screen: " + ex.Message, "screen.read", 0.4); }
            Step("Read the text on screen", Composer.Plural(screen.Words, "word", "words"));

            if (t.Has(@"\b(solve|answer|work out|figure out|do|help (me )?with)\b.*\b(problem|question|math|equation|homework|sum)\b|\bsolve\b"))
            {
                foreach (var line in screen.Lines.OrderByDescending(l => l.Contains('=') ? 1 : 0))
                {
                    if (!Regex.IsMatch(line, @"\d")) continue;
                    var work = MathSteps.Solve(line);
                    if (work == null) continue;
                    var hw = HomeworkAnswer(work);
                    hw.Text = $"On your screen I found {AceV4Brain.Short(line, 60)}. " + hw.Text;
                    return hw;
                }
                foreach (var line in screen.Lines)
                {
                    string? expr = Calculator.ToExpression(line);
                    if (expr != null && Calculator.Evaluate(expr) is double v)
                        return AceAnswer.Say($"On your screen I found {AceV4Brain.Short(line, 60)}. That's {Calculator.Format(v)}.", "screen.read");
                }
                var none = AceAnswer.Say("I couldn't find a math problem on your screen. Make sure it's showing (not covered up), then ask again.", "screen.read", 0.5);
                none.Cards.Add(new ScreenTextCard { Screen = screen });
                return none;
            }
            var a = AceAnswer.Say(ScreenReader.Summary(screen), "screen.read");
            a.Cards.Add(new ScreenTextCard { Screen = screen });
            return a;
        }

        // ===================================================== Screen time

        private Task<AceAnswer?> ScreenTimeAsync(Turn t)
        {
            string l = t.Lower.Trim().TrimEnd('.', '!', '?');
            var now = DateTime.Now;
            AceAnswer Card(AceAnswer a) { a.Cards.Add(new ScreenTimeCard()); return a; }

            // Turn limits off.
            var off = Regex.Match(l, @"^(?:turn off|remove|clear|delete|stop|cancel|get rid of|no more)(?: the| my| all)?(?: (.+?))? (?:screen ?time )?(?:time )?limits?(?: (?:for|on) (.+))?$");
            if (off.Success)
            {
                string app = (off.Groups[2].Success ? off.Groups[2].Value : off.Groups[1].Success ? off.Groups[1].Value : "").Trim();
                app = Regex.Replace(app, @"^(screen ?time|the|my|all|app)$", "");
                if (app.Length > 0 && !Regex.IsMatch(app, @"^(screen ?time|daily)$"))
                {
                    string name = ScreenTimeAppName(app);
                    ScreenTime.SetAppLimit(name, 0);
                    return Task.FromResult<AceAnswer?>(Card(AceAnswer.Say($"Okay, no more limit on {name}.", "screentime")));
                }
                ScreenTime.ClearLimits();
                return Task.FromResult<AceAnswer?>(Card(AceAnswer.Say("Okay, I turned off your screen time limits.", "screentime")));
            }

            // "limit minecraft to 1 hour a day", "set a limit for roblox to 45 minutes"
            var appLimit = Regex.Match(l, @"^(?:limit|cap|set (?:a |the )?(?:time )?limit (?:for|on)|put (?:a )?limit on) (.+?) (?:to|at|for) (.+?)(?: a day| per day| each day| every day| daily)?$");
            if (appLimit.Success && !Regex.IsMatch(appLimit.Groups[1].Value, @"^(my )?(screen ?time|computer|pc|laptop|time)$"))
            {
                var span = ParseDuration(appLimit.Groups[2].Value, out _);
                if (span <= TimeSpan.Zero) return Task.FromResult<AceAnswer?>(AceAnswer.Say("How long a day? Say something like \"limit Minecraft to 1 hour a day\".", "screentime", 0.5));
                string name = ScreenTimeAppName(appLimit.Groups[1].Value);
                ScreenTime.SetAppLimit(name, (int)span.TotalMinutes);
                return Task.FromResult<AceAnswer?>(Card(AceAnswer.Say($"Done. I'll give you a heads-up 15 minutes before your {ScreenTime.Span(span)} of {name} each day, and when it's up. I won't close anything.", "screentime")));
            }

            // "set my screen time limit to 2 hours", "limit my screen time to 3 hours"
            if (Regex.IsMatch(l, @"\b(set|make|change|put|limit|cap)\b") && Regex.IsMatch(l, @"\b(hours?|minutes?|mins?)\b"))
            {
                var span = ParseDuration(l, out _);
                if (span > TimeSpan.Zero)
                {
                    ScreenTime.SetLimit((int)span.TotalMinutes);
                    return Task.FromResult<AceAnswer?>(Card(AceAnswer.Say($"Okay! Your screen time limit is {ScreenTime.Span(span)} a day. I'll give you a gentle heads-up 15 minutes before and when it's up. I won't close anything.", "screentime")));
                }
            }

            // A week.
            if (Regex.IsMatch(l, @"\b(this week|week|weekly|last 7 days|past week)\b"))
            {
                var week = ScreenTime.Week(now);
                var total = TimeSpan.FromSeconds(week.Sum(d => d.Total.TotalSeconds));
                int days = week.Count(d => d.Total > TimeSpan.Zero);
                if (days == 0) return Task.FromResult<AceAnswer?>(Card(AceAnswer.Say("I haven't counted any screen time this week yet. I count while Ace is running.", "screentime", 0.7)));
                var avg = TimeSpan.FromSeconds(total.TotalSeconds / days);
                return Task.FromResult<AceAnswer?>(Card(AceAnswer.Say($"This week you've used your PC for {ScreenTime.Span(total)}, about {ScreenTime.Span(avg)} a day.", "screentime")));
            }

            // One app ("how long have i played minecraft today").
            var oneApp = Regex.Match(l, @"how (?:long|much time|much) (?:have|did) i (?:been )?(?:play(?:ed|ing)?|us(?:ed|ing|e)|spen[dt]|been on|watch(?:ed|ing)?|on)(?: on| in| using| playing)? (.+?)(?: today)?$");
            if (oneApp.Success && !Regex.IsMatch(oneApp.Groups[1].Value, @"^(my |the )?(computer|pc|laptop|screen|screens|it)$"))
            {
                string name = ScreenTimeAppName(oneApp.Groups[1].Value);
                var used = ScreenTime.AppTotal(now, name);
                string limit = ScreenTime.AppLimits.TryGetValue(name, out int m) ? $" Your limit is {ScreenTime.Span(TimeSpan.FromMinutes(m))}." : "";
                return Task.FromResult<AceAnswer?>(Card(AceAnswer.Say(used < TimeSpan.FromMinutes(1) ? $"I haven't seen you use {name} today.{limit}" : $"You've used {name} for {ScreenTime.Span(used)} today.{limit}", "screentime")));
            }

            // Today.
            var day = ScreenTime.Day(now);
            var today = ScreenTime.Total(now);
            AceAnswer ans;
            if (today < TimeSpan.FromMinutes(1))
                ans = AceAnswer.Say("I haven't counted any screen time yet today. I count the time you're actively using your PC while Ace is running.", "screentime", 0.7);
            else
            {
                string top = day.Count > 0 ? $" Most of it was {day[0].App} ({ScreenTime.Span(TimeSpan.FromSeconds(day[0].Seconds))})." : "";
                int limit = ScreenTime.LimitMinutes;
                string left = limit > 0 ? (today.TotalMinutes >= limit ? " That's past your limit for today." : $" You have {ScreenTime.Span(TimeSpan.FromMinutes(limit) - today)} left of your limit.") : "";
                ans = AceAnswer.Say($"You've used your PC for {ScreenTime.Span(today)} today.{top}{left}", "screentime");
            }
            return Task.FromResult<AceAnswer?>(Card(ans));
        }

        private static string ScreenTimeAppName(string said)
        {
            string s = Regex.Replace(said.Trim(), @"^(the|my|playing|using|on)\s+|\s+(app|game|today)$", "", RegexOptions.IgnoreCase).Trim();
            return s.Length == 0 ? s : WindowControl.AppName(s);
        }

        // ===================================================== Money

        private static readonly Dictionary<string, string> Currencies = new(StringComparer.OrdinalIgnoreCase)
        {
            ["dollar"] = "USD", ["dollars"] = "USD", ["usd"] = "USD", ["us dollars"] = "USD", ["$"] = "USD", ["bucks"] = "USD",
            ["euro"] = "EUR", ["euros"] = "EUR", ["eur"] = "EUR", ["€"] = "EUR",
            ["pound"] = "GBP", ["pounds"] = "GBP", ["british pounds"] = "GBP", ["pounds sterling"] = "GBP", ["gbp"] = "GBP", ["£"] = "GBP",
            ["yen"] = "JPY", ["jpy"] = "JPY", ["japanese yen"] = "JPY", ["¥"] = "JPY",
            ["canadian dollars"] = "CAD", ["canadian dollar"] = "CAD", ["cad"] = "CAD",
            ["australian dollars"] = "AUD", ["australian dollar"] = "AUD", ["aud"] = "AUD",
            ["peso"] = "MXN", ["pesos"] = "MXN", ["mexican pesos"] = "MXN", ["mxn"] = "MXN",
            ["rupee"] = "INR", ["rupees"] = "INR", ["inr"] = "INR", ["yuan"] = "CNY", ["cny"] = "CNY", ["renminbi"] = "CNY",
            ["won"] = "KRW", ["krw"] = "KRW", ["swiss francs"] = "CHF", ["franc"] = "CHF", ["francs"] = "CHF", ["chf"] = "CHF",
            ["real"] = "BRL", ["reais"] = "BRL", ["brl"] = "BRL", ["krona"] = "SEK", ["kronor"] = "SEK", ["sek"] = "SEK",
            ["zloty"] = "PLN", ["pln"] = "PLN", ["lira"] = "TRY", ["try"] = "TRY", ["rand"] = "ZAR", ["zar"] = "ZAR",
            ["nz dollars"] = "NZD", ["new zealand dollars"] = "NZD", ["nzd"] = "NZD", ["hkd"] = "HKD", ["hong kong dollars"] = "HKD",
            ["sgd"] = "SGD", ["singapore dollars"] = "SGD", ["nok"] = "NOK", ["norwegian krone"] = "NOK", ["dkk"] = "DKK", ["danish krone"] = "DKK",
        };

        private static readonly string CurrencyPattern = string.Join("|", Currencies.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape));

        private async Task<AceAnswer?> CurrencyAsync(Turn t)
        {
            string l = t.Lower.Replace(",", "");
            var m = Regex.Match(l, @"(?:^|\s)(\$|€|£|¥)?\s?(\d+(?:\.\d+)?)\s*(" + CurrencyPattern + @")?\s+(?:to|in|into|as|is how many|in how many)\s+(" + CurrencyPattern + @")\b");
            if (!m.Success) return null;
            string fromWord = m.Groups[3].Success && m.Groups[3].Value.Length > 0 ? m.Groups[3].Value : m.Groups[1].Value;
            if (fromWord.Length == 0 || !Currencies.TryGetValue(fromWord, out var from) || !Currencies.TryGetValue(m.Groups[4].Value, out var to)) return null;
            // "pounds to kilograms" is weight; only money when both sides are money.
            if (from == to) return AceAnswer.Say("That's the same money!", "currency");
            double amount = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            Step("Getting today's money rate", $"{from} → {to}");
            string? json = await Web.WebClient.FetchAsync($"https://api.frankfurter.app/latest?amount={amount.ToString(CultureInfo.InvariantCulture)}&from={from}&to={to}", t.Ct).ConfigureAwait(false);
            if (json == null) return AceAnswer.Say("I need the internet to get today's money rates. Try again when you're online.", "currency", 0.4);
            try
            {
                using var doc = JsonDocument.Parse(json);
                double value = doc.RootElement.GetProperty("rates").GetProperty(to).GetDouble();
                string date = doc.RootElement.TryGetProperty("date", out var d) ? d.GetString() ?? "" : "";
                var a = AceAnswer.Say($"{Money(amount, from)} is about {Money(value, to)} today.", "currency");
                a.Cards.Add(new InfoCard { Title = $"{from} → {to}", Body = $"1 {from} = {value / amount:0.####} {to}", Source = "European Central Bank rates" + (date.Length > 0 ? ", " + date : ""), Url = "https://www.frankfurter.app" });
                return a;
            }
            catch { return AceAnswer.Say("I couldn't read today's money rates. Try again in a bit.", "currency", 0.4); }
        }

        private static string Money(double v, string code) => code switch
        {
            "USD" => "$" + v.ToString("#,0.00", CultureInfo.InvariantCulture),
            "EUR" => "€" + v.ToString("#,0.00", CultureInfo.InvariantCulture),
            "GBP" => "£" + v.ToString("#,0.00", CultureInfo.InvariantCulture),
            "JPY" => "¥" + v.ToString("#,0", CultureInfo.InvariantCulture),
            _ => v.ToString("#,0.00", CultureInfo.InvariantCulture) + " " + code,
        };

        // ===================================================== Scripts

        private Task<AceAnswer?> ScriptAsync(Turn t)
        {
            string l = t.Lower.Trim().TrimEnd('.', '!', '?');
            // "find my lua scripts" is a file search; "open roblox" opens the app; "promo code" isn't code.
            if (Regex.IsMatch(l, @"^(find|locate|search for|search my|where'?s|where is|where are|open|launch|start|close|quit|run|play)\b")) return Task.FromResult<AceAnswer?>(null);
            if (Regex.IsMatch(l, @"\b(promo|zip|area|postal|wifi|wi-fi|door|locker|gift|discount|cheat|qr|bar|morse|dress|country|redeem|verification|pin|secret|access|coupon|referral|friend|join|server|lock|safe)\s+codes?\b")) return Task.FromResult<AceAnswer?>(null);
            if (ScriptLibrary.All.Count == 0) return Task.FromResult<AceAnswer?>(null);
            string lang = ScriptLibrary.LangOf(l);
            bool tech = lang.Length > 0 || Regex.IsMatch(l, @"\b(scripts?|code|coding|sketch)\b");
            bool browse = Regex.IsMatch(l, @"^(show|list|browse|open)( me)?( all)?( of)?( the| your| my)? |^(what|which)( \w+){0,3} (scripts|code)\b|\bscript library\b|\ball (the |your )?(\w+ )?scripts\b|^(lua|luau|roblox|c\+\+|cpp|arduino|unreal)( scripts?| code)?$|^scripts$|\b(scripts|code) do you have\b|\bwhat can you code\b");

            var hits = ScriptLibrary.Search(l);
            var best = hits.FirstOrDefault();
            if (best.Script != null && best.Score >= (tech ? 0.5 : 0.75) && !(browse && best.Score < 0.8))
                return Task.FromResult<AceAnswer?>(ScriptAnswer(best.Script, hits.Skip(1).Where(h => h.Score >= 0.5).Select(h => h.Script).Take(3).ToList()));

            if (browse && tech)
            {
                var list = ScriptLibrary.InLang(lang).ToList();
                string what = lang switch { "lua" => "Roblox Lua", "arduino" => "Arduino", "unreal" => "Unreal Engine", "cpp" => "C++", "console" => "C++ console", _ => "" };
                int lua = ScriptLibrary.InLang("lua").Count(), cpp = ScriptLibrary.InLang("cpp").Count();
                string text = what.Length > 0
                    ? $"I've got {Composer.Plural(list.Count, what + " script", what + " scripts")}, like {Composer.List(list.Take(4).Select(s => s.Name.ToLowerInvariant()).ToList())}. Pick one below, or open the Scripts page to see them all."
                    : $"I've got {lua} Roblox Lua scripts and {cpp} C++ scripts (Arduino, games and Unreal). Pick one below, or open the Scripts page to see them all.";
                var a = AceAnswer.Say(text, "script");
                a.Cards.Add(new ScriptListCard { Title = what.Length > 0 ? what + " scripts" : "Script library", Scripts = list.Take(12).ToList() });
                return Task.FromResult<AceAnswer?>(a);
            }

            // Asked for a script Ace doesn't have.
            if (Regex.IsMatch(l, @"\b(scripts?|code|coding|sketch)\b") && Regex.IsMatch(l, @"\b(give|make|write|need|want|show|create|build|how|can you|script for|script that|code for|code that|script to|code to)\b"))
            {
                var close = hits.Where(h => h.Score >= 0.25).Select(h => h.Script).Take(4).ToList();
                var a = AceAnswer.Say(close.Count > 0
                    ? $"I don't have a ready-made script for exactly that yet. These are the closest ones I have."
                    : $"I don't have a ready-made script for that yet. Say \"show me {(lang is "cpp" or "arduino" or "unreal" or "console" ? "the C++ scripts" : "the Lua scripts")}\" to see what I've got.", "script", 0.5);
                if (close.Count > 0) a.Cards.Add(new ScriptListCard { Title = "Closest scripts", Scripts = close });
                return Task.FromResult<AceAnswer?>(a);
            }
            return Task.FromResult<AceAnswer?>(null);
        }

        public AceAnswer ScriptAnswer(ScriptEntry s, List<ScriptEntry>? alsoSee = null)
        {
            Talk.LastScript = s;
            Step("Found a script in my library", s.Name + " · " + s.KindName);
            string article = Regex.IsMatch(s.Name, @"^[AEIOUaeiou]") ? "an" : "a";
            string where = s.Where.Length > 0 ? " " + s.Where : "";
            var a = AceAnswer.Say($"Here's {article} {s.Name.ToLowerInvariant()} {s.KindName}.{where}", "script");
            a.Speak = $"Here's {article} {s.Name.ToLowerInvariant()} script. I put the code on screen with a copy button.{where}";
            a.Cards.Add(new CodeCard { Script = s });
            a.Suggestions.Add("Explain this script");
            Talk.LastScriptAnswer = a;
            if (alsoSee != null && alsoSee.Count > 0) a.Suggestions.AddRange(alsoSee.Select(x => (x.Lang == "lua" ? "Lua script: " : "C++ script: ") + x.Name.ToLowerInvariant()));
            return a;
        }
    }
}
