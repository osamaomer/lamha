using Lamha.Core.Abstractions;
using Lamha.Core.Text;

namespace Lamha.Core.Search;

/// <summary>What a suggestion of the main window's search is.</summary>
public enum QuickKind
{
    /// <summary>A setting, or a category of Settings: opens that category.</summary>
    Setting,

    /// <summary>A word looked up before: its answer.</summary>
    Word,

    /// <summary>A copy in clipboard history: the Clipboard page.</summary>
    Copy,

    /// <summary>The text itself, translated: always the last suggestion.</summary>
    Translate,
}

/// <summary>A suggestion: what it is, its line, the line under it, and where it goes (a category's id, the word, the copy's id, or the text).</summary>
public sealed record QuickHit(QuickKind Kind, string Text, string Detail, string Target);

/// <summary>Something in Settings the search can find: its name, the line under it, and its category's id.</summary>
public sealed record SettingEntry(string Name, string Detail, string Target);

/// <summary>
/// The search in the main window's title bar, as Windows' Settings' "Find a setting" (docs/design/system.md; decision
/// 0020, the user's choice of what it finds): settings, words looked up, clipboard copies, and always "Translate …" for
/// what was typed. Spelling variants, marks, digits and case don't matter (<see cref="SearchText"/>).
/// </summary>
public static class QuickSearch
{
    /// <summary>At most this many of each kind, so every kind shows.</summary>
    public const int PerKind = 3;

    /// <summary>
    /// The suggestions for <paramref name="query"/>: settings whose name matches, words whose word or meaning matches
    /// (each word once, the newest first as <paramref name="words"/> gives them), the <paramref name="copies"/> clipboard
    /// history found (it searches its own, encrypted, copies), then the text to translate. Nothing for an empty query.
    /// </summary>
    public static IReadOnlyList<QuickHit> Find(string query, IEnumerable<SettingEntry> settings, IEnumerable<HistoryEntry> words, IEnumerable<(string Id, string Text)> copies)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(copies);
        var text = (query ?? "").Trim();
        var q = SearchText.Normalize(text);
        if (q.Length == 0)
        {
            return [];
        }

        bool Has(string value) => SearchText.Normalize(value).Contains(q, StringComparison.Ordinal);

        var hits = new List<QuickHit>();
        hits.AddRange(settings
            .Where(s => Has(s.Name))
            .DistinctBy(s => s.Name)
            .Take(PerKind)
            .Select(s => new QuickHit(QuickKind.Setting, s.Name, s.Detail, s.Target)));
        hits.AddRange(words
            .Where(w => Has(w.Word) || Has(w.Meaning))
            .DistinctBy(w => w.Key)
            .Take(PerKind)
            .Select(w => new QuickHit(QuickKind.Word, w.Word, w.Meaning, w.Word)));
        hits.AddRange(copies
            .Take(PerKind)
            .Select(c => new QuickHit(QuickKind.Copy, FirstLine(c.Text), "", c.Id)));
        hits.Add(new QuickHit(QuickKind.Translate, text, "", text));
        return hits;
    }

    /// <summary>A copy's first line, as one line of a suggestion.</summary>
    private static string FirstLine(string text)
    {
        var line = text.Trim().Split('\n', 2)[0].Trim();
        return line.Length <= 80 ? line : string.Concat(line.AsSpan(0, 80), "…");
    }
}
