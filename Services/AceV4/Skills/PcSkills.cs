using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Nimbus.AceV4.Brain;
using Nimbus.AceV4.Pc;

namespace Nimbus.AceV4
{
    /// <summary>Skills that control the PC: power, apps, windows, browser tabs, media, volume, files and checks.</summary>
    public sealed partial class AceV4Brain
    {
        private static readonly Random Rng = new();

        // ===================================================== Power

        private Task<AceAnswer?> ShutdownAsync(Turn t) => PowerAsync(t, restart: false);
        private Task<AceAnswer?> RestartAsync(Turn t) => PowerAsync(t, restart: true);

        private Task<AceAnswer?> PowerAsync(Turn t, bool restart)
        {
            // "shut down discord" is closing an app, not the PC.
            if (!restart && Regex.IsMatch(t.Lower, @"^(shut ?down|turn off) (?!the |my |this |pc|computer|laptop|system|machine|windows|everything|now)\w")) return Task.FromResult<AceAnswer?>(null);
            string what = restart ? "Restarting" : "Shutting down";
            Step(what + " in 10 seconds", "Cancel any time");
            var card = new CountdownCard
            {
                Title = restart ? "Restarting your PC" : "Shutting down your PC",
                Seconds = 10,
                OnFire = () => { ActivityTracker.Save(); if (restart) PowerControl.Restart(); else PowerControl.Shutdown(); },
            };
            card.Start();
            var a = AceAnswer.Say(restart
                ? Composer.Pick("restart", new[] { "Restarting your PC in 10 seconds. Save anything open! Say cancel or press Cancel to stop it.", "Okay {user}, restarting in 10 seconds. Press Cancel if you change your mind." })
                : Composer.Pick("shutdown", new[] { "Shutting down your PC in 10 seconds. Save anything open! Say cancel or press Cancel to stop it.", "Okay {user}, shutting down in 10 seconds. Press Cancel if you change your mind." }), "power");
            a.Speak = restart ? "Restarting in 10 seconds. Say cancel to stop." : "Shutting down in 10 seconds. Say cancel to stop.";
            a.Cards.Add(card);
            a.Mood = "alert";
            return Task.FromResult<AceAnswer?>(a);
        }

        private Task<AceAnswer?> SleepPcAsync(Turn t)
        {
            Step("Putting the PC to sleep");
            _ = Task.Run(async () => { await Task.Delay(2500).ConfigureAwait(false); ActivityTracker.Save(); PowerControl.Sleep(); });
            return Task.FromResult<AceAnswer?>(AceAnswer.Say("Putting your PC to sleep. Night night!", "power"));
        }

        private Task<AceAnswer?> LockAsync(Turn t)
        {
            Step("Locking the PC");
            _ = Task.Run(async () => { await Task.Delay(1200).ConfigureAwait(false); PowerControl.Lock(); });
            return Task.FromResult<AceAnswer?>(AceAnswer.Say("Locking your PC.", "power"));
        }

        // ===================================================== Apps and sites

        private static string StripTail(string s)
        {
            s = Regex.Replace(s, @"\b(for me|please|now|right now|real quick|quickly)$", "", RegexOptions.IgnoreCase).Trim();
            s = Regex.Replace(s, @"^(up|the|my|a|an)\s+", "", RegexOptions.IgnoreCase).Trim();
            return s.Trim(' ', '.', '!', '?', ',');
        }

        private async Task<AceAnswer?> OpenAsync(Turn t)
        {
            var m = t.Match(@"^(?:open|launch|start|run|fire up|bring up|pull up|load|go to|take me to|visit|navigate to)\s+(.+)$");
            string target = m.Success ? StripTail(m.Groups[1].Value) : StripTail(Regex.Replace(t.Lower, @"^(can you |could you )?(open|launch|start|run|go to)\s+", ""));
            if (target.Length == 0) return null;

            // "open the first one", "open number 2", "open it": something Ace just showed.
            var pick = PickResult(target);
            if (pick != null)
            {
                Step("Opening what I found", pick.Title);
                AppLauncher.Start(pick.Target);
                return AceAnswer.Say("Opening " + pick.Title + ".", "files");
            }

            // Timers, music and so on: "start a timer", "start the music".
            if (Regex.IsMatch(target, @"\btimer\b")) return await TimerAsync(t).ConfigureAwait(false);
            if (Regex.IsMatch(target, @"^(a )?new tab$|^tab$")) return await TabAsync(new Turn { Text = "new tab", Lower = "new tab", Ct = t.Ct }).ConfigureAwait(false);

            bool wantsWeb = Regex.IsMatch(target, @"\b(website|site|web site|tab|in (a |the )?(new )?(tab|browser)|in chrome|in edge|in firefox|\.com|\.org|\.net|\.gg|\.io)\b");
            string siteName = Regex.Replace(target, @"\s*\b(in (a |the )?(new )?(tab|browser|window)|in chrome|in edge|in firefox|in brave)\b", "").Trim();

            if (!wantsWeb)
            {
                Step("Looking for the app", target);
                string? opened = await AppLauncher.OpenAsync(target).ConfigureAwait(false);
                if (opened != null) return AceAnswer.Say(Composer.Pick("open", new[] { "Opening {0}.", "{0} coming right up.", "Launching {0}." }).Replace("{0}", opened), "apps");
            }

            string? url = BrowserControl.SiteUrl(siteName);
            if (url != null)
            {
                Step("Opening a site", url);
                BrowserControl.Open(url);
                return AceAnswer.Say(Composer.Pick("site", new[] { "Opening {0} in a new tab.", "Here's {0}.", "Pulling up {0}." }).Replace("{0}", PrettySite(url)), "browser");
            }

            // Maybe it's a file or folder: "open my homework doc".
            if (Regex.IsMatch(target, @"\b(file|doc|document|pdf|folder|video|photo|picture|song|spreadsheet|slides|presentation)\b"))
            {
                var q = FileQuery.Parse(target);
                if (q.Words.Count > 0)
                {
                    Step("Searching your files", string.Join(" ", q.Words));
                    var files = await Task.Run(() => FileFinder.Search(q, 5, t.Ct), t.Ct).ConfigureAwait(false);
                    if (files.Count > 0)
                    {
                        AppLauncher.Start(files[0].Path);
                        var a = AceAnswer.Say("Opening " + files[0].Name + ".", "files");
                        if (files.Count > 1) a.Cards.Add(FilesCard("Other matches", files.Skip(1).ToList()));
                        return a;
                    }
                }
            }

            // A folder by name: "open downloads".
            string? folder = KnownFolder(target);
            if (folder != null) { AppLauncher.Start(folder); return AceAnswer.Say("Opening " + Path.GetFileName(folder.TrimEnd('\\')) + ".", "apps"); }

            if (wantsWeb || Regex.IsMatch(target, @"^[a-z0-9-]+$"))
            {
                // Unknown name: search for it in a new tab rather than failing.
                string search = BrowserControl.SearchUrl(siteName, "", Config.SearchEngine);
                BrowserControl.Open(search);
                return AceAnswer.Say($"I don't know a site or app called \"{siteName}\", so I searched for it in a new tab.", "browser", 0.6);
            }
            return AceAnswer.Say($"I couldn't find an app called \"{target}\". If it's installed, try its exact name, like it appears in the Start menu.", "apps", 0.4);
        }

        private static string PrettySite(string url)
        {
            try { return new Uri(url).Host.Replace("www.", ""); } catch { return url; }
        }

        private static string? KnownFolder(string name) => name.ToLowerInvariant() switch
        {
            "downloads" or "my downloads" or "downloads folder" => Path.Combine(PcScan.Home, "Downloads"),
            "documents" or "my documents" or "documents folder" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "desktop" or "desktop folder" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "pictures" or "photos folder" or "pictures folder" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "videos" or "videos folder" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "music" or "music folder" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "screenshots" or "screenshots folder" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots"),
            _ => null,
        };

        /// <summary>"the first one", "number 3", "it", "the last one" → something from Ace's last list.</summary>
        private ResultItem? PickResult(string text)
        {
            if (Talk.LastResults.Count == 0) return null;
            string s = text.ToLowerInvariant().Trim();
            int index = -1;
            if (Regex.IsMatch(s, @"^(it|that|that one|this one|this|the top one|the best one)$")) index = 0;
            var m = Regex.Match(s, @"^(?:the\s+)?(first|second|third|fourth|fifth|sixth|last|1st|2nd|3rd|4th|5th)\s*(?:one|result|file|link|screenshot|picture|item)?$");
            if (m.Success) index = m.Groups[1].Value switch { "first" or "1st" => 0, "second" or "2nd" => 1, "third" or "3rd" => 2, "fourth" or "4th" => 3, "fifth" or "5th" => 4, "sixth" => 5, _ => Talk.LastResults.Count - 1 };
            m = Regex.Match(s, @"^(?:number|#|result|item)\s*(\d+)$");
            if (m.Success) index = int.Parse(m.Groups[1].Value) - 1;
            return index >= 0 && index < Talk.LastResults.Count ? Talk.LastResults[index] : null;
        }

        private static List<WinInfo> WindowsFor(string target)
        {
            string t = StripTail(target);
            if (Regex.IsMatch(t, @"^(that|this|it|the|current|this window|that window|the window|that app|this app|the app|whatever'?s open)$"))
            {
                var top = WindowControl.TopWindows().FirstOrDefault();
                return top != null ? new List<WinInfo> { top } : new List<WinInfo>();
            }
            return WindowControl.Find(t);
        }

        private Task<AceAnswer?> CloseAppAsync(Turn t)
        {
            var m = t.Match(@"^(?:close|quit|exit|kill|end|shut down|stop running)\s+(.+)$");
            if (!m.Success) return Task.FromResult<AceAnswer?>(null);
            string target = StripTail(m.Groups[1].Value);
            if (Regex.IsMatch(target, @"^(nimbus|ace|yourself|you)$")) return Task.FromResult<AceAnswer?>(AceAnswer.Say("I can't close myself from here, but you can close my window with the X at the top right.", "apps"));
            if (Regex.IsMatch(target, @"^(all|everything|all (my )?(apps|windows|programs))$"))
            {
                var all = WindowControl.TopWindows();
                if (all.Count == 0) return Task.FromResult<AceAnswer?>(AceAnswer.Say("Nothing's open to close.", "apps"));
                var a = AceAnswer.Say($"Close all {all.Count} open windows? Anything unsaved could ask you to save first.", "apps");
                a.Cards.Add(new ConfirmCard
                {
                    Question = $"Close {all.Count} windows?",
                    YesLabel = "Close them",
                    OnYes = () => { foreach (var w in all) WindowControl.Close(w); return Task.FromResult($"Closed {all.Count} windows."); },
                });
                return Task.FromResult<AceAnswer?>(a);
            }
            var wins = WindowsFor(target);
            Step("Finding the window", target);
            if (wins.Count == 0) return Task.FromResult<AceAnswer?>(AceAnswer.Say($"I don't see {target} open right now.", "apps", 0.8));
            foreach (var w in wins) WindowControl.Close(w);
            string name = WindowControl.Pretty(wins[0]);
            return Task.FromResult<AceAnswer?>(AceAnswer.Say(wins.Count > 1 ? $"Closed {wins.Count} {name} windows." : Composer.Pick("close", new[] { "Closed {0}.", "{0} is closed.", "Done, {0} is gone." }).Replace("{0}", name), "apps"));
        }

        private Task<AceAnswer?> MinimizeAsync(Turn t)
        {
            if (t.Has(@"^(minimi[sz]e|hide) (everything|all|all (my )?(apps|windows))$|^show( me)? (the )?desktop$"))
            {
                int n = WindowControl.MinimizeAll();
                return Task.FromResult<AceAnswer?>(AceAnswer.Say(n > 0 ? $"Minimized {Composer.Plural(n, "window", "windows")}." : "Everything's already out of the way.", "apps"));
            }
            var m = t.Match(@"^(?:minimi[sz]e|hide)\s+(.+)$");
            if (!m.Success) return Task.FromResult<AceAnswer?>(null);
            var wins = WindowsFor(m.Groups[1].Value);
            if (wins.Count == 0) return Task.FromResult<AceAnswer?>(AceAnswer.Say($"I don't see {StripTail(m.Groups[1].Value)} open.", "apps", 0.8));
            foreach (var w in wins) WindowControl.Minimize(w);
            return Task.FromResult<AceAnswer?>(AceAnswer.Say("Minimized " + WindowControl.Pretty(wins[0]) + ".", "apps"));
        }

        private Task<AceAnswer?> MaximizeAsync(Turn t)
        {
            var m = t.Match(@"^(?:maximi[sz]e|restore|un-?minimi[sz]e)\s+(.+)$");
            if (!m.Success) m = t.Match(@"^make\s+(.+?)\s+(?:full ?screen|maximi[sz]ed|big)$");
            if (!m.Success) return Task.FromResult<AceAnswer?>(null);
            var wins = WindowsFor(m.Groups[1].Value);
            if (wins.Count == 0) return Task.FromResult<AceAnswer?>(AceAnswer.Say($"I don't see {StripTail(m.Groups[1].Value)} open.", "apps", 0.8));
            bool restore = t.Lower.StartsWith("restore") || t.Lower.StartsWith("un");
            if (restore) WindowControl.Restore(wins[0]); else WindowControl.Maximize(wins[0]);
            return Task.FromResult<AceAnswer?>(AceAnswer.Say((restore ? "Brought back " : "Maximized ") + WindowControl.Pretty(wins[0]) + ".", "apps"));
        }

        private Task<AceAnswer?> ResizeAsync(Turn t)
        {
            // "resize discord to 800 by 600"
            var size = t.Match(@"^(?:resize|make|set)\s+(.+?)\s+(?:to\s+)?(\d{3,4})\s*(?:x|by|×)\s*(\d{3,4})");
            if (size.Success)
            {
                var wins = WindowsFor(size.Groups[1].Value);
                if (wins.Count == 0) return NotOpen(size.Groups[1].Value);
                WindowControl.Resize(wins[0], int.Parse(size.Groups[2].Value), int.Parse(size.Groups[3].Value));
                return Done($"Resized {WindowControl.Pretty(wins[0])} to {size.Groups[2].Value} by {size.Groups[3].Value}.");
            }
            // "make spotify bigger"
            var grow = t.Match(@"^make\s+(.+?)\s+(bigger|larger|wider|taller|smaller|narrower|shorter)$");
            if (grow.Success)
            {
                var wins = WindowsFor(grow.Groups[1].Value);
                if (wins.Count == 0) return NotOpen(grow.Groups[1].Value);
                bool up = grow.Groups[2].Value is "bigger" or "larger" or "wider" or "taller";
                WindowControl.Scale(wins[0], up ? 1.25 : 0.8);
                return Done((up ? "Made " : "Shrunk ") + WindowControl.Pretty(wins[0]) + (up ? " bigger." : " a bit."));
            }
            // "snap chrome to the left", "put spotify on the right half", "center notepad"
            var snap = t.Match(@"^(?:resize|snap|move|put|dock|make|center|centre)\s+(.+?)\s+(?:to|on|onto|into|in|at)?\s*(?:the\s+)?((?:top |bottom )?(?:left|right)(?: half| side| third| corner)?|top(?: half)?|bottom(?: half)?|center|centre|middle)(?: of the screen| side| half)?$");
            if (!snap.Success)
            {
                var c = t.Match(@"^(?:center|centre)\s+(.+)$");
                if (c.Success) snap = Regex.Match("x " + c.Groups[1].Value + " center", @"^x (.+) (center)$");
            }
            if (snap.Success)
            {
                var wins = WindowsFor(snap.Groups[1].Value);
                if (wins.Count == 0) return NotOpen(snap.Groups[1].Value);
                string where = snap.Groups[2].Value.Replace("centre", "center").Replace("middle", "center");
                WindowControl.Snap(wins[0], where);
                return Done($"Moved {WindowControl.Pretty(wins[0])} to the {where}.");
            }
            return Task.FromResult<AceAnswer?>(null);

            Task<AceAnswer?> NotOpen(string name) => Task.FromResult<AceAnswer?>(AceAnswer.Say($"I don't see {StripTail(name)} open.", "apps", 0.8));
            Task<AceAnswer?> Done(string text) { Step("Moved the window"); return Task.FromResult<AceAnswer?>(AceAnswer.Say(text, "apps")); }
        }

        private Task<AceAnswer?> ListAppsAsync(Turn t)
        {
            var wins = WindowControl.TopWindows();
            if (wins.Count == 0) return Task.FromResult<AceAnswer?>(AceAnswer.Say("Nothing's open besides me.", "apps"));
            var names = wins.Select(WindowControl.Pretty).Distinct().ToList();
            var a = AceAnswer.Say($"You've got {Composer.Plural(names.Count, "app", "apps")} open: {Composer.List(names.Take(8).ToList())}{(names.Count > 8 ? " and more" : "")}.", "apps");
            var card = new ResultsCard { Title = "Open apps" };
            foreach (var w in wins.Take(12)) card.Items.Add(new ResultItem { Title = WindowControl.Pretty(w), Subtitle = BrainShort(w.Title, 60) });
            a.Cards.Add(card);
            return Task.FromResult<AceAnswer?>(a);
        }

        private static string BrainShort(string s, int n) => Short(s, n);

        // ===================================================== Browser

        private static int WordNumber(string s) => s switch
        {
            "one" => 1, "two" => 2, "three" => 3, "four" => 4, "five" => 5, "six" => 6, "seven" => 7, "eight" => 8, "nine" => 9,
            _ => int.TryParse(s, out int n) ? n : 0,
        };

        private async Task<AceAnswer?> TabAsync(Turn t)
        {
            string l = t.Lower;
            string command; int number = 0;
            Match m;
            if (Regex.IsMatch(l, @"\b(close|shut)\b.*\btab\b")) command = "close";
            else if (Regex.IsMatch(l, @"\breopen\b")) command = "reopen";
            else if (Regex.IsMatch(l, @"\b(next|other) tab\b|\bswitch tabs?\b")) command = "next";
            else if (Regex.IsMatch(l, @"\b(previous|prev|last) tab\b")) command = "previous";
            else if (Regex.IsMatch(l, @"\b(refresh|reload)\b")) command = "refresh";
            else if (l == "go back") command = "back";
            else if (l == "go forward") command = "forward";
            else if (Regex.IsMatch(l, @"\bnew tab\b")) command = "new";
            else if (l.StartsWith("zoom in")) command = "zoom in";
            else if (l.StartsWith("zoom out")) command = "zoom out";
            else if (l.Contains("reset zoom")) command = "zoom reset";
            else if ((m = Regex.Match(l, @"\btab (\d|one|two|three|four|five|six|seven|eight|nine)\b")).Success) { command = "number"; number = WordNumber(m.Groups[1].Value); }
            else return null;

            Step("Sending the tab shortcut", command);
            if (command == "new" && BrowserControl.FrontBrowser() == null)
            {
                BrowserControl.Open(BrowserControl.SearchUrl("", "", Config.SearchEngine).Replace("search?q=", "").Replace("/?q=", ""));
                return AceAnswer.Say("Opened your browser.", "browser");
            }
            string? done = await BrowserControl.TabAsync(command, number).ConfigureAwait(false);
            return done == null ? AceAnswer.Say("I don't see a browser open.", "browser", 0.8) : AceAnswer.Say(done, "browser");
        }

        private Task<AceAnswer?> BrowserSearchAsync(Turn t)
        {
            const string Places = "youtube|amazon|reddit|wikipedia|github|google|images|maps|twitch|ebay|roblox|spotify|bing|duckduckgo";
            string where = "", query = "";
            var m = Regex.Match(t.Lower, @"^(?:search|look up)\s+(?:on\s+)?(" + Places + @")\s+(?:for\s+)?(.+)$");
            if (m.Success) { where = m.Groups[1].Value; query = m.Groups[2].Value; }
            else if ((m = Regex.Match(t.Lower, @"^(?:search|look up|find)\s+(?:for\s+)?(.+?)\s+on\s+(" + Places + @")$")).Success) { query = m.Groups[1].Value; where = m.Groups[2].Value; }
            else if ((m = Regex.Match(t.Lower, @"^(google|bing)\s+(.+)$")).Success) { where = m.Groups[1].Value; query = m.Groups[2].Value; }
            else if ((m = Regex.Match(t.Lower, @"^search (?:the web|the internet|online)\s+(?:for\s+)?(.+)$")).Success) { query = m.Groups[1].Value; }
            else if ((m = Regex.Match(t.Lower, @"^youtube search\s+(?:for\s+)?(.+)$")).Success) { where = "youtube"; query = m.Groups[1].Value; }
            else return Task.FromResult<AceAnswer?>(null);
            query = StripTail(query);
            if (query.Length == 0) return Task.FromResult<AceAnswer?>(null);
            if (where == "google") where = "";
            string url = BrowserControl.SearchUrl(query, where, Config.SearchEngine);
            Step("Opening a search in a new tab", url);
            BrowserControl.Open(url);
            string place = where.Length > 0 ? Composer.Capital(where) : "the web";
            return Task.FromResult<AceAnswer?>(AceAnswer.Say($"Searching {place} for \"{query}\".", "browser"));
        }

        // ===================================================== Media and volume

        private Task<AceAnswer?> MediaAsync(Turn t)
        {
            string l = t.Lower;
            string said;
            if (Regex.IsMatch(l, @"^(next|skip)|\b(skip|next) (this )?(song|track)\b")) { Media.Next(); said = "Skipped."; }
            else if (Regex.IsMatch(l, @"^(previous|last|go back a)")) { Media.Previous(); said = "Going back a track."; }
            else if (l.StartsWith("stop")) { Media.Stop(); said = "Stopped."; }
            else if (l.StartsWith("pause")) { Media.PlayPause(); said = "Paused."; }
            else { Media.PlayPause(); said = "Playing."; }
            Step("Pressed the media key");
            return Task.FromResult<AceAnswer?>(AceAnswer.Say(said, "media"));
        }

        private Task<AceAnswer?> VolumeAsync(Turn t)
        {
            string l = t.Lower;
            string said;
            var set = Regex.Match(l, @"\b(?:to|at)\s+(\d{1,3})\s*(?:%|percent)?");
            if (Regex.IsMatch(l, @"^unmute|\bunmute\b")) { Media.SetMute(false); said = "Unmuted."; }
            else if (Regex.IsMatch(l, @"^mute|\bmute\b")) { Media.SetMute(true); said = "Muted."; }
            else if (set.Success && Regex.IsMatch(l, @"\b(set|put|change|make|turn)\b|^volume")) { int v = Math.Clamp(int.Parse(set.Groups[1].Value), 0, 100); Media.SetVolume(v); said = $"Volume set to {v}%."; }
            else if (Regex.IsMatch(l, @"\b(up|louder|raise|increase|higher)\b")) { Media.Change(Regex.IsMatch(l, @"\b(a lot|way|much)\b") ? 25 : 10); said = "Turned it up" + VolumeNow(); }
            else if (Regex.IsMatch(l, @"\b(down|quieter|softer|lower|decrease)\b")) { Media.Change(Regex.IsMatch(l, @"\b(a lot|way|much)\b") ? -25 : -10); said = "Turned it down" + VolumeNow(); }
            else
            {
                var v = Media.GetVolume();
                said = v == null ? "I couldn't read the volume." : $"Volume is at {v}%{(Media.IsMuted() == true ? ", but you're muted" : "")}.";
            }
            Step("Changed the volume");
            return Task.FromResult<AceAnswer?>(AceAnswer.Say(said, "volume"));
        }

        private static string VolumeNow() => Media.GetVolume() is int v ? $" to {v}%." : ".";

        // ===================================================== Screenshots and files

        private async Task<AceAnswer?> TakeScreenshotAsync(Turn t)
        {
            bool snip = t.Has(@"\bsnip\b|\bpart of\b|\bselect");
            Step(snip ? "Opening the snipping tool" : "Taking a screenshot");
            if (snip) { Media.Snip(); return AceAnswer.Say("Drag over the part you want.", "screenshot"); }
            Status?.Invoke("hide-for-screenshot");
            await Task.Delay(450, t.Ct).ConfigureAwait(false);   // let the overlay get out of the way
            Media.Screenshot();
            await Task.Delay(900, t.Ct).ConfigureAwait(false);
            var latest = FileFinder.AllScreenshots(1).FirstOrDefault();
            var a = AceAnswer.Say("Screenshot saved" + (latest != null && (DateTime.Now - latest.Modified).TotalSeconds < 10 ? " to " + Path.GetFileName(Path.GetDirectoryName(latest.Path)) + "." : " to Pictures › Screenshots."), "screenshot");
            if (latest != null && (DateTime.Now - latest.Modified).TotalSeconds < 10)
                a.Cards.Add(new ResultsCard { Title = "Just now", Thumbnails = true, Items = { new ResultItem { Title = latest.Name, Target = latest.Path, IsFile = true, ImagePath = latest.Path } } });
            return a;
        }

        private async Task<AceAnswer?> FindScreenshotsAsync(Turn t)
        {
            if (t.Has(@"^(take|grab|capture|snap)\b")) return await TakeScreenshotAsync(t).ConfigureAwait(false);
            var q = FileQuery.Parse(t.Text);
            // "that say discord" / "with the error": the words to look for inside the picture.
            var inside = t.Match(@"\b(?:that (?:says?|shows?|has)|with|of|showing|containing|about)\s+(?:the\s+|a\s+|my\s+)?(.+)$");
            if (inside.Success)
            {
                q.Words = Lexical.Tokens(inside.Groups[1].Value).Where(w => !Regex.IsMatch(w, @"^(today|yesterday|week|month|screenshot|screen|shot)$")).ToList();
                // Tokens are stemmed; use the plain words for matching inside OCR text.
                q.Words = Regex.Matches(inside.Groups[1].Value.ToLowerInvariant(), @"[a-z0-9]{2,}").Select(x => x.Value)
                    .Where(w => !Regex.IsMatch(w, @"^(the|a|an|my|in|on|from|today|yesterday|this|last|week|month|screenshot|screenshots|screen|shot|that|it)$")).ToList();
            }
            Step(q.Words.Count > 0 ? "Reading your screenshots for " + string.Join(" ", q.Words) : "Finding your screenshots", q.From != null ? "since " + q.From.Value.ToString("MMM d") : "");
            var hits = await FileFinder.SearchScreenshotsAsync(q, 12, s => Say(s), t.Ct).ConfigureAwait(false);
            if (hits.Count == 0)
                return AceAnswer.Say(q.Words.Count > 0 ? $"I didn't find a screenshot with \"{string.Join(" ", q.Words)}\" in it." : "I didn't find any screenshots" + (q.From != null ? " from then." : "."), "files", 0.7);
            var card = new ResultsCard { Title = "Screenshots", Thumbnails = true };
            foreach (var (f, why) in hits)
                card.Items.Add(new ResultItem { Title = f.Modified.ToString("MMM d, h:mm tt"), Subtitle = why.Length > 0 ? why : f.Name, Target = f.Path, IsFile = true, ImagePath = f.Path });
            bool latestOnly = t.Has(@"\b(latest|last|newest|most recent)\b") && q.Words.Count == 0;
            if (latestOnly) card.Items = card.Items.Take(1).ToList();
            var a = AceAnswer.Say(latestOnly ? "Here's your latest screenshot, from " + NaturalTime.Friendly(hits[0].File.Modified, DateTime.Now) + "."
                : $"Found {Composer.Plural(hits.Count, "screenshot", "screenshots")}. Say \"open the first one\" to open it.", "files");
            a.Cards.Add(card);
            return a;
        }

        private static ResultsCard FilesCard(string title, List<FoundFile> files)
        {
            var card = new ResultsCard { Title = title };
            foreach (var f in files)
                card.Items.Add(new ResultItem
                {
                    Title = f.Name,
                    Subtitle = $"{f.SizeText} · {NaturalTime.Friendly(f.Modified, DateTime.Now)} · {ShortFolder(f.Path)}",
                    Target = f.Path,
                    IsFile = true,
                    ImagePath = Regex.IsMatch(f.Path, @"\.(png|jpe?g|gif|bmp|webp)$", RegexOptions.IgnoreCase) ? f.Path : "",
                });
            return card;
        }

        private static string ShortFolder(string path)
        {
            string dir = Path.GetDirectoryName(path) ?? "";
            string home = PcScan.Home;
            if (dir.StartsWith(home, StringComparison.OrdinalIgnoreCase)) dir = "~" + dir[home.Length..];
            return dir.Length > 50 ? "…" + dir[^48..] : dir;
        }

        private async Task<AceAnswer?> FindFilesAsync(Turn t)
        {
            var q = FileQuery.Parse(t.Text);
            if (q.Words.Count == 0 && q.Extensions.Count == 0 && q.From == null) return null;
            Step("Searching your files", (q.Words.Count > 0 ? string.Join(" ", q.Words) : "") + (q.KindName.Length > 0 ? " (" + q.KindName + ")" : "") + (q.From != null ? " since " + q.From.Value.ToString("MMM d") : ""));
            Say("Searching your files…");
            var files = await Task.Run(() => FileFinder.Search(q, 10, t.Ct), t.Ct).ConfigureAwait(false);
            if (files.Count == 0)
            {
                Step("Nothing yet; trying looser matching");
                if (q.Words.Count > 1)
                {
                    var loose = new FileQuery { Words = q.Words.OrderByDescending(w => w.Length).Take(1).ToList(), Extensions = q.Extensions, From = q.From, To = q.To, Folder = q.Folder };
                    files = await Task.Run(() => FileFinder.Search(loose, 10, t.Ct), t.Ct).ConfigureAwait(false);
                }
            }
            if (files.Count == 0) return AceAnswer.Say("I couldn't find a file like that in your Desktop, Documents, Downloads, Pictures, Videos, Music or OneDrive.", "files", 0.7);
            var a = AceAnswer.Say(files.Count == 1 ? $"Found it: {files[0].Name}." : $"Found {files.Count} files. The best match is {files[0].Name}. Say \"open the first one\" to open it.", "files");
            a.Cards.Add(FilesCard("Files", files));
            return a;
        }

        // ===================================================== Checks

        private async Task<AceAnswer?> NotificationsAsync(Turn t)
        {
            Step("Reading your notifications");
            var list = await NotificationReader.RecentAsync(TimeSpan.FromHours(t.Has(@"\b(today|all)\b") ? 24 : 12)).ConfigureAwait(false);
            if (list.Count == 0)
                return AceAnswer.Say(NotificationReader.LastProblem.Length > 0 ? "I couldn't read your notifications (" + NotificationReader.LastProblem + ")." : "No notifications in the last few hours. You're all caught up.", "checks");
            var important = list.Where(n => n.NeedsAttention).ToList();
            var a = AceAnswer.Say(important.Count > 0
                ? $"{Composer.Plural(important.Count, "notification looks", "notifications look")} like they need you. The newest is from {important[0].App}."
                : $"You've got {Composer.Plural(list.Count, "notification", "notifications")}, but nothing looks urgent.", "checks");
            var card = new ResultsCard { Title = important.Count > 0 ? "Needs you" : "Notifications" };
            foreach (var n in (important.Count > 0 ? important : list).Take(8))
                card.Items.Add(new ResultItem { Title = n.App, Subtitle = n.Line.Substring(Math.Min(n.Line.Length, n.App.Length + 3)) + " · " + NaturalTime.Friendly(n.When, DateTime.Now) });
            a.Cards.Add(card);
            if (important.Count > 0) a.Mood = "alert";
            return a;
        }

        private async Task<AceAnswer?> CheckNowAsync(Turn t)
        {
            if (RunChecks == null) return AceAnswer.Say("My checks aren't running yet. Give me a second to start up.", "checks", 0.5);
            Step("Waking up to check everything");
            return await RunChecks(t.Ct).ConfigureAwait(false);
        }

        private Task<AceAnswer?> BatteryAsync(Turn t)
        {
            var (has, pct, charging) = PcScan.Battery();
            return Task.FromResult<AceAnswer?>(AceAnswer.Say(!has ? "This PC doesn't have a battery, so it's always plugged in." : $"Battery's at {pct}%{(charging ? " and charging" : "")}.", "checks"));
        }

        private async Task<AceAnswer?> StorageAsync(Turn t)
        {
            if (t.Has(@"\b(empty|clear|clean)\b.*\brecycle bin\b"))
            {
                var (bytes, items) = PcScan.RecycleBin();
                if (items == 0) return AceAnswer.Say("The recycle bin's already empty.", "storage");
                var a = AceAnswer.Say($"The recycle bin has {items:N0} items using {Sizes.Text(bytes)}. Empty it? This can't be undone.", "storage");
                a.Cards.Add(new ConfirmCard { Question = "Empty the recycle bin?", YesLabel = "Empty it", OnYes = () => Task.FromResult(PcScan.EmptyRecycleBin() ? $"Emptied it. You got {Sizes.Text(bytes)} back." : "Windows wouldn't empty it.") });
                return a;
            }
            Step("Checking your drives");
            var drives = PcScan.Drives();
            var stats = new StatsCard { Title = "Storage" };
            foreach (var d in drives)
                stats.Rows.Add(new StatRow(d.Name + (d.Label.Length > 0 ? " " + d.Label : ""), $"{d.FreeGb:0} GB free of {d.TotalGb:0} GB", d.UsedPercent, d.Low));
            var main = drives.FirstOrDefault(d => d.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase)) ?? drives.FirstOrDefault();
            string text = main == null ? "I couldn't read your drives." : $"{main.Name} has {main.FreeGb:0} GB free out of {main.TotalGb:0} GB." + (main.Low ? " That's getting low." : "");
            var answer = AceAnswer.Say(text, "storage");
            answer.Cards.Add(stats);
            if (t.Has(@"\b(big|large|huge|biggest|largest|taking up|free up)\b") || main?.Low == true)
            {
                Step("Looking for big files", "over " + Config.BigFileMb + " MB");
                Say("Looking for big files…");
                var big = await Task.Run(() => PcScan.LargeFiles(Math.Max(100, Config.BigFileMb / 2) * 1024L * 1024, 8, t.Ct), t.Ct).ConfigureAwait(false);
                var (rb, ri) = PcScan.RecycleBin();
                var (dlFiles, dlBytes) = PcScan.DownloadsSize();
                var tips = new List<string>();
                if (big.Count > 0) tips.Add($"your biggest file is {big[0].Name} ({big[0].SizeText})");
                if (dlBytes > 2L * 1024 * 1024 * 1024) tips.Add($"Downloads is using {Sizes.Text(dlBytes)}");
                if (rb > 500L * 1024 * 1024) tips.Add($"the recycle bin has {Sizes.Text(rb)} you could empty");
                if (tips.Count > 0) answer.Text += " " + Composer.Capital(Composer.List(tips)) + ".";
                if (big.Count > 0) answer.Cards.Add(FilesCard("Biggest files", big));
            }
            if (main?.Low == true) answer.Mood = "alert";
            return answer;
        }

        private Task<AceAnswer?> SystemStatusAsync(Turn t)
        {
            var s = PcScan.Stats;
            Step("Reading the live stats");
            var card = new StatsCard { Title = "Your PC right now" };
            card.Rows.Add(new StatRow("CPU", $"{s.Cpu:0}%", s.Cpu, s.Cpu > 85));
            card.Rows.Add(new StatRow("RAM", $"{s.MemUsedGb:0.0} of {s.MemTotalGb:0.0} GB", s.MemPercent, s.MemPercent > 85));
            if (s.Gpu.HasValue) card.Rows.Add(new StatRow("GPU", $"{s.Gpu:0}%", s.Gpu.Value, s.Gpu > 95));
            if (s.CpuTemp.HasValue) card.Rows.Add(new StatRow("CPU temp", $"{s.CpuTemp:0}°C", Math.Min(100, s.CpuTemp.Value), s.CpuTemp >= 85));
            if (s.GpuTemp.HasValue) card.Rows.Add(new StatRow("GPU temp", $"{s.GpuTemp:0}°C", Math.Min(100, s.GpuTemp.Value), s.GpuTemp >= 85));
            card.Rows.Add(new StatRow("Disk C:", $"{s.DiskFreeGb:0} GB free", s.DiskUsedPercent, s.DiskLow));
            card.Rows.Add(new StatRow("Network", s.NetText, Math.Min(100, s.DownMBps * 5), false));

            var problems = new List<string>();
            if (s.Cpu > 85) problems.Add($"the CPU is busy at {s.Cpu:0}%");
            if (s.MemPercent > 85) problems.Add($"RAM is {s.MemPercent:0}% full");
            if (s.CpuTemp >= 85) problems.Add($"the CPU is hot ({s.CpuTemp:0}°C)");
            if (s.GpuTemp >= 85) problems.Add($"the GPU is hot ({s.GpuTemp:0}°C)");
            if (s.DiskLow) problems.Add("disk space is low");

            string text;
            if (t.Has(@"\b(ram|memory)\b") && t.Has(@"\b(using|eating|hogging|taking|what)\b"))
            {
                var top = PcScan.TopApps(5);
                text = top.Count == 0 ? s.RamLine + "." : $"{s.RamLine}. The biggest are {Composer.List(top.Take(3).Select(g => $"{g.Name} ({g.RamMb / 1024:0.0} GB)").ToList())}.";
            }
            else if (t.Has(@"\bcpu\b") && t.Has(@"\b(using|eating|hogging|taking|what)\b"))
            {
                var top = PcScan.TopCpu(4);
                text = $"CPU is at {s.Cpu:0}%. " + (top.Count > 0 ? $"Busiest: {Composer.List(top.Take(3).Select(x => $"{x.Name} ({x.Percent:0}%)").ToList())}." : "");
            }
            else if (t.Has(@"\btemp"))
                text = s.CpuTemp.HasValue ? $"CPU is {s.CpuTemp:0}°C{(s.GpuTemp.HasValue ? $" and the GPU is {s.GpuTemp:0}°C" : "")}." + (s.CpuTemp >= 85 ? " That's hot." : " That's fine.") : "I can't read temperatures. Run Ace as admin to turn the sensors on.";
            else if (problems.Count == 0)
                text = Composer.Pick("pc.ok", new[] { "Your PC's doing great. CPU {0}%, RAM {1}%.", "All good! CPU's at {0}% and RAM's at {1}%.", "Running smooth: {0}% CPU, {1}% RAM." })
                    .Replace("{0}", s.Cpu.ToString("0")).Replace("{1}", s.MemPercent.ToString("0"));
            else text = "Heads up: " + Composer.List(problems) + ".";

            var a = AceAnswer.Say(text, "status");
            a.Cards.Add(card);
            if (problems.Count > 0) a.Mood = "alert";
            return Task.FromResult<AceAnswer?>(a);
        }
    }
}
