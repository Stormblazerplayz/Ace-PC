using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Nimbus.AceV4.Pc
{
    public sealed class DayActivity
    {
        public DateTime First { get; set; }
        public DateTime Last { get; set; }
    }

    public sealed record SleepSummary(int Nights, TimeSpan AvgBed, TimeSpan AvgWake, TimeSpan AvgSleep, TimeSpan SuggestBed);

    /// <summary>
    /// Works out your sleep from when you use the PC: the last time you used it at night and the first time
    /// the next morning. A "day" runs from 4 AM to 4 AM, so staying up past midnight still counts as that evening.
    /// Saved only on this PC, in %LOCALAPPDATA%\Nimbus\ace-v4\activity.json.
    /// </summary>
    public static class ActivityTracker
    {
        private static string FilePath => Path.Combine(AceV4Config.Folder, "activity.json");
        private static readonly object Gate = new();
        private static Dictionary<string, DayActivity>? _days;
        private static DateTime _lastSave;
        public static readonly TimeSpan DayStarts = TimeSpan.FromHours(4);

        public static DateTime LogicalDay(DateTime t) => (t - DayStarts).Date;
        public static string Key(DateTime day) => day.ToString("yyyy-MM-dd");

        private static Dictionary<string, DayActivity> Days
        {
            get
            {
                if (_days != null) return _days;
                try
                {
                    if (File.Exists(FilePath))
                        _days = JsonSerializer.Deserialize<Dictionary<string, DayActivity>>(File.ReadAllText(FilePath));
                }
                catch { }
                _days ??= new Dictionary<string, DayActivity>();
                return _days;
            }
        }

        /// <summary>You were using the PC at this moment.</summary>
        public static void Record(DateTime when)
        {
            lock (Gate)
            {
                string key = Key(LogicalDay(when));
                if (!Days.TryGetValue(key, out var d)) Days[key] = d = new DayActivity { First = when, Last = when };
                if (when < d.First) d.First = when;
                if (when > d.Last) d.Last = when;
                if (DateTime.Now - _lastSave > TimeSpan.FromMinutes(5)) Save();
            }
        }

        /// <summary>The first time you used the PC today (logical day), if Ace saw it.</summary>
        public static DateTime? FirstToday()
        {
            lock (Gate) return Days.TryGetValue(Key(LogicalDay(DateTime.Now)), out var d) ? d.First : null;
        }

        public static void Save()
        {
            lock (Gate)
            {
                try
                {
                    // Keep 120 days.
                    var cutoff = Key(DateTime.Now.AddDays(-120));
                    foreach (var k in Days.Keys.Where(k => string.CompareOrdinal(k, cutoff) < 0).ToList()) Days.Remove(k);
                    Directory.CreateDirectory(AceV4Config.Folder);
                    File.WriteAllText(FilePath, JsonSerializer.Serialize(Days));
                    _lastSave = DateTime.Now;
                }
                catch { }
            }
        }

        /// <summary>
        /// Fills in the last few weeks from Windows' own log (start-ups, shutdowns, sleep, wake and sign-ins),
        /// so the bedtime tip works on day one. Runs once; safe to call again.
        /// </summary>
        public static void BackfillFromEventLog(int days = 30)
        {
            try
            {
                long ms = (long)TimeSpan.FromDays(days).TotalMilliseconds;
                string xpath = "*[System[(EventID=6005 or EventID=6006 or EventID=1 or EventID=42 or EventID=7001 or EventID=7002 or EventID=107 or EventID=506 or EventID=507) and TimeCreated[timediff(@SystemTime) <= " + ms + "]]]";
                var query = new EventLogQuery("System", PathType.LogName, xpath) { ReverseDirection = false };
                using var reader = new EventLogReader(query);
                int n = 0;
                for (EventRecord? e = reader.ReadEvent(); e != null && n < 20000; e = reader.ReadEvent(), n++)
                {
                    using (e)
                    {
                        if (e.TimeCreated is not DateTime t) continue;
                        string provider = e.ProviderName ?? "";
                        // Event 1 is used by many providers; only Power-Troubleshooter's means "woke up".
                        if (e.Id == 1 && !provider.Contains("Power-Troubleshooter", StringComparison.OrdinalIgnoreCase)) continue;
                        if (e.Id == 42 && !provider.Contains("Kernel-Power", StringComparison.OrdinalIgnoreCase)) continue;
                        Record(t);
                    }
                }
                Save();
            }
            catch { /* the log can be off or locked down; tracking from now on still works */ }
        }

        /// <summary>Averages over the last 21 nights. Null until there are at least 3 nights.</summary>
        public static SleepSummary? Summary(double goalHours)
        {
            List<(DateTime Bed, DateTime Wake)> nights;
            lock (Gate)
            {
                nights = new List<(DateTime, DateTime)>();
                var today = LogicalDay(DateTime.Now);
                for (int i = 1; i <= 21; i++)
                {
                    var day = today.AddDays(-i);
                    if (!Days.TryGetValue(Key(day), out var evening) || !Days.TryGetValue(Key(day.AddDays(1)), out var morning)) continue;
                    var gap = morning.First - evening.Last;
                    if (gap < TimeSpan.FromHours(3) || gap > TimeSpan.FromHours(15)) continue;
                    nights.Add((evening.Last, morning.First));
                }
            }
            if (nights.Count < 3) return null;

            // Bedtimes wrap around midnight, so average them as minutes after noon.
            double bedMin = nights.Average(n => ((n.Bed.TimeOfDay - TimeSpan.FromHours(12)).TotalMinutes + 1440) % 1440);
            double wakeMin = nights.Average(n => n.Wake.TimeOfDay.TotalMinutes);
            double sleepMin = nights.Average(n => (n.Wake - n.Bed).TotalMinutes);
            var avgBed = TimeSpan.FromMinutes((bedMin + 720) % 1440);
            var avgWake = TimeSpan.FromMinutes(wakeMin);
            var suggest = TimeSpan.FromMinutes(((wakeMin - goalHours * 60) % 1440 + 1440) % 1440);
            // Round to the nearest quarter hour, which is easier to remember.
            suggest = TimeSpan.FromMinutes(Math.Round(suggest.TotalMinutes / 15) * 15 % 1440);
            return new SleepSummary(nights.Count, avgBed, avgWake, TimeSpan.FromMinutes(sleepMin), suggest);
        }

        public static string Clock(TimeSpan t) => DateTime.Today.Add(t).ToString("h:mm tt");

        public static string Hours(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" : $"{t.Minutes}m";
    }
}
