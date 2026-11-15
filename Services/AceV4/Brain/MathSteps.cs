using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Nimbus.AceV4.Brain
{
    /// <summary>An exact fraction (3/4), so homework answers come out as 5/6 instead of 0.8333.</summary>
    public readonly struct Frac : IEquatable<Frac>
    {
        public readonly long Num, Den;

        public Frac(long num, long den = 1)
        {
            if (den == 0) throw new DivideByZeroException("You can't divide by zero.");
            if (den < 0) { num = -num; den = -den; }
            long g = Gcd(Math.Abs(num), den);
            if (g > 1) { num /= g; den /= g; }
            Num = num; Den = den;
        }

        public static long Gcd(long a, long b) { a = Math.Abs(a); b = Math.Abs(b); while (b != 0) (a, b) = (b, a % b); return a == 0 ? 1 : a; }

        public static Frac FromDecimal(string s)
        {
            s = s.Trim();
            if (!s.Contains('.')) return new Frac(long.Parse(s, CultureInfo.InvariantCulture));
            var parts = s.Split('.');
            string frac = parts[1].Length > 9 ? parts[1][..9] : parts[1];
            long den = (long)Math.Pow(10, frac.Length);
            long whole = parts[0].Length == 0 ? 0 : long.Parse(parts[0], CultureInfo.InvariantCulture);
            return new Frac(checked(whole * den + (frac.Length == 0 ? 0 : long.Parse(frac, CultureInfo.InvariantCulture))), den);
        }

        public bool IsZero => Num == 0;
        public bool IsWhole => Den == 1;
        public double Value => (double)Num / Den;

        public static Frac operator +(Frac a, Frac b) => new(checked(a.Num * b.Den + b.Num * a.Den), checked(a.Den * b.Den));
        public static Frac operator -(Frac a, Frac b) => new(checked(a.Num * b.Den - b.Num * a.Den), checked(a.Den * b.Den));
        public static Frac operator *(Frac a, Frac b) => new(checked(a.Num * b.Num), checked(a.Den * b.Den));
        public static Frac operator /(Frac a, Frac b) => b.Num == 0 ? throw new DivideByZeroException("You can't divide by zero.") : new Frac(checked(a.Num * b.Den), checked(a.Den * b.Num));
        public static Frac operator -(Frac a) => new(-a.Num, a.Den);
        public static bool operator ==(Frac a, Frac b) => a.Num == b.Num && a.Den == b.Den;
        public static bool operator !=(Frac a, Frac b) => !(a == b);
        public bool Equals(Frac o) => this == o;
        public override bool Equals(object? obj) => obj is Frac f && f == this;
        public override int GetHashCode() => HashCode.Combine(Num, Den);

        public static Frac Pow(Frac a, Frac b)
        {
            if (!b.IsWhole || Math.Abs(b.Num) > 30) throw new InvalidOperationException("That power is too tricky for exact steps.");
            var r = new Frac(1);
            for (int i = 0; i < Math.Abs(b.Num); i++) r *= a;
            return b.Num < 0 ? new Frac(1) / r : r;
        }

        /// <summary>"3", "-5/6".</summary>
        public override string ToString() => Den == 1 ? Num.ToString(CultureInfo.InvariantCulture) : $"{Num}/{Den}";

        /// <summary>"5/6 (about 0.83)" or "3".</summary>
        public string Friendly()
        {
            if (Den == 1) return ToString();
            double v = Value;
            string dec = Math.Round(v, 4).ToString("0.####", CultureInfo.InvariantCulture);
            bool exact = Math.Abs(double.Parse(dec, CultureInfo.InvariantCulture) - v) < 1e-12;
            return $"{this} ({(exact ? "" : "about ")}{dec})";
        }
    }

    /// <summary>The result of a worked homework problem.</summary>
    public sealed class MathWork
    {
        public string Title = "";
        public string Problem = "";
        public List<string> Steps = new();
        public string Answer = "";
        /// <summary>The sentence Ace says ("x = 4.").</summary>
        public string Say = "";
    }

    /// <summary>
    /// The homework helper. Solves problems step by step the way a teacher would show them:
    /// one-variable equations (5x - 7 = 2x + 8), order of operations (3 + 4 × 2), fractions (1/2 + 1/3),
    /// percents, long division, factors (GCF, LCM, primes) and geometry (area, perimeter, Pythagoras).
    /// Everything is worked out exactly; nothing is looked up.
    /// </summary>
    public static class MathSteps
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>True when the request is a homework problem that should get steps (not just "what's 3 + 4").</summary>
        public static bool Wants(string text)
        {
            string l = Clean(text.ToLowerInvariant());
            if (Regex.IsMatch(l, @"\bon (my|the) screen\b")) return false;
            if (Regex.IsMatch(l, @"\b(step by step|show (me )?(the |your )?(steps|work|working)|steps for|explain how|how (do|would|can|should) (i|you|we) (solve|do|work out|figure out|find|simplify|calculate)|work it out|help me (solve|with)|solve)\b") && Regex.IsMatch(l, @"\d")) return true;
            if (IsEquation(l)) return true;
            if (FractionPair(l) != null) return true;
            if (Regex.IsMatch(l, @"\b(gcf|gcd|hcf|lcm|lcd|greatest common (factor|divisor)|highest common factor|least common (multiple|denominator)|lowest common (multiple|denominator)|prime factori[sz]ation|prime factors|factors of|is \d+ (a )?prime|hypotenuse|pythag|area of|perimeter of|circumference of|volume of)\b") && Regex.IsMatch(l, @"\d")) return true;
            if (Regex.IsMatch(l, @"\bis what (percent|%) of\b|\bwhat (percent|%) of [\d.]+ is\b")) return true;
            return false;
        }

        private static bool IsEquation(string l) =>
            l.Contains('=') && Regex.IsMatch(Regex.Replace(l, @"\b(solve|for|what|is|find|equals?)\b", " "), @"(?<![a-z])[a-z](?![a-z])");

        private static string Clean(string l)
        {
            l = l.Replace('×', '*').Replace('÷', '/').Replace('−', '-').Replace('–', '-').Replace("²", "^2").Replace("³", "^3");
            l = Regex.Replace(l, @"\bmultiplied by\b|\btimes\b", "*");
            l = Regex.Replace(l, @"\bdivided by\b|\bover\b", "/");
            l = Regex.Replace(l, @"\bplus\b|\badded to\b", "+");
            l = Regex.Replace(l, @"\bminus\b|\btake away\b", "-");
            l = Regex.Replace(l, @"\bequals\b|\bis equal to\b", "=");
            l = Regex.Replace(l, @"\bsquared\b", "^2");
            l = Regex.Replace(l, @"\bcubed\b", "^3");
            l = Regex.Replace(l, @"\bto the power of\b", "^");
            return Regex.Replace(l, @"\s+", " ").Trim();
        }

        /// <summary>Works the problem out, or null if it isn't one Ace can do step by step.</summary>
        public static MathWork? Solve(string text)
        {
            string raw = text.Trim();
            string l = Clean(raw.ToLowerInvariant());
            try
            {
                return Percent(l) ?? Factors(l) ?? Geometry(l) ?? (IsEquation(l) ? Equation(l) : null) ?? FractionWork(l) ?? LongDivision(l) ?? Arithmetic(l);
            }
            catch (DivideByZeroException) { return new MathWork { Title = "Uh oh", Steps = { "That divides by zero, which can't be done." }, Answer = "undefined", Say = "That divides by zero, so there's no answer. Dividing by zero is undefined." }; }
            catch (OverflowException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (FormatException) { return null; }
        }

        // ===================================================================== Equations

        private readonly struct Lin
        {
            public readonly Frac A, B;   // A·x + B
            public Lin(Frac a, Frac b) { A = a; B = b; }
            public bool Const => A.IsZero;
        }

        private sealed class Parser
        {
            private readonly List<string> _t;
            private int _i;
            private readonly char _var;
            public Parser(string s, char var) { _var = var; _t = Tokens(s, var); }

            public static List<string> Tokens(string s, char var)
            {
                var list = new List<string>();
                int i = 0;
                while (i < s.Length)
                {
                    char c = s[i];
                    if (char.IsWhiteSpace(c)) { i++; continue; }
                    if (char.IsDigit(c) || c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1]))
                    {
                        int j = i;
                        while (j < s.Length && (char.IsDigit(s[j]) || s[j] == '.' || s[j] == ',' && j + 3 < s.Length && char.IsDigit(s[j + 1]))) j++;
                        list.Add(s[i..j].Replace(",", ""));
                        i = j;
                        continue;
                    }
                    if (c == var) { list.Add("x"); i++; continue; }
                    if ("+-*/^()".IndexOf(c) >= 0) { list.Add(c.ToString()); i++; continue; }
                    throw new FormatException("Unexpected " + c);
                }
                // Implicit multiplication: 2x, 3(x+1), (x+1)(x-1), x(2).
                var withTimes = new List<string>();
                for (int k = 0; k < list.Count; k++)
                {
                    if (k > 0)
                    {
                        string a = list[k - 1], b = list[k];
                        bool aEnds = a == ")" || a == "x" || char.IsDigit(a[0]) || a[0] == '.';
                        bool bStarts = b == "(" || b == "x" || char.IsDigit(b[0]) || b[0] == '.';
                        if (aEnds && bStarts) withTimes.Add("*");
                    }
                    withTimes.Add(list[k]);
                }
                return withTimes;
            }

            private string? Peek => _i < _t.Count ? _t[_i] : null;
            public bool AtEnd => _i >= _t.Count;

            public Lin Expr()
            {
                var v = Term();
                while (Peek is "+" or "-")
                {
                    string op = _t[_i++];
                    var r = Term();
                    v = op == "+" ? new Lin(v.A + r.A, v.B + r.B) : new Lin(v.A - r.A, v.B - r.B);
                }
                return v;
            }

            private Lin Term()
            {
                var v = Unary();
                while (Peek is "*" or "/")
                {
                    string op = _t[_i++];
                    var r = Unary();
                    if (op == "*")
                    {
                        if (v.Const) v = new Lin(r.A * v.B, r.B * v.B);
                        else if (r.Const) v = new Lin(v.A * r.B, v.B * r.B);
                        else throw new InvalidOperationException("x times x isn't a one-step equation.");
                    }
                    else
                    {
                        if (!r.Const) throw new InvalidOperationException("Dividing by x isn't supported.");
                        v = new Lin(v.A / r.B, v.B / r.B);
                    }
                }
                return v;
            }

            private Lin Unary()
            {
                if (Peek == "-") { _i++; var v = Unary(); return new Lin(-v.A, -v.B); }
                if (Peek == "+") { _i++; return Unary(); }
                return Power();
            }

            private Lin Power()
            {
                var v = Atom();
                if (Peek == "^")
                {
                    _i++;
                    var e = Unary();
                    if (!v.Const || !e.Const) throw new InvalidOperationException("Powers of x aren't supported.");
                    v = new Lin(new Frac(0), Frac.Pow(v.B, e.B));
                }
                return v;
            }

            private Lin Atom()
            {
                string? t = Peek ?? throw new FormatException("Ended early");
                _i++;
                if (t == "(")
                {
                    var v = Expr();
                    if (Peek != ")") throw new FormatException("Missing )");
                    _i++;
                    return v;
                }
                if (t == "x") return new Lin(new Frac(1), new Frac(0));
                if (char.IsDigit(t[0]) || t[0] == '.') return new Lin(new Frac(0), Frac.FromDecimal(t.StartsWith('.') ? "0" + t : t));
                throw new FormatException("Unexpected " + t);
            }
        }

        private static Lin Parse(string side, char var)
        {
            var p = new Parser(side, var);
            var v = p.Expr();
            if (!p.AtEnd) throw new FormatException("Extra stuff");
            return v;
        }

        private static string LinText(Lin v, char var)
        {
            var sb = new StringBuilder();
            if (!v.A.IsZero)
            {
                if (v.A == new Frac(1)) sb.Append(var);
                else if (v.A == new Frac(-1)) sb.Append('-').Append(var);
                else if (v.A.IsWhole) sb.Append(v.A).Append(var);
                else sb.Append('(').Append(v.A).Append(')').Append(var);
            }
            if (!v.B.IsZero || v.A.IsZero)
            {
                if (sb.Length == 0) sb.Append(v.B);
                else sb.Append(v.B.Num < 0 ? " - " : " + ").Append(v.B.Num < 0 ? (-v.B).ToString() : v.B.ToString());
            }
            return sb.ToString();
        }

        private static string Term(Frac a, char var) => a == new Frac(1) ? var.ToString() : a.IsWhole ? a + var.ToString() : "(" + a + ")" + var;

        private static MathWork? Equation(string l)
        {
            // Take just the math part: "solve 2x + 3 = 11 for x please" → "2x + 3 = 11".
            var m = Regex.Match(l, @"([0-9a-z.()+\-*/^ ]*?=[0-9a-z.()+\-*/^ ]*)");
            if (!m.Success) return null;
            string eq = m.Groups[1].Value;
            eq = Regex.Replace(eq, @"^\s*(?:(?:can|could) you |please |help me |how (?:do|would|can) (?:i|you|we) )*(?:solve|find|work out|figure out|calculate)?\s*(?:for [a-z]\s*:?)?\s*(?:if\s+)?", "");
            eq = Regex.Replace(eq, @"\s+(?:for|step|steps|please|and|show|then)\b.*$", "");
            eq = Regex.Replace(eq, @"\b(what|is|the|value|of)\b", " ").Trim();
            var sides = eq.Split('=');
            if (sides.Length != 2 || sides[0].Trim().Length == 0 || sides[1].Trim().Length == 0) return null;
            var vars = Regex.Matches(eq, @"(?<![a-z])[a-z](?![a-z])").Select(x => x.Value[0]).Distinct().ToList();
            if (vars.Count != 1) return null;
            char v = vars[0];
            var L = Parse(sides[0], v);
            var R = Parse(sides[1], v);
            string original = Pretty(sides[0]) + " = " + Pretty(sides[1]);

            var w = new MathWork { Title = "Solve for " + v, Problem = original };
            w.Steps.Add("Start with: " + original);
            string simple = LinText(L, v) + " = " + LinText(R, v);
            if (simple.Replace(" ", "") != original.Replace(" ", "").Replace("*", "")) w.Steps.Add("Simplify each side (multiply out brackets, combine like terms): " + simple);

            if (!R.A.IsZero)
            {
                var a2 = R.A;
                L = new Lin(L.A - a2, L.B);
                R = new Lin(new Frac(0), R.B);
                w.Steps.Add((a2.Num > 0 ? "Subtract " + Term(a2, v) : "Add " + Term(-a2, v)) + $" on both sides so the {v}'s are together on the left: " + LinText(L, v) + " = " + LinText(R, v));
            }
            if (!L.B.IsZero)
            {
                var b1 = L.B;
                L = new Lin(L.A, new Frac(0));
                R = new Lin(R.A, R.B - b1);
                w.Steps.Add((b1.Num > 0 ? "Subtract " + b1 : "Add " + (-b1)) + " on both sides so the numbers are on the right: " + LinText(L, v) + " = " + LinText(R, v));
            }
            if (L.A.IsZero)
            {
                bool always = R.B.IsZero;
                w.Steps.Add(always ? "Both sides are always equal." : "That says " + LinText(L, v) + " = " + R.B + ", which is never true.");
                w.Answer = always ? $"any number works for {v}" : "no solution";
                w.Say = always ? $"Every value of {v} works. Both sides are always the same." : "There's no solution. The two sides can never be equal.";
                return w;
            }
            var x = R.B / L.A;
            if (L.A != new Frac(1))
                w.Steps.Add((L.A.IsWhole ? "Divide both sides by " + L.A : "Divide both sides by " + L.A + " (same as multiplying by " + new Frac(L.A.Den, L.A.Num) + ")") + $": {v} = " + x.Friendly());
            // Check it.
            var check = Regex.Replace(sides[0].Trim(), @"(?<![a-z])" + v + @"(?![a-z])", "(" + x + ")");
            var lhs = Parse(sides[0], v);
            var rhs = Parse(sides[1], v);
            var leftVal = lhs.A * x + lhs.B;
            var rightVal = rhs.A * x + rhs.B;
            w.Steps.Add($"Check: put {v} = {x} back in. Left side = {leftVal}, right side = {rightVal}. " + (leftVal == rightVal ? "They match! ✓" : "Hmm, they don't match."));
            w.Answer = $"{v} = {x.Friendly()}";
            w.Say = $"{v} = {x.Friendly()}.";
            return w;
        }

        private static string Pretty(string s)
        {
            s = Regex.Replace(s.Trim(), @"\s*([+\-=])\s*", " $1 ");
            s = Regex.Replace(s, @"\s*([*/^])\s*", "$1");
            s = Regex.Replace(s, @"\(\s+", "(");
            s = Regex.Replace(s, @"\s+\)", ")");
            s = Regex.Replace(s, @"^\s*-\s+", "-");
            s = Regex.Replace(s, @"\(\s*-\s+", "(-");
            return Regex.Replace(s, @"\s+", " ").Trim().Replace("*", " × ");
        }

        // ===================================================================== Fractions

        private static (Frac A, string Op, Frac B)? FractionPair(string l)
        {
            var m = Regex.Match(l, @"(-?\d+)\s*/\s*(\d+)\s*(\+|-|\*|/|of)\s*(-?\d+)\s*/\s*(\d+)");
            if (!m.Success) return null;
            // Not inside a bigger expression ("1/2 + 1/3 + 5").
            string rest = l.Remove(m.Index, m.Length);
            if (Regex.IsMatch(rest, @"[\d=]")) return null;
            long d1 = long.Parse(m.Groups[2].Value), d2 = long.Parse(m.Groups[5].Value);
            if (d1 == 0 || d2 == 0) return null;
            return (new Frac(long.Parse(m.Groups[1].Value), d1), m.Groups[3].Value, new Frac(long.Parse(m.Groups[4].Value), d2));
        }

        private static MathWork? FractionWork(string l)
        {
            var m = Regex.Match(l, @"(-?\d+)\s*/\s*(\d+)\s*(\+|-|\*|/|of)\s*(-?\d+)\s*/\s*(\d+)");
            if (!m.Success || FractionPair(l) == null) return null;
            long n1 = long.Parse(m.Groups[1].Value), d1 = long.Parse(m.Groups[2].Value), n2 = long.Parse(m.Groups[4].Value), d2 = long.Parse(m.Groups[5].Value);
            string op = m.Groups[3].Value;
            var w = new MathWork { Title = "Fractions", Problem = $"{n1}/{d1} {Sym(op)} {n2}/{d2}" };
            Frac result;
            if (op is "+" or "-")
            {
                long lcd = d1 / Frac.Gcd(d1, d2) * d2;
                long a = n1 * (lcd / d1), b = n2 * (lcd / d2);
                if (d1 == d2) w.Steps.Add($"The bottoms are already the same ({d1}), so just {(op == "+" ? "add" : "subtract")} the tops.");
                else
                {
                    w.Steps.Add($"Find a common bottom number. The smallest one that {d1} and {d2} both go into is {lcd}.");
                    w.Steps.Add($"Change both fractions: {n1}/{d1} = {a}/{lcd} and {n2}/{d2} = {b}/{lcd}.");
                }
                long top = op == "+" ? a + b : a - b;
                w.Steps.Add($"{(op == "+" ? "Add" : "Subtract")} the tops: {a} {op} {b} = {top}, so it's {top}/{lcd}.");
                result = new Frac(top, lcd);
                if (result.Den != lcd || result.Num != top) w.Steps.Add($"Simplify: divide the top and bottom by {Frac.Gcd(top, lcd)} to get {result}.");
            }
            else if (op is "*" or "of")
            {
                w.Steps.Add($"Multiply the tops: {n1} × {n2} = {n1 * n2}.");
                w.Steps.Add($"Multiply the bottoms: {d1} × {d2} = {d1 * d2}.");
                result = new Frac(n1 * n2, d1 * d2);
                w.Steps.Add(result.Num == n1 * n2 && result.Den == d1 * d2 ? $"That's {result}, and it can't be simplified." : $"Simplify {n1 * n2}/{d1 * d2}: divide the top and bottom by {Frac.Gcd(n1 * n2, d1 * d2)} to get {result}.");
            }
            else
            {
                if (n2 == 0) throw new DivideByZeroException();
                w.Steps.Add($"Dividing by a fraction is the same as multiplying by it flipped. Flip {n2}/{d2} to get {d2}/{n2}.");
                w.Steps.Add($"Now multiply: {n1}/{d1} × {d2}/{n2} = {n1 * d2}/{d1 * n2}.");
                result = new Frac(n1 * d2, d1 * n2);
                if (result.Num != n1 * d2 || result.Den != d1 * n2) w.Steps.Add($"Simplify: divide the top and bottom by {Frac.Gcd(n1 * d2, d1 * n2)} to get {result}.");
            }
            if (!result.IsWhole && Math.Abs(result.Num) > result.Den)
                w.Steps.Add($"As a mixed number that's {MixedNumber(result)}.");
            w.Answer = result.Friendly();
            w.Say = $"{w.Problem} = {result}.";
            return w;
        }

        private static string MixedNumber(Frac f)
        {
            long whole = f.Num / f.Den, rest = Math.Abs(f.Num % f.Den);
            return rest == 0 ? whole.ToString() : $"{whole} {rest}/{f.Den}";
        }

        private static string Sym(string op) => op switch { "*" => "×", "/" => "÷", "of" => "×", _ => op };

        // ===================================================================== Percent

        private static MathWork? Percent(string l)
        {
            var whatPct = Regex.Match(l, @"([\d.]+) is what (?:percent|%) of ([\d.]+)");
            var whatPct2 = Regex.Match(l, @"what (?:percent|%) of ([\d.]+) is ([\d.]+)");
            if (whatPct.Success || whatPct2.Success)
            {
                double part = double.Parse(whatPct.Success ? whatPct.Groups[1].Value : whatPct2.Groups[2].Value, Inv);
                double whole = double.Parse(whatPct.Success ? whatPct.Groups[2].Value : whatPct2.Groups[1].Value, Inv);
                if (whole == 0) throw new DivideByZeroException();
                double frac = part / whole, pct = frac * 100;
                var w = new MathWork { Title = "Percent", Problem = $"{N(part)} is what percent of {N(whole)}?" };
                w.Steps.Add($"Divide the part by the whole: {N(part)} ÷ {N(whole)} = {N(frac)}.");
                w.Steps.Add($"Multiply by 100 to turn it into a percent: {N(frac)} × 100 = {N(pct)}%.");
                w.Answer = N(pct) + "%";
                w.Say = $"{N(part)} is {N(pct)}% of {N(whole)}.";
                return w;
            }
            var of = Regex.Match(l, @"([\d.]+)\s*(?:%|percent) of ([\d.]+)");
            if (of.Success && Regex.IsMatch(l, @"\bstep|\bshow|\bexplain|\bhow (do|would|can)|\bsolve|\bwork"))
            {
                double p = double.Parse(of.Groups[1].Value, Inv), x = double.Parse(of.Groups[2].Value, Inv);
                double d = p / 100, r = d * x;
                var w = new MathWork { Title = "Percent", Problem = $"{N(p)}% of {N(x)}" };
                w.Steps.Add($"\"Percent\" means \"out of 100\", so {N(p)}% = {N(p)} ÷ 100 = {N(d)}.");
                w.Steps.Add($"\"Of\" means multiply: {N(d)} × {N(x)} = {N(r)}.");
                w.Answer = N(r);
                w.Say = $"{N(p)}% of {N(x)} is {N(r)}.";
                return w;
            }
            return null;
        }

        private static string N(double v) => Math.Round(v, 4).ToString("#,0.####", Inv);

        // ===================================================================== Factors and primes

        private static MathWork? Factors(string l)
        {
            var nums = Regex.Matches(l, @"\d+").Select(m => long.Parse(m.Value)).ToList();
            if (Regex.IsMatch(l, @"\b(gcf|gcd|hcf|greatest common (factor|divisor)|highest common factor)\b") && nums.Count >= 2)
            {
                long a = nums[0], b = nums[1];
                if (a == 0 || b == 0 || a > 100000 || b > 100000) return null;
                var fa = Divisors(a); var fb = Divisors(b);
                var common = fa.Intersect(fb).ToList();
                var w = new MathWork { Title = "Greatest common factor", Problem = $"GCF of {a} and {b}" };
                w.Steps.Add($"Factors of {a}: {string.Join(", ", fa.Take(24))}{(fa.Count > 24 ? "…" : "")}");
                w.Steps.Add($"Factors of {b}: {string.Join(", ", fb.Take(24))}{(fb.Count > 24 ? "…" : "")}");
                w.Steps.Add($"Factors they share: {string.Join(", ", common)}");
                w.Steps.Add($"The biggest one is {common.Max()}.");
                w.Answer = common.Max().ToString();
                w.Say = $"The greatest common factor of {a} and {b} is {common.Max()}.";
                return w;
            }
            if (Regex.IsMatch(l, @"\b(lcm|lcd|least common (multiple|denominator)|lowest common (multiple|denominator))\b") && nums.Count >= 2)
            {
                long a = nums[0], b = nums[1];
                if (a == 0 || b == 0 || a > 10000 || b > 10000) return null;
                long lcm = a / Frac.Gcd(a, b) * b;
                var w = new MathWork { Title = "Least common multiple", Problem = $"LCM of {a} and {b}" };
                w.Steps.Add($"Multiples of {a}: {string.Join(", ", Multiples(a, lcm))}");
                w.Steps.Add($"Multiples of {b}: {string.Join(", ", Multiples(b, lcm))}");
                w.Steps.Add($"The first number in both lists is {lcm}.");
                w.Answer = lcm.ToString();
                w.Say = $"The least common multiple of {a} and {b} is {lcm}.";
                return w;
            }
            if (Regex.IsMatch(l, @"\bprime factori[sz]ation\b|\bprime factors\b") && nums.Count >= 1)
            {
                long n = nums[0];
                if (n < 2 || n > 1_000_000_000) return null;
                var w = new MathWork { Title = "Prime factorization", Problem = $"Prime factors of {n}" };
                var factors = new List<long>();
                long left = n;
                for (long p = 2; p * p <= left; p++)
                    while (left % p == 0) { w.Steps.Add($"{left} ÷ {p} = {left / p}"); factors.Add(p); left /= p; }
                if (left > 1) { if (factors.Count > 0) w.Steps.Add($"{left} is prime, so we stop."); factors.Add(left); }
                string chain = string.Join(" × ", factors);
                string power = string.Join(" × ", factors.GroupBy(f => f).Select(g => g.Count() > 1 ? g.Key + Sup(g.Count()) : g.Key.ToString()));
                w.Steps.Add($"Multiply the primes back together: {chain} = {n}.");
                w.Answer = chain + (power != chain ? $" (or {power})" : "");
                w.Say = factors.Count == 1 ? $"{n} is prime, so it's just {n}." : $"{n} = {chain}" + (power != chain ? $", or {power}." : ".");
                return w;
            }
            var prime = Regex.Match(l, @"\bis (\d+) (?:a )?prime\b|\bis (\d+) (?:a )?(?:prime|composite) number\b");
            if (prime.Success)
            {
                long n = long.Parse(prime.Groups[1].Success ? prime.Groups[1].Value : prime.Groups[2].Value);
                if (n > 1_000_000_000_000) return null;
                var w = new MathWork { Title = "Is it prime?", Problem = $"Is {n} prime?" };
                if (n < 2) { w.Steps.Add("Prime numbers start at 2."); w.Answer = "not prime"; w.Say = $"No, {n} isn't prime. Primes start at 2."; return w; }
                long root = (long)Math.Sqrt(n);
                w.Steps.Add($"Only check dividing by numbers up to √{n} ≈ {Math.Sqrt(n):0.##}.");
                for (long p = 2; p <= root; p++)
                {
                    if (n % p != 0) continue;
                    w.Steps.Add($"{n} ÷ {p} = {n / p} exactly, so {n} = {p} × {n / p}.");
                    w.Answer = "not prime";
                    w.Say = $"No, {n} isn't prime. {n} = {p} × {n / p}.";
                    return w;
                }
                w.Steps.Add(root >= 2 ? $"None of 2 to {root} divide it evenly." : "Nothing smaller divides it.");
                w.Answer = "prime";
                w.Say = $"Yes, {n} is a prime number. Only 1 and {n} divide it.";
                return w;
            }
            if (Regex.IsMatch(l, @"\b(?:what are the |list the |all the )?factors of (\d+)\b") && nums.Count >= 1 && !l.Contains("prime"))
            {
                long n = nums[0];
                if (n < 1 || n > 1_000_000) return null;
                var f = Divisors(n);
                var w = new MathWork { Title = "Factors", Problem = $"Factors of {n}" };
                foreach (var a in f.Where(a => a * a <= n)) w.Steps.Add($"{a} × {n / a} = {n}");
                w.Answer = string.Join(", ", f);
                w.Say = $"The factors of {n} are {string.Join(", ", f)}.";
                return w;
            }
            return null;
        }

        private static List<long> Divisors(long n)
        {
            var small = new List<long>(); var big = new List<long>();
            for (long i = 1; i * i <= n; i++)
                if (n % i == 0) { small.Add(i); if (i != n / i) big.Insert(0, n / i); }
            return small.Concat(big).ToList();
        }

        private static IEnumerable<long> Multiples(long a, long upTo)
        {
            for (long m = a; m <= upTo; m += a) yield return m;
        }

        private static string Sup(int n) => string.Concat(n.ToString().Select(c => "⁰¹²³⁴⁵⁶⁷⁸⁹"[c - '0']));

        // ===================================================================== Geometry

        private static MathWork? Geometry(string l)
        {
            var nums = Regex.Matches(l, @"\d+(?:\.\d+)?").Select(m => double.Parse(m.Value, Inv)).ToList();
            if (nums.Count == 0) return null;
            if (Regex.IsMatch(l, @"\bhypotenuse\b") && nums.Count >= 2 || Regex.IsMatch(l, @"\bpythag") && nums.Count >= 2 && !Regex.IsMatch(l, @"\bhypotenuse (is|of) [\d.]+"))
            {
                // "hypotenuse is 10 and a leg is 6" → missing leg.
                var hyp = Regex.Match(l, @"hypotenuse (?:is |of |=\s*)?([\d.]+)");
                if (hyp.Success && nums.Count >= 2 && Regex.IsMatch(l, @"\b(leg|side|other)\b") && !Regex.IsMatch(l, @"\bfind the hypotenuse\b"))
                {
                    double c = double.Parse(hyp.Groups[1].Value, Inv);
                    double a = nums.First(n => Math.Abs(n - c) > 1e-9);
                    if (a >= c) return null;
                    double b = Math.Sqrt(c * c - a * a);
                    var w2 = new MathWork { Title = "Pythagorean theorem", Problem = $"Hypotenuse {N(c)}, one leg {N(a)}" };
                    w2.Steps.Add("For a right triangle: a² + b² = c² (c is the hypotenuse, the long side).");
                    w2.Steps.Add($"{N(a)}² + b² = {N(c)}² → {N(a * a)} + b² = {N(c * c)}");
                    w2.Steps.Add($"b² = {N(c * c)} - {N(a * a)} = {N(c * c - a * a)}");
                    w2.Steps.Add($"b = √{N(c * c - a * a)} = {N(b)}");
                    w2.Answer = N(b);
                    w2.Say = $"The other leg is {N(b)}.";
                    return w2;
                }
                double x = nums[0], y = nums[1];
                double h = Math.Sqrt(x * x + y * y);
                var w = new MathWork { Title = "Pythagorean theorem", Problem = $"Legs {N(x)} and {N(y)}" };
                w.Steps.Add("For a right triangle: a² + b² = c² (c is the hypotenuse, the long side).");
                w.Steps.Add($"{N(x)}² + {N(y)}² = {N(x * x)} + {N(y * y)} = {N(x * x + y * y)}");
                w.Steps.Add($"c = √{N(x * x + y * y)} = {N(h)}");
                w.Answer = N(h);
                w.Say = $"The hypotenuse is {N(h)}.";
                return w;
            }
            bool area = Regex.IsMatch(l, @"\barea\b"), perim = Regex.IsMatch(l, @"\bperimeter\b"), circum = Regex.IsMatch(l, @"\bcircumference\b"), vol = Regex.IsMatch(l, @"\bvolume\b");
            if (!(area || perim || circum || vol)) return null;
            if (Regex.IsMatch(l, @"\bcircle\b") || circum)
            {
                bool diameter = Regex.IsMatch(l, @"\bdiameter\b");
                double r = diameter ? nums[0] / 2 : nums[0];
                var w = new MathWork { Title = area ? "Area of a circle" : "Circumference of a circle", Problem = (diameter ? "Diameter " : "Radius ") + N(nums[0]) };
                if (diameter) w.Steps.Add($"The radius is half the diameter: {N(nums[0])} ÷ 2 = {N(r)}.");
                if (area)
                {
                    w.Steps.Add("Area of a circle = π × r².");
                    w.Steps.Add($"r² = {N(r)} × {N(r)} = {N(r * r)}");
                    w.Steps.Add($"π × {N(r * r)} ≈ 3.14159 × {N(r * r)} = {Math.Round(Math.PI * r * r, 2).ToString("0.##", Inv)}");
                    w.Answer = Math.Round(Math.PI * r * r, 2).ToString("0.##", Inv) + " square units";
                    w.Say = $"The area is about {Math.Round(Math.PI * r * r, 2).ToString("0.##", Inv)} square units ({N(r * r)}π).";
                }
                else
                {
                    w.Steps.Add("Circumference = 2 × π × r.");
                    w.Steps.Add($"2 × π × {N(r)} ≈ {Math.Round(2 * Math.PI * r, 2).ToString("0.##", Inv)}");
                    w.Answer = Math.Round(2 * Math.PI * r, 2).ToString("0.##", Inv) + " units";
                    w.Say = $"The circumference is about {Math.Round(2 * Math.PI * r, 2).ToString("0.##", Inv)} ({N(2 * r)}π).";
                }
                return w;
            }
            if (Regex.IsMatch(l, @"\btriangle\b") && area && nums.Count >= 2)
            {
                double b = nums[0], h = nums[1];
                var w = new MathWork { Title = "Area of a triangle", Problem = $"Base {N(b)}, height {N(h)}" };
                w.Steps.Add("Area of a triangle = ½ × base × height.");
                w.Steps.Add($"½ × {N(b)} × {N(h)} = {N(b * h)} ÷ 2 = {N(b * h / 2)}");
                w.Answer = N(b * h / 2) + " square units";
                w.Say = $"The area is {N(b * h / 2)} square units.";
                return w;
            }
            if (Regex.IsMatch(l, @"\bsquare\b") && !Regex.IsMatch(l, @"\bsquare (feet|foot|meters?|units?|inches|inch|cm|miles?|km)\b"))
            {
                double s = nums[0];
                var w = new MathWork { Title = area ? "Area of a square" : "Perimeter of a square", Problem = $"Side {N(s)}" };
                if (area) { w.Steps.Add("Area of a square = side × side."); w.Steps.Add($"{N(s)} × {N(s)} = {N(s * s)}"); w.Answer = N(s * s) + " square units"; w.Say = $"The area is {N(s * s)} square units."; }
                else { w.Steps.Add("Perimeter of a square = 4 × side."); w.Steps.Add($"4 × {N(s)} = {N(4 * s)}"); w.Answer = N(4 * s) + " units"; w.Say = $"The perimeter is {N(4 * s)}."; }
                return w;
            }
            if (vol && Regex.IsMatch(l, @"\bcube\b"))
            {
                double s = nums[0];
                var w = new MathWork { Title = "Volume of a cube", Problem = $"Side {N(s)}" };
                w.Steps.Add("Volume of a cube = side × side × side.");
                w.Steps.Add($"{N(s)} × {N(s)} × {N(s)} = {N(s * s * s)}");
                w.Answer = N(s * s * s) + " cubic units";
                w.Say = $"The volume is {N(s * s * s)} cubic units.";
                return w;
            }
            if (vol && nums.Count >= 3)
            {
                double a = nums[0], b = nums[1], c = nums[2];
                var w = new MathWork { Title = "Volume of a box", Problem = $"{N(a)} × {N(b)} × {N(c)}" };
                w.Steps.Add("Volume of a box = length × width × height.");
                w.Steps.Add($"{N(a)} × {N(b)} = {N(a * b)}, then × {N(c)} = {N(a * b * c)}");
                w.Answer = N(a * b * c) + " cubic units";
                w.Say = $"The volume is {N(a * b * c)} cubic units.";
                return w;
            }
            if (nums.Count >= 2 && (area || perim))
            {
                double a = nums[0], b = nums[1];
                var w = new MathWork { Title = area ? "Area of a rectangle" : "Perimeter of a rectangle", Problem = $"{N(a)} by {N(b)}" };
                if (area) { w.Steps.Add("Area of a rectangle = length × width."); w.Steps.Add($"{N(a)} × {N(b)} = {N(a * b)}"); w.Answer = N(a * b) + " square units"; w.Say = $"The area is {N(a * b)} square units."; }
                else { w.Steps.Add("Perimeter = 2 × (length + width)."); w.Steps.Add($"{N(a)} + {N(b)} = {N(a + b)}, and 2 × {N(a + b)} = {N(2 * (a + b))}"); w.Answer = N(2 * (a + b)) + " units"; w.Say = $"The perimeter is {N(2 * (a + b))}."; }
                return w;
            }
            return null;
        }

        // ===================================================================== Long division

        private static MathWork? LongDivision(string l)
        {
            var m = Regex.Match(l, @"^(?:.*?\b)?(\d{2,9})\s*/\s*(\d{1,6})(?:\s|$|\?)");
            if (!m.Success || Regex.IsMatch(l, @"[+\-*^()=]") || !Regex.IsMatch(l, @"\bstep|\bshow|\bexplain|\bhow (do|would|can)|\bsolve|\bwork|\blong division")) return null;
            long dividend = long.Parse(m.Groups[1].Value), divisor = long.Parse(m.Groups[2].Value);
            if (divisor == 0) throw new DivideByZeroException();
            var w = new MathWork { Title = "Long division", Problem = $"{dividend} ÷ {divisor}" };
            string digits = dividend.ToString();
            long current = 0;
            var quotient = new StringBuilder();
            bool started = false;
            for (int i = 0; i < digits.Length; i++)
            {
                current = current * 10 + (digits[i] - '0');
                long times = current / divisor;
                if (!started && times == 0 && i < digits.Length - 1) continue;   // not big enough yet: use the next digit too
                string lead = started ? $"Bring down the {digits[i]} to make {current}." : $"Start with {current}.";
                started = true;
                quotient.Append(times);
                w.Steps.Add($"{lead} {divisor} goes into {current} {times} time{(times == 1 ? "" : "s")} ({times} × {divisor} = {times * divisor}). {current} - {times * divisor} = {current - times * divisor}.");
                current -= times * divisor;
            }
            long q = dividend / divisor, r = dividend % divisor;
            w.Steps.Add(r == 0 ? $"Nothing is left over, so the answer is {q}." : $"{r} is left over, so it's {q} remainder {r} ({N((double)dividend / divisor)} as a decimal).");
            w.Answer = r == 0 ? q.ToString() : $"{q} R{r}";
            w.Say = r == 0 ? $"{dividend} ÷ {divisor} = {q}." : $"{dividend} ÷ {divisor} = {q} remainder {r}, or about {N((double)dividend / divisor)}.";
            return w;
        }

        // ===================================================================== Order of operations

        private abstract class Node { public bool Paren; }
        private sealed class NumNode : Node { public Frac V; public NumNode(Frac v) { V = v; } }
        private sealed class OpNode : Node { public string Op = ""; public Node L = null!, R = null!; }
        private sealed class NegNode : Node { public Node X = null!; }

        private sealed class TreeParser
        {
            private readonly List<string> _t; private int _i;
            public TreeParser(string s) { _t = Parser.Tokens(s, '\0'); }
            private string? Peek => _i < _t.Count ? _t[_i] : null;
            public bool AtEnd => _i >= _t.Count;
            public Node Expr()
            {
                var v = Term();
                while (Peek is "+" or "-") { string op = _t[_i++]; v = new OpNode { Op = op, L = v, R = Term() }; }
                return v;
            }
            private Node Term()
            {
                var v = Unary();
                while (Peek is "*" or "/") { string op = _t[_i++]; v = new OpNode { Op = op, L = v, R = Unary() }; }
                return v;
            }
            private Node Unary()
            {
                if (Peek == "-") { _i++; var x = Unary(); return x is NumNode n ? new NumNode(-n.V) : new NegNode { X = x }; }
                if (Peek == "+") { _i++; return Unary(); }
                var b = Atom();
                if (Peek == "^") { _i++; return new OpNode { Op = "^", L = b, R = Unary() }; }
                return b;
            }
            private Node Atom()
            {
                string t = Peek ?? throw new FormatException("Ended early");
                _i++;
                if (t == "(") { var v = Expr(); if (Peek != ")") throw new FormatException("Missing )"); _i++; v.Paren = true; return v; }
                if (char.IsDigit(t[0]) || t[0] == '.') return new NumNode(Frac.FromDecimal(t.StartsWith('.') ? "0" + t : t));
                throw new FormatException("Unexpected " + t);
            }
        }

        private static MathWork? Arithmetic(string l)
        {
            // Pull out the math: digits, operators, brackets.
            var m = Regex.Match(l, @"[-(]*\d[\d.,]*(?:\s*[-+*/^()]\s*[(]*\s*-?\d[\d.,]*[)]*)+[)]*");
            if (!m.Success) return null;
            string expr = m.Value;
            if (!Regex.IsMatch(expr, @"[-+*/^]")) return null;
            var p = new TreeParser(expr);
            Node root = p.Expr();
            if (!p.AtEnd) return null;
            var w = new MathWork { Title = "Order of operations", Problem = Show(root) };
            w.Steps.Add("Order: brackets first, then powers, then × and ÷ left to right, then + and - left to right.");
            int guard = 0;
            while (root is not NumNode && guard++ < 40)
            {
                var (node, parent, isLeft) = Next(root, null, false, 0);
                if (node == null) break;
                Frac value; string say;
                if (node is NegNode neg) { value = -((NumNode)neg.X).V; say = $"Make it negative: -{((NumNode)neg.X).V}"; }
                else
                {
                    var op = (OpNode)node;
                    Frac a = ((NumNode)op.L).V, b = ((NumNode)op.R).V;
                    (value, say) = op.Op switch
                    {
                        "+" => (a + b, $"Add {a} + {b} = {a + b}"),
                        "-" => (a - b, $"Subtract {a} - {b} = {a - b}"),
                        "*" => (a * b, $"Multiply {a} × {b} = {a * b}"),
                        "/" => (a / b, $"Divide {a} ÷ {b} = {a / b}"),
                        _ => (Frac.Pow(a, b), $"Power {a}^{b} = {Frac.Pow(a, b)}"),
                    };
                }
                var result = new NumNode(value);
                if (parent == null) root = result;
                else if (parent is NegNode pn) pn.X = result;
                else if (isLeft) ((OpNode)parent).L = result; else ((OpNode)parent).R = result;
                w.Steps.Add(say + (root is NumNode ? "" : "  →  " + Show(root)));
            }
            if (root is not NumNode final || w.Steps.Count < 2) return null;
            w.Answer = final.V.Friendly();
            w.Say = $"{w.Problem} = {final.V.Friendly()}.";
            return w;
        }

        /// <summary>The next operation to do: deepest brackets first, then ^, then × ÷, then + -, leftmost.</summary>
        private static (Node? Node, Node? Parent, bool IsLeft) Next(Node root, Node? parent, bool isLeft, int depth)
        {
            var ready = new List<(Node N, Node? P, bool L, int Depth, int Rank, int Order)>();
            int order = 0;
            void Walk(Node n, Node? par, bool left, int d)
            {
                int dd = d + (n.Paren ? 1 : 0);
                switch (n)
                {
                    case OpNode o:
                        Walk(o.L, o, true, dd);
                        if (o.L is NumNode && o.R is NumNode) ready.Add((o, par, left, dd, o.Op == "^" ? 3 : o.Op is "*" or "/" ? 2 : 1, order));
                        order++;
                        Walk(o.R, o, false, dd);
                        break;
                    case NegNode g:
                        Walk(g.X, g, true, dd);
                        if (g.X is NumNode) ready.Add((g, par, left, dd, 4, order++));
                        break;
                }
            }
            Walk(root, parent, isLeft, depth);
            if (ready.Count == 0) return (null, null, false);
            var best = ready.OrderByDescending(r => r.Depth).ThenByDescending(r => r.Rank).ThenBy(r => r.Order).First();
            return (best.N, best.P, best.L);
        }

        private static string Show(Node n)
        {
            string s = n switch
            {
                NumNode x => x.V.Num < 0 && x.V.Den == 1 ? x.V.ToString() : x.V.IsWhole ? x.V.ToString() : "(" + x.V + ")",
                NegNode g => "-" + Show(g.X),
                OpNode o => Show(o.L) + (o.Op == "^" ? "^" : " " + Sym(o.Op) + " ") + Show(o.R),
                _ => "?",
            };
            return n.Paren && n is not NumNode ? "(" + s + ")" : s;
        }
    }
}
