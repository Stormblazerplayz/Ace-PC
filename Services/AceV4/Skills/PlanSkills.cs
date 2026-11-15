using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Nimbus.AceV4.Brain;
using Nimbus.AceV4.Pc;
using Nimbus.Services.Calendar;

namespace Nimbus.AceV4
{
    /// <summary>Calendar (always asks first), sleep, memory and help.</summary>
    public sealed partial class AceV4Brain
    {
        // ===================================================== Calendar

        private static readonly string CalendarFiller = @"\b(add|put|schedule|create|make|book|set up|plan|new|an?|event|appointment|meeting|called|named|titled|to|on|in|onto|into|my|the|calendar|for|please|i have|i've got|there's|there is|got|at|it)\b";

        private static string EventTitle(string rest)
        {
            var named = Regex.Match(rest, @"\b(?:called|named|titled)\s+['""]?(.+?)['""]?$", RegexOptions.IgnoreCase);
            string t = named.Success ? named.Groups[1].Value : rest;
            if (!named.Success)
            {
                t = Regex.Replace(t, @"\b(to|on|in|onto|into)\s+(my|the)\s+calendar\b", " ", RegexOptions.IgnoreCase);
                // Trim filler words from both ends, keep the middle ("doctor appointment" stays).
                string prev;
                do
                {
                    prev = t;
                    t = Regex.Replace(t.Trim(), @"^(" + CalendarFiller[2..^2] + @")\s+", "", RegexOptions.IgnoreCase);
                    t = Regex.Replace(t.Trim(), @"\s+(" + CalendarFiller[2..^2] + @")$", "", RegexOptions.IgnoreCase);
                } while (t != prev);
                t = Regex.Replace(t, @"^(add|put|schedule)\s+", "", RegexOptions.IgnoreCase);
            }
            t = Regex.Replace(t, @"\s+", " ").Trim(' ', '.', ',', '"', '\'', '?', '!');
            if (Regex.IsMatch(t, @"^(event|appointment|meeting|it|something|calendar)?$", RegexOptions.IgnoreCase)) return "";
            return Composer.Capital(t);
        }

        private Task<AceAnswer?> CalendarAddAsync(Turn t)
        {
            var now = DateTime.Now;
            var when = NaturalTime.Parse(t.Text, now);
            string title = EventTitle(when.Rest);
            if (title.Length == 0) title = "Event";
            if (!when.Found)
            {
                Step("I need a day and time", title);
                Talk.PendingFollow = async follow =>
                {
                    var w2 = NaturalTime.Parse(follow.Text, DateTime.Now);
                    if (!w2.Found) return null;
                    return await Task.FromResult(MakeAddConfirm(title, w2));
                };
                Talk.PendingFollowAt = now;
                return Task.FromResult<AceAnswer?>(AceAnswer.Say($"When is \"{title}\"? Tell me a day and time, like \"Friday at 5\".", "calendar", 0.8));
            }
            return Task.FromResult<AceAnswer?>(MakeAddConfirm(title, when));
        }

        private AceAnswer MakeAddConfirm(string title, WhenResult when)
        {
            var now = DateTime.Now;
            var e = new CalendarEvent { Title = title, AllDay = when.AllDay };
            if (when.AllDay)
            {
                e.Start = (when.Date ?? now.Date).Date;
                e.End = e.Start.AddDays(1);
            }
            else
            {
                e.Start = when.StartOr(now, new TimeSpan(9, 0, 0));
                e.End = when.EndFor(e.Start, TimeSpan.FromHours(1));
            }
            e.Color = GuessColor(title);
            var clash = CalendarStore.Between(e.Start, e.End).Where(x => !x.AllDay && !e.AllDay).ToList();
            Step("Planned the event", e.WhenText + (clash.Count > 0 ? " · overlaps " + clash[0].Title : ""));
            string question = $"Add \"{e.Title}\" on {e.WhenText}?";
            var card = new ConfirmCard
            {
                Question = question,
                YesLabel = "Add it",
                NoLabel = "Don't",
                OnYes = () => { CalendarStore.Add(e); return Task.FromResult($"Added \"{e.Title}\" to your calendar for {NaturalTime.Friendly(e.Start, DateTime.Now, !e.AllDay)}."); },
                OnNo = () => "Okay, I didn't add it.",
            };
            var a = AceAnswer.Say(question + (clash.Count > 0 ? $" Heads up, it overlaps \"{clash[0].Title}\"." : ""), "calendar");
            a.Cards.Add(card);
            a.Cards.Add(new CalendarCard { Title = "New event", Events = { e } });
            return a;
        }

        private static string GuessColor(string title)
        {
            string l = title.ToLowerInvariant();
            if (Regex.IsMatch(l, @"\b(test|exam|quiz|homework|school|class|study|project|due)\b")) return "amber";
            if (Regex.IsMatch(l, @"\b(doctor|dentist|appointment|checkup|orthodontist)\b")) return "red";
            if (Regex.IsMatch(l, @"\b(practice|game|soccer|football|basketball|baseball|swim|gym|run|match|training)\b")) return "green";
            if (Regex.IsMatch(l, @"\b(birthday|party|movie|sleepover|hangout|fun)\b")) return "pink";
            if (Regex.IsMatch(l, @"\b(meeting|call|stream|upload|release)\b")) return "blue";
            return "violet";
        }

        /// <summary>Finds the event the user means from words like "the dentist" or "my event tomorrow".</summary>
        private (CalendarEvent? Event, List<CalendarEvent> Others) FindEvent(string words, DateTime? onDay)
        {
            string w = Regex.Replace(words, @"\b(my|the|a|an|event|appointment|meeting|calendar|from|on|in|thing|one|that|this)\b", " ", RegexOptions.IgnoreCase);
            w = Regex.Replace(w, @"\s+", " ").Trim();
            var list = CalendarStore.Search(w, onDay);
            if (list.Count == 0 && onDay != null && w.Length > 0) list = CalendarStore.Search(w, null);
            if (list.Count == 0 && w.Length == 0 && onDay != null) list = CalendarStore.OnDay(onDay.Value).Select(e => e.Copy()).ToList();
            return (list.FirstOrDefault(), list.Skip(1).Take(3).ToList());
        }

        private Task<AceAnswer?> CalendarDeleteAsync(Turn t)
        {
            var now = DateTime.Now;
            string body = Regex.Replace(t.Lower, @"^(delete|remove|cancel|clear|erase|get rid of|take)\s+", "");
            body = Regex.Replace(body, @"\b(off|from)\s+(my|the)\s+calendar\b", "");
            var when = NaturalTime.Parse(body, now);
            Step("Looking for the event", when.Rest.Trim());
            var (e, others) = FindEvent(when.Rest, when.Date);
            if (e == null) return Task.FromResult<AceAnswer?>(AceAnswer.Say("I couldn't find that event on your calendar.", "calendar", 0.7));
            string q = $"Delete \"{e.Title}\" on {e.WhenText}?";
            var a = AceAnswer.Say(q, "calendar");
            a.Cards.Add(new ConfirmCard
            {
                Question = q,
                YesLabel = "Delete it",
                NoLabel = "Keep it",
                OnYes = () => Task.FromResult(CalendarStore.Delete(e.Id) ? $"Deleted \"{e.Title}\"." : "It was already gone."),
                OnNo = () => $"Okay, \"{e.Title}\" stays.",
            });
            if (others.Count > 0) a.Text += $" (If you meant a different one, I also see {Composer.List(others.Select(o => "\"" + o.Title + "\"").ToList(), "or")}.)";
            return Task.FromResult<AceAnswer?>(a);
        }

        private Task<AceAnswer?> CalendarEditAsync(Turn t)
        {
            var now = DateTime.Now;
            string l = Regex.Replace(t.Lower, @"\b(on|in|from)\s+(my|the)\s+calendar\b", "");

            // "rename movie night to game night"
            var rename = Regex.Match(l, @"^(?:rename|change the name of|call)\s+(.+?)\s+(?:to|as)\s+(.+)$");
            // "push homework back an hour" / "move practice forward 30 minutes" / "move it an hour later"
            var shift = Regex.Match(l, @"^(?:move|push|shift|bump|postpone)\s+(.+?)\s+(back|forward|later|earlier|up)?\s*(?:by\s+)?(an?|\d+|one|two|three|half an?)\s*(minutes?|mins?|hours?|hrs?|days?)\s*(back|forward|later|earlier|up)?$");
            // "move the dentist to friday", "change practice to 6pm"
            var move = Regex.Match(l, @"^(?:move|reschedule|change|push|shift|bump|edit|update|make)\s+(.+?)\s+(?:to|until|till|for|start at|at)\s+(.+)$");

            CalendarEvent? e;
            List<CalendarEvent> others;
            if (rename.Success)
            {
                (e, others) = FindEvent(rename.Groups[1].Value, null);
                if (e == null) return NotFound();
                string newTitle = Composer.Capital(rename.Groups[2].Value.Trim(' ', '"', '\''));
                var updated = e.Copy();
                updated.Title = newTitle;
                return Confirm(e, updated, $"Rename \"{e.Title}\" to \"{newTitle}\"?");
            }
            if (shift.Success)
            {
                (e, others) = FindEvent(shift.Groups[1].Value, null);
                if (e == null) return NotFound();
                string dir = shift.Groups[2].Value.Length > 0 ? shift.Groups[2].Value : shift.Groups[5].Value;
                string n = shift.Groups[3].Value;
                double v = n.StartsWith("half") ? 0.5 : NaturalTime.ParseNumber(n);
                var by = NaturalTime.ParseUnit(v, shift.Groups[4].Value) ?? TimeSpan.FromHours(1);
                if (dir is "forward" or "earlier" or "up") by = -by;
                var updated = e.Copy();
                updated.Start += by;
                updated.End += by;
                return Confirm(e, updated, $"Move \"{e.Title}\" to {updated.WhenText}?");
            }
            if (move.Success)
            {
                var when = NaturalTime.Parse(move.Groups[2].Value, now);
                var nameWhen = NaturalTime.Parse(move.Groups[1].Value, now);
                (e, others) = FindEvent(nameWhen.Rest, nameWhen.Date);
                if (e == null) return NotFound();
                var updated = e.Copy();
                if (!when.Found)
                {
                    // "change movie night to game night": no time in it, so it's a new name.
                    string newTitle = Composer.Capital(move.Groups[2].Value.Trim(' ', '"', '\''));
                    updated.Title = newTitle;
                    return Confirm(e, updated, $"Rename \"{e.Title}\" to \"{newTitle}\"?");
                }
                var length = e.End - e.Start;
                if (when.AllDay)
                {
                    updated.AllDay = true;
                    updated.Start = (when.Date ?? e.Start).Date;
                    updated.End = updated.Start.AddDays(1);
                }
                else
                {
                    var day = when.Exact?.Date ?? when.Date ?? e.Start.Date;
                    var time = when.Exact?.TimeOfDay ?? when.Time ?? (e.AllDay ? new TimeSpan(9, 0, 0) : e.Start.TimeOfDay);
                    updated.AllDay = false;
                    updated.Start = day.Date + time;
                    updated.End = when.EndTime != null || when.Duration != null ? when.EndFor(updated.Start, length) : updated.Start + (e.AllDay ? TimeSpan.FromHours(1) : length);
                }
                return Confirm(e, updated, $"Move \"{e.Title}\" from {e.WhenText} to {updated.WhenText}?");
            }
            return Task.FromResult<AceAnswer?>(null);

            Task<AceAnswer?> NotFound() => Task.FromResult<AceAnswer?>(AceAnswer.Say("I couldn't find that event on your calendar.", "calendar", 0.7));

            Task<AceAnswer?> Confirm(CalendarEvent before, CalendarEvent after, string question)
            {
                Step("Planned the change", after.WhenText);
                var a = AceAnswer.Say(question, "calendar");
                a.Cards.Add(new ConfirmCard
                {
                    Question = question,
                    YesLabel = "Change it",
                    NoLabel = "Leave it",
                    OnYes = () =>
                    {
                        after.Reminded = false;
                        return Task.FromResult(CalendarStore.Update(after) ? $"Done. \"{after.Title}\" is now {NaturalTime.Friendly(after.Start, DateTime.Now, !after.AllDay)}." : "I couldn't find it anymore.");
                    },
                    OnNo = () => $"Okay, \"{before.Title}\" stays the same.",
                });
                a.Cards.Add(new CalendarCard { Title = "After the change", Events = { after } });
                return Task.FromResult<AceAnswer?>(a);
            }
        }

        private Task<AceAnswer?> CalendarListAsync(Turn t)
        {
            var now = DateTime.Now;
            DateTime from = now.Date, to = now.Date.AddDays(1);
            string label = "today";
            if (t.Has(@"\b(next|coming up|upcoming)\b") && !t.Has(@"\bnext week\b"))
            {
                var next = CalendarStore.Upcoming(4);
                if (next.Count == 0) return Say("Nothing coming up on your calendar.");
                var a0 = AceAnswer.Say($"Next up: \"{next[0].Title}\" {NaturalTime.Friendly(next[0].Start, now, !next[0].AllDay)}." + (next.Count > 1 ? $" Then \"{next[1].Title}\" {NaturalTime.Friendly(next[1].Start, now, !next[1].AllDay)}." : ""), "calendar");
                a0.Cards.Add(new CalendarCard { Title = "Coming up", Events = next.ToList() });
                return Task.FromResult<AceAnswer?>(a0);
            }
            if (t.Has(@"\bthis week\b|\bthe week\b")) { from = now.Date; to = now.Date.AddDays(7 - (int)now.DayOfWeek + 1); label = "this week"; }
            else if (t.Has(@"\bnext week\b")) { from = now.Date.AddDays(7 - (int)now.DayOfWeek + 1); to = from.AddDays(7); label = "next week"; }
            else if (t.Has(@"\bweekend\b")) { var sat = now.Date; while (sat.DayOfWeek != DayOfWeek.Saturday) sat = sat.AddDays(1); from = sat; to = sat.AddDays(2); label = "this weekend"; }
            else
            {
                var w = NaturalTime.Parse(t.Text, now);
                if (w.Date != null) { from = w.Date.Value.Date; to = from.AddDays(1); label = NaturalTime.Friendly(from, now, false); }
            }
            var events = CalendarStore.Between(from, to).ToList();
            Step("Read your calendar", $"{label}: {events.Count} events");
            if (events.Count == 0)
                return Say(t.Has(@"\b(busy|free)\b") ? $"You're free {label}. Nothing on the calendar." : $"Nothing on your calendar {label}.");
            string list = Composer.List(events.Take(4).Select(e => e.AllDay ? $"\"{e.Title}\" (all day)" : $"\"{e.Title}\" at {e.Start:h:mm tt}" + (label.Contains("week") ? $" {e.Start:dddd}" : "")).ToList());
            var a = AceAnswer.Say($"{Composer.Capital(label)} you have {Composer.Plural(events.Count, "event", "events")}: {list}{(events.Count > 4 ? " and more" : "")}.", "calendar");
            a.Cards.Add(new CalendarCard { Title = Composer.Capital(label), Events = events });
            return Task.FromResult<AceAnswer?>(a);

            Task<AceAnswer?> Say(string text) => Task.FromResult<AceAnswer?>(AceAnswer.Say(text, "calendar"));
        }

        // ===================================================== Sleep

        public SleepCard? SleepInfo()
        {
            var s = ActivityTracker.Summary(Config.SleepGoalHours);
            if (s == null) return null;
            return new SleepCard
            {
                AvgBed = ActivityTracker.Clock(s.AvgBed),
                AvgWake = ActivityTracker.Clock(s.AvgWake),
                AvgSleep = ActivityTracker.Hours(s.AvgSleep),
                Suggest = ActivityTracker.Clock(s.SuggestBed),
                Nights = s.Nights,
            };
        }

        private Task<AceAnswer?> BedtimeAsync(Turn t)
        {
            Step("Looking at when you use your PC", "last 3 weeks");
            var s = ActivityTracker.Summary(Config.SleepGoalHours);
            if (s == null)
                return Task.FromResult<AceAnswer?>(AceAnswer.Say("I need at least 3 nights of seeing when you stop and start using your PC to guess your sleep. Check back in a few days!", "sleep", 0.6));
            var card = SleepInfo()!;
            string text;
            if (t.Has(@"\bhow (much|long)\b"))
                text = $"Going by your PC, you sleep about {card.AvgSleep} a night (bed around {card.AvgBed}, up around {card.AvgWake}).";
            else
                text = $"You usually get up around {card.AvgWake}, so for {Config.SleepGoalHours:0.#} hours of sleep you should head to bed by {card.Suggest}." +
                       (s.AvgSleep.TotalHours < Config.SleepGoalHours - 0.75 ? $" Lately you've been getting about {card.AvgSleep}, so an earlier night would help." : "");
            var a = AceAnswer.Say(text, "sleep");
            a.Cards.Add(card);
            a.Mood = "sleepy";
            return Task.FromResult<AceAnswer?>(a);
        }

        // ===================================================== Memory

        private Task<AceAnswer?> RememberAsync(Turn t)
        {
            string fact = Regex.Replace(t.Text, @"^(remember|note|learn|don'?t forget|keep in mind)\s*(that|this|:)?\s*", "", RegexOptions.IgnoreCase).Trim().TrimEnd('.', '!');
            if (fact.Length < 3) return Task.FromResult<AceAnswer?>(AceAnswer.Say("What should I remember?", "memory", 0.5));
            // A question ("what's my dog's name?") is asking for a memory, not giving one.
            if (fact.EndsWith("?") || Regex.IsMatch(fact, @"^(what|what's|whats|who|who's|when|where|where's|why|how|which|is|are|do|does|did|can|could|tell me|remind me)\b", RegexOptions.IgnoreCase))
                return Task.FromResult<AceAnswer?>(null);
            var questions = new List<string> { fact, "what did i tell you about " + string.Join(" ", Lexical.Tokens(fact).Take(4)) };
            string answer;
            string you(string s) => Regex.Replace(Regex.Replace(Regex.Replace(s, @"\bmy\b", "your", RegexOptions.IgnoreCase), @"\bI am\b|\bI'm\b", "you're", RegexOptions.IgnoreCase), @"\bI\b", "you");

            var mine = Regex.Match(fact, @"^my\s+(.+?)\s+(is|are|was|were)\s+(.+)$", RegexOptions.IgnoreCase);
            var named = Regex.Match(fact, @"^(.+?)'s\s+(.+?)\s+(is|are)\s+(.+)$", RegexOptions.IgnoreCase);
            var have = Regex.Match(fact, @"^i\s+(have|got)\s+(?:a|an)\s+(.+?)\s+(on|at|in|this|next|tomorrow|today)\b(.*)$", RegexOptions.IgnoreCase);
            if (mine.Success)
            {
                string thing = mine.Groups[1].Value, verb = mine.Groups[2].Value, value = mine.Groups[3].Value;
                questions.AddRange(new[] { $"what is my {thing}", $"what's my {thing}", $"my {thing}", $"do you know my {thing}", $"when is my {thing}", $"who is my {thing}", $"where is my {thing}" });
                answer = $"Your {thing} {verb} {value}.";
            }
            else if (named.Success)
            {
                string who = named.Groups[1].Value, thing = named.Groups[2].Value, value = named.Groups[4].Value;
                questions.AddRange(new[] { $"what is {who}'s {thing}", $"{who}'s {thing}", $"what's {who}'s {thing}" });
                answer = $"{Composer.Capital(who)}'s {thing} {named.Groups[3].Value} {value}.";
            }
            else answer = "You told me: " + you(fact) + ".";
            if (have.Success) questions.Add("when is my " + have.Groups[2].Value);

            var entry = Bank.Learn(questions, answer, "you told me", "personal");
            Talk.LastLearnedId = entry.Id;
            Step("Saved to my memory", answer);
            var a = AceAnswer.Say(Composer.Pick("remember", new[] { "Got it. I'll remember that.", "Okay, saved! Ask me anytime.", "Noted. I won't forget." }), "memory");
            if (have.Success)
            {
                var when = NaturalTime.Parse(fact, DateTime.Now);
                if (when.Found) a.Text += " Want it on your calendar too? Just say \"add it to my calendar\".";
            }
            a.Mood = "love";
            return Task.FromResult<AceAnswer?>(a);
        }

        private Task<AceAnswer?> ForgetAsync(Turn t)
        {
            var about = t.Match(@"^forget (?:what i (?:said|told you) )?about (.+)$");
            if (about.Success)
            {
                var hits = Bank.Search(about.Groups[1].Value, 5).Where(h => h.Entry.Learned != null).ToList();
                if (hits.Count == 0) return Task.FromResult<AceAnswer?>(AceAnswer.Say("I don't have anything saved about that.", "memory"));
                Bank.Forget(hits[0].Entry.Id);
                return Task.FromResult<AceAnswer?>(AceAnswer.Say("Forgotten: " + hits[0].Entry.Answers[0], "memory"));
            }
            if (Talk.LastLearnedId.Length > 0 && Bank.Forget(Talk.LastLearnedId))
            {
                Talk.LastLearnedId = "";
                return Task.FromResult<AceAnswer?>(AceAnswer.Say("Okay, I forgot that.", "memory"));
            }
            if (Talk.LastEntry?.Learned != null && Bank.Forget(Talk.LastEntry.Id))
                return Task.FromResult<AceAnswer?>(AceAnswer.Say("Okay, I forgot that answer.", "memory"));
            return Task.FromResult<AceAnswer?>(AceAnswer.Say("There's nothing new for me to forget. Say \"forget about\" and the topic.", "memory", 0.6));
        }

        private Task<AceAnswer?> CorrectAsync(Turn t)
        {
            var m = Regex.Match(t.Text, @"(?:it'?s|it is|the answer is|actually|it was)\s+(.+)$", RegexOptions.IgnoreCase);
            if (!m.Success || Talk.LastKnowledgeQuestion.Length == 0) return Task.FromResult<AceAnswer?>(null);
            string fixedAnswer = Composer.Capital(m.Groups[1].Value.Trim().TrimEnd('.', '!')) + ".";
            var entry = Bank.Learn(new[] { Talk.LastKnowledgeQuestion }, fixedAnswer, "you corrected me", "learned");
            Talk.LastLearnedId = entry.Id;
            Step("Learned the right answer", Talk.LastKnowledgeQuestion + " → " + fixedAnswer);
            return Task.FromResult<AceAnswer?>(AceAnswer.Say(Composer.Pick("corrected", new[] { "Thanks! I'll remember that next time.", "Oops, my bad. Fixed it.", "Got it, I learned the right answer." }), "memory"));
        }

        // ===================================================== Help

        private Task<AceAnswer?> HelpAsync(Turn t)
        {
            var a = AceAnswer.Say("Here's some of what I can do. Just ask like you'd ask a friend!", "help");
            a.Cards.Add(new ResultsCard
            {
                Title = "Try saying",
                Items =
                {
                    new ResultItem { Title = "Homework", Subtitle = "solve 2x + 3 = 11 step by step · 3/4 + 1/2 · lcm of 4 and 6 · solve the problem on my screen" },
                    new ResultItem { Title = "Scripts", Subtitle = "give me a kill brick script · Roblox jump pad · Arduino servo code · show me all the Lua scripts" },
                    new ResultItem { Title = "Games", Subtitle = "let's play trivia · quiz me on space · let's play 20 questions" },
                    new ResultItem { Title = "Translate and convert", Subtitle = "how do you say thank you in Spanish · what does gracias mean · convert 10 kilograms · 20 dollars to euros" },
                    new ResultItem { Title = "Notes and lists", Subtitle = "take a note: buy headphones · read my notes · add milk and eggs to my shopping list · add the pancake ingredients" },
                    new ResultItem { Title = "Screen and music", Subtitle = "what's on my screen · what song is playing · how much screen time today · limit Minecraft to 1 hour" },
                    new ResultItem { Title = "Apps and windows", Subtitle = "open Spotify · close Discord · snap Chrome to the left · minimize everything" },
                    new ResultItem { Title = "Browser", Subtitle = "open YouTube · search YouTube for lofi · close this tab · next tab" },
                    new ResultItem { Title = "Files", Subtitle = "find my homework doc · find STL files from this week · find the screenshot that says error" },
                    new ResultItem { Title = "Calendar", Subtitle = "add soccer practice Friday at 5 · what's on my calendar · move the dentist to Monday" },
                    new ResultItem { Title = "Questions", Subtitle = "why is the sky blue · capital and currency of Japan · which is bigger, Mars or Earth" },
                    new ResultItem { Title = "Web", Subtitle = "look up the latest Minecraft update · recipe for pancakes · weather tomorrow" },
                    new ResultItem { Title = "PC", Subtitle = "how's my PC doing · what's using my RAM · find big files · any notifications · check now" },
                    new ResultItem { Title = "Helpers", Subtitle = "timer 10 minutes · remind me at 5 to feed the dog · 15% of 80 · 5 miles in km" },
                    new ResultItem { Title = "Focus", Subtitle = "start a focus timer · help me focus on math for 30 minutes · pause focus · how long did I focus today" },
                    new ResultItem { Title = "Cooking", Subtitle = "how do I make pancakes · what can I make with eggs and cheese · start cooking · next step" },
                    new ResultItem { Title = "Report card", Subtitle = "weekly report · how healthy is my PC · what apps do I usually use · stop suggesting apps" },
                    new ResultItem { Title = "Power", Subtitle = "shut down · restart · lock · sleep (with a 10 second cancel for shut down and restart)" },
                    new ResultItem { Title = "Me", Subtitle = "remember that my dog's name is Max · what do you know about me · forget that · cancel my reminder" },
                },
            });
            return Task.FromResult<AceAnswer?>(a);
        }
    }
}
