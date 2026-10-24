using Lamha.Core.Review;

namespace Lamha.Core.Abstractions;

/// <summary>Counts for the review screen.</summary>
/// <param name="Due">Reviewed cards whose time has come.</param>
/// <param name="New">Cards never reviewed.</param>
/// <param name="Total">Every card.</param>
/// <param name="Learned">Cards with an interval of three weeks or more.</param>
/// <param name="NextDue">When the next reviewed card falls due after now, or null when none does.</param>
public readonly record struct DeckCounts(int Due, int New, int Total, int Learned, long? NextDue);

/// <summary>
/// The review deck, queried from disk rather than kept in memory. Removed words are remembered with the time they were
/// removed, so a backup or another copy doesn't bring them back (Lamha 2's <c>cardsRemoved</c>).
/// </summary>
public interface IDeckStore
{
    Card? Get(string key);

    /// <summary>Adds or replaces the card under its key.</summary>
    void Put(Card card);

    /// <summary>Deletes the card under <paramref name="key"/>; false when there was none.</summary>
    bool Delete(string key);

    /// <summary>Reviewed cards due at <paramref name="now"/> or before, the longest-waiting first.</summary>
    IReadOnlyList<Card> Due(long now, int limit);

    /// <summary>New cards, the most recently added first.</summary>
    IReadOnlyList<Card> NewestNew(int limit);

    DeckCounts Counts(long now);

    /// <summary>
    /// The reviewed card due soonest, no later than <paramref name="before"/>, other than <paramref name="exceptKey"/>,
    /// that has a meaning or a definition: the word of the day's first choice.
    /// </summary>
    Card? FirstDue(long before, string exceptKey);

    /// <summary>Every card, for a backup.</summary>
    IReadOnlyList<Card> All();

    /// <summary>Deletes every card, noting each as removed at <paramref name="when"/>.</summary>
    void Clear(long when);

    /// <summary>When the word under <paramref name="key"/> was removed, or null.</summary>
    long? RemovedAt(string key);

    void SetRemoved(string key, long when);

    void ForgetRemoved(string key);

    /// <summary>Forgets removals older than <paramref name="before"/>: they have reached every copy long ago.</summary>
    void PruneRemoved(long before);

    /// <summary>Every remembered removal, for a backup.</summary>
    IReadOnlyDictionary<string, long> AllRemoved();

    /// <summary>Runs <paramref name="work"/> as one write: all of it is kept, or none of it.</summary>
    T Batch<T>(Func<T> work);
}
