using Lamha.Core.Dictionary;
using Lamha.Core.Review;
using Lamha.Core.Settings;
using Lamha.Core.Tests.Support;
using Lamha.Core.Translation;

namespace Lamha.Core.Tests.Translation;

/// <summary>Lamha 2's test-writing.mjs: lookups, the word in its sentence, explanations, and what a lookup leaves behind.</summary>
public class LookupTests
{
    private static readonly LamhaSettings Online = new() { UiLang = "ar", DictSource = DictSource.Online, TranslateDefinitions = false };

    private static FakeNet Answering(Func<Call, HttpResponseMessage> reply) => new(reply);

    [Fact]
    public async Task ALookupAddsACardWithItsSentenceAndInContextMeaningWhenCardsAreAutomatic()
    {
        var net = Answering(c => Nets.IsGoogleSingle(c)
            ? Nets.GoogleWord("مصرف", more: """ "definitions": [ { "pos": "noun", "entry": [ { "gloss": "the land alongside a river", "definition_id": "d1" } ] } ] """)
            : Nets.IsGoogleBatch(c) ? FakeNet.Json(200, """["جلسنا على <a i=0>ضفة</a> النهر."]""") : FakeNet.Json(404, "{}"));
        using (var lamha = new TestLamha(net, Online with { CardsAuto = true }))
        {
            await lamha.Lookup("bank", "We sat on the ", " of the river.");
            Assert.True(await lamha.Review.HasAsync("bank"), "the automatic add is done before the answer");
            var card = lamha.Deck.Get("bank")!;
            Assert.Equal("ضفة", card.Meaning); // the meaning in this sentence, not the first dictionary meaning
            Assert.Equal("We sat on the bank of the river.", card.Example);
            Assert.Equal("the land alongside a river", card.Definition);
        }

        using (var off = new TestLamha(net, Online))
        {
            await off.Lookup("bank");
            Assert.False(await off.Review.HasAsync("bank"), "cards aren't automatic by default");
        }

        using var noHistory = new TestLamha(net, Online with { CardsAuto = true, SaveHistory = false });
        await noHistory.Lookup("bank");
        Assert.False(await noHistory.Review.HasAsync("bank"), "history off: no automatic card");
        Assert.Equal(0, noHistory.History.Count());
    }

    [Fact]
    public async Task ANameTheSentencesTranslationKeepsIsFlaggedNotTheMeaningNotACard()
    {
        var net = Answering(c => Nets.IsGoogleSingle(c) ? Nets.GoogleWord("الجوزاء")
            : Nets.IsGoogleBatch(c) ? FakeNet.Json(200, """["استخدم <a i=0>Gemini</a> أو Claude."]""") : FakeNet.Json(404, "{}"));
        using var lamha = new TestLamha(net, Online with { CardsAuto = true });
        var r = await lamha.Lookup("gemini", "Use ", " or Claude.");
        Assert.True(r.Context!.Untranslated);
        Assert.Equal("الجوزاء", r.Translation); // the dictionary meaning is kept
        Assert.False(await lamha.Review.HasAsync("gemini"));
    }

    [Fact]
    public async Task ByDefaultTheUserPicksWordsForReviewWithTheBookmark()
    {
        var net = Answering(c => Nets.IsGoogleSingle(c) ? Nets.GoogleWord("بدلاً") : FakeNet.Json(200, """[["زر <a i=0>بدلاً</a> من مفتاح", "en"]]"""));
        using var lamha = new TestLamha(net, Online);
        lamha.History.Add(new("serendipity", "صدفة سعيدة", "en", 1), 100);
        await lamha.Lookup("instead", "a button ", " of a switch");
        Assert.False(await lamha.Review.HasAsync("instead"), "looked up, not added");
        await lamha.Review.QueueAsync();
        Assert.False(await lamha.Review.HasAsync("serendipity"), "the history stays the history");
        Assert.True(await lamha.Review.ToggleAsync(new CardDraft("instead", "بدلاً")));
        Assert.True(await lamha.Review.HasAsync("instead"));
    }

    [Fact]
    public async Task MilestonesAreSaidOnceAndTheCachedResultStaysClean()
    {
        var net = Answering(c => Nets.IsGoogleBatch(c) ? FakeNet.Json(200, """[["الأطفال <a i=0>مرنون</a> جدًا.", "en"]]""") : Nets.GoogleWord("مرن"));
        using (var lamha = new TestLamha(net, Online))
        {
            lamha.State.Set("lookupCount", "9");
            Assert.Equal(10, (await lamha.Lookup("resilient")).Milestone);
            Assert.Equal(0, (await lamha.Lookup("resilient")).Milestone); // the cached result has no milestone
            Assert.Equal(10, lamha.Today.LookupCount); // cached lookups aren't counted again
            await lamha.Lookup("Resilient", "Kids are very ", " after all.");
            Assert.Equal(10, lamha.Today.LookupCount); // the same word in another sentence isn't a new word
        }

        using var noHistory = new TestLamha(net, Online with { SaveHistory = false });
        noHistory.State.Set("lookupCount", "9");
        Assert.Equal(0, (await noHistory.Lookup("resilient")).Milestone);
        Assert.Equal(9, noHistory.Today.LookupCount);
    }

    [Fact]
    public async Task TodaysGoalCountsNewWordsAndReviewAnswersAndSaysSoOnce()
    {
        using var lamha = new TestLamha(Answering(_ => Nets.GoogleWord("كلمة")), Online with { DailyGoal = 3 });
        lamha.Deck.Put(new Card { Word = "a", Meaning = "أ", Added = 1 });
        lamha.State.Set("deck.imported", "true");
        Assert.Equal(0, (await lamha.Lookup("resilient")).Goal);
        await lamha.Lookup("resilient"); // the same word again: not counted
        Assert.Equal(0, await lamha.Review.GradeAsync("a", Grade.Good)); // a review answer counts too
        Assert.Equal(3, (await lamha.Lookup("tenacious")).Goal); // the answer that reached the goal
        Assert.Equal(0, (await lamha.Lookup("candid")).Goal); // past the goal: no second celebration
        var t = lamha.Today.Info();
        Assert.Equal((4, 3, 1), (t.Done, t.Goal, t.Streak));
    }

    [Fact]
    public async Task AWordLookedUpAgainGoesBackToTheTopOfTheHistory()
    {
        var net = Answering(c => Nets.GoogleWord(c.Url.Contains("q=tenacious", StringComparison.Ordinal) ? "عنيد" : "مرن"));
        using var lamha = new TestLamha(net, Online);
        await lamha.Lookup("resilient");
        await lamha.Lookup("tenacious");
        var asked = net.Calls.Count;
        await lamha.Lookup("resilient");
        Assert.Equal(asked, net.Calls.Count); // answered from the cache
        Assert.Equal(["resilient", "tenacious"], lamha.HistoryService.Recent().Select(h => h.Word));
        lamha.HistoryService.Clear();
        await lamha.Lookup("tenacious");
        Assert.Equal(["tenacious"], lamha.HistoryService.Recent().Select(h => h.Word)); // recorded again after clearing
    }

    [Fact]
    public async Task EnglishEnglishOfflineKeepsTheArabicForReviewAndTheHistory()
    {
        var net = Answering(_ => FakeNet.Json(429, "{}"));
        using var lamha = new TestLamha(net, new LamhaSettings { UiLang = "ar", EnDict = true, DictSource = DictSource.Offline, CardsAuto = true }, realDict: true);
        var d = await lamha.Lookup("resilient");
        Assert.True(d.Explain);
        Assert.Equal(("en", ResultSource.Local, "مَرِن"), (d.Target, d.From, d.Meaning));
        await lamha.Lookup("resilient", "Her hair was soft, bouncy and ", ".");
        Assert.Empty(net.Calls); // no internet needed
        var card = lamha.Deck.Get("resilient")!;
        Assert.Equal((true, "مَرِن"), (card.EnglishFirst, card.Meaning));
        Assert.StartsWith("recovering readily", card.Definition, StringComparison.Ordinal); // the English definition, the Arabic under it
        Assert.Equal("مَرِن", lamha.HistoryService.Recent()[0].Meaning); // recent lookups list the short meaning
    }

    [Fact]
    public async Task ANameKeepsItsCapitalOnTheCardASentencesFirstWordDoesnt()
    {
        using var lamha = new TestLamha(Answering(_ => FakeNet.Json(429, "{}")), new LamhaSettings { UiLang = "ar", DictSource = DictSource.Offline }, realDict: true);
        Assert.Equal("paris", (await lamha.Lookup("Paris")).Query); // typed alone, a capital says nothing
        Assert.Equal("Paris", (await lamha.Lookup("Paris", "We flew to ", " last May.")).Query); // inside a sentence
        Assert.Equal("river", (await lamha.Lookup("River", "It rained. ", " banks flooded.")).Query); // only the start of a sentence
        Assert.Equal("paris", (await lamha.Lookup("paris")).Query);
        Assert.Equal("paris", (await lamha.Lookup("PARIS")).Query); // shouting isn't a name
    }

    [Fact]
    public async Task EnglishEnglishIsAutomaticForEnglishAndTheSwitchIsRemembered()
    {
        using (var toEnglish = new TestLamha(Answering(_ => FakeNet.Json(429, "{}")), new LamhaSettings { UiLang = "en", TargetLang = "en" }, realDict: true))
        {
            Assert.True((await toEnglish.Lookup("resilient")).Explain); // no more "resilient → resilient"
        }

        using var lamha = new TestLamha(new FakeNet(Nets.FakeGoogle), new LamhaSettings { UiLang = "ar", UseContext = false }, realDict: true);
        Assert.Equal("مَرِن", (await lamha.Lookup("resilient")).Translation); // Arabic by default
        lamha.Lookups.SetExplain("en", true);
        Assert.True(lamha.Settings.Current.EnDict);
        Assert.True((await lamha.Lookup("resilient")).Explain);
        lamha.Lookups.SetExplain("en", false);
        Assert.Equal("مَرِن", (await lamha.Lookup("resilient")).Translation);
        Assert.False((await lamha.Lookup("Can we talk tomorrow?")).Explain); // sentences still translate
    }

    [Fact]
    public async Task EnglishEnglishOnlineUsesGooglesDefinitionsUnknownWordsOfflineSaySo()
    {
        var google = Answering(_ => Nets.GoogleWord("مرن", more: """ "definitions": [ { "pos": "adjective", "entry": [ { "gloss": "able to recover quickly from difficult conditions.", "example": "a <b>resilient</b> economy" } ] } ] """));
        using (var lamha = new TestLamha(google, new LamhaSettings { UiLang = "ar", EnDict = true, DictSource = DictSource.Online }, realDict: true))
        {
            var d = await lamha.Lookup("resilient");
            Assert.Equal((true, "able to recover quickly from difficult conditions.", "a resilient economy", "مرن", ResultSource.Online), (d.Explain, d.Translation, d.HeroExample, d.Meaning, d.From));
        }

        using var offline = new TestLamha(google, new LamhaSettings { UiLang = "ar", EnDict = true, DictSource = DictSource.Offline }, realDict: true);
        Assert.Equal("not_found_offline", await offline.LookupError("qwxzyv"));
    }

    [Fact]
    public async Task GooglesDefinitionsLeadWithThePartOfSpeechTheSentenceSuggests()
    {
        var google = Answering(_ => Nets.GoogleWord("ذكر", more: """
            "definitions": [
              { "pos": "noun", "base_form": "mention", "entry": [ { "gloss": "a reference to someone or something." } ] },
              { "pos": "verb", "base_form": "mention", "entry": [ { "gloss": "refer to something briefly.", "example": "I mentioned it to her" } ] } ]
            """));
        using var lamha = new TestLamha(google, new LamhaSettings { UiLang = "ar", EnDict = true, DictSource = DictSource.Online }, realDict: true);
        var d = await lamha.Lookup("mentioned", "Everything I ", " yesterday.");
        Assert.Equal(("refer to something briefly.", "verb", "v", true), (d.Translation, d.HeroPos.Label, d.HeroPos.Code, d.ContextSense));
    }

    [Fact]
    public async Task OnlineGooglesArabicForTheWordInItsSentencePicksTheSameSenseInTheEnglishView()
    {
        foreach (var (arabic, lead) in new[] { ("البنك", "financial institution"), ("الضفة", "sloping land") })
        {
            var net = Answering(c => Nets.IsGoogleBatch(c) ? FakeNet.Json(200, $"""[["التقينا قرب <a i=0>{arabic}</a> صباحًا.", "en"]]""") : FakeNet.Json(429, "{}"));
            using var lamha = new TestLamha(net, new LamhaSettings { UiLang = "ar", EnDict = true }, realDict: true);
            var d = await lamha.Lookup("bank", "We met near the ", " this morning.");
            Assert.Contains(lead, d.Translation, StringComparison.Ordinal);
            Assert.True(d.ContextSense);
            Assert.Contains(net.Calls, Nets.IsGoogleBatch); // the sentence was translated, as in the Arabic view
        }
    }

    private static FakeNet French(string translation = "maison") => Answering(c => Nets.IsGoogleSingle(c)
        ? Nets.GoogleWord(translation, "fr", """ "definitions": [ { "pos": "nom", "entry": [ { "gloss": "Bâtiment servant d'habitation.", "example": "une <b>maison</b> de campagne" }, { "gloss": "Famille, lignée." } ] } ] """)
        : FakeNet.Json(429, "{}"));

    [Fact]
    public async Task AFrenchWordWithFrenchAsTheTranslationLanguageIsExplainedInFrench()
    {
        using var lamha = new TestLamha(French(), new LamhaSettings { UiLang = "ar", TargetLang = "fr", DictSource = DictSource.Online }, realDict: true);
        var d = await lamha.Lookup("maison");
        Assert.Equal((true, "fr", "fr", "Bâtiment servant d'habitation.", "une maison de campagne", ""), (d.Explain, d.Source, d.Target, d.Translation, d.HeroExample, d.Meaning));
        Assert.Equal("Famille, lignée.", d.Definitions[0].Entries[1].Gloss);
    }

    [Fact]
    public async Task TheSwitchTurnsExplanationsOnForOneLanguageAtATime()
    {
        using var lamha = new TestLamha(French("منزل"), new LamhaSettings { UiLang = "ar", DictSource = DictSource.Online, UseContext = false }, realDict: true);
        Assert.Equal("منزل", (await lamha.Lookup("maison")).Translation); // translated by default
        lamha.Lookups.SetExplain("fr", true);
        Assert.Equal(["fr"], lamha.Settings.Current.ExplainLangs);
        var d = await lamha.Lookup("maison");
        Assert.Equal((true, "Bâtiment servant d'habitation.", "منزل", "ar"), (d.Explain, d.Translation, d.Meaning, d.Other));
        lamha.Lookups.SetExplain("fr", false);
        Assert.Empty(lamha.Settings.Current.ExplainLangs);
        Assert.False(lamha.Settings.Current.EnDict); // English has its own setting
    }

    [Fact]
    public async Task ALatinLetterWordThatIsntEnglishIsntForcedIntoTheEnglishDictionary()
    {
        using var lamha = new TestLamha(French("منزل"), new LamhaSettings { UiLang = "ar", EnDict = true, DictSource = DictSource.Online, UseContext = false }, realDict: true);
        var d = await lamha.Lookup("maison");
        Assert.Equal((false, "fr", "منزل"), (d.Explain, d.Source, d.Translation));
    }

    [Fact]
    public async Task ArabicIsExplainedInArabicByTheAiWithTheEnglishMeaningUnderIt()
    {
        var asked = new List<string>();
        var net = Answering(c =>
        {
            if (!Nets.IsOllama(c))
            {
                return FakeNet.Json(429, "{}"); // Google is down: the offline dictionary gives the English meaning
            }

            asked.Add(string.Join('\n', c.Json!["messages"]!.AsArray().Select(m => m!["content"]!.GetValue<string>())));
            return Replies.Ollama("""
                { "senses": [
                    { "pos": "noun", "gloss": "مجموعة أوراق مطبوعة ومجلّدة تُقرأ.", "example": "قرأتُ كتابًا ممتعًا.", "synonyms": ["مؤلَّف", "سِفر"] },
                    { "pos": "noun", "gloss": "رسالة مكتوبة.", "example": "وصلني كتابك.", "synonyms": [] } ],
                  "meaning": "book", "root": "ك ت ب", "plural": "كُتُب" }
                """);
        });
        using var lamha = new TestLamha(net, new LamhaSettings { UiLang = "ar", ExplainLangs = ["ar"], UseContext = false, AiProvider = Settings.AiProviderKind.Ollama, OllamaModel = "qwen3.5:4b" }, realDict: true);
        var d = await lamha.Lookup("كتاب");
        Assert.Equal((true, "ar", "ar", "en", ResultSource.Ai), (d.Explain, d.Source, d.Target, d.Other, d.From));
        Assert.Equal(("مجموعة أوراق مطبوعة ومجلّدة تُقرأ.", "قرأتُ كتابًا ممتعًا.", "n", "ك ت ب", "كُتُب"), (d.Translation, d.HeroExample, d.HeroPos.Code, d.Root, d.Plural));
        Assert.Equal("book", d.Meaning); // the English meaning, from the offline dictionary
        Assert.Equal([("n", 2)], d.Definitions.Select(p => (p.Pos.Code, p.Entries.Count))); // senses grouped by part of speech
        Assert.Contains("Arabic–Arabic learner's dictionary", asked[0], StringComparison.Ordinal);
        Assert.Contains("<word>\nكتاب\n</word>", asked[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAiExplanationInTheWrongScriptIsAskedAgainThenDropped()
    {
        var n = 0;
        var net = Answering(c => Nets.IsOllama(c)
            ? Replies.Ollama(Interlocked.Increment(ref n) > 0 ? """{ "senses": [ { "pos": "noun", "gloss": "مجموعة 캐시 أوراق.", "example": "", "synonyms": [] } ], "meaning": "", "root": "", "plural": "" }""" : "")
            : FakeNet.Json(429, "{}"));
        using var lamha = new TestLamha(net, new LamhaSettings { UiLang = "ar", ExplainLangs = ["ar"], UseContext = false, AiProvider = Settings.AiProviderKind.Ollama, OllamaModel = "qwen3.5:4b" }, realDict: true);
        var d = await lamha.Lookup("كتاب");
        Assert.Equal(2, n); // asked twice
        Assert.Equal((false, "book", true), (d.Explain, d.Translation, d.ExplainMissing));
    }

    [Fact]
    public async Task NoDefinitionsAndNoAiASameLanguageWordSaysThereIsNoExplanation()
    {
        var google = Answering(c => Nets.IsGoogleSingle(c) ? Nets.GoogleWord("maison", "fr") : FakeNet.Json(429, "{}"));
        using var lamha = new TestLamha(google, new LamhaSettings { UiLang = "ar", TargetLang = "fr", DictSource = DictSource.Online }, realDict: true);
        var d = await lamha.Lookup("maison");
        Assert.Equal((true, "", true), (d.Explain, d.Translation, d.ExplainMissing));
    }

    [Fact]
    public async Task AWordNoDictionaryHasIsAGuessKeptOutOfTheHistoryAKnownWordGetsTheDictionary()
    {
        var net = Nets.Translation();
        using (var lamha = new TestLamha(net, new LamhaSettings { UiLang = "ar", DictSource = DictSource.Offline, AiProvider = Settings.AiProviderKind.Ollama, OllamaModel = "qwen3.5:4b" }, realDict: true))
        {
            var d = await lamha.Lookup("blorptastic");
            Assert.Equal((ResultSource.Ai, true), (d.From, d.AiGuess));
            Assert.False(lamha.History.Contains("blorptastic"));
        }

        // "Online first" with Google down reached the AI before the dictionary: a word the dictionary knows is its answer.
        var down = Nets.Translation(googleUp: false);
        using var online = new TestLamha(down, new LamhaSettings { UiLang = "ar", DictSource = DictSource.Online, UseContext = false, AiProvider = Settings.AiProviderKind.Ollama, OllamaModel = "qwen3.5:4b" }, realDict: true);
        var known = await online.Lookup("resilient");
        Assert.Equal((ResultSource.Local, false), (known.From, known.AiGuess));
    }

    [Fact]
    public async Task WordsNamedLikeObjectPropertiesLookUpOfflineLikeAnyOther()
    {
        using var lamha = new TestLamha(Answering(_ => FakeNet.Json(404, "{}")), new LamhaSettings { UiLang = "ar", DictSource = DictSource.Offline }, realDict: true);
        var r = await lamha.Lookup("constructor");
        Assert.Equal(ResultSource.Local, r.From);
        Assert.Matches(@"\p{IsArabic}", r.Translation);
    }

    [Fact]
    public async Task NoNetworkAtAllTheDictionaryAnswersWithoutAskingTheInternet()
    {
        var net = Answering(_ => throw FakeNet.Offline());
        using var lamha = new TestLamha(net, new LamhaSettings { UiLang = "ar", DictSource = DictSource.Online }, realDict: true);
        lamha.Network.IsOffline = true;
        Assert.Equal(ResultSource.Local, (await lamha.Lookup("house")).From); // Online first, but there's no network
        Assert.Null(await lamha.Run(() => lamha.Wikipedia.SummaryAsync("house", "ar")));
        Assert.Empty(net.Calls); // nothing was sent to the internet
    }

    /// <summary>A French pack as tools/build_packs.py writes one, in memory.</summary>
    private sealed class FrenchPack : ILanguagePacks
    {
        private static readonly DictionaryEntry Maison = new("mɛ.zɔ̃",
        [
            new("n", "Bâtiment servant d'habitation.", "une maison de campagne", ["demeure", "logis"], []),
            new("n", "Famille, lignée.", "", [], []),
            new("v", "(verbe fictif pour le test)", "", [], []),
        ], []);

        private static readonly DictionaryEntry Famille = new("", [new("n", "Ensemble des parents.", "", [], [])], []);

        public IReadOnlyList<string> Languages => ["fr"];

        public string Key => "fr";

        public PackHit? Find(string lang, string text) => (lang, text.Trim().ToLowerInvariant()) switch
        {
            ("fr", "maison") => new PackHit("maison", "", Maison),
            ("fr", "maisons") => new PackHit("maison", "maisons", Maison),
            ("fr", "famille") => new PackHit("famille", "", Famille),
            _ => null,
        };
    }

    [Fact]
    public async Task APackFindsWordsAndTheirFormsWithNoInternet()
    {
        var net = Answering(_ => FakeNet.Json(429, "{}"));
        using (var lamha = new TestLamha(net, new LamhaSettings { UiLang = "ar", TargetLang = "fr", DictSource = DictSource.Offline }, realDict: true, packs: new FrenchPack()))
        {
            var d = await lamha.Lookup("maisons");
            Assert.Equal((true, ResultSource.Local, "maison", "maisons", "Bâtiment servant d'habitation.", "une maison de campagne", "n", "mɛ.zɔ̃"),
                (d.Explain, d.From, d.Query, d.Inflected, d.Translation, d.HeroExample, d.HeroPos.Code, d.SourceTranslit));
            Assert.Equal([("n", 2), ("v", 1)], d.Definitions.Select(p => (p.Pos.Code, p.Entries.Count))); // grouped by part of speech
            Assert.Equal(["demeure", "logis"], d.Definitions[0].Entries[0].Synonyms);
            Assert.Equal("famille", (await lamha.Lookup("Famille")).Query); // any capitalisation
        }

        using var toArabic = new TestLamha(net, new LamhaSettings { UiLang = "ar", DictSource = DictSource.Offline }, realDict: true, packs: new FrenchPack());
        Assert.Equal(ResultSource.Local, (await toArabic.Lookup("maison")).From); // offline, any installed pack beats an error
        Assert.Empty(net.Calls);
    }

    [Fact]
    public async Task OnlineAPackExplainsAWordBeforeTheAiIsAsked()
    {
        var net = Answering(c => Nets.IsGoogleSingle(c) ? Nets.GoogleWord("منزل", "fr") : FakeNet.Json(429, "{}"));
        using var lamha = new TestLamha(net, new LamhaSettings { UiLang = "ar", ExplainLangs = ["fr"], DictSource = DictSource.Online, UseContext = false, AiProvider = Settings.AiProviderKind.Ollama, OllamaModel = "qwen3.5:4b" }, realDict: true, packs: new FrenchPack());
        var d = await lamha.Lookup("maison");
        Assert.Equal((true, ResultSource.Local, "Bâtiment servant d'habitation.", "منزل", "ar"), (d.Explain, d.From, d.Translation, d.Meaning, d.Other));
        Assert.DoesNotContain(net.Calls, Nets.IsOllama); // no AI call
    }
}
