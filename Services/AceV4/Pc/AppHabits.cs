using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Nimbus.AceV4.Pc
{
    /// <summary>
    /// Learns which apps you open at what times ("Minecraft around 4 PM on weekdays") and suggests opening them.
    /// It notices an app when it shows up that wasn't open a moment ago. Saved only on this PC (app-habits.json),
    /// keeps 4 weeks, and stops offering an app after you say no to it 3 times.
    /// </summary>
    public static class AppHabits
    {
        public sealed class Data
        {
            /// <summary>App name → times it was opened ("2026-11-14 16").</summary>
            public Dictionary<string, List<string>> Opens { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            /// <summary>App name → the day Ace last offered it.</summary>
            public Dictionary<string, string> Offered { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            /// <summary>App name → how many times you said no.</summary>
            public Dictionary<string, int> Declined { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            /// <summary>App name → the program file, so it can be opened again even if it isn't in the Start menu.</summary>
            public Dictionary<string, string> Paths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        public sealed record Habit(string App, int Hour, int Days, bool Weekend);

        private static readonly object Gate = new();
        private static Data? _data;
        private static HashSet<string>? _lastOpen;
        private static DateTime _lastSave;
        private static string FilePath => Path.Combine(AceV4Config.Folder, "app-habits.json");
        /// <summary>Opened on this many different days at the same hour = a habit.</summary>
        public const int DaysNeeded = 3;
        public const int MaxDeclines = 3;

        /// <summary>Set by tests so nothing is written to disk.</summary>
        public static bool DontSave;

        private static Data D
        {
            get
            {
                if (_data != null) return _data;
                try
                {
                    if (File.Exists(FilePath)) _data = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath));
                    if (_data != null)
                    {
                        // JSON loses the ignore-case comparer.
                        _data.Opens = new Dictionary<string, List<string>>(_data.Opens, StringComparer.OrdinalIgnoreCase);
                        _data.Offered = new Dictionary<string, string>(_data.Offered, StringComparer.OrdinalIgnoreCase);
                        _data.Declined = new Dictionary<string, int>(_data.Declined, StringComparer.OrdinalIgnoreCase);
                        _data.Paths = new Dictionary<string, string>(_data.Paths ?? new(), StringComparer.OrdinalIgnoreCase);
                    }
                }
                catch (Exception ex) { Debug.WriteLine("App habits: " + ex.Message); }
                return _data ??= new Data();
            }
        }

        /// <summary>For tests: start from nothing.</summary>
        public static void Reset() { lock (Gate) { _data = new Data(); _lastOpen = null; } }

        private static string Stamp(DateTime t) => t.ToString("yyyy-MM-dd HH");
        private static bool IsWeekend(DateTime d) => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

        /// <summary>
        /// Call every minute or so with the apps that have windows open now. The first call only remembers what's open
        /// (apps already open when Nimbus started weren't "opened" just now).
        /// </summary>
        public static void Record(DateTime now, IEnumerable<string> openApps, Func<string, string?>? pathOf = null)
        {
            var open = new HashSet<string>(openApps.Where(a => !string.IsNullOrWhiteSpace(a) && !IsSkipped(a)), StringComparer.OrdinalIgnoreCase);
            lock (Gate)
            {
                if (_lastOpen != null)
                {
                    foreach (var app in open.Where(a => !_lastOpen.Contains(a)))
                    {
                        if (!D.Opens.TryGetValue(app, out var list)) D.Opens[app] = list = new List<string>();
                        string stamp = Stamp(now);
                        if (!list.Contains(stamp)) list.Add(stamp);
                        if (pathOf != null && !D.Paths.ContainsKey(app))
                        {
                            try { if (pathOf(app) is { Length: > 0 } path) D.Paths[app] = path; } catch { }
                        }
                    }
                    // Keep 4 weeks.
                    string cutoff = Stamp(now.AddDays(-28));
                    foreach (var key in D.Opens.Keys.ToList())
                    {
                        D.Opens[key].RemoveAll(s => string.CompareOrdinal(s, cutoff) < 0);
                        if (D.Opens[key].Count == 0) D.Opens.Remove(key);
                    }
                }
                _lastOpen = open;
                if (now - _lastSave > TimeSpan.FromMinutes(15)) { Save(); _lastSave = now; }
            }
        }

        /// <summary>Nimbus itself, Windows bits and the like aren't habits.</summary>
        private static bool IsSkipped(string app) =>
            System.Text.RegularExpressions.Regex.IsMatch(app, @"^(nimbus|explorer|file explorer|settings|task manager|search|start|shellexperiencehost|applicationframehost|textinputhost|lockapp|windows security|control panel|system settings)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>Your habits, most regular first: which app, at what hour, on how many different days.</summary>
        public static List<Habit> Habits(DateTime now, int lookbackDays = 21)
        {
            var result = new List<Habit>();
            lock (Gate)
            {
                foreach (var (app, stamps) in D.Opens)
                {
                    var times = stamps.Select(s => DateTime.TryParseExact(s, "yyyy-MM-dd HH", null, System.Globalization.DateTimeStyles.None, out var t) ? t : (DateTime?)null)
                                      .Where(t => t.HasValue && t.Value > now.AddDays(-lookbackDays)).Select(t => t!.Value).ToList();
                    foreach (var group in times.GroupBy(t => (t.Hour, Weekend: IsWeekend(t))))
                    {
                        int days = group.Select(t => t.Date).Distinct().Count();
                        if (days >= DaysNeeded) result.Add(new Habit(app, group.Key.Hour, days, group.Key.Weekend));
                    }
                }
            }
            return result.OrderByDescending(h => h.Days).ThenBy(h => h.Hour).ToList();
        }

        /// <summary>
        /// An app worth offering right now: you usually open it at this hour (on this kind of day), it isn't open,
        /// you haven't opened it today, it wasn't offered today and you haven't turned it down 3 times.
        /// </summary>
        public static Habit? Suggest(DateTime now, IEnumerable<string> openApps)
        {
            var open = new HashSet<string>(openApps, StringComparer.OrdinalIgnoreCase);
            string today = now.ToString("yyyy-MM-dd");
            bool weekend = IsWeekend(now);
            lock (Gate)
            {
                foreach (var h in Habits(now))
                {
                    if (h.Hour != now.Hour || h.Weekend != weekend) continue;
                    if (open.Contains(h.App)) continue;
                    if (D.Offered.TryGetValue(h.App, out var day) && day == today) continue;
                    if (D.Declined.TryGetValue(h.App, out int no) && no >= MaxDeclines) continue;
                    if (D.Opens.TryGetValue(h.App, out var stamps) && stamps.Any(s => s.StartsWith(today, StringComparison.Ordinal))) continue;
                    return h;
                }
            }
            return null;
        }

        /// <summary>The program file seen for this app, if any.</summary>
        public static string? PathOf(string app) { lock (Gate) return D.Paths.TryGetValue(app, out var p) && File.Exists(p) ? p : null; }

        public static void MarkOffered(string app, DateTime now) { lock (Gate) { D.Offered[app] = now.ToString("yyyy-MM-dd"); Save(); } }

        public static void MarkDeclined(string app)
        {
            lock (Gate) { D.Declined[app] = D.Declined.TryGetValue(app, out int n) ? n + 1 : 1; Save(); }
        }

        public static bool GaveUpOn(string app) { lock (Gate) return D.Declined.TryGetValue(app, out int n) && n >= MaxDeclines; }

        /// <summary>Forget everything learned (or just one app).</summary>
        public static void Forget(string? app = null)
        {
            lock (Gate)
            {
                if (app == null) { _data = new Data(); }
                else { D.Opens.Remove(app); D.Offered.Remove(app); D.Declined.Remove(app); D.Paths.Remove(app); }
                Save();
            }
        }

        public static void Save()
        {
            if (DontSave) return;
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(AceV4Config.Folder);
                    File.WriteAllText(FilePath, JsonSerializer.Serialize(D));
                }
                catch (Exception ex) { Debug.WriteLine("App habits save: " + ex.Message); }
            }
        }

        /// <summary>"4 PM" / "9 AM".</summary>
        public static string HourText(int hour) => new DateTime(2000, 1, 1, hour, 0, 0).ToString("h tt", System.Globalization.CultureInfo.CurrentCulture);
    }
}
