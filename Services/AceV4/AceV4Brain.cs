using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Nimbus.AceV4.Brain;
using Nimbus.AceV4.Pc;

namespace Nimbus.AceV4
{
    /// <summary>One request (or one part of a request) being worked on.</summary>
    public sealed class Turn
    {
        public string Text = "";
        public string Lower = "";
        public CancellationToken Ct;
        public List<IntentGuess> Guesses = new();
        public bool Spoken;
        public string TopIntent => Guesses.Count > 0 ? Guesses[0].Intent : "chat";
        public double TopConfidence => Guesses.Count > 0 ? Guesses[0].Confidence : 0;
        public bool Has(string pattern) => Regex.IsMatch(Lower, pattern);
        public Match Match(string pattern) => Regex.Match(Lower, pattern);
    }

    /// <summary>What Ace remembers about the conversation so follow-ups work ("open the second one", "what about Germany?").</summary>
    public sealed class Conversation
    {
        public string LastQuestion = "";
        public string LastKnowledgeQuestion = "";
        public string LastSubject = "";
        public DateTime LastSubjectAt;
        public QaEntry? LastEntry;
        public AceAnswer? LastAnswer;
        public List<ResultItem> LastResults = new();
        public string LastPageText = "";
        public int LastPageUsed;
        public string LastPageUrl = "";
        public ConfirmCard? PendingConfirm;
        /// <summary>Ace asked a question ("When is it?"): the next message goes here first.</summary>
        public Func<Turn, Task<AceAnswer?>>? PendingFollow;
        public DateTime PendingFollowAt;
        public CountdownCard? ActiveCountdown;
        public string LastLearnedId = "";
        /// <summary>The name the last answer gave ("Abraham Lincoln", "Tokyo"), for "he", "she" and "it".</summary>
        public string LastAnswerName = "";
        public bool LastAnswerWho;
        public bool LastAnswerIsThing;
        /// <summary>The last math answer, for "divide that by 4".</summary>
        public double? LastNumber;
        /// <summary>The last app opened or closed, for "close it".</summary>
        public string LastApp = "";
        /// <summary>The last weather/calendar/time request, for "what about tomorrow?".</summary>
        public string LastIntent = "";
        public string LastIntentText = "";
        public DateTime LastIntentAt;
        /// <summary>The last step Ace answered, after follow-ups were filled in.</summary>
        public string LastClause = "";
        /// <summary>The last recipe shown, for "start cooking" and "what do I need".</summary>
        public Brain.Recipe? LastRecipe;
        public DateTime LastRecipeAt;
        /// <summary>Cooking mode, while it's on.</summary>
        public CookingSession? Cooking;
        public readonly List<(string You, string Ace)> Recent = new();
        /// <summary>A question Ace offered to answer next; "yes" answers it.</summary>
        public string OfferedFollowUp = "";
        public DateTime OfferedAt;
        public Brain.TriviaGame? Trivia;
        public Brain.TwentyQuestions? TwentyQ;
        /// <summary>The last script shown, for "copy it" and "where do I put it".</summary>
        public Brain.ScriptEntry? LastScript;
        /// <summary>The answer that showed LastScript, so "explain it" right after knows what "it" is.</summary>
        public AceAnswer? LastScriptAnswer;
    }

    /// <summary>
    /// Ace V4's brain. A big rule-based assistant with small neural nets inside:
    ///   1. rules and a planner split the request into steps,
    ///   2. a tiny intent net (on MiniLM sentence vectors) picks the skill when the rules don't,
    ///   3. a bank of thousands of question-and-answer pairs is searched by meaning AND by words,
    ///   4. a visible "think" loop checks the draft, breaks hard questions into parts, compares things,
    ///      reads the web when it should, double-checks, and learns what it found,
    ///   5. LFM3 Omni (if running) can be asked to help as a last resort.
    /// Every step is shown in Ace's thinking list.
    /// </summary>
    public sealed partial class AceV4Brain
    {
        public AceV4Config Config { get; }
        public AceV4State State { get; }
        public Embedder Embedder { get; private set; } = null!;
        public KnowledgeBank Bank { get; private set; } = null!;
        public IntentClassifier Intents { get; private set; } = null!;
        public Conversation Talk { get; private set; } = new();

        /// <summary>Starts a fresh conversation (forgets "it", the last answer and open questions).</summary>
        public void NewChat() => Talk = new Conversation();

        /// <summary>
        /// Something Ace brought up by itself (like "open Minecraft?") becomes the last thing said, so a spoken
        /// "yes" or "no" answers its question.
        /// </summary>
        public void Offer(AceAnswer a)
        {
            Talk.LastAnswer = a;
            foreach (var card in a.Cards) if (card is ConfirmCard c) Talk.PendingConfirm = c;
        }

        /// <summary>Tests only: skills are matched but not run (no apps open, nothing touches Windows).</summary>
        public bool DryRunActions { get; set; }

        /// <summary>Which part of the brain handled each step of the last request ("app.open", "bank:space.mars.year", "fallback").</summary>
        public List<string> Routes { get; } = new();

        /// <summary>A thinking step was added (any thread). The UI shows these live.</summary>
        public event Action<ThoughtStep>? Thought;
        /// <summary>Short status for the island ("Reading allrecipes.com…").</summary>
        public event Action<string>? Status;
        /// <summary>Set by AceRuntime: runs the wake-up checks now and returns what it found.</summary>
        public Func<CancellationToken, Task<AceAnswer>>? RunChecks;
        /// <summary>Set by the runtime: checks GitHub for a new Ace and installs it ("update yourself").</summary>
        public Func<CancellationToken, Task<string>>? CheckForUpdates;

        public bool Ready { get; private set; }
        public string LoadProblem { get; private set; } = "";
        private readonly SemaphoreSlim _askGate = new(1, 1);
        private List<ThoughtStep> _steps = new();
        private Stopwatch _clock = new();
        private int _passes;

        public AceV4Brain(AceV4Config config, AceV4State state)
        {
            Config = config;
            State = state;
            Composer.UserName = config.UserName;
        }

        /// <summary>Loads the sentence model, the Q&A bank and trains the intent net. Run on a background thread (a few seconds).</summary>
        public void Load()
        {
            try
            {
                Embedder = new Embedder();
                Bank = new KnowledgeBank(Embedder);
                Bank.Load();
                Intents = new IntentClassifier(Embedder);
                Intents.Train();
                Cookbook = RecipeBook.Load();
                BuildSpeller();
                BuildEntities();
                Ready = true;
                if (!Embedder.IsReady) LoadProblem = Embedder.Problem;
            }
            catch (Exception ex)
            {
                LoadProblem = ex.Message;
                Embedder ??= new Embedder();
                Bank ??= new KnowledgeBank(Embedder);
                Intents ??= new IntentClassifier(Embedder);
                Ready = true;
            }
        }

        /// <summary>Ace's built-in recipes (Data/recipes.json), so recipes work offline.</summary>
        public RecipeBook Cookbook { get; private set; } = new();

        /// <summary>Fixes typos before Ace reads a request.</summary>
        public Speller Speller { get; } = new();

        private const string CommandWords =
            "open close launch start run quit exit minimize maximize resize snap restore hide show search google youtube find look locate " +
            "remind reminder reminders timer timers alarm countdown screenshot screenshots volume mute unmute louder quieter softer weather forecast rain snow " +
            "calendar schedule agenda event events appointment meeting practice delete remove cancel move reschedule rename add put book plan " +
            "check scan storage space disk drive battery cpu gpu ram memory temperature notifications messages define definition meaning spell " +
            "recipe recipes cook bake convert calculate times divided plus minus percent square root cube minutes minute seconds second hours hour " +
            "today tonight tomorrow yesterday morning afternoon evening night week weekend month year monday tuesday wednesday thursday friday saturday sunday " +
            "help remember forget lock restart reboot shutdown sleep bedtime bed wake focus study pomodoro break report health habits usually concentrate homework weekly grade card suggesting suggest offering resume unpause round rounds healthy " +
            "tab tabs window windows app apps program programs browser desktop file files folder document documents pdf photo photos picture pictures video videos " +
            "music song songs play pause skip next previous track what when where which who why how many much is are was were does did can could would should " +
            "the a an and or of to in on at for with about from by tell me my your you it this that these those there their they please thanks thank";

        private const string UpgradeWords =
            "lcm lcd gcf gcd hcf factor factors multiple multiples prime primes fraction fractions numerator denominator equation equations solve simplify " +
            "algebra homework math maths percent percentage hypotenuse pythagorean perimeter area volume circumference radius diameter quotient remainder " +
            "translate translation spanish french german italian portuguese japanese chinese korean russian arabic hindi dutch english language " +
            "trivia quiz questions twenty guess guessing hint notes note shopping grocery groceries list lists ingredients " +
            "memory memories remember forget song playing music screen time limit limits convert currency dollars euros pounds yen pesos " +
            "script scripts lua luau roblox arduino sketch unreal code coding cpp leaderstats datastore teleporter checkpoint conveyor lava kill brick";

        private void BuildSpeller()
        {
            foreach (var e in Bank.All())
            {
                foreach (var q in e.Questions) Speller.Learn(q, 3);
                foreach (var a in e.Answers) Speller.Learn(a, 1);
            }
            foreach (var list in IntentClassifier.Examples.Values) foreach (var ex in list) Speller.Learn(ex, 3);
            Speller.Learn(CommandWords, 20);
            foreach (var r in Cookbook.All)
            {
                foreach (var name in r.Aliases.Append(r.Name)) Speller.Learn(name, 10);
                foreach (var ing in r.Ingredients) Speller.Learn(ing, 2);
            }
            Speller.Learn(UpgradeWords, 20);
            foreach (var phrase in Translator.Vocabulary) Speller.Learn(phrase, 4);
            foreach (var s in ScriptLibrary.All)
            {
                Speller.Learn(s.Name, 10);
                foreach (var alias in s.Aliases) Speller.Learn(alias, 8);
            }
            foreach (var q in Trivia.All) Speller.Learn(q.Question + " " + q.Answer, 2);
            foreach (var name in Pc.WindowControl.Aliases.Keys) Speller.LearnName(name);
            foreach (var name in Pc.AppLauncher.BuiltInNames) Speller.LearnName(name);
            foreach (var name in Pc.BrowserControl.Sites.Keys) Speller.LearnName(name);
            try
            {
                string words = System.IO.Path.Combine(AppContext.BaseDirectory, "Data", "english-words.txt");
                if (System.IO.File.Exists(words)) Speller.Protect(System.IO.File.ReadLines(words));
            }
            catch { }
            Speller.Seal();
        }

        public string BrainSummary =>
            $"{Bank.Count:N0} answers · {Bank.QuestionCount:N0} ways to ask · {Bank.LearnedCount:N0} learned · " +
            (Embedder.IsReady ? "meaning net on" : "words only (run Setup-AceV4.ps1 for the meaning net)");

        // ===================================================== Thinking steps

        public void Step(string label, string detail = "")
        {
            var s = new ThoughtStep(label, detail);
            lock (_steps) _steps.Add(s);
            Thought?.Invoke(s);
        }

        public void Say(string status) => Status?.Invoke(status);

        private bool OutOfTime => _clock.Elapsed.TotalSeconds > Config.MaxThinkSeconds || _passes >= Config.MaxThinkPasses;

        // ===================================================== Asking

        /// <summary>Answers a request. One at a time; a new question waits for the last one.</summary>
        public async Task<AceAnswer> AskAsync(string text, CancellationToken ct = default, bool spoken = false)
        {
            await _askGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                lock (_steps) _steps = new List<ThoughtStep>();
                Routes.Clear();
                _clock = Stopwatch.StartNew();
                _passes = 0;
                var answer = await AskCoreAsync(text ?? "", ct, spoken).ConfigureAwait(false);
                lock (_steps) answer.Thoughts = _steps.ToList();
                if (string.IsNullOrWhiteSpace(answer.Speak)) answer.Speak = Composer.ForSpeech(answer.Text);
                Talk.LastQuestion = text ?? "";
                Talk.LastAnswer = answer;
                Talk.Recent.Add((text ?? "", answer.Text));
                if (Talk.Recent.Count > 12) Talk.Recent.RemoveAt(0);
                foreach (var card in answer.Cards)
                {
                    if (card is ConfirmCard c) Talk.PendingConfirm = c;
                    if (card is CountdownCard cd) Talk.ActiveCountdown = cd;
                    if (card is ResultsCard rc && rc.Items.Count > 0) Talk.LastResults = rc.Items;
                }
                Say("");
                return answer;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return AceAnswer.Say("Something went wrong while I was thinking: " + ex.Message, "error", 0);
            }
            finally { _askGate.Release(); }
        }

        private async Task<AceAnswer> AskCoreAsync(string raw, CancellationToken ct, bool spoken)
        {
            string text = Normalize(raw);
            var (spelled, fixes) = Speller.Fix(text);
            if (fixes.Count > 0)
            {
                Step("Fixed typos", string.Join(", ", fixes.Take(6)));
                text = Normalize(spelled);
            }
            if (text.Length == 0) return AceAnswer.Say(Composer.Pick("empty", new[] { "I'm here. What do you need?", "Yep? I'm listening.", "Hey {user}, what's up?" }), "chat");

            // A countdown is running: "cancel", "stop", "wait", "no" stop it.
            if (Talk.ActiveCountdown is { Done: false } cd && Regex.IsMatch(text.ToLowerInvariant(), @"^(no+|stop|cancel|wait|don'?t|abort|hold on|never ?mind|nevermind)\b"))
            {
                cd.Cancel();
                Step("Stopped the countdown");
                return AceAnswer.Say(Composer.Pick("cancel.power", new[] { "Cancelled. Your PC stays on.", "Okay, stopped it. Nothing's shutting down.", "Got it, cancelled." }), "power");
            }

            // Ace just asked a yes/no question: answer it. (Only the last answer's question, and only a plain yes or no:
            // "no, it's actually Everest" is a correction, not an answer.)
            if (Talk.PendingConfirm is { Answered: false } pc && Talk.LastAnswer != null && Talk.LastAnswer.Cards.Contains(pc))
            {
                string l = text.ToLowerInvariant();
                bool plain = l.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 5 && !Regex.IsMatch(l, @"\b(it'?s|it is|actually|the answer)\b");
                bool yes = plain && Regex.IsMatch(l, @"^(yes|yeah|yep|yup|sure|ok(ay)?|do it|go ahead|please do|confirm|sounds good|alright|y)\b");
                bool no = plain && Regex.IsMatch(l, @"^(no|nope|nah|don'?t|cancel|stop|never ?mind|nevermind|n)\b");
                if (yes || no)
                {
                    Step(yes ? "You said yes" : "You said no", pc.Question);
                    Talk.PendingConfirm = null;
                    string result = await pc.AnswerAsync(yes).ConfigureAwait(false) ?? "Already done.";
                    return AceAnswer.Say(result, "confirm");
                }
                // Something else: leave the question open but carry on with the new request.
            }

            Step("Reading your request", Short(text, 90));
            var clauses = Planner.Split(text);
            if (clauses.Count > 1) Step("Splitting it into " + clauses.Count + " steps", string.Join(" · ", clauses.Select(c => Short(c, 40))));

            var parts = new List<AceAnswer>();
            foreach (var clause in clauses.Take(6))
            {
                ct.ThrowIfCancellationRequested();
                var turn = new Turn { Text = clause, Lower = clause.ToLowerInvariant(), Ct = ct, Spoken = spoken };
                parts.Add(await AnswerClauseAsync(turn).ConfigureAwait(false));
            }
            if (parts.Count == 1) return parts[0];

            var all = new AceAnswer
            {
                Text = Composer.Join(parts.Select(p => p.Text).ToList()),
                Source = string.Join("+", parts.Select(p => p.Source).Distinct()),
                Confidence = parts.Min(p => p.Confidence),
                Mood = parts.Any(p => p.Mood == "alert") ? "alert" : parts.Last().Mood,
            };
            foreach (var p in parts) all.Cards.AddRange(p.Cards);
            return all;
        }

        /// <summary>Removes "hey Ace", "please" and filler, keeps the meaning.</summary>
        public static string Normalize(string raw)
        {
            string t = Regex.Replace(raw ?? "", @"\s+", " ").Trim();
            t = Regex.Replace(t, @"^(hey|hi|ok|okay|yo)?[\s,]*ace\b[\s,:!.]*", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"^(can|could|would|will)\s+you\s+(please\s+)?(?=(open|close|launch|start|find|search|set|add|remind|tell|show|play|pause|turn|move|delete|remove|minimi|maximi|resize|snap|lock|restart|shut|check|take|make|put|look|give|help|define|convert|calculate|what|schedule|go|empty|mute|unmute|skip))", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"^please\s+", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"[,\s]+please[.!?]*$", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\b(i want you to|i need you to|i'd like you to)\s+", "", RegexOptions.IgnoreCase);
            return t.Trim().TrimEnd('.', '!').Trim();
        }

        // ===================================================== One step

        private async Task<AceAnswer> AnswerClauseAsync(Turn turn)
        {
            int before = Routes.Count;
            string re = Rephrase.Apply(turn.Text);
            if (!re.Equals(turn.Text, StringComparison.OrdinalIgnoreCase) && re.Length > 0)
            {
                Step("Understood it as", re);
                turn.Text = re;
                turn.Lower = re.ToLowerInvariant();
            }
            var answer = await AnswerClauseCoreAsync(turn).ConfigureAwait(false);
            string route = Routes.Count > before ? Routes[^1] : answer.Source;
            NoteAnswer(turn.Text, answer, route);
            OfferFollowUp(answer);
            Talk.LastClause = turn.Text;
            return answer;
        }

        private async Task<AceAnswer> AnswerClauseCoreAsync(Turn turn)
        {
            // Cooking mode first: while cooking, "next" is the next step, not the next song.
            var cooking = await CookingAsync(turn).ConfigureAwait(false);
            if (cooking != null) { Routes.Add(cooking.Source == "timer" ? "cook.timer" : "cook"); return Done(cooking); }

            // "Yes" to a question Ace offered to answer next.
            var offered = TakeOfferedFollowUp(turn);
            if (offered != null) { Routes.Add("bank:" + offered.BankId); return Done(offered); }

            // Follow-ups that point back at the last answer.
            var follow = await FollowUpAsync(turn).ConfigureAwait(false);
            if (follow != null) { Routes.Add(follow.BankId.Length > 0 ? "bank:" + follow.BankId : "follow:" + follow.Source); return follow; }

            // Homework problems (step by step) and money.
            var early = await BeforeMathAsync(turn).ConfigureAwait(false);
            if (early != null) return early;

            // Cheap and exact first: unit conversions and math.
            string? converted = Units.Convert(turn.Text);
            if (converted != null) { Step("Converting units"); Routes.Add("convert"); return Done(AceAnswer.Say(converted, "convert")); }
            string? expr = Calculator.ToExpression(turn.Text);
            if (expr != null && Calculator.Evaluate(expr) is double v)
            {
                Step("Doing the math", expr + " = " + Calculator.Format(v));
                Routes.Add("calc");
                return Done(AceAnswer.Say(Composer.Pick("calc", new[] { "That's {0}.", "{0}.", "It comes out to {0}." }).Replace("{0}", Calculator.Format(v)), "calc"));
            }
            var everyUnit = ConvertAllAnswer(turn);
            if (everyUnit != null) return everyUnit;

            // Rules: clear commands go straight to their skill.
            foreach (var rule in Rules)
            {
                if (!rule.Pattern.IsMatch(turn.Lower)) continue;
                Step("Matched a rule", rule.Intent);
                if (DryRunActions && !RealInDryRun.Contains(rule.Intent)) { Routes.Add(rule.Intent); return Done(AceAnswer.Say("[dry run] " + rule.Intent, rule.Intent)); }
                var got = await rule.Run(turn).ConfigureAwait(false);
                if (got != null) { Routes.Add(rule.Intent); return Done(got); }
            }

            // The intent net: catches requests worded in ways the rules don't know.
            turn.Guesses = Intents.Guess(turn.Text, 3);
            if (turn.Guesses.Count > 0)
                Step(Intents.UsesNeuralNet ? "Intent net's guess" : "Closest example", string.Join(", ", turn.Guesses.Select(g => $"{g.Intent} {g.Confidence:P0}")));
            double needed = Intents.UsesNeuralNet ? 0.55 : 0.45;
            bool question = turn.Lower.EndsWith("?") || Regex.IsMatch(turn.Lower, @"^(do|does|did|is|are|was|were|can|could|would|should|will|who|what|what's|whats|why|which|whose|have|has)\b");
            // A question the bank answers well beats a so-so guess ("what is photosynthesis" is a fact, not "define").
            var peek = turn.Guesses.Count > 0 && turn.Guesses[0].Intent != "chat" ? Bank.Search(turn.Text, 1).FirstOrDefault() : null;
            bool bankKnows = peek != null && peek.Score >= HighScore && peek.Score >= turn.Guesses[0].Confidence - 0.05;
            if (bankKnows) Step("My notes know this one", $"\"{Short(peek!.MatchedQuestion, 50)}\" {peek.Score:0.00}");
            foreach (var g in turn.Guesses.Take(2))
            {
                if (bankKnows) break;
                if (g.Intent == "chat" || g.Confidence < needed) break;
                // Never power off, restart or sleep the PC on a guess unless the words are clearly about power.
                if (g.Intent.StartsWith("power.", StringComparison.Ordinal) && !Regex.IsMatch(turn.Lower, @"\b(pc|computer|laptop|system|machine|shut ?down|power|restart|reboot|sleep|lock|turn (it )?off)\b")) continue;
                // "Do you like Minecraft?" is chat, not "open Minecraft": a question never runs an action on a guess.
                if (question && ActionIntents.Contains(g.Intent)) continue;
                if (!Skills.TryGetValue(g.Intent, out var skill)) continue;
                if (DryRunActions && !RealInDryRun.Contains(g.Intent)) { Routes.Add(g.Intent); return Done(AceAnswer.Say("[dry run] " + g.Intent, g.Intent)); }
                var got = await skill(turn).ConfigureAwait(false);
                if (got != null) { Routes.Add(g.Intent); return Done(got); }
                needed = Math.Max(needed, 0.7);   // the second guess has to be quite sure
            }

            // Everything else: think it through.
            var thought = await ThinkAsync(turn).ConfigureAwait(false);
            Routes.Add(thought.BankId.Length > 0 ? "bank:" + thought.BankId : thought.Source);
            return Done(thought);
        }

        /// <summary>Skills that only touch Ace's own lists and answers, so tests run them for real.</summary>
        private static readonly HashSet<string> RealInDryRun = new()
        {
            "correct", "recipe", "cook", "homework", "translate", "trivia", "twentyq", "notes", "shopping", "memory.list", "memory.clear",
            "remember", "forget", "convert.all", "currency", "reminder", "reminder.list", "reminder.cancel", "script", "script.explain", "trivia.scores",
        };

        private static readonly HashSet<string> ActionIntents = new()
        {
            "app.open", "app.close", "app.minimize", "app.maximize", "app.resize", "browser.open", "browser.search", "browser.tab",
            "power.shutdown", "power.restart", "power.sleep", "power.lock", "media", "volume", "screenshot.take",
            "calendar.add", "calendar.edit", "calendar.delete", "timer", "reminder", "remember", "forget", "check.now",
        };

        private AceAnswer Done(AceAnswer a)
        {
            if (string.IsNullOrWhiteSpace(a.Speak)) a.Speak = Composer.ForSpeech(a.Text);
            return a;
        }

        public static string Short(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd() + "…";

        // ===================================================== Rules table

        private sealed record Rule(string Intent, Regex Pattern, Func<Turn, Task<AceAnswer?>> Run);

        private List<Rule>? _rules;
        private Dictionary<string, Func<Turn, Task<AceAnswer?>>>? _skills;

        private List<Rule> Rules => _rules ??= AllRules();
        private Dictionary<string, Func<Turn, Task<AceAnswer?>>> Skills => _skills ??= BuildSkills();

        private static Regex R(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>The older rules with the upgrade rules slotted in (clearing memory before "forget", the rest after "correct").</summary>
        private List<Rule> AllRules()
        {
            var list = BuildRules();
            var upgrades = UpgradeRules();
            var clear = upgrades.First(r => r.Intent == "memory.clear");
            upgrades.Remove(clear);
            list.Insert(list.FindIndex(r => r.Intent == "forget"), clear);
            list.InsertRange(list.FindIndex(r => r.Intent == "correct") + 1, upgrades);
            return list;
        }

        private List<Rule> BuildRules() => new()
        {
            // Order matters: the first rule whose skill returns an answer wins.
            new("help", R(@"^(help(?! me \w)|what can you do|what are your (skills|commands|abilities)|commands|how do i use you)\b"), HelpAsync),
            new("remember", R(@"^(remember|note that|learn|don'?t forget|keep in mind)\b(?! (when|what|how|where|who|if))"), RememberAsync),
            new("forget", R(@"^forget\b(?!.*\bhabits\b)|^delete (that|the last) memory\b"), ForgetAsync),
            new("correct", R(@"^(no|nope|wrong|incorrect|that'?s (wrong|not right|incorrect|not true))[,.!]*\s+(it'?s|it is|the answer is|actually|it was)\s+.+"), CorrectAsync),
            new("power.shutdown", R(@"^(shut ?down|power (off|down)|turn off)( the| my| this)?( pc| computer| laptop| system| machine| windows| everything)?( now| right now)?$"), ShutdownAsync),
            new("power.restart", R(@"^(restart|reboot)( the| my| this)?( pc| computer| laptop| system| machine| windows)?( now| right now)?$"), RestartAsync),
            new("power.sleep", R(@"^(put )?(the |my )?(pc|computer|laptop) (to )?sleep$|^sleep (the |my )?(pc|computer|laptop)$|^(go to )?sleep mode$"), SleepPcAsync),
            new("power.lock", R(@"^lock( the| my| this)?( pc| computer| screen| laptop| it)?( now)?$"), LockAsync),
            new("focus", R(@"^(?!.*\bremind me\b)(?:.*\b(?:pomodoro|focus (?:timer|mode|session|time|round)s?|study (?:timer|session|mode)|homework (?:timer|mode))\b.*|(?:(?:let'?s|time to|i (?:need|have|want) to|i gotta|i wanna|help me|it'?s time to)\s+)?(?:focus|study|concentrate)(?:\s+(?:for|on)\s+.+)?|.*\bhow (?:much|long) (?:did|have) i (?:been )?(?:focus|stud).*|(?:pause|resume|unpause|skip|stop|end|cancel)\s+(?:the\s+|my\s+|this\s+)?(?:focus|pomodoro|break|study session)\b.*|.*\bfocus stats\b.*)$"), FocusAsync),
            new("report", R(@"\b(?:weekly|week'?s|this week'?s) (?:pc |computer )?(?:health )?(?:report|summary|grade)\b|\b(?:pc|computer|laptop)(?:'?s)? (?:health )?(?:report|grade|report card)\b|\bhealth (?:report|check ?up|grade)\b|\breport card\b|\bhow healthy is my (?:pc|computer|laptop)\b|\bgrade my (?:pc|computer|laptop)\b"), HealthReportAsync),
            new("habits", R(@"\b(?:what|which) apps? do i (?:usually|normally|always|often) (?:use|open|play|start)\b|\b(?:my|app) habits\b|\bwhat do i (?:usually|normally) (?:open|use|play)\b|\b(?:stop|quit|don'?t|no more|start|turn on|turn off|disable|enable)\b.*\b(?:suggesting|offering|suggest|offer)(?:ing)? (?:apps|to open apps|to open)\b|\bapp suggestions?\b"), HabitsAsync),
            new("timer.cancel", R(@"\b(cancel|stop|clear|delete)\b.*\btimers?\b"), CancelTimersAsync),
            new("timer", R(@"\btimer\b|^count ?down\b"), TimerAsync),
            new("reminder.list", R(@"\b(what|which|any|list|show)\b.*\breminders?\b"), ListRemindersAsync),
            new("reminder", R(@"\bremind me\b|\bset (a |an )?reminder\b"), ReminderAsync),
            new("screenshot.take", R(@"^(take|grab|capture|snap|make) (a |me a )?(screen ?shot|snip|picture of (my|the) screen)|^screen ?shot( this| it| now)?$|^capture (my |the )?screen$|^snip\b"), TakeScreenshotAsync),
            new("screenshot.search", R(@"\bscreen ?shots?\b|\bsnips\b"), FindScreenshotsAsync),
            new("browser.tab", R(@"\b(close|shut)( this| the| that| current| my)? tab\b|\b(next|previous|prev|last|other) tab\b|\breopen\b|\b(refresh|reload)( the| this)?( page| tab)?$|^go (back|forward)$|^(open )?(a )?new tab$|\b(go to|switch to|open) tab (\d|one|two|three|four|five|six|seven|eight|nine)\b|^zoom (in|out)$|^reset zoom$|\bswitch tabs?\b"), TabAsync),
            new("browser.search", R(@"^(google|bing|search (google|youtube|amazon|reddit|wikipedia|github|images|maps|twitch|ebay|roblox|spotify|bing|duckduckgo|the web|the internet|online)( for)?|search (for )?.+ on (youtube|amazon|reddit|wikipedia|github|google|images|maps|twitch|ebay|roblox|spotify)$|youtube search)\b"), BrowserSearchAsync),
            new("recipe", R(@"\brecipes?\b|\bcookbook\b|^(?:how (?:do i|do you|to|can i|should i|would i)|teach me (?:how )?to|show me how to|help me|i want to|i wanna|i'?d like to|let'?s|can we|we should)\s+(?:cook|bake|grill|roast|fry|boil|make|whip up|prepare)\s+.+|^(?:what|which)(?: \w+)? (?:can|could|should|do) (?:i|we) (?:make|cook|bake|eat)\b|^i (?:have|got|only have) .+ (?:what|anything) (?:can|could) i (?:make|cook|bake)\b|^(?:what'?s|whats) for (?:breakfast|lunch|dinner|dessert)\b|^surprise me\b|^i'?m hungry$|^(?:what|which) (?:recipes|dishes|foods?) (?:do you know|can you)|^what can you (?:cook|bake)\b|^(?:bake|cook|whip up|grill|roast)\s+.+|^make (?:me |us )?(?:some |a |an )?\w+(?: \w+){0,3}$"), RecipeAsync),
            new("calendar.list", R(@"^(?!(?:please )?(?:add|put|schedule|create|make|book|set up|plan)\b).*?(?:\b(what'?s|what is|anything|show( me)?|check|read) (on )?my (calendar|schedule|agenda)\b|\bwhat do i have (today|tomorrow|on|this|next|going on)\b|\bam i (busy|free)\b|\bmy (calendar|schedule|agenda)( for| today| tomorrow| this)\b|\bwhat'?s (next|coming up)( on my calendar| for me)?\??$|\bany (events|plans|appointments)\b)"), CalendarListAsync),
            new("calendar.delete", R(@"^(delete|remove|cancel|clear|erase|get rid of)\b.*\b(event|appointment|meeting|calendar|practice|party|class|lesson|game|session)\b|\b(take|remove) .+ off (my|the) calendar\b"), CalendarDeleteAsync),
            new("calendar.edit", R(@"^(move|reschedule|change|rename|push|postpone|shift|bump|edit|update)\b.*\b(to|back|forward|earlier|later|instead|by)\b"), CalendarEditAsync),
            new("calendar.add", R(@"\b(add|put|schedule|create|make|book|set up|plan)\b.*\b(calendar|event|appointment|meeting)\b|^schedule\b|\bon my calendar\b|\bto my calendar\b|^(add|put|book|plan|set up)\b.+\b(today|tonight|tomorrow|monday|tuesday|wednesday|thursday|friday|saturday|sunday|next (week|month)|this (weekend|week)|at (\d|noon|midnight)|\d{1,2}(:\d\d)? ?(am|pm)|on (the )?\d{1,2}(st|nd|rd|th)?|on (jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*)\b"), CalendarAddAsync),
            new("sleep.bedtime", R(@"\b(bed ?time|go to (bed|sleep)|sleep schedule|how (much|long) (do|am|did) i (usually )?sleep|when (do|should) i (usually )?(sleep|wake|get up)|how much sleep)\b"), BedtimeAsync),
            new("app.list", R(@"\b(what|which)( apps| programs| windows| stuff)( are| is)? (open|running)\b|^list( my)?( open)? (apps|windows|programs)\b|^what'?s (open|running)( right now)?$"), ListAppsAsync),
            new("app.minimize", R(@"^(minimi[sz]e|hide)\b|^show( me)? (the )?desktop$"), MinimizeAsync),
            new("app.maximize", R(@"^(maximi[sz]e|restore|un-?minimi[sz]e)\b|^make .+ (full ?screen|maximi[sz]ed|big)$"), MaximizeAsync),
            new("app.resize", R(@"^(resize|snap|move|put|dock|make|center|centre)\b.+\b(left|right|top|bottom|center|centre|middle|half|corner|third|bigger|smaller|larger|wider|taller|narrower|shorter|\d+ ?(x|by) ?\d+)\b"), ResizeAsync),
            new("app.close", R(@"^(close|quit|exit|kill|end|shut down|stop running) (?!.*\btabs?\b).+"), CloseAppAsync),
            new("app.open", R(@"^(open|launch|start|run|fire up|bring up|pull up|load|go to|take me to|visit|navigate to) .+"), OpenAsync),
            new("media", R(@"^(play|pause|resume|unpause|stop)( the| my)?( music| song| video| track| it| playback| spotify)?$|^(next|skip)( song| track| this( song| track)?)?$|^(previous|last|go back a)( song| track)$|\b(skip|next) (this )?(song|track)\b"), MediaAsync),
            new("volume", R(@"\bvolume\b|^(un)?mute\b|\b(louder|quieter|softer)\b|^turn (it|the sound|sound) (up|down)$"), VolumeAsync),
            new("file.search", R(@"^(find|locate|look for|search for|search my (files|pc|computer|drive|downloads|desktop|documents) for|where('?s| is| are| did i (put|save))|show me( my)?)\b.+\b(file|files|doc|docs|document|documents|pdf|pdfs|folder|video|videos|photo|photos|picture|pictures|image|images|song|songs|stl|stls|blend|zip|download|downloads|spreadsheet|slides|presentation|model|models|3mf|exe|installer|script)\b|^search my (files|pc|computer|drive) for\b"), FindFilesAsync),
            new("notifications", R(@"\bnotifications?\b|\bdid i miss anything\b|\banything (i need to|i should|to) (see|know|check)\b|\bany (new )?messages\b"), NotificationsAsync),
            new("check.now", R(@"^(check|scan)( my| the)? (pc|computer|everything|stuff|things|system)( now)?$|^(do|run)( a| your)? (check|checks|scan)( now)?$|^check now$|^wake up( and check.*)?$|^look around$"), CheckNowAsync),
            new("battery", R(@"\bbattery\b"), BatteryAsync),
            new("storage", R(@"\b(disk|drive|storage|ssd|hdd|space)\b.*\b(free|left|full|check|much|have|low)\b|\b(how much|check|check on|look at)( my| the)?( free)? (disk |drive )?(space|storage)\b|\b(big|large|huge|biggest|largest)( files| downloads)\b|\bfree up (some )?space\b|\bwhat'?s taking up (my )?space\b|\brecycle bin\b"), StorageAsync),
            new("system.status", R(@"\b(cpu|gpu|ram)\b|\bhow('?s| is)( my)? (pc|computer|laptop|system)( doing| running)?\b|\bsystem status\b|\b(pc|computer|laptop)( is)?( running)? (slow|hot|laggy)\b|\bwhat'?s using (my |the |all )?(ram|memory|cpu)\b|\b(temps?|temperatures?)\b.*\b(pc|cpu|gpu|computer)\b|\bfan speed\b|\binternet speed\b|\bupload|download speed\b"), SystemStatusAsync),
            new("time", R(@"^(what )?time( is it)?( right now| now)?\??$|^what'?s the time|\b(what|which)('?s| is) (the )?(date|day)( today| is it)?\b|\bwhich (date|day) is (it|today)\b|\bwhat date is (it|today)\b|\bwhat day is (it|today)\b|\bwhat year is it\b|\bhow (many|long) (days|weeks|months|hours|sleeps) (until|till|til|to|before)\b|\bwhat time is it in\b|\btime in [a-z ]+$"), TimeAsync),
            new("weather", R(@"\b(weather|forecast|umbrella|raining|snowing|rain|snow)\b|\b(how|is it) (hot|cold|warm|chilly) (is it )?(outside|out)\b|\btemperature (outside|today|tomorrow|now|right now|in)\b"), WeatherAsync),
            new("define", R(@"^(define|definition of|meaning of|what'?s the meaning of|what'?s the definition of)\s+\S+|^what does (the word )?['""]?[a-z-]+['""]? mean\??$|^how do you spell\s+\S+"), DefineAsync),
            new("web.search", R(@"^(look up|search (the )?(web|internet) for|find out|google it|search it up|search online for|check online|research)\b"), WebSearchAsync),
        };

        private Dictionary<string, Func<Turn, Task<AceAnswer?>>> BuildSkills() => new()
        {
            ["calendar.add"] = CalendarAddAsync,
            ["calendar.edit"] = CalendarEditAsync,
            ["calendar.delete"] = CalendarDeleteAsync,
            ["calendar.list"] = CalendarListAsync,
            ["power.shutdown"] = ShutdownAsync,
            ["power.restart"] = RestartAsync,
            ["power.sleep"] = SleepPcAsync,
            ["power.lock"] = LockAsync,
            ["app.open"] = OpenAsync,
            ["app.close"] = CloseAppAsync,
            ["app.minimize"] = MinimizeAsync,
            ["app.maximize"] = MaximizeAsync,
            ["app.resize"] = ResizeAsync,
            ["app.list"] = ListAppsAsync,
            ["browser.open"] = OpenAsync,
            ["browser.search"] = BrowserSearchAsync,
            ["browser.tab"] = TabAsync,
            ["file.search"] = FindFilesAsync,
            ["screenshot.search"] = FindScreenshotsAsync,
            ["web.search"] = WebSearchAsync,
            ["recipe"] = RecipeAsync,
            ["weather"] = WeatherAsync,
            ["define"] = DefineAsync,
            ["system.status"] = SystemStatusAsync,
            ["storage"] = StorageAsync,
            ["notifications"] = NotificationsAsync,
            ["sleep.bedtime"] = BedtimeAsync,
            ["timer"] = TimerAsync,
            ["reminder"] = ReminderAsync,
            ["time"] = TimeAsync,
            ["media"] = MediaAsync,
            ["volume"] = VolumeAsync,
            ["screenshot.take"] = TakeScreenshotAsync,
            ["remember"] = RememberAsync,
            ["forget"] = ForgetAsync,
            ["check.now"] = CheckNowAsync,
            ["help"] = HelpAsync,
            ["focus"] = FocusAsync,
            ["report"] = HealthReportAsync,
            ["habits"] = HabitsAsync,
        };
    }

    /// <summary>Splits "open spotify and then search youtube for lofi" into separate steps.</summary>
    public static class Planner
    {
        private const string Verbs = "open|close|launch|start|minimi[sz]e|maximi[sz]e|resize|snap|search|google|look up|find|play|pause|resume|skip|set|remind|add|schedule|delete|remove|move|reschedule|turn|mute|unmute|lock|restart|shut|take|show|tell|check|what|what's|whats|who|how|when|where|why|is|are|does|do|can|define|convert|calculate|go|put|make|empty|give|read|create|book|cancel|quit|exit|kill|hide|snap|run";

        public static List<string> Split(string text)
        {
            string t = text.Trim();
            var parts = new List<string>();
            // Sentence breaks and "and then"/"then"/"also"/"after that" always split.
            foreach (var sentence in Regex.Split(t, @"(?<=[.!?;])\s+(?=[A-Za-z])|\s*;\s*|,?\s+(?:and then|then|and also|also|after that)\s+(?=\S)", RegexOptions.IgnoreCase))
            {
                // " and " splits only when a new command starts after it ("open spotify and play music"),
                // not inside a name or question ("salt and pepper", "capital and currency of France").
                var pieces = Regex.Split(sentence, @",?\s+and\s+(?=(?:" + Verbs + @")\b)", RegexOptions.IgnoreCase);
                foreach (var p in pieces)
                {
                    string piece = p.Trim().Trim(',', '.', ' ');
                    if (piece.Length > 0) parts.Add(piece);
                }
            }
            // "open spotify and discord": share the verb.
            var shared = new List<string>();
            foreach (var p in parts)
            {
                var m = Regex.Match(p, @"^(open|close|launch|minimi[sz]e|maximi[sz]e|quit|start)\s+(.+?)\s+and\s+(.+)$", RegexOptions.IgnoreCase);
                if (m.Success && !Regex.IsMatch(m.Groups[3].Value, @"^(the )?(tab|window)s?$", RegexOptions.IgnoreCase) && m.Groups[2].Value.Split(' ').Length <= 3 && m.Groups[3].Value.Split(' ').Length <= 3)
                {
                    shared.Add(m.Groups[1].Value + " " + m.Groups[2].Value);
                    shared.Add(m.Groups[1].Value + " " + m.Groups[3].Value);
                }
                else shared.Add(p);
            }
            if (shared.Count == 0) shared.Add(t);
            return shared;
        }
    }
}
