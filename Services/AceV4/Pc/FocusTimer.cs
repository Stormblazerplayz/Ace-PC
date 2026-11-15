using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Nimbus.AceV4.Brain;

namespace Nimbus.AceV4.Pc
{
    public enum FocusPhase { Off, Focus, ShortBreak, LongBreak }

    /// <summary>
    /// The focus (study) timer: 25 minutes of focus, then a 5-minute break, and a longer 15-minute break after every
    /// 4th round. The eyes show how far along you are. Minutes you focused are saved per day (focus-log.json) so the
    /// weekly report can tell you how much you studied.
    /// </summary>
    public static class FocusTimer
    {
        public const int DefaultFocus = 25, ShortBreak = 5, LongBreak = 15, RoundsBeforeLongBreak = 4;

        private static readonly object Gate = new();
        private static Dictionary<string, int>? _log;
        private static string FilePath => Path.Combine(AceV4Config.Folder, "focus-log.json");

        public static FocusPhase Phase { get; private set; }
        public static DateTime PhaseStart { get; private set; }
        public static DateTime PhaseEnd { get; private set; }
        /// <summary>Which focus round this is (1, 2, 3...).</summary>
        public static int Round { get; private set; }
        /// <summary>Focus rounds finished since you started.</summary>
        public static int RoundsDone { get; private set; }
        /// <summary>How long each focus round is, in minutes.</summary>
        public static int FocusMinutes { get; private set; } = DefaultFocus;
        /// <summary>What you're working on ("math homework"), if you said.</summary>
        public static string Task { get; private set; } = "";
        public static bool Paused { get; private set; }
        private static TimeSpan _pausedLeft;

        /// <summary>Raised (on any thread) whenever the timer starts, stops, pauses or changes phase.</summary>
        public static event Action? Changed;

        public static bool Running => Phase != FocusPhase.Off;
        public static bool OnBreak => Phase is FocusPhase.ShortBreak or FocusPhase.LongBreak;

        public static TimeSpan Length => Phase switch
        {
            FocusPhase.Focus => TimeSpan.FromMinutes(FocusMinutes),
            FocusPhase.ShortBreak => TimeSpan.FromMinutes(ShortBreak),
            FocusPhase.LongBreak => TimeSpan.FromMinutes(LongBreak),
            _ => TimeSpan.Zero,
        };

        public static TimeSpan LeftAt(DateTime now)
        {
            if (!Running) return TimeSpan.Zero;
            var left = Paused ? _pausedLeft : PhaseEnd - now;
            return left < TimeSpan.Zero ? TimeSpan.Zero : left;
        }

        public static TimeSpan Left => LeftAt(DateTime.Now);

        /// <summary>0 at the start of a phase, 1 at the end.</summary>
        public static double ProgressAt(DateTime now)
        {
            var len = Length;
            if (!Running || len <= TimeSpan.Zero) return 0;
            return Math.Clamp(1 - LeftAt(now).TotalSeconds / len.TotalSeconds, 0, 1);
        }

        public static double Progress => ProgressAt(DateTime.Now);

        public static string PhaseName => Phase switch
        {
            FocusPhase.Focus => "Focus",
            FocusPhase.ShortBreak => "Short break",
            FocusPhase.LongBreak => "Long break",
            _ => "Off",
        };

        /// <summary>Starts focusing (or starts over). minutes: 1 to 180, default 25.</summary>
        public static void Start(DateTime now, int minutes = DefaultFocus, string task = "")
        {
            lock (Gate)
            {
                if (Phase == FocusPhase.Focus) LogFocus(now - PhaseStart, PhaseStart);
                FocusMinutes = Math.Clamp(minutes, 1, 180);
                Task = task.Trim();
                Round = 1;
                RoundsDone = 0;
                Paused = false;
                Begin(FocusPhase.Focus, now);
            }
            Changed?.Invoke();
        }

        /// <summary>Stops the timer. Returns the minutes you focused in this run (rounds done plus the round you were in).</summary>
        public static int Stop(DateTime now)
        {
            int minutes;
            lock (Gate)
            {
                if (!Running) return 0;
                minutes = RoundsDone * FocusMinutes;
                if (Phase == FocusPhase.Focus)
                {
                    var did = Paused ? Length - _pausedLeft : now - PhaseStart;
                    LogFocus(did, PhaseStart);
                    minutes += (int)Math.Round(Math.Max(0, did.TotalMinutes));
                }
                Phase = FocusPhase.Off;
                Paused = false;
            }
            Changed?.Invoke();
            return minutes;
        }

        public static bool Pause(DateTime now)
        {
            lock (Gate)
            {
                if (!Running || Paused) return false;
                _pausedLeft = LeftAt(now);
                Paused = true;
            }
            Changed?.Invoke();
            return true;
        }

        public static bool Resume(DateTime now)
        {
            lock (Gate)
            {
                if (!Running || !Paused) return false;
                Paused = false;
                PhaseEnd = now + _pausedLeft;
                PhaseStart = PhaseEnd - Length;
            }
            Changed?.Invoke();
            return true;
        }

        /// <summary>Skips to the next phase (break → focus, focus → break). Returns what to say, or null if it's off.</summary>
        public static string? Skip(DateTime now)
        {
            lock (Gate)
            {
                if (!Running) return null;
                Paused = false;
                PhaseEnd = now;
            }
            return Advance(now);
        }

        /// <summary>
        /// Call often (every few seconds). When a phase ends it moves on and returns what Ace should say
        /// ("Round 2 done! Take a 5-minute break."); otherwise null.
        /// </summary>
        public static string? Advance(DateTime now)
        {
            string? say = null;
            lock (Gate)
            {
                if (!Running || Paused || now < PhaseEnd) return null;
                if (Phase == FocusPhase.Focus)
                {
                    LogFocus(PhaseEnd - PhaseStart, PhaseStart);
                    RoundsDone++;
                    bool longBreak = RoundsDone % RoundsBeforeLongBreak == 0;
                    Begin(longBreak ? FocusPhase.LongBreak : FocusPhase.ShortBreak, now);
                    say = Composer.Pick(longBreak ? "focus.longbreak" : "focus.break", longBreak
                        ? new[] { "That's {0} rounds! Take a longer {1}-minute break. You earned it.", "{0} rounds done, nice! Time for a {1}-minute break.", "Awesome, {0} rounds! Enjoy a {1}-minute break." }
                        : new[] { "Round {0} done! Take a {1}-minute break.", "Nice focus! Break time: {1} minutes.", "Round {0} finished. Stretch, drink some water, and rest for {1} minutes." })
                        .Replace("{0}", RoundsDone.ToString()).Replace("{1}", (longBreak ? LongBreak : ShortBreak).ToString());
                }
                else
                {
                    Round = RoundsDone + 1;
                    Begin(FocusPhase.Focus, now);
                    say = Composer.Pick("focus.back", new[] { "Break's over! Round {0}: {1} minutes of focus.", "Back to it! Round {0} starts now, {1} minutes.", "Ready? Round {0}, {1} minutes. You've got this." })
                        .Replace("{0}", Round.ToString()).Replace("{1}", FocusMinutes.ToString());
                }
            }
            Changed?.Invoke();
            return say;
        }

        private static void Begin(FocusPhase phase, DateTime now)
        {
            Phase = phase;
            PhaseStart = now;
            PhaseEnd = now + Length;
        }

        // ===================================================== The log (minutes focused per day)

        private static Dictionary<string, int> Log
        {
            get
            {
                if (_log != null) return _log;
                try
                {
                    if (File.Exists(FilePath))
                        _log = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(FilePath));
                }
                catch (Exception ex) { Debug.WriteLine("Focus log: " + ex.Message); }
                return _log ??= new Dictionary<string, int>();
            }
        }

        /// <summary>Set by tests so nothing is written to disk.</summary>
        public static bool DontSave;

        private static void LogFocus(TimeSpan did, DateTime start)
        {
            int minutes = (int)Math.Round(did.TotalMinutes);
            if (minutes < 1) return;
            string day = start.ToString("yyyy-MM-dd");
            lock (Log)
            {
                Log[day] = Log.TryGetValue(day, out int m) ? m + minutes : minutes;
                // Keep about two months.
                foreach (var old in Log.Keys.Where(k => string.CompareOrdinal(k, start.AddDays(-62).ToString("yyyy-MM-dd")) < 0).ToList()) Log.Remove(old);
                if (DontSave) return;
                try
                {
                    Directory.CreateDirectory(AceV4Config.Folder);
                    File.WriteAllText(FilePath, JsonSerializer.Serialize(Log));
                }
                catch (Exception ex) { Debug.WriteLine("Focus log save: " + ex.Message); }
            }
        }

        /// <summary>Minutes focused on each of the days from..to (inclusive), counting the round in progress.</summary>
        public static int MinutesBetween(DateTime from, DateTime to, DateTime now)
        {
            int total;
            lock (Log)
                total = Log.Where(kv => DateTime.TryParse(kv.Key, out var d) && d.Date >= from.Date && d.Date <= to.Date).Sum(kv => kv.Value);
            if (Phase == FocusPhase.Focus && PhaseStart.Date >= from.Date && PhaseStart.Date <= to.Date)
                total += (int)Math.Round(Math.Max(0, ((Paused ? Length - _pausedLeft : now - PhaseStart)).TotalMinutes));
            return total;
        }

        public static int MinutesToday(DateTime now) => MinutesBetween(now, now, now);
        public static int MinutesThisWeek(DateTime now) => MinutesBetween(now.AddDays(-6), now, now);

        /// <summary>"1 hour 15 minutes" / "40 minutes".</summary>
        public static string Say(int minutes) =>
            minutes >= 60 ? Composer.Plural(minutes / 60, "hour", "hours") + (minutes % 60 > 0 ? " " + Composer.Plural(minutes % 60, "minute", "minutes") : "")
                          : Composer.Plural(minutes, "minute", "minutes");
    }
}
