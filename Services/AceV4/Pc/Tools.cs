using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nimbus.AceV4.Pc
{
    // ============================================================ Calculator

    /// <summary>Math from words or symbols: "what's 12 times 7", "15% of 80", "square root of 144", "(3+4)^2".</summary>
    public static class Calculator
    {
        /// <summary>Turns a spoken or typed question into a math expression, or null if it isn't math.</summary>
        public static string? ToExpression(string text)
        {
            string t = " " + text.ToLowerInvariant().Replace(",", "") + " ";
            t = Regex.Replace(t, @"\b(what\s*'?s|what\s+is|calculate|compute|solve|how\s+much\s+is|equals?|please|ace|hey|tell\s+me|the\s+answer\s+to|work\s+out)\b", " ");
            t = t.Replace("?", " ").Replace("=", " ");
            t = Regex.Replace(t, @"\b(the|now|then|okay|ok|so)\b", " ");
            t = Regex.Replace(t, @"\bdivide\s+(-?\d+(?:\.\d+)?)\s+by\b", "$1 /");
            t = Regex.Replace(t, @"\bmultiply\s+(-?\d+(?:\.\d+)?)\s+by\b", "$1 *");
            t = Regex.Replace(t, @"\bsubtract\s+(-?\d+(?:\.\d+)?)\s+from\s+(-?\d+(?:\.\d+)?)", "$2 - $1");
            t = Regex.Replace(t, @"\badd\s+(-?\d+(?:\.\d+)?)\s+to\s+(-?\d+(?:\.\d+)?)", "$2 + $1");
            // "15% of 80" / "15 percent of 80"
            t = Regex.Replace(t, @"(\d+(?:\.\d+)?)\s*(?:%|percent)\s+of\s+", "($1/100)*");
            t = Regex.Replace(t, @"(\d+(?:\.\d+)?)\s*(?:%|percent)", "($1/100)");
            t = Regex.Replace(t, @"\bsquare\s+root\s+of\s+(\d+(?:\.\d+)?)", "sqrt($1)");
            t = Regex.Replace(t, @"\bcube\s+root\s+of\s+(\d+(?:\.\d+)?)", "cbrt($1)");
            t = Regex.Replace(t, @"(\d+(?:\.\d+)?)\s+squared\b", "($1^2)");
            t = Regex.Replace(t, @"(\d+(?:\.\d+)?)\s+cubed\b", "($1^3)");
            t = Regex.Replace(t, @"\bto\s+the\s+power\s+of\b|\bto\s+the\b|\braised\s+to\b", "^");
            t = Regex.Replace(t, @"\b(plus|add|and)\b", "+");
            t = Regex.Replace(t, @"\b(minus|subtract|less)\b", "-");
            t = Regex.Replace(t, @"\b(times|multiplied\s+by|x)\b", "*");
            t = Regex.Replace(t, @"\b(divided\s+by|over)\b", "/");
            t = Regex.Replace(t, @"\b(mod|modulo)\b", "%");
            t = Regex.Replace(t, @"\bhalf\s+of\b", "0.5*");
            t = Regex.Replace(t, @"\bdouble\b", "2*");
            t = Regex.Replace(t, @"\bof\b", "*");
            t = t.Replace("×", "*").Replace("÷", "/").Replace("−", "-");
            t = Regex.Replace(t, @"\s+", " ").Trim();
            if (t.Length == 0) return null;
            // Only digits, operators, brackets and known functions may remain.
            string bare = Regex.Replace(t, @"\b(sqrt|cbrt|sin|cos|tan|log|ln|abs|round|floor|ceil|pi|e)\b", "");
            if (!Regex.IsMatch(bare, @"^[\d\s\.\+\-\*/\^%\(\)]+$")) return null;
            if (!Regex.IsMatch(t, @"\d")) return null;
            if (!Regex.IsMatch(t, @"[\+\-\*/\^%]|sqrt|cbrt|sin|cos|tan|log|ln|abs|round|floor|ceil")) return null;
            return t;
        }

        public static double? Evaluate(string expression)
        {
            try
            {
                var p = new Parser(expression);
                double v = p.Expr();
                if (!p.AtEnd) return null;
                return double.IsFinite(v) ? v : null;
            }
            catch { return null; }
        }

        public static string Format(double v)
        {
            if (Math.Abs(v - Math.Round(v)) < 1e-9 && Math.Abs(v) < 1e15) return Math.Round(v).ToString("#,0", CultureInfo.CurrentCulture);
            return v.ToString(Math.Abs(v) < 0.001 ? "0.######" : "#,0.####", CultureInfo.CurrentCulture);
        }

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;
            public Parser(string s) { _s = s.Replace(" ", ""); }
            public bool AtEnd => _i >= _s.Length;
            private char Peek => _i < _s.Length ? _s[_i] : '\0';

            public double Expr()
            {
                double v = Term();
                while (Peek is '+' or '-') { char op = _s[_i++]; double r = Term(); v = op == '+' ? v + r : v - r; }
                return v;
            }

            private double Term()
            {
                double v = Power();
                while (true)
                {
                    if (Peek is '*' or '/' or '%') { char op = _s[_i++]; double r = Power(); v = op == '*' ? v * r : op == '/' ? v / r : v % r; }
                    else if (Peek == '(' || char.IsLetter(Peek)) v *= Power();     // 2(3+4), 2pi
                    else return v;
                }
            }

            private double Power()
            {
                double b = Unary();
                if (Peek == '^') { _i++; return Math.Pow(b, Power()); }   // right to left: 2^3^2 = 2^9
                return b;
            }

            private double Unary()
            {
                if (Peek == '-') { _i++; return -Unary(); }
                if (Peek == '+') { _i++; return Unary(); }
                return Atom();
            }

            private double Atom()
            {
                if (Peek == '(') { _i++; double v = Expr(); if (Peek == ')') _i++; return v; }
                if (char.IsLetter(Peek))
                {
                    int start = _i;
                    while (char.IsLetter(Peek)) _i++;
                    string name = _s[start.._i];
                    if (name == "pi") return Math.PI;
                    if (name == "e") return Math.E;
                    double a = Atom();
                    return name switch
                    {
                        "sqrt" => Math.Sqrt(a),
                        "cbrt" => Math.Cbrt(a),
                        "sin" => Math.Sin(a * Math.PI / 180),
                        "cos" => Math.Cos(a * Math.PI / 180),
                        "tan" => Math.Tan(a * Math.PI / 180),
                        "log" => Math.Log10(a),
                        "ln" => Math.Log(a),
                        "abs" => Math.Abs(a),
                        "round" => Math.Round(a),
                        "floor" => Math.Floor(a),
                        "ceil" => Math.Ceiling(a),
                        _ => throw new FormatException(name),
                    };
                }
                int s = _i;
                while (char.IsDigit(Peek) || Peek == '.') _i++;
                if (s == _i) throw new FormatException("number expected");
                return double.Parse(_s[s.._i], CultureInfo.InvariantCulture);
            }
        }
    }

    // ============================================================ Units

    /// <summary>"5 miles in km", "72 f to c", "how many cups in a gallon", "3 gb in mb".</summary>
    public static class Units
    {
        private sealed record Unit(string Kind, double Factor, string Name);

        // Factor converts to the base unit of the kind (meter, gram, liter, second, byte, m/s, square meter).
        private static readonly Dictionary<string, Unit> Table = Build();

        private static Dictionary<string, Unit> Build()
        {
            var d = new Dictionary<string, Unit>(StringComparer.OrdinalIgnoreCase);
            void Add(string kind, double f, string name, params string[] names) { foreach (var n in names) d[n] = new Unit(kind, f, name); d[name] = new Unit(kind, f, name); }
            Add("length", 0.001, "millimeters", "mm", "millimeter", "millimetre", "millimetres");
            Add("length", 0.01, "centimeters", "cm", "centimeter", "centimetre", "centimetres");
            Add("length", 1, "meters", "m", "meter", "metre", "metres");
            Add("length", 1000, "kilometers", "km", "kilometer", "kilometre", "kilometres", "kms");
            Add("length", 0.0254, "inches", "in", "inch", "\"");
            Add("length", 0.3048, "feet", "ft", "foot", "'");
            Add("length", 0.9144, "yards", "yd", "yard", "yds");
            Add("length", 1609.344, "miles", "mi", "mile");
            Add("length", 1852, "nautical miles", "nautical mile", "nmi");
            Add("mass", 0.001, "milligrams", "mg", "milligram");
            Add("mass", 1, "grams", "g", "gram", "gr");
            Add("mass", 1000, "kilograms", "kg", "kilogram", "kilo", "kilos", "kgs");
            Add("mass", 28.349523125, "ounces", "oz", "ounce");
            Add("mass", 453.59237, "pounds", "lb", "lbs", "pound");
            Add("mass", 6350.29318, "stone", "st", "stones");
            Add("mass", 907184.74, "tons", "ton", "short ton");
            Add("mass", 1_000_000, "metric tons", "tonne", "tonnes", "metric ton");
            Add("volume", 0.001, "milliliters", "ml", "milliliter", "millilitre", "millilitres");
            Add("volume", 1, "liters", "l", "liter", "litre", "litres");
            Add("volume", 0.00492892, "teaspoons", "tsp", "teaspoon");
            Add("volume", 0.0147868, "tablespoons", "tbsp", "tablespoon");
            Add("volume", 0.0295735, "fluid ounces", "fl oz", "fluid ounce", "floz");
            Add("volume", 0.236588, "cups", "cup", "c");
            Add("volume", 0.473176, "pints", "pt", "pint");
            Add("volume", 0.946353, "quarts", "qt", "quart");
            Add("volume", 3.78541, "gallons", "gal", "gallon");
            Add("time", 0.001, "milliseconds", "ms", "millisecond");
            Add("time", 1, "seconds", "s", "sec", "secs", "second");
            Add("time", 60, "minutes", "min", "mins", "minute");
            Add("time", 3600, "hours", "h", "hr", "hrs", "hour");
            Add("time", 86400, "days", "day", "d");
            Add("time", 604800, "weeks", "week", "wk", "wks");
            Add("time", 2629746, "months", "month");
            Add("time", 31556952, "years", "year", "yr", "yrs");
            Add("data", 1, "bytes", "b", "byte");
            Add("data", 1024, "kilobytes", "kb", "kilobyte");
            Add("data", 1024 * 1024, "megabytes", "mb", "megabyte");
            Add("data", 1024d * 1024 * 1024, "gigabytes", "gb", "gig", "gigs", "gigabyte");
            Add("data", 1024d * 1024 * 1024 * 1024, "terabytes", "tb", "terabyte");
            Add("speed", 1, "meters per second", "m/s", "mps");
            Add("speed", 0.277778, "kilometers per hour", "km/h", "kph", "kmh");
            Add("speed", 0.44704, "miles per hour", "mph");
            Add("speed", 0.514444, "knots", "knot", "kn");
            Add("area", 1, "square meters", "m2", "sq m", "square meter");
            Add("area", 0.092903, "square feet", "sq ft", "ft2", "square foot");
            Add("area", 4046.86, "acres", "acre");
            Add("area", 10000, "hectares", "hectare", "ha");
            Add("area", 2589988.11, "square miles", "sq mi", "square mile");
            Add("area", 1_000_000, "square kilometers", "sq km", "km2", "square kilometer");
            foreach (var t in new[] { "f", "fahrenheit", "degrees f", "degrees fahrenheit", "°f" }) d[t] = new Unit("temp", 0, "°F");
            foreach (var t in new[] { "c", "celsius", "centigrade", "degrees c", "degrees celsius", "°c" }) d[t] = new Unit("temp", 1, "°C");
            foreach (var t in new[] { "k", "kelvin", "kelvins" }) d[t] = new Unit("temp", 2, "K");
            return d;
        }

        private static readonly string UnitPattern = string.Join("|", Table.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape));

        /// <summary>Returns "5 miles is 8.05 kilometers", or null if this isn't a conversion.</summary>
        public static string? Convert(string text)
        {
            string t = text.ToLowerInvariant().Replace("?", " ").Replace(",", "");
            t = Regex.Replace(t, @"\b(an?|one)\s+(?=(" + UnitPattern + @")\b)", "1 ");
            Match m = Regex.Match(t, @"(-?\d+(?:\.\d+)?)\s*(" + UnitPattern + @")\s+(?:to|in|into|as|equals|=|is how many|in how many)\s+(" + UnitPattern + @")\b");
            double amount;
            string from, to;
            if (m.Success) { amount = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); from = m.Groups[2].Value; to = m.Groups[3].Value; }
            else
            {
                // "how many cups in a gallon", "how many feet are in 3 miles"
                m = Regex.Match(t, @"how\s+many\s+(" + UnitPattern + @")\s+(?:are\s+)?(?:in|is|make|per)\s+(?:(-?\d+(?:\.\d+)?)\s*)?(" + UnitPattern + @")\b");
                if (!m.Success) return null;
                to = m.Groups[1].Value; from = m.Groups[3].Value;
                amount = m.Groups[2].Success ? double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 1;
            }
            if (!Table.TryGetValue(from.Trim(), out var a) || !Table.TryGetValue(to.Trim(), out var b) || a.Kind != b.Kind) return null;
            double result;
            if (a.Kind == "temp")
            {
                double c = a.Factor switch { 0 => (amount - 32) * 5 / 9, 2 => amount - 273.15, _ => amount };
                result = b.Factor switch { 0 => c * 9 / 5 + 32, 2 => c + 273.15, _ => c };
                return $"{Calculator.Format(Math.Round(amount, 2))}{a.Name} is {Calculator.Format(Math.Round(result, 1))}{b.Name}.";
            }
            result = amount * a.Factor / b.Factor;
            string shown = Math.Abs(result) >= 100 ? Calculator.Format(Math.Round(result, 1)) : Math.Abs(result) >= 1 ? Calculator.Format(Math.Round(result, 2)) : Calculator.Format(Math.Round(result, 4));
            return $"{Calculator.Format(amount)} {(amount == 1 ? Single(a.Name) : a.Name)} is {shown} {(Math.Abs(result - 1) < 1e-9 ? Single(b.Name) : b.Name)}.";
        }

        /// <summary>
        /// "convert 10 kilograms" or "5 miles in all units": the amount in every other unit of that kind.
        /// Returns null if this isn't that kind of question.
        /// </summary>
        public static (string Amount, string From, List<(string Value, string Unit)> Rows)? ConvertAll(string text)
        {
            string t = text.ToLowerInvariant().Replace("?", " ").Replace(",", "").Trim().TrimEnd('.', '!', ' ');
            t = Regex.Replace(t, @"\b(an?|one)\s+(?=(" + UnitPattern + @")\b)", "1 ");
            var m = Regex.Match(t, @"^(?:please\s+)?(?:convert|change|turn)\s+(-?\d+(?:\.\d+)?)\s*(" + UnitPattern + @")(?:\s+(?:to|into|in)\s+(?:everything|all(?: the)?(?: other)? units|other units|all|every(?: other)? unit|anything))?$"
                                 + @"|^(-?\d+(?:\.\d+)?)\s*(" + UnitPattern + @")\s+(?:to|into|in)\s+(?:everything|all(?: the)?(?: other)? units|other units|every(?: other)? unit)$");
            if (!m.Success) return null;
            string num = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[3].Value;
            string unit = (m.Groups[2].Success ? m.Groups[2].Value : m.Groups[4].Value).Trim();
            if (!Table.TryGetValue(unit, out var a)) return null;
            double amount = double.Parse(num, CultureInfo.InvariantCulture);
            var rows = new List<(string Value, string Unit)>();
            var seen = new HashSet<string> { a.Name };
            foreach (var b in Table.Values.Where(u => u.Kind == a.Kind))
            {
                if (!seen.Add(b.Name)) continue;
                double result;
                if (a.Kind == "temp")
                {
                    double c = a.Factor switch { 0 => (amount - 32) * 5 / 9, 2 => amount - 273.15, _ => amount };
                    result = b.Factor switch { 0 => c * 9 / 5 + 32, 2 => c + 273.15, _ => c };
                    rows.Add((Calculator.Format(Math.Round(result, 1)), b.Name));
                    continue;
                }
                result = amount * a.Factor / b.Factor;
                if (Math.Abs(result) < 0.0001 || Math.Abs(result) > 1e12) continue;   // too tiny or huge to be useful
                double shown = Math.Abs(result) >= 100 ? Math.Round(result, 1) : Math.Abs(result) >= 1 ? Math.Round(result, 2) : Math.Round(result, 5);
                rows.Add((Calculator.Format(shown), Math.Abs(result - 1) < 1e-9 ? Single(b.Name) : b.Name));
            }
            if (rows.Count == 0) return null;
            // Most useful first: the ones closest to 1 to 1000 in size.
            rows = rows.OrderBy(r => double.TryParse(r.Value, NumberStyles.Any, CultureInfo.CurrentCulture, out var v) ? Math.Abs(Math.Log10(Math.Max(Math.Abs(v), 1e-9)) - 1.2) : 9).ToList();
            string from = a.Kind == "temp" ? a.Name : (amount == 1 ? Single(a.Name) : a.Name);
            return (Calculator.Format(amount), from, rows);
        }

        private static string Single(string name) => name switch
        {
            "feet" => "foot",
            "inches" => "inch",
            "square feet" => "square foot",
            "stone" => "stone",
            "knots" => "knot",
            _ when name.EndsWith("s") && !name.Contains(" per ") => name[..^1],
            _ => name,
        };
    }

    // ============================================================ Reminders and timers

    public sealed class Reminder
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Text { get; set; } = "";
        public DateTime Due { get; set; }
        public bool IsTimer { get; set; }
        public bool Done { get; set; }
    }

    /// <summary>Timers and reminders, saved so they survive a restart. AceRuntime checks them every few seconds.</summary>
    public static class ReminderStore
    {
        private static string FilePath => Path.Combine(AceV4Config.Folder, "reminders.json");
        private static readonly object Gate = new();
        private static List<Reminder>? _all;
        public static event Action? Changed;

        private static List<Reminder> All
        {
            get
            {
                if (_all != null) return _all;
                try { if (File.Exists(FilePath)) _all = JsonSerializer.Deserialize<List<Reminder>>(File.ReadAllText(FilePath)); } catch { }
                _all ??= new List<Reminder>();
                return _all;
            }
        }

        public static Reminder Add(string text, DateTime due, bool isTimer)
        {
            var r = new Reminder { Text = text, Due = due, IsTimer = isTimer };
            lock (Gate) { All.Add(r); Save(); }
            Changed?.Invoke();
            return r;
        }

        public static List<Reminder> Pending()
        {
            lock (Gate) return All.Where(r => !r.Done).OrderBy(r => r.Due).ToList();
        }

        /// <summary>Reminders that are due now. Each is returned once.</summary>
        public static List<Reminder> TakeDue(DateTime now)
        {
            List<Reminder> due;
            lock (Gate)
            {
                due = All.Where(r => !r.Done && r.Due <= now).ToList();
                if (due.Count == 0) return due;
                foreach (var r in due) r.Done = true;
                All.RemoveAll(r => r.Done && r.Due < now.AddDays(-2));
                Save();
            }
            Changed?.Invoke();
            return due;
        }

        public static int CancelTimers()
        {
            int n;
            lock (Gate) { n = All.RemoveAll(r => r.IsTimer && !r.Done); Save(); }
            Changed?.Invoke();
            return n;
        }

        public static bool Cancel(string id)
        {
            bool ok;
            lock (Gate) { ok = All.RemoveAll(r => r.Id == id) > 0; Save(); }
            Changed?.Invoke();
            return ok;
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(AceV4Config.Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(All));
            }
            catch { }
        }
    }

    // ============================================================ Media, volume and screenshots

    public static class Media
    {
        public static bool PlayPause() => Win32.Chord(Win32.VK_MEDIA_PLAY_PAUSE);
        public static bool Next() => Win32.Chord(Win32.VK_MEDIA_NEXT_TRACK);
        public static bool Previous() => Win32.Chord(Win32.VK_MEDIA_PREV_TRACK);
        public static bool Stop() => Win32.Chord(Win32.VK_MEDIA_STOP);

        /// <summary>Win + Print Screen: Windows saves it in Pictures\Screenshots.</summary>
        public static bool Screenshot() => Win32.Chord(Win32.VK_LWIN, Win32.VK_SNAPSHOT);

        /// <summary>Opens the Snipping Tool's selection (Win + Shift + S).</summary>
        public static bool Snip() => Win32.Chord(Win32.VK_LWIN, Win32.VK_SHIFT, 0x53);

        public static int? GetVolume()
        {
            try { using var v = new SpeakerVolume(); return (int)Math.Round(v.Level * 100); } catch { return null; }
        }

        public static bool SetVolume(int percent)
        {
            try { using var v = new SpeakerVolume(); v.Level = Math.Clamp(percent, 0, 100) / 100f; if (percent > 0) v.Muted = false; return true; }
            catch { return false; }
        }

        public static bool Change(int byPercent)
        {
            var now = GetVolume();
            if (now == null)
            {
                // Couldn't reach the speaker's volume: fall back to the volume keys (2% each).
                for (int i = 0; i < Math.Abs(byPercent) / 2; i++) Win32.Chord(byPercent > 0 ? Win32.VK_VOLUME_UP : Win32.VK_VOLUME_DOWN);
                return true;
            }
            return SetVolume(now.Value + byPercent);
        }

        public static bool SetMute(bool mute)
        {
            try { using var v = new SpeakerVolume(); v.Muted = mute; return true; }
            catch { return Win32.Chord(Win32.VK_VOLUME_MUTE); }
        }

        public static bool? IsMuted()
        {
            try { using var v = new SpeakerVolume(); return v.Muted; } catch { return null; }
        }
    }

    /// <summary>The default speaker's master volume, through Windows' Core Audio.</summary>
    internal sealed class SpeakerVolume : IDisposable
    {
        private readonly IAudioEndpointVolume _vol;
        private readonly IMMDevice _device;
        private readonly IMMDeviceEnumerator _enum;

        public SpeakerVolume()
        {
            _enum = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            Marshal.ThrowExceptionForHR(_enum.GetDefaultAudioEndpoint(0 /* render */, 1 /* multimedia */, out _device));
            var iid = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(_device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out object o));
            _vol = (IAudioEndpointVolume)o;
        }

        public float Level
        {
            get { Marshal.ThrowExceptionForHR(_vol.GetMasterVolumeLevelScalar(out float v)); return v; }
            set { var g = Guid.Empty; Marshal.ThrowExceptionForHR(_vol.SetMasterVolumeLevelScalar(Math.Clamp(value, 0f, 1f), ref g)); }
        }

        public bool Muted
        {
            get { Marshal.ThrowExceptionForHR(_vol.GetMute(out bool m)); return m; }
            set { var g = Guid.Empty; Marshal.ThrowExceptionForHR(_vol.SetMute(value, ref g)); }
        }

        public void Dispose()
        {
            try { Marshal.ReleaseComObject(_vol); } catch { }
            try { Marshal.ReleaseComObject(_device); } catch { }
            try { Marshal.ReleaseComObject(_enum); } catch { }
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorCom { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int GetChannelCount(out int count);
            [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
            [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
            [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }
    }
}
