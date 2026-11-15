using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nimbus.AceV4.Brain;

namespace Nimbus.AceV4.Pc
{
    /// <summary>One look at how the PC was doing (taken every 10 minutes while you use it).</summary>
    public sealed class HealthSample
    {
        [JsonPropertyName("t")] public DateTime Time { get; set; }
        [JsonPropertyName("cpu")] public double Cpu { get; set; }
        [JsonPropertyName("mem")] public double Mem { get; set; }
        [JsonPropertyName("ct")] public double? CpuTemp { get; set; }
        [JsonPropertyName("gt")] public double? GpuTemp { get; set; }
        /// <summary>Free space on the fullest drive, in GB and as a percent.</summary>
        [JsonPropertyName("fg")] public double FreeGb { get; set; }
        [JsonPropertyName("fp")] public double FreePct { get; set; }
        /// <summary>Hours since Windows last started.</summary>
        [JsonPropertyName("up")] public double UptimeHours { get; set; }
        [JsonPropertyName("apps")] public int Apps { get; set; }
    }

    public sealed record ReportLine(string Label, string Grade, string Detail);

    public sealed class HealthReport
    {
        public string Grade = "";
        public int Score;
        public List<ReportLine> Lines = new();
        public List<string> Tips = new();
        public int Samples;
        public double Days;
        public int FocusMinutes;
        public string? LastWeekGrade;
    }

    /// <summary>
    /// Keeps a few weeks of PC health samples (health-log.json) and turns the last 7 days into a report card
    /// with an A to F grade for speed, memory, temperature, storage and restarts.
    /// Everything comes from your own PC; nothing is made up when there's too little data.
    /// </summary>
    public static class HealthLog
    {
        private static readonly object Gate = new();
        private static List<HealthSample>? _samples;
        private static DateTime _lastSave;
        private static string FilePath => Path.Combine(AceV4Config.Folder, "health-log.json");

        /// <summary>Set by tests so nothing is written to disk.</summary>
        public static bool DontSave;

        private static List<HealthSample> Samples
        {
            get
            {
                if (_samples != null) return _samples;
                try
                {
                    if (File.Exists(FilePath)) _samples = JsonSerializer.Deserialize<List<HealthSample>>(File.ReadAllText(FilePath));
                }
                catch (Exception ex) { Debug.WriteLine("Health log: " + ex.Message); }
                return _samples ??= new List<HealthSample>();
            }
        }

        public static int Count { get { lock (Gate) return Samples.Count; } }

        public static DateTime LastSample { get { lock (Gate) return Samples.Count > 0 ? Samples[^1].Time : DateTime.MinValue; } }

        public static void Add(HealthSample s)
        {
            lock (Gate)
            {
                Samples.Add(s);
                Samples.RemoveAll(x => x.Time < s.Time.AddDays(-35));
                if (DontSave || DateTime.Now - _lastSave < TimeSpan.FromMinutes(30) && Samples.Count > 1) return;
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
                    File.WriteAllText(FilePath, JsonSerializer.Serialize(Samples));
                    _lastSave = DateTime.Now;
                }
                catch (Exception ex) { Debug.WriteLine("Health log save: " + ex.Message); }
            }
        }

        /// <summary>For tests: start from these samples.</summary>
        public static void Replace(IEnumerable<HealthSample> samples) { lock (Gate) _samples = samples.OrderBy(s => s.Time).ToList(); }

        /// <summary>The report card for the 7 days before now, or null if there are fewer than 6 samples (about an hour).</summary>
        public static HealthReport? Report(DateTime now)
        {
            List<HealthSample> week, before;
            lock (Gate)
            {
                week = Samples.Where(s => s.Time > now.AddDays(-7) && s.Time <= now).ToList();
                before = Samples.Where(s => s.Time > now.AddDays(-14) && s.Time <= now.AddDays(-7)).ToList();
            }
            if (week.Count < 6) return null;
            var r = Build(week, now);
            if (before.Count >= 6) r.LastWeekGrade = Build(before, now.AddDays(-7)).Grade;
            return r;
        }

        private static HealthReport Build(List<HealthSample> week, DateTime now)
        {
            var r = new HealthReport { Samples = week.Count, Days = Math.Max(0.1, (week[^1].Time - week[0].Time).TotalDays) };
            var scores = new List<int>();

            // Speed: how busy the CPU usually was, and how often it was maxed out.
            double avgCpu = week.Average(s => s.Cpu);
            double busy = week.Count(s => s.Cpu > 85) / (double)week.Count;
            int cpuScore = Clamp100(100 - Math.Max(0, avgCpu - 30) * 1.2 - busy * 160);
            scores.Add(cpuScore);
            r.Lines.Add(new ReportLine("Speed", Letter(cpuScore), $"CPU averaged {avgCpu:0}%{(busy > 0.02 ? $", maxed out {busy * 100:0}% of the time" : "")}."));
            if (busy > 0.1) r.Tips.Add("Your CPU was maxed out a lot. Say \"what's using my CPU\" to find the app doing it.");

            // Memory.
            double avgMem = week.Average(s => s.Mem);
            double full = week.Count(s => s.Mem > 88) / (double)week.Count;
            int memScore = Clamp100(100 - Math.Max(0, avgMem - 55) * 1.3 - full * 150);
            scores.Add(memScore);
            r.Lines.Add(new ReportLine("Memory", Letter(memScore), $"RAM averaged {avgMem:0}%{(full > 0.02 ? $", nearly full {full * 100:0}% of the time" : "")}."));
            if (full > 0.1) r.Tips.Add("Memory got full often. Closing browser tabs and apps you aren't using helps a lot.");

            // Temperature (only if Nimbus can read the sensors).
            var temps = week.Where(s => s.CpuTemp.HasValue || s.GpuTemp.HasValue).ToList();
            if (temps.Count >= 3)
            {
                double max = temps.Max(s => Math.Max(s.CpuTemp ?? 0, s.GpuTemp ?? 0));
                double avg = temps.Average(s => Math.Max(s.CpuTemp ?? 0, s.GpuTemp ?? 0));
                double hot = temps.Count(s => (s.CpuTemp ?? 0) >= 90 || (s.GpuTemp ?? 0) >= 88) / (double)temps.Count;
                int tempScore = Clamp100(100 - Math.Max(0, avg - 60) * 1.5 - hot * 200);
                scores.Add(tempScore);
                r.Lines.Add(new ReportLine("Temperature", Letter(tempScore), $"Averaged {avg:0}°C, hottest {max:0}°C."));
                if (hot > 0.05) r.Tips.Add("It ran hot sometimes. Make sure the vents aren't blocked and the fans are clean.");
            }

            // Storage: how much is free now, and how fast it's filling up.
            var last = week[^1];
            double usedThisWeek = week[0].FreeGb - last.FreeGb;
            int diskScore = Clamp100(last.FreePct >= 25 ? 100 : last.FreePct >= 15 ? 85 : last.FreePct >= 8 ? 65 : last.FreePct >= 4 ? 45 : 20);
            if (usedThisWeek > 20) diskScore = Clamp100(diskScore - 10);
            scores.Add(diskScore);
            r.Lines.Add(new ReportLine("Storage", Letter(diskScore), $"{last.FreeGb:0} GB free ({last.FreePct:0}%)" +
                (Math.Abs(usedThisWeek) >= 1 ? usedThisWeek > 0 ? $", {usedThisWeek:0} GB used up this week." : $", {-usedThisWeek:0} GB freed this week." : ".")));
            if (last.FreePct < 15) r.Tips.Add("Storage is getting low. Say \"free up space\" and I'll find big files.");

            // Restarts: Windows likes a restart every few days (updates finish, memory gets cleaned up).
            double longest = week.Max(s => s.UptimeHours) / 24;
            int restartScore = Clamp100(longest <= 3 ? 100 : longest <= 7 ? 85 : longest <= 14 ? 65 : 45);
            scores.Add(restartScore);
            r.Lines.Add(new ReportLine("Restarts", Letter(restartScore), longest < 1 ? "Restarted at least once a day." : $"Longest it went without a restart: {longest:0.#} days."));
            if (longest > 7) r.Tips.Add("Your PC went over a week without a restart. Restarting lets updates finish and clears out memory.");

            r.Score = (int)Math.Round(scores.Average());
            r.Grade = Letter(r.Score);
            r.FocusMinutes = FocusTimer.MinutesBetween(now.AddDays(-6), now, now);
            if (r.Tips.Count == 0) r.Tips.Add("Nothing to fix. Keep it up!");
            return r;
        }

        private static int Clamp100(double v) => (int)Math.Round(Math.Clamp(v, 0, 100));

        public static string Letter(int score) => score switch
        {
            >= 93 => "A",
            >= 90 => "A-",
            >= 87 => "B+",
            >= 83 => "B",
            >= 80 => "B-",
            >= 77 => "C+",
            >= 73 => "C",
            >= 70 => "C-",
            >= 60 => "D",
            _ => "F",
        };

        /// <summary>How long the log covers, in words ("3 days", "5 hours").</summary>
        public static string Span(double days) => days >= 1 ? Composer.Plural((int)Math.Round(days), "day", "days") : Composer.Plural(Math.Max(1, (int)Math.Round(days * 24)), "hour", "hours");
    }
}
