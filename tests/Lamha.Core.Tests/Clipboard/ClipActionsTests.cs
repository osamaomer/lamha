using Lamha.Core.Clipboard;
using Lamha.Core.Settings;
using Lamha.Core.Tests.Support;
using Lamha.Data.Database;

namespace Lamha.Core.Tests.Clipboard;

/// <summary>
/// Lamha 2's test-clipboard.mjs, section "actions": Lamha's tools on a copy, on the real lookup, writing tools and review
/// list with a fake network that counts Google's and Claude's requests.
/// </summary>
public sealed class ClipActionsTests
{
    private static readonly Dictionary<string, string> ClaudeKey = new() { ["claude"] = "sk-test" };

    /// <summary>Lamha 2's <c>PROOF</c> in test-clipboard.mjs.</summary>
    private const string Proof = """
        { "corrected": "She doesn't like apples.",
          "issues": [ { "original": "dont", "fix": "doesn't", "category": "agreement", "why": "مع she نستخدم doesn't." } ] }
        """;

    private int _google;
    private int _claude;

    [Fact]
    public async Task TranslateIsKeptWithTheCopyASecondLookMakesNoRequestAndRedoAsksAgain()
    {
        using var t = Env();
        var id = t.Ingest("Keep your promise.");
        var a = await t.Run(id, ClipAction.Translate);
        Assert.Equal("حافظ على وعدك.", a.Text);
        Assert.Equal(1, _google);
        Assert.Equal("حافظ على وعدك.", t.History.Get(id)!.Cache.Translation["ar"]);

        var b = await t.Run(id, ClipAction.Translate);
        Assert.True(b.Cached);
        Assert.Equal(1, _google); // the second look went to the network otherwise
        Assert.Equal("حافظ على وعدك.", t.History.List(targetLang: "ar").Items[0].Translation); // the list's preview line

        var again = await t.Run(id, ClipAction.Translate, fresh: true);
        Assert.False(again.Cached);
        Assert.Equal("حافظ على وعدك.", again.Text);
    }

    [Fact]
    public async Task TranslateRespectsOfflineOnlyASentenceIsSentNowhere()
    {
        using var t = Env(new LamhaSettings { UiLang = "ar", DictSource = DictSource.Offline, ClipboardEnabled = true });
        var id = t.Ingest("Keep your promise.");
        Assert.Equal(ErrorCodes.OfflineMode, await t.Lamha.ErrorOf(() => t.Run(id, ClipAction.Translate)));
        Assert.Equal(0, _google);
    }

    [Fact]
    public async Task WriteItInEnglishAndSummarizeAreKeptASecondCallMakesNoRequest()
    {
        using var t = Env(keys: ClaudeKey);
        var id = t.Ingest("سأرسل لك التقرير النهائي اليوم " + string.Concat(Enumerable.Repeat("مع كل التفاصيل المطلوبة ", 20)));
        var en = await t.Run(id, ClipAction.English);
        Assert.Equal("I will send you the final report today.", en.Text);
        var sum = await t.Run(id, ClipAction.Summary);
        Assert.Equal("• ملخص قصير", sum.Text);
        Assert.Equal(2, _claude);

        Assert.True((await t.Run(id, ClipAction.English)).Cached);
        Assert.True((await t.Run(id, ClipAction.Summary)).Cached);
        Assert.Equal(2, _claude); // cached results went to the network otherwise
        Assert.Equal(en.Text, t.History.Get(id)!.Cache.English);
    }

    [Fact]
    public async Task SummarizeInEitherLanguageEachKeptWithTheCopy()
    {
        using var t = Env(keys: ClaudeKey);
        var id = t.Ingest(string.Concat(Enumerable.Repeat("The quarterly report shows growth. ", 20)));
        var ar = await t.Run(id, ClipAction.Summary);
        Assert.Equal(("ar", "• ملخص قصير"), (ar.Lang, ar.Text));
        var en = await t.Run(id, ClipAction.Summary, lang: "en");
        Assert.Equal(("en", "• a short summary"), (en.Lang, en.Text));
        Assert.Equal(2, _claude);

        Assert.True((await t.Run(id, ClipAction.Summary, lang: "en")).Cached);
        Assert.Equal("• ملخص قصير", (await t.Run(id, ClipAction.Summary, lang: "ar")).Text);
        Assert.Equal(2, _claude); // a cached language went to the network otherwise
    }

    [Fact]
    public async Task ProofreadIsKeptAndTheJournalCountsItOnceRedoIsNotCountedAgain()
    {
        using var t = Env(keys: ClaudeKey);
        var id = t.Ingest("She dont like apples.");
        var r = await t.Run(id, ClipAction.Proofread);
        Assert.Equal("She doesn't like apples.", r.Text);
        Assert.Single(r.Issues);
        Assert.Equal(1, t.Lamha.Writing.Journal().Checks);

        Assert.True((await t.Run(id, ClipAction.Proofread)).Cached);
        await t.Run(id, ClipAction.Proofread, fresh: true);
        Assert.Equal(2, _claude);
        Assert.Equal(1, t.Lamha.Writing.Journal().Checks); // a retry was counted again otherwise
    }

    [Fact]
    public async Task WithTheJournalOffProofreadingACopyRecordsNothing()
    {
        using var t = Env(new LamhaSettings { UiLang = "ar", DictSource = DictSource.Online, ClipboardEnabled = true, SaveMistakes = false }, ClaudeKey);
        await t.Run(t.Ingest("She dont like apples."), ClipAction.Proofread);
        Assert.Equal(0, t.Lamha.Writing.Journal().Checks);
    }

    [Fact]
    public async Task WithNoWritingProviderTheUsualMessageAndNothingIsKept()
    {
        using var t = Env();
        var id = t.Ingest("She dont like apples.");
        Assert.Equal(ErrorCodes.AiNoKey, await t.Lamha.ErrorOf(() => t.Run(id, ClipAction.Proofread)));
        Assert.Equal(0, _claude);
        Assert.Null(t.History.Get(id)!.Cache.Proofread); // an error was kept otherwise
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AddToReviewAddsWithTheDictionaryExampleAndAgainRemovesIt(bool cardsAuto)
    {
        using var t = Env(new LamhaSettings { UiLang = "ar", DictSource = DictSource.Online, ClipboardEnabled = true, CardsAuto = cardsAuto });
        var id = t.Ingest("bank");
        var first = await t.Run(id, ClipAction.Review);
        Assert.True(first.InReview);
        var card = t.Lamha.Deck.Get("bank")!;
        Assert.Equal(("مصرف", "he cashed a check at the bank", "a financial institution"), (card.Meaning, card.Example, card.Definition));

        Assert.False((await t.Run(id, ClipAction.Review)).InReview);
        Assert.Null(t.Lamha.Deck.Get("bank")); // not removed otherwise
        Assert.True((await t.Run(id, ClipAction.Review)).InReview); // a third press adds it again
    }

    [Fact]
    public async Task AddToReviewTakesAWordNamedLikeAnObjectsPropertyAsAnyWord()
    {
        using var t = Env(new LamhaSettings { UiLang = "ar", DictSource = DictSource.Online, ClipboardEnabled = true });
        var id = t.Ingest("constructor");
        Assert.True((await t.Run(id, ClipAction.Review)).InReview);
        Assert.NotNull(t.Lamha.Deck.Get("constructor"));
        Assert.False((await t.Run(id, ClipAction.Review)).InReview);
    }

    [Fact]
    public async Task AddToReviewOnlyForOneEnglishWordAndCopyingNeverAddsCards()
    {
        using var t = Env();
        Assert.Equal(ClipActions.NotAWord, await t.Lamha.ErrorOf(() => t.Run(t.Ingest("two words"), ClipAction.Review)));
        t.Ingest("serendipity"); // a copy alone
        Assert.Empty(t.Lamha.Deck.All());
        Assert.Equal(ClipboardHistory.NotFound, await t.Lamha.ErrorOf(() => t.Run("c_missing", ClipAction.Translate)));
    }

    [Fact]
    public async Task ReplaceTakesTheProofreadTextInTheCopysPlaceAndUndoPutsItBack()
    {
        using var t = Env(keys: ClaudeKey);
        var id = t.Ingest("She dont like apples.");
        t.Ingest("Something copied later.");
        t.History.SetPinned(id, true);
        await t.Run(id, ClipAction.Translate);
        await t.Run(id, ClipAction.Proofread);

        var replaced = t.Actions.Replace(id, ClipAction.Proofread);
        Assert.Equal("She doesn't like apples.", replaced.Text);
        var clip = t.History.Get(id)!;
        Assert.Equal(("She doesn't like apples.", true, "notepad.exe"), (clip.Text, clip.Pinned, clip.SourceApp));
        Assert.Equal("حافظ على وعدك.", clip.Cache.Translation["ar"]); // the same meaning: kept
        Assert.Equal(2, t.History.List().Total);

        var again = await t.Run(id, ClipAction.Proofread);
        Assert.Empty(again.Issues); // "No mistakes", from the copy: nothing asked
        Assert.Equal(1, _claude);

        Assert.True(t.History.UndoReplace(replaced));
        clip = t.History.Get(id)!;
        Assert.Equal("She dont like apples.", clip.Text);
        Assert.Single(clip.Cache.Proofread!.Issues);
    }

    [Fact]
    public async Task WriteItInEnglishStaysUnderTheArabicCopyAndReplaceKeepsTheArabicUnderTheEnglish()
    {
        using var t = Env(keys: ClaudeKey);
        var id = t.Ingest("سأرسل لك التقرير النهائي اليوم");
        await t.Run(id, ClipAction.English);
        Assert.Equal("I will send you the final report today.", t.History.List(targetLang: "ar").Items[0].Translation);

        t.Actions.Replace(id, ClipAction.English);
        var row = t.History.List(targetLang: "ar").Items[0];
        Assert.Equal(("I will send you the final report today.", "en", "سأرسل لك التقرير النهائي اليوم"), (row.Text, row.Lang, row.Translation));
        Assert.Contains(ClipAction.Proofread, ClipActions.Available(row.Text, row.Lang, row.Length, panel: false)); // an English copy's tools now
    }

    [Fact]
    public async Task ReplaceMergesACopyOfTheNewTextAndUndoBringsItBack()
    {
        using var t = Env(keys: ClaudeKey);
        var id = t.Ingest("She dont like apples.");
        await t.Run(id, ClipAction.Proofread);
        var copied = t.Ingest("She doesn't like apples."); // the result's Copy, recorded as any copy
        t.History.SetPinned(copied, true);
        t.History.SetLabel(copied, "apples");

        var replaced = t.Actions.Replace(id, ClipAction.Proofread);
        var only = Assert.Single(t.History.List().Items);
        Assert.Equal((id, true, "apples"), (only.Id, only.Pinned, only.Label));

        Assert.True(t.History.UndoReplace(replaced));
        Assert.Equal(2, t.History.List().Total);
        Assert.Equal(("She dont like apples.", false), (t.History.Get(id)!.Text, t.History.Get(id)!.Pinned));
        Assert.True(t.History.Get(copied)!.Pinned);
    }

    [Fact]
    public async Task ReplaceNeedsAResultAndUndoIsTooLateOnceTheOldTextIsCopiedAgain()
    {
        using var t = Env(keys: ClaudeKey);
        var id = t.Ingest("She dont like apples.");
        Assert.Equal(ClipActions.EmptyResult, Assert.Throws<LamhaException>(() => t.Actions.Replace(id, ClipAction.Proofread)).Code);
        Assert.False(ClipActions.CanReplace(ClipAction.Translate));

        await t.Run(id, ClipAction.Proofread);
        var replaced = t.Actions.Replace(id, ClipAction.Proofread);
        t.Ingest("She dont like apples.");
        Assert.False(t.History.UndoReplace(replaced));
        Assert.Equal("She doesn't like apples.", t.History.Get(id)!.Text); // the new copy of the old text was overwritten otherwise
    }

    [Fact]
    public void TheToolsThatFitACopyByItsLanguageAndLength()
    {
        Assert.Equal([ClipAction.Translate, ClipAction.Proofread, ClipAction.Lookup, ClipAction.Review], ClipActions.Available("bank", "en", 4, panel: false));
        Assert.Equal([ClipAction.English], ClipActions.Available("مرحبا بك", "ar", 8, panel: false));
        Assert.Equal([ClipAction.Translate, ClipAction.English, ClipAction.Summary, ClipAction.Plain], ClipActions.Available(new string('x', 401), "mixed", 401, panel: true));
        Assert.DoesNotContain(ClipAction.Lookup, ClipActions.Available("one two three four five", "en", 23, panel: false)); // more than four words
    }

    private Harness Env(LamhaSettings? settings = null, Dictionary<string, string>? keys = null)
    {
        var net = new FakeNet(c =>
        {
            if (Nets.IsGoogleSingle(c))
            {
                _google++;
                return c.Param("q")[0] == "bank"
                    ? Nets.GoogleWord("مصرف", more: """ "definitions": [ { "pos": "noun", "entry": [ { "gloss": "a financial institution", "example": "he cashed a check at the bank", "definition_id": "d1" } ] } ] """)
                    : Nets.GoogleWord("حافظ على وعدك.");
            }

            if (c.Url.StartsWith("https://api.anthropic.com/", StringComparison.Ordinal))
            {
                _claude++;
                var prompt = c.Prompt;
                return prompt.StartsWith("Proofread", StringComparison.Ordinal) ? Replies.Claude(Proof)
                    : prompt.Contains("Summarize", StringComparison.Ordinal)
                        ? Replies.Claude(Replies.Text(prompt.Contains("in English", StringComparison.Ordinal) ? "• a short summary" : "• ملخص قصير"))
                        : Replies.Claude(Replies.Text("I will send you the final report today."));
            }

            return FakeNet.Json(404, "{}");
        });
        var lamha = new TestLamha(net, settings ?? new LamhaSettings { UiLang = "ar", DictSource = DictSource.Online, TranslateDefinitions = false, ClipboardEnabled = true }, keys);
        lamha.State.Set("deck.imported", "true"); // Lamha 2's cardsImported: the history isn't turned into cards
        return new Harness(lamha);
    }

    /// <summary>Lamha with clipboard history, and the tools on it.</summary>
    private sealed class Harness : IDisposable
    {
        private long _captured = 1_800_000_000_000;

        public Harness(TestLamha lamha)
        {
            Lamha = lamha;
            History = new ClipboardHistory(new SqliteClipStore(lamha.Db), new FakeProtector(), lamha.State, lamha.Settings, lamha.Clock);
            Actions = new ClipActions(History, lamha.Lookups, lamha.Writing, lamha.Review, lamha.Settings);
        }

        public TestLamha Lamha { get; }

        public ClipboardHistory History { get; }

        public ClipActions Actions { get; }

        public string Ingest(string text) => History.Ingest(new CapturedClip(text, null, "notepad.exe", ++_captured));

        public Task<ClipActionResult> Run(string id, ClipAction action, bool fresh = false, string? lang = null) =>
            Lamha.Run(() => Actions.RunAsync(id, action, fresh, lang));

        public void Dispose() => Lamha.Dispose();
    }
}
