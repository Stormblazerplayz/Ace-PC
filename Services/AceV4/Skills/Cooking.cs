using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Nimbus.AceV4.Brain;
using Nimbus.AceV4.Pc;
using Nimbus.AceV4.Web;

namespace Nimbus.AceV4
{
    /// <summary>
    /// Recipes and cooking mode. Recipes come from Ace's built-in cookbook first (works with no internet), then the web.
    /// Cooking mode reads one step at a time ("next", "go back", "repeat", "step 5"), and any step with a wait
    /// ("Bake for 25 minutes", "Chill for 1 to 2 hours") gets a timer set to exactly that time.
    /// </summary>
    public sealed partial class AceV4Brain
    {
        private const RegexOptions Ci = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        /// <summary>Foods that make "how do I make X" a recipe question even when the cookbook doesn't have X.</summary>
        private static readonly Regex FoodWords = new(@"\b(cookies?|cakes?|pancakes?|waffles?|pasta|spaghetti|pizza|bread|brownies?|muffins?|cupcakes?|lasagna|soup|stew|smoothies?|tacos?|burritos?|burgers?|fries|toast|eggs?|omelett?e|cheesecake|pie|curry|rice|chili|salad|sandwich(es)?|lemonade|chocolate|ramen|noodles|guacamole|salsa|rolls|dumplings|sushi|chicken|beef|pork|steak|fish|salmon|shrimp|tofu|potato(es)?|fudge|candy|icing|frosting|cereal bars?|granola|oatmeal|porridge|pudding|jelly|jam|dip|sauce|gravy|stir ?fry|meatballs|nuggets|wings|kebabs?|biscuits?|scones?|crepes?|donuts?|doughnuts?|bagels?|pretzels?|tea|coffee|milkshake|shake|juice|ice cream|popsicles?|cocoa|nachos|quesadillas?|enchiladas?|mac and cheese|dessert|snack|breakfast|lunch|dinner|meal)\b", Ci);

        private static readonly Regex CookbookList = new(@"^(?:what|which) (?:recipes|dishes|foods?) (?:do you know|can you (?:make|do|show me)|are in your cookbook|do you have)|^(?:list|show me|read me) (?:all )?(?:your |the )?(?:recipes|cookbook)$|^what'?s in your cookbook|^what can you (?:cook|bake)\b", Ci);
        private static readonly Regex WithWhat = new(@"^(?:what|which)(?: \w+)? (?:can|could|should|do) (?:i|we) (?:make|cook|bake)(?: \w+)? with (?<have>.+)$|^i (?:have|got|only have) (?<have2>.+?)[,.]? (?:what|anything) (?:can|could) i (?:make|cook|bake)\b", Ci);
        private static readonly Regex Suggest = new(@"^what (?:should|can|could|do) (?:i|we) (?:make|cook|bake|eat|have)(?: (?:for|as) (?:a |an )?(?<kind>breakfast|lunch|dinner|dessert|snack|drink|treat|side))?(?: today| tonight| now| this weekend)?$|^(?:give|show|find) (?:me |us )?(?:a |an |some )?(?:random |good |easy |new )?(?<kind2>breakfast|lunch|dinner|dessert|snack|drink|baking|treat)? ?(?:recipe|idea|recipe idea)s?$|^surprise me(?: with (?:a |an )?(?<kind3>breakfast|lunch|dinner|dessert|snack|drink)?\s*recipe)?$|^(?:what'?s|whats) for (?<kind4>breakfast|lunch|dinner|dessert)(?: tonight| today)?$|^i'?m hungry$", Ci);

        // ===================================================== Recipes

        private async Task<AceAnswer?> RecipeAsync(Turn t)
        {
            string l = t.Lower.Trim().TrimEnd('?', '.', '!');
            // "Add recipe night to my calendar" isn't a recipe request.
            if (Regex.IsMatch(l, @"\b(calendar|schedule|remind me|reminder)\b")) return null;

            if (CookbookList.IsMatch(l)) return CookbookAnswer();

            var with = WithWhat.Match(l);
            if (with.Success) return WithAnswer(with.Groups["have"].Success && with.Groups["have"].Length > 0 ? with.Groups["have"].Value : with.Groups["have2"].Value);

            var suggest = Suggest.Match(l);
            if (suggest.Success)
            {
                string kind = new[] { "kind", "kind2", "kind3", "kind4" }.Select(k => suggest.Groups[k].Value).FirstOrDefault(v => v.Length > 0) ?? "";
                return SuggestAnswer(kind, bake: Regex.IsMatch(l, @"\bbake|baking\b"));
            }

            bool saidRecipe = Regex.IsMatch(l, @"\b(recipes?|cook|cooking|bake|baking|cookbook)\b");
            string dish = DishFrom(l);
            if (dish.Length < 2) return AceAnswer.Say(Composer.Pick("recipe.what", new[] { "What do you want to make?", "Sure! What should we make?", "What are you hungry for?" }), "recipe", 0.5);

            var built = Cookbook.Find(dish);
            if (built != null)
            {
                Step("Found it in my cookbook", built.Name);
                return RecipeAnswer(built, "");
            }

            // "How do I make a Minecraft server?" is not a recipe question.
            if (!saidRecipe && !FoodWords.IsMatch(dish) && Cookbook.With(dish).Count == 0) return null;

            if (!WebClient.Disabled)
            {
                Step("Not in my cookbook, so I'm searching the web", dish);
                Say("Finding a recipe…");
                var found = await WebClient.RecipeAsync(dish, s => { Step(s); Say(s + "…"); }, t.Ct).ConfigureAwait(false);
                if (found.Recipe is { } card)
                {
                    var r = new Recipe
                    {
                        Id = "web", Name = card.Title.Length > 0 ? card.Title : Composer.Capital(dish), Time = card.TotalTime, Serves = card.Yield,
                        Ingredients = card.Ingredients.ToList(), Steps = card.Steps.ToList(), Source = card.Source, Url = card.Url, ImageUrl = card.ImageUrl,
                        GrownUp = card.Steps.Any(s => Regex.IsMatch(s, @"\b(oven|stove|bake|fry|boil|simmer|knife|grill|roast)\b", Ci)),
                    };
                    Step("Read the recipe", $"{r.Ingredients.Count} ingredients, {r.Steps.Count} steps");
                    return RecipeAnswer(r, "");
                }
                if (found.Results.Count > 0)
                {
                    var links = AceAnswer.Say($"I found some {dish} recipes online but couldn't read their ingredient lists. Here are the links.", "recipe", 0.6);
                    links.Cards.Add(WebCard("Recipes", found.Results));
                    return links;
                }
            }

            var close = Cookbook.With(dish).Take(3).Select(r => r.Name).ToList();
            string offer = close.Count > 0 ? $" I do have {Composer.List(close)}." : $" I know {Cookbook.Count} recipes, like pancakes, brownies and tacos. Say \"what recipes do you know\" to see them all.";
            var none = AceAnswer.Say($"I don't have a {dish} recipe in my cookbook yet, and I can't reach the web right now.{offer}", "recipe", 0.3);
            none.Mood = "confused";
            return none;
        }

        /// <summary>The dish in "how do I make banana bread", "i want to bake brownies", "recipe for mac n cheese", "guac recipe".</summary>
        private static string DishFrom(string l)
        {
            string d = l;
            d = Regex.Replace(d, @"^(?:hey |ok |okay |so |um )+", "");
            d = Regex.Replace(d, @"^(?:can you |could you |please )?(?:show me|give me|find me|find|get me|send me|look up|search for|i want|i need|i'?d like|tell me)\s+", "");
            d = Regex.Replace(d, @"^(?:how (?:do i|do you|to|can i|should i|would i)|teach me (?:how )?to|show me how to|help me|i want to|i wanna|i'?d like to|let'?s|can we|we should)\s+(?:cook|bake|grill|roast|fry|boil|make|whip up|prepare)\s+", "");
            d = Regex.Replace(d, @"^(?:the |a |an )?recipes? (?:for|of|to make|to cook|to bake)\s+", "");
            d = Regex.Replace(d, @"\b(?:a|an|some|the|good|best|easy|simple|quick|recipes?|homemade|home made|yummy|tasty|really|super)\b", " ");
            d = Regex.Replace(d, @"\b(?:from scratch|at home|for (?:me|us|dinner|lunch|breakfast|dessert|tonight|today)|today|tonight|please|step by step)\b", " ");
            d = Regex.Replace(d, @"^(?:how to|to)\s+(?:make|cook|bake)\s+", "");
            d = Regex.Replace(d, @"^(?:for|of)\s+", "");
            return Regex.Replace(d, @"\s+", " ").Trim(' ', '?', '.', '!', ',');
        }

        /// <summary>A recipe with its card (ingredients, numbered steps, timer buttons) and a short spoken intro.</summary>
        private AceAnswer RecipeAnswer(Recipe r, string intro)
        {
            Talk.LastRecipe = r;
            Talk.LastRecipeAt = DateTime.Now;
            var card = new RecipeCard
            {
                Title = r.Name, Source = r.Source, Url = r.Url, ImageUrl = r.ImageUrl, Yield = r.Serves, TotalTime = r.Time,
                Ingredients = r.Ingredients.ToList(), Steps = r.Steps.ToList(),
            };
            int waits = r.Steps.Count(s => StepTimer.Read(s) != null);
            string about = string.Join(" and ", new[] { r.Time.Length > 0 ? "takes " + r.Time : "", r.Serves.Length > 0 ? "makes " + r.Serves : "" }.Where(x => x.Length > 0));
            string opener = Composer.Pick("recipe.found", new[]
            {
                "Here's {0}.", "Ooh, {0}! Here you go.", "Got one: {0}.", "Here's how to make {0}.", "Let's make {0}!",
                "Here's my recipe for {0}.", "Yum, {0}. Here it is.", "Found it: {0}.", "{0}, coming right up.", "Here's a good one: {0}.",
            }).Replace("{0}", r.Name);
            string text = (intro.Length > 0 ? intro + " " : "") + opener + (about.Length > 0 ? $" It {about}." : "") +
                $" You'll need {r.Ingredients.Count} ingredients and there are {r.Steps.Count} steps." +
                (waits > 0 ? $" {Composer.Plural(waits, "step has", "steps have")} a timer button." : "") +
                " Say \"start cooking\" and I'll walk you through it." +
                (r.GrownUp ? " Ask a grown-up to help with the hot or sharp parts." : "");
            var a = AceAnswer.Say(text, "recipe");
            a.Speak = (intro.Length > 0 ? intro + " " : "") + opener + (r.Time.Length > 0 ? $" It takes {r.Time}." : "") + " The ingredients and steps are on screen. Say start cooking and I'll read them one at a time.";
            a.Cards.Add(card);
            a.Mood = "love";
            return a;
        }

        private AceAnswer CookbookAnswer()
        {
            if (Cookbook.Count == 0) return AceAnswer.Say("My cookbook is missing, but I can still look up recipes online.", "recipe", 0.5);
            var groups = new[] { ("breakfast", "Breakfast"), ("lunch", "Lunch"), ("dinner", "Dinner"), ("side", "Sides"), ("snack", "Snacks"), ("dessert", "Desserts"), ("drink", "Drinks") };
            var lines = groups.Select(g => (g.Item2, Cookbook.OfKind(g.Item1))).Where(g => g.Item2.Count > 0)
                .Select(g => $"{g.Item1}: {string.Join(", ", g.Item2.Select(r => r.Name))}.");
            var a = AceAnswer.Say($"I know {Cookbook.Count} recipes by heart, and I can look up any other one online.\n" + string.Join("\n", lines), "recipe");
            a.Speak = $"I know {Cookbook.Count} recipes, like pancakes, tacos, brownies and smoothies. They're all listed on screen.";
            return a;
        }

        private AceAnswer WithAnswer(string have)
        {
            var list = Cookbook.With(have).Take(5).ToList();
            if (list.Count == 0)
                return AceAnswer.Say($"Nothing in my cookbook uses {have.Trim()}. Try asking for a recipe by name, like \"how do I make pancakes\".", "recipe", 0.4);
            if (list.Count == 1) return RecipeAnswer(list[0], $"With {have.Trim()} you could make {list[0].Name}.");
            Talk.LastResults = list.Select(r => new ResultItem { Title = r.Name, Subtitle = r.Time }).ToList();
            var options = list;
            Talk.PendingFollow = turn =>
            {
                string l = turn.Lower.Trim().TrimEnd('?', '.', '!');
                int pick = Regex.Match(l, @"\b(first|1st|one|1)\b").Success ? 0 : Regex.Match(l, @"\b(second|2nd|two|2)\b").Success ? 1 :
                           Regex.Match(l, @"\b(third|3rd|three|3)\b").Success ? 2 : Regex.Match(l, @"\b(fourth|4th|four|4)\b").Success ? 3 :
                           Regex.Match(l, @"\b(fifth|5th|five|5|last)\b").Success ? options.Count - 1 : -1;
                if (pick < 0)
                {
                    var named = Cookbook.Find(DishFrom(l));
                    pick = named != null ? options.IndexOf(named) : -1;
                    if (pick < 0 && named != null && Regex.IsMatch(l, @"^(?:the |make |let'?s make |i want )?\w+( \w+){0,3}$")) return Task.FromResult<AceAnswer?>(RecipeAnswer(named, ""));
                }
                return Task.FromResult<AceAnswer?>(pick >= 0 && pick < options.Count ? RecipeAnswer(options[pick], "") : null);
            };
            Talk.PendingFollowAt = DateTime.Now;
            string names = Composer.List(list.Select(r => r.Name).ToList(), "or");
            var a = AceAnswer.Say(Composer.Pick("recipe.with", new[]
            {
                "With {0} you could make {1}. Which one?",
                "{0}? You could make {1}. Pick one!",
                "Good news: {0} is enough for {1}. Which sounds best?",
            }).Replace("{0}", have.Trim()).Replace("{1}", names), "recipe");
            var card = new ResultsCard { Title = "You could make" };
            foreach (var r in list) card.Items.Add(new ResultItem { Title = r.Name, Subtitle = r.Time + (r.Serves.Length > 0 ? " · makes " + r.Serves : "") });
            a.Cards.Add(card);
            return a;
        }

        private AceAnswer SuggestAnswer(string kind, bool bake)
        {
            kind = kind.ToLowerInvariant() switch { "treat" => "dessert", "baking" => "", _ => kind.ToLowerInvariant() };
            IEnumerable<Recipe> pool = kind.Length > 0 ? Cookbook.OfKind(kind) : Cookbook.All;
            if (bake) pool = pool.Where(r => r.Steps.Any(s => Regex.IsMatch(s, @"\bbake\b", Ci)));
            if (kind.Length == 0 && !bake && DateTime.Now.Hour < 11) pool = Cookbook.OfKind("breakfast");
            var list = pool.Where(r => r != Talk.LastRecipe).ToList();
            if (list.Count == 0) list = Cookbook.All.ToList();
            if (list.Count == 0) return AceAnswer.Say("My cookbook is empty right now, but ask me for any recipe and I'll look it up.", "recipe", 0.4);
            var pick = list[Random.Shared.Next(list.Count)];
            string intro = Composer.Pick("recipe.suggest", new[] { "How about {0}?", "Let's try {0}!", "I vote {0}.", "{0} sounds good to me!", "Ooh, what about {0}?" }).Replace("{0}", pick.Name);
            return RecipeAnswer(pick, intro);
        }

        // ===================================================== Cooking mode

        private static readonly Regex CookStart = new(@"^(?:ok(?:ay)? |alright |yes )?(?:let'?s |can we |i want to |i'?m ready to )?(?:start|begin|go into|turn on|enter)?\s*(?:cooking|baking|cook mode|cooking mode|cooking time)(?: mode)?(?: (?:for|with) (?<d>.+))?(?: now| please)?$|^(?:let'?s |ok let'?s )(?:cook|make|bake) (?:it|this|that|them)(?: now)?$|^(?:walk|talk) me through (?:it|this|that|the steps|the recipe)$|^(?:start|begin) (?:cooking|baking|making) (?<d2>.+)$|^(?:cook|bake|make) (?<d3>.+) with me$|^(?:read|go through) (?:me )?the steps(?: one at a time)?$", Ci);
        private static readonly Regex CookNext = new(@"^(?:(?:ok(?:ay)?|alright|done|got it|yes|yep|cool)[ ,.!]+)?(?:next|next step|what'?s next|whats next|what now|then what|and then|continue|go on|keep going|i'?m done|done|finished|ready|i did it|did it)(?: step)?(?: please)?$", Ci);
        private static readonly Regex CookBack = new(@"^(?:go )?back(?: a step| one(?: step)?)?$|^previous(?: step)?$|^(?:go to the |the )?last step$|^step back$|^go to the previous step$|^undo$", Ci);
        private static readonly Regex CookRepeat = new(@"^(?:repeat(?: that| it| the step| step)?|say (?:that|it) again|again|what was that|read (?:it|that|the step) again|what step (?:am i|are we) on|where (?:am i|are we|was i)|what'?s the step|current step|what do i do(?: now)?)$", Ci);
        private static readonly Regex CookGoTo = new(@"^(?:go to |skip to |jump to |read |what'?s |whats )?step (?:number )?(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)$", Ci);
        private static readonly Regex CookTimer = new(@"^(?:ok |yes |yeah )?(?:start|set|begin|run|turn on)(?: the| a| my| that)?(?: \w+)? timer(?: for (?:it|this|that|this step))?(?: please| now)?$|^(?:start|set) (?:it|that)$|^timer(?: please)?$|^time (?:it|this)$", Ci);
        private static readonly Regex CookIngredients = new(@"^(?:what do i need|what will i need|what (?:are|were) the ingredients|(?:the )?ingredients|read (?:me )?the ingredients|what goes in(?: it)?|what'?s in it|what do i need for it|shopping list)$", Ci);
        private static readonly Regex CookStop = new(@"^(?:stop|exit|quit|end|leave|close|turn off|cancel) (?:the )?(?:cooking|cook mode|cooking mode|baking)(?: mode)?$|^(?:i'?m )?(?:done|finished) (?:cooking|baking)$|^we'?re done$", Ci);

        private static int StepNumber(string n) => int.TryParse(n, out int v) ? v : Array.IndexOf(new[] { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve" }, n.ToLowerInvariant());

        /// <summary>Cooking-mode commands. Runs first, so "next" means the next step (not the next song) while you're cooking.</summary>
        private Task<AceAnswer?> CookingAsync(Turn t)
        {
            string l = t.Lower.Trim().TrimEnd('?', '.', '!');
            var session = Talk.Cooking;
            if (session != null && (session.Finished || DateTime.Now - session.LastUsed > TimeSpan.FromHours(3))) session = Talk.Cooking = null;
            var recent = Talk.LastRecipe != null && DateTime.Now - Talk.LastRecipeAt < TimeSpan.FromHours(2) ? Talk.LastRecipe : null;

            var start = CookStart.Match(l);
            if (start.Success)
            {
                string named = new[] { "d", "d2", "d3" }.Select(k => start.Groups[k].Value).FirstOrDefault(v => v.Length > 0) ?? "";
                var recipe = named.Length > 0 ? Cookbook.Find(DishFrom(named)) : session?.Recipe ?? recent;
                if (recipe == null)
                {
                    if (named.Length > 0) return Task.FromResult<AceAnswer?>(null);   // a recipe Ace has to look up first
                    return Task.FromResult<AceAnswer?>(AceAnswer.Say("What are we cooking? Ask me for a recipe first, like \"how do I make pancakes\".", "cook", 0.5));
                }
                Talk.Cooking = new CookingSession(recipe);
                Talk.LastRecipe = recipe;
                Talk.LastRecipeAt = DateTime.Now;
                Step("Cooking mode on", recipe.Name);
                string lead = Composer.Pick("cook.start", new[]
                {
                    "Cooking mode is on! Say \"next\" when you're ready for the next step.",
                    "Let's cook! Say \"next\", \"go back\" or \"repeat\" anytime.",
                    "Apron on! I'll read one step at a time. Just say \"next\".",
                    "Okay, chef! Say \"next\" to move on and \"repeat\" to hear a step again.",
                }) + (recipe.GrownUp ? " Have a grown-up help with the hot or sharp parts." : "");
                return Task.FromResult<AceAnswer?>(StepAnswer(Talk.Cooking, lead));
            }

            if (CookIngredients.IsMatch(l) && (session?.Recipe ?? recent) is { } forList)
            {
                Step("Reading the ingredients", forList.Name);
                var a = AceAnswer.Say($"For {forList.Name} you need: " + string.Join("; ", forList.Ingredients) + ".", "cook");
                a.Speak = $"For {forList.Name} you need " + Composer.List(forList.Ingredients.ToList()) + ".";
                return Task.FromResult<AceAnswer?>(a);
            }

            if (session == null) return Task.FromResult<AceAnswer?>(null);

            if (CookStop.IsMatch(l))
            {
                session.Finish();
                Talk.Cooking = null;
                return Task.FromResult<AceAnswer?>(AceAnswer.Say(Composer.Pick("cook.stop", new[] { "Cooking mode off. Hope it turned out great!", "Okay, I stopped cooking mode. Your timers are still running.", "Done cooking! Enjoy." }), "cook"));
            }
            if (CookNext.IsMatch(l))
            {
                if (!session.Next())
                {
                    session.Finish();
                    Talk.Cooking = null;
                    var done = AceAnswer.Say(Composer.Pick("cook.done", new[] { "That was the last step! Enjoy your {0}.", "All done! Your {0} is ready. Nice work, chef!", "You did it! Enjoy the {0}." }).Replace("{0}", session.Recipe.Name), "cook");
                    done.Mood = "love";
                    return Task.FromResult<AceAnswer?>(done);
                }
                return Task.FromResult<AceAnswer?>(StepAnswer(session, ""));
            }
            if (CookBack.IsMatch(l))
            {
                bool moved = session.Back();
                return Task.FromResult<AceAnswer?>(StepAnswer(session, moved ? "" : "That's the first step."));
            }
            if (CookRepeat.IsMatch(l)) return Task.FromResult<AceAnswer?>(StepAnswer(session, ""));
            var go = CookGoTo.Match(l);
            if (go.Success)
            {
                int n = StepNumber(go.Groups["n"].Value);
                if (n < 1 || n > session.Count) return Task.FromResult<AceAnswer?>(AceAnswer.Say($"This recipe only has {session.Count} steps.", "cook", 0.6));
                session.GoTo(n - 1);
                return Task.FromResult<AceAnswer?>(StepAnswer(session, ""));
            }
            if (CookTimer.IsMatch(l) && !Regex.IsMatch(l, @"\d"))
            {
                var wait = session.Wait;
                if (wait == null) return Task.FromResult<AceAnswer?>(AceAnswer.Say("This step doesn't have a wait, so there's no timer for it. Say \"set a timer for 5 minutes\" if you want one anyway.", "cook", 0.6));
                string said = StartRecipeTimer(session.Recipe.Name, wait);
                var a = AceAnswer.Say(said, "timer");
                a.Cards.Add(new TimersCard { Title = "Your timers" });
                return Task.FromResult<AceAnswer?>(a);
            }
            return Task.FromResult<AceAnswer?>(null);
        }

        private AceAnswer StepAnswer(CookingSession session, string lead)
        {
            var wait = session.Wait;
            string timer = wait == null ? "" : " " + Composer.Pick("cook.timer", new[]
            {
                "Want a {0} {1} timer? Say \"start the timer\" or press the button.",
                "I can time that: say \"start the timer\" for {0}.",
                "There's a {0} timer for this one. Just say \"start the timer\".",
            }).Replace("{0}", StepTimer.Say(wait.Length)).Replace("{1}", wait.Label);
            var a = AceAnswer.Say((lead.Length > 0 ? lead + "\n" : "") + session.Say() + timer, "cook");
            a.Speak = (lead.Length > 0 ? lead + " " : "") + $"Step {session.Step + 1}. {session.Current}" + timer;
            a.Cards.Add(new CookingCard { Session = session });
            a.Mood = "love";
            return a;
        }

        /// <summary>Starts a labeled timer for a recipe step ("Fudgy Brownies: bake"). Several can run at once.</summary>
        private string StartRecipeTimer(string recipeName, StepWait wait)
        {
            string label = $"{recipeName}: {wait.Label}";
            if (!DryRunActions) ReminderStore.Add(label, DateTime.Now + wait.Length, isTimer: true);
            Step("Started a timer", $"{label} · {StepTimer.Say(wait.Length)}");
            return Composer.Pick("cook.timer.on", new[]
            {
                "Started a {0} {1} timer. I'll ping you when it's done.",
                "{0} {1} timer is running. I'll let you know!",
                "Timer on: {0} to {1}. I'll tell you when it's up.",
            }).Replace("{0}", StepTimer.Say(wait.Length)).Replace("{1}", wait.Label);
        }
    }
}
