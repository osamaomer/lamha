using System.Text.Json;
using System.Text.Json.Serialization;
using Lamha.Core.Abstractions;
using Lamha.Core.Settings;

namespace Lamha.Core.Review;

/// <summary>A word to add to review: from a lookup, the bookmark, or the history.</summary>
public sealed record CardDraft(string Word, string Meaning = "", string Example = "", string Form = "", string Definition = "", bool EnglishFirst = false);

/// <summary>A card waiting in review, with how long each answer would put it off.</summary>
public sealed record QueuedCard(Card Card, TimeSpan Again, TimeSpan Hard, TimeSpan Good);

/// <summary>What the review screen shows.</summary>
/// <param name="Cards">Due cards first (the longest waiting first), then today's share of new ones (the newest first), at most 100.</param>
/// <param name="Due">Due cards.</param>
/// <param name="New">New cards left for today.</param>
/// <param name="Total">Every card.</param>
/// <param name="Learned">Cards with an interval of three weeks or more.</param>
/// <param name="NextDue">When the next card falls due, or null.</param>
/// <param name="Streak">Days in a row with a review, counting today; 0 when nothing was reviewed today yet.</param>
public sealed record ReviewQueue(IReadOnlyList<QueuedCard> Cards, int Due, int New, int Total, int Learned, long? NextDue, int Streak);

/// <summary>The deck's daily counters (Lamha 2's <c>cardStats</c>).</summary>
public sealed record DeckStats
{
    /// <summary>The day <see cref="NewSeen"/> counts.</summary>
    public DateOnly? Day { get; init; }

    /// <summary>New words reviewed for the first time that day.</summary>
    public int NewSeen { get; init; }

    /// <summary>The last day with a review.</summary>
    public DateOnly? ReviewDay { get; init; }

    /// <summary>Days in a row with a review, up to <see cref="ReviewDay"/>.</summary>
    public int Streak { get; init; }
}

/// <summary>
/// The review deck: adding and removing words, the queue and the answers. Lamha 2's flashcards section, with the deck
/// queried from SQLite instead of kept in memory. Changes are made one at a time (a lookup's automatic card and a
/// review answer can arrive together).
/// </summary>
public sealed class ReviewService(IDeckStore deck, IStateStore state, IHistoryStore history, SettingsService settings, TodayService today, TimeProvider time)
{
    /// <summary>A removal older than this has reached every copy long ago (Lamha 2's <c>REMOVED_KEEP_MS</c>).</summary>
    public const long RemovedKeep = 180 * Scheduler.Day;

    private const string StatsKey = "deck.stats";
    private const string ImportedKey = "deck.imported";
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Adds the word if it's new; a word already there keeps its progress and gets what it was missing.</summary>
    public Task<bool> AddAsync(CardDraft draft) => Locked(() => Put(draft));

    /// <summary>The card's bookmark: adds the word if it's missing, else removes it. Returns whether it's in the deck now.</summary>
    public Task<bool> ToggleAsync(CardDraft draft) => Locked(() => deck.Get(Card.KeyOf(draft.Word)) is null ? Put(draft) : !Drop(Card.KeyOf(draft.Word)));

    public Task<bool> RemoveAsync(string key) => Locked(() => Drop(key));

    /// <summary>Waits for changes on their way (a lookup's automatic card) before answering.</summary>
    public Task<bool> HasAsync(string word) => Locked(() => deck.Get(Card.KeyOf(word)) is not null);

    /// <summary>Settings → delete all cards: the words and the progress, each noted as removed; the history isn't turned into cards again.</summary>
    public Task ClearAsync() => Locked(() =>
    {
        deck.Clear(Days.NowMs(time));
        state.Set(StatsKey, null);
        state.Set(ImportedKey, "true");
        return true;
    });

    /// <summary>The cards to review now (Lamha 2's <c>reviewQueue</c>).</summary>
    public Task<ReviewQueue> QueueAsync() => Locked(() =>
    {
        ImportHistoryOnce();
        var now = Days.NowMs(time);
        var stats = Stats();
        var todayDate = Days.Today(time);
        var seen = stats.Day == todayDate ? stats.NewSeen : 0;
        var perDay = settings.Current.CardsNewPerDay > 0 ? settings.Current.CardsNewPerDay : 10;
        var newLeft = Math.Max(0, perDay - seen);

        var counts = deck.Counts(now);
        var due = deck.Due(now, 100);
        var fresh = newLeft > 0 ? deck.NewestNew(newLeft) : [];
        var cards = due.Concat(fresh).Take(100).Select(c => new QueuedCard(
            c,
            TimeSpan.FromMilliseconds(Scheduler.Schedule(c, Grade.Again, now).Due - now),
            TimeSpan.FromMilliseconds(Scheduler.Schedule(c, Grade.Hard, now).Due - now),
            TimeSpan.FromMilliseconds(Scheduler.Schedule(c, Grade.Good, now).Due - now))).ToList();
        var streak = stats.ReviewDay == todayDate ? Math.Max(stats.Streak, 1) : 0;
        return new ReviewQueue(cards, counts.Due, fresh.Count, counts.Total, counts.Learned, counts.NextDue, streak);
    });

    /// <summary>
    /// An answer: reschedules the card, counts a new word toward today's limit, keeps the review streak, and counts
    /// toward the day's goal. Returns the goal when this answer reached it, else 0 (Lamha 2's <c>reviewGrade</c>).
    /// </summary>
    public async Task<int> GradeAsync(string key, Grade grade)
    {
        await Locked(() =>
        {
            var card = deck.Get(key);
            if (card is null)
            {
                return false;
            }

            var todayDate = Days.Today(time);
            var stats = Stats();
            if (card.IsNew)
            {
                stats = stats.Day == todayDate ? stats with { NewSeen = stats.NewSeen + 1 } : stats with { Day = todayDate, NewSeen = 1 };
            }

            if (stats.ReviewDay != todayDate)
            {
                // Yesterday too: one more day; otherwise it starts again.
                stats = stats with { Streak = stats.ReviewDay == todayDate.AddDays(-1) ? stats.Streak + 1 : 1, ReviewDay = todayDate };
            }

            var now = Days.NowMs(time);
            deck.Batch(() =>
            {
                deck.Put(Scheduler.Schedule(card, grade, now) with { Modified = now });
                SaveStats(stats);
                return true;
            });
            return true;
        }).ConfigureAwait(false);
        return today.CountActivity();
    }

    public DeckStats Stats() => state.Get(StatsKey) is { } json ? JsonSerializer.Deserialize(json, DeckJson.Default.DeckStats) ?? new() : new();

    /// <summary>Runs <paramref name="work"/> with the deck to itself.</summary>
    internal async Task<T> Locked<T>(Func<T> work)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return work();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Lamha 2's <c>putCard</c>: true when the word was added.</summary>
    private bool Put(CardDraft draft)
    {
        var key = Card.KeyOf(draft.Word);
        // English–English cards may have only the definition.
        if (key.Length == 0 || !(draft.Meaning.Length > 0 || (draft.EnglishFirst && draft.Definition.Length > 0)))
        {
            return false;
        }

        var now = Days.NowMs(time);
        var old = deck.Get(key);
        if (old is not null)
        {
            var filled = old with
            {
                Meaning = old.Meaning.Length > 0 ? old.Meaning : draft.Meaning,
                Example = old.Example.Length > 0 ? old.Example : draft.Example,
                Form = old.Form.Length > 0 ? old.Form : draft.Form,
                Definition = old.Definition.Length > 0 ? old.Definition : draft.Definition,
            };
            if (filled != old)
            {
                deck.Put(filled with { Modified = now });
            }

            return false;
        }

        deck.Batch(() =>
        {
            deck.Put(new Card
            {
                Word = draft.Word,
                Meaning = Cut(draft.Meaning, 200),
                Example = draft.Example,
                Form = draft.Form,
                Definition = Cut(draft.Definition, 300),
                EnglishFirst = draft.EnglishFirst,
                Added = now,
                Modified = now,
            });
            deck.ForgetRemoved(key); // added again after it was removed
            deck.PruneRemoved(now - RemovedKeep);
            return true;
        });
        return true;
    }

    /// <summary>Removes a word, noting when, so its copies elsewhere (a backup) go too.</summary>
    private bool Drop(string key) => deck.Batch(() =>
    {
        if (!deck.Delete(key))
        {
            return false;
        }

        var now = Days.NowMs(time);
        deck.SetRemoved(key, now);
        deck.PruneRemoved(now - RemovedKeep);
        return true;
    });

    /// <summary>
    /// Once: words looked up before review existed become cards, when lookups add words by themselves; otherwise the user
    /// chooses each word (Lamha 2's <c>importHistory</c>).
    /// </summary>
    private void ImportHistoryOnce()
    {
        if (state.Get(ImportedKey) == "true")
        {
            return;
        }

        if (settings.Current.CardsAuto)
        {
            foreach (var h in history.Recent(HistoryService.Keep).Reverse().Where(h => h.Language == "en"))
            {
                Put(new CardDraft(h.Word, h.Meaning));
            }
        }

        state.Set(ImportedKey, "true");
    }

    private void SaveStats(DeckStats stats) => state.Set(StatsKey, JsonSerializer.Serialize(stats, DeckJson.Default.DeckStats));

    private static string Cut(string s, int max) => s.Length > max ? s[..max] : s;
}

/// <summary>Review's small records as JSON, without reflection (NativeAOT).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DeckStats))]
[JsonSerializable(typeof(WordOfDay))]
internal sealed partial class DeckJson : JsonSerializerContext;
