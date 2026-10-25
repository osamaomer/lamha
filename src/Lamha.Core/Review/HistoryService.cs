using Lamha.Core.Abstractions;

namespace Lamha.Core.Review;

/// <summary>The lookup history: the last 100 words, newest first (Lamha 2's <c>addHistory</c>).</summary>
public sealed class HistoryService(IHistoryStore store, TimeProvider time)
{
    public const int Keep = 100;

    /// <summary>Puts the word at the top; true when it wasn't in the history (a word new to the user).</summary>
    public bool Add(string word, string meaning, string language) =>
        store.Add(new HistoryEntry(word, meaning, language, Days.NowMs(time)), Keep);

    public IReadOnlyList<HistoryEntry> Recent(int limit = Keep) => store.Recent(limit);

    public bool Contains(string word) => store.Contains(word);

    public int Count() => store.Count();

    public void Clear() => store.Clear();

    /// <summary>
    /// Undo after <see cref="Clear"/> (Lamha 2's "History cleared · Undo"): the cleared words come back where they were
    /// by time, and words looked up since stay. Returns how many came back.
    /// </summary>
    public int Restore(IReadOnlyList<HistoryEntry> cleared) => store.Merge(cleared, Keep);
}
