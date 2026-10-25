using Lamha.Core.Abstractions;
using Lamha.Core.Review;
using Lamha.Core.Settings;
using Lamha.Core.Tests.Support;

namespace Lamha.Core.Tests.Review;

/// <summary>Lamha 2's test-writing.mjs: flashcards (SM-2), the queue, the streak, Today and the word of the day.</summary>
public class ReviewTests
{
    private const long Day = Scheduler.Day;
    private static readonly Card NewCard = new() { Word = "bank", Meaning = "ضفة", Added = 1 };

    private static TestLamha Lamha(LamhaSettings? settings = null, bool imported = true, bool realDict = false, TimeZoneInfo? zone = null, DateTimeOffset? start = null)
    {
        var lamha = new TestLamha(new FakeNet(_ => FakeNet.Json(200, "{}")), settings, realDict: realDict, zone: zone, start: start);
        if (imported)
        {
            lamha.State.Set("deck.imported", "true");
        }

        return lamha;
    }

    [Fact]
    public void ScheduleAgainIs10MinutesHardIsBeforeGoodAndIntervalsGrow()
    {
        const long Now = 1_000_000_000_000;
        var again = Scheduler.Schedule(NewCard, Grade.Again, Now);
        Assert.Equal(10 * Scheduler.Minute, again.Due - Now);
        Assert.Equal(1, again.Lapses);
        Assert.Equal(2.3, again.Ease, 10);
        Assert.Equal(1, Scheduler.Schedule(NewCard, Grade.Hard, Now).Interval);
        var c = Scheduler.Schedule(NewCard, Grade.Good, Now);
        Assert.Equal(2, c.Interval);
        c = Scheduler.Schedule(c, Grade.Good, Now);
        Assert.Equal(5, c.Interval);
        c = Scheduler.Schedule(c, Grade.Good, Now);
        Assert.Equal(13, c.Interval);
        Assert.Equal(13 * Day, c.Due - Now);
        // Hard is never later than good, even with the lowest ease.
        foreach (var card in new[] { NewCard, NewCard with { Reps = 1, Interval = 2 }, NewCard with { Reps = 4, Interval = 5, Ease = 1.3 }, NewCard with { Reps = 6, Interval = 40 } })
        {
            Assert.True(Scheduler.Schedule(card, Grade.Hard, Now).Due <= Scheduler.Schedule(card, Grade.Good, Now).Due, card.ToString());
        }

        Assert.Equal(0, NewCard.Reps); // the input isn't changed
    }

    [Fact]
    public async Task TheQueueImportsTheHistoryOnceDueFirstNewWordsLimitedPerDay()
    {
        using var lamha = Lamha(new LamhaSettings { UiLang = "ar", CardsNewPerDay = 5, CardsAuto = true }, imported: false);
        lamha.History.Add(new HistoryEntry("bank", "مصرف", "en", 1), 100);
        lamha.History.Add(new HistoryEntry("مرحبا", "hello", "ar", 2), 100);
        lamha.History.Add(new HistoryEntry("serendipity", "صدفة سعيدة", "en", 3), 100);
        var past = lamha.Now - 1000;
        for (var i = 0; i < 15; i++)
        {
            lamha.Deck.Put(NewCard with { Word = "new" + i, Added = i });
        }

        lamha.Deck.Put(NewCard with { Word = "old", Reps = 2, Interval = 5, Last = past - (5 * Day), Due = past });
        lamha.Deck.Put(NewCard with { Word = "later", Reps = 1, Interval = 2, Last = past, Due = lamha.Now + Day });

        var r = await lamha.Review.QueueAsync();
        Assert.Equal(19, r.Total); // 15 new + old + later + 2 English history words
        Assert.Null(lamha.Deck.Get("مرحبا")); // Arabic lookups are not cards
        Assert.Equal("old", r.Cards[0].Card.Key); // due review comes first
        Assert.Equal(1, r.Due);
        Assert.Equal(5, r.New); // the daily new-word limit
        Assert.Equal(6, r.Cards.Count);
        Assert.All(r.Cards.Skip(1), c => Assert.True(c.Card.IsNew));
        Assert.True(r.NextDue > lamha.Now);
        Assert.Equal(TimeSpan.FromMinutes(10), r.Cards[0].Again);

        // The import happens only once: a removed history word doesn't come back.
        await lamha.Review.RemoveAsync("bank");
        Assert.Equal(18, (await lamha.Review.QueueAsync()).Total);
    }

    [Fact]
    public async Task NewWordsCountTowardTodaysLimitAndAgainComesBackLater()
    {
        using var lamha = Lamha(new LamhaSettings { UiLang = "ar", CardsNewPerDay = 2 });
        for (var i = 0; i < 3; i++)
        {
            lamha.Deck.Put(NewCard with { Word = "w" + i, Added = i });
        }

        var r = await lamha.Review.QueueAsync();
        Assert.Equal(2, r.New);
        await lamha.Review.GradeAsync(r.Cards[0].Card.Key, Grade.Good);
        r = await lamha.Review.QueueAsync();
        Assert.Equal(1, r.New); // one new word left today
        await lamha.Review.GradeAsync(r.Cards[0].Card.Key, Grade.Again);
        r = await lamha.Review.QueueAsync();
        Assert.Equal(0, r.New); // limit reached
        Assert.Equal(0, r.Due); // "again" comes back in 10 minutes, not now
        Assert.Equal(2, lamha.Review.Stats().NewSeen);
        Assert.Equal(2, lamha.Deck.All().Count(c => !c.IsNew));
    }

    [Fact]
    public async Task TheBookmarkTogglesACardAndHasSeesItInAnyCase()
    {
        using var lamha = Lamha();
        var card = new CardDraft("Resilient", "مرن", "She is resilient.", "resilient", "able to recover");
        Assert.False(await lamha.Review.HasAsync("resilient"));
        Assert.True(await lamha.Review.ToggleAsync(card));
        Assert.True(await lamha.Review.HasAsync("RESILIENT"));
        Assert.Equal("She is resilient.", lamha.Deck.Get("resilient")!.Example);
        Assert.False(await lamha.Review.ToggleAsync(card));
        Assert.False(await lamha.Review.HasAsync("resilient"));
    }

    [Fact]
    public async Task WordsNamedLikeObjectPropertiesAreOrdinaryCards()
    {
        using var lamha = Lamha();
        foreach (var q in new[] { "constructor", "__proto__", "toString" })
        {
            Assert.False(await lamha.Review.HasAsync(q));
            Assert.True(await lamha.Review.ToggleAsync(new CardDraft(q, "x", "a " + q)));
            Assert.True(await lamha.Review.HasAsync(q));
        }

        Assert.Equal(["__proto__", "constructor", "tostring"], lamha.Deck.All().Select(c => c.Key).Order(StringComparer.Ordinal));
        Assert.False(await lamha.Review.ToggleAsync(new CardDraft("constructor", "x")));
        Assert.False(await lamha.Review.HasAsync("constructor"));
    }

    [Fact]
    public async Task TheReviewStreakCountsUpOnceADayAndAMissedDayStartsAgain()
    {
        using (var lamha = Lamha())
        {
            foreach (var q in new[] { "a", "b" })
            {
                lamha.Deck.Put(NewCard with { Word = q, Meaning = "x" });
            }

            var today = Days.Today(lamha.Clock);
            lamha.State.Set("deck.stats", $$"""{ "reviewDay": "{{today.AddDays(-1):yyyy-MM-dd}}", "streak": 4 }""");
            Assert.Equal(0, (await lamha.Review.QueueAsync()).Streak); // nothing reviewed today yet
            await lamha.Review.GradeAsync("a", Grade.Good);
            Assert.Equal(5, (await lamha.Review.QueueAsync()).Streak);
            await lamha.Review.GradeAsync("b", Grade.Good);
            Assert.Equal(5, (await lamha.Review.QueueAsync()).Streak); // once per day
            Assert.Equal(2, lamha.Review.Stats().NewSeen); // the daily new-word count is kept alongside
        }

        using var gap = Lamha();
        gap.Deck.Put(NewCard with { Word = "a" });
        gap.State.Set("deck.stats", $$"""{ "reviewDay": "{{Days.Today(gap.Clock).AddDays(-3):yyyy-MM-dd}}", "streak": 9 }""");
        await gap.Review.GradeAsync("a", Grade.Again);
        Assert.Equal(1, (await gap.Review.QueueAsync()).Streak);
    }

    [Fact]
    public async Task TodaysStreakIsDaysInARowWithPracticeAndNotPractisedYetKeepsYesterdays()
    {
        using var lamha = Lamha();
        var today = Days.Today(lamha.Clock);
        lamha.Activity.RaiseTo(today.AddDays(-1), 4);
        lamha.Activity.RaiseTo(today.AddDays(-2), 1);
        lamha.Activity.RaiseTo(today.AddDays(-4), 7);
        lamha.Deck.Put(NewCard with { Word = "a" });
        Assert.Equal(2, lamha.Today.Info().Streak); // yesterday and the day before; day 4 is past a gap
        await lamha.Review.GradeAsync("a", Grade.Good);
        var t = lamha.Today.Info();
        Assert.Equal((1, 3), (t.Done, t.Streak));
        Assert.True(lamha.Activity.All().Count <= 60);
    }

    [Fact]
    public void TheWordOfTheDayIsAWordOfYoursDueSoonElseANewOneTheSameAllDay()
    {
        using (var lamha = Lamha())
        {
            lamha.Deck.Put(NewCard with { Word = "bank", Last = lamha.Now - (5 * Day), Due = lamha.Now + Day, Reps = 2, Interval = 6 });
            lamha.Deck.Put(NewCard with { Word = "river", Meaning = "نهر", Last = lamha.Now, Due = lamha.Now + (30 * Day), Reps = 4, Interval = 30 });
            var w = lamha.Today.Info().Word!;
            Assert.Equal(("bank", "ضفة", true), (w.Word, w.Meaning, w.FromDeck));
        }

        using var fresh = Lamha(realDict: true);
        fresh.Deck.Put(NewCard with { Word = "river", Meaning = "نهر", Last = fresh.Now, Due = fresh.Now + (30 * Day), Reps = 4, Interval = 30 });
        fresh.History.Add(new HistoryEntry("menace", "تهديد", "en", 1), 100);
        var d = fresh.Today.Info().Word!;
        Assert.False(d.FromDeck);
        Assert.Matches("^[a-z]{5,12}$", d.Word);
        Assert.NotEmpty(d.Meaning);
        Assert.NotEmpty(d.Definition);
        Assert.True(d.Example.Length == 0 || d.Example.Contains(d.Word[..4], StringComparison.OrdinalIgnoreCase)); // an example only when it uses the word
        Assert.DoesNotContain(d.Word, new[] { "river", "menace" });
        Assert.Equal(d.Word, fresh.Today.Info().Word!.Word); // the same all day
    }

    [Fact]
    public async Task DaysGoByTheCalendarTheNightTheClocksGoForwardBreaksNoStreak()
    {
        // Egypt moved its clocks forward at midnight on Friday 24 April 2026; it's 00:30 on Saturday (still Friday in UTC).
        var cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 4, 25, 0, 30, 0, DateTimeKind.Unspecified), cairo), TimeSpan.Zero);
        using var lamha = Lamha(zone: cairo, start: start);
        lamha.Deck.Put(NewCard with { Word = "a", Meaning = "أ" });
        lamha.Activity.RaiseTo(new DateOnly(2026, 4, 23), 2);
        lamha.Activity.RaiseTo(new DateOnly(2026, 4, 24), 3);
        lamha.State.Set("deck.stats", """{ "reviewDay": "2026-04-24", "streak": 4 }""");

        Assert.Equal(new DateOnly(2026, 4, 25), Days.Today(lamha.Clock));
        Assert.Equal(2, lamha.Today.Info().Streak); // Friday and Thursday, not a gap on Friday
        await lamha.Review.GradeAsync("a", Grade.Good);
        Assert.Equal(5, (await lamha.Review.QueueAsync()).Streak); // yesterday's review streak goes on
        Assert.Equal(3, lamha.Activity.Get(new DateOnly(2026, 4, 24))); // yesterday's practice is kept
        Assert.Equal(3, lamha.Today.Info().Streak);
    }

    [Fact]
    public async Task DeletingAllCardsIsNeverUndone()
    {
        using var lamha = Lamha();
        for (var i = 0; i < 50; i++)
        {
            lamha.Deck.Put(NewCard with { Word = "w" + i, Meaning = "ك" });
        }

        await lamha.Review.ClearAsync();
        Assert.Equal(0, (await lamha.Review.QueueAsync()).Total);
        await lamha.Review.ToggleAsync(new CardDraft("candid", "صريح"));
        Assert.Equal(["candid"], lamha.Deck.All().Select(c => c.Key));
        Assert.Equal(lamha.Now, lamha.Deck.RemovedAt("w7")); // each word noted as removed, for backups
    }

    [Fact]
    public async Task AWordAlreadyInTheDeckKeepsItsProgressAndGetsWhatItWasMissing()
    {
        using var lamha = Lamha();
        lamha.Deck.Put(NewCard with { Word = "bank", Example = "", Reps = 3, Last = 5 });
        Assert.False(await lamha.Review.AddAsync(new CardDraft("Bank", "مصرف", "At the bank.")));
        var card = lamha.Deck.Get("bank")!;
        Assert.Equal(("ضفة", "At the bank.", 3), (card.Meaning, card.Example, card.Reps));
        Assert.False(await lamha.Review.AddAsync(new CardDraft("nothing")), "a card needs a meaning");
    }
}
