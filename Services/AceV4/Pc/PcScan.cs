using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Nimbus.Services;

namespace Nimbus.AceV4.Pc
{
    public sealed record DriveSpace(string Name, string Label, double FreeGb, double TotalGb)
    {
        public double UsedPercent => TotalGb > 0 ? (1 - FreeGb / TotalGb) * 100 : 0;
        public bool Low => TotalGb > 0 && (FreeGb < 25 || FreeGb / TotalGb < 0.08);
    }

    public sealed record FoundFile(string Path, long Bytes, DateTime Modified)
    {
        public string Name => System.IO.Path.GetFileName(Path);
        public string SizeText => Sizes.Text(Bytes);
    }

    public static class Sizes
    {
        public static string Text(long bytes)
        {
            if (bytes >= 1L << 30) return (bytes / (double)(1L << 30)).ToString("0.0") + " GB";
            if (bytes >= 1L << 20) return (bytes / (double)(1L << 20)).ToString("0") + " MB";
            if (bytes >= 1L << 10) return (bytes / (double)(1L << 10)).ToString("0") + " KB";
            return bytes + " B";
        }
    }

    /// <summary>The checks Ace runs when it wakes up. Each one is quick and safe to run on a background thread.</summary>
    public static class PcScan
    {
        public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        /// <summary>The folders Ace looks through for your files (not Windows or Program Files).</summary>
        public static List<string> UserFolders()
        {
            var list = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Path.Combine(Home, "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            };
            string oneDrive = Environment.GetEnvironmentVariable("OneDrive") ?? "";
            if (oneDrive.Length > 0) list.Add(oneDrive);
            foreach (var extra in new[] { "source", "repos", "Projects", "dev", "3D Objects" })
                list.Add(Path.Combine(Home, extra));
            return list.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase)
                       // OneDrive often holds Desktop/Documents too; drop folders inside other folders in the list.
                       .Aggregate(new List<string>(), (acc, f) =>
                       {
                           if (!acc.Any(a => IsInside(f, a))) { acc.RemoveAll(a => IsInside(a, f)); acc.Add(f); }
                           return acc;
                       });
        }

        private static bool IsInside(string child, string parent) =>
            child.Length > parent.Length && child.StartsWith(parent.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

        public static List<DriveSpace> Drives()
        {
            var list = new List<DriveSpace>();
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady || d.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                    list.Add(new DriveSpace(d.Name.TrimEnd('\\'), d.VolumeLabel, d.AvailableFreeSpace / 1073741824.0, d.TotalSize / 1073741824.0));
                }
                catch { }
            }
            return list;
        }

        /// <summary>
        /// Walks your folders and calls visit for each file. Stops at maxFiles or when the time runs out,
        /// so a huge drive can't freeze a check. Skips node_modules, .git and other tool folders.
        /// </summary>
        public static void EachFile(IEnumerable<string> roots, Action<FileInfo> visit, int maxFiles = 250_000, double maxSeconds = 12, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            int n = 0;
            foreach (var root in roots)
            {
                var stack = new Stack<string>();
                stack.Push(root);
                while (stack.Count > 0)
                {
                    if (ct.IsCancellationRequested || n >= maxFiles || sw.Elapsed.TotalSeconds > maxSeconds) return;
                    string dir = stack.Pop();
                    try
                    {
                        var info = new DirectoryInfo(dir);
                        foreach (var sub in info.EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint }))
                        {
                            string name = sub.Name;
                            if (name.StartsWith('.') || name is "node_modules" or "bin" or "obj" or "__pycache__" or "Library" or "venv" or ".venv"
                                or "packages" or "AppData" or "$RECYCLE.BIN" or "cache" or "Cache") continue;
                            stack.Push(sub.FullName);
                        }
                        foreach (var f in info.EnumerateFiles("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System | FileAttributes.Offline }))
                        {
                            visit(f);
                            if (++n >= maxFiles) return;
                        }
                    }
                    catch { }
                }
            }
        }

        /// <summary>Your biggest files over minBytes, biggest first.</summary>
        public static List<FoundFile> LargeFiles(long minBytes, int take = 12, CancellationToken ct = default)
        {
            var found = new List<FoundFile>();
            EachFile(UserFolders(), f =>
            {
                try { if (f.Length >= minBytes) found.Add(new FoundFile(f.FullName, f.Length, f.LastWriteTime)); } catch { }
            }, ct: ct);
            return found.OrderByDescending(f => f.Bytes).Take(take).ToList();
        }

        private static readonly HashSet<string> DocExt = new(StringComparer.OrdinalIgnoreCase)
        { ".doc", ".docx", ".pdf", ".txt", ".md", ".rtf", ".odt", ".xls", ".xlsx", ".csv", ".ppt", ".pptx", ".odp", ".pages", ".key" };

        /// <summary>Documents changed in the last few days in Desktop, Documents and Downloads, newest first.</summary>
        public static List<FoundFile> RecentDocuments(TimeSpan within, int take = 30)
        {
            var since = DateTime.Now - within;
            var found = new List<FoundFile>();
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Path.Combine(Home, "Downloads"),
            }.Where(Directory.Exists);
            EachFile(roots, f =>
            {
                try
                {
                    if (DocExt.Contains(f.Extension) && f.LastWriteTime >= since && !f.Name.StartsWith("~$"))
                        found.Add(new FoundFile(f.FullName, f.Length, f.LastWriteTime));
                }
                catch { }
            }, 60_000, 5);
            return found.OrderByDescending(f => f.Modified).Take(take).ToList();
        }

        /// <summary>Downloads folder: how many files and how much space.</summary>
        public static (int Files, long Bytes) DownloadsSize()
        {
            int n = 0;
            long bytes = 0;
            string dl = Path.Combine(Home, "Downloads");
            if (!Directory.Exists(dl)) return (0, 0);
            EachFile(new[] { dl }, f => { n++; try { bytes += f.Length; } catch { } }, 50_000, 4);
            return (n, bytes);
        }

        /// <summary>Recycle bin size and item count.</summary>
        public static (long Bytes, long Items) RecycleBin()
        {
            var info = new Win32.SHQUERYRBINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.SHQUERYRBINFO>() };
            try { if (Win32.SHQueryRecycleBin(null, ref info) == 0) return (info.i64Size, info.i64NumItems); } catch { }
            return (0, 0);
        }

        /// <summary>Empties the recycle bin with no Windows pop-up (Ace already asked).</summary>
        public static bool EmptyRecycleBin()
        {
            const uint NoConfirm = 0x1, NoProgress = 0x2, NoSound = 0x4;
            try { return Win32.SHEmptyRecycleBin(IntPtr.Zero, null, NoConfirm | NoProgress | NoSound) == 0; } catch { return false; }
        }

        /// <summary>What the live stats say right now (CPU, RAM, GPU, temps, disk).</summary>
        public static SystemStats Stats => Hub.Stats;

        /// <summary>The apps hogging the most memory, for "what's using my RAM".</summary>
        public static List<AppGroup> TopApps(int n = 5)
        {
            try { return Processes.TopGroups(n); } catch { return new List<AppGroup>(); }
        }

        /// <summary>Apps using the most CPU over a short sample.</summary>
        public static List<(string Name, double Percent)> TopCpu(int n = 5, int sampleMs = 700)
        {
            var first = new Dictionary<int, (string Name, TimeSpan Cpu)>();
            foreach (var p in Process.GetProcesses())
            {
                try { first[p.Id] = (p.ProcessName, p.TotalProcessorTime); } catch { }
                finally { p.Dispose(); }
            }
            Thread.Sleep(sampleMs);
            var usage = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (first.TryGetValue(p.Id, out var a) && p.ProcessName != "Idle")
                    {
                        double pct = (p.TotalProcessorTime - a.Cpu).TotalMilliseconds / sampleMs / Environment.ProcessorCount * 100;
                        usage[a.Name] = usage.TryGetValue(a.Name, out double old) ? old + pct : pct;
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }
            return usage.OrderByDescending(kv => kv.Value).Take(n).Select(kv => (kv.Key, kv.Value)).ToList();
        }

        public static (bool HasBattery, int Percent, bool Charging) Battery()
        {
            try
            {
                if (Win32.GetSystemPowerStatus(out var s) && s.BatteryFlag != 128 && s.BatteryLifePercent <= 100)
                    return (true, s.BatteryLifePercent, s.ACLineStatus == 1);
            }
            catch { }
            return (false, 0, false);
        }
    }
}
