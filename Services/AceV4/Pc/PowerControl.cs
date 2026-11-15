using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Nimbus.AceV4.Pc
{
    /// <summary>Shut down, restart, sleep and lock. Ace announces these with a 10-second countdown you can cancel.</summary>
    public static class PowerControl
    {
        public static bool Shutdown() => Run("shutdown", "/s /t 0");

        public static bool Restart() => Run("shutdown", "/r /t 0");

        /// <summary>Stops a shutdown Windows already scheduled (for example one started from somewhere else).</summary>
        public static bool AbortScheduled() => Run("shutdown", "/a");

        public static bool Sleep()
        {
            try { return Win32.SetSuspendState(false, false, false); } catch { return false; }
        }

        public static bool Lock()
        {
            try { return Win32.LockWorkStation(); } catch { return false; }
        }

        public static bool SignOut() => Run("shutdown", "/l");

        private static bool Run(string exe, string args)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false });
                return p != null;
            }
            catch { return false; }
        }
    }
}
