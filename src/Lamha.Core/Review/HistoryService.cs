using Lamha.Core.Abstractions;
using Lamha.Core.Text;

namespace Lamha.Core.Review;

/// <summary>A day of the history, by this PC's calendar, and the words looked up that day, newest first.</summary>
public sealed record HistoryDay(DateOnly Day, IReadOnlyList<HistoryEntry> Entries);

/// <summary>The lookup history: the last 1,000 words, newest first (Lamha 2's <c>addHistory</c>).</summary>
public sealed class HistoryService(IHistoryStore store, TimeProvider time)
{
    /// <summary>
    /// Lamha 2 kept 100; Lamha 3's History page shows them all, so it keeps more (decided 2026-10-25). A word stays
    /// "known" for milestones and the goal as long as it's in here. Lamha 2's restore keeps the newest 100 of a backup.
    /// </summary>
    public const int Keep = 1000;

    /// <summary>Today on this PC's calendar, for naming the history's days.</summary>
    public DateOnly Today => Days.Today(time);

    /// <summary>Puts the word at the top; true when it wasn't in the history (a word new to the user).</summary>
    public bool Add(string word, string meaning, string language) =>
        store.Add(new HistoryEntry(word, meaning, language, Days.NowMs(time)), Keep);

    public IReadOnlyList<HistoryEntry> Recent(int limit = Keep) => store.Recent(limit);

    /// <summary>
    /// The History page: the whole history by day, newest first, or only the words that match <paramref name="query"/>.
    /// Every word of the query must be in the word or its meaning, with Arabic spelling variants, marks, digits and case
    /// set aside, as in clipboard history's search (<see cref="SearchText"/>).
    /// </summary>
    public IReadOnlyList<HistoryDay> ByDay(string query = "")
    {
        var words = SearchText.Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return [.. store.Recent(Keep)
            .Where(e => words.Length == 0 || Matches(e, words))
            .GroupBy(e => DateOnly.FromDateTime(LocalTime(e)))
            .Select(g => new HistoryDay(g.Key, [.. g]))];
    }

    /// <summary>When the entry was looked up, in this PC's time.</summary>
    public DateTime LocalTime(HistoryEntry entry) =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(entry.Time), time.LocalTimeZone).DateTime;

    public bool Contains(string word) => store.Contains(word);

    public int Count() => store.Count();

    public void Clear() => store.Clear();

    /// <summary>
    /// Undo after <see cref="Clear"/> (Lamha 2's "History cleared · Undo"): the cleared words come back where they were
    /// by time, and words looked up since stay. Returns how many came back.
    /// </summary>
    public int Restore(IReadOnlyList<HistoryEntry> cleared) => store.Merge(cleared, Keep);

    private static bool Matches(HistoryEntry entry, string[] words)
    {
        var text = SearchText.Normalize(entry.Word + " " + entry.Meaning);
        return words.All(w => text.Contains(w, StringComparison.Ordinal));
    }
}
