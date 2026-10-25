using System.Text.RegularExpressions;
using Lamha.Core.Settings;
using Lamha.Core.Tests.Support;
using Lamha.Core.Writing;

namespace Lamha.Core.Tests.Writing;

/// <summary>Lamha 2's test-writing.mjs: the writing tools on Claude, the prompts, and the mistake journal.</summary>
public class WritingToolTests
{
    private static readonly Dictionary<string, string> ClaudeKey = new() { ["claude"] = "sk-test" };

    [Fact]
    public async Task WriteNewMakesAnEmailOrAMessageFromADescription()
    {
        var net = new FakeNet(_ => Replies.Claude(Replies.Text("Subject: Day off on Sunday\n\nHi Sam, …\n\n[Your name]")));
        using var lamha = new TestLamha(net, keys: ClaudeKey);
        const string Idea = "بريد لمديري أطلب إجازة يوم الأحد";

        var email = await lamha.Writing.RunAsync(WritingTool.Compose, Idea, new() { Email = true, Tone = "formal" }, TestContext.Current.CancellationToken);
        Assert.StartsWith("Subject:", email.Text, StringComparison.Ordinal);
        var prompt = net.Calls[0].Prompt;
        Assert.Contains("\"Subject: …\"", prompt, StringComparison.Ordinal);
        Assert.Contains("professional and polite", prompt, StringComparison.Ordinal);
        Assert.Contains("<text>\n" + Idea + "\n</text>", prompt, StringComparison.Ordinal);

        await lamha.Writing.RunAsync(WritingTool.Compose, Idea, new(), TestContext.Current.CancellationToken);
        Assert.Equal(2, net.Calls.Count); // Email and Message aren't served from the same cache entry
        Assert.Contains("no subject line and no sign-off", net.Calls[1].Prompt, StringComparison.Ordinal);
        Assert.Contains("a short message", net.Calls[1].Prompt, StringComparison.Ordinal);

        await lamha.Writing.RunAsync(WritingTool.Compose, Idea, new() { Tone = "long" }, TestContext.Current.CancellationToken);
        Assert.Contains("Tone: detailed and complete", net.Calls[2].Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("a short message", net.Calls[2].Prompt, StringComparison.Ordinal);

        await lamha.Writing.RunAsync(WritingTool.Compose, Idea, new() { Tone = "constructor" }, TestContext.Current.CancellationToken);
        Assert.Contains("Tone: natural and appropriate", net.Calls[3].Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LongerDevelopsTheTextWithoutInventingFacts()
    {
        var net = new FakeNet(_ => Replies.Claude(Replies.Text("Hi Sam, could we move Sunday's meeting to [day]?")));
        using var lamha = new TestLamha(net, keys: ClaudeKey);
        await lamha.Writing.RunAsync(WritingTool.Expand, "cant make sunday meeting", null, TestContext.Current.CancellationToken);
        var prompt = net.Calls[0].Prompt;
        Assert.Contains("longer and more complete", prompt, StringComparison.Ordinal);
        Assert.Contains("clearly longer than the original", prompt, StringComparison.Ordinal);
        Assert.Contains("Never add reasons, events, names, dates", prompt, StringComparison.Ordinal);
        Assert.Contains("[reason]", prompt, StringComparison.Ordinal);
        Assert.Contains("Put only the rewritten text", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClaudeProofreadRequestShapeCleanedIssuesAndTheJournal()
    {
        var net = new FakeNet(_ => Replies.Claude(Replies.Proof));
        using var lamha = new TestLamha(net, keys: ClaudeKey);
        var result = await lamha.Writing.RunAsync(WritingTool.Proofread, "I go to market yesterday.", null, TestContext.Current.CancellationToken);

        var call = net.Calls[0];
        Assert.Equal("https://api.anthropic.com/v1/messages", call.Url);
        Assert.Equal("sk-test", call.Header("x-api-key"));
        Assert.Equal("2023-06-01", call.Header("anthropic-version"));
        var body = call.Json!;
        Assert.Equal("claude-opus-5-5", body["model"]!.GetValue<string>());
        Assert.Equal("default", body["fallbacks"]!.GetValue<string>());
        Assert.Equal("server-side-fallback-2026-07-01", call.Header("anthropic-beta"));
        Assert.Equal("medium", body["output_config"]!["effort"]!.GetValue<string>());
        var item = body["output_config"]!["format"]!["schema"]!["properties"]!["issues"]!["items"]!;
        Assert.Contains("prepositions", item["properties"]!["category"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Contains("category", item["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.DoesNotContain("often makes mistakes", call.Prompt, StringComparison.Ordinal);

        Assert.Equal(3, result.Issues.Count); // the no-op change is dropped
        Assert.Equal("other", result.Issues[2].Category);

        var journal = lamha.Mistakes.Read();
        Assert.Equal(1, journal.Checks);
        Assert.Equal(new Dictionary<string, int> { ["verb_tense"] = 1, ["articles"] = 1, ["other"] = 1 }, journal.Counts);
        Assert.Equal(["I go", "market", "a"], journal.Recent.Select(m => m.Original)); // in text order
    }

    [Fact]
    public async Task CacheHitsAndRetriesAreNotCountedTwice()
    {
        var net = new FakeNet(_ => Replies.Claude(Replies.Proof));
        using var lamha = new TestLamha(net, keys: ClaudeKey);
        await lamha.Writing.RunAsync(WritingTool.Proofread, "I go to market yesterday.", null, TestContext.Current.CancellationToken);
        await lamha.Writing.RunAsync(WritingTool.Proofread, "I go to market yesterday.", null, TestContext.Current.CancellationToken);
        Assert.Single(net.Calls); // the second is served from the cache
        await lamha.Writing.RunAsync(WritingTool.Proofread, "I go to market yesterday.", new() { Fresh = true }, TestContext.Current.CancellationToken);
        Assert.Equal(2, net.Calls.Count);
        Assert.Equal(1, lamha.Mistakes.Read().Checks);
    }

    [Fact]
    public async Task JournalOffRecordsNothingAndLeavesWeakPointsOut()
    {
        var net = new FakeNet(_ => Replies.Claude(Replies.Proof));
        using var lamha = new TestLamha(net, new LamhaSettings { UiLang = "ar", SaveMistakes = false }, ClaudeKey);
        lamha.Mistakes.Replace(new(5, new Dictionary<string, int> { ["articles"] = 9 }, [], 0));
        await lamha.Writing.RunAsync(WritingTool.Proofread, "Some text here.", null, TestContext.Current.CancellationToken);
        Assert.Equal(5, lamha.Mistakes.Read().Checks);
        Assert.DoesNotContain("often makes mistakes", net.Calls[0].Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WeakPointsGoIntoTheProofreadPromptOnly()
    {
        var net = new FakeNet(c => Replies.Claude(c.Prompt.Contains("Proofread", StringComparison.Ordinal) ? Replies.Proof : Replies.Text("ok")));
        using var lamha = new TestLamha(net, keys: ClaudeKey);
        lamha.Mistakes.Replace(new(9, new Dictionary<string, int> { ["articles"] = 12, ["prepositions"] = 7, ["spelling"] = 2, ["other"] = 30, ["plurals"] = 4, ["word_order"] = 3 }, [], 0));
        await lamha.Writing.RunAsync(WritingTool.Proofread, "Text one.", null, TestContext.Current.CancellationToken);
        Assert.Contains("often makes mistakes with: articles (a/an/the), prepositions, singular and plural nouns.", net.Calls[0].Prompt, StringComparison.Ordinal);
        await lamha.Writing.RunAsync(WritingTool.Improve, "Text one.", null, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("often makes mistakes", net.Calls[1].Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheEnglishInterfaceGetsEnglishExplanationsAndSummaries()
    {
        var net = new FakeNet(c => Replies.Claude(c.Prompt.Contains("Proofread", StringComparison.Ordinal) ? Replies.Proof : Replies.Text("ok")));
        using var lamha = new TestLamha(net, new LamhaSettings { UiLang = "en" }, ClaudeKey);
        var cancel = TestContext.Current.CancellationToken;
        await lamha.Writing.RunAsync(WritingTool.Proofread, "I go to market yesterday.", null, cancel);
        await lamha.Writing.RunAsync(WritingTool.Explain, "It's raining cats and dogs.", null, cancel);
        await lamha.Writing.RunAsync(WritingTool.Summarize, "A long report about the quarter.", null, cancel);
        await lamha.Writing.RunAsync(WritingTool.Summarize, "A long report about the quarter.", new() { Language = "ar" }, cancel);
        var prompts = net.Calls.Select(c => c.Prompt).ToList();
        Assert.Contains("one-sentence explanation in English", prompts[0], StringComparison.Ordinal);
        Assert.Contains("in simple English", prompts[1], StringComparison.Ordinal);
        Assert.Contains("Summarize the key points of the text in English", prompts[2], StringComparison.Ordinal);
        Assert.Contains("in Arabic", prompts[3], StringComparison.Ordinal); // an explicit summary language still wins
    }

    [Fact]
    public async Task TheArabicInterfaceGetsArabicExplanations()
    {
        var net = new FakeNet(_ => Replies.Claude(Replies.Proof));
        using var lamha = new TestLamha(net, keys: ClaudeKey);
        await lamha.Writing.RunAsync(WritingTool.Proofread, "I go to market yesterday.", null, TestContext.Current.CancellationToken);
        Assert.Contains("one-sentence explanation in Arabic", net.Calls[0].Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyAndTooLongTextsAreRefusedBeforeAnyRequest()
    {
        var net = new FakeNet(_ => Replies.Claude(Replies.Text("x")));
        using var lamha = new TestLamha(net, keys: ClaudeKey);
        Assert.Equal("empty", await lamha.ErrorOf(() => lamha.Writing.RunAsync(WritingTool.Improve, "   ")));
        Assert.Equal("ai_too_long", await lamha.ErrorOf(() => lamha.Writing.RunAsync(WritingTool.Improve, new string('a', 50_001))));
        Assert.Empty(net.Calls);
    }

    [Fact]
    public void TheMostFrequentMistakeIsNeverOtherAndTheJournalClears()
    {
        using var lamha = new TestLamha(new FakeNet(_ => Replies.Claude(Replies.Proof)), keys: ClaudeKey);
        Assert.Null(lamha.Writing.MostFrequent());
        lamha.Mistakes.Replace(new(4, new Dictionary<string, int> { ["other"] = 30, ["constructor"] = 50, ["spelling"] = 2 }, [], 0));
        Assert.Equal(("spelling", 2), lamha.Writing.MostFrequent());
        lamha.Mistakes.Replace(new(9, new Dictionary<string, int> { ["articles"] = 3, ["prepositions"] = 7, ["other"] = 30 }, [], 0));
        Assert.Equal(("prepositions", 7), lamha.Writing.MostFrequent());

        lamha.Mistakes.Record([new("articles", "a hour", "an hour", "why", lamha.Now)], lamha.Now, 200);
        var cleared = lamha.Writing.ClearJournal();
        var journal = lamha.Writing.Journal();
        Assert.Equal(0, journal.Checks);
        Assert.Empty(journal.Counts);
        Assert.Empty(journal.Recent);
        Assert.Equal(lamha.Now, journal.Since);
        Assert.Null(lamha.Writing.MostFrequent());

        // Undo: the journal as it was, its latest mistake included.
        lamha.Writing.RestoreJournal(cleared);
        journal = lamha.Writing.Journal();
        Assert.Equal(10, journal.Checks);
        Assert.Equal(4, journal.Counts["articles"]);
        Assert.Equal("an hour", Assert.Single(journal.Recent).Fix);
        Assert.Equal(("prepositions", 7), lamha.Writing.MostFrequent());
    }

    [Fact]
    public void CategoriesAreOwnKeysOnly()
    {
        Assert.False(MistakeCategories.IsKnown("constructor"));
        Assert.True(MistakeCategories.IsKnown("articles"));
        Assert.Matches(new Regex("^articles"), MistakeCategories.PromptName("articles"));
    }
}
