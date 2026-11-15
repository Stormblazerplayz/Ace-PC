using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace Nimbus.Updater
{
    /// <summary>What the updater was asked to do (from its command line).</summary>
    public sealed class UpdateJob
    {
        /// <summary>Nimbus's process id. The updater waits for it to close before touching any file.</summary>
        public int Pid;
        /// <summary>The new version's files (unzipped and checked by Nimbus).</summary>
        public string Source = "";
        /// <summary>Where Nimbus is installed (the files that get replaced).</summary>
        public string Target = "";
        /// <summary>Where the old version is copied first, so it can be put back.</summary>
        public string Backup = "";
        /// <summary>The program to start afterwards, like NimbusV3Polish.exe.</summary>
        public string Exe = "";
        /// <summary>The file the new version writes once it started fine. Empty skips the check.</summary>
        public string OkFile = "";
        public string Log = "";
        public TimeSpan WaitForExit = TimeSpan.FromSeconds(90);
        public TimeSpan WaitForOk = TimeSpan.FromSeconds(60);

        public static UpdateJob? Parse(string[] args)
        {
            var job = new UpdateJob();
            for (int i = 0; i + 1 < args.Length; i += 2)
            {
                string v = args[i + 1];
                switch (args[i].ToLowerInvariant())
                {
                    case "--pid": if (!int.TryParse(v, out job.Pid)) return null; break;
                    case "--source": job.Source = v; break;
                    case "--target": job.Target = v; break;
                    case "--backup": job.Backup = v; break;
                    case "--exe": job.Exe = v; break;
                    case "--ok": job.OkFile = v; break;
                    case "--log": job.Log = v; break;
                    case "--wait-ok": if (int.TryParse(v, out int s)) job.WaitForOk = TimeSpan.FromSeconds(s); break;
                    default: return null;
                }
            }
            if (job.Source.Length == 0 || job.Target.Length == 0 || job.Backup.Length == 0 || job.Exe.Length == 0) return null;
            // The program to start must be a plain file name inside the install folder.
            if (job.Exe != Path.GetFileName(job.Exe) || job.Exe.Contains('/') || job.Exe.Contains('\\') || job.Exe.Contains("..")) return null;
            return job;
        }
    }

    /// <summary>
    /// Swaps Nimbus's files for a new version: waits for Nimbus to close, backs up the old files, copies the new
    /// ones in, starts Nimbus again and waits for it to say it started fine. If anything goes wrong it puts the
    /// old files back and starts the old version. Files that aren't in the new version are never deleted.
    /// </summary>
    public static class UpdaterCore
    {
        public enum Result { Updated, RolledBack, NothingChanged }

        /// <summary>Runs the update. start(path) starts a program; isRunning(pid) says if a process is still open.</summary>
        public static Result Run(UpdateJob job, Func<string, Process?> start, Func<int, bool> isRunning, Action<string>? log = null)
        {
            void Log(string s)
            {
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {s}";
                log?.Invoke(line);
                if (job.Log.Length > 0) try { File.AppendAllText(job.Log, line + Environment.NewLine); } catch { }
            }

            string source = Path.GetFullPath(job.Source), target = Path.GetFullPath(job.Target), backup = Path.GetFullPath(job.Backup);
            Log($"Update from {source} into {target}.");
            if (!File.Exists(Path.Combine(source, job.Exe)))
            {
                Log($"The new version has no {job.Exe}. Nothing changed.");
                StartOld();
                return Result.NothingChanged;
            }
            if (Inside(backup, target) || Inside(target, source) || Inside(source, target) && source != target)
            {
                Log("The folders overlap. Nothing changed.");
                StartOld();
                return Result.NothingChanged;
            }

            // 1. Wait for Nimbus to close.
            var until = DateTime.Now + job.WaitForExit;
            while (job.Pid > 0 && isRunning(job.Pid))
            {
                if (DateTime.Now > until) { Log("Nimbus didn't close, so nothing changed."); return Result.NothingChanged; }
                Thread.Sleep(250);
            }
            Thread.Sleep(500);   // let Windows let go of the files

            // 2. Back up the old version.
            try
            {
                if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
                CopyTree(target, backup, null);
                Log($"Backed up the old version to {backup}.");
            }
            catch (Exception ex)
            {
                Log("Couldn't back up the old version: " + ex.Message + ". Nothing changed.");
                StartOld();
                return Result.NothingChanged;
            }

            // 3. Copy the new files in.
            var written = new List<(string Path, bool Existed)>();
            try
            {
                CopyTree(source, target, written);
                Log($"Copied {written.Count} files.");
            }
            catch (Exception ex)
            {
                Log("Copying failed: " + ex.Message + ". Rolling back.");
                Rollback();
                return Result.RolledBack;
            }

            // 4. Start the new version and wait for it to say it's fine.
            try { if (job.OkFile.Length > 0 && File.Exists(job.OkFile)) File.Delete(job.OkFile); } catch { }
            Process? p;
            try { p = start(Path.Combine(target, job.Exe)); }
            catch (Exception ex) { Log("Couldn't start the new version: " + ex.Message + ". Rolling back."); Rollback(); return Result.RolledBack; }
            if (p == null) { Log("The new version didn't start. Rolling back."); Rollback(); return Result.RolledBack; }
            if (job.OkFile.Length > 0)
            {
                var okUntil = DateTime.Now + job.WaitForOk;
                while (!File.Exists(job.OkFile))
                {
                    bool exited;
                    try { exited = p.HasExited; } catch { exited = false; }
                    if (exited)
                    {
                        Log("The new version closed right after starting. Rolling back.");
                        Rollback();
                        return Result.RolledBack;
                    }
                    if (DateTime.Now > okUntil) { Log("The new version is running but didn't check in. Keeping it."); break; }
                    Thread.Sleep(300);
                }
            }
            Log("Update done.");
            return Result.Updated;

            void Rollback()
            {
                int restored = 0, removed = 0;
                foreach (var (path, existed) in written.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (existed)
                        {
                            string from = Path.Combine(backup, Path.GetRelativePath(target, path));
                            if (File.Exists(from)) { Retry(() => File.Copy(from, path, overwrite: true)); restored++; }
                        }
                        else if (File.Exists(path)) { Retry(() => File.Delete(path)); removed++; }
                    }
                    catch (Exception ex) { Log("Rollback trouble with " + path + ": " + ex.Message); }
                }
                Log($"Rolled back: put back {restored} files, removed {removed} new ones.");
                StartOld();
            }

            void StartOld()
            {
                try
                {
                    string old = Path.Combine(target, job.Exe);
                    if (File.Exists(old)) start(old);
                }
                catch (Exception ex) { Log("Couldn't start Nimbus again: " + ex.Message); }
            }
        }

        /// <summary>Copies every file under from into to (overwriting), noting each file written and whether it was there before.</summary>
        public static void CopyTree(string from, string to, List<(string Path, bool Existed)>? written)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(from, file);
                string dest = Path.Combine(to, rel);
                if (!Inside(dest, to)) throw new IOException("A file path points outside the folder: " + rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                bool existed = File.Exists(dest);
                Retry(() => File.Copy(file, dest, overwrite: true));
                written?.Add((dest, existed));
            }
        }

        /// <summary>True when path is the folder or inside it.</summary>
        public static bool Inside(string path, string folder)
        {
            string p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string f = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return p.Equals(f, cmp) || p.StartsWith(f + Path.DirectorySeparatorChar, cmp);
        }

        /// <summary>Files can stay locked for a moment after a program closes; try a few times.</summary>
        private static void Retry(Action a)
        {
            for (int i = 0; ; i++)
            {
                try { a(); return; }
                catch (IOException) when (i < 20) { Thread.Sleep(500); }
                catch (UnauthorizedAccessException) when (i < 20) { Thread.Sleep(500); }
            }
        }
    }
}
