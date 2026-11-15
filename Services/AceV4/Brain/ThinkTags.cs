using System.Text.RegularExpressions;

namespace Nimbus.AceV4.Brain
{
    /// <summary>
    /// Removes a model's hidden reasoning from what it says. Handles &lt;think&gt;, &lt;thinking&gt;, &lt;reasoning&gt; and
    /// &lt;reflection&gt; blocks in any case, a block that's still being streamed, and a closing tag with no opening tag
    /// (some models skip the opening one, so everything before it is reasoning).
    /// </summary>
    public static class ThinkTags
    {
        private const string Names = "think|thinking|thought|reasoning|reflection|scratchpad";
        private static readonly Regex Block = new(@"<\|?\s*(" + Names + @")\b[^>]*\|?>.*?<\|?\s*/\s*\1\s*\|?>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex Open = new(@"<\|?\s*(" + Names + @")\b[^>]*\|?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Close = new(@"<\|?\s*/\s*(" + Names + @")\s*\|?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string Strip(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string s = Block.Replace(text, "");
            // A closing tag left over: everything before it was reasoning.
            var close = Close.Matches(s);
            if (close.Count > 0)
            {
                var last = close[close.Count - 1];
                s = s[(last.Index + last.Length)..];
            }
            // An opening tag with no end yet (still streaming): hide the rest.
            var open = Open.Match(s);
            if (open.Success) s = s[..open.Index];
            return s.TrimStart();
        }
    }
}
