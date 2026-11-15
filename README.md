# Ace for PC

Ace is a friendly assistant app for Windows, made to look and work like the **Ace iPad app**: a glowing orb at the top, your chat under it, and a "Talk to Ace" bar at the bottom. It runs by itself on your PC. No account, no internet needed for most things, and no .NET install needed for the release copy.

Everything Ace saves stays on your PC, in `%LOCALAPPDATA%\Nimbus` (the folder name is kept so nothing you saved before gets lost).

## Using it

| Do this | What happens |
|---|---|
| **Type** in "Talk to Ace" and press Enter | Ace answers in the chat, with cards for recipes, scripts, the calendar, games and more. Shift+Enter makes a new line. Up arrow brings back what you last typed. |
| **Click the orb** | Opens Settings: pick the brain (**Normal Ace** or **LFM3 Omni**), the "ask LFM3 Omni when Ace doesn't know" switch, auto-update, beta updates, sounds, voice and more. |
| **Hold the orb** (or press **Ctrl+Alt+Space** anywhere) | Ace listens. Let go of the orb, or just stop talking, and it answers out loud. |
| **Say "Hey Ace"** | Same as holding the orb (turn it on in Settings > Voice). |
| **Calendar button** (left of the orb) | Today and the next 7 days, plus your reminders. |
| **Heart button** (right of the orb) | "Check my PC": CPU, memory, temps, storage, apps and more. |
| **+ button** | Quick things to ask, the Scripts library, What I remember, Calendar and Settings. |
| **Esc** · **Ctrl+,** · **Ctrl+L** | Close a sheet · open Settings · clear the chat. |

The orb shows what Ace is doing, with the same colors as the iPad app: blue when it's ready, bright blue with rings when it's listening, gold with a spinning ring when it's thinking, little bars when it's talking, dots while you type, dim when you're away. When it looks at something (a PC check, "Good to see you!") it stretches into a pill with two eyes. A green ring fills around it during a focus round.

Each answer has **How I got this**: the steps Ace took and where the answer came from. Right-click a message to copy it.

Ace is a normal Windows window: taskbar button, Start menu shortcut, minimize / maximize / close, and you can drag the edges to resize it. There is **no pop-up** over your screen. When Ace has something to say while you're in another app, its taskbar button lights up and the message waits in the chat.

Starting Ace a second time just brings the open window to the front. "Start Ace with Windows" (Settings > Waking up) starts it minimized when you sign in.

## Install it (Windows 10 or 11)

1. Download **Ace-Setup.exe** from the newest release at **github.com/Stormblazerplayz/Ace-PC/releases**.
2. Double-click it. If Windows says "Windows protected your PC", click **More info** then **Run anyway**. No admin needed: it installs just for you into `%LOCALAPPDATA%\Programs\Ace`.
3. Ace opens. Find it again any time in the **Start menu** (tick "Put Ace on the desktop" in the installer for a desktop icon too). Uninstall it from **Settings > Apps**.
4. Optional, once: right-click `Setup-AceV4.ps1` in Ace's folder > **Run with PowerShell**. It downloads Ace's meaning net (about 90 MB) so Ace understands more ways of asking.

Prefer no installer? Unzip `NimbusAce-win-x64.zip` from the same release into a folder it can stay in and double-click `Ace.exe` (keep `Ace.exe`, `NimbusUpdater.exe` and `Data` together). Both copies update themselves.

**Tip:** for "Hey Ace" and Windows dictation, add the English speech pack under Windows **Settings > Time & language > Speech**.

### Run it from the source code

Install the **.NET 8 SDK**, open a terminal in this folder and run `dotnet run`, or open `NimbusV3Polish.csproj` in Visual Studio 2022 and press F5. (The project file keeps its old name so the build and release workflow don't break.)

## Updates by itself (from GitHub)

Ace checks **github.com/Stormblazerplayz/Ace-PC** for a new release when it starts and every 6 hours. When there is one it downloads `NimbusAce-win-x64.zip`, checks it against the release's `.sha256` file, unzips it, and waits until you're away (or the screen is off). Then the orb says "Updating Ace…", Ace closes, `NimbusUpdater.exe` swaps the files, keeps a backup of the old version, and starts Ace again. If the new version doesn't start, it puts the old one back. After an update Ace shows **What's new** once (or ask "what's new" any time).

- **Turn it off or check now:** Settings > Updates. Or ask Ace "check for updates".
- **Beta updates:** Settings > Updates > "Get beta updates", or say "turn on beta updates". Betas are tags like `v4.2.0-beta.1`. They come out first and might have bugs. "Switch to stable updates" goes back.
- **It only installs into the release copy.** Running from the source code, Ace tells you about new versions but doesn't swap files.
- **The updater's log** is in `%LOCALAPPDATA%\Nimbus\ace-v4\updates\updater.log`.

### Put out a new version

1. Add notes for it to `Data/whats-new.json` and push your changes to the Ace-PC repo.
2. On the repo's GitHub page open **Actions > Release > Run workflow**, type the version (like `4.1.1`, or `4.2.0-beta.1` for a beta) and press **Run workflow**. Or push a tag: `git tag v4.1.1` then `git push origin v4.1.1`.
3. GitHub Actions (`.github/workflows/release.yml`) builds Ace, zips it, makes the `.sha256` file and `Ace-Setup.exe`, runs `Tools/Test-Ace.ps1` (opens Ace on Windows, asks it a question, installs and uninstalls it) and only then publishes the release. Every Ace with auto-update on picks it up within 6 hours. Tags with a dash become pre-releases, which only go to people on the beta.

## What Ace can do

| Thing | How to use it |
|---|---|
| Wakes up every 15-30 min | The orb stretches into a pill and its eyes look around while it checks: CPU, RAM, GPU, temps, time, open apps (and frozen ones), notifications, new documents, files sitting in Downloads, disk space, big files, battery, calendar, screen on/off and if you're away. If all is fine it says so and goes back to a plain orb. If you're away or gaming full screen, it saves what it found and tells you when you're back. |
| Greets you | The first time you use your PC each day: hi, today's calendar, reminders, weather and anything it found while you were away. |
| Bedtime | Learns when you usually stop and start using your PC, then suggests a bedtime (goal: 9.5 hours, change it in settings). Nudges you when it's late. |
| Voice | Mic button, Ctrl+Alt+Space or "Hey Ace". Stops listening when you stop talking. Uses LFM3 Omni if it's running, otherwise Windows dictation (offline). |
| Thinking | Up to 8 passes: rules, a math and units solver, an intent net, the meaning net over 4,051 Q&A topics (18,131 ways to ask), breaking questions into parts, comparing things, web research across several sites, double-checking against the web, then Omni if it's running. Tap "Thought in N steps" to see how it got there. |
| Learns | "Remember that my dog's name is Max", "no, it's actually..." corrections, and good web answers get saved. |
| Recipes | "Give me a pancake recipe", "what can I make with eggs and cheese", "something sweet". 44 recipes work offline (more from the web). Tap "Start cooking" for one step at a time. |
| Recipe timers | Every step that waits ("bake 12 minutes", "let it cool 1 hour") gets a timer button with the right time. Run several at once, each with a name. When one ends you get a Windows pop-up and a sound. Say "next step", "set the timer" or "how long is left". |
| Focus timer | "Help me focus", "focus 40 minutes on math homework". 25 min focus, 5 min break, a long break after 4 rounds. A green ring around the orb fills up as you go, and Ace keeps quiet until your break. "Pause", "skip", "stop", "how long did I focus this week". |
| Weekly report card | "PC report card". Grades speed, memory, temperature, storage and restarts from A to F, with tips and how many minutes you focused. Pops up by itself on Sunday. Only uses real samples from your PC (it needs about an hour of data first). |
| App suggestions | Ace learns apps you open at the same hour on 3+ days ("Minecraft around 4 PM on weekdays") and asks "Want me to open it?". Say no 3 times and it stops. "What habits have you learned", "stop suggesting apps", "forget my habits". |
| Web | Facts, definitions, weather and news-type questions come from the web with sources. |
| Files | "Find my screenshots", "find my science essay doc", "biggest files". Screenshots show as thumbnails. |
| Apps | "Open Spotify", "minimize Chrome", "make Discord bigger", "close Notepad". |
| Browser, hands-free | "Open YouTube in a new tab", "search YouTube for Minecraft seeds", "close this tab", "next tab". |
| Calendar | The calendar button left of the orb, or tell Ace: "add soccer Friday at 5", "move it to 6", "rename it", "delete it". Ace always asks before changing it. |
| Shutdown/restart | "Shut down my PC" / "restart". Ace tells you and counts down 10 seconds. Press Cancel or say "cancel" to stop it. |
| Homework helper | "Help me with my math homework", "solve 2x + 3 = 11", "3/4 + 1/2", "lcm of 4 and 6", "is 91 prime", "area of a circle with radius 3". Shows every step and the answer. Keep going with the next problem. |
| Script library | **62 built-in scripts, offline.** 37 Roblox Lua (kill brick, moving and spinning parts, fading platforms, teleporters, checkpoints, jump/launch/speed pads, doors, coins with leaderstats, DataStore saving, day/night, rising lava, conveyor, heal pad, damage zone, sword, follower NPC, shop with a button, round timer, team door, leaderboard and more) and 25 C++ (11 Arduino, 11 console games and basics, 3 Unreal). "Give me a kill brick script", "Arduino servo code", "show me all the Lua scripts". The code card has colors, Copy, Save, and says exactly where to put it. **Scripts library** (click the orb, or the + button) to browse and search them all. |
| Explain the script | "Explain this script", "explain line 12", "explain the servo script", or tap **💡 Explain it** on a code card. Ace walks through every part of the script in plain words (all 62 scripts, every line covered). |
| Memory you can see | "What do you know about me" lists everything you told Ace, with a ✕ to forget each one. Also under **What I remember** (click the orb). "Forget everything about me" asks first. |
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
| Sounds | 19 sounds made by code (no sound files): wake, eyes out/in, looks that pan left and right with the eyes, blinks, a thinking hum, success, alerts, countdown ticks and more. Settings > "Save sounds as WAV" exports them. |


## Smarter replies

- **20 wordings for every reply** (Data/replies.json, 900 total), so Ace doesn't say the same thing twice in a row.
- **Typos, synonyms and follow-ups:** "wether tomoro", "how tall is it", "what about Germany?" all work.
- **Rule brain first.** The AI models are only a backup, and `<think>` tags are removed from their answers.
- **Fresh test (50 brand-new questions Ace was never tuned on):** 40/50 on the first try. The misses were rewordings like "why is the sky blue and not like green or something", "what does CPU stand for" (it checked the CPU instead) and "who invinted the lightbulb". After the fixes below: 49/50, and the last one was a wrong answer key (Ace said 100 km/h, the key only had mph).
- **Filler words are ignored** when matching questions ("actually", "kinda", "or something", "right now"), and fact questions like "what is RAM" or "how do I take a screenshot" now get the answer instead of a PC check or a screenshot search.
- **Smartness test:** now 315 questions (homework, translating, games, memory, notes, shopping, converting, screen and music, and 15 script questions were added). **Score: 315/315.** 3 of the old questions were fixed by adding new ways of asking to the Q&A bank.

## Honest part: Ace V4 vs. a real AI model

Ace V4 is a lot smarter than V3 at everyday stuff, but it isn't an LLM like Claude or LFM3 Omni.

- **Great at:** PC stuff, calendar, reminders, math, units, time, the 4,051 topics it knows, and anything a web page answers clearly.
- **Okay at:** follow-up questions ("what about Germany?", "why?", "tell me more"), questions with two parts, comparisons.
- **Not great at:** long chats, writing stories or essays, tricky reasoning, and brand-new questions with no web answer. For those it asks LFM3 Omni when it's running, or offers a web search.
- Without the setup script it matches by words only, so it understands fewer ways of asking.

## Files

```
App.xaml(.cs)                   starts Ace (one copy at a time) and opens the Ace window
Windows/AceWindow.cs            the Ace window: orb, chat, talk bar, timers, Settings/Calendar/Memories/Scripts sheets
Controls/AceOrb.cs              the orb: shading, swirl, moods, eyes, listening rings, thinking ring, focus ring
Controls/AceCardViews.cs        recipe, cooking, timers, focus, report card, results, stats, weather, calendar, sleep, confirm, countdown cards
Controls/AceMoreCards.cs        homework steps, memory, notes, shopping, translate, trivia, 20 questions, music, screen text, screen time, convert and code cards
Controls/AceSettingsView.cs     Ace's settings
Services/AceV4/AceRuntime.cs    the heartbeat: wake checks, greeting, bedtime, hotkey, "Hey Ace", updates
Services/AceV4/AceV4Brain.cs    rules and intents
Services/AceV4/Skills/          Think (research loop), Info, Plan (calendar, reminders, sleep, memory), PC, Cooking, Extras, Upgrades
Services/AceV4/Brain/           meaning net, word matching, intent net, Q&A bank, math steps, translator, games, scripts
Services/AceV4/Pc/              apps, windows, files, notifications, power, sleep, focus timer, screen time, AutoUpdater, WhatsNew
Services/AceV4/Web/             web search and page reading
Services/AceV4/Audio/           sound maker, voice, dictation, wake word
Services/Ace/                   talks to LFM3 Omni when it's running
Services/Calendar/              Ace's calendar (saved in calendar.json)
Data/                           Q&A bank, general facts, recipes, replies, scripts, whats-new.json
Updater/                        NimbusUpdater.exe: swaps the files, keeps a backup, rolls back
Setup-AceV4.ps1                 downloads the meaning net (run once)
Build-Release.ps1               builds the release zip and Ace-Setup.exe on Windows
Installer/Ace.iss               the installer (Inno Setup 6): Start menu, desktop icon, uninstaller
Tools/Test-Ace.ps1              the Windows check: smoke test, install, uninstall
.github/workflows/build.yml     builds and tests every change
.github/workflows/release.yml   builds, tests and publishes a release
```
