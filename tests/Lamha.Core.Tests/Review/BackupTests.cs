using System.Text.Json.Nodes;
using Lamha.Core.Abstractions;
using Lamha.Core.Review;
using Lamha.Core.Settings;
using Lamha.Core.Tests.Support;
using Lamha.Core.Writing;

namespace Lamha.Core.Tests.Review;

/// <summary>Lamha 2's test-writing.mjs: a copy of my data, and Report a problem.</summary>
public class BackupTests
{
    private const long Day = Scheduler.Day;

    private static TestLamha Lamha(FakeNet? net = null, LamhaSettings? settings = null, Dictionary<string, string>? keys = null)
    {
        var lamha = new TestLamha(net ?? new FakeNet(_ => FakeNet.Json(404, "{}")), settings, keys);
        lamha.State.Set("deck.imported", "true");
        return lamha;
    }

    private static Card DeckCard(TestLamha l, string q) => new() { Word = q, Meaning = "ك", Added = l.Now - (30 * Day), Modified = l.Now - (30 * Day) };

    [Fact]
    public async Task ACopyIsSavedAndRestoredElsewhereNothingThereIsLostAndARemovedWordStaysRemoved()
    {
        string saved;
        using (var here = Lamha())
        {
            here.Deck.Put(DeckCard(here, "apple") with { Last = here.Now - (10 * Day), Reps = 2, Interval = 5, Due = 9_000_000_000_000, Example = "An apple a day." });
            here.Deck.Put(DeckCard(here, "pear"));
            here.History.Add(new HistoryEntry("apple", "تفاحة", "en", here.Now - (10 * Day)), 100);
            here.Mistakes.Replace(new MistakeJournal(5, new Dictionary<string, int> { ["articles"] = 3 }, [], 0));
            saved = await here.Backup.ExportAsync();
        }

        var file = JsonNode.Parse(saved)!;
        Assert.Equal(("lamha", 1), (file["app"]!.GetValue<string>(), file["format"]!.GetValue<int>()));
        Assert.Equal(["apple", "pear"], file["cards"]!.AsObject().Select(c => c.Key).Order(StringComparer.Ordinal));

        using var there = Lamha();
        there.Deck.Put(DeckCard(there, "apple") with { Last = there.Now - (2 * Day), Reps = 3, Interval = 12 });
        there.Deck.Put(DeckCard(there, "kiwi"));
        there.Deck.SetRemoved("pear", there.Now - (5 * Day)); // pear was removed there after it last changed
        Assert.Equal(new RestoreResult(1, 1), await there.Backup.ImportAsync(saved));
        Assert.Equal(["apple", "kiwi"], there.Deck.All().Select(c => c.Key)); // kiwi kept; pear stays removed
        var apple = there.Deck.Get("apple")!;
        Assert.Equal((3, 12, "An apple a day."), (apple.Reps, apple.Interval, apple.Example)); // the later review's schedule, the missing example filled in
        Assert.Equal(5, there.Mistakes.Read().Checks); // the journal that has seen more
        Assert.Equal(new RestoreResult(0, 0), await there.Backup.ImportAsync(saved)); // restoring twice adds nothing
    }

    [Fact]
    public async Task AHostileFileGivesOrdinaryCardsAndNothingElse()
    {
        using var there = Lamha();
        var hostile = """
            { "app": "lamha", "format": 1, "history": "no", "activity": { "__proto__": 5, "Mon Sep 28 2026": 3, "x": 1e9 },
              "cards": { "a": { "q": "__proto__", "tr": "بروتو" }, "b": { "q": 5 }, "c": { "q": "okay", "tr": "ENDLESS", "ease": 99, "reps": -3, "en": "yes" }, "d": null } }
            """.Replace("ENDLESS", new string('ع', 5000), StringComparison.Ordinal);
        await there.Backup.ImportAsync(hostile);
        Assert.Equal("بروتو", there.Deck.Get("__proto__")!.Meaning); // an ordinary word
        var okay = there.Deck.Get("okay")!;
        Assert.Equal((200, 5.0, 0, false), (okay.Meaning.Length, okay.Ease, okay.Reps, okay.EnglishFirst));
        Assert.Equal([new DateOnly(2026, 9, 28)], there.Activity.All().Keys);
        var e = await Assert.ThrowsAsync<LamhaException>(() => there.Backup.ImportAsync("""{ "cards": {} }"""));
        Assert.Equal("not_backup", e.Code);
        Assert.Equal("not_backup", (await Assert.ThrowsAsync<LamhaException>(() => there.Backup.ImportAsync("not json"))).Code);
    }

    [Fact]
    public async Task ReportAProblemHasSettingsCountsAndTheLatestErrorsNeverAWordATextAKeyOrAnAddress()
    {
        var net = new FakeNet(_ => throw FakeNet.Offline());
        var settings = new LamhaSettings { UiLang = "ar", DictSource = DictSource.Online, OllamaUrl = "http://192.168.1.7:11434", OllamaModel = "qwen3.5:4b", CustomUrl = "https://secret-server.example/v1" };
        using var lamha = Lamha(net, settings, new Dictionary<string, string> { ["claude"] = "sk-ant-SECRET" });
        lamha.Deck.Put(DeckCard(lamha, "serendipity"));
        lamha.History.Add(new HistoryEntry("privateword", "خاص", "en", 1), 100);
        await lamha.ErrorOf(() => lamha.Lookups.LookupAsync("A sentence the user selected."));
        await lamha.ErrorOf(() => lamha.Writing.RunAsync(WritingTool.Proofread, "my private letter"));
        var report = lamha.Diagnostics.Build();
        foreach (var secret in new[] { "SECRET", "192.168", "privateword", "serendipity", "secret-server", "sentence the user", "private letter" })
        {
            Assert.DoesNotContain(secret, report, StringComparison.Ordinal);
        }

        Assert.StartsWith("Lamha ", report, StringComparison.Ordinal);
        Assert.Contains("1 cards, 1 in history", report, StringComparison.Ordinal);
        Assert.Contains("lookup: network", report, StringComparison.Ordinal);
        Assert.Contains("ai proofread: network", report, StringComparison.Ordinal);
        Assert.Contains("Ollama model qwen3.5:4b at a custom address", report, StringComparison.Ordinal);
        Assert.Contains("custom server on the internet", report, StringComparison.Ordinal);
        Assert.Contains("Memory 30 MB", report, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorCodesKeepOnlyTheCode()
    {
        Assert.Equal("ai_error", Diagnostics.ErrorLog.Code("ai_error:Your text \"my secret\" was refused"));
        Assert.Equal("Something about … failed", Diagnostics.ErrorLog.Code("Something about \"my secret\" failed"));
        Assert.Equal("HttpRequestException", Diagnostics.ErrorLog.CodeOf(new HttpRequestException("secret")));
    }
}
