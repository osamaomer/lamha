using System.Text.RegularExpressions;

namespace Lamha.Core.Text;

/// <summary>A piece of a longer text and the whitespace that followed it, to join translations back the same way.</summary>
public readonly record struct TextPiece(string Text, string Separator);

/// <summary>Selected text tidied and cut into pieces a translator accepts.</summary>
public static partial class Pieces
{
    /// <summary>
    /// Selected text tidied: runs of spaces become one, paragraph breaks stay (at most one empty line). Lamha 2's
    /// <c>tidyText</c> in background.js.
    /// </summary>
    public static string Tidy(string? text)
    {
        var s = LineBreaks().Replace(text ?? "", "\n");
        s = Spaces().Replace(s, " ");
        s = SpaceAroundBreak().Replace(s, "\n");
        s = ManyBreaks().Replace(s, "\n\n");
        return s.Trim();
    }

    /// <summary>
    /// Long text in pieces of at most <paramref name="max"/> characters, cut at a line break, else a sentence end, else a
    /// space; nothing is lost. Lamha 2's <c>splitLong</c> in background.js.
    /// </summary>
    public static IReadOnlyList<TextPiece> SplitLong(string text, int max)
    {
        var pieces = new List<TextPiece>();
        var rest = text;
        while (rest.Length > max)
        {
            var part = rest[..max];
            var cut = part.LastIndexOf('\n');
            if (cut < max * 0.3)
            {
                cut = LastOfAny(part, ". ", "! ", "? ", "؟ ", "; ") + 1; // keep the full stop
            }

            if (cut < max * 0.3)
            {
                cut = part.LastIndexOf(' ');
            }

            if (cut <= 0)
            {
                cut = max;
            }

            // "\n\n": the whole break goes in the separator, none stays on the piece.
            while (cut > 1 && char.IsWhiteSpace(rest[cut - 1]))
            {
                cut--;
            }

            var after = cut;
            while (after < rest.Length && char.IsWhiteSpace(rest[after]))
            {
                after++;
            }

            pieces.Add(new TextPiece(rest[..cut], rest[cut..after]));
            rest = rest[after..];
        }

        if (rest.Length > 0)
        {
            pieces.Add(new TextPiece(rest, ""));
        }

        return pieces;
    }

    /// <summary>
    /// Text for a voice that reads at most <paramref name="max"/> characters at a time, cut at sentence, comma or word
    /// boundaries. Lamha 2's <c>ttsChunks</c> (Google's voice reads about 200 characters per request).
    /// </summary>
    public static IReadOnlyList<string> ForSpeech(string text, int max = 190)
    {
        var chunks = new List<string>();
        var rest = Whitespace().Replace(text, " ").Trim();
        while (rest.Length > max)
        {
            var part = rest[..max];
            var cut = LastOfAny(part, ". ", "! ", "? ", "؟ ", "; ", "، ", ", ");
            if (cut < max * 0.4)
            {
                cut = part.LastIndexOf(' ');
            }

            if (cut <= 0)
            {
                cut = max - 1;
            }

            chunks.Add(rest[..(cut + 1)].Trim());
            rest = rest[(cut + 1)..].Trim();
        }

        if (rest.Length > 0)
        {
            chunks.Add(rest);
        }

        return chunks;
    }

    /// <summary>Where the last of <paramref name="marks"/> starts in <paramref name="text"/>, or -1.</summary>
    private static int LastOfAny(string text, params ReadOnlySpan<string> marks)
    {
        var last = -1;
        foreach (var mark in marks)
        {
            last = Math.Max(last, text.LastIndexOf(mark, StringComparison.Ordinal));
        }

        return last;
    }

    [GeneratedRegex(@"\r\n?")]
    private static partial Regex LineBreaks();

    [GeneratedRegex(@"[^\S\n]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@" ?\n ?")]
    private static partial Regex SpaceAroundBreak();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ManyBreaks();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
