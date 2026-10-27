using System.Text.RegularExpressions;

namespace Lamha.Core.Translation;

/// <summary>A row of side by side: a sentence and its translation, and whether a paragraph ends after it.</summary>
public sealed record SideRow(string Source, string Translation, bool EndsParagraph);

/// <summary>
/// Side by side (docs/design/system.md, the study's features): a paragraph in Translate's box shows as two columns, the
/// text and its translation, a sentence beside its translation. Google's sentences pair them; the AI's whole
/// translation is paired line by line when the lines match, or whole.
/// </summary>
public static partial class SideBySide
{
    /// <summary>
    /// The rows for <paramref name="result"/>: a text of two sentences or more. Empty for a word, an explanation, or a
    /// single sentence, which show as they always have.
    /// </summary>
    public static IReadOnlyList<SideRow> Rows(LookupResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsWord || result.Explain || result.Translation.Trim().Length == 0)
        {
            return [];
        }

        var rows = new List<SideRow>();
        foreach (var pair in result.Sentences.Count > 0 ? result.Sentences : Lines(result.Query, result.Translation))
        {
            var source = pair.Source.Trim();
            var translation = pair.Translation.Trim();
            var breaks = pair.Source.Contains('\n', StringComparison.Ordinal) || pair.Translation.Contains('\n', StringComparison.Ordinal);
            if (source.Length == 0 && translation.Length == 0)
            {
                // A line break Google sent on its own ends the paragraph before it.
                if (breaks && rows.Count > 0)
                {
                    rows[^1] = rows[^1] with { EndsParagraph = true };
                }

                continue;
            }

            rows.Add(new SideRow(source, translation, breaks));
        }

        if (rows.Count > 0)
        {
            rows[^1] = rows[^1] with { EndsParagraph = false }; // nothing comes after the last one
        }

        return rows.Count >= 2 || (rows.Count == 1 && SentenceCount(result.Query) >= 2) ? rows : [];
    }

    /// <summary>The sentences of <paramref name="text"/>, counted by their ends (. ! ? ؟ …) and line breaks.</summary>
    public static int SentenceCount(string text) =>
        SentenceBreak().Split(text ?? "").Count(s => s.Any(char.IsLetterOrDigit));

    /// <summary>No sentences from Google: the text and its translation line by line when both have as many lines, otherwise whole.</summary>
    private static IEnumerable<SentencePair> Lines(string text, string translation)
    {
        var source = LineBreak().Split(text.Trim());
        var translated = LineBreak().Split(translation.Trim());
        return source.Length > 1 && source.Length == translated.Length
            ? source.Zip(translated, (s, t) => new SentencePair(s + "\n", t + "\n"))
            : [new SentencePair(text, translation)];
    }

    [GeneratedRegex(@"(?<=[.!?؟…])\s+|\n+")]
    private static partial Regex SentenceBreak();

    [GeneratedRegex(@"\s*\n\s*")]
    private static partial Regex LineBreak();
}
