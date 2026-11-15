using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nimbus.AceV4.Brain
{
    // ============================================================================ Trivia

    public sealed record TriviaQuestion(string Category, string Question, string Answer, string[] Wrong, string Fact = "");

    /// <summary>One game of trivia: questions in a random order, A to D choices and a score.</summary>
    public sealed class TriviaGame
    {
        private static readonly Random Rng = new();
        public string Category { get; }
        public int Score { get; private set; }
        public int Asked { get; private set; }
        /// <summary>Right answers in a row right now, and the most in a row this game.</summary>
        public int Streak { get; private set; }
        public int BestStreak { get; private set; }
        /// <summary>Set once the game was saved to the high scores, so it's only saved once.</summary>
        public bool Saved { get; set; }
        public TriviaQuestion? Current { get; private set; }
        public List<string> Choices { get; } = new();
        public bool Over { get; private set; }
        /// <summary>Raised when the question or score changes, so the card can update.</summary>
        public event Action? Changed;
        private readonly List<TriviaQuestion> _left;

        public TriviaGame(string category = "")
        {
            Category = category;
            var pool = category.Length == 0 ? Trivia.All.ToList() : Trivia.All.Where(q => q.Category == category).ToList();
            if (pool.Count == 0) pool = Trivia.All.ToList();
            _left = pool.OrderBy(_ => Rng.Next()).ToList();
        }

        public bool HasMore => _left.Count > 0;

        /// <summary>Moves on to the next question and returns how to ask it.</summary>
        public string Next()
        {
            if (_left.Count == 0) { Over = true; Current = null; Changed?.Invoke(); return ""; }
            Current = _left[0];
            _left.RemoveAt(0);
            Asked++;
            Choices.Clear();
            Choices.AddRange(Current.Wrong.Append(Current.Answer).OrderBy(_ => Rng.Next()));
            Changed?.Invoke();
            return AskText();
        }

        public string AskText() => Current == null ? "" :
            $"Question {Asked}: {Current.Question} " + string.Join(", ", Choices.Select((c, i) => $"{"ABCD"[i]}) {c}").Take(Choices.Count - 1)) + $" or {"ABCD"[Choices.Count - 1]}) {Choices[^1]}?";

        /// <summary>
        /// Which choice an answer means: "b", "B)", "the second one", "number 2" or the answer itself ("Jupiter").
        /// -1 for "I don't know"/"skip", null when it doesn't look like an answer at all.
        /// </summary>
        public int? ChoiceOf(string said)
        {
            if (Current == null) return null;
            string l = Regex.Replace(said.ToLowerInvariant().Trim(), @"[.!?]+$", "").Trim();
            l = Regex.Replace(l, @"^(is it|it'?s|i think( it'?s)?|my answer is|the answer is|answer|i'?ll go with|i choose|i pick|i say|um+|uh+)\s+", "").Trim();
            if (Regex.IsMatch(l, @"^(i don'?t know|idk|no idea|skip( it| this one)?|pass|next( question)?|dunno|i give up|not sure)$")) return -1;
            var letter = Regex.Match(l, @"^(?:letter |option |choice )?([abcd])\)?$");
            if (letter.Success) return letter.Groups[1].Value[0] - 'a';
            // An answer that is one of the choices wins over "1" meaning the first choice ("How many moons? 1").
            string Plain0(string s) => Regex.Replace(s.ToLowerInvariant(), @"^(the|a|an)\s+|[^a-z0-9 ]", "").Trim();
            for (int i = 0; i < Choices.Count; i++)
                if (Plain0(Choices[i]) == Plain0(l)) return i;
            string[] ords = { "first|1st|one|1", "second|2nd|two|2", "third|3rd|three|3", "fourth|4th|four|4|last" };
            for (int i = 0; i < 4; i++)
                if (Regex.IsMatch(l, @"^(?:the |number |option |choice )?(?:" + ords[i] + @")(?: one| option| choice)?$")) return i == 3 ? Choices.Count - 1 : i;
            string Plain(string s) => Regex.Replace(s.ToLowerInvariant(), @"^(the|a|an)\s+|[^a-z0-9 ]", "").Trim();
            string p = Plain(l);
            for (int i = 0; i < Choices.Count; i++)
                if (Plain(Choices[i]) == p) return i;
            // "neil armstrong" for "Neil Armstrong", "armstrong" for it, "about 8 minutes" for "8 minutes".
            var close = Choices.Select((c, i) => (i, c: Plain(c))).Where(x => p.Length >= 3 && (x.c.Contains(p) || p.Contains(x.c))).ToList();
            if (close.Count == 1) return close[0].i;
            return null;
        }

        /// <summary>Marks an answer. Returns what Ace says (right or wrong, the fact, the score).</summary>
        public (bool Right, string Say) Mark(int choice)
        {
            var q = Current!;
            bool right = choice >= 0 && choice < Choices.Count && Choices[choice] == q.Answer;
            if (right) { Score++; Streak++; BestStreak = Math.Max(BestStreak, Streak); }
            else Streak = 0;
            string fact = q.Fact.Length > 0 ? " " + q.Fact : "";
            string say = choice < 0
                ? $"No worries! It's {Letter(q.Answer)}.{fact}"
                : right ? Composer.Pick("trivia.right", new[] { "Correct!", "Yes! That's right.", "You got it!", "Nailed it!", "Correct, nice one!" }) + fact
                        : Composer.Pick("trivia.wrong", new[] { "Not quite.", "Nope, sorry!", "Close, but no.", "Oof, not that one." }) + $" It's {Letter(q.Answer)}.{fact}";
            Changed?.Invoke();
            return (right, say);
        }

        private string Letter(string answer) { int i = Choices.IndexOf(answer); return i < 0 ? answer : $"{"ABCD"[i]}) {answer}"; }

        public string ScoreText => $"{Score} out of {Asked - (Current != null && !Marked ? 1 : 0)}";
        /// <summary>Set by the brain once the current question was answered.</summary>
        public bool Marked { get; set; }

        public void End() { Over = true; Changed?.Invoke(); }
    }

    public static class Trivia
    {
        /// <summary>Which category a request means ("quiz me on space" → "space").</summary>
        public static string CategoryOf(string text)
        {
            string l = text.ToLowerInvariant();
            if (Regex.IsMatch(l, @"\b(space|planets?|astronomy|stars?|solar system)\b")) return "space";
            if (Regex.IsMatch(l, @"\b(animals?|nature|wildlife|pets?)\b")) return "animals";
            if (Regex.IsMatch(l, @"\b(science|chemistry|physics|biology)\b")) return "science";
            if (Regex.IsMatch(l, @"\b(body|human body|health)\b")) return "body";
            if (Regex.IsMatch(l, @"\b(geography|countries|capitals?|world|maps?|continents?)\b")) return "geography";
            if (Regex.IsMatch(l, @"\b(history|past|presidents?)\b")) return "history";
            if (Regex.IsMatch(l, @"\b(games?|gaming|video games?|minecraft|roblox|pokemon|pokémon|tech|technology|computers?)\b")) return "games";
            if (Regex.IsMatch(l, @"\b(food|cooking|fruit)\b")) return "food";
            if (Regex.IsMatch(l, @"\b(sports?|soccer|football|basketball|olympics)\b")) return "sports";
            if (Regex.IsMatch(l, @"\b(math|maths|numbers)\b")) return "math";
            return "";
        }

        private static TriviaQuestion Q(string cat, string q, string a, string w1, string w2, string w3, string fact = "") => new(cat, q, a, new[] { w1, w2, w3 }, fact);

        public static readonly IReadOnlyList<TriviaQuestion> All = new List<TriviaQuestion>
        {
            Q("space", "Which planet is called the Red Planet?", "Mars", "Venus", "Jupiter", "Mercury", "Rusty iron in its dirt makes it red."),
            Q("space", "What is the biggest planet in our solar system?", "Jupiter", "Saturn", "Neptune", "Earth", "More than 1,300 Earths could fit inside it."),
            Q("space", "Which planet has the most famous rings?", "Saturn", "Mars", "Venus", "Mercury", "Its rings are mostly chunks of ice."),
            Q("space", "What is the closest star to Earth?", "The Sun", "Proxima Centauri", "Sirius", "Polaris", "The next closest, Proxima Centauri, is over 4 light-years away."),
            Q("space", "How many planets are in our solar system?", "8", "7", "9", "10", "Pluto was called a dwarf planet in 2006."),
            Q("space", "Who was the first person to walk on the Moon?", "Neil Armstrong", "Buzz Aldrin", "Yuri Gagarin", "John Glenn", "He stepped onto the Moon in July 1969."),
            Q("space", "Which planet is closest to the Sun?", "Mercury", "Venus", "Earth", "Mars"),
            Q("space", "What is the hottest planet in our solar system?", "Venus", "Mercury", "Mars", "Jupiter", "Its thick clouds trap heat, so it's hotter than Mercury."),
            Q("space", "What galaxy do we live in?", "The Milky Way", "Andromeda", "The Sombrero Galaxy", "The Whirlpool Galaxy"),
            Q("space", "Who was the first person in space?", "Yuri Gagarin", "Neil Armstrong", "Alan Shepard", "John Glenn", "He orbited Earth in 1961."),
            Q("space", "About how long does sunlight take to reach Earth?", "About 8 minutes", "About 8 seconds", "About 8 hours", "About 8 days"),
            Q("space", "Which planet spins on its side?", "Uranus", "Neptune", "Saturn", "Mars"),
            Q("space", "What is the biggest volcano in the solar system?", "Olympus Mons", "Mount Everest", "Mauna Kea", "Mount Vesuvius", "It's on Mars and about 2.5 times taller than Everest."),
            Q("space", "How many moons does Earth have?", "1", "2", "0", "3"),

            Q("animals", "What is the fastest land animal?", "Cheetah", "Lion", "Horse", "Greyhound", "It can run about 110 km/h (70 mph) in short bursts."),
            Q("animals", "What is the largest animal that has ever lived?", "Blue whale", "African elephant", "Megalodon", "T. rex"),
            Q("animals", "How many legs does a spider have?", "8", "6", "10", "12", "That's why spiders aren't insects."),
            Q("animals", "How many hearts does an octopus have?", "3", "1", "2", "4"),
            Q("animals", "What is a baby kangaroo called?", "A joey", "A cub", "A kit", "A calf"),
            Q("animals", "Which bird can't fly but is the fastest runner?", "Ostrich", "Penguin", "Kiwi", "Chicken"),
            Q("animals", "What is the tallest animal?", "Giraffe", "Elephant", "Moose", "Camel"),
            Q("animals", "Which mammal can truly fly?", "Bat", "Flying squirrel", "Sugar glider", "Flying fish", "Flying squirrels only glide."),
            Q("animals", "What is a group of lions called?", "A pride", "A pack", "A herd", "A flock"),
            Q("animals", "How many legs does an insect have?", "6", "4", "8", "10"),
            Q("animals", "What is the largest land animal?", "African elephant", "Giraffe", "Hippo", "Rhino"),
            Q("animals", "What do bees make?", "Honey", "Jam", "Silk", "Syrup"),
            Q("animals", "Which of these animals has black and white stripes?", "Zebra", "Leopard", "Giraffe", "Tiger"),
            Q("animals", "What is a baby frog called?", "A tadpole", "A froglet pup", "A kitten", "A chick"),

            Q("science", "What gas do plants take in from the air?", "Carbon dioxide", "Oxygen", "Nitrogen", "Helium", "They let out oxygen, which we breathe."),
            Q("science", "What is H2O?", "Water", "Salt", "Oxygen", "Sugar"),
            Q("science", "At what temperature does water boil at sea level?", "100°C (212°F)", "0°C (32°F)", "50°C (122°F)", "150°C (302°F)"),
            Q("science", "What is the chemical symbol for gold?", "Au", "Ag", "Go", "Gd", "It comes from the Latin word aurum."),
            Q("science", "What force keeps us on the ground?", "Gravity", "Magnetism", "Friction", "Wind"),
            Q("science", "Which part of a plant makes food from sunlight?", "The leaves", "The roots", "The stem", "The flower"),
            Q("science", "What is the hardest natural material?", "Diamond", "Gold", "Iron", "Quartz"),
            Q("science", "What are animals that eat only plants called?", "Herbivores", "Carnivores", "Omnivores", "Insectivores"),
            Q("science", "What is the center of an atom called?", "The nucleus", "The electron", "The molecule", "The neutrino"),
            Q("science", "What color do you get when you mix blue and yellow paint?", "Green", "Purple", "Orange", "Brown"),
            Q("science", "Which is faster, light or sound?", "Light", "Sound", "They're the same", "It depends on the day", "That's why you see lightning before you hear thunder."),
            Q("science", "What is the chemical symbol for oxygen?", "O", "Ox", "Om", "Og"),

            Q("body", "How many bones does an adult human have?", "206", "106", "306", "186", "Babies are born with around 300 that join together."),
            Q("body", "What is the largest organ of the human body?", "Skin", "Liver", "Heart", "Brain"),
            Q("body", "How many teeth does an adult usually have?", "32", "28", "24", "36", "That counts all 4 wisdom teeth."),
            Q("body", "Which organ pumps blood around your body?", "Heart", "Lungs", "Liver", "Kidneys"),
            Q("body", "Where is the smallest bone in your body?", "In your ear", "In your toe", "In your nose", "In your finger"),

            Q("geography", "What is the capital of France?", "Paris", "London", "Rome", "Berlin"),
            Q("geography", "What is the largest ocean?", "Pacific Ocean", "Atlantic Ocean", "Indian Ocean", "Arctic Ocean"),
            Q("geography", "Which river is usually called the longest in the world?", "The Nile", "The Amazon", "The Mississippi", "The Yangtze", "Some scientists think the Amazon might be a little longer."),
            Q("geography", "What is the tallest mountain in the world?", "Mount Everest", "K2", "Kilimanjaro", "Mount Fuji"),
            Q("geography", "Which continent is Egypt in?", "Africa", "Asia", "Europe", "South America"),
            Q("geography", "What is the capital of Japan?", "Tokyo", "Kyoto", "Osaka", "Seoul"),
            Q("geography", "What is the largest country by area?", "Russia", "Canada", "China", "USA"),
            Q("geography", "What is the smallest continent?", "Australia", "Europe", "Antarctica", "South America"),
            Q("geography", "What is the largest desert in the world?", "Antarctica", "The Sahara", "The Gobi", "The Arabian Desert", "A desert is a place with very little rain, and Antarctica counts! The Sahara is the biggest hot desert."),
            Q("geography", "How many continents are there?", "7", "5", "6", "8"),
            Q("geography", "What is the capital of Canada?", "Ottawa", "Toronto", "Vancouver", "Montreal"),
            Q("geography", "What is the capital of Australia?", "Canberra", "Sydney", "Melbourne", "Perth"),
            Q("geography", "Which country is shaped like a boot?", "Italy", "Spain", "Greece", "Portugal"),
            Q("geography", "What is the biggest US state?", "Alaska", "Texas", "California", "Montana"),

            Q("history", "Who was the first president of the United States?", "George Washington", "Abraham Lincoln", "Thomas Jefferson", "John Adams"),
            Q("history", "In what year did people first land on the Moon?", "1969", "1959", "1979", "1989"),
            Q("history", "Who painted the Mona Lisa?", "Leonardo da Vinci", "Michelangelo", "Pablo Picasso", "Vincent van Gogh"),
            Q("history", "Who built the pyramids of Giza?", "Ancient Egyptians", "Romans", "Greeks", "Vikings", "They're about 4,500 years old."),
            Q("history", "In what year did the Titanic sink?", "1912", "1905", "1920", "1898"),
            Q("history", "Who wrote Romeo and Juliet?", "William Shakespeare", "Charles Dickens", "Mark Twain", "J.K. Rowling"),
            Q("history", "Which country gave the Statue of Liberty to the USA?", "France", "England", "Spain", "Italy"),
            Q("history", "Who came up with the theory of relativity?", "Albert Einstein", "Isaac Newton", "Galileo Galilei", "Nikola Tesla"),
            Q("history", "In what year did World War II end?", "1945", "1918", "1939", "1950"),
            Q("history", "Who was the 16th president of the United States?", "Abraham Lincoln", "George Washington", "Theodore Roosevelt", "Ulysses S. Grant"),

            Q("games", "In Minecraft, which mob sneaks up and explodes?", "Creeper", "Zombie", "Enderman", "Skeleton"),
            Q("games", "In Minecraft, what block do you need to build a Nether portal?", "Obsidian", "Diamond", "Cobblestone", "Gold block"),
            Q("games", "Which company makes the PlayStation?", "Sony", "Microsoft", "Nintendo", "Sega"),
            Q("games", "Which company makes the Xbox?", "Microsoft", "Sony", "Nintendo", "Apple"),
            Q("games", "What is the name of Nintendo's famous plumber?", "Mario", "Luigi", "Wario", "Link"),
            Q("games", "In Minecraft, which boss lives in the End?", "The Ender Dragon", "The Wither", "The Ghast", "The Warden"),
            Q("games", "What does CPU stand for?", "Central Processing Unit", "Computer Power Unit", "Central Program Utility", "Core Processing Unit"),
            Q("games", "What does WWW stand for?", "World Wide Web", "Wide World Web", "World Web Works", "Web Wide World"),
            Q("games", "What does RAM stand for?", "Random Access Memory", "Read And Memorize", "Rapid Action Memory", "Random App Manager"),
            Q("games", "Who started Microsoft with Paul Allen?", "Bill Gates", "Steve Jobs", "Elon Musk", "Mark Zuckerberg"),
            Q("games", "What is the money in Roblox called?", "Robux", "Coins", "V-Bucks", "Gems"),
            Q("games", "What type of Pokémon is Pikachu?", "Electric", "Fire", "Water", "Normal"),

            Q("food", "Which fruit is guacamole made from?", "Avocado", "Lime", "Tomato", "Pepper"),
            Q("food", "What is the main ingredient in hummus?", "Chickpeas", "Lentils", "Peanuts", "Rice"),
            Q("food", "Which country is pizza from?", "Italy", "USA", "Greece", "France"),
            Q("food", "What do you call a dried grape?", "A raisin", "A prune", "A date", "A fig"),
            Q("food", "Which nut is marzipan made from?", "Almonds", "Peanuts", "Walnuts", "Cashews"),
            Q("food", "Popcorn is made from which plant?", "Corn", "Rice", "Wheat", "Oats"),
            Q("food", "Which vegetable makes your eyes water when you cut it?", "Onion", "Carrot", "Potato", "Cucumber"),

            Q("sports", "How many players does a soccer team have on the field?", "11", "9", "10", "12"),
            Q("sports", "In which sport do you score a touchdown?", "American football", "Soccer", "Basketball", "Baseball"),
            Q("sports", "How many points is a basketball shot from behind the arc?", "3", "2", "4", "1"),
            Q("sports", "How often are the Summer Olympics held?", "Every 4 years", "Every 2 years", "Every year", "Every 5 years"),
            Q("sports", "What sport is played at Wimbledon?", "Tennis", "Golf", "Cricket", "Soccer"),

            Q("math", "What is 7 × 8?", "56", "54", "64", "48"),
            Q("math", "How many sides does a hexagon have?", "6", "5", "7", "8"),
            Q("math", "What is the square root of 81?", "9", "8", "7", "18"),
            Q("math", "How many degrees are in a right angle?", "90", "180", "45", "360"),
            Q("math", "What is 15% of 200?", "30", "15", "20", "25"),
            Q("math", "How many minutes are in 3 hours?", "180", "160", "120", "300"),
            Q("math", "What is the only even prime number?", "2", "4", "0", "6"),
            Q("math", "What do you call a shape with 8 sides?", "Octagon", "Hexagon", "Pentagon", "Decagon"),
        };
    }

    // ============================================================================ 20 questions

    /// <summary>
    /// 20 questions, both ways. Ace guesses what you're thinking of by asking the question that splits the
    /// possibilities best, and learns new things when it loses. Or Ace thinks of something and you ask.
    /// </summary>
    public sealed class TwentyQuestions
    {
        public sealed class Thing
        {
            public string Name { get; set; } = "";
            /// <summary>Attribute code → 1 yes, 0 no, 0.5 sometimes.</summary>
            public Dictionary<string, double> Traits { get; set; } = new();
            public double Get(string code) => Traits.TryGetValue(code, out var v) ? v : 0;
        }

        public static readonly (string Code, string Question)[] Questions =
        {
            ("an", "Is it an animal?"), ("mm", "Is it a mammal?"), ("bd", "Is it a bird?"), ("wt", "Does it live in water, or go on water?"),
            ("fl", "Can it fly?"), ("pt", "Do people keep it as a pet?"), ("fm", "Would you find it on a farm?"), ("dg", "Can it be dangerous to people?"),
            ("bg", "Is it bigger than a person?"), ("hd", "Can you hold it in one hand?"), ("fd", "Can you eat it?"), ("sw", "Is it sweet?"),
            ("fr", "Is it a fruit or a vegetable?"), ("ht", "Is it usually eaten hot?"), ("el", "Does it use electricity or batteries?"),
            ("sc", "Does it have a screen?"), ("wh", "Does it have wheels?"), ("rd", "Can you ride in it or on it?"), ("mu", "Do people make music with it?"),
            ("kt", "Would you find it in a kitchen?"), ("sk", "Do you use it at school?"), ("lg", "Does it have legs?"), ("rn", "Is it round?"),
            ("gr", "Is it green?"), ("pl", "Is it a plant?"), ("sy", "Is it up in the sky?"), ("fu", "Does it have fur?"), ("nt", "Is it found in nature (not made by people)?"),
        };

        /// <summary>Words in a question → the trait it asks about ("is it furry" → fu). "Man-made" asks the opposite of nt.</summary>
        private static readonly (string Pattern, string Code, bool Flip)[] Keywords =
        {
            (@"\bmammal", "mm", false), (@"\bbird", "bd", false), (@"\banimal|\bcreature|\bliving thing|\balive\b", "an", false),
            (@"\b(water|swim|ocean|sea|lake|river)\b", "wt", false), (@"\b(fly|flies|flying|wings?)\b", "fl", false), (@"\bpets?\b", "pt", false),
            (@"\bfarm", "fm", false), (@"\b(dangerous|bite|hurt|scary|deadly|kill)\b", "dg", false),
            (@"\b(bigger|larger|taller) than (a|an|me|the|my|people|person|human)|\b(big|huge|large|giant)\b", "bg", false),
            (@"\b(hold|hand|small|tiny|little|fit in)\b", "hd", false), (@"\b(eat|food|edible|eaten)\b", "fd", false), (@"\bsweet|\bsugary", "sw", false),
            (@"\b(fruit|vegetable|veggie)", "fr", false), (@"\bhot\b|\bwarm\b|\bcooked\b", "ht", false),
            (@"\b(electric|electronic|batter|plug|power|charge)", "el", false), (@"\bscreen|\bdisplay", "sc", false), (@"\bwheels?\b", "wh", false),
            (@"\b(ride|drive|vehicle|travel)\b", "rd", false), (@"\b(music|instrument|play songs)\b", "mu", false), (@"\bkitchen", "kt", false),
            (@"\b(school|class|homework)\b", "sk", false), (@"\blegs?\b", "lg", false), (@"\b(round|circle|ball|sphere)\b", "rn", false), (@"\bgreen\b", "gr", false),
            (@"\b(plant|grow|tree|flower)\b", "pl", false), (@"\b(sky|space|air)\b", "sy", false), (@"\b(fur|furry|fluffy|hairy)\b", "fu", false),
            (@"\b(man-?made|made by (people|humans|man)|built|manufactured|invented|a toy|an object|a thing you buy)\b", "nt", true), (@"\b(nature|natural)\b", "nt", false),
        };

        private static List<Thing>? _all;
        private static string LearnedPath => Path.Combine(AceV4Config.Folder, "twenty-questions.json");
        public static bool DontSave;

        public static List<Thing> All
        {
            get
            {
                if (_all != null) return _all;
                var list = BuiltIn();
                try
                {
                    if (File.Exists(LearnedPath))
                        foreach (var t in JsonSerializer.Deserialize<List<Thing>>(File.ReadAllText(LearnedPath)) ?? new())
                            if (!list.Any(x => x.Name.Equals(t.Name, StringComparison.OrdinalIgnoreCase))) list.Add(t);
                }
                catch { }
                return _all = list;
            }
        }

        private static List<Thing> BuiltIn()
        {
            // name: traits that are yes, then after "/" traits that are "sometimes".
            string[] rows =
            {
                "dog: an mm pt lg fu nt / dg fm", "cat: an mm pt lg fu nt / hd", "horse: an mm fm rd bg lg fu nt / pt", "cow: an mm fm bg lg nt / fu fd",
                "pig: an mm fm lg nt / fd pt", "chicken: an bd fm lg nt fd / fl", "sheep: an mm fm lg fu nt", "rabbit: an mm pt lg fu nt / hd fm",
                "mouse: an mm lg fu nt hd / pt", "hamster: an mm pt lg fu nt hd", "elephant: an mm bg lg nt / dg", "lion: an mm bg lg fu nt dg",
                "tiger: an mm bg lg fu nt dg", "bear: an mm bg lg fu nt dg", "monkey: an mm lg fu nt", "giraffe: an mm bg lg nt",
                "zebra: an mm lg nt / bg", "kangaroo: an mm lg nt fu / bg", "shark: an wt dg nt / bg", "dolphin: an mm wt nt / bg",
                "whale: an mm wt nt bg", "goldfish: an wt pt hd nt", "octopus: an wt nt", "frog: an lg nt hd / wt gr pt", "snake: an nt / dg pt",
                "turtle: an lg nt / wt pt", "eagle: an bd fl lg nt / dg", "penguin: an bd wt lg nt", "owl: an bd fl lg nt", "parrot: an bd fl lg nt pt / gr",
                "bee: an fl nt hd lg / dg", "butterfly: an fl nt hd lg", "spider: an lg nt hd / dg", "ant: an lg nt hd", "crocodile: an wt nt dg lg bg / gr",
                "phone: el sc hd", "computer: el sc / sk", "television: el sc", "tablet: el sc / hd sk", "video game console: el / sc", "car: wh rd bg / el",
                "bicycle: wh rd", "airplane: fl rd bg / el sy", "train: wh rd bg / el", "boat: wt rd / bg", "skateboard: wh rd", "chair: lg / sk kt",
                "table: lg / sk kt bg", "bed: bg / lg", "pencil: hd sk", "book: hd sk", "backpack: sk", "ball: rn / hd", "guitar: mu / el",
                "piano: mu bg / el lg", "drum: mu / rn", "clock: / el rn sk kt", "umbrella: / hd", "shoe: hd", "cup: hd kt / rn", "spoon: hd kt", "fork: hd kt",
                "refrigerator: el kt bg", "microwave: el kt", "toothbrush: hd / el", "lamp: el", "scissors: hd sk", "key: hd", "headphones: el hd mu",
                "pizza: fd ht / rn", "apple: fd fr sw rn hd nt / gr pl", "banana: fd fr sw hd nt", "orange: fd fr sw rn hd nt", "watermelon: fd fr sw nt / gr rn",
                "strawberry: fd fr sw hd nt", "carrot: fd fr hd nt / pl", "broccoli: fd fr gr nt hd / pl", "ice cream: fd sw hd", "chocolate: fd sw hd",
                "cookie: fd sw hd rn / kt", "hamburger: fd ht / hd rn", "bread: fd / hd kt", "cheese: fd / kt", "egg: fd hd / kt nt", "popcorn: fd / ht hd",
                "taco: fd hd / ht", "cake: fd sw / rn", "sun: sy nt rn bg", "moon: sy nt rn bg", "tree: pl nt bg / gr", "flower: pl nt hd", "grass: pl nt gr",
                "cloud: sy nt / bg", "rainbow: sy nt", "rock: nt / hd", "snowman: / bg rn", "creeper: gr lg / dg", "diamond: nt hd", "minecraft: el sc",
            };
            var list = new List<Thing>();
            foreach (var row in rows)
            {
                var (name, rest) = (row[..row.IndexOf(':')].Trim(), row[(row.IndexOf(':') + 1)..]);
                var halves = rest.Split('/');
                var t = new Thing { Name = name };
                foreach (var c in halves[0].Split(' ', StringSplitOptions.RemoveEmptyEntries)) t.Traits[c] = 1;
                if (halves.Length > 1) foreach (var c in halves[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)) t.Traits[c] = 0.5;
                list.Add(t);
            }
            return list;
        }

        private static readonly Random Rng = new();

        // ------------------------------------------------ Ace guesses

        public bool AceGuesses { get; }
        public int Count { get; private set; }
        public bool Over { get; private set; }
        public string? AskedCode { get; private set; }
        public Thing? Guessing { get; private set; }
        /// <summary>Raised when the game moves on, so the card can update.</summary>
        public event Action? Changed;
        private readonly Dictionary<Thing, double> _weights = new();
        private readonly Dictionary<string, double> _answers = new();
        private readonly HashSet<Thing> _wrongGuesses = new();

        // ------------------------------------------------ You guess

        public Thing? Secret { get; }

        private TwentyQuestions(bool aceGuesses)
        {
            AceGuesses = aceGuesses;
            foreach (var t in All) _weights[t] = 1;
            if (!aceGuesses) Secret = All.Where(t => !t.Name.Contains(' ') || t.Name.Length < 14).OrderBy(_ => Rng.Next()).First();
        }

        public static TwentyQuestions AceGuessing() => new(true);
        public static TwentyQuestions YouGuessing() => new(false);

        /// <summary>What Ace says next: a question, or a guess.</summary>
        public string NextMove()
        {
            double total = _weights.Values.Sum();
            var best = _weights.Where(kv => !_wrongGuesses.Contains(kv.Key)).OrderByDescending(kv => kv.Value).FirstOrDefault();
            if (best.Key == null) { Over = true; return GiveUp(); }
            double share = best.Value / Math.Max(1e-9, _weights.Where(kv => !_wrongGuesses.Contains(kv.Key)).Sum(kv => kv.Value));
            var question = Questions.Where(q => !_answers.ContainsKey(q.Code))
                .Select(q => (q, Split(q.Code))).OrderByDescending(x => x.Item2).FirstOrDefault();
            bool guess = Count >= 20 || share >= 0.8 || question.q.Code == null || question.Item2 < 0.05;
            if (Count >= 20 && _wrongGuesses.Count >= 2) { Over = true; return GiveUp(); }
            Count++;
            if (guess)
            {
                Guessing = best.Key;
                AskedCode = null;
                Changed?.Invoke();
                return $"Is it {Article(best.Key.Name)}? (guess {Count} of 20)";
            }
            Guessing = null;
            AskedCode = question.q.Code;
            Changed?.Invoke();
            return $"{question.q.Question} ({Count} of 20)";
        }

        private string GiveUp()
        {
            Changed?.Invoke();
            return "You stumped me! What were you thinking of? Tell me and I'll remember it for next time.";
        }

        /// <summary>How evenly a question splits what's left (0 = useless, 1 = perfect 50/50).</summary>
        private double Split(string code)
        {
            double yes = 0, no = 0;
            foreach (var (t, w) in _weights)
            {
                if (_wrongGuesses.Contains(t)) continue;
                double v = t.Get(code);
                yes += w * v; no += w * (1 - v);
            }
            double sum = yes + no;
            if (sum <= 0) return 0;
            double p = yes / sum;
            return p <= 0 || p >= 1 ? 0 : -(p * Math.Log2(p) + (1 - p) * Math.Log2(1 - p));
        }

        public static string Article(string name) => Regex.IsMatch(name, @"^(sun|moon|minecraft)$") ? "the " + name.Replace("minecraft", "game Minecraft") : (Regex.IsMatch(name, @"^[aeiou]") ? "an " : "a ") + name;

        /// <summary>yes = 1, no = 0, maybe = 0.5, or null if it isn't an answer.</summary>
        public static double? YesNo(string said)
        {
            string l = said.ToLowerInvariant().Trim().TrimEnd('.', '!', '?');
            if (Regex.IsMatch(l, @"^(yes|yeah|yep|yup|ya|yea|correct|right|true|it is|it does|it can|they do|uh huh|definitely|of course|sure|y)\b")) return 1;
            if (Regex.IsMatch(l, @"^(no|nope|nah|not really|it isn'?t|it doesn'?t|it can'?t|they don'?t|false|wrong|n)\b")) return 0;
            if (Regex.IsMatch(l, @"^(maybe|sometimes|sort of|kind of|kinda|i don'?t know|idk|not sure|depends|both|probably|partly)\b")) return 0.5;
            return null;
        }

        /// <summary>Takes yes/no/maybe for Ace's last question or guess. Returns what Ace says next.</summary>
        public string Answer(double v)
        {
            if (Guessing != null)
            {
                if (v >= 0.75)
                {
                    Over = true;
                    Changed?.Invoke();
                    return Composer.Pick("twentyq.won", new[] { "Yes! I got it in {0}! Want to play again?", "Woohoo! Guessed it in {0}. That was fun!", "I knew it! {0} questions. Play again?" })
                        .Replace("{0}", Count.ToString());
                }
                _wrongGuesses.Add(Guessing);
                Guessing = null;
                if (Count >= 20) { Over = true; return GiveUp(); }
                return "Hmm, okay. " + NextMove();
            }
            if (AskedCode != null)
            {
                _answers[AskedCode] = v;
                if (Math.Abs(v - 0.5) > 0.01)
                    foreach (var t in _weights.Keys.ToList())
                    {
                        double trait = t.Get(AskedCode);
                        double fit = trait == 0.5 ? 0.6 : Math.Abs(trait - v) < 0.01 ? 1 : 0.06;
                        _weights[t] *= fit;
                    }
            }
            return NextMove();
        }

        /// <summary>After Ace gives up: learns the thing with the answers you gave.</summary>
        public string Learn(string name)
        {
            name = Regex.Replace(name.ToLowerInvariant().Trim().TrimEnd('.', '!'), @"^(it was|it'?s|i was thinking of|a|an|the)\s+", "");
            name = Regex.Replace(name, @"^(a|an|the)\s+", "").Trim();
            if (name.Length < 2 || name.Length > 40) return "Okay! Good game.";
            var existing = All.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                var t = new Thing { Name = name };
                foreach (var (code, v) in _answers) if (v > 0) t.Traits[code] = v;
                All.Add(t);
                Save();
                return $"{Composer.Capital(Article(name))}! I'll remember that next time. Good game!";
            }
            foreach (var (code, v) in _answers) existing.Traits[code] = (existing.Get(code) + v) / 2;
            Save();
            return $"Oh, {Article(name)}! I know that one, I just guessed wrong. I'll do better next time.";
        }

        private static void Save()
        {
            if (DontSave) return;
            try
            {
                var builtIn = BuiltIn().Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                Directory.CreateDirectory(AceV4Config.Folder);
                File.WriteAllText(LearnedPath, JsonSerializer.Serialize(All.Where(t => !builtIn.Contains(t.Name)).ToList()));
            }
            catch { }
        }

        /// <summary>You asked a question about Ace's secret. Returns Ace's answer, or null if Ace can't tell what you asked.</summary>
        public string? AnswerQuestion(string question, out bool won)
        {
            won = false;
            var s = Secret!;
            string l = question.ToLowerInvariant().Trim().TrimEnd('?', '.', '!');
            var guess = Regex.Match(l, @"^(?:is it|it'?s|was it|are you thinking of|my guess is|i guess|i think it'?s)\s+(?:a|an|the)?\s*(.+)$");
            if (guess.Success)
            {
                string g = guess.Groups[1].Value.Trim();
                bool isThing = All.Any(t => t.Name.Equals(g, StringComparison.OrdinalIgnoreCase) || t.Name.Equals(Regex.Replace(g, "s$", ""), StringComparison.OrdinalIgnoreCase));
                if (g.Equals(s.Name, StringComparison.OrdinalIgnoreCase) || Regex.Replace(g, "s$", "").Equals(s.Name, StringComparison.OrdinalIgnoreCase))
                {
                    Count++;
                    won = true; Over = true; Changed?.Invoke();
                    return $"Yes!! It's {Article(s.Name)}! You got it in {Count} question{(Count == 1 ? "" : "s")}.";
                }
                if (isThing)
                {
                    Count++;
                    Changed?.Invoke();
                    return Count >= 20 ? Lose() : $"Nope, it's not {Article(g)}. ({Count} of 20)";
                }
            }
            foreach (var (pattern, code, flip) in Keywords)
            {
                if (!Regex.IsMatch(l, pattern)) continue;
                double v = s.Get(code);
                if (flip) v = 1 - v;
                Count++;
                Changed?.Invoke();
                if (Count >= 20) return Lose();
                string reply = v >= 0.75 ? Composer.Pick("twentyq.yes", new[] { "Yes!", "Yep.", "Yes, it is." }) : v <= 0.25 ? Composer.Pick("twentyq.no", new[] { "Nope.", "No.", "Nah." }) : "Sometimes, sort of.";
                return $"{reply} ({Count} of 20)";
            }
            return null;
        }

        private string Lose()
        {
            Over = true;
            Changed?.Invoke();
            return $"That's 20! It was {Article(Secret!.Name)}. Good game!";
        }

        /// <summary>A hint about the secret: one trait it has.</summary>
        public string Hint()
        {
            var yes = Questions.Where(q => Secret!.Get(q.Code) >= 1).Select(q => q.Question).ToList();
            if (yes.Count == 0) return "Hmm, it's a tricky one. It isn't an animal.";
            string q = yes[Rng.Next(yes.Count)];
            return "Here's a hint: if you asked \"" + q + "\" I'd say yes.";
        }
    }
}
