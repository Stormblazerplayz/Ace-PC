using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Nimbus.AceV4.Brain;

namespace Nimbus.AceV4.Pc
{
    /// <summary>
    /// Screen time: how long each app was in front while you were actually using the PC (no input for
    /// 2 minutes doesn't count), per day, for 30 days. Optional daily limits for the whole PC and for single
    /// apps, with gentle heads-ups. Nothing is ever blocked or closed. Saved in screentime.json on this PC.
    /// </summary>
    public static class ScreenTime
    {
        public sealed class Data
        {
            /// <summary>"yyyy-MM-dd" → app → seconds.</summary>
            public Dictionary<string, Dictionary<string, double>> Days { get; set; } = new();
            /// <summary>Daily limit for the whole PC in minutes, 0 = none.</summary>
            public int LimitMinutes { get; set; }
            /// <summary>App → daily limit in minutes.</summary>
            public Dictionary<string, int> AppLimits { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            /// <summary>"yyyy-MM-dd|what|stage" for heads-ups already given.</summary>
            public HashSet<string> Warned { get; set; } = new();
        }

        private static readonly object Gate = new();
        private static Data? _data;
        private static DateTime _lastTick, _lastSave;
        private static string FilePath => Path.Combine(AceV4Config.Folder, "screentime.json");
        public static bool DontSave;
        /// <summary>Raised when limits change.</summary>
        public static event Action? Changed;

        private static Data D
        {
            get
            {
                if (_data != null) return _data;
                try { if (File.Exists(FilePath)) _data = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)); } catch { }
                _data ??= new Data();
                _data.AppLimits = new Dictionary<string, int>(_data.AppLimits ?? new(), StringComparer.OrdinalIgnoreCase);
                _data.Warned ??= new();
                return _data;
            }
        }

        public static string Key(DateTime day) => ActivityTracker.LogicalDay(day).ToString("yyyy-MM-dd");

        /// <summary>The app in front right now ("Minecraft", "Chrome"), or null.</summary>
        public static string? ForegroundApp()
        {
            try
            {
                var hwnd = Win32.GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return null;
                Win32.GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0) return null;
                using var p = Process.GetProcessById((int)pid);
                string name = p.ProcessName;
                if (name.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return "Desktop and File Explorer";
                if (name.Equals("LockApp", StringComparison.OrdinalIgnoreCase)) return null;
                return WindowControl.AppName(name);
            }
            catch { return null; }
        }

        /// <summary>Called every few seconds. Adds the time since the last call to the app in front, if you're active.</summary>
        public static void Tick(DateTime now, string? app, double idleSeconds)
        {
            lock (Gate)
            {
                double gap = _lastTick == default ? 0 : (now - _lastTick).TotalSeconds;
                _lastTick = now;
                if (app == null || idleSeconds > 120 || gap <= 0 || gap > 30) return;
                string key = Key(now);
                if (!D.Days.TryGetValue(key, out var day)) D.Days[key] = day = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                day[app] = (day.TryGetValue(app, out var s) ? s : 0) + gap;
                if (now - _lastSave > TimeSpan.FromMinutes(5)) Save();
            }
        }

        /// <summary>Seconds per app on a day, most first.</summary>
        public static List<(string App, double Seconds)> Day(DateTime day)
        {
            lock (Gate)
                return D.Days.TryGetValue(Key(day), out var d) ? d.Select(kv => (kv.Key, kv.Value)).OrderByDescending(x => x.Value).ToList() : new();
        }

        public static TimeSpan Total(DateTime day) => TimeSpan.FromSeconds(Day(day).Sum(x => x.Seconds));

        public static TimeSpan AppTotal(DateTime day, string app)
        {
            var d = Day(day);
            var hit = d.FirstOrDefault(x => x.App.Equals(app, StringComparison.OrdinalIgnoreCase));
            if (hit.App == null) hit = d.FirstOrDefault(x => x.App.Contains(app, StringComparison.OrdinalIgnoreCase) || app.Contains(x.App, StringComparison.OrdinalIgnoreCase));
            return TimeSpan.FromSeconds(hit.App == null ? 0 : hit.Seconds);
        }

        /// <summary>The last 7 days, oldest first: (day, total).</summary>
        public static List<(DateTime Day, TimeSpan Total)> Week(DateTime now)
        {
            var today = ActivityTracker.LogicalDay(now);
            return Enumerable.Range(0, 7).Select(i => today.AddDays(i - 6)).Select(d => (d, Total(d + ActivityTracker.DayStarts))).ToList();
        }

        public static int LimitMinutes { get { lock (Gate) return D.LimitMinutes; } }
        public static Dictionary<string, int> AppLimits { get { lock (Gate) return new Dictionary<string, int>(D.AppLimits, StringComparer.OrdinalIgnoreCase); } }

        public static void SetLimit(int minutes)
        {
            lock (Gate) { D.LimitMinutes = Math.Clamp(minutes, 0, 24 * 60); Save(); }
            Changed?.Invoke();
        }

        public static void SetAppLimit(string app, int minutes)
        {
            lock (Gate)
            {
                if (minutes <= 0) D.AppLimits.Remove(app); else D.AppLimits[app] = Math.Clamp(minutes, 1, 24 * 60);
                Save();
            }
            Changed?.Invoke();
        }

        public static void ClearLimits()
        {
            lock (Gate) { D.LimitMinutes = 0; D.AppLimits.Clear(); Save(); }
            Changed?.Invoke();
        }

        /// <summary>
        /// Gentle heads-ups to show now (each once): 15 minutes left, at the limit, and 30 minutes over.
        /// </summary>
        public static List<string> Warnings(DateTime now)
        {
            var say = new List<string>();
            lock (Gate)
            {
                string day = Key(now);
                void Check(string what, string label, int limit, TimeSpan used)
                {
                    if (limit <= 0) return;
                    double left = limit - used.TotalMinutes;
                    string stage = left <= -30 ? "over" : left <= 0 ? "limit" : left <= 15 && limit > 20 ? "soon" : "";
                    if (stage.Length == 0) return;
                    string key = $"{day}|{what}|{stage}";
                    if (!D.Warned.Add(key)) return;
                    // Skip the earlier stages if they were never shown (Nimbus was closed).
                    if (stage == "limit") D.Warned.Add($"{day}|{what}|soon");
                    if (stage == "over") { D.Warned.Add($"{day}|{what}|soon"); D.Warned.Add($"{day}|{what}|limit"); }
                    string amount = Span(TimeSpan.FromMinutes(limit));
                    say.Add(stage switch
                    {
                        "soon" => $"Heads up: about {Composer.Plural((int)Math.Ceiling(left), "minute", "minutes")} left of your {amount} {label} today.",
                        "limit" => $"That's your {amount} {label} for today. Maybe a good time for a break?",
                        _ => $"You're 30 minutes past your {amount} {label}. Totally your call, but a break might feel good.",
                    });
                }
                Check("all", "of screen time", D.LimitMinutes, TimeSpan.FromSeconds(D.Days.TryGetValue(day, out var d) ? d.Values.Sum() : 0));
                foreach (var (app, limit) in D.AppLimits)
                    Check(app, "on " + app, limit, AppTotal(now, app));
                D.Warned.RemoveWhere(w => string.CompareOrdinal(w, Key(now.AddDays(-2))) < 0);
                if (say.Count > 0) Save();
            }
            return say;
        }

        /// <summary>"2 hours 5 minutes", "45 minutes", "less than a minute".</summary>
        public static string Span(TimeSpan t)
        {
            int h = (int)t.TotalHours, m = t.Minutes;
            if (h == 0 && m == 0) return "less than a minute";
            if (h == 0) return Composer.Plural(m, "minute", "minutes");
            return m == 0 ? Composer.Plural(h, "hour", "hours") : $"{Composer.Plural(h, "hour", "hours")} {Composer.Plural(m, "minute", "minutes")}";
        }

        public static void Save()
        {
            if (DontSave) return;
            lock (Gate)
            {
                try
                {
                    var cutoff = Key(DateTime.Now.AddDays(-31));
                    foreach (var old in D.Days.Keys.Where(k => string.CompareOrdinal(k, cutoff) < 0).ToList()) D.Days.Remove(old);
                    Directory.CreateDirectory(AceV4Config.Folder);
                    File.WriteAllText(FilePath, JsonSerializer.Serialize(D));
                    _lastSave = DateTime.Now;
                }
                catch { }
            }
        }
    }
}
