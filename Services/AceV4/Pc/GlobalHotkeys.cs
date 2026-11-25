using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Nimbus.AceV4.Pc
{
    /// <summary>What a global shortcut does.</summary>
    public enum HotkeyAction
    {
        /// <summary>Opens Ace (or brings it to the front) from anywhere in Windows.</summary>
        ShowAce,
        /// <summary>Starts listening, like holding the orb.</summary>
        Talk,
    }

    /// <summary>
    /// A key combo like Ctrl+Alt+A: Windows modifier flags (MOD_CONTROL, MOD_ALT, MOD_SHIFT, MOD_WIN) and a
    /// virtual-key code. Turns to and from text ("Ctrl+Alt+A") so it can live in the settings file.
    /// </summary>
    public readonly record struct HotkeyCombo(uint Modifiers, uint Key)
    {
        public const uint Ctrl = 2, Alt = 1, Shift = 4, Win = 8;
        private const uint AllMods = Ctrl | Alt | Shift | Win;

        public static HotkeyCombo None => default;
        public bool IsNone => Key == 0;

        /// <summary>Key names, by virtual-key code. The first name is the one Ace shows.</summary>
        private static readonly Dictionary<uint, string[]> Names = BuildNames();
        private static readonly Dictionary<string, uint> ByName = BuildLookup();

        private static Dictionary<uint, string[]> BuildNames()
        {
            var d = new Dictionary<uint, string[]>();
            for (uint c = 'A'; c <= 'Z'; c++) d[c] = new[] { ((char)c).ToString() };
            for (uint c = '0'; c <= '9'; c++) d[c] = new[] { ((char)c).ToString() };
            for (uint f = 1; f <= 24; f++) d[0x6F + f] = new[] { "F" + f };
            for (uint n = 0; n <= 9; n++) d[0x60 + n] = new[] { "Num" + n, "NumPad" + n };
            d[0x20] = new[] { "Space", "Spacebar" };
            d[0x0D] = new[] { "Enter", "Return" };
            d[0x09] = new[] { "Tab" };
            d[0x1B] = new[] { "Esc", "Escape" };
            d[0x08] = new[] { "Backspace", "Back" };
            d[0x2D] = new[] { "Insert", "Ins" };
            d[0x2E] = new[] { "Delete", "Del" };
            d[0x24] = new[] { "Home" };
            d[0x23] = new[] { "End" };
            d[0x21] = new[] { "PageUp", "PgUp", "Prior" };
            d[0x22] = new[] { "PageDown", "PgDn", "Next" };
            d[0x25] = new[] { "Left" };
            d[0x26] = new[] { "Up" };
            d[0x27] = new[] { "Right" };
            d[0x28] = new[] { "Down" };
            d[0x13] = new[] { "Pause", "Break" };
            d[0x2C] = new[] { "PrintScreen", "PrtSc", "Snapshot" };
            d[0x91] = new[] { "ScrollLock", "Scroll" };
            d[0x6A] = new[] { "NumMultiply", "Multiply" };
            d[0x6B] = new[] { "NumAdd", "Add" };
            d[0x6D] = new[] { "NumSubtract", "Subtract" };
            d[0x6E] = new[] { "NumDecimal", "Decimal" };
            d[0x6F] = new[] { "NumDivide", "Divide" };
            d[0xBA] = new[] { ";", "Semicolon", "OemSemicolon", "Oem1" };
            d[0xBB] = new[] { "=", "Equals", "OemPlus", "Plus" };
            d[0xBC] = new[] { ",", "Comma", "OemComma" };
            d[0xBD] = new[] { "-", "Minus", "Dash", "OemMinus" };
            d[0xBE] = new[] { ".", "Period", "Dot", "OemPeriod" };
            d[0xBF] = new[] { "/", "Slash", "OemQuestion", "Oem2" };
            d[0xC0] = new[] { "`", "Backtick", "Grave", "Tilde", "OemTilde", "Oem3" };
            d[0xDB] = new[] { "[", "OpenBracket", "OemOpenBrackets", "Oem4" };
            d[0xDC] = new[] { "\\", "Backslash", "OemPipe", "Oem5" };
            d[0xDD] = new[] { "]", "CloseBracket", "OemCloseBrackets", "Oem6" };
            d[0xDE] = new[] { "'", "Quote", "Apostrophe", "OemQuotes", "Oem7" };
            return d;
        }

        private static Dictionary<string, uint> BuildLookup()
        {
            var d = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            foreach (var (vk, names) in Names)
                foreach (string n in names) d[n.Replace(" ", "")] = vk;
            return d;
        }

        /// <summary>True if Ace knows a name for this virtual-key code (so it can be saved and shown).</summary>
        public static bool IsKnownKey(uint vk) => Names.ContainsKey(vk);

        public static string KeyName(uint vk) => Names.TryGetValue(vk, out var n) ? n[0] : "Key" + vk.ToString("X2");

        /// <summary>"Ctrl+Alt+Shift+Win+A". An empty string for no combo.</summary>
        public override string ToString()
        {
            if (IsNone) return "";
            var parts = new List<string>(5);
            if ((Modifiers & Ctrl) != 0) parts.Add("Ctrl");
            if ((Modifiers & Alt) != 0) parts.Add("Alt");
            if ((Modifiers & Shift) != 0) parts.Add("Shift");
            if ((Modifiers & Win) != 0) parts.Add("Win");
            parts.Add(KeyName(Key));
            return string.Join("+", parts);
        }

        /// <summary>
        /// Reads "Ctrl+Alt+A", "ctrl + shift + f5", "Win+Space" and so on. Empty, "off" or "none" means no combo.
        /// Returns false (with a reason) for text that isn't a combo Ace can use.
        /// </summary>
        public static bool TryParse(string? text, out HotkeyCombo combo, out string problem)
        {
            if (!TryParseRaw(text, out combo, out problem)) return false;
            string why = combo.Problem();
            if (why.Length > 0) { problem = why; combo = None; return false; }
            return true;
        }

        /// <summary>Reads the text without checking whether Windows lets apps use the combo.</summary>
        private static bool TryParseRaw(string? text, out HotkeyCombo combo, out string problem)
        {
            combo = None;
            problem = "";
            string s = (text ?? "").Trim();
            if (s.Length == 0 || s.Equals("off", StringComparison.OrdinalIgnoreCase) || s.Equals("none", StringComparison.OrdinalIgnoreCase))
                return true;

            uint mods = 0, key = 0;
            // "+" splits the parts. A key called "+" is written "=" (it's the same key), or "NumAdd" on the number pad.
            var parts = s.Split('+').Select(p => p.Trim().Replace(" ", "")).ToList();
            if (parts.Any(p => p.Length == 0)) { problem = $"\"{s}\" has an empty part. Write it like Ctrl+Alt+A."; return false; }
            foreach (string p in parts)
            {
                uint mod = p.ToLowerInvariant() switch
                {
                    "ctrl" or "control" or "ctl" => Ctrl,
                    "alt" or "option" => Alt,
                    "shift" => Shift,
                    "win" or "windows" or "super" or "meta" or "cmd" => Win,
                    _ => 0,
                };
                if (mod != 0)
                {
                    if ((mods & mod) != 0) { problem = $"\"{p}\" is in there twice."; return false; }
                    mods |= mod;
                    continue;
                }
                if (key != 0) { problem = $"\"{s}\" has two keys. Use one key with Ctrl, Alt, Shift or Win."; return false; }
                if (!ByName.TryGetValue(p, out key)) { problem = $"I don't know the key \"{p}\"."; return false; }
            }
            if (key == 0) { problem = $"\"{s}\" needs a key too, like Ctrl+Alt+A."; return false; }
            combo = new HotkeyCombo(mods, key);
            return true;
        }

        /// <summary>Why this combo can't be a global shortcut, or "" if it can.</summary>
        public string Problem()
        {
            if (IsNone) return "";
            if ((Modifiers & ~AllMods) != 0) return "That combo has a modifier Windows doesn't know.";
            if (!IsKnownKey(Key)) return "Ace can't use that key for a shortcut. Try a letter, number or F key.";
            if (Key == 0x7B) return "Windows keeps F12 for debuggers, so apps can't use it as a shortcut. Pick a different key.";
            bool fKey13Plus = Key >= 0x7C && Key <= 0x87;   // F13-F24: nothing else uses them, so they can be alone
            if ((Modifiers & (Ctrl | Alt | Win)) == 0 && !fKey13Plus)
                return "Add Ctrl, Alt or the Windows key, so the shortcut doesn't get in the way when you type.";
            if (ReservedSet.Contains(this)) return $"{this} belongs to Windows. Pick a different combo.";
            return "";
        }

        /// <summary>Combos Windows keeps for itself (some never reach apps, the rest would break something you use).</summary>
        private static readonly string[] Reserved =
        {
            "Ctrl+Alt+Delete", "Ctrl+Shift+Esc", "Ctrl+Esc", "Alt+Tab", "Alt+Shift+Tab", "Ctrl+Alt+Tab", "Alt+Esc", "Alt+F4",
            "Alt+Space", "Win+L", "Win+Tab", "Win+D", "Win+E", "Win+R", "Win+I", "Win+S", "Win+X", "Win+V", "Win+G",
            "Win+PrintScreen", "Win+Shift+S", "Ctrl+Win+Left", "Ctrl+Win+Right", "Ctrl+Win+D", "Ctrl+Win+F4",
        };

        private static HashSet<HotkeyCombo>? _reserved;
        private static HashSet<HotkeyCombo> ReservedSet => _reserved ??= Reserved
            .Select(r => TryParseRaw(r, out var c, out _) ? c : None)
            .Where(c => !c.IsNone)
            .ToHashSet();
    }

    /// <summary>
    /// Ace's global shortcuts (Windows RegisterHotKey). They work while Ace is minimized or behind other windows,
    /// because Windows sends the key press to Ace's window whatever is in front. If another app already uses a combo,
    /// Windows refuses it: Ace keeps the shortcut it had, says which one is taken, and keeps trying a combo that was
    /// taken when Ace started (it works as soon as the other app lets go of it).
    /// All calls must be made on the thread that owns the window (the UI thread).
    /// </summary>
    public sealed class GlobalHotkeys : IDisposable
    {
        public const int ErrorHotkeyAlreadyRegistered = 1409;
        public const int WM_HOTKEY = 0x0312;
        private const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

        public enum Result { Ok, Off, Taken, Invalid, SameAsOther, NoWindow, Failed }

        /// <summary>One shortcut: the combo you picked (Wanted) and the one Windows has given Ace right now (Active).</summary>
        public sealed class Slot
        {
            public HotkeyAction Action { get; init; }
            public HotkeyCombo Wanted { get; internal set; }
            public HotkeyCombo Active { get; internal set; }
            internal int Id;
            /// <summary>The Windows error from the last failed try (1409 = another app has it), or 0.</summary>
            public int LastError { get; internal set; }
            public bool Working => Id != 0 && !Active.IsNone;
        }

        private readonly Dictionary<HotkeyAction, Slot> _slots = Enum.GetValues<HotkeyAction>().ToDictionary(a => a, a => new Slot { Action = a });
        private IntPtr _hwnd;
        private int _nextId = 0x4AC0;
        private bool _suspended, _disposed;
        private DispatcherTimer? _retry;

        /// <summary>A shortcut was pressed (on the UI thread).</summary>
        public event Action<HotkeyAction>? Pressed;
        /// <summary>A shortcut started or stopped working.</summary>
        public event Action? Changed;

        public bool Attached => _hwnd != IntPtr.Zero;
        public bool Suspended => _suspended;
        public Slot this[HotkeyAction action] => _slots[action];

        /// <summary>Hooks the shortcuts to Ace's window and turns on the ones you picked.</summary>
        public void Attach(IntPtr hwnd, IReadOnlyDictionary<HotkeyAction, HotkeyCombo> wanted)
        {
            if (hwnd == IntPtr.Zero || _disposed) return;
            if (_hwnd != IntPtr.Zero && _hwnd != hwnd) UnregisterAll();
            _hwnd = hwnd;
            foreach (var (action, combo) in wanted) _slots[action].Wanted = combo;
            foreach (var slot in _slots.Values) TryActivate(slot);
            _retry ??= new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(20) };
            _retry.Tick -= OnRetry;
            _retry.Tick += OnRetry;
            _retry.Start();
            Changed?.Invoke();
        }

        /// <summary>Call from the window's message hook. True if it was one of Ace's shortcuts.</summary>
        public bool HandleMessage(int msg, IntPtr wParam)
        {
            if (msg != WM_HOTKEY || _suspended) return false;
            int id = unchecked((int)wParam.ToInt64());
            foreach (var slot in _slots.Values)
            {
                if (slot.Id != 0 && slot.Id == id)
                {
                    Pressed?.Invoke(slot.Action);
                    return true;
                }
            }
            return false;
        }

        /// <summary>The window id Windows sends for this shortcut, or 0 if it isn't on (for tests).</summary>
        public int IdOf(HotkeyAction action) => _slots[action].Id;

        /// <summary>
        /// Changes a shortcut. The new combo is tried first and the old one is only let go once Windows says yes,
        /// so a taken combo never leaves you with no shortcut. HotkeyCombo.None turns it off.
        /// </summary>
        public Result Set(HotkeyAction action, HotkeyCombo combo, out string message)
        {
            var slot = _slots[action];
            if (_suspended) Resume();

            if (combo.IsNone)
            {
                Release(slot);
                slot.Wanted = HotkeyCombo.None;
                slot.LastError = 0;
                message = "Shortcut turned off.";
                Changed?.Invoke();
                return Result.Off;
            }
            string problem = combo.Problem();
            if (problem.Length > 0) { message = problem; return Result.Invalid; }
            foreach (var other in _slots.Values)
            {
                if (other != slot && other.Wanted == combo)
                {
                    message = $"{combo} is already Ace's shortcut to {Describe(other.Action)}. Pick a different combo.";
                    return Result.SameAsOther;
                }
            }
            if (slot.Working && slot.Active == combo)
            {
                slot.Wanted = combo;
                message = $"{combo} is already set.";
                return Result.Ok;
            }
            if (_hwnd == IntPtr.Zero)
            {
                slot.Wanted = combo;
                message = $"Saved. {combo} starts working when Ace's window opens.";
                Changed?.Invoke();
                return Result.NoWindow;
            }

            int id = NewId();
            if (!RegisterHotKey(_hwnd, id, combo.Modifiers | MOD_NOREPEAT, combo.Key))
            {
                int err = Marshal.GetLastWin32Error();
                string keep = slot.Working ? $" You still have {slot.Active}." : "";
                message = err == ErrorHotkeyAlreadyRegistered
                    ? $"{combo} is already used by another app (or by Windows), so Ace can't have it. Pick a different combo.{keep}"
                    : $"Windows wouldn't let Ace use {combo} (error {err}). Pick a different combo.{keep}";
                return err == ErrorHotkeyAlreadyRegistered ? Result.Taken : Result.Failed;
            }
            Release(slot);
            slot.Id = id;
            slot.Active = combo;
            slot.Wanted = combo;
            slot.LastError = 0;
            message = $"Done! Press {combo} anywhere in Windows to {Describe(action)}.";
            Changed?.Invoke();
            return Result.Ok;
        }

        /// <summary>Lets go of every shortcut for a moment (while you press a new combo in Settings, so the old one doesn't fire).</summary>
        public void Suspend()
        {
            if (_suspended) return;
            _suspended = true;
            foreach (var slot in _slots.Values)
            {
                if (slot.Id != 0) { try { UnregisterHotKey(_hwnd, slot.Id); } catch { } }
                slot.Id = 0;
            }
        }

        /// <summary>Turns the shortcuts back on after Suspend.</summary>
        public void Resume()
        {
            if (!_suspended) return;
            _suspended = false;
            foreach (var slot in _slots.Values) TryActivate(slot);
            Changed?.Invoke();
        }

        /// <summary>Plain words for what a shortcut does ("open Ace").</summary>
        public static string Describe(HotkeyAction action) => action switch
        {
            HotkeyAction.ShowAce => "open Ace",
            HotkeyAction.Talk => "talk to Ace",
            _ => action.ToString(),
        };

        /// <summary>One line for Settings: is the shortcut working, off, or taken by another app?</summary>
        public string StatusText(HotkeyAction action)
        {
            var slot = _slots[action];
            if (slot.Wanted.IsNone) return "Off.";
            if (slot.Working) return $"On. Press {slot.Active} anywhere in Windows to {Describe(action)}, even when Ace is minimized.";
            if (_suspended) return "Press the new combo…";
            if (_hwnd == IntPtr.Zero) return $"{slot.Wanted} starts working when Ace's window opens.";
            if (slot.LastError == -1) return $"{slot.Wanted} isn't a combo Ace can use. {slot.Wanted.Problem()}";
            if (slot.LastError == ErrorHotkeyAlreadyRegistered)
                return $"{slot.Wanted} isn't working: another app (or Windows) already uses it. Pick a different combo, or close that app and Ace will grab it.";
            return $"{slot.Wanted} isn't working (Windows error {slot.LastError}). Pick a different combo.";
        }

        private void OnRetry(object? sender, EventArgs e)
        {
            if (_suspended || _hwnd == IntPtr.Zero) return;
            bool changed = false;
            foreach (var slot in _slots.Values)
                if (!slot.Working && !slot.Wanted.IsNone && TryActivate(slot)) changed = true;
            if (changed) Changed?.Invoke();
        }

        private bool TryActivate(Slot slot)
        {
            if (_hwnd == IntPtr.Zero || _suspended || slot.Wanted.IsNone) return false;
            if (slot.Working && slot.Active == slot.Wanted) return true;
            if (slot.Wanted.Problem().Length > 0) { slot.LastError = -1; return false; }
            int id = NewId();
            if (RegisterHotKey(_hwnd, id, slot.Wanted.Modifiers | MOD_NOREPEAT, slot.Wanted.Key))
            {
                Release(slot);
                slot.Id = id;
                slot.Active = slot.Wanted;
                slot.LastError = 0;
                return true;
            }
            slot.LastError = Marshal.GetLastWin32Error();
            if (slot.Id == 0) slot.Active = HotkeyCombo.None;
            return false;
        }

        private void Release(Slot slot)
        {
            if (slot.Id != 0 && _hwnd != IntPtr.Zero) { try { UnregisterHotKey(_hwnd, slot.Id); } catch { } }
            slot.Id = 0;
            slot.Active = HotkeyCombo.None;
        }

        private void UnregisterAll()
        {
            foreach (var slot in _slots.Values) Release(slot);
        }

        private int NewId()
        {
            // RegisterHotKey ids for apps are 0x0000-0xBFFF. Each try gets a new id so the old combo can stay on until the new one works.
            _nextId++;
            if (_nextId > 0xBFFF) _nextId = 0x4AC1;
            return _nextId;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _retry?.Stop();
            UnregisterAll();
            _hwnd = IntPtr.Zero;
        }
    }
}
