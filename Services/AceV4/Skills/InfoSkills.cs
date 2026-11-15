using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Nimbus.AceV4.Brain;
using Nimbus.AceV4.Pc;
using Nimbus.AceV4.Web;

namespace Nimbus.AceV4
{
    /// <summary>Skills that answer things: time, weather, words, recipes, web lookups, timers and reminders.</summary>
    public sealed partial class AceV4Brain
    {
        // ===================================================== Time and dates

        private static readonly Dictionary<string, string> CityZones = new(StringComparer.OrdinalIgnoreCase)
        {
            ["london"] = "Europe/London", ["uk"] = "Europe/London", ["england"] = "Europe/London", ["paris"] = "Europe/Paris", ["france"] = "Europe/Paris",
            ["berlin"] = "Europe/Berlin", ["germany"] = "Europe/Berlin", ["tokyo"] = "Asia/Tokyo", ["japan"] = "Asia/Tokyo", ["new york"] = "America/New_York",
            ["nyc"] = "America/New_York", ["los angeles"] = "America/Los_Angeles", ["la"] = "America/Los_Angeles", ["california"] = "America/Los_Angeles",
            ["chicago"] = "America/Chicago", ["texas"] = "America/Chicago", ["denver"] = "America/Denver", ["sydney"] = "Australia/Sydney",
            ["australia"] = "Australia/Sydney", ["beijing"] = "Asia/Shanghai", ["china"] = "Asia/Shanghai", ["india"] = "Asia/Kolkata",
            ["dubai"] = "Asia/Dubai", ["moscow"] = "Europe/Moscow", ["seoul"] = "Asia/Seoul", ["korea"] = "Asia/Seoul", ["hawaii"] = "Pacific/Honolulu",
            ["mexico city"] = "America/Mexico_City", ["toronto"] = "America/Toronto", ["vancouver"] = "America/Vancouver", ["brazil"] = "America/Sao_Paulo",
            ["utc"] = "Etc/UTC", ["gmt"] = "Etc/UTC", ["florida"] = "America/New_York", ["seattle"] = "America/Los_Angeles", ["singapore"] = "Asia/Singapore",
        };

        private static string WeeksText(int days)
        {
            int weeks = days / 7, rest = days % 7;
            if (weeks == 0) return Composer.Plural(rest, "day", "days");
            return Composer.Plural(weeks, "week", "weeks") + (rest > 0 ? " and " + Composer.Plural(rest, "day", "days") : "");
        }

        private static string MonthsText(DateTime now, DateTime target)
        {
            int months = (target.Year - now.Year) * 12 + target.Month - now.Month;
            if (now.AddMonths(months) > target) months--;
            int days = (target.Date - now.Date.AddMonths(months)).Days;
            if (months <= 0) return Composer.Plural(days, "day", "days");
            return Composer.Plural(months, "month", "months") + (days > 0 ? " and " + Composer.Plural(days, "day", "days") : "");
        }

        private async Task<AceAnswer?> TimeAsync(Turn t)
        {
            var now = DateTime.Now;
            var zoneAsk = t.Match(@"\b(?:time|is it)\s+in\s+([a-z .'-]+?)\??$");
            if (zoneAsk.Success)
            {
                string place = zoneAsk.Groups[1].Value.Trim();
                string? zone = CityZones.TryGetValue(place, out var z) ? z : null;
                string name = Composer.Capital(place);
                if (zone == null)
                {
                    Step("Looking up the time zone", place);
                    var found = await WebClient.TimeZoneOfAsync(place, t.Ct).ConfigureAwait(false);
                    if (found != null) { zone = found.Value.Zone; name = found.Value.Name; }
                }
                if (zone != null)
                {
                    try
                    {
                        var tz = TimeZoneInfo.FindSystemTimeZoneById(zone);
                        var there = TimeZoneInfo.ConvertTime(DateTimeOffset.Now, tz);
                        int dayDiff = (there.Date - now.Date).Days;
                        string when = there.ToString("h:mm tt") + (dayDiff == 1 ? " tomorrow" : dayDiff == -1 ? " yesterday" : "");
                        return AceAnswer.Say($"It's {when} in {name}.", "time");
                    }
                    catch { }
                }
                return AceAnswer.Say($"I couldn't find the time zone for {place}.", "time", 0.4);
            }

            var until = t.Match(@"how (?:many|long) (days|weeks|months|hours|sleeps) (?:until|till|til|to|before) (.+?)\??$");
            if (until.Success)
            {
                string what = until.Groups[2].Value.Trim();
                DateTime? target = Holiday(what, now);
                if (target == null)
                {
                    var w = NaturalTime.Parse(what, now);
                    if (w.Exact != null) target = w.Exact;
                    else if (w.Date != null) target = w.Date.Value + (w.Time ?? TimeSpan.Zero);
                }
                if (target == null)
                {
                    // Something the user taught Ace ("my birthday is march 3").
                    var hit = Bank.Search("when is " + what, 1).FirstOrDefault();
                    if (hit != null && hit.Entry.Learned != null && hit.Score > 0.6)
                    {
                        var w = NaturalTime.Parse(hit.Entry.Answers[0], now);
                        if (w.Date != null) { target = w.Date; if (target < now.Date) target = target.Value.AddYears(1); }
                    }
                }
                if (target == null) return null;
                var span = target.Value - now;
                if (span.TotalMinutes < 0) return AceAnswer.Say($"That already happened, {NaturalTime.Friendly(target.Value, now, false)}.", "time");
                string unit = until.Groups[1].Value;
                string amount = unit switch
                {
                    "weeks" => WeeksText((target.Value.Date - now.Date).Days),
                    "months" => MonthsText(now, target.Value),
                    "hours" => Math.Ceiling(span.TotalHours).ToString("N0") + " hours",
                    "sleeps" => Composer.Plural((target.Value.Date - now.Date).Days, "sleep", "sleeps"),
                    _ => Composer.Plural((target.Value.Date - now.Date).Days, "day", "days"),
                };
                Step("Counting the days", target.Value.ToString("ddd MMM d, yyyy"));
                return AceAnswer.Say($"{Composer.Capital(amount)} until {(Holiday(what, now) != null && !System.Text.RegularExpressions.Regex.IsMatch(what, @"^(spring|summer|fall|autumn|winter)$") ? System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(what) : what)} ({target.Value:dddd, MMMM d}).", "time");
            }

            if (t.Has(@"\bwhat year\b")) return AceAnswer.Say($"It's {now.Year}.", "time");
            if (t.Has(@"\b(date|day)\b") && !t.Has(@"\btime\b"))
                return AceAnswer.Say(Composer.Pick("date", new[] { "It's {day}, {date}.", "Today is {day}, {date}." }), "time");
            return AceAnswer.Say(Composer.Pick("time", new[] { "It's {time}.", "{time} right now.", "It's {time} on {day}." }), "time");
        }

        /// <summary>The next date of a holiday, or null.</summary>
        public static DateTime? Holiday(string name, DateTime now)
        {
            string n = name.ToLowerInvariant().Replace("'", "").Replace("the ", "").Trim();
            DateTime? Next(int month, int day)
            {
                var d = new DateTime(now.Year, month, day);
                return d.Date < now.Date ? d.AddYears(1) : d;
            }
            if (n.Contains("christmas eve")) return Next(12, 24);
            if (n.Contains("christmas") || n == "xmas") return Next(12, 25);
            if (n.Contains("new years eve")) return Next(12, 31);
            if (n.Contains("new year")) return Next(1, 1);
            if (n.Contains("halloween")) return Next(10, 31);
            if (n.Contains("valentine")) return Next(2, 14);
            if (n.Contains("fourth of july") || n.Contains("4th of july") || n.Contains("independence day")) return Next(7, 4);
            if (n.Contains("st patrick") || n.Contains("saint patrick")) return Next(3, 17);
            if (n.Contains("summer")) return Next(6, 21);
            if (n.Contains("winter") && !n.Contains("break")) return Next(12, 21);
            if (n.Contains("spring") && !n.Contains("break")) return Next(3, 20);
            if (n.Contains("fall") || n.Contains("autumn")) return Next(9, 22);
            if (n.Contains("thanksgiving"))
            {
                DateTime Thanks(int year) { var d = new DateTime(year, 11, 1); while (d.DayOfWeek != DayOfWeek.Thursday) d = d.AddDays(1); return d.AddDays(21); }
                var th = Thanks(now.Year);
                return th.Date < now.Date ? Thanks(now.Year + 1) : th;
            }
            if (n.Contains("easter"))
            {
                DateTime Easter(int y)
                {
                    int a = y % 19, b = y / 100, c = y % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3, h = (19 * a + b - d - g + 15) % 30;
                    int i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7, m = (a + 11 * h + 22 * l) / 451;
                    int month = (h + l - 7 * m + 114) / 31, day = (h + l - 7 * m + 114) % 31 + 1;
                    return new DateTime(y, month, day);
                }
                var es = Easter(now.Year);
                return es.Date < now.Date ? Easter(now.Year + 1) : es;
            }
            if (n.Contains("weekend")) { var d = now.Date; while (d.DayOfWeek != DayOfWeek.Saturday) d = d.AddDays(1); return d; }
            return null;
        }

        // ===================================================== Weather

        private async Task<AceAnswer?> WeatherAsync(Turn t)
        {
            string place = "";
            var m = t.Match(@"\b(?:in|for|at|near)\s+(?!the morning|the afternoon|the evening|a bit|a while|an hour)([a-z][a-z .'-]+?)(?:\s+(?:today|tomorrow|tonight|this weekend|right now|now|this week|later|on \w+))?\??$");
            if (m.Success && !Regex.IsMatch(m.Groups[1].Value, @"^(today|tomorrow|tonight|the weekend|this weekend|the week|outside|here)$")) place = m.Groups[1].Value.Trim();
            if (place.Length == 0) place = Config.HomeCity;
            Step("Checking the weather", place.Length > 0 ? place : "where you are");
            var w = await WebClient.WeatherAsync(place, t.Ct).ConfigureAwait(false);
            if (w == null) return AceAnswer.Say("I couldn't get the weather right now. " + (WebClient.LastProblem.Length > 0 ? "(" + WebClient.LastProblem + ")" : "Are you online?"), "weather", 0.3);

            string unit = w.Fahrenheit ? "°F" : "°C";
            string speed = w.Fahrenheit ? "mph" : "km/h";
            var card = new WeatherCard
            {
                Place = string.IsNullOrWhiteSpace(w.Place) ? "Here" : w.Place,
                Now = $"{w.TempNow:0}{unit}, {w.Sky}",
                Detail = $"Feels like {w.FeelsLike:0}{unit} · wind {w.Wind:0} {speed} · humidity {w.Humidity}%",
            };
            foreach (var d in w.Days) card.Days.Add((d.Day.Date == DateTime.Today ? "Today" : d.Day.ToString("ddd"), $"{d.Sky} · {d.High:0}° / {d.Low:0}° · rain {d.Rain}%"));

            int dayIndex = t.Has(@"\btomorrow\b") ? 1 : 0;
            var dayMatch = t.Match(@"\b(monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b");
            if (dayMatch.Success)
                for (int i = 0; i < w.Days.Count; i++)
                    if (w.Days[i].Day.DayOfWeek.ToString().Equals(dayMatch.Groups[1].Value, StringComparison.OrdinalIgnoreCase)) { dayIndex = i; break; }
            bool weekend = t.Has(@"\bweekend\b");
            string where = w.Place == "your area" || string.IsNullOrWhiteSpace(w.Place) ? "" : " in " + w.Place;
            string text;
            if (weekend)
            {
                var days = w.Days.Where(d => d.Day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday).ToList();
                text = days.Count == 0 ? "The weekend's past my 4-day forecast, so I can't say yet."
                    : "This weekend" + where + ": " + string.Join("; ", days.Select(d => $"{d.Day:dddd} {d.Sky}, high {d.High:0}{unit}, {d.Rain}% chance of rain")) + ".";
            }
            else if (t.Has(@"\b(rain|umbrella|snow|storm)\b") && dayIndex < w.Days.Count)
            {
                var d = w.Days[dayIndex];
                string dayWord = dayIndex == 0 ? "today" : dayIndex == 1 ? "tomorrow" : d.Day.ToString("dddd");
                bool snowAsk = t.Has(@"\bsnow");
                bool likely = d.Rain >= 50;
                text = snowAsk
                    ? (d.Sky.Contains("snow") ? $"Yep, snow looks likely {dayWord}{where}." : $"No snow in the forecast {dayWord}{where}. It looks {d.Sky}.")
                    : likely ? $"Probably. There's a {d.Rain}% chance of rain {dayWord}{where}. Bring an umbrella!" : d.Rain >= 25 ? $"Maybe. {d.Rain}% chance of rain {dayWord}{where}." : $"Probably not. Only {d.Rain}% chance of rain {dayWord}{where}.";
            }
            else if (dayIndex > 0 && dayIndex < w.Days.Count)
            {
                var d = w.Days[dayIndex];
                text = $"{(dayIndex == 1 ? "Tomorrow" : d.Day.ToString("dddd"))}{where}: {d.Sky}, high of {d.High:0}{unit} and low of {d.Low:0}{unit}, {d.Rain}% chance of rain.";
            }
            else
            {
                text = $"It's {w.TempNow:0}{unit} and {w.Sky}{where}. Feels like {w.FeelsLike:0}{unit}.";
                if (w.Days.Count > 0) text += $" Today's high is {w.Days[0].High:0}{unit}" + (w.Days[0].Rain >= 40 ? $" with a {w.Days[0].Rain}% chance of rain." : ".");
            }
            var a = AceAnswer.Say(text, "weather");
            a.Cards.Add(card);
            return a;
        }

        // ===================================================== Words

        private async Task<AceAnswer?> DefineAsync(Turn t)
        {
            var m = t.Match(@"^(?:define|definition of|meaning of|what'?s the meaning of|what'?s the definition of)\s+(?:the word\s+)?['""]?([a-z][a-z' -]*?)['""]?\??$");
            if (!m.Success) m = t.Match(@"^what does (?:the word )?['""]?([a-z-]+)['""]? mean\??$");
            if (!m.Success)
            {
                var spell = t.Match(@"^how do you spell\s+['""]?([a-z' -]+?)['""]?\??$");
                if (spell.Success)
                {
                    string word = spell.Groups[1].Value.Trim();
                    return AceAnswer.Say($"{word}: {string.Join("-", word.ToUpperInvariant().Where(c => c != ' ').Select(c => c.ToString()))}.", "words");
                }
                return null;
            }
            string w = m.Groups[1].Value.Trim();
            // The bank may already know it (and works offline).
            var hit = Bank.Search("what does " + w + " mean", 1).FirstOrDefault();
            if (hit != null && hit.Score >= HighScore && hit.Entry.Topic == "words")
            {
                Step("Found it in my notes", hit.MatchedQuestion);
                return BankAnswer(hit, 0.95);
            }
            if (!await WebClient.OnlineAsync(t.Ct).ConfigureAwait(false))
                return AceAnswer.Say($"I can't reach the dictionary right now, so I can't look up \"{w}\". Check your internet?", "words", 0.3);
            Step("Looking it up in the dictionary", w);
            var def = await WebClient.DefineAsync(w, t.Ct).ConfigureAwait(false);
            if (def == null)
            {
                Step("Not in the dictionary; trying Wikipedia");
                var wiki = await WebClient.WikipediaAsync(w, t.Ct).ConfigureAwait(false);
                if (wiki == null) return AceAnswer.Say($"I couldn't find a definition for \"{w}\". Is it spelled right?", "words", 0.4);
                var first = Lexical.BestSentences("what is " + w, wiki.Extract, 2, Embedder);
                var wa = AceAnswer.Say(first.Count > 0 ? string.Join(" ", first) : wiki.Extract, "web", 0.8);
                wa.Cards.Add(new InfoCard { Title = wiki.Title, Body = wiki.Extract, Source = "Wikipedia", Url = wiki.Url, ImageUrl = wiki.ImageUrl });
                return wa;
            }
            var (part, meaning, example) = def.Meanings[0];
            string text = $"{Composer.Capital(def.Word)}{(part.Length > 0 ? " (" + part + ")" : "")}: {meaning}" + (example.Length > 0 ? $" Like: \"{example}\"" : "");
            var a = AceAnswer.Say(text, "words");
            a.Cards.Add(new InfoCard
            {
                Title = def.Word + (def.Phonetic.Length > 0 ? "  " + def.Phonetic : ""),
                Body = string.Join("\n", def.Meanings.Select((d, i) => $"{i + 1}. ({d.Part}) {d.Meaning}" + (d.Example.Length > 0 ? $" — \"{d.Example}\"" : ""))),
                Source = "Free Dictionary",
            });
            Bank.Learn(new[] { "what does " + w + " mean", "define " + w }, text, "dictionary", "words");
            return a;
        }

        private static ResultsCard WebCard(string title, IEnumerable<WebResult> results)
        {
            var card = new ResultsCard { Title = title };
            foreach (var r in results.Take(6)) card.Items.Add(new ResultItem { Title = r.Title, Subtitle = r.Host + (r.Snippet.Length > 0 ? " · " + Short(r.Snippet, 110) : ""), Target = r.Url });
            return card;
        }

        // ===================================================== Web lookups

        private async Task<AceAnswer?> WebSearchAsync(Turn t)
        {
            string q = Regex.Replace(t.Text, @"^(look up|search (the )?(web|internet) for|find out|google it|search it up|search online for|check online|research)\s*", "", RegexOptions.IgnoreCase).Trim();
            if (q.Length == 0 || Regex.IsMatch(q, @"^(it|that|this)$", RegexOptions.IgnoreCase)) q = Talk.LastKnowledgeQuestion;
            if (q.Length == 0) return AceAnswer.Say("What should I look up?", "web", 0.5);
            Talk.LastKnowledgeQuestion = q;
            var r = await ResearchAsync(q, t, forceWeb: true).ConfigureAwait(false);
            if (r == null) return AceAnswer.Say("I couldn't reach the web right now.", "web", 0.2);
            var a = AceAnswer.Say(r.Answer.Length > 0 ? r.Answer : "I searched, but no page answered it clearly. Here's what I found.", "web", r.Confidence);
            if (r.Sources.Count > 0) a.Cards.Add(WebCard("From the web", r.Sources));
            return a;
        }

        // ===================================================== Timers and reminders

        private static readonly Regex DurationPart = new(@"\b(\d+(?:\.\d+)?|an?|one|two|three|four|five|six|seven|eight|nine|ten|fifteen|twenty|thirty|forty|forty-five|sixty|half an?)\s*(seconds?|secs?|s\b|minutes?|mins?|m\b|hours?|hrs?|h\b)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static TimeSpan ParseDuration(string text, out string rest)
        {
            var total = TimeSpan.Zero;
            foreach (Match m in DurationPart.Matches(text))
            {
                string n = m.Groups[1].Value.ToLowerInvariant();
                double v = n.StartsWith("half") ? 0.5 : n is "a" or "an" ? 1 : NaturalTime.ParseNumber(n);
                if (double.IsNaN(v) || v <= 0) continue;
                string u = m.Groups[2].Value.ToLowerInvariant();
                total += u.StartsWith("h") ? TimeSpan.FromHours(v) : u.StartsWith("m") ? TimeSpan.FromMinutes(v) : TimeSpan.FromSeconds(v);
            }
            if (Regex.IsMatch(text, @"\band a half\b") && total > TimeSpan.Zero)
                total += Regex.IsMatch(text, @"hours?\s+and a half|and a half hours?") ? TimeSpan.FromMinutes(30) : TimeSpan.FromSeconds(30);
            rest = DurationPart.Replace(text, " ");
            return total;
        }

        public static string SpanText(TimeSpan s)
        {
            var parts = new List<string>();
            if (s.Hours + s.Days * 24 > 0) parts.Add(Composer.Plural(s.Hours + s.Days * 24, "hour", "hours"));
            if (s.Minutes > 0) parts.Add(Composer.Plural(s.Minutes, "minute", "minutes"));
            if (s.Seconds > 0 && s.TotalMinutes < 10) parts.Add(Composer.Plural(s.Seconds, "second", "seconds"));
            return parts.Count == 0 ? "a moment" : Composer.List(parts);
        }

        private Task<AceAnswer?> TimerAsync(Turn t)
        {
            if (t.Has(@"\b(how (much|long)|time left|left on)\b"))
            {
                var timers = ReminderStore.Pending().Where(r => r.IsTimer).ToList();
                if (timers.Count == 0) return Task.FromResult<AceAnswer?>(AceAnswer.Say("No timers running.", "timer"));
                var left = AceAnswer.Say(string.Join(" ", timers.Select(r => $"{(r.Text.Length > 0 ? Composer.Capital(r.Text) + " timer" : "Your timer")}: {SpanText(r.Due - DateTime.Now)} left.")), "timer");
                left.Cards.Add(new TimersCard { Title = "Your timers" });
                return Task.FromResult<AceAnswer?>(left);
            }
            var length = ParseDuration(t.Lower, out string rest);
            if (length <= TimeSpan.Zero) return Task.FromResult<AceAnswer?>(AceAnswer.Say("How long should the timer be? Say something like \"timer for 10 minutes\".", "timer", 0.5));
            var label = Regex.Match(rest, @"\bfor\s+(?:the\s+|my\s+)?([a-z][a-z ]{1,30}?)\s*$");
            string what = label.Success && !Regex.IsMatch(label.Groups[1].Value, @"^(a|an|the|timer)$") ? label.Groups[1].Value.Trim() : "";
            ReminderStore.Add(what, DateTime.Now + length, isTimer: true);
            Step("Started a timer", SpanText(length));
            var set = AceAnswer.Say(Composer.Pick("timer", new[] { "Timer set for {0}.", "Okay, {0}. I'll let you know.", "{0}, starting now." }).Replace("{0}", SpanText(length)) + (what.Length > 0 ? $" ({what})" : ""), "timer");
            set.Cards.Add(new TimersCard { Title = "Your timers" });
            return Task.FromResult<AceAnswer?>(set);
        }

        private Task<AceAnswer?> CancelTimersAsync(Turn t)
        {
            int n = ReminderStore.CancelTimers();
            return Task.FromResult<AceAnswer?>(AceAnswer.Say(n == 0 ? "There weren't any timers running." : $"Cancelled {Composer.Plural(n, "timer", "timers")}.", "timer"));
        }

        private Task<AceAnswer?> ReminderAsync(Turn t)
        {
            var m = t.Match(@"\bremind me\b(.*)$");
            string body = m.Success ? m.Groups[1].Value : Regex.Replace(t.Lower, @"\bset (a |an )?reminder\b", "");
            var now = DateTime.Now;
            var when = NaturalTime.Parse(body, now);
            string what = Regex.Replace(when.Rest, @"^\s*(to|that|about|of|for)\s+", "").Trim(' ', '.', ',', '?');
            what = Regex.Replace(what, @"\s+(at|on|in|by)$", "").Trim();
            if (what.Length == 0) what = "your reminder";
            DateTime due;
            string note = "";
            if (when.Exact != null) due = when.Exact.Value;
            else if (when.Date != null || when.Time != null) due = when.StartOr(now, new TimeSpan(9, 0, 0));
            else { due = now.AddHours(1); note = " (you didn't say when, so I picked an hour from now)"; }
            if (due <= now) due = due.AddDays(1);
            ReminderStore.Add(what, due, isTimer: false);
            Step("Saved a reminder", due.ToString("ddd h:mm tt"));
            return Task.FromResult<AceAnswer?>(AceAnswer.Say($"I'll remind you to {what} {NaturalTime.Friendly(due, now)}{note}.", "reminder"));
        }

        private Task<AceAnswer?> ListRemindersAsync(Turn t)
        {
            var list = ReminderStore.Pending();
            if (list.Count == 0) return Task.FromResult<AceAnswer?>(AceAnswer.Say("You don't have any reminders or timers set.", "reminder"));
            var card = new ResultsCard { Title = "Reminders and timers" };
            foreach (var r in list.Take(10))
                card.Items.Add(new ResultItem { Title = r.IsTimer ? "Timer" + (r.Text.Length > 0 ? ": " + r.Text : "") : Composer.Capital(r.Text), Subtitle = NaturalTime.Friendly(r.Due, DateTime.Now) });
            string Name(Reminder r) => r.IsTimer ? (r.Text.Length > 0 ? r.Text + " timer" : "a timer") : r.Text;
            string text = list.Count == 1
                ? $"You have 1 reminder: {Name(list[0])} {NaturalTime.Friendly(list[0].Due, DateTime.Now)}."
                : $"You have {Composer.Plural(list.Count, "reminder", "reminders")}: {string.Join("; ", list.Take(5).Select(r => Name(r) + " " + NaturalTime.Friendly(r.Due, DateTime.Now)))}{(list.Count > 5 ? "; and more" : "")}.";
            var a = AceAnswer.Say(text, "reminder");
            a.Speak = list.Count <= 3 ? text : $"You have {list.Count} reminders. Next: {Name(list[0])} {NaturalTime.Friendly(list[0].Due, DateTime.Now)}.";
            a.Cards.Add(card);
            return Task.FromResult<AceAnswer?>(a);
        }
    }
}
