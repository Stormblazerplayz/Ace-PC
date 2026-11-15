using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Nimbus.AceV4.Brain;
using Nimbus.AceV4.Pc;

namespace Nimbus.AceV4
{
    /// <summary>The focus timer, the weekly PC health report and the app habits Ace learns.</summary>
    public sealed partial class AceV4Brain
    {
        // ===================================================== Focus timer

        private Task<AceAnswer?> FocusAsync(Turn t) => Task.FromResult<AceAnswer?>(Focus(t));

        private AceAnswer Focus(Turn t)
        {
            var now = DateTime.Now;
            string l = t.Lower;

            // "how much did I focus today", "focus stats"
            if (t.Has(@"\bhow (?:much|long) (?:did|have) i (?:focus|stud|concentrat|work)|\bfocus (?:stats|minutes|history)\b|\bhow much (?:focus|study) time\b(?!.*\bleft\b)"))
            {
                int today = FocusTimer.MinutesToday(now), week = FocusTimer.MinutesThisWeek(now);
                Step("Adding up your focus time", $"{today} min today, {week} min this week");
                string text = today == 0 && week == 0
                    ? "You haven't used the focus timer this week yet. Say \"start a focus timer\" and I'll keep track."
                    : (today > 0 ? $"You focused for {FocusTimer.Say(today)} today" : "You haven't focused yet today") +
                      (week > today ? $", and {FocusTimer.Say(week)} this week." : ".") + (week >= 300 ? " That's awesome!" : "");
                return Done(AceAnswer.Say(text, "focus"));
            }

            bool running = FocusTimer.Running;
            if (t.Has(@"\b(?:stop|end|cancel|quit|finish|turn off|kill|done with)\b"))
            {
                if (!running) return Done(AceAnswer.Say("The focus timer isn't running. Say \"start a focus timer\" to begin one.", "focus"));
                int rounds = FocusTimer.RoundsDone;
                int minutes = FocusTimer.Stop(now);
                Step("Stopped the focus timer", $"{minutes} min");
                string said = minutes < 1
                    ? "Okay, I stopped the focus timer."
                    : Composer.Pick("focus.stop", new[] { "Focus timer stopped. You focused for {0}.", "Done! That was {0} of focus.", "Okay, stopped. Nice work: {0} of focus." }).Replace("{0}", FocusTimer.Say(minutes));
                var a = AceAnswer.Say(said + (rounds > 0 ? $" ({Composer.Plural(rounds, "round", "rounds")} finished.)" : ""), "focus");
                return Done(a);
            }
            if (t.Has(@"\b(?:resume|unpause|un-pause|continue|keep going)\b"))
            {
                if (!FocusTimer.Resume(now)) return Done(AceAnswer.Say(running ? "The focus timer is already going." : "The focus timer isn't running.", "focus"));
                return Done(FocusAnswer($"Back to it! {FocusTimer.Say((int)Math.Ceiling(FocusTimer.Left.TotalMinutes))} left."));
            }
            if (t.Has(@"\bpause\b"))
            {
                if (!FocusTimer.Pause(now)) return Done(AceAnswer.Say(running ? "It's already paused. Say \"resume focus\" to keep going." : "The focus timer isn't running.", "focus"));
                return Done(FocusAnswer("Paused. Say \"resume focus\" when you're ready."));
            }
            if (t.Has(@"\bskip\b"))
            {
                string? said = FocusTimer.Skip(now);
                return Done(said == null ? AceAnswer.Say("The focus timer isn't running.", "focus") : FocusAnswer(said));
            }
            if (running && t.Has(@"\b(?:how (?:much|long)|time left|left\b|status|what round)"))
                return Done(FocusAnswer(FocusStatus(now)));

            // Start (or start over).
            var length = ParseDuration(l, out string rest);
            int focusMinutes = length > TimeSpan.Zero ? (int)Math.Round(Math.Clamp(length.TotalMinutes, 5, 180)) : Config.FocusMinutes;
            string task = FocusTask(rest);
            FocusTimer.Start(now, focusMinutes, task);
            Step("Started the focus timer", $"{focusMinutes} min{(task.Length > 0 ? " · " + task : "")}");
            string start = Composer.Pick("focus.start", new[]
            {
                "Focus timer on: {0} minutes{1}. I'll show it on my eyes and tell you when it's break time.",
                "Let's focus{1}! {0} minutes, then a break. I'll keep quiet until then.",
                "Okay, {0} minutes of focus{1}. You've got this!",
            }).Replace("{0}", focusMinutes.ToString()).Replace("{1}", task.Length > 0 ? " on " + task : "");
            return Done(FocusAnswer(start));
        }

        /// <summary>"math homework" from "help me focus on my math homework for 30 minutes".</summary>
        public static string FocusTask(string rest)
        {
            string r = Regex.Replace(rest.ToLowerInvariant(), @"\s+", " ").Trim(' ', '.', '!', '?');
            var m = Regex.Match(r, @"\b(?:focus|study|work|concentrate)(?:ing)?\s+(?:on|for)\s+(?:my\s+|the\s+|a\s+)?([a-z].*)$");
            if (!m.Success) m = Regex.Match(r, @"\b(?:study|studying|do|doing|finish|finishing)\s+(?:my\s+|the\s+|some\s+)?(?!timer|mode|session|time\b|for\b)([a-z].*)$");
            if (!m.Success) return "";
            string task = Regex.Replace(m.Groups[1].Value, @"\b(?:for|with|and|please|now|timer|focus timer|pomodoro)\s*$", "").Trim();
            task = Regex.Replace(task, @"\s+(?:for|with)$", "").Trim();
            return task.Length > 40 ? task[..40].TrimEnd() : task;
        }

        private static string FocusStatus(DateTime now)
        {
            int left = (int)Math.Ceiling(FocusTimer.LeftAt(now).TotalMinutes);
            string what = FocusTimer.Phase == FocusPhase.Focus ? $"Round {FocusTimer.Round} of focus" : FocusTimer.PhaseName;
            return $"{what}: {FocusTimer.Say(Math.Max(1, left))} left{(FocusTimer.Paused ? " (paused)" : "")}.";
        }

        public static AceAnswer FocusAnswer(string text)
        {
            var a = AceAnswer.Say(text, "focus");
            a.Cards.Add(new FocusCard());
            a.Mood = "happy";
            return a;
        }

        // ===================================================== Weekly PC health report

        private Task<AceAnswer?> HealthReportAsync(Turn t) => Task.FromResult<AceAnswer?>(HealthReport(t));

        private AceAnswer HealthReport(Turn t)
        {
            var now = DateTime.Now;
            var report = HealthLog.Report(now);
            Step("Grading the last 7 days", $"{HealthLog.Count} samples");
            if (report == null)
            {
                int n = HealthLog.Count;
                return Done(AceAnswer.Say(n == 0
                    ? "I just started keeping track. I take a quick look at your PC every 10 minutes while you use it, so ask me again in a few hours and I'll have a report card ready."
                    : $"I need a bit more to go on. I've looked at your PC {Composer.Plural(n, "time", "times")} so far, and I check every 10 minutes while you use it. Ask again in an hour or so!", "report", 0.8));
            }
            return Done(HealthReportAnswer(report, weekly: false));
        }

        /// <summary>The report card answer (also used for the automatic Sunday report).</summary>
        public static AceAnswer HealthReportAnswer(HealthReport r, bool weekly)
        {
            string lead = Composer.Pick(weekly ? "report.weekly" : "report.grade", weekly
                ? new[] { "It's Sunday, so here's your PC's report card: {g}.", "Weekly PC report time! Your PC got {0}.", "Your PC's grade this week: {g}." }
                : new[] { "Your PC gets {0} this week.", "This week's grade: {g}.", "Report card says {0}." })
                .Replace("{0}", (r.Grade.StartsWith("A") || r.Grade.StartsWith("F") ? "an " : "a ") + r.Grade).Replace("{g}", r.Grade);
            var parts = new List<string> { lead };
            if (r.LastWeekGrade != null && r.LastWeekGrade != r.Grade)
                parts.Add(GradeRank(r.Grade) > GradeRank(r.LastWeekGrade) ? $"That's up from {r.LastWeekGrade} last week!" : $"That's down from {r.LastWeekGrade} last week.");
            else if (r.LastWeekGrade != null) parts.Add("Same as last week.");
            var worst = r.Lines.OrderBy(x => GradeRank(x.Grade)).First();
            if (GradeRank(worst.Grade) < GradeRank("B")) parts.Add($"{worst.Label} needs the most help ({SmallFirst(worst.Detail).TrimEnd('.')}).");
            if (r.Tips.Count > 0 && !r.Tips[0].StartsWith("Nothing to fix")) parts.Add(r.Tips[0]);
            if (r.FocusMinutes > 0) parts.Add($"You also focused for {FocusTimer.Say(r.FocusMinutes)} this week.");
            if (r.Days < 5) parts.Add($"(This is from {HealthLog.Span(r.Days)} of watching, so it gets more accurate over the week.)");
            var a = AceAnswer.Say(string.Join(" ", parts), "report");
            a.Speak = lead + (parts.Count > 1 ? " " + parts[1] : "");
            a.Cards.Add(new ReportCard
            {
                Title = weekly ? "Weekly PC report" : "PC health report",
                Grade = r.Grade,
                Subtitle = $"Last {HealthLog.Span(Math.Min(7, r.Days))} · {r.Samples} checks" + (r.LastWeekGrade != null ? $" · last week {r.LastWeekGrade}" : ""),
                Lines = r.Lines.ToList(),
                Tips = r.Tips.ToList(),
            });
            a.Mood = GradeRank(r.Grade) >= GradeRank("B") ? "happy" : GradeRank(r.Grade) >= GradeRank("C") ? "think" : "alert";
            return a;
        }

        /// <summary>"Averaged 70°C" → "averaged 70°C", but "CPU averaged" stays.</summary>
        private static string SmallFirst(string s) => s.Length > 1 && char.IsUpper(s[0]) && !char.IsUpper(s[1]) ? char.ToLowerInvariant(s[0]) + s[1..] : s;

        private static readonly string[] Grades = { "F", "D", "C-", "C", "C+", "B-", "B", "B+", "A-", "A" };
        private static int GradeRank(string g) => Array.IndexOf(Grades, g);

        // ===================================================== App habits

        private Task<AceAnswer?> HabitsAsync(Turn t) => Task.FromResult<AceAnswer?>(Habits(t));

        private AceAnswer Habits(Turn t)
        {
            var now = DateTime.Now;
            if (t.Has(@"\b(?:stop|quit|don'?t|do not|no more|turn off|disable)\b"))
            {
                Config.AppSuggestions = false;
                Config.Save();
                return Done(AceAnswer.Say("Okay, I won't offer to open apps anymore. Say \"start suggesting apps\" to turn it back on.", "habits"));
            }
            if (t.Has(@"\b(?:start|turn on|enable|resume)\b"))
            {
                Config.AppSuggestions = true;
                Config.Save();
                return Done(AceAnswer.Say("Okay! When it's around the time you usually open an app, I'll offer to open it.", "habits"));
            }
            if (t.Has(@"\b(?:forget|clear|reset|delete)\b"))
            {
                AppHabits.Forget();
                return Done(AceAnswer.Say("Done, I forgot which apps you usually open. I'll start learning again from now.", "habits"));
            }

            var habits = AppHabits.Habits(now);
            Step("Looking at which apps you open when", Composer.Plural(habits.Count, "habit", "habits"));
            if (habits.Count == 0)
                return Done(AceAnswer.Say("I haven't spotted a habit yet. When you open the same app around the same time on 3 different days, I'll notice and offer to open it for you.", "habits", 0.8));
            var top = habits.GroupBy(h => h.App, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).Take(5).ToList();
            var lines = top.Select(h => $"{h.App} around {AppHabits.HourText(h.Hour)} on {(h.Weekend ? "weekends" : "weekdays")}").ToList();
            var a = AceAnswer.Say("You usually open " + Composer.List(lines) + "." + (Config.AppSuggestions ? (top.Count == 1 ? " I'll offer to open it around that time." : " I'll offer to open them around those times.") : ""), "habits");
            var card = new ResultsCard { Title = "Your app habits" };
            foreach (var h in habits.Take(8))
                card.Items.Add(new ResultItem { Title = h.App, Subtitle = $"Around {AppHabits.HourText(h.Hour)} on {(h.Weekend ? "weekends" : "weekdays")} · {Composer.Plural(h.Days, "day", "days")} in the last 3 weeks" });
            a.Cards.Add(card);
            return Done(a);
        }

        /// <summary>"Open Minecraft? You usually open it around 4 PM." with Open / Not now buttons.</summary>
        public static AceAnswer HabitOffer(AppHabits.Habit h)
        {
            string app = h.App;
            var a = AceAnswer.Say(Composer.Pick("habit.offer", new[]
            {
                "You usually open {0} around {1}. Want me to open it?",
                "It's about {0} time! Should I open it?",
                "Hey, you normally start {0} around {1}. Open it now?",
            }).Replace("{0}", app).Replace("{1}", AppHabits.HourText(h.Hour)), "habits");
            a.Speak = $"Want me to open {app}?";
            a.Mood = "happy";
            a.Cards.Add(new ConfirmCard
            {
                Question = $"Open {app}?",
                YesLabel = "Open " + app,
                NoLabel = "Not now",
                OnYes = async () =>
                {
                    string? opened = await AppLauncher.OpenAsync(app).ConfigureAwait(false);
                    if (opened == null && AppHabits.PathOf(app) is string path) { AppLauncher.Start(path); opened = app; }
                    return opened != null ? $"Opening {opened}." : $"I couldn't find {app} to open. Try \"open {app}\".";
                },
                OnNo = () =>
                {
                    AppHabits.MarkDeclined(app);
                    return AppHabits.GaveUpOn(app) ? $"Okay, I'll stop offering {app}." : "Okay, maybe later!";
                },
            });
            return a;
        }
    }
}
