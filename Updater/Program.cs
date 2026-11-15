using System;
using System.Diagnostics;
using System.IO;

namespace Nimbus.Updater
{
    public static class Program
    {
        /// <summary>
        /// NimbusUpdater --pid 1234 --source "new files" --target "install folder" --backup "backup folder"
        ///               --exe Ace.exe [--old-exe NimbusV3Polish.exe] --ok "started.ok" --log "updater.log"
        /// </summary>
        public static int Main(string[] args)
        {
            var job = UpdateJob.Parse(args);
            if (job == null)
            {
                Console.Error.WriteLine("NimbusUpdater is started by Ace when it updates itself. You don't need to run it.");
                return 1;
            }
            var result = UpdaterCore.Run(job, Start, IsRunning, Console.WriteLine);
            return result == UpdaterCore.Result.Updated ? 0 : 2;
        }

        private static Process? Start(string exe) =>
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! });

        private static bool IsRunning(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                return !p.HasExited;
            }
            catch (ArgumentException) { return false; }   // no process with that id
            catch (InvalidOperationException) { return false; }
        }
    }
}
