using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nimbus.AceV4
{
    /// <summary>What NaturalTime found in a sentence.</summary>
    public sealed class WhenResult
    {
        public DateTime? Date;          // the day (time of day ignored)
        public TimeSpan? Time;          // start time of day
        public TimeSpan? EndTime;       // "from 3 to 4"
        public TimeSpan? Duration;      // "for 2 hours"
        public DateTime? Exact;         // "in 20 minutes" gives an exact moment
        public bool AllDay;
        /// <summary>The sentence with the date and time words taken out (used as an event title).</summary>
        public string Rest = "";
        public bool Found => Date != null || Time != null || Exact != null || Duration != null || AllDay;

        /// <summary>The start moment, filling gaps: no day = today (or tomorrow if that time has passed).</summary>
        public DateTime StartOr(DateTime now, TimeSpan defaultTime)
        {
            if (Exact != null) return Exact.Value;
            var day = Date ?? now.Date;
            var time = Time ?? defaultTime;
            var start = day.Date + time;
            if (Date == null && Time != null && start < now) start = start.AddDays(1);
            return start;
        }

        public DateTime EndFor(DateTime start, TimeSpan defaultLength)
        {
            if (EndTime != null)
            {
                var end = start.Date + EndTime.Value;
                if (end <= start) end = end.AddDays(1);
                return end;
            }
            return start + (Duration ?? defaultLength);
        }
    }

    /// <summary>
    /// Reads dates and times out of everyday words: "tomorrow at 5", "next friday 3-4pm", "in 20 minutes",
    /// "nov 20th", "11/20", "this weekend", "for 2 hours", "all day". No internet needed.
    /// </summary>
    public static class NaturalTime
    {
        private static readonly string[] DayNames = { "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday" };

        private static readonly Dictionary<string, int> Months = new()
        {
            ["jan"] = 1, ["january"] = 1, ["feb"] = 2, ["february"] = 2, ["mar"] = 3, ["march"] = 3, ["apr"] = 4, ["april"] = 4,
            ["may"] = 5, ["jun"] = 6, ["june"] = 6, ["jul"] = 7, ["july"] = 7, ["aug"] = 8, ["august"] = 8,
            ["sep"] = 9, ["sept"] = 9, ["september"] = 9, ["oct"] = 10, ["october"] = 10, ["nov"] = 11, ["november"] = 11,
            ["dec"] = 12, ["december"] = 12,
        };

        private static readonly Dictionary<string, int> NumberWords = new()
        {
            ["a"] = 1, ["an"] = 1, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6,
            ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["fifteen"] = 15,
            ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["forty-five"] = 45, ["fortyfive"] = 45, ["sixty"] = 60,
            ["ninety"] = 90, ["half an"] = 1, ["a couple of"] = 2, ["a couple"] = 2, ["a few"] = 3, ["few"] = 3,
        };

        private const string Num = @"(\d+(?:\.\d+)?|a couple of|a couple|a few|few|half an|an|a|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|fifteen|twenty|thirty|forty-five|fortyfive|forty|sixty|ninety)";
        private const string MonthRx = @"(jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|june?|july?|aug(?:ust)?|sept?(?:ember)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)";
        private const string Clock = @"(\d{1,2})(?::(\d{2}))?\s*(a\.?m\.?|p\.?m\.?|am|pm)?";

        private static readonly RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        public static double ParseNumber(string s)
        {
            s = s.Trim().ToLowerInvariant();
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
            return NumberWords.TryGetValue(s, out int n) ? n : 1;
        }

        public static TimeSpan? ParseUnit(double n, string unit, bool half = false)
        {
            unit = unit.ToLowerInvariant();
            if (half) n = 0.5;
            if (unit.StartsWith("sec")) return TimeSpan.FromSeconds(n);
            if (unit.StartsWith("min")) return TimeSpan.FromMinutes(n);
            if (unit.StartsWith("h")) return TimeSpan.FromHours(n);
            if (unit.StartsWith("day")) return TimeSpan.FromDays(n);
            if (unit.StartsWith("week") || unit.StartsWith("wk")) return TimeSpan.FromDays(7 * n);
            return null;
        }

        public static WhenResult Parse(string text, DateTime now)
        {
            var r = new WhenResult();
            var cut = new List<(int start, int len)>();
            string t = " " + text + " ";

            void Take(Match m) { if (m.Success) cut.Add((m.Index, m.Length)); }

            // ---- exact offsets: "in 20 minutes", "in an hour", "in half an hour", "20 minutes from now"
            var m = Regex.Match(t, @"\b(?:in|after)\s+" + Num + @"\s+(seconds?|secs?|minutes?|mins?|hours?|hrs?|h|days?|weeks?|wks?)\b", Opt);
            if (!m.Success) m = Regex.Match(t, @"\b" + Num + @"\s+(seconds?|secs?|minutes?|mins?|hours?|hrs?|days?|weeks?)\s+from\s+now\b", Opt);
            if (m.Success)
            {
                bool half = m.Groups[1].Value.Equals("half an", StringComparison.OrdinalIgnoreCase);
                var span = ParseUnit(ParseNumber(m.Groups[1].Value), m.Groups[2].Value, half);
                if (span != null)
                {
                    var exact = now + span.Value;
                    if (span.Value.TotalDays >= 1 && span.Value.TotalDays == Math.Floor(span.Value.TotalDays)) r.Date = exact.Date;
                    else r.Exact = exact;
                    Take(m);
                }
            }
            else if ((m = Regex.Match(t, @"\bin\s+half\s+an?\s+hour\b", Opt)).Success)
            {
                r.Exact = now.AddMinutes(30); Take(m);
            }

            // ---- durations: "for 2 hours", "for an hour", "for 30 min"
            m = Regex.Match(t, @"\bfor\s+" + Num + @"\s+(minutes?|mins?|hours?|hrs?|h)\b", Opt);
            if (m.Success)
            {
                bool half = m.Groups[1].Value.Equals("half an", StringComparison.OrdinalIgnoreCase);
                r.Duration = ParseUnit(ParseNumber(m.Groups[1].Value), m.Groups[2].Value, half);
                Take(m);
            }

            if ((m = Regex.Match(t, @"\ball[\s-]day\b", Opt)).Success) { r.AllDay = true; Take(m); }

            // ---- days
            if (r.Date == null && (m = Regex.Match(t, @"\b(?:the\s+)?day\s+after\s+tomorrow\b", Opt)).Success) { r.Date = now.Date.AddDays(2); Take(m); }
            else if (r.Date == null && (m = Regex.Match(t, @"\b(tomorrow|tmrw|tmr|tomorow)\b", Opt)).Success) { r.Date = now.Date.AddDays(1); Take(m); }
            else if (r.Date == null && (m = Regex.Match(t, @"\b(today|tonight|this\s+(?:morning|afternoon|evening))\b", Opt)).Success)
            {
                r.Date = now.Date;
                string w = m.Value.ToLowerInvariant();
                if (w.Contains("tonight") || w.Contains("evening")) r.Time ??= w.Contains("evening") ? new TimeSpan(18, 0, 0) : new TimeSpan(20, 0, 0);
                else if (w.Contains("morning")) r.Time ??= new TimeSpan(9, 0, 0);
                else if (w.Contains("afternoon")) r.Time ??= new TimeSpan(15, 0, 0);
                Take(m);
            }
            else if (r.Date == null && (m = Regex.Match(t, @"\byesterday\b", Opt)).Success) { r.Date = now.Date.AddDays(-1); Take(m); }

            if (r.Date == null && (m = Regex.Match(t, @"\b(?:this\s+)?weekend\b", Opt)).Success)
            {
                int toSat = ((int)DayOfWeek.Saturday - (int)now.DayOfWeek + 7) % 7;
                r.Date = now.Date.AddDays(toSat); Take(m);
            }
            if (r.Date == null && (m = Regex.Match(t, @"\bnext\s+week\b", Opt)).Success)
            {
                int toMon = ((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7;
                r.Date = now.Date.AddDays(toMon == 0 ? 7 : toMon); Take(m);
            }
            if (r.Date == null && (m = Regex.Match(t, @"\bnext\s+month\b", Opt)).Success)
            {
                r.Date = new DateTime(now.Year, now.Month, 1).AddMonths(1); Take(m);
            }

            if (r.Date == null && (m = Regex.Match(t, @"\b(?:(on|this|next|coming)\s+)?(sun|mon|tue|tues|wed|thu|thur|thurs|fri|sat)(?:day|nesday|sday|urday|rsday)?\b", Opt)).Success)
            {
                string key = m.Groups[2].Value.ToLowerInvariant().Substring(0, 3);
                int dow = Array.FindIndex(DayNames, d => d.StartsWith(key, StringComparison.Ordinal));
                // Skip "sat" in "sat down", "sun" in "sun is out", "wed" alone: only accept full names or a lead word.
                bool full = Regex.IsMatch(m.Value, @"(sunday|monday|tuesday|wednesday|thursday|friday|saturday)", Opt);
                if (dow >= 0 && (full || m.Groups[1].Success))
                {
                    int ahead = (dow - (int)now.DayOfWeek + 7) % 7;
                    string lead = m.Groups[1].Value.ToLowerInvariant();
                    if (lead == "next" && ahead == 0) ahead = 7;
                    else if (lead == "next" && ahead < 3) ahead += 7;   // "next friday" said on wednesday = the one after
                    r.Date = now.Date.AddDays(ahead);
                    Take(m);
                }
            }

            // "nov 20", "november 20th, 2026"
            if (r.Date == null && (m = Regex.Match(t, @"\b" + MonthRx + @"\.?\s+(\d{1,2})(?:st|nd|rd|th)?(?:,?\s+(\d{4}))?\b", Opt)).Success)
            {
                r.Date = MakeDate(now, Months[MonthKey(m.Groups[1].Value)], int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : null);
                if (r.Date != null) Take(m);
            }
            // "20th of november", "20 nov"
            if (r.Date == null && (m = Regex.Match(t, @"\b(\d{1,2})(?:st|nd|rd|th)?\s+(?:of\s+)?" + MonthRx + @"\b(?:,?\s+(\d{4}))?", Opt)).Success)
            {
                r.Date = MakeDate(now, Months[MonthKey(m.Groups[2].Value)], int.Parse(m.Groups[1].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : null);
                if (r.Date != null) Take(m);
            }
            // 2026-11-20
            if (r.Date == null && (m = Regex.Match(t, @"\b(\d{4})-(\d{1,2})-(\d{1,2})\b", Opt)).Success)
            {
                r.Date = MakeDate(now, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), int.Parse(m.Groups[1].Value));
                if (r.Date != null) Take(m);
            }
            // 11/20 or 11/20/2026 (US order, which is what Windows here uses)
            if (r.Date == null && (m = Regex.Match(t, @"\b(\d{1,2})/(\d{1,2})(?:/(\d{2,4}))?\b", Opt)).Success)
            {
                int? year = null;
                if (m.Groups[3].Success) { int y = int.Parse(m.Groups[3].Value); year = y < 100 ? 2000 + y : y; }
                r.Date = MakeDate(now, int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), year);
                if (r.Date != null) Take(m);
            }
            // "on the 20th"
            if (r.Date == null && (m = Regex.Match(t, @"\b(?:on\s+)?the\s+(\d{1,2})(?:st|nd|rd|th)\b", Opt)).Success)
            {
                int day = int.Parse(m.Groups[1].Value);
                var d = MakeDate(now, now.Month, day, now.Year);
                if (d != null && d < now.Date) d = MakeDate(now, now.AddMonths(1).Month, day, now.AddMonths(1).Year);
                r.Date = d;
                if (r.Date != null) Take(m);
            }

            // ---- times
            // ranges: "from 3 to 4pm", "3-4pm", "3pm - 5pm", "between 2 and 3"
            m = Regex.Match(t, @"\b(?:from|between)?\s*" + Clock + @"\s*(?:-|–|to|till|until|and)\s*" + Clock + @"(?=\W)", Opt);
            if (m.Success && (m.Groups[3].Success || m.Groups[6].Success || Regex.IsMatch(m.Value, @"\b(from|between)\b|:", Opt)))
            {
                string endAmPm = m.Groups[6].Value;
                string startAmPm = m.Groups[3].Success ? m.Groups[3].Value : endAmPm;
                var start = ToTime(m.Groups[1].Value, m.Groups[2].Value, startAmPm);
                var end = ToTime(m.Groups[4].Value, m.Groups[5].Value, endAmPm);
                if (start != null && end != null)
                {
                    // "11 to 1pm": the start is the morning one
                    if (!m.Groups[3].Success && end.Value.Hours >= 12 && start.Value.Hours >= 12 && start > end) start = start.Value.Add(TimeSpan.FromHours(-12));
                    r.Time = start; r.EndTime = end; Take(m);
                }
            }
            if (r.Time == null || r.Exact == null)
            {
                if ((m = Regex.Match(t, @"\b(?:at\s+)?(noon|midday)\b", Opt)).Success) { r.Time = new TimeSpan(12, 0, 0); Take(m); }
                else if ((m = Regex.Match(t, @"\b(?:at\s+)?midnight\b", Opt)).Success) { r.Time = new TimeSpan(23, 59, 0); Take(m); }
            }
            if (r.Time == null)
            {
                // "at 5", "at 5:30 pm", "5pm", "17:00", "at 7 in the morning"
                m = Regex.Match(t, @"\bat\s+" + Clock + @"(\s+in\s+the\s+(morning|afternoon|evening)|\s+at\s+night|\s+tonight)?(?=\W)", Opt);
                if (!m.Success) m = Regex.Match(t, @"\b(\d{1,2})(?::(\d{2}))?\s*(a\.?m\.?|p\.?m\.?|am|pm)(\s+in\s+the\s+(morning|afternoon|evening))?(?=\W)", Opt);
                if (!m.Success) m = Regex.Match(t, @"\b([01]?\d|2[0-3]):([0-5]\d)()(\s+in\s+the\s+(morning|afternoon|evening))?(?=\W)", Opt);
                if (m.Success)
                {
                    string ampm = m.Groups[3].Value;
                    string part = m.Groups[4].Value.ToLowerInvariant();
                    if (ampm.Length == 0 && part.Length > 0) ampm = part.Contains("morning") ? "am" : "pm";
                    var time = ToTime(m.Groups[1].Value, m.Groups[2].Value, ampm);
                    if (time != null) { r.Time = time; Take(m); }
                }
            }
            if (r.Time == null && (m = Regex.Match(t, @"\b(?:in\s+the\s+|this\s+)?(morning|afternoon|evening)\b", Opt)).Success)
            {
                string w = m.Groups[1].Value.ToLowerInvariant();
                r.Time = w == "morning" ? new TimeSpan(9, 0, 0) : w == "afternoon" ? new TimeSpan(15, 0, 0) : new TimeSpan(18, 0, 0);
                Take(m);
            }
            if (r.Time == null && r.EndTime == null && (m = Regex.Match(t, @"\b(?:until|till|to)\s+" + Clock + @"(?=\W)", Opt)).Success && (m.Groups[3].Success || m.Groups[2].Success))
            {
                r.EndTime = ToTime(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
                if (r.EndTime != null) Take(m);
            }

            // ---- what's left is the title
            var keep = t.ToCharArray();
            foreach (var (start, len) in cut)
                for (int i = start; i < start + len && i < keep.Length; i++) keep[i] = ' ';
            string rest = Regex.Replace(new string(keep), @"\s+", " ").Trim();
            rest = Regex.Replace(rest, @"^(on|at|for|from|by|this|next)\s+|\s+(on|at|for|from|by|this|next|in)$", "", Opt).Trim(' ', ',', '.');
            r.Rest = rest;
            return r;
        }

        private static string MonthKey(string s)
        {
            s = s.ToLowerInvariant().TrimEnd('.');
            if (Months.ContainsKey(s)) return s;
            return s.Length >= 3 ? s.Substring(0, 3) : s;
        }

        private static DateTime? MakeDate(DateTime now, int month, int day, int? year)
        {
            if (month < 1 || month > 12 || day < 1) return null;
            int y = year ?? now.Year;
            if (day > DateTime.DaysInMonth(y, month)) return null;
            var d = new DateTime(y, month, day);
            // No year said and the day already passed this year: they mean next year.
            if (year == null && d < now.Date.AddDays(-1)) d = d.AddYears(1);
            return d;
        }

        /// <summary>"5", "30", "pm" to 17:30. A bare 1 to 7 means afternoon (nobody books 3 AM).</summary>
        public static TimeSpan? ToTime(string hour, string minute, string ampm)
        {
            if (!int.TryParse(hour, out int h)) return null;
            int min = 0;
            if (minute.Length > 0 && !int.TryParse(minute, out min)) return null;
            if (min > 59 || h > 23) return null;
            string a = ampm.Replace(".", "").Trim().ToLowerInvariant();
            if (a == "pm" && h < 12) h += 12;
            else if (a == "am" && h == 12) h = 0;
            else if (a.Length == 0 && minute.Length == 0 && h >= 1 && h <= 7) h += 12;
            else if (a.Length == 0 && h >= 1 && h <= 6 && minute.Length > 0) h += 12;
            if (a.Length > 0 && h > 23) return null;
            return new TimeSpan(h, min, 0);
        }

        /// <summary>"in 3 days", "tomorrow at 5:00 PM", "Friday, Nov 20".</summary>
        public static string Friendly(DateTime when, DateTime now, bool withTime = true)
        {
            string day;
            int diff = (when.Date - now.Date).Days;
            if (diff == 0) day = "today";
            else if (diff == 1) day = "tomorrow";
            else if (diff == -1) day = "yesterday";
            else if (diff > 1 && diff < 7) day = when.ToString("dddd");
            else day = when.ToString("ddd, MMM d");
            return withTime ? day + " at " + when.ToString("h:mm tt") : day;
        }
    }
}
