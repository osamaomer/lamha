using Lamha.Core.Abstractions;
using Lamha.Core.Review;
using Lamha.Core.Tests.Support;

namespace Lamha.Core.Tests.Review;

/// <summary>The History page's history: by day on this PC's calendar, its search, and which words are in review.</summary>
public class HistoryTests
{
    private const long Hour = 60 * 60 * 1000;

    private static TestLamha Lamha(TimeZoneInfo? zone = null) => new(new FakeNet(_ => FakeNet.Json(200, "{}")), zone: zone);

    [Fact]
    public void DaysFollowThisPcsCalendarNewestFirst()
    {
        // Riyadh is UTC+3: 23:00 UTC on 23 October is already the 24th there.
        using var lamha = Lamha(TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh"));
        var now = lamha.Now; // 24 October, 12:00 in Riyadh
        lamha.History.Add(new HistoryEntry("commute", "التنقّل اليومي", "en", now - (13 * Hour)), HistoryService.Keep); // 23:00 on the 23rd
        lamha.History.Add(new HistoryEntry("reluctant", "متردّد", "en", now - (10 * Hour)), HistoryService.Keep); // 02:00 on the 24th
        lamha.History.Add(new HistoryEntry("settle", "يسدّد", "en", now), HistoryService.Keep);

        var days = lamha.HistoryService.ByDay();
        Assert.Equal([new DateOnly(2026, 10, 24), new DateOnly(2026, 10, 23)], days.Select(d => d.Day));
        Assert.Equal(["settle", "reluctant"], days[0].Entries.Select(e => e.Word));
        Assert.Equal(["commute"], days[1].Entries.Select(e => e.Word));
        Assert.Equal(new DateOnly(2026, 10, 24), lamha.HistoryService.Today);
        Assert.Equal(new DateTime(2026, 10, 24, 2, 0, 0), lamha.HistoryService.LocalTime(days[0].Entries[1]));
    }

    [Fact]
    public void SearchFindsTheWordOrItsMeaningWithArabicVariantsAndCaseAside()
    {
        using var lamha = Lamha();
        lamha.History.Add(new HistoryEntry("Bank", "مَصْرِف", "en", 1), HistoryService.Keep);
        lamha.History.Add(new HistoryEntry("as soon as possible", "في أقرب وقت ممكن", "en", 2), HistoryService.Keep);
        lamha.History.Add(new HistoryEntry("مسؤول", "responsible", "ar", 3), HistoryService.Keep);

        string[] Found(string query) => [.. lamha.HistoryService.ByDay(query).SelectMany(d => d.Entries).Select(e => e.Word)];

        Assert.Equal(["Bank"], Found("bank"));
        Assert.Equal(["Bank"], Found("مصرف")); // without its marks
        Assert.Equal(["as soon as possible"], Found("اقرب")); // أ as ا
        Assert.Equal(["مسؤول"], Found("مسوول"));
        Assert.Equal(["as soon as possible"], Found("soon ممكن")); // every word, in the word or its meaning
        Assert.Empty(Found("soon bank"));
        Assert.Equal(3, Found("  ").Length);
    }

    [Fact]
    public async Task TheHistoryKeeps1000WordsAndTheDeckImportsLamha2s100()
    {
        using var lamha = new TestLamha(new FakeNet(_ => FakeNet.Json(200, "{}")), new() { UiLang = "ar", CardsAuto = true });
        for (var i = 1; i <= 1001; i++)
        {
            lamha.History.Add(new HistoryEntry("w" + i, "ك", "en", i), HistoryService.Keep);
        }

        Assert.Equal(1000, lamha.HistoryService.Count());
        Assert.False(lamha.HistoryService.Contains("w1")); // the oldest went
        Assert.Equal(100, (await lamha.Review.QueueAsync()).Total); // the first queue makes cards of the newest 100 only
        Assert.NotNull(lamha.Deck.Get("w1001"));
        Assert.Null(lamha.Deck.Get("w901"));
    }

    [Fact]
    public async Task InReviewSaysWhichWordsAreInTheDeckInOneGo()
    {
        using var lamha = Lamha();
        await lamha.Review.AddAsync(new CardDraft("settle", "يسدّد"));
        await lamha.Review.AddAsync(new CardDraft("Nile", "النيل"));

        var keys = await lamha.Review.InReviewAsync(["Settle", "nile", "reluctant", ""]);
        Assert.Equal(["nile", "settle"], keys.Order());
    }
}
