using Lamha.Core.Abstractions;
using Lamha.Core.Settings;

namespace Lamha.Core.Review;

/// <summary>
/// The tray's count of words waiting for review, and when to remind: Lamha 2's <c>setBadge()</c> in main.js, with the
/// count of background.js <c>updateBadge()</c> (due + new, as Review counts them). Read straight from the deck, so the
/// tray needs nothing a lookup builds.
/// </summary>
public sealed class ReviewReminder(IDeckStore deck, IStateStore state, SettingsService settings, TimeProvider time)
{
    /// <summary>At most one reminder this often (Lamha 2's <c>4 * 3600e3</c>).</summary>
    public static readonly TimeSpan Every = TimeSpan.FromHours(4);

    // In memory, as Lamha 2's lastReminder: a start may remind again.
    private long? _lastReminder;

    /// <summary>Words waiting now: the due ones and today's new ones.</summary>
    public int Waiting { get; private set; }

    /// <summary>
    /// Counts the waiting words again. True when it's time for a reminder: some are waiting, more than at the last count
    /// (or there's been no reminder since Lamha started), <paramref name="mayRemind"/> (Lamha is on and its window isn't
    /// in use), and the last reminder was more than 4 hours ago.
    /// </summary>
    public bool Recount(bool mayRemind)
    {
        var now = Days.NowMs(time);
        var counts = deck.Counts(now);
        var n = counts.Due + Math.Min(counts.New, NewLeft());
        var grew = n > Waiting;
        Waiting = n;
        if (n > 0 && (grew || _lastReminder is null) && mayRemind && (_lastReminder is not { } last || now - last > (long)Every.TotalMilliseconds))
        {
            _lastReminder = now;
            return true;
        }

        return false;
    }

    /// <summary>The word a reminder asks about: Review's first (the longest-waiting due word, else the newest new one).</summary>
    public Card? First()
    {
        if (deck.Due(Days.NowMs(time), 1) is [var due])
        {
            return due;
        }

        return NewLeft() > 0 && deck.NewestNew(1) is [var fresh] ? fresh : null;
    }

    /// <summary>The card under <paramref name="key"/>, if it's still in the deck.</summary>
    public Card? Find(string key) => deck.Get(key);

    private int NewLeft() => ReviewService.NewLeft(ReviewService.ReadStats(state), settings.Current, Days.Today(time));
}
