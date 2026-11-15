# Nimbus with Ace V4

Ace V4 is a big rule-based assistant with small neural nets inside it. It wakes up on its own every 15 to 30 minutes (random), slides two digital eyes out of the island, looks around while it checks your PC, and tells you only what matters. It has a new Calendar tab, voice dictation, "Hey Ace", web answers with recipe lists, file and screenshot search, app and tab control, a daily greeting, bedtime tips and shutdown/restart with a 10-second countdown.

Everything is saved on your PC, in `%LOCALAPPDATA%\Nimbus`.

## Run it (Windows 10 or 11)

1. Install the **.NET 8 SDK** if you don't have it: https://dotnet.microsoft.com/download/dotnet/8.0
2. Unzip this folder anywhere, like `Documents\NimbusAceV4`.
3. Right-click `Setup-AceV4.ps1` and pick **Run with PowerShell**. It downloads Ace's meaning net (about 90 MB, once). You can skip it, and Ace will use word matching only.
4. Open a terminal in the folder and run:

```
dotnet run
```

Or open `NimbusV3Polish.csproj` in Visual Studio 2022 and press F5.

5. Go to the **Ace** tab and type or tap the mic. Or press **Ctrl+Alt+Space** anywhere, or say **"Hey Ace"**.

**Tip:** to make "Hey Ace" and Windows dictation work, add the English speech pack under Windows **Settings > Time & language > Speech**. The setup script tells you if it's missing.

## Updates by itself (from GitHub)

Ace checks **github.com/Stormblazerplayz/Ace-PC** for a new release when Nimbus starts and every 6 hours. When there is one it downloads `NimbusAce-win-x64.zip`, checks it against the release's `.sha256` file, unzips it, and waits until you're away (or the screen is off). Then the eyes say "Updating Ace…", Nimbus closes, `NimbusUpdater.exe` swaps the files, keeps a backup of the old version, and starts Nimbus again. If the new version doesn't start, it puts the old one back. Your saved stuff in `%LOCALAPPDATA%\Nimbus` is never touched.

- **Turn it off or check now:** Settings > Ace V4 > Updates. Or ask Ace "check for updates".
- **It only installs into the release copy.** If you run from the source code (`dotnet run` or Visual Studio), Ace tells you about new versions but doesn't swap files, because your next build would put the old ones back. To get the self-updating copy: download `NimbusAce-win-x64.zip` from the newest release on GitHub, unzip it to a folder like `%LOCALAPPDATA%\Programs\NimbusAce`, and run `NimbusV3Polish.exe`. It needs the .NET 8 Desktop Runtime (the SDK already has it).
- **The updater's log** is in `%LOCALAPPDATA%\Nimbus\ace-v4\updates\updater.log`.

### Put out a new version

1. Push your changes to the Ace-PC repo.
2. Push a tag with the new version number:

```
git tag v4.0.1
git push origin v4.0.1
```

3. GitHub Actions (`.github/workflows/release.yml`) builds Nimbus, zips it, makes the `.sha256` file and publishes the release. Every Nimbus with auto-update on picks it up within 6 hours. Tags with a dash, like `v4.1.0-beta`, become pre-releases, and Ace skips those.

## Drop it into your own Nimbus

If your Nimbus is this project (V3 Polish), just replace your folder with this one. Your saved stuff in `%LOCALAPPDATA%\Nimbus` stays.

If your Nimbus is a different project, copy these in this order:

1. **Packages:** add these to your `.csproj` (copy them from this `.csproj`): `NAudio.WinMM`, `System.Speech`, `System.Diagnostics.EventLog`, `Microsoft.ML.OnnxRuntime`, `Microsoft.Data.Sqlite`. Also copy the `<None Include="Data\**\*" ...>` line. Your `TargetFramework` must be `net8.0-windows10.0.19041.0` or newer.
2. **Folders:** copy `Data/`, `Services/AceV4/`, `Services/Calendar/`, `Controls/AceEyes.cs`, `Controls/AceCardViews.cs`, `Controls/AceSettingsView.cs`, `Windows/AceOverlay.cs`, `Pages/AcePage.cs`, `Pages/CalendarPage.cs` and `Setup-AceV4.ps1`.
3. **Old Ace chat:** if you have the LFM3 Omni chat page, rename it to `AceChatPage` (the class name and `x:Class`). Ace V4 uses Omni for dictation and hard questions when it's running.
4. **MainWindow.xaml.cs:**
   - Add pages to your page list: `["ace"] = () => new AcePage()`, `["acechat"] = () => new AceChatPage()`, `["calendar"] = () => new CalendarPage()`.
   - In the constructor, after `InitializeComponent()`: `AceRuntime.Current.Start();` and `SourceInitialized += (_, _) => AceRuntime.Current.AttachWindow(this);`
   - In `Window_Loaded`: `_overlay = new AceOverlay { IsIslanded = () => _islanded };`
   - In `Closed`: `_overlay?.Close();`
   - Add `using Nimbus.AceV4;` and `using Nimbus.Overlay;`.
5. **MainWindow.xaml:** add a Calendar tab next to Ace: `<RadioButton Style="{StaticResource NavTab}" Tag="calendar" Content="Calendar" Checked="Nav_Checked"/>`
6. **App.xaml.cs:** in `OnExit`, add `AceRuntime.Current.Dispose();` first.
7. Build. It should say **0 errors, 0 warnings**.

## What Ace V4 does

| Thing | How to use it |
|---|---|
| Wakes up every 15-30 min | Eyes slide out of the island and look around while it checks: CPU, RAM, GPU, temps, time, open apps (and frozen ones), notifications, new documents, files sitting in Downloads, disk space, big files, battery, calendar, screen on/off and if you're away. If all is fine it blinks and goes back in. If you're away or gaming full screen, it saves what it found and tells you when you're back. |
| Greets you | The first time you use your PC each day: hi, today's calendar, reminders, weather and anything it found while you were away. |
| Bedtime | Learns when you usually stop and start using your PC, then suggests a bedtime (goal: 9.5 hours, change it in settings). Nudges you when it's late. |
| Voice | Mic button, Ctrl+Alt+Space or "Hey Ace". Stops listening when you stop talking. Uses LFM3 Omni if it's running, otherwise Windows dictation (offline). |
| Thinking | Up to 8 passes: rules, a math and units solver, an intent net, the meaning net over 3,555 Q&A topics (15,598 ways to ask), breaking questions into parts, comparing things, web research across several sites, double-checking against the web, then Omni if it's running. Tap "Thought in N steps" to see how it got there. |
| Learns | "Remember that my dog's name is Max", "no, it's actually..." corrections, and good web answers get saved. |
| Recipes | "Give me a pancake recipe", "what can I make with eggs and cheese", "something sweet". 44 recipes work offline (more from the web). Tap "Start cooking" for one step at a time. |
| Recipe timers | Every step that waits ("bake 12 minutes", "let it cool 1 hour") gets a timer button with the right time. Run several at once, each with a name. When one ends you get a Windows pop-up and a sound. Say "next step", "set the timer" or "how long is left". |
| Focus timer | "Help me focus", "focus 40 minutes on math homework". 25 min focus, 5 min break, a long break after 4 rounds. A bar under the eyes shows how far along you are, and Ace keeps quiet until your break. "Pause", "skip", "stop", "how long did I focus this week". |
| Eyes follow your mouse | When Ace is idle the eyes look at your cursor, and go back to center when you stop moving. Turn it off in settings. |
| Weekly report card | "PC report card". Grades speed, memory, temperature, storage and restarts from A to F, with tips and how many minutes you focused. Pops up by itself on Sunday. Only uses real samples from your PC (it needs about an hour of data first). |
| App suggestions | Ace learns apps you open at the same hour on 3+ days ("Minecraft around 4 PM on weekdays") and asks "Want me to open it?". Say no 3 times and it stops. "What habits have you learned", "stop suggesting apps", "forget my habits". |
| Web | Facts, definitions, weather and news-type questions come from the web with sources. |
| Files | "Find my screenshots", "find my science essay doc", "biggest files". Screenshots show as thumbnails. |
| Apps | "Open Spotify", "minimize Chrome", "make Discord bigger", "close Notepad". |
| Browser, hands-free | "Open YouTube in a new tab", "search YouTube for Minecraft seeds", "close this tab", "next tab". |
| Calendar | Calendar tab, or tell Ace: "add soccer Friday at 5", "move it to 6", "rename it", "delete it". Ace always asks before changing it. |
| Shutdown/restart | "Shut down my PC" / "restart". Ace tells you and counts down 10 seconds. Press Cancel or say "cancel" to stop it. |
| Homework helper | "Help me with my math homework", "solve 2x + 3 = 11", "3/4 + 1/2", "lcm of 4 and 6", "is 91 prime", "area of a circle with radius 3". Shows every step and the answer. Keep going with the next problem. |
| Script library | **62 built-in scripts, offline.** 37 Roblox Lua (kill brick, moving and spinning parts, fading platforms, teleporters, checkpoints, jump/launch/speed pads, doors, coins with leaderstats, DataStore saving, day/night, rising lava, conveyor, heal pad, damage zone, sword, follower NPC, shop with a button, round timer, team door, leaderboard and more) and 25 C++ (11 Arduino, 11 console games and basics, 3 Unreal). "Give me a kill brick script", "Arduino servo code", "show me all the Lua scripts". The code card has colors, Copy, Save, and says exactly where to put it. New **Scripts** tab to browse and search them all. |
| Explain the script | "Explain this script", "explain line 12", "explain the servo script", or tap **💡 Explain it** on a code card. Ace walks through every part of the script in plain words (all 62 scripts, every line covered). |
| Memory you can see | "What do you know about me" lists everything you told Ace, with a ✕ to forget each one. Also on the Ace tab side panel. "Forget everything about me" asks first. |
| Follow-up questions | After a fact Ace offers a related one ("Want to know: What currency does France use?"). Say "yes" or tap it. |
| Translate | "How do you say thank you in Spanish", "what does gracias mean", "translate good morning to French". About 100 phrases in Spanish, French, German, Italian, Portuguese and Japanese work offline; other languages and phrases use the web. |
| Trivia and 20 questions | "Let's play trivia" (10 questions, about 100 offline, pick a topic like "quiz me on space"). "Let's play 20 questions": Ace guesses what you're thinking of, or say "you think of something". Answer by voice or tap the buttons. **High scores:** a 🔥 streak counter during the game, your best score per topic, best streak and recent games. "Show my trivia scores", "what's my best space score", "reset my trivia scores". |
| Notes | "Take a note: buy new headphones", "read my notes", "delete the note about headphones". Add and delete on the card too. |
| Shopping list | "Add milk and eggs to my shopping list", "add the ingredients" after a recipe (the recipe card has a button too). Amounts add up: 1 cup + 1/2 cup flour = 1 1/2 cups. Tick things off, copy the list. |
| What's playing | "What song is playing?" Shows the song, artist and cover from Spotify, YouTube or any app, with play/pause and skip buttons. |
| What's on my screen | "What's on my screen?" reads the text on your screen (Windows' built-in text reader, offline). "Solve the problem on my screen" finds a math problem and works it out. |
| Screen time | Counts how long each app is used (only while you're actually using the PC). "How much screen time today", "this week", "how long have I played Minecraft". Optional limits: "set my screen time limit to 2 hours", "limit Minecraft to 1 hour". Gentle heads-ups only; it never closes anything. |
| Converting | "Convert 10 kilograms" shows every unit. "20 dollars to euros" uses today's rates (needs the internet). |
| Reminders | "What are my reminders" names them all, "cancel my reminder to study". |
| Sounds | 19 sounds made by code (no sound files): wake, eyes out/in, looks that pan left and right with the eyes, blinks, a thinking hum, success, alerts, countdown ticks and more. Settings > Ace V4 > "Save sounds as WAV" exports them. |

Ace V4's settings are on the Ace tab (side panel) and in Settings > Ace V4.

## Smarter replies

- **20 wordings for every reply** (Data/replies.json, 900 total), so Ace doesn't say the same thing twice in a row.
- **Typos, synonyms and follow-ups:** "wether tomoro", "how tall is it", "what about Germany?" all work.
- **Rule brain first.** The AI models are only a backup, and `<think>` tags are removed from their answers.
- **Fresh test (never seen before):** 50 brand-new questions, run once and not tuned on. **Score: 42/50.** Two of the 8 misses were mistakes in the answer key (adding pancake ingredients to the list was right, and Saturn really has 270+ known moons). The 6 real misses: who painted the Mona Lisa, what gas plants breathe in, sides of a hexagon, the longest river, "remember my dog is named Biscuit" then "what's my dog's name", and "who will win the World Cup in 2030" (it offered a web search instead of saying nobody knows yet).
- **Smartness test:** now 315 questions (homework, translating, games, memory, notes, shopping, converting, screen and music, and 15 script questions were added). **Score: 315/315.** 3 of the old questions were fixed by adding new ways of asking to the Q&A bank.

## Honest part: Ace V4 vs. a real AI model

Ace V4 is a lot smarter than V3 at everyday stuff, but it isn't an LLM like Claude or LFM3 Omni.

- **Great at:** PC stuff, calendar, reminders, math, units, time, the 3,555 topics it knows, and anything a web page answers clearly.
- **Okay at:** follow-up questions ("what about Germany?", "why?", "tell me more"), questions with two parts, comparisons.
- **Not great at:** long chats, writing stories or essays, tricky reasoning, and brand-new questions with no web answer. For those it asks LFM3 Omni when it's running, or offers a web search.
- Without the setup script it matches by words only, so it understands fewer ways of asking.

## Files (new in Ace V4)

```
Setup-AceV4.ps1                 downloads the meaning net (run once)
Data/qa-bank.json               3,555 question-and-answer topics
Services/AceV4/AceRuntime.cs    the heartbeat: wake checks, greeting, bedtime, hotkey, "Hey Ace"
Services/AceV4/AceV4Brain.cs    rules and intents
Data/recipes.json               44 offline recipes with step timers
Data/replies.json               20 wordings for each of Ace's replies
Services/AceV4/Skills/          Think (research loop), Info, Plan (calendar, reminders, sleep), PC, Cooking, Extras (focus, report card, habits)
Services/AceV4/Brain/           meaning net, word matching, intent net, Q&A bank, sentence maker
Services/AceV4/Pc/              apps, windows, files, notifications, power, sleep tracking, FocusTimer, HealthLog, AppHabits
Services/AceV4/Web/             web search and page reading
Services/AceV4/Audio/           sound maker, sound mixer, voice, dictation, wake word
Services/Calendar/              the Nimbus calendar (saved in calendar.json)
Controls/AceEyes.cs             the digital eyes
Controls/AceCardViews.cs        recipe, cooking, timers, focus, report card, results, stats, weather, calendar, sleep, confirm, countdown cards
Controls/AceSettingsView.cs     Ace's settings (now with a screen time limit)
Controls/AceMoreCards.cs        homework steps, memory, notes, shopping, translate, trivia, 20 questions, music, screen text, screen time, convert and code cards
Pages/ScriptsPage.cs            the Scripts tab
Data/scripts/                   62 scripts (lua/, cpp/) and index.json
Services/AceV4/Skills/Upgrades.cs   all the new skills
Services/AceV4/Brain/MathSteps.cs, Translator.cs, Games.cs, ScriptLibrary.cs
Services/AceV4/Pc/Lists.cs (notes, shopping), NowPlaying.cs (music, screen reading), ScreenTime.cs
Services/AceV4/Brain/ScriptExplainer.cs   explains scripts part by part
Services/AceV4/Pc/TriviaScores.cs        trivia high scores (trivia-scores.json)
Services/AceV4/Pc/AutoUpdater.cs         checks GitHub releases, downloads, checks the SHA256, starts the updater
Updater/                        NimbusUpdater.exe: swaps the files, keeps a backup, rolls back
.github/workflows/release.yml   builds and publishes a release when you push a v* tag
Windows/AceOverlay.cs           the eyes pod that slides out of the island
Pages/AcePage.cs                the Ace V4 tab
Pages/CalendarPage.cs           the Calendar tab
Pages/AceChatPage.xaml(.cs)     the old LFM3 Omni chat (button on the Ace tab)
```

---

# Nimbus V3 Polish notes (from before)

A working WPF version of the V3 Polish design: every tab, the island, and bouncy motion everywhere.
You can run it as its own demo app, or copy the pieces you like into your real Nimbus.

## Run it (Windows)

1. Install the **.NET 8 SDK** if you don't have it.
2. Open a terminal in this folder and run:

```
dotnet run
```

Or open `NimbusV3Polish.csproj` in Visual Studio 2022 and press F5.

**Temps and fans:** CPU temp and fan speed need admin rights. Run the terminal (or Visual Studio) as administrator.
If they still show a dash, LibreHardwareMonitor may also need its driver installed on your PC. GPU temp and load usually work without admin.

## What's real and what's sample

| Tab | Real | Sample (hook to your code) |
|---|---|---|
| Glance | Clock, CPU, RAM, GPU, temps, fans, disk, download speed, uptime | Weather line, AFK / Keep awake / Code / Empty Bin buttons |
| Ace | Ace V4 (see above). The old Omni chat is AceChatPage | |
| Alerts | Filters, dismiss, Clear all, badge count | The 4 alerts (call `Add(...)` with your real ones) |
| Audio | Mute, sliders, play/pause UI | Track, per-app volume (use NAudio or CoreAudio) |
| Clips | Folder button opens Videos | Save adds a fake clip; hook your replay buffer |
| Memory | Committed, RAM, graph, app list, **Trim** and **Close** (asks "Sure?" first) | The auto-trim keeper itself |
| Games | RAM free, **Free memory now** (trims background apps, skips Nimbus) | Now playing, library |
| Orbit | Nothing yet | All of it. This tab is my guess from your photos |
| Settings | Search, Motion slider (changes the bounce live), animations on/off | Other rows are empty |

## Copy pieces into your Nimbus

Do these in order. Each one works on its own.

1. **Theme**: copy `Themes/NimbusTheme.xaml` and merge it in your `App.xaml` (see this project's `App.xaml`).
   Now any button can use `Style="{StaticResource PillButton}"` and it grows on hover and squishes when pressed.
2. **Spring helper**: copy `Motion/Spring.cs`. Then anywhere in code:
   - `Spring.PopChildren(panel)` pops cards in one by one
   - `Spring.Bump(element)` squishes something that just changed
   - `Spring.Jump(element)` makes a buddy jump
   - `Spring.To(transform, TranslateTransform.YProperty, 120)` springs any number to a new value
3. **Sliding tab highlight**: copy `NavIndicator` + `NavList` from `MainWindow.xaml` and `MoveIndicator` from `MainWindow.xaml.cs`.
4. **Island**: copy the `Canvas` / `Shell` setup from `MainWindow.xaml`, then `ToIsland`, `FromIsland`, `SpringShell` and `MoveWindow` from `MainWindow.xaml.cs`.
   Your window needs `WindowStyle="None"` and `AllowsTransparency="True"`.
5. **Real temps**: copy `Services/SystemStats.cs`, `Services/Hub.cs` and `Controls/StatTile.*`, and add the LibreHardwareMonitorLib package (see the `.csproj`).
6. **Extras**: `Controls/CrewBuddy.*` (Pebble, Nova, Ace that blink, jump and sweat when hot) and `Controls/WaveBars.cs` (music bars).

Change `RootNamespace`/namespaces from `Nimbus` if your project uses a different one.

## Make it more or less bouncy

- Live: Settings (gear) → Motion → slider.
- For good: in `Motion/Spring.cs` change `Springiness` (lower = bouncier) and `Oscillations` (more = more wobbles).
  Change `NimbusSpring` in `Themes/NimbusTheme.xaml` to match, since the button styles use that one.

## Files

```
App.xaml(.cs)             starts the app, merges the theme
MainWindow.xaml(.cs)      title bar, sidebar, island
Themes/NimbusTheme.xaml   colors, fonts, buttons, switches, sliders
Motion/Spring.cs          all the bouncy motion
Controls/                 StatTile, CrewBuddy, WaveBars
Services/                 SystemStats (live numbers), Processes (trim/close), Hub (shared stuff)
Pages/                    one file pair per tab, plus Settings
```
