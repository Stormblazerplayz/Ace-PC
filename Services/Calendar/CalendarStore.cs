using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nimbus.Services.Calendar
{
    /// <summary>One thing on the Nimbus calendar.</summary>
    public sealed class CalendarEvent
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = "";
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public bool AllDay { get; set; }
        public string Notes { get; set; } = "";
        /// <summary>One of the CalendarStore.Colors names: violet, blue, green, amber, pink, red.</summary>
        public string Color { get; set; } = "violet";
        /// <summary>Minutes before Start that Ace pops a reminder. 0 = no reminder.</summary>
        public int RemindMinutes { get; set; } = 10;
        /// <summary>Set once Ace has shown the reminder, so it only shows once.</summary>
        public bool Reminded { get; set; }

        [JsonIgnore] public TimeSpan Length => End - Start;

        public CalendarEvent Copy() => (CalendarEvent)MemberwiseClone();

        /// <summary>"Thu Nov 20, 3:00 PM to 4:00 PM" or "Thu Nov 20 (all day)".</summary>
        [JsonIgnore]
        public string WhenText
        {
            get
            {
                if (AllDay) return Start.ToString("ddd MMM d") + " (all day)";
                string end = End.Date == Start.Date ? End.ToString("h:mm tt") : End.ToString("ddd MMM d, h:mm tt");
                return Start.ToString("ddd MMM d, h:mm tt") + " to " + end;
            }
        }

        [JsonIgnore] public string TimeText => AllDay ? "All day" : Start.ToString("h:mm tt");
    }

    /// <summary>
    /// The Nimbus calendar. Everything is saved on this PC in %LOCALAPPDATA%\Nimbus\calendar.json.
    /// Safe to call from any thread. Changed fires on whatever thread made the change.
    /// </summary>
    public static class CalendarStore
    {
        public static readonly string[] Colors = { "violet", "blue", "green", "amber", "pink", "red" };

        public static string FilePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nimbus", "calendar.json");

        private static readonly object Gate = new();
        private static List<CalendarEvent>? _events;
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static event Action? Changed;

        private static List<CalendarEvent> Events
        {
            get
            {
                if (_events != null) return _events;
                try
                {
                    if (File.Exists(FilePath))
                        _events = JsonSerializer.Deserialize<List<CalendarEvent>>(File.ReadAllText(FilePath), JsonOptions);
                }
                catch
                {
                    // A broken file is kept as a backup so nothing is lost, then we start fresh.
                    try { File.Copy(FilePath, FilePath + ".broken", true); } catch { }
                }
                _events ??= new List<CalendarEvent>();
                return _events;
            }
        }

        public static IReadOnlyList<CalendarEvent> All()
        {
            lock (Gate) return Events.Select(e => e.Copy()).OrderBy(e => e.Start).ToList();
        }

        /// <summary>Events that touch the day.</summary>
        public static IReadOnlyList<CalendarEvent> OnDay(DateTime day) => Between(day.Date, day.Date.AddDays(1));

        /// <summary>Events that overlap [from, to).</summary>
        public static IReadOnlyList<CalendarEvent> Between(DateTime from, DateTime to)
        {
            lock (Gate)
                return Events.Where(e => e.Start < to && (e.End > from || (e.End <= e.Start && e.Start >= from)))
                             .Select(e => e.Copy()).OrderBy(e => e.AllDay ? 0 : 1).ThenBy(e => e.Start).ToList();
        }

        public static IReadOnlyList<CalendarEvent> Upcoming(int count = 5)
        {
            var now = DateTime.Now;
            lock (Gate)
                return Events.Where(e => e.End > now || (e.AllDay && e.Start.Date >= now.Date))
                             .OrderBy(e => e.Start).Take(count).Select(e => e.Copy()).ToList();
        }

        public static CalendarEvent? Get(string id)
        {
            lock (Gate) return Events.FirstOrDefault(e => e.Id == id)?.Copy();
        }

        public static CalendarEvent Add(CalendarEvent e)
        {
            Fix(e);
            lock (Gate) { Events.Add(e.Copy()); Save(); }
            Changed?.Invoke();
            return e;
        }

        public static bool Update(CalendarEvent e)
        {
            Fix(e);
            lock (Gate)
            {
                int i = Events.FindIndex(x => x.Id == e.Id);
                if (i < 0) return false;
                Events[i] = e.Copy();
                Save();
            }
            Changed?.Invoke();
            return true;
        }

        public static bool Delete(string id)
        {
            bool removed;
            lock (Gate)
            {
                removed = Events.RemoveAll(e => e.Id == id) > 0;
                if (removed) Save();
            }
            if (removed) Changed?.Invoke();
            return removed;
        }

        /// <summary>Marks a reminder as shown.</summary>
        public static void MarkReminded(string id)
        {
            lock (Gate)
            {
                var e = Events.FirstOrDefault(x => x.Id == id);
                if (e == null || e.Reminded) return;
                e.Reminded = true;
                Save();
            }
        }

        /// <summary>
        /// Finds events by words in the title, best match first. Used by Ace ("move the dentist to Friday").
        /// Upcoming events win over old ones when the score ties.
        /// </summary>
        public static List<CalendarEvent> Search(string words, DateTime? onDay = null)
        {
            var tokens = Tokens(words);
            var now = DateTime.Now;
            lock (Gate)
            {
                return Events
                    .Where(e => onDay == null || e.Start.Date == onDay.Value.Date)
                    .Select(e => (e, score: Score(Tokens(e.Title), tokens)))
                    .Where(x => x.score > 0 || (tokens.Count == 0 && onDay != null))
                    .OrderByDescending(x => x.score)
                    .ThenBy(x => x.e.Start >= now.Date ? 0 : 1)
                    .ThenBy(x => Math.Abs((x.e.Start - now).TotalHours))
                    .Select(x => x.e.Copy())
                    .ToList();
            }
        }

        private static double Score(List<string> title, List<string> query)
        {
            if (query.Count == 0 || title.Count == 0) return 0;
            double s = 0;
            foreach (var q in query)
            {
                if (title.Contains(q)) s += 1;
                else if (title.Any(t => t.StartsWith(q, StringComparison.Ordinal) || q.StartsWith(t, StringComparison.Ordinal))) s += 0.6;
            }
            return s / query.Count;
        }

        private static readonly HashSet<string> Filler = new(StringComparer.OrdinalIgnoreCase)
        { "the", "a", "an", "my", "to", "for", "on", "at", "in", "with", "event", "appointment", "thing" };

        private static List<string> Tokens(string s) =>
            s.ToLowerInvariant().Split(new[] { ' ', ',', '.', '!', '?', '\'', '"', '-', '/' }, StringSplitOptions.RemoveEmptyEntries)
             .Where(t => !Filler.Contains(t)).ToList();

        private static void Fix(CalendarEvent e)
        {
            e.Title = string.IsNullOrWhiteSpace(e.Title) ? "Untitled" : e.Title.Trim();
            if (e.AllDay)
            {
                e.Start = e.Start.Date;
                if (e.End <= e.Start) e.End = e.Start.AddDays(1);
            }
            else if (e.End <= e.Start) e.End = e.Start.AddHours(1);
            if (!Colors.Contains(e.Color)) e.Color = "violet";
            if (e.RemindMinutes < 0) e.RemindMinutes = 0;
        }

        /// <summary>Call only while holding Gate. Writes to a temp file first so a crash can't leave half a file.</summary>
        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(Events.OrderBy(e => e.Start).ToList(), JsonOptions));
                File.Move(tmp, FilePath, true);
            }
            catch { /* saving is best effort; the events stay in memory */ }
        }
    }
}
