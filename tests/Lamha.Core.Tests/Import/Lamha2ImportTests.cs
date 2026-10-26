using System.Text.Json.Nodes;
using Lamha.Core.Clipboard;
using Lamha.Core.Import;
using Lamha.Core.Review;
using Lamha.Core.Settings;
using Lamha.Core.Tests.Clipboard;
using Lamha.Core.Tests.Support;
using Lamha.Data.Database;

namespace Lamha.Core.Tests.Import;

/// <summary>Phase 6: Lamha 2's data moved into Lamha 3, from its storage files as they are on disk.</summary>
public sealed class Lamha2ImportTests : IDisposable
{
    private const long Day = Scheduler.Day;

    private readonly TestLamha _lamha = new(new FakeNet(_ => FakeNet.Json(404, "{}")));
    private readonly ClipboardHistory _clipboard;
    private readonly Lamha2Import _import;

    public Lamha2ImportTests()
    {
        _clipboard = new ClipboardHistory(new SqliteClipStore(_lamha.Db), new FakeProtector(), _lamha.State, _lamha.Settings, _lamha.Clock);
        _import = new Lamha2Import(_lamha.Settings, _lamha.Secrets, _lamha.State, _lamha.Backup, _lamha.Review, _clipboard);
    }

    public void Dispose() => _lamha.Dispose();

    /// <summary>A Lamha 2 profile in the shape this PC's has (desktop/storage.js), with the keys already decrypted.</summary>
    private Lamha2Data Profile(int wikipedias = 0, bool keysLost = false)
    {
        var now = _lamha.Now;
        var sync = JsonNode.Parse("""
            { "showInInputs": false, "theme": "dark", "uiLang": "en", "enabled": false, "enDict": true,
              "explainLangs": ["fr", "en", "de", "fr", "<script>"], "saveMistakes": true, "motion": "subtle", "aiInInputs": true,
              "saveHistory": true, "reverseForArabic": false, "cardsNewPerDay": 20, "dailyGoal": 0, "aiModel": "claude-sonnet-5" }
            """)!.AsObject();
        var local = JsonNode.Parse($$"""
            { "cards": {
                "serendipity": { "q": "serendipity", "tr": "مصادفة سعيدة", "ex": "Pure serendipity.", "added": {{now - (40 * Day)}}, "due": {{now + (3 * Day)}},
                                 "interval": 12, "ease": 2.6, "reps": 4, "lapses": 1, "mod": {{now - (9 * Day)}}, "last": {{now - (9 * Day)}} },
                "settle": { "q": "settle", "tr": "يسدّد", "added": {{now - Day}}, "due": 0, "interval": 0, "ease": 2.5, "reps": 0, "lapses": 0, "mod": {{now - Day}} },
                "broken": { "tr": "no word" } },
              "cardsRemoved": { "sync": {{now - (2 * Day)}} },
              "cardStats": { "day": "Fri Oct 23 2026", "newSeen": 3, "streak": 6, "reviewDay": "Fri Oct 23 2026" },
              "cardsImported": true, "cardsRev": 41,
              "history": [ { "q": "settle", "tr": "يسدّد", "src": "en", "t": {{now - Day}} }, { "q": "serendipity", "tr": "مصادفة سعيدة", "src": "en", "t": {{now - (40 * Day)}} } ],
              "mistakes": { "checks": 7, "counts": { "articles": 3, "spelling": 2, "made-up": 9 },
                            "recent": [ { "cat": "articles", "original": "a apple", "fix": "an apple", "why": "a vowel sound", "t": {{now - Day}} } ], "since": {{now - (30 * Day)}} },
              "activity": { "Thu Oct 22 2026": 4, "Fri Oct 23 2026": 9 },
              "lookupCount": 312,
              "aiProvider": "gemini", "geminiModel": "gemini-3-flash", "geminiKey": "AIza-test-key", "geminiKeySet": true, "aiKey": "sk-ant-test",
              "trService": "ai", "trProvider": "gemini", "trModels": { "gemini": "gemini-3-flash", "openai": "x" },
              "clipboardEnabled": true, "clipboardMaxItems": 250, "clipboardExpiryDays": 7, "clipboardSkipCards": false,
              "clipboardExcludedApps": ["C:\\Tools\\KeePassXC.exe", ""], "clipboardSummaryLang": "en",
              "accentWindows": true, "windowMica": false, "updatesAuto": false, "useGpu": true, "firefoxLink": true, "wikiOfflineFirst": true,
              "draft": "Dear Sara, I am writting to", "popupMode": "review", "motionHint": "subtle", "errorLog": [ { "t": 1, "where": "x", "code": "network" } ],
              "wotd": { "q": "settle", "day": "Fri Oct 23 2026" } }
            """)!.AsObject();
        var clips = JsonNode.Parse($$"""
            [ { "id": "c_mgzx1abc0000a1b", "text": "The meeting moved to Thursday.", "createdAt": {{now - (3 * Day)}}, "lastCopiedAt": {{now - (2 * Day)}},
                "lastUsedAt": 0, "copyCount": 2, "useCount": 0, "pinned": true, "label": "Meeting", "sourceApp": "OUTLOOK.EXE", "lang": "en",
                "cache": { "english": null, "proofread": null, "summary": { "ar": { "text": "تأجل الاجتماع" } }, "translation": { "ar": "انتقل الاجتماع إلى الخميس." } } },
              { "id": "not an id", "text": "مرحبا", "createdAt": {{now - Day}}, "sourceApp": "notepad.exe" },
              { "id": "c_mgzx1abc0000a1c", "text": "   " } ]
            """)!.AsArray();
        return new Lamha2Data(sync, local, clips, keysLost, ClipsLost: false, wikipedias);
    }

    [Fact]
    public void TheStepCountsWhatWillMove()
    {
        // The broken card, the blank copy and the journal's unknown category aren't counted.
        Assert.Equal(new Lamha2Counts(Cards: 2, History: 2, Mistakes: 5, Clips: 2), Lamha2Import.Count(Profile()));
    }

    [Fact]
    public async Task EverythingMovesTheDeckWithItsProgressAndTheStreakGoesOn()
    {
        var summary = await _import.MoveAsync(Profile());

        var serendipity = _lamha.Deck.Get("serendipity")!;
        Assert.Equal((4, 12, 1, "Pure serendipity."), (serendipity.Reps, serendipity.Interval, serendipity.Lapses, serendipity.Example));
        Assert.Equal(["serendipity", "settle"], _lamha.Deck.All().Select(c => c.Key).Order(StringComparer.Ordinal));
        Assert.NotNull(_lamha.Deck.RemovedAt("sync")); // a word removed in Lamha 2 isn't brought back by a later copy
        var stats = _lamha.Review.Stats();
        Assert.Equal((6, 3, new DateOnly(2026, 10, 23)), (stats.Streak, stats.NewSeen, stats.ReviewDay));
        Assert.Equal("true", _lamha.State.Get("deck.imported")); // Lamha 2 had made its history into cards

        Assert.Equal(["settle", "serendipity"], _lamha.History.Recent(10).Select(h => h.Word));
        var journal = _lamha.Mistakes.Read();
        Assert.Equal((7, 3, 1), (journal.Checks, journal.Counts["articles"], journal.Recent.Count));
        Assert.Equal(9, _lamha.Activity.Get(new DateOnly(2026, 10, 23)));
        Assert.Equal(312, _lamha.Today.LookupCount);
        Assert.Equal("Dear Sara, I am writting to", _lamha.State.Get("draft"));
        Assert.Equal("en", _lamha.State.Get("clipboardSummaryLang"));

        Assert.Equal(("AIza-test-key", "sk-ant-test"), (_lamha.Secrets.Get("gemini"), _lamha.Secrets.Get("claude")));
        Assert.Equal(["claude", "gemini"], summary.Keys);
        Assert.Equal([Lamha2Dropped.Firefox], summary.Dropped);
        Assert.Equal(new Lamha2Counts(2, 2, 5, 2), summary.Counts);
    }

    [Fact]
    public async Task SettingsMoveButNotTheLanguageTheSwitchOrMica()
    {
        _lamha.Settings.Update(s => s with { UiLang = "ar" }); // first run's step 1
        await _import.MoveAsync(Profile());

        var s = _lamha.Settings.Current;
        Assert.Equal(("dark", "subtle", true, false), (s.Theme, s.Motion, s.EnDict, s.ReverseForArabic));
        Assert.Equal(["fr", "de"], s.ExplainLangs);
        Assert.Equal((20, 0), (s.CardsNewPerDay, s.DailyGoal));
        Assert.Equal((AiProviderKind.Gemini, "gemini-3-flash", "claude-sonnet-5"), (s.AiProvider, s.GeminiModel, s.ClaudeModel));
        Assert.Equal((TranslateWith.Ai, AiProviderKind.Gemini), (s.TrService, s.TrProvider));
        Assert.Equal(new Dictionary<string, string> { ["gemini"] = "gemini-3-flash" }, s.TrModels);
        Assert.Equal((true, 250, 7, false), (s.ClipboardEnabled, s.ClipboardMaxItems, s.ClipboardExpiryDays, s.ClipboardSkipCards));
        Assert.Equal(["keepassxc.exe"], s.ClipboardExcludedApps);
        Assert.Equal((true, false), (s.AccentWindows, s.UpdatesAuto));

        // Not moved: the interface language (step 1 chose it), enabled (the user's call) and Mica (Lamha 3 is designed on it).
        Assert.Equal(("ar", true, true), (s.UiLang, s.Enabled, s.WindowMica));
    }

    [Fact]
    public async Task ClipboardHistoryMovesWithItsPinLabelAndTranslation()
    {
        await _import.MoveAsync(Profile());

        var page = _clipboard.List();
        Assert.Equal(2, page.Total);
        var meeting = _clipboard.Get("c_mgzx1abc0000a1b")!;
        Assert.Equal((true, "Meeting", "outlook.exe", 2, "en"), (meeting.Pinned, meeting.Label, meeting.SourceApp, meeting.CopyCount, meeting.Lang));
        Assert.Equal("انتقل الاجتماع إلى الخميس.", meeting.Cache.Translation["ar"]);
        Assert.Equal("تأجل الاجتماع", meeting.Cache.Summary["ar"]);
        var hello = page.Items.Single(c => c.Text == "مرحبا");
        Assert.StartsWith("c_", hello.Id, StringComparison.Ordinal); // a new id for one that wasn't Lamha's
        Assert.Equal("ar", hello.Lang);
    }

    [Fact]
    public async Task AKeyAlreadyInLamha3StaysAndMovingTwiceAddsNothing()
    {
        _lamha.Secrets.Set("claude", "sk-ant-mine");
        var first = await _import.MoveAsync(Profile());
        Assert.Equal(["gemini"], first.Keys);
        Assert.Equal("sk-ant-mine", _lamha.Secrets.Get("claude"));

        await _import.MoveAsync(Profile());
        Assert.Equal(2, _lamha.Deck.All().Count);
        Assert.Equal(2, _lamha.History.Count());
        Assert.Equal(2, _clipboard.List().Total);
    }

    [Fact]
    public async Task KeysThatCouldNotBeReadAndDownloadedWikipediasAreNamed()
    {
        var profile = Profile(wikipedias: 2, keysLost: true);
        profile.Local.Remove("aiKey");
        profile.Local.Remove("geminiKey");
        var summary = await _import.MoveAsync(profile);
        Assert.True(summary.KeysLost);
        Assert.Empty(summary.Keys);
        Assert.Equal([Lamha2Dropped.Firefox, Lamha2Dropped.OfflineWikipedia], summary.Dropped);
    }

    [Fact]
    public async Task ValuesLamha3DoesNotHaveKeepItsDefaults()
    {
        var odd = new Lamha2Data(
            JsonNode.Parse("""{ "theme": "purple", "cardsNewPerDay": 7, "dailyGoal": -1, "dictSource": 5, "explainLangs": "fr", "targetLang": "a" }""")!.AsObject(),
            JsonNode.Parse("""{ "aiProvider": "openai", "clipboardMaxItems": 999, "trModels": [1], "cards": [], "history": {}, "cardStats": "x" }""")!.AsObject(),
            [],
            KeysLost: false,
            ClipsLost: false,
            WikipediaFiles: 0);
        var summary = await _import.MoveAsync(odd);

        var (s, d) = (_lamha.Settings.Current, new LamhaSettings());
        Assert.Equal((d.Theme, d.CardsNewPerDay, d.DailyGoal, d.DictSource), (s.Theme, s.CardsNewPerDay, s.DailyGoal, s.DictSource));
        Assert.Equal((d.TargetLang, d.AiProvider, d.ClipboardMaxItems), (s.TargetLang, s.AiProvider, s.ClipboardMaxItems));
        Assert.Empty(s.ExplainLangs);
        Assert.Equal(new Lamha2Counts(0, 0, 0, 0), summary.Counts);
        Assert.Empty(summary.Dropped);
    }
}
