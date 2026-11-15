using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Nimbus.AceV4.Brain;

namespace Nimbus.AceV4.Pc
{
    /// <summary>One top-level window you can see on the taskbar.</summary>
    public sealed record WinInfo(IntPtr Handle, string Title, string Process, int Pid);

    /// <summary>Finds, opens, moves, minimizes and closes app windows.</summary>
    public static class WindowControl
    {
        private static readonly int SelfPid = Environment.ProcessId;

        /// <summary>Friendly names people say, to the process names Windows uses.</summary>
        public static readonly Dictionary<string, string[]> Aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["chrome"] = new[] { "chrome" }, ["google chrome"] = new[] { "chrome" }, ["edge"] = new[] { "msedge" },
            ["microsoft edge"] = new[] { "msedge" }, ["firefox"] = new[] { "firefox" }, ["brave"] = new[] { "brave" },
            ["opera"] = new[] { "opera", "opera_gx" }, ["opera gx"] = new[] { "opera" }, ["browser"] = new[] { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc" },
            ["file explorer"] = new[] { "explorer" }, ["explorer"] = new[] { "explorer" }, ["files"] = new[] { "explorer" },
            ["discord"] = new[] { "discord" }, ["spotify"] = new[] { "spotify" }, ["steam"] = new[] { "steamwebhelper", "steam" },
            ["vs code"] = new[] { "code" }, ["vscode"] = new[] { "code" }, ["visual studio code"] = new[] { "code" },
            ["visual studio"] = new[] { "devenv" }, ["blender"] = new[] { "blender" }, ["obs"] = new[] { "obs64", "obs" },
            ["notepad"] = new[] { "notepad" }, ["calculator"] = new[] { "calculatorapp", "calculator" }, ["calc"] = new[] { "calculatorapp" },
            ["task manager"] = new[] { "taskmgr" }, ["settings"] = new[] { "systemsettings" }, ["terminal"] = new[] { "windowsterminal" },
            ["minecraft"] = new[] { "javaw", "minecraft.windows", "minecraftlauncher" }, ["roblox"] = new[] { "robloxplayerbeta", "robloxstudiobeta" },
            ["bambu studio"] = new[] { "bambu-studio", "bambustudio" }, ["word"] = new[] { "winword" }, ["excel"] = new[] { "excel" },
            ["powerpoint"] = new[] { "powerpnt" }, ["paint"] = new[] { "mspaint" }, ["photos"] = new[] { "photos", "microsoft.photos" },
            ["beamng"] = new[] { "beamng.drive.x64", "beamng.drive" }, ["nimbus"] = new[] { "nimbusv3polish" },
        };

        public static readonly string[] Browsers = { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc", "opera_gx" };

        /// <summary>Visible app windows, front-most first.</summary>
        public static List<WinInfo> TopWindows(bool includeNimbus = false)
        {
            var list = new List<WinInfo>();
            var names = new Dictionary<uint, string>();
            Win32.EnumWindows((h, _) =>
            {
                try
                {
                    if (!Win32.IsWindowVisible(h)) return true;
                    if (Win32.GetWindow(h, Win32.GW_OWNER) != IntPtr.Zero) return true;
                    long ex = (long)Win32.GetWindowLongPtr(h, Win32.GWL_EXSTYLE);
                    if ((ex & Win32.WS_EX_TOOLWINDOW) != 0 && (ex & Win32.WS_EX_APPWINDOW) == 0) return true;
                    if (Win32.DwmGetWindowAttribute(h, Win32.DWMWA_CLOAKED, out int cloaked, 4) == 0 && cloaked != 0) return true;
                    int len = Win32.GetWindowTextLength(h);
                    if (len <= 0) return true;
                    var sb = new StringBuilder(len + 1);
                    Win32.GetWindowText(h, sb, sb.Capacity);
                    string title = sb.ToString();
                    if (title is "Program Manager" or "Windows Input Experience") return true;
                    Win32.GetWindowThreadProcessId(h, out uint pid);
                    if (!includeNimbus && pid == SelfPid) return true;
                    if (!names.TryGetValue(pid, out var pname))
                    {
                        try { using var p = Process.GetProcessById((int)pid); pname = p.ProcessName; }
                        catch { pname = ""; }
                        names[pid] = pname;
                    }
                    list.Add(new WinInfo(h, title, pname, (int)pid));
                }
                catch { }
                return true;
            }, IntPtr.Zero);
            return list;
        }

        /// <summary>
        /// The front-most window that matches what someone said: an alias ("edge"), a process name ("blender"),
        /// or words in the title ("the farside doc").
        /// </summary>
        public static List<WinInfo> Find(string name)
        {
            name = name.Trim().Trim('.', '!', '?').ToLowerInvariant();
            foreach (var junk in new[] { "the ", "my ", "app ", "window " }) if (name.StartsWith(junk)) name = name[junk.Length..];
            name = name.Replace(" app", "").Replace(" window", "").Trim();
            var windows = TopWindows();
            if (name is "this" or "it" or "that" or "this one" or "current" or "")
                return windows.Take(1).ToList();

            if (Aliases.TryGetValue(name, out var procs))
            {
                var byAlias = windows.Where(w => procs.Any(p => w.Process.Equals(p, StringComparison.OrdinalIgnoreCase))).ToList();
                if (byAlias.Count > 0) return byAlias;
            }
            string squashed = name.Replace(" ", "");
            var byProcess = windows.Where(w => w.Process.Equals(squashed, StringComparison.OrdinalIgnoreCase)
                                            || w.Process.StartsWith(squashed, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byProcess.Count > 0) return byProcess;
            var byTitle = windows.Where(w => w.Title.Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byTitle.Count > 0) return byTitle;
            var words = Lexical.Tokens(name);
            return windows.Where(w => words.Count > 0 && words.All(t => (w.Title + " " + w.Process).ToLowerInvariant().Contains(t))).ToList();
        }

        /// <summary>"msedge" → "Edge", "discord" → "Discord".</summary>
        public static string AppName(string process)
        {
            string p = (process ?? "").Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
            return p.ToLowerInvariant() switch
            {
                "msedge" => "Edge", "chrome" => "Chrome", "firefox" => "Firefox", "explorer" => "File Explorer", "code" => "VS Code",
                "devenv" => "Visual Studio", "winword" => "Word", "excel" => "Excel", "powerpnt" => "PowerPoint", "robloxstudiobeta" => "Roblox Studio",
                "robloxplayerbeta" => "Roblox", "spotify" => "Spotify", "discord" => "Discord", "steam" => "Steam", "steamwebhelper" => "Steam",
                "blender" => "Blender", "obs64" => "OBS", "windowsterminal" => "Terminal", "notepad" => "Notepad", "nimbusv3polish" => "Ace", "ace" => "Ace",
                "applicationframehost" => "a Windows app", "javaw" => "Minecraft (Java)", "minecraft.windows" => "Minecraft",
                _ => p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..] : p,
            };
        }

        public static string Pretty(WinInfo w)
        {
            string t = w.Title.Length > 48 ? w.Title[..48] + "…" : w.Title;
            return t;
        }

        public static void Minimize(WinInfo w) => Win32.ShowWindow(w.Handle, Win32.SW_MINIMIZE);
        public static void Maximize(WinInfo w) { Win32.ShowWindow(w.Handle, Win32.SW_MAXIMIZE); Win32.ForceForeground(w.Handle); }
        public static void Restore(WinInfo w) { Win32.ShowWindow(w.Handle, Win32.SW_RESTORE); Win32.ForceForeground(w.Handle); }
        public static bool Focus(WinInfo w) => Win32.ForceForeground(w.Handle);

        /// <summary>Asks the app to close, like clicking its X. The app can still ask to save.</summary>
        public static void Close(WinInfo w) => Win32.PostMessage(w.Handle, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

        public static int MinimizeAll()
        {
            int n = 0;
            foreach (var w in TopWindows()) { if (!Win32.IsIconic(w.Handle)) { Minimize(w); n++; } }
            return n;
        }

        /// <summary>The screen area (without the taskbar) of the monitor the window is on, in pixels.</summary>
        internal static Win32.RECT WorkArea(IntPtr hwnd)
        {
            var mon = Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST);
            var info = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
            Win32.GetMonitorInfo(mon, ref info);
            return info.rcWork;
        }

        public static void MoveTo(WinInfo w, int x, int y, int width, int height)
        {
            if (Win32.IsZoomed(w.Handle) || Win32.IsIconic(w.Handle)) Win32.ShowWindow(w.Handle, Win32.SW_RESTORE);
            Win32.SetWindowPos(w.Handle, IntPtr.Zero, x, y, Math.Max(200, width), Math.Max(150, height), Win32.SWP_NOZORDER | Win32.SWP_SHOWWINDOW);
            Win32.ForceForeground(w.Handle);
        }

        /// <summary>"left", "right", "top", "bottom", "center", "left third" ...</summary>
        public static bool Snap(WinInfo w, string where)
        {
            var a = WorkArea(w.Handle);
            int W = a.Width, H = a.Height;
            where = where.ToLowerInvariant();
            if (where.Contains("top left")) { MoveTo(w, a.Left, a.Top, W / 2, H / 2); return true; }
            if (where.Contains("top right")) { MoveTo(w, a.Left + W / 2, a.Top, W / 2, H / 2); return true; }
            if (where.Contains("bottom left")) { MoveTo(w, a.Left, a.Top + H / 2, W / 2, H / 2); return true; }
            if (where.Contains("bottom right")) { MoveTo(w, a.Left + W / 2, a.Top + H / 2, W / 2, H / 2); return true; }
            if (where.Contains("left") && where.Contains("third")) { MoveTo(w, a.Left, a.Top, W / 3, H); return true; }
            if (where.Contains("right") && where.Contains("third")) { MoveTo(w, a.Left + 2 * W / 3, a.Top, W / 3, H); return true; }
            if (where.Contains("left")) { MoveTo(w, a.Left, a.Top, W / 2, H); return true; }
            if (where.Contains("right")) { MoveTo(w, a.Left + W / 2, a.Top, W / 2, H); return true; }
            if (where.Contains("top")) { MoveTo(w, a.Left, a.Top, W, H / 2); return true; }
            if (where.Contains("bottom")) { MoveTo(w, a.Left, a.Top + H / 2, W, H / 2); return true; }
            if (where.Contains("center") || where.Contains("middle"))
            {
                Win32.GetWindowRect(w.Handle, out var r);
                int cw = Math.Min(r.Width, W), ch = Math.Min(r.Height, H);
                MoveTo(w, a.Left + (W - cw) / 2, a.Top + (H - ch) / 2, cw, ch);
                return true;
            }
            return false;
        }

        /// <summary>Grows (factor over 1) or shrinks the window around its center.</summary>
        public static void Scale(WinInfo w, double factor)
        {
            if (Win32.IsZoomed(w.Handle)) Win32.ShowWindow(w.Handle, Win32.SW_RESTORE);
            Win32.GetWindowRect(w.Handle, out var r);
            var a = WorkArea(w.Handle);
            int nw = (int)Math.Min(a.Width, r.Width * factor), nh = (int)Math.Min(a.Height, r.Height * factor);
            int cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2;
            int x = Math.Clamp(cx - nw / 2, a.Left, Math.Max(a.Left, a.Right - nw));
            int y = Math.Clamp(cy - nh / 2, a.Top, Math.Max(a.Top, a.Bottom - nh));
            MoveTo(w, x, y, nw, nh);
        }

        public static void Resize(WinInfo w, int width, int height)
        {
            if (Win32.IsZoomed(w.Handle)) Win32.ShowWindow(w.Handle, Win32.SW_RESTORE);
            Win32.GetWindowRect(w.Handle, out var r);
            var a = WorkArea(w.Handle);
            width = Math.Min(width, a.Width);
            height = Math.Min(height, a.Height);
            int x = Math.Clamp(r.Left, a.Left, Math.Max(a.Left, a.Right - width));
            int y = Math.Clamp(r.Top, a.Top, Math.Max(a.Top, a.Bottom - height));
            MoveTo(w, x, y, width, height);
        }
    }

    /// <summary>Opens apps by the name you say: built-in Windows tools, Start menu shortcuts and Store apps.</summary>
    public static class AppLauncher
    {
        private static readonly Dictionary<string, string> BuiltIn = new(StringComparer.OrdinalIgnoreCase)
        {
            ["notepad"] = "notepad.exe", ["calculator"] = "calc.exe", ["calc"] = "calc.exe", ["paint"] = "mspaint.exe",
            ["command prompt"] = "cmd.exe", ["cmd"] = "cmd.exe", ["powershell"] = "powershell.exe", ["terminal"] = "wt.exe",
            ["windows terminal"] = "wt.exe", ["task manager"] = "taskmgr.exe", ["file explorer"] = "explorer.exe",
            ["explorer"] = "explorer.exe", ["files"] = "explorer.exe", ["control panel"] = "control.exe", ["settings"] = "ms-settings:",
            ["windows settings"] = "ms-settings:", ["snipping tool"] = "ms-screenclip:", ["registry editor"] = "regedit.exe",
            ["device manager"] = "devmgmt.msc", ["disk cleanup"] = "cleanmgr.exe", ["character map"] = "charmap.exe",
            ["sound settings"] = "ms-settings:sound", ["display settings"] = "ms-settings:display", ["bluetooth settings"] = "ms-settings:bluetooth",
            ["wifi settings"] = "ms-settings:network-wifi", ["storage settings"] = "ms-settings:storagesense", ["windows update"] = "ms-settings:windowsupdate",
            ["camera"] = "microsoft.windows.camera:", ["clock"] = "ms-clock:", ["alarms"] = "ms-clock:", ["store"] = "ms-windows-store:",
            ["microsoft store"] = "ms-windows-store:", ["mail"] = "outlookmail:", ["photos"] = "ms-photos:", ["xbox"] = "xbox:",
            ["recycle bin"] = "shell:RecycleBinFolder", ["downloads"] = "shell:Downloads", ["documents"] = "shell:Personal",
            ["pictures"] = "shell:My Pictures", ["videos"] = "shell:My Video", ["music"] = "shell:My Music", ["desktop folder"] = "shell:Desktop",
            ["screenshots"] = "shell:Screenshots", ["startup folder"] = "shell:Startup", ["this pc"] = "shell:MyComputerFolder",
        };

        /// <summary>The names of Windows' own tools that Ace can open ("notepad", "task manager"...).</summary>
        public static IEnumerable<string> BuiltInNames => BuiltIn.Keys;

        private static List<(string Name, string Path)>? _shortcuts;
        private static List<(string Name, string AppId)>? _startApps;
        private static readonly SemaphoreSlim Gate = new(1, 1);

        /// <summary>Opens an app. Returns what it opened, or null if nothing matched.</summary>
        public static async Task<string?> OpenAsync(string name)
        {
            name = name.Trim().Trim('.', '!', '?');
            foreach (var junk in new[] { "the ", "my ", "up " }) if (name.StartsWith(junk, StringComparison.OrdinalIgnoreCase)) name = name[junk.Length..];
            name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+(app|application|program)$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            if (name.Length == 0) return null;

            if (BuiltIn.TryGetValue(name, out var cmd)) { Start(cmd); return Title(name); }

            await LoadAsync().ConfigureAwait(false);
            var best = Best(name, _shortcuts!.Select(s => (s.Name, s.Path)));
            var bestApp = Best(name, _startApps!.Select(a => (a.Name, a.AppId)));
            if (best.Score >= bestApp.Score && best.Score >= 0.6)
            {
                Start(best.Target);
                return best.Name;
            }
            if (bestApp.Score >= 0.6)
            {
                Start("shell:AppsFolder\\" + bestApp.Target);
                return bestApp.Name;
            }

            // Last try: maybe it's on the PATH, like "code" or "blender"
            try
            {
                var psi = new ProcessStartInfo(name.Replace(" ", "")) { UseShellExecute = true };
                using var p = Process.Start(psi);
                return Title(name);
            }
            catch { return null; }
        }

        /// <summary>Names of every app Ace knows how to open (for the help text and matching).</summary>
        public static async Task<List<string>> KnownAppsAsync()
        {
            await LoadAsync().ConfigureAwait(false);
            return _shortcuts!.Select(s => s.Name).Concat(_startApps!.Select(a => a.Name)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
        }

        private static string Title(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

        private static (string Name, string Target, double Score) Best(string want, IEnumerable<(string Name, string Target)> options)
        {
            string w = want.ToLowerInvariant();
            (string, string, double) best = ("", "", 0);
            foreach (var (n, target) in options)
            {
                string name = n.ToLowerInvariant();
                double score;
                if (name == w) score = 1;
                else if (name.StartsWith(w + " ") || name.StartsWith(w)) score = 0.9 - Math.Min(0.2, (name.Length - w.Length) * 0.01);
                else if (name.Contains(" " + w) || name.Contains(w)) score = 0.75 - Math.Min(0.2, (name.Length - w.Length) * 0.01);
                else
                {
                    var a = Lexical.Tokens(w);
                    var b = Lexical.Tokens(name);
                    score = a.Count == 0 ? 0 : a.Count(t => b.Any(x => x == t || Lexical.Close(x, t))) / (double)a.Count * 0.7;
                }
                // Uninstallers and help files are never what someone means.
                if (name.Contains("uninstall") || name.Contains("readme") || name.Contains("help") || name.Contains("documentation")) score -= 0.5;
                if (score > best.Item3) best = (n, target, score);
            }
            return best;
        }

        private static async Task LoadAsync()
        {
            if (_shortcuts != null && _startApps != null) return;
            await Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_shortcuts == null)
                {
                    var list = new List<(string, string)>();
                    foreach (var root in new[]
                    {
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                    })
                    {
                        try
                        {
                            if (!Directory.Exists(root)) continue;
                            foreach (var f in Directory.EnumerateFiles(root, "*.*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                            {
                                string ext = Path.GetExtension(f).ToLowerInvariant();
                                if (ext is ".lnk" or ".url" or ".appref-ms") list.Add((Path.GetFileNameWithoutExtension(f), f));
                            }
                        }
                        catch { }
                    }
                    _shortcuts = list;
                }
                if (_startApps == null)
                {
                    _startApps = new List<(string, string)>();
                    try
                    {
                        // Store apps (Spotify from the Store, Minecraft Launcher, Xbox ...) are only listed by Get-StartApps.
                        var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command \"Get-StartApps | ConvertTo-Json -Compress\"")
                        {
                            UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8,
                        };
                        using var p = Process.Start(psi)!;
                        var read = p.StandardOutput.ReadToEndAsync();
                        if (await Task.WhenAny(read, Task.Delay(8000)).ConfigureAwait(false) == read)
                        {
                            using var doc = JsonDocument.Parse(read.Result);
                            var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new List<JsonElement> { doc.RootElement };
                            foreach (var item in items)
                            {
                                string n = item.TryGetProperty("Name", out var a) ? a.GetString() ?? "" : "";
                                string id = item.TryGetProperty("AppID", out var b) ? b.GetString() ?? "" : "";
                                if (n.Length > 0 && id.Length > 0) _startApps.Add((n, id));
                            }
                        }
                        else { try { p.Kill(); } catch { } }
                    }
                    catch { }
                }
            }
            finally { Gate.Release(); }
        }

        public static void Start(string target)
        {
            var psi = new ProcessStartInfo(target) { UseShellExecute = true };
            using var _ = Process.Start(psi);
        }
    }

    /// <summary>Hands-free browser control: open sites and searches in new tabs, and switch or close tabs.</summary>
    public static class BrowserControl
    {
        public static readonly Dictionary<string, string> Sites = new(StringComparer.OrdinalIgnoreCase)
        {
            ["youtube"] = "https://www.youtube.com", ["yt"] = "https://www.youtube.com", ["google"] = "https://www.google.com",
            ["gmail"] = "https://mail.google.com", ["email"] = "https://mail.google.com", ["google drive"] = "https://drive.google.com",
            ["drive"] = "https://drive.google.com", ["google docs"] = "https://docs.google.com", ["docs"] = "https://docs.google.com",
            ["google sheets"] = "https://sheets.google.com", ["google calendar"] = "https://calendar.google.com", ["google maps"] = "https://maps.google.com",
            ["maps"] = "https://maps.google.com", ["google photos"] = "https://photos.google.com", ["google translate"] = "https://translate.google.com",
            ["translate"] = "https://translate.google.com", ["reddit"] = "https://www.reddit.com", ["github"] = "https://github.com",
            ["twitch"] = "https://www.twitch.tv", ["netflix"] = "https://www.netflix.com", ["amazon"] = "https://www.amazon.com",
            ["wikipedia"] = "https://www.wikipedia.org", ["wiki"] = "https://www.wikipedia.org", ["roblox"] = "https://www.roblox.com",
            ["discord"] = "https://discord.com/app", ["twitter"] = "https://x.com", ["x"] = "https://x.com", ["instagram"] = "https://www.instagram.com",
            ["tiktok"] = "https://www.tiktok.com", ["facebook"] = "https://www.facebook.com", ["pinterest"] = "https://www.pinterest.com",
            ["claude"] = "https://claude.ai", ["chatgpt"] = "https://chatgpt.com", ["outlook"] = "https://outlook.live.com",
            ["minecraft"] = "https://www.minecraft.net", ["curseforge"] = "https://www.curseforge.com", ["modrinth"] = "https://modrinth.com",
            ["makerworld"] = "https://makerworld.com", ["printables"] = "https://www.printables.com", ["thingiverse"] = "https://www.thingiverse.com",
            ["bambu"] = "https://bambulab.com", ["blender"] = "https://www.blender.org", ["stack overflow"] = "https://stackoverflow.com",
            ["stackoverflow"] = "https://stackoverflow.com", ["duolingo"] = "https://www.duolingo.com", ["khan academy"] = "https://www.khanacademy.org",
            ["epic games"] = "https://store.epicgames.com", ["steam"] = "https://store.steampowered.com", ["news"] = "https://news.google.com",
            ["weather"] = "https://weather.com", ["photopea"] = "https://www.photopea.com", ["canva"] = "https://www.canva.com",
            ["figma"] = "https://www.figma.com", ["hugging face"] = "https://huggingface.co", ["huggingface"] = "https://huggingface.co",
            ["chess"] = "https://www.chess.com", ["chess.com"] = "https://www.chess.com", ["ebay"] = "https://www.ebay.com",
            ["walmart"] = "https://www.walmart.com", ["target"] = "https://www.target.com", ["imdb"] = "https://www.imdb.com",
            ["spotify"] = "https://open.spotify.com", ["youtube music"] = "https://music.youtube.com", ["youtube studio"] = "https://studio.youtube.com",
            ["scratch"] = "https://scratch.mit.edu", ["speedtest"] = "https://www.speedtest.net", ["speed test"] = "https://www.speedtest.net",
            ["beamng"] = "https://www.beamng.com", ["anthropic"] = "https://www.anthropic.com",
        };

        public static string SearchUrl(string query, string where, string engine)
        {
            string q = Uri.EscapeDataString(query.Trim());
            return where.ToLowerInvariant() switch
            {
                "youtube" or "yt" => "https://www.youtube.com/results?search_query=" + q,
                "amazon" => "https://www.amazon.com/s?k=" + q,
                "reddit" => "https://www.reddit.com/search/?q=" + q,
                "wikipedia" or "wiki" => "https://en.wikipedia.org/w/index.php?search=" + q,
                "github" => "https://github.com/search?q=" + q,
                "images" or "pictures" => "https://www.google.com/search?tbm=isch&q=" + q,
                "maps" or "google maps" => "https://www.google.com/maps/search/" + q,
                "twitch" => "https://www.twitch.tv/search?term=" + q,
                "ebay" => "https://www.ebay.com/sch/i.html?_nkw=" + q,
                "roblox" => "https://www.roblox.com/discover/?Keyword=" + q,
                "spotify" => "https://open.spotify.com/search/" + q,
                "bing" => "https://www.bing.com/search?q=" + q,
                "duckduckgo" => "https://duckduckgo.com/?q=" + q,
                _ => engine.ToLowerInvariant() switch
                {
                    "bing" => "https://www.bing.com/search?q=" + q,
                    "duckduckgo" => "https://duckduckgo.com/?q=" + q,
                    "brave" => "https://search.brave.com/search?q=" + q,
                    _ => "https://www.google.com/search?q=" + q,
                },
            };
        }

        /// <summary>Turns "youtube", "github.com", "www.roblox.com/games" into a full address. Null if it isn't a site.</summary>
        public static string? SiteUrl(string what)
        {
            string w = what.Trim().Trim('.', '!', '?').ToLowerInvariant();
            foreach (var junk in new[] { "the ", "a ", "up " }) if (w.StartsWith(junk)) w = w[junk.Length..];
            foreach (var junk in new[] { " website", " site", " web site", " page", " homepage", " in a new tab", " in new tab", " in the browser", " in chrome", " in edge" })
                if (w.EndsWith(junk)) w = w[..^junk.Length];
            w = w.Trim();
            if (Sites.TryGetValue(w, out var url)) return url;
            if (w.StartsWith("http://") || w.StartsWith("https://")) return w;
            if (System.Text.RegularExpressions.Regex.IsMatch(w, @"^[a-z0-9-]+(\.[a-z0-9-]+)*\.(com|org|net|io|ai|gg|tv|co|dev|app|edu|gov|me|us|uk|ca|de|fr|xyz|games|info)(/\S*)?$"))
                return "https://" + w;
            return null;
        }

        /// <summary>Opens an address in the default browser. Browsers open it as a new tab when they're already open.</summary>
        public static void Open(string url) => AppLauncher.Start(url);

        /// <summary>The browser window that was most recently in front, if any.</summary>
        public static WinInfo? FrontBrowser() =>
            WindowControl.TopWindows().FirstOrDefault(w => Browsers().Contains(w.Process, StringComparer.OrdinalIgnoreCase));

        private static string[] Browsers() => WindowControl.Browsers;

        /// <summary>
        /// Sends a tab shortcut to the front-most browser: close, next, previous, reopen, refresh, back, forward, new, or a tab number.
        /// Returns a short description, or null if no browser is open.
        /// </summary>
        public static async Task<string?> TabAsync(string command, int number = 0)
        {
            var browser = FrontBrowser();
            if (browser == null) return null;
            Win32.ForceForeground(browser.Handle);
            await Task.Delay(180).ConfigureAwait(false);   // let the browser take focus before the keys arrive
            const ushort C = Win32.VK_CONTROL, S = Win32.VK_SHIFT, A = Win32.VK_MENU;
            switch (command)
            {
                case "close": Win32.Chord(C, Win32.VK_W); return "Closed the tab.";
                case "next": Win32.Chord(C, Win32.VK_TAB); return "Next tab.";
                case "previous": Win32.Chord(C, S, Win32.VK_TAB); return "Previous tab.";
                case "reopen": Win32.Chord(C, S, Win32.VK_T); return "Brought back the last closed tab.";
                case "refresh": Win32.Chord(Win32.VK_F5); return "Refreshed the page.";
                case "back": Win32.Chord(A, Win32.VK_LEFT); return "Went back.";
                case "forward": Win32.Chord(A, Win32.VK_RIGHT); return "Went forward.";
                case "new": Win32.Chord(C, Win32.VK_T); return "Opened a new tab.";
                case "zoom in": Win32.Chord(C, 0xBB); return "Zoomed in.";
                case "zoom out": Win32.Chord(C, 0xBD); return "Zoomed out.";
                case "zoom reset": Win32.Chord(C, 0x30); return "Zoom reset.";
                case "number":
                    int n = Math.Clamp(number, 1, 9);
                    Win32.Chord(C, (ushort)(0x30 + n));
                    return n == 9 ? "Went to the last tab." : $"Went to tab {n}.";
                default: return null;
            }
        }
    }
}
