using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lamha.Core.Text;

/// <summary>The little HTML that Google's answers and the sentence-with-a-marked-word carry.</summary>
public static partial class Html
{
    /// <summary>Tags taken out ("a <b>resilient</b> economy" → "a resilient economy"): Lamha 2's <c>stripTags</c>.</summary>
    public static string StripTags(string? text) => Tag().Replace(text ?? "", "");

    /// <summary>&amp;, &lt;, &gt; for text put inside markup: Lamha 2's <c>escHTML</c>.</summary>
    public static string Escape(string text) => text.Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);

    /// <summary>Numeric and the common named entities decoded: Lamha 2's <c>decodeEntities</c>.</summary>
    public static string DecodeEntities(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('&', StringComparison.Ordinal))
        {
            return text ?? "";
        }

        var s = DecimalEntity().Replace(text, m => CodePoint(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)));
        s = HexEntity().Replace(s, m => CodePoint(int.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
        return s.Replace("&quot;", "\"", StringComparison.Ordinal).Replace("&#39;", "'", StringComparison.Ordinal)
            .Replace("&apos;", "'", StringComparison.Ordinal).Replace("&lt;", "<", StringComparison.Ordinal)
            .Replace("&gt;", ">", StringComparison.Ordinal).Replace("&amp;", "&", StringComparison.Ordinal);
    }

    private static string CodePoint(int value) => Rune.IsValid(value) ? char.ConvertFromUtf32(value) : "";

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"&#(\d{1,7});")]
    private static partial Regex DecimalEntity();

    [GeneratedRegex("&#x([0-9a-f]{1,6});", RegexOptions.IgnoreCase)]
    private static partial Regex HexEntity();
}
