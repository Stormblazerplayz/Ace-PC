using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Nimbus.AceV4.Pc
{
    /// <summary>A release on GitHub with a newer Ace.</summary>
    public sealed record ReleaseInfo(Version Version, string Tag, string Name, string ZipUrl, string ShaUrl, string Notes);

    /// <summary>
    /// Keeps Ace up to date from GitHub Releases. It checks the newest release of <see cref="Repo"/>, downloads its
    /// zip, checks it against the release's SHA256 file, unzips it, then hands over to NimbusUpdater, which swaps the
    /// files while Nimbus is closed, keeps a backup and rolls back if the new version doesn't start.
    /// </summary>
    public static class AutoUpdater
    {
        /// <summary>The GitHub repo that has Ace's releases. Change it here if the repo moves.</summary>
        public const string Repo = "Stormblazerplayz/Ace-PC";
        /// <summary>The release file with the app in it (the GitHub workflow makes it), and its SHA256 file.</summary>
        public const string AssetName = "NimbusAce-win-x64.zip";
        public const string ShaName = AssetName + ".sha256";
        public const string UpdaterExe = "NimbusUpdater.exe";
        public static readonly TimeSpan Every = TimeSpan.FromHours(6);

        public static string Folder => Path.Combine(AceV4Config.Folder, "updates");
        private static string StatePath => Path.Combine(Folder, "update-state.json");
        public static string LogPath => Path.Combine(Folder, "updater.log");

        /// <summary>Raised (on any thread) when Status changes.</summary>
        public static event Action? Changed;
        /// <summary>What the updater is doing or found last ("You're up to date").</summary>
        public static string Status { get; private set; } = "";
        public static bool Busy => _busy == 1;
        /// <summary>A newer version is downloaded and checked, waiting for a good moment to install.</summary>
        public static bool ReadyToInstall { get; private set; }
        private static int _busy;

        private static readonly HttpClient Http = MakeClient();
        private static HttpClient MakeClient()
        {
            var h = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("Nimbus-Ace-Updater/1.0");
            h.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return h;
        }

        // ===================================================================== Version

        /// <summary>This copy's version (the GitHub workflow sets it from the tag, like v4.0.1 → 4.0.1).</summary>
        public static Version Current
        {
            get
            {
                var asm = Assembly.GetEntryAssembly() ?? typeof(AutoUpdater).Assembly;
                string? info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                return ParseVersion(info ?? "") ?? Norm(asm.GetName().Version ?? new Version(4, 0, 0));
            }
        }

        public static string CurrentText => Text(Current);

        /// <summary>
        /// Versions keep a 4th number so betas sort right: a beta is 4.1.0.N (N = its beta number) and the real
        /// 4.1.0 is 4.1.0.<see cref="FinalRevision"/>, so 4.1.0-beta.1 &lt; 4.1.0-beta.2 &lt; 4.1.0 &lt; 4.1.1-beta.1.
        /// </summary>
        public const int FinalRevision = int.MaxValue;
        public static bool IsBeta(Version v) => v.Revision >= 0 && v.Revision < FinalRevision;
        public static string Text(Version v) =>
            $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}" + (IsBeta(v) ? "-beta" + (v.Revision > 0 ? "." + v.Revision : "") : "");

        /// <summary>
        /// "v4.0.1" → 4.0.1, "4.1" → 4.1.0, "v4.2.0-beta.3" → the 3rd beta of 4.2.0, "4.0.0+8f1c2a" → 4.0.0.
        /// Null if it isn't a version.
        /// </summary>
        public static Version? ParseVersion(string tag)
        {
            var m = Regex.Match(tag.Trim(), @"^[vV]?(\d+)\.(\d+)(?:\.(\d+))?(?:\.\d+)?(?:-([0-9A-Za-z.-]+))?(?:\+.*)?$");
            if (!m.Success) return null;
            try
            {
                int rev = FinalRevision;
                if (m.Groups[4].Success)
                {
                    var n = Regex.Match(m.Groups[4].Value, @"(\d+)(?!.*\d)");
                    rev = n.Success && int.TryParse(n.Groups[1].Value, out int b) ? Math.Min(b, FinalRevision - 1) : 0;
                }
                return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
                                   m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0, rev);
            }
            catch { return null; }
        }

        private static Version Norm(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), FinalRevision);

        // ===================================================================== Saved state

        private sealed class SavedState
        {
            public DateTime LastCheck { get; set; }
            public string LastRunVersion { get; set; } = "";
            public string UpdatedFrom { get; set; } = "";
            public DateTime UpdatedAt { get; set; }
            public string WhatsNewShownFor { get; set; } = "";
        }

        private static SavedState? _state;
        private static SavedState State
        {
            get
            {
                if (_state != null) return _state;
                try { if (File.Exists(StatePath)) _state = JsonSerializer.Deserialize<SavedState>(File.ReadAllText(StatePath)); } catch { }
                return _state ??= new SavedState();
            }
        }

        private static void SaveState()
        {
            try { Directory.CreateDirectory(Folder); File.WriteAllText(StatePath, JsonSerializer.Serialize(State)); } catch { }
        }

        public static DateTime LastCheck => State.LastCheck;
        /// <summary>The version this one replaced, if Ace updated in the last day ("4.0.0"), or "".</summary>
        public static string JustUpdatedFrom => State.UpdatedFrom.Length > 0 && DateTime.Now - State.UpdatedAt < TimeSpan.FromDays(1) ? State.UpdatedFrom : "";

        /// <summary>
        /// The version Ace updated from, while its "What's new" hasn't popped up yet (for a week after an update), or "".
        /// </summary>
        public static string PendingWhatsNewFrom =>
            State.UpdatedFrom.Length > 0 && State.WhatsNewShownFor != CurrentText && DateTime.Now - State.UpdatedAt < TimeSpan.FromDays(7) ? State.UpdatedFrom : "";

        /// <summary>"What's new" popped up for this version, so it won't again.</summary>
        public static void MarkWhatsNewShown() { State.WhatsNewShownFor = CurrentText; SaveState(); }

        public static bool CheckDue => DateTime.Now - State.LastCheck >= Every;

        /// <summary>
        /// True when Nimbus runs from a build folder (bin\Debug or bin\Release next to the .csproj), like with
        /// "dotnet run". Swapping those files wouldn't stick (the next build puts the old ones back), so updates
        /// are only installed into a copy made from a release zip.
        /// </summary>
        public static bool RunningFromSource => FromSource(AppContext.BaseDirectory);

        public static bool FromSource(string baseDir)
        {
            try
            {
                var dir = new DirectoryInfo(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                bool sawBin = false;
                for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
                {
                    if (dir.Name.Equals("bin", StringComparison.OrdinalIgnoreCase)) sawBin = true;
                    else if (sawBin && dir.GetFiles("*.csproj").Length > 0) return true;
                }
            }
            catch { }
            return false;
        }

        private static void SetStatus(string s) { Status = s; Changed?.Invoke(); }

        /// <summary>
        /// Call once when Nimbus has started: tells the updater (if it's waiting) that this version works, and
        /// notices when the version changed since last time.
        /// </summary>
        public static void MarkStartedOk()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(OkFile(Current), DateTime.Now.ToString("o"));
                string now = CurrentText;
                if (State.LastRunVersion.Length > 0 && State.LastRunVersion != now && ParseVersion(State.LastRunVersion) is { } old && old < Current)
                {
                    State.UpdatedFrom = State.LastRunVersion;
                    State.UpdatedAt = DateTime.Now;
                }
                State.LastRunVersion = now;
                SaveState();
            }
            catch { }
        }

        public static string OkFile(Version v) => Path.Combine(Folder, $"started-{Text(v)}.ok");

        // ===================================================================== GitHub

        /// <summary>
        /// The newest release with Ace's zip in it, or null (with why in Status). With beta on, pre-releases
        /// count too (GitHub's "latest" skips them, so it reads the list of releases instead).
        /// </summary>
        public static async Task<ReleaseInfo?> LatestAsync(CancellationToken ct, bool beta = false)
        {
            if (Web.WebClient.Disabled) { SetStatus("Updates are off while the internet is off."); return null; }
            HttpResponseMessage res;
            string url = beta ? $"https://api.github.com/repos/{Repo}/releases?per_page=30" : $"https://api.github.com/repos/{Repo}/releases/latest";
            try { res = await Http.GetAsync(url, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                SetStatus("Couldn't reach GitHub. I'll try again later.");
                return null;
            }
            using (res)
            {
                if ((int)res.StatusCode == 404) { SetStatus("There's no release on GitHub yet."); return null; }
                if ((int)res.StatusCode == 403 || (int)res.StatusCode == 429) { SetStatus("GitHub says to slow down. I'll try again later."); return null; }
                if (!res.IsSuccessStatusCode) { SetStatus($"GitHub answered with an error ({(int)res.StatusCode}). I'll try again later."); return null; }
                string json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var rel = beta ? NewestRelease(json, includeBetas: true) : ParseRelease(json);
                if (rel == null) SetStatus(beta ? $"There's no release with {AssetName} and {ShaName} in it yet." : $"The newest release doesn't have {AssetName} and {ShaName} in it yet.");
                return rel;
            }
        }

        /// <summary>
        /// Reads GitHub's release JSON. Null if it has no version tag, no zip or no SHA256 file, or if it's a
        /// pre-release (unless includeBetas).
        /// </summary>
        public static ReleaseInfo? ParseRelease(string json, bool includeBetas = false)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                return ReadRelease(doc.RootElement, includeBetas);
            }
            catch { return null; }
        }

        /// <summary>The newest usable release in GitHub's list of releases (betas too if includeBetas), or null.</summary>
        public static ReleaseInfo? NewestRelease(string listJson, bool includeBetas)
        {
            try
            {
                using var doc = JsonDocument.Parse(listJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
                ReleaseInfo? best = null;
                foreach (var el in doc.RootElement.EnumerateArray())
                    if (ReadRelease(el, includeBetas) is { } r && (best == null || r.Version > best.Version)) best = r;
                return best;
            }
            catch { return null; }
        }

        private static ReleaseInfo? ReadRelease(JsonElement root, bool includeBetas)
        {
            try
            {
                if (root.ValueKind != JsonValueKind.Object) return null;
                if (root.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) return null;
                bool pre = root.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True;
                if (pre && !includeBetas) return null;
                string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
                var version = ParseVersion(tag);
                if (version == null) return null;
                // A beta tag on a release that isn't marked as a pre-release still only goes to beta testers.
                if (IsBeta(version) && !includeBetas) return null;
                string zip = "", sha = "";
                if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                    foreach (var a in assets.EnumerateArray())
                    {
                        string name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        string url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                        if (!SafeDownloadUrl(url)) continue;
                        if (name.Equals(AssetName, StringComparison.OrdinalIgnoreCase)) zip = url;
                        else if (name.Equals(ShaName, StringComparison.OrdinalIgnoreCase)) sha = url;
                    }
                if (zip.Length == 0 || sha.Length == 0) return null;
                string title = root.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "";
                string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
                return new ReleaseInfo(version, tag, title.Length > 0 ? title : tag, zip, sha, notes);
            }
            catch { return null; }
        }

        /// <summary>Only downloads from GitHub itself (release files live on github.com and redirect to its storage).</summary>
        public static bool SafeDownloadUrl(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps &&
            (u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

        // ===================================================================== Download, check and unzip

        /// <summary>
        /// Checks the zip against the SHA256 text ("hash" or "hash  file.zip"), unzips it into stageDir and returns
        /// the folder that holds the app (the one with Ace.exe). Null with the reason in error if not.
        /// </summary>
        public static string? VerifyAndExtract(string zipPath, string shaText, string stageDir, out string error)
        {
            error = "";
            var want = Regex.Match(shaText ?? "", @"\b[0-9a-fA-F]{64}\b");
            if (!want.Success) { error = "The SHA256 file is empty or broken."; return null; }
            string got;
            using (var fs = File.OpenRead(zipPath)) got = Convert.ToHexString(SHA256.HashData(fs));
            if (!got.Equals(want.Value, StringComparison.OrdinalIgnoreCase)) { error = "The download didn't match its SHA256, so I didn't install it."; return null; }
            try
            {
                if (Directory.Exists(stageDir)) Directory.Delete(stageDir, recursive: true);
                Directory.CreateDirectory(stageDir);
                // ExtractToDirectory refuses entries that point outside the folder.
                ZipFile.ExtractToDirectory(zipPath, stageDir);
            }
            catch (Exception ex) { error = "Couldn't unzip the update: " + ex.Message; return null; }
            string? app = FindApp(stageDir);
            if (app == null) error = "The update zip doesn't have Ace in it.";
            return app;
        }

        /// <summary>Names the app's program has had, newest first. Older releases were called NimbusV3Polish.</summary>
        public static readonly string[] AppNames = { "Ace", "NimbusV3Polish" };

        private static string? FindApp(string dir)
        {
            bool Has(string d) => AppExeIn(d) != null;
            if (Has(dir)) return dir;
            foreach (var sub in Directory.GetDirectories(dir))
            {
                if (Has(sub)) return sub;
                foreach (var sub2 in Directory.GetDirectories(sub)) if (Has(sub2)) return sub2;
            }
            return null;
        }

        /// <summary>The program in a folder ("Ace.exe"), found by its .exe (or the .dll of a non-single-file build). Null if none.</summary>
        public static string? AppExeIn(string dir)
        {
            foreach (var name in AppNames)
                if (File.Exists(Path.Combine(dir, name + ".exe")) || File.Exists(Path.Combine(dir, name + ".dll"))) return name + ".exe";
            return null;
        }

        /// <summary>The running app's own name ("Ace"), so the files to swap and start are known.</summary>
        public static string MainName()
        {
            var asm = Assembly.GetEntryAssembly();
            string name = asm?.GetName().Name ?? "";
            return name.Length == 0 || name.StartsWith("AceTest", StringComparison.OrdinalIgnoreCase) ? "Ace" : name;
        }

        private static async Task DownloadAsync(string url, string path, CancellationToken ct, Action<double>? progress = null)
        {
            using var res = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();
            if (res.RequestMessage?.RequestUri is { } final && !SafeDownloadUrl(final.ToString()))
                throw new HttpRequestException("The download went somewhere other than GitHub.");
            long total = res.Content.Headers.ContentLength ?? -1;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var src = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = File.Create(path);
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                done += n;
                if (total > 0) progress?.Invoke((double)done / total);
                if (done > 1_000_000_000) throw new IOException("The download is way too big.");
            }
        }

        // ===================================================================== The whole thing

        /// <summary>What a check came to.</summary>
        public enum Outcome { UpToDate, Failed, Restarting, Busy }

        /// <summary>
        /// Checks GitHub. If there's a newer Ace it downloads and checks it, then (when canRestart says it's a good
        /// time) runs beforeRestart (the eyes say "Updating Ace…"), starts NimbusUpdater and returns Restarting:
        /// the caller then closes Nimbus. Anything that goes wrong leaves this version exactly as it is.
        /// </summary>
        public static async Task<(Outcome Outcome, string Message)> CheckAndInstallAsync(Func<bool> canRestart, Func<string, Task> beforeRestart, CancellationToken ct, bool beta = false)
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return (Outcome.Busy, "I'm already checking for updates.");
            try
            {
                SetStatus("Checking GitHub for a new version…");
                var rel = await LatestAsync(ct, beta).ConfigureAwait(false);
                if (rel == null) return (Outcome.Failed, Status);
                State.LastCheck = DateTime.Now;
                SaveState();
                if (rel.Version <= Current)
                {
                    SetStatus($"You're up to date (version {CurrentText}).");
                    return (Outcome.UpToDate, Status);
                }

                if (RunningFromSource)
                {
                    SetStatus($"Ace {Text(rel.Version)} is out. You're running from the source code, so I can't swap the files. " +
                              "Download NimbusAce-win-x64.zip from the GitHub release once, and that copy updates itself.");
                    return (Outcome.Failed, Status);
                }

                string dir = Path.Combine(Folder, "v" + Text(rel.Version));
                string zip = Path.Combine(dir, AssetName);
                string verified = Path.Combine(dir, "verified");
                try
                {
                    // Already downloaded and checked (it was waiting for a good moment)? Use it.
                    string? app = File.Exists(verified) && Directory.Exists(Path.Combine(dir, "new")) ? FindApp(Path.Combine(dir, "new")) : null;
                    if (app == null)
                    {
                        SetStatus($"Downloading Ace {Text(rel.Version)}…");
                        await DownloadAsync(rel.ZipUrl, zip, ct, f => SetStatus($"Downloading Ace {Text(rel.Version)}… {f:P0}")).ConfigureAwait(false);
                        string sha = await Http.GetStringAsync(rel.ShaUrl, ct).ConfigureAwait(false);
                        app = VerifyAndExtract(zip, sha, Path.Combine(dir, "new"), out string error);
                        if (app == null) { SetStatus(error); return (Outcome.Failed, error); }
                        try { File.Delete(zip); File.WriteAllText(verified, DateTime.Now.ToString("o")); } catch { }
                    }
                    CleanOld(rel.Version);
                    ReadyToInstall = true;

                    if (!canRestart())
                    {
                        SetStatus($"Ace {Text(rel.Version)} is ready. I'll install it when you're not busy.");
                        return (Outcome.Failed, Status);
                    }
                    await beforeRestart($"Updating Ace to {Text(rel.Version)}…").ConfigureAwait(false);
                    string? why = LaunchUpdater(app, rel.Version);
                    if (why != null) { SetStatus(why); return (Outcome.Failed, why); }
                    ReadyToInstall = false;
                    SetStatus($"Updating to {Text(rel.Version)}. Ace will be right back!");
                    return (Outcome.Restarting, Status);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { SetStatus("Update stopped."); return (Outcome.Failed, Status); }
                catch (Exception ex)
                {
                    SetStatus("The update didn't download: " + ex.Message);
                    return (Outcome.Failed, Status);
                }
            }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        /// <summary>Deletes downloads of other versions (the backup folder stays).</summary>
        private static void CleanOld(Version keep)
        {
            try
            {
                foreach (var d in Directory.GetDirectories(Folder, "v*"))
                    if (!Path.GetFileName(d).Equals("v" + Text(keep), StringComparison.OrdinalIgnoreCase))
                        try { Directory.Delete(d, recursive: true); } catch { }
            }
            catch { }
        }

        /// <summary>Starts NimbusUpdater from the new files (so it isn't one of the files being replaced). Null if it started.</summary>
        public static string? LaunchUpdater(string newApp, Version version)
        {
            string install = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string runner = Path.Combine(newApp, UpdaterExe);
            if (!File.Exists(runner))
            {
                // An older release without its own updater: copy ours next to the new files and run that.
                try
                {
                    foreach (var f in Directory.GetFiles(install, "NimbusUpdater.*"))
                        File.Copy(f, Path.Combine(newApp, Path.GetFileName(f)), overwrite: true);
                }
                catch { }
                if (!File.Exists(runner)) return "The update is missing NimbusUpdater, so I didn't install it.";
            }
            // Start whatever program the new version has (Ace.exe), and if it fails, the one running now.
            string oldExe = MainName() + ".exe";
            string exe = AppExeIn(newApp) ?? oldExe;
            string args = string.Join(" ", new[]
            {
                "--pid", Environment.ProcessId.ToString(),
                "--source", Quote(newApp),
                "--target", Quote(install),
                "--backup", Quote(Path.Combine(Folder, "backup")),
                "--exe", Quote(exe),
                "--old-exe", Quote(oldExe),
                "--ok", Quote(OkFile(version)),
                "--log", Quote(LogPath),
            });
            try
            {
                var p = Process.Start(new ProcessStartInfo(runner, args) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = newApp });
                return p == null ? "Couldn't start the updater." : null;
            }
            catch (Exception ex) { return "Couldn't start the updater: " + ex.Message; }
        }

        private static string Quote(string s) => "\"" + s.Replace("\"", "") + "\"";
    }
}
