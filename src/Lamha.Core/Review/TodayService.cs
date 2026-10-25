using System.Text.Json;
using Lamha.Core.Abstractions;
using Lamha.Core.Dictionary;
using Lamha.Core.Settings;
using Lamha.Core.Translation;

namespace Lamha.Core.Review;

/// <summary>The word of the day, the same until midnight (Lamha 2's <c>wotd</c>).</summary>
public sealed record WordOfDay
{
    public DateOnly Day { get; init; }

    public required string Word { get; init; }

    public string Meaning { get; init; } = "";

    public string Definition { get; init; } = "";

    public string Example { get; init; } = "";

    /// <summary>Lamha's part-of-speech code, when it came from the dictionary.</summary>
    public string Pos { get; init; } = "";

    public string Ipa { get; init; } = "";

    /// <summary>A word of the user's own that's due soon, to refresh it; else a new one from the dictionary.</summary>
    public bool FromDeck { get; init; }
}

/// <summary>What the Today card shows.</summary>
/// <param name="Done">Words looked up and review answers today.</param>
/// <param name="Goal">The day's goal (0: none).</param>
/// <param name="Streak">Days in a row with practice; today not started yet doesn't break it.</param>
/// <param name="Word">The word of the day, or null.</param>
public sealed record TodayInfo(int Done, int Goal, int Streak, WordOfDay? Word);

/// <summary>
/// Today: the daily goal, the practice streak and the word of the day (Lamha 2's today section). The day's practice
/// is words new to the history plus review answers, kept for 60 days.
/// </summary>
public sealed class TodayService(IActivityStore activity, IStateStore state, IDeckStore deck, IHistoryStore history, SettingsService settings, TimeProvider time, LocalDictionary? dictionary = null)
{
    private const string WordKey = "wotd";
    private const string LookupCountKey = "lookupCount";
    private static readonly int[] Milestones = [10, 50, 100, 250, 500, 1000, 2000, 5000, 10000];
    private readonly Lock _gate = new();

    /// <summary>Counts a looked-up word or a review answer for today; returns the goal when this one reached it, else 0.</summary>
    public int CountActivity()
    {
        var today = Days.Today(time);
        var n = activity.Add(today, today.AddDays(-59));
        var goal = settings.Current.DailyGoal;
        return goal > 0 && n == goal ? goal : 0;
    }

    /// <summary>Counts a word looked up; returns the count when it just reached a milestone (10, 50, 100…), else 0.</summary>
    public int CountLookup()
    {
        lock (_gate)
        {
            var n = (int.TryParse(state.Get(LookupCountKey), out var c) ? c : 0) + 1;
            state.Set(LookupCountKey, n.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return Milestones.Contains(n) ? n : 0;
        }
    }

    public int LookupCount => int.TryParse(state.Get(LookupCountKey), out var c) ? c : 0;

    public TodayInfo Info()
    {
        var today = Days.Today(time);
        WordOfDay? word = null;
        try
        {
            word = Word();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or JsonException or System.Data.Common.DbException)
        {
            // The goal still shows.
        }

        return new TodayInfo(activity.Get(today), settings.Current.DailyGoal, Streak(today), word);
    }

    /// <summary>Days in a row with some practice, counting back from today, or from yesterday if today hasn't started.</summary>
    public int Streak(DateOnly today)
    {
        var n = 0;
        for (var back = activity.Get(today) > 0 ? 0 : 1; activity.Get(today.AddDays(-back)) > 0; back++)
        {
            n++;
        }

        return n;
    }

    /// <summary>
    /// One word a day: a word from the deck that's due (or nearly), to refresh it; otherwise a new one from the
    /// dictionary. The same until midnight (Lamha 2's <c>wordOfDay</c>).
    /// </summary>
    public WordOfDay? Word()
    {
        var today = Days.Today(time);
        var saved = state.Get(WordKey) is { } json ? JsonSerializer.Deserialize(json, DeckJson.Default.WordOfDay) : null;
        if (saved is not null && saved.Day == today && saved.Word.Length > 0)
        {
            return saved;
        }

        var previous = saved is null ? "" : Card.KeyOf(saved.Word);
        var now = Days.NowMs(time);
        WordOfDay? word = null;
        if (deck.FirstDue(now + (3 * Scheduler.Day), previous) is { } card)
        {
            word = new WordOfDay
            {
                Word = card.Word,
                Meaning = card.Meaning,
                Definition = card.EnglishFirst ? card.Definition : "",
                Example = card.Example,
                FromDeck = true,
            };
        }
        else if (dictionary?.WordOfDay(now / Scheduler.Day, w => w == previous || deck.Get(w) is not null || history.Contains(w)) is { } d)
        {
            word = new WordOfDay { Word = d.Word, Meaning = d.Meaning, Definition = d.Definition, Example = d.Example, Pos = d.Pos.Code, Ipa = d.Ipa };
        }

        if (word is null)
        {
            return null;
        }

        word = word with { Day = today };
        state.Set(WordKey, JsonSerializer.Serialize(word, DeckJson.Default.WordOfDay));
        return word;
    }
}

/// <summary>Turns a lookup into a review card.</summary>
public static class CardFrom
{
    /// <summary>
    /// The lookup's word, its meaning in the sentence (else its first), the first definition, and the sentence it was
    /// found in (Lamha 2's <c>cardFromLookup</c>).
    /// </summary>
    public static CardDraft Lookup(LookupResult result, string selected, WordContext? context)
    {
        var example = context is null ? "" : Cut(System.Text.RegularExpressions.Regex.Replace(context.Before + selected + context.After, @"\s+", " ").Trim(), 300);
        if (result.Explain)
        {
            // The definition that fits the sentence, with the meaning under it.
            return new CardDraft(result.Query, result.Meaning, example, selected, result.Translation, EnglishFirst: true);
        }

        var inSentence = result.Context is { Untranslated: false } c ? c.Word : "";
        var definition = result.Definitions.FirstOrDefault()?.Entries.FirstOrDefault()?.Gloss ?? "";
        return new CardDraft(result.Query, inSentence.Length > 0 ? inSentence : result.Translation, example, selected, definition);
    }

    private static string Cut(string s, int max) => s.Length > max ? s[..max] : s;
}
