using Lamha.Core.Settings;
using Lamha.Core.Tests.Support;
using Lamha.Core.Text;
using Lamha.Core.Translation;

namespace Lamha.Core.Tests.Translation;

/// <summary>Lamha 2's test-writing.mjs: the translation service (Google, the AI, or both) and long texts.</summary>
public class TranslationTests
{
    private const string Sentence = "It's a piece of cake, honestly.";
    private static readonly Dictionary<string, string> GeminiKey = new() { ["gemini"] = "AIza-test" };

    private static LamhaSettings WithGemini(TranslateWith service = TranslateWith.Auto) =>
        new() { UiLang = "ar", AiProvider = AiProviderKind.Gemini, TrService = service };

    [Fact]
    public async Task TranslationsKeepParagraphBreaksAndSpacesInsideLinesAreTidied()
    {
        using var lamha = new TestLamha(new FakeNet(Nets.FakeGoogle));
        var r = await lamha.Lookup("Hello   there.\r\n\n\n\nGood morning.  ");
        Assert.Equal("Hello there.\n\nGood morning.", r.Query);
        Assert.Equal("AR<Hello there.>\n\nAR<Good morning.>", r.Translation);
    }

    [Fact]
    public async Task LongTextIsTranslatedInPiecesAndJoinedBackWholeTooLongIsRefused()
    {
        var net = new FakeNet(Nets.FakeGoogle);
        using var lamha = new TestLamha(net);
        var text = string.Join("\n\n", Enumerable.Range(0, 12).Select(i => $"Paragraph {i}. " + string.Concat(Enumerable.Repeat("Some words here. ", 50)).Trim()));
        Assert.True(text.Length > 9000);
        var r = await lamha.Lookup(text);
        Assert.True(net.Calls.Count >= 2, "more than one request");
        var back = r.Translation.Split("\n\n");
        Assert.Equal(12, back.Length); // every paragraph break survives
        for (var i = 0; i < 12; i++)
        {
            Assert.Contains($"Paragraph {i}. ", back[i], StringComparison.Ordinal);
        }

        Assert.Equal("too_long", await lamha.LookupError(string.Concat(Enumerable.Repeat("word ", 7000))));
    }

    [Fact]
    public async Task AutomaticIsGoogleFirstAndTheQuickAiWithATranslatorsPromptWhenGoogleCant()
    {
        var up = Nets.Translation();
        using (var lamha = new TestLamha(up, WithGemini(), GeminiKey))
        {
            var a = await lamha.Lookup(Sentence);
            Assert.Equal(["google"], Nets.Who(up.Calls));
            Assert.Equal(ResultSource.Online, a.From);
        }

        var down = Nets.Translation(googleUp: false);
        using var offline = new TestLamha(down, WithGemini(), GeminiKey);
        var b = await offline.Lookup(Sentence);
        Assert.Equal("AI<" + Sentence + ">", b.Translation);
        Assert.Equal((ResultSource.Ai, "Gemini"), (b.From, b.AiName));
        var ai = down.Calls.First(Nets.IsGemini);
        Assert.Contains("gemini-3.5-flash-lite", ai.Url, StringComparison.Ordinal); // translation defaults to the quick model
        Assert.Contains("professional translator", ai.Json!["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains("Idioms and slang become their natural equivalent", ai.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GoogleOnlyNeverAsksTheAiAndAiFirstFallsBackToGoogle()
    {
        var down = Nets.Translation(googleUp: false);
        using (var google = new TestLamha(down, WithGemini(TranslateWith.Google), GeminiKey))
        {
            Assert.NotNull(await google.LookupError(Sentence));
            Assert.DoesNotContain("gemini", Nets.Who(down.Calls));
        }

        var quota = Nets.Translation(aiReply: (_, _) => null); // Gemini's free quota is used up
        using var ai = new TestLamha(quota, WithGemini(TranslateWith.Ai), GeminiKey);
        var r = await ai.Lookup(Sentence);
        Assert.Equal(ResultSource.Online, r.From); // Google stepped in
        Assert.Equal(["gemini", "google"], Nets.Who(quota.Calls).Distinct());
    }

    [Fact]
    public async Task WordsKeepGooglesDictionaryFirstEvenWithTheAiChosen()
    {
        var net = Nets.Translation();
        using var lamha = new TestLamha(net, WithGemini(TranslateWith.Ai) with { DictSource = DictSource.Online, UseContext = false, TranslateDefinitions = false, ShowWikipedia = false }, GeminiKey);
        await lamha.Lookup("resilient");
        Assert.Equal(["google"], Nets.Who(net.Calls));
    }

    [Fact]
    public async Task StrayLettersAreAskedAgainThenGoogleAndShownOnlyIfNothingElseWorks()
    {
        static string Korean(string t) => t.Replace("cake", "캐시", StringComparison.Ordinal);

        // Once wrong, then right: the second answer is used.
        var retry = Nets.Translation(aiReply: (texts, n) => [.. texts.Select(t => "AI<" + (n == 1 ? Korean(t) : t) + ">")]);
        using (var e1 = new TestLamha(retry, WithGemini(TranslateWith.Ai), GeminiKey))
        {
            Assert.Equal("AI<" + Sentence + ">", (await e1.Lookup(Sentence)).Translation);
        }

        // Always wrong, Google up: Google's answer.
        var bad = Nets.Translation(aiReply: (texts, _) => [.. texts.Select(Korean)]);
        using (var e2 = new TestLamha(bad, WithGemini(TranslateWith.Ai), GeminiKey))
        {
            Assert.Equal(ResultSource.Online, (await e2.Lookup(Sentence)).From);
        }

        // Always wrong and Google down: the rough answer beats nothing, and isn't cached.
        var alone = Nets.Translation(googleUp: false, aiReply: (texts, _) => [.. texts.Select(Korean)]);
        using var e3 = new TestLamha(alone, WithGemini(), GeminiKey);
        var r3 = await e3.Lookup(Sentence);
        Assert.Contains("캐시", r3.Translation, StringComparison.Ordinal);
        Assert.True(r3.Rough);
        var before = alone.Calls.Count;
        await e3.Lookup(Sentence);
        Assert.True(alone.Calls.Count > before, "asked again next time");
    }

    [Fact]
    public async Task BetterTranslationAsksTheAiWhateverTheSetting()
    {
        var net = Nets.Translation();
        using var lamha = new TestLamha(net, WithGemini(TranslateWith.Google), GeminiKey);
        var better = await lamha.Lookup(Sentence, forceAi: true);
        Assert.Equal(ResultSource.Ai, better.From);
        Assert.Equal(["gemini"], Nets.Who(net.Calls));
    }

    [Fact]
    public async Task MarkupKeepsItsTagsThroughTheAi()
    {
        var net = Nets.Translation();
        using var lamha = new TestLamha(net, WithGemini(TranslateWith.Ai), GeminiKey);
        var t = await lamha.Run(() => lamha.Translation.TranslateAsync(["Hello <a i=0>world</a>"], "ar", html: true));
        Assert.Equal("AI<Hello <a i=0>world</a>>", t.Texts[0]);
        Assert.Contains("keep every tag", net.Calls[^1].Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalOnlyTranslatesSentencesWithALocalAiNeverAnOnlineOne()
    {
        var net = Nets.Translation();
        using (var local = new TestLamha(net, new LamhaSettings { UiLang = "ar", DictSource = DictSource.Offline, AiProvider = AiProviderKind.Ollama, OllamaModel = "qwen3.5:4b" }))
        {
            var r = await local.Lookup(Sentence);
            Assert.Equal((ResultSource.Ai, "Ollama"), (r.From, r.AiName));
            Assert.Equal(["ollama"], Nets.Who(net.Calls));
        }

        using var online = new TestLamha(net, WithGemini() with { DictSource = DictSource.Offline }, GeminiKey);
        Assert.Equal("offline_mode", await online.LookupError(Sentence));

        // Lamha 3: a custom server on this PC counts as local.
        var lm = new FakeNet(c => Replies.OpenAi(new System.Text.Json.Nodes.JsonObject { ["translations"] = new System.Text.Json.Nodes.JsonArray(Nets.TextsOf(c.Prompt).Select(t => (System.Text.Json.Nodes.JsonNode)("LM<" + t + ">")).ToArray()) }.ToJsonString()));
        using var lmStudio = new TestLamha(lm, new LamhaSettings { UiLang = "ar", DictSource = DictSource.Offline, AiProvider = AiProviderKind.Custom, CustomUrl = "http://localhost:1234/v1", CustomModel = "qwen" });
        Assert.Equal("LM<" + Sentence + ">", (await lmStudio.Lookup(Sentence)).Translation);
    }

    [Fact]
    public async Task TranslationHasItsOwnProviderAndModel()
    {
        var net = Nets.Translation(googleUp: false);
        // Writing on Ollama, translation on Gemini Flash.
        var settings = new LamhaSettings
        {
            UiLang = "ar",
            AiProvider = AiProviderKind.Ollama,
            OllamaModel = "qwen3.5:4b",
            TrProvider = AiProviderKind.Gemini,
            TrModels = new Dictionary<string, string> { ["gemini"] = "gemini-3.8-flash" },
        };
        using (var lamha = new TestLamha(net, settings, GeminiKey))
        {
            await lamha.Lookup(Sentence);
            Assert.Contains("gemini-3.8-flash", net.Calls.First(Nets.IsGemini).Url, StringComparison.Ordinal);
        }

        using var claude = new TestLamha(new FakeNet(_ => FakeNet.Json(429, "{}")), new LamhaSettings { UiLang = "ar", TrProvider = AiProviderKind.Claude }, new Dictionary<string, string> { ["claude"] = "sk-test" });
        Assert.Equal("claude-haiku-5-5", claude.Translation.TranslatorAi()!.Model); // Claude translates with Haiku unless told otherwise
    }

    [Fact]
    public void ChunksKeepUnderTheLimits()
    {
        var lengths = new[] { 3000, 3000, 10, 6000, 1 };
        var chunks = Chunks.ByLength(Enumerable.Range(0, lengths.Length), i => lengths[i], 5000, 100);
        Assert.Equal([[0], [1, 2], [3], [4]], chunks);
        Assert.Equal(3, Chunks.ByLength(Enumerable.Range(0, 250), _ => 1, 5000, 100).Count);
        Assert.Equal("a b", Pieces.Tidy(" a  b "));
    }
}
