using System.Text.RegularExpressions;

namespace Lamha.Core.Text;

/// <summary>What kind of text the user selected.</summary>
public static partial class TextKind
{
    /// <summary>
    /// A word or short phrase, looked up in the dictionary rather than translated: at most three words and 40
    /// characters, and no sentence punctuation or line break. Lamha 2's <c>isLookupCandidate()</c> in background.js.
    /// </summary>
    public static bool IsLookupCandidate(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var words = Whitespace().Split(text.Trim());
        return words.Length <= 3 && text.Length <= 40 && !SentenceBreak().IsMatch(text);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[.!?;:]\s|[\n\r]")]
    private static partial Regex SentenceBreak();
}
