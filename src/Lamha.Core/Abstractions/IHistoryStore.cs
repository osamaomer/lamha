namespace Lamha.Core.Abstractions;

/// <summary>A looked-up word in the history (Lamha 2's <c>{ q, tr, src, t }</c>).</summary>
/// <param name="Word">The word as the card showed it.</param>
/// <param name="Meaning">Its short meaning.</param>
/// <param name="Language">The word's language ("en", "ar"…).</param>
/// <param name="Time">When it was looked up, in milliseconds since 1970 (UTC).</param>
public sealed record HistoryEntry(string Word, string Meaning, string Language, long Time)
{
    /// <summary>The history's key for a word: small letters, so "Bank" and "bank" are one entry.</summary>
    public string Key => Word.ToLowerInvariant();
}

/// <summary>The lookup history, newest first.</summary>
public interface IHistoryStore
{
    /// <summary>
    /// Puts <paramref name="entry"/> at the top, replacing the same word lower down, and keeps the newest
    /// <paramref name="keep"/>. Returns true when the word wasn't in the history (a word new to the user).
    /// </summary>
    bool Add(HistoryEntry entry, int keep);

    /// <summary>
    /// Adds the entries whose words aren't in the history yet, keeping the newest <paramref name="keep"/> by time.
    /// Returns how many were added.
    /// </summary>
    int Merge(IReadOnlyList<HistoryEntry> entries, int keep);

    /// <summary>The newest <paramref name="limit"/> entries.</summary>
    IReadOnlyList<HistoryEntry> Recent(int limit);

    bool Contains(string word);

    int Count();

    void Clear();
}
