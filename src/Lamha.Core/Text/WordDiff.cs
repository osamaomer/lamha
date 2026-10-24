using System.Text.RegularExpressions;

namespace Lamha.Core.Text;

/// <summary>What happened to a piece of text between two versions.</summary>
public enum DiffKind
{
    Same,
    Removed,
    Inserted,
}

/// <summary>A run of words (with their spaces) that stayed, went or came.</summary>
public readonly record struct DiffPart(DiffKind Kind, string Text);

/// <summary>Word-level differences, for showing proofreading's corrections struck through and highlighted.</summary>
public static partial class WordDiff
{
    /// <summary>
    /// The changes from <paramref name="before"/> to <paramref name="after"/>, word by word, neighbouring pieces of the
    /// same kind joined; null when the texts are too long to compare quickly. Lamha 2's <c>diffParts</c> in
    /// shared/lamha-ai.js.
    /// </summary>
    public static IReadOnlyList<DiffPart>? Compare(string before, string after)
    {
        var a = Tokens(before);
        var b = Tokens(after);
        int n = a.Length, m = b.Length;
        if ((long)n * m > 400_000)
        {
            return null;
        }

        // Longest common subsequence, from the end.
        var dp = new ushort[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                dp[i, j] = a[i] == b[j] ? (ushort)(dp[i + 1, j + 1] + 1) : Math.Max(dp[i + 1, j], dp[i, j + 1]);
            }
        }

        var parts = new List<DiffPart>();
        void Push(DiffKind kind, string text)
        {
            if (parts.Count > 0 && parts[^1].Kind == kind)
            {
                parts[^1] = parts[^1] with { Text = parts[^1].Text + text };
            }
            else
            {
                parts.Add(new DiffPart(kind, text));
            }
        }

        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y])
            {
                Push(DiffKind.Same, a[x]);
                x++;
                y++;
            }
            else if (dp[x + 1, y] >= dp[x, y + 1])
            {
                Push(DiffKind.Removed, a[x++]);
            }
            else
            {
                Push(DiffKind.Inserted, b[y++]);
            }
        }

        while (x < n)
        {
            Push(DiffKind.Removed, a[x++]);
        }

        while (y < m)
        {
            Push(DiffKind.Inserted, b[y++]);
        }

        return parts;
    }

    private static string[] Tokens(string text) => [.. Token().Matches(text).Select(m => m.Value)];

    [GeneratedRegex(@"\s+|\S+")]
    private static partial Regex Token();
}
