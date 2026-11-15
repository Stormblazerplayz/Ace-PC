using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace Nimbus.Services
{
    /// <summary>One row on the Memory tab: every process with the same name, added up.</summary>
    public sealed record AppGroup(string Name, int Count, double CommitGb, double RamMb, bool HasWindow);

    /// <summary>Reads, trims and closes apps for the Memory and Games tabs. Call these off the UI thread.</summary>
    public static class Processes
    {
        private static readonly string Self = Process.GetCurrentProcess().ProcessName;

        /// <summary>Apps sorted by committed (private) memory, biggest first.</summary>
        public static List<AppGroup> TopGroups(int count)
        {
            var all = Process.GetProcesses();
            try
            {
                return all
                    .GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Key != "Idle" && g.Key != "System" && g.Key != "Memory Compression")
                    .Select(g => new AppGroup(
                        g.Key,
                        g.Count(),
                        g.Sum(p => Safe(() => p.PrivateMemorySize64)) / 1073741824.0,
                        g.Sum(p => Safe(() => p.WorkingSet64)) / 1048576.0,
                        g.Any(p => Safe(() => (long)p.MainWindowHandle) != 0)))
                    .OrderByDescending(a => a.CommitGb)
                    .Take(count)
                    .ToList();
            }
            finally
            {
                foreach (var p in all) p.Dispose();
            }
        }

        /// <summary>
        /// Asks Windows to move an app's unused memory out of RAM (the app keeps working).
        /// Returns how many MB of RAM it gave back.
        /// </summary>
        public static double Trim(string name)
        {
            long before = 0, after = 0;
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    before += p.WorkingSet64;
                    EmptyWorkingSet(p.Handle);
                    p.Refresh();
                    after += p.WorkingSet64;
                }
                catch { /* protected process: skip it */ }
                finally { p.Dispose(); }
            }
            return Math.Max(0, (before - after) / 1048576.0);
        }

        /// <summary>Trims the biggest apps except Nimbus itself. Returns MB given back.</summary>
        public static double TrimBackground(int howMany = 10)
        {
            double mb = 0;
            foreach (var g in TopGroups(howMany + 1))
                if (!g.Name.Equals(Self, StringComparison.OrdinalIgnoreCase)) mb += Trim(g.Name);
            return mb;
        }

        /// <summary>Politely closes an app's windows, like clicking its X. Returns how many it asked.</summary>
        public static int CloseWindows(string name)
        {
            int n = 0;
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { if (p.MainWindowHandle != IntPtr.Zero && p.CloseMainWindow()) n++; }
                catch { }
                finally { p.Dispose(); }
            }
            return n;
        }

        private static long Safe(Func<long> read)
        {
            try { return read(); } catch { return 0; }
        }

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr process);
    }
}
