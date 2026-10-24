using System.Text.Json.Nodes;
using Lamha.Core.Ai;
using Lamha.Core.Settings;
using Lamha.Core.Tests.Support;
using Lamha.Core.Writing;

namespace Lamha.Core.Tests.Ai;

/// <summary>Lamha 2's test-writing.mjs for Ollama, Gemini and Claude's errors, and Lamha 3's OpenAI and custom servers.</summary>
public class AiProviderTests
{
    private static LamhaSettings Using(AiProviderKind provider, string ollamaModel = "") =>
        new() { UiLang = "ar", AiProvider = provider, OllamaModel = ollamaModel };

    [Fact]
    public async Task OllamaRequestShapeAndParsing()
    {
        var net = new FakeNet(_ => Replies.Ollama(Replies.Text("Could you send me the report today?")));
        using var lamha = new TestLamha(net, Using(AiProviderKind.Ollama, "qwen3.5:latest"));
        var result = await lamha.Writing.RunAsync(WritingTool.Formal, "send me report today", null, TestContext.Current.CancellationToken);
        Assert.Equal("Could you send me the report today?", result.Text);
        var call = net.Calls[0];
        var body = call.Json!;
        Assert.Equal("http://localhost:11434/api/chat", call.Url);
        Assert.Equal("qwen3.5:latest", body["model"]!.GetValue<string>());
        Assert.False(body["stream"]!.GetValue<bool>());
        Assert.False(body["think"]!.GetValue<bool>());
        Assert.Equal("text", body["format"]!["required"]![0]!.GetValue<string>());
        Assert.Equal(4096, body["options"]!["num_ctx"]!.GetValue<int>());
        Assert.Equal("system", body["messages"]![0]!["role"]!.GetValue<string>());
    }

    [Fact]
    public async Task OllamaRetriesWithoutThinkForModelsThatRejectIt()
    {
        var net = new FakeNet(c => c.Json!["think"] is not null
            ? FakeNet.Json(400, """{ "error": "\"gemma\" does not support thinking" }""")
            : Replies.Ollama(Replies.Text("fine")));
        using var lamha = new TestLamha(net, Using(AiProviderKind.Ollama, "gemma"));
        Assert.Equal("fine", (await lamha.Writing.RunAsync(WritingTool.Improve, "hello", null, TestContext.Current.CancellationToken)).Text);
        Assert.Equal(2, net.Calls.Count);
        Assert.Null(net.Calls[1].Json!["think"]);
    }

    [Theory]
    [InlineData(0, "", "ollama_offline")]
    [InlineData(403, "{}", "ollama_origin")]
    [InlineData(404, """{ "error": "model \"qwen9\" not found, try pulling it first" }""", "ollama_model")]
    public async Task OllamaErrorsMapToCodes(int status, string body, string code)
    {
        var net = new FakeNet(_ => status == 0 ? throw FakeNet.Offline() : FakeNet.Json(status, body));
        using var lamha = new TestLamha(net, Using(AiProviderKind.Ollama, "qwen9"));
        Assert.Equal(code, await lamha.ErrorOf(() => lamha.Writing.RunAsync(WritingTool.Improve, "hello")));
    }

    [Fact]
    public async Task OllamaWithoutAModelIsNotSetUp()
    {
        using var lamha = new TestLamha(new FakeNet(_ => FakeNet.Json(200, "{}")), Using(AiProviderKind.Ollama));
        Assert.Equal("ollama_no_model", await lamha.ErrorOf(() => lamha.Writing.RunAsync(WritingTool.Improve, "hi")));
    }

    [Theory]
    [InlineData(401, "{}", "ai_bad_key")]
    [InlineData(400, """{ "error": { "message": "Your credit balance is too low" } }""", "ai_no_credit")]
    [InlineData(429, "{}", "ai_rate_limited")]
    [InlineData(404, "{}", "ai_model")]
    public async Task ClaudeErrorsMapToCodes(int status, string body, string code)
    {
        using var lamha = new TestLamha(new FakeNet(_ => FakeNet.Json(status, body)), keys: new Dictionary<string, string> { ["claude"] = "k" });
        Assert.Equal(code, await lamha.ErrorOf(() => lamha.Writing.RunAsync(WritingTool.Improve, "hi")));
    }

    [Fact]
    public async Task ClaudeBusyIsTriedOnceMoreAndHaikuHasNoFallback()
    {
        var net = new FakeNet((_, n) => n == 1 ? FakeNet.Json(529, "{}") : Replies.Claude(Replies.Text("ok")));
        using var lamha = new TestLamha(net, new LamhaSettings { UiLang = "ar", ClaudeModel = "claude-haiku-5-5" }, new Dictionary<string, string> { ["claude"] = "k" });
        Assert.Equal("ok", (await lamha.Run(() => lamha.Writing.RunAsync(WritingTool.Improve, "hi"))).Text);
        Assert.Equal(2, net.Calls.Count);
        Assert.Null(net.Calls[0].Json!["fallbacks"]);
        Assert.Equal("", net.Calls[0].Header("anthropic-beta"));
    }

    [Fact]
    public async Task ClaudeRefusalsAndCutAnswersHaveTheirCodes()
    {
        foreach (var (reason, code) in new[] { ("refusal", "ai_refused"), ("max_tokens", "ai_too_long") })
        {
            var net = new FakeNet(_ => FakeNet.Json(200, new JsonObject { ["stop_reason"] = reason, ["content"] = new JsonArray() }));
            using var lamha = new TestLamha(net, keys: new Dictionary<string, string> { ["claude"] = "k" });
            Assert.Equal(code, await lamha.ErrorOf(() => lamha.Writing.RunAsync(WritingTool.Improve, "hi")));
        }
    }

    private static readonly Dictionary<string, string> GeminiKey = new() { ["gemini"] = "AIza-test" };

    [Fact]
    public async Task GeminiRequestShapeSchemaConvertedThoughtsIgnored()
    {
        var net = new FakeNet(_ => Replies.Gemini(Replies.Proof));
        using var lamha = new TestLamha(net, new LamhaSettings { UiLang = "ar", AiProvider = AiProviderKind.Gemini, GeminiModel = "gemini-3.5-flash-lite" }, GeminiKey);
        var result = await lamha.Writing.RunAsync(WritingTool.Proofread, "I go to market yesterday.", null, TestContext.Current.CancellationToken);
        Assert.Equal(3, result.Issues.Count);
        var call = net.Calls[0];
        var body = call.Json!;
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash-lite:generateContent", call.Url);
        Assert.Equal("AIza-test", call.Header("x-goog-api-key"));
        Assert.Contains("Arabic speakers", body["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains("<text>\nI go to market yesterday.\n</text>", call.Prompt, StringComparison.Ordinal);
        var schema = body["generationConfig"]!["responseSchema"]!;
        Assert.Equal("application/json", body["generationConfig"]!["responseMimeType"]!.GetValue<string>());
        Assert.Equal("OBJECT", schema["type"]!.GetValue<string>());
        Assert.Equal("ARRAY", schema["properties"]!["issues"]!["type"]!.GetValue<string>());
        Assert.Equal("STRING", schema["properties"]!["issues"]!["items"]!["properties"]!["category"]!["type"]!.GetValue<string>());
        Assert.DoesNotContain("additionalProperties", schema.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeminiDefaultModelNotSetUpBlockedAndCutAnswers()
    {
        var net = new FakeNet(_ => Replies.Gemini(Replies.Text("ok")));
        using (var lamha = new TestLamha(net, new LamhaSettings { UiLang = "ar", AiProvider = AiProviderKind.Gemini, GeminiModel = "made-up" }, GeminiKey))
        {
            await lamha.Writing.RunAsync(WritingTool.Improve, "hi", null, TestContext.Current.CancellationToken);
            Assert.Contains("models/gemini-3.8-flash:generateContent", net.Calls[0].Url, StringComparison.Ordinal);
        }

        using (var none = new TestLamha(net, Using(AiProviderKind.Gemini)))
        {
            Assert.Equal("gemini_no_key", await none.ErrorOf(() => none.Writing.RunAsync(WritingTool.Improve, "hi")));
        }

        var cases = new (Func<HttpResponseMessage> Reply, string Code)[]
        {
            (() => FakeNet.Json(200, """{ "promptFeedback": { "blockReason": "SAFETY" } }"""), "ai_refused"),
            (() => Replies.Gemini(Replies.Text("x"), "SAFETY"), "ai_refused"),
            (() => Replies.Gemini(Replies.Text("x"), "MAX_TOKENS"), "ai_too_long"),
        };
        foreach (var (reply, code) in cases)
        {
            using var lamha = new TestLamha(new FakeNet(_ => reply()), Using(AiProviderKind.Gemini), GeminiKey);
            Assert.Equal(code, await lamha.ErrorOf(() => lamha.Writing.RunAsync(WritingTool.Improve, "hi")));
        }
    }

    [Theory]
    [InlineData(400, "API key not valid. Please pass a valid API key.", "gemini_bad_key")]
    [InlineData(429, "Quota exceeded", "gemini_quota")]
    [InlineData(400, "User location is not supported for the API use.", "gemini_region")]
    [InlineData(404, "models/x is not found", "ai_model")]
    public async Task GeminiErrorsMapToCodes(int status, string message, string code)
    {
        var body = new JsonObject { ["error"] = new JsonObject { ["code"] = status, ["message"] = message } };
        using var lamha = new TestLamha(new FakeNet(_ => FakeNet.Json(status, body)), Using(AiProviderKind.Gemini), GeminiKey);
        Assert.Equal(code, await lamha.ErrorOf(() => lamha.Writing.RunAsync(WritingTool.Improve, "hi")));
    }

    [Fact]
    public async Task GeminiBusyIsRetriedThenTheOtherFreeModelAnswers()
    {
        var net = new FakeNet(c => c.Url.Contains("gemini-3.8-flash:", StringComparison.Ordinal)
            ? FakeNet.Json(503, """{ "error": { "code": 503, "status": "UNAVAILABLE", "message": "The model is overloaded. Please try again later." } }""")
            : Replies.Gemini(Replies.Text("fixed")));
        using var lamha = new TestLamha(net, Using(AiProviderKind.Gemini), GeminiKey);
        Assert.Equal("fixed", (await lamha.Run(() => lamha.Writing.RunAsync(WritingTool.Improve, "hi"))).Text);
        Assert.Equal(
            ["gemini-3.8-flash", "gemini-3.8-flash", "gemini-3.8-flash", "gemini-3.5-flash-lite"],
            net.Calls.Select(c => c.Url.Split("models/")[1].Split(':')[0]));
    }

    [Fact]
    public async Task GeminiKeyCheckPassesWhenThinkingUsedTheSmallBudgetOrTheModelsAreBusy()
    {
        using (var lamha = new TestLamha(new FakeNet(_ => Replies.Gemini(Replies.Text("x"), "MAX_TOKENS"))))
        {
            Assert.Null(await lamha.Ai.TestAsync(new AiConfig(AiProviderKind.Gemini, "gemini-3.8-flash", "k"), TestContext.Current.CancellationToken));
        }

        using (var busy = new TestLamha(new FakeNet(_ => FakeNet.Json(503, """{ "error": { "message": "The model is overloaded." } }"""))))
        {
            Assert.Equal("gemini_busy", await busy.Run(() => busy.Ai.TestAsync(new AiConfig(AiProviderKind.Gemini, "gemini-3.8-flash", "k"))));
        }

        using var bad = new TestLamha(new FakeNet(_ => FakeNet.Json(400, """{ "error": { "message": "API key not valid. Please pass a valid API key." } }""")));
        var e = await Assert.ThrowsAsync<LamhaException>(() => bad.Ai.TestAsync(new AiConfig(AiProviderKind.Gemini, "gemini-3.8-flash", "k"), TestContext.Current.CancellationToken));
        Assert.Equal("gemini_bad_key", e.Code);
        Assert.Contains("API key not valid", e.Detail, StringComparison.Ordinal); // Google's own words, for Settings
    }

    [Fact]
    public void WhichProviderIsSetUp()
    {
        using var lamha = new TestLamha(new FakeNet(_ => FakeNet.Json(200, "{}")));
        string? Code(Action setup)
        {
            setup();
            try
            {
                lamha.Ai.Config();
                return null;
            }
            catch (LamhaException e)
            {
                return e.Code;
            }
        }

        Assert.Equal("ai_no_key", Code(() => { }));
        Assert.Equal("gemini_no_key", Code(() => lamha.Settings.Update(s => s with { AiProvider = AiProviderKind.Gemini })));
        Assert.Equal("gemini_no_key", Code(() => lamha.Secrets.Set("claude", "k")));  // a Claude key doesn't make Gemini ready
        Assert.Null(Code(() => lamha.Secrets.Set("gemini", "k")));
        Assert.Equal("openai_no_key", Code(() => lamha.Settings.Update(s => s with { AiProvider = AiProviderKind.OpenAi })));
        Assert.Equal("custom_no_server", Code(() => lamha.Settings.Update(s => s with { AiProvider = AiProviderKind.Custom })));
        Assert.Equal("custom_insecure", Code(() => lamha.Settings.Update(s => s with { CustomUrl = "http://example.com/v1", CustomModel = "m" })));
        Assert.Null(Code(() => lamha.Settings.Update(s => s with { CustomUrl = "http://192.168.1.7:1234/v1/" })));
        Assert.Equal("Ollama", lamha.Ai.Config(AiProviderKind.Ollama, "q").Name);
        Assert.DoesNotContain("k", lamha.Ai.Config(AiProviderKind.Gemini).ToString(), StringComparison.Ordinal); // never the key
    }

    [Fact]
    public async Task OpenAiSpeaksChatCompletionsWithAJsonSchema()
    {
        var net = new FakeNet(_ => Replies.OpenAi(Replies.Proof));
        using var lamha = new TestLamha(net, new LamhaSettings { UiLang = "ar", AiProvider = AiProviderKind.OpenAi, OpenAiModel = "gpt-test" }, new Dictionary<string, string> { ["openai"] = "sk-proj" });
        var result = await lamha.Writing.RunAsync(WritingTool.Proofread, "I go to market yesterday.", null, TestContext.Current.CancellationToken);
        Assert.Equal(3, result.Issues.Count);
        var call = net.Calls[0];
        Assert.Equal("https://api.openai.com/v1/chat/completions", call.Url);
        Assert.Equal("Bearer sk-proj", call.Header("authorization"));
        var body = call.Json!;
        Assert.Equal("gpt-test", body["model"]!.GetValue<string>());
        Assert.Equal("system", body["messages"]![0]!["role"]!.GetValue<string>());
        Assert.Equal("json_schema", body["response_format"]!["type"]!.GetValue<string>());
        Assert.True(body["response_format"]!["json_schema"]!["strict"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ACustomServerWithoutJsonSchemaIsAskedForPlainJson()
    {
        var net = new FakeNet(c => c.Json!["response_format"]!["type"]!.GetValue<string>() == "json_schema"
            ? FakeNet.Json(400, """{ "error": { "message": "'response_format.type' must be 'json_object' or 'text'" } }""")
            : Replies.OpenAi("```json\n" + Replies.Text("fine") + "\n```"));
        using var lamha = new TestLamha(net, new LamhaSettings { UiLang = "ar", AiProvider = AiProviderKind.Custom, CustomUrl = "http://localhost:1234/v1", CustomModel = "qwen" });
        Assert.Equal("fine", (await lamha.Writing.RunAsync(WritingTool.Improve, "hello", null, TestContext.Current.CancellationToken)).Text);
        Assert.Equal("http://localhost:1234/v1/chat/completions", net.Calls[0].Url);
        Assert.Equal("", net.Calls[0].Header("authorization")); // no key, no header
        Assert.EndsWith("Answer with JSON only.", net.Calls[1].Prompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "", "custom_offline")]
    [InlineData(401, "{}", "ai_bad_key")]
    [InlineData(402, "{}", "ai_no_credit")]
    [InlineData(429, """{ "error": { "code": "insufficient_quota", "message": "You exceeded your current quota" } }""", "ai_no_credit")]
    [InlineData(429, """{ "error": { "code": "rate_limit_exceeded", "message": "Slow down" } }""", "ai_rate_limited")]
    [InlineData(404, "{}", "ai_model")]
    public async Task CustomServerErrorsMapToCodes(int status, string body, string code)
    {
        var net = new FakeNet(_ => status == 0 ? throw FakeNet.Offline() : FakeNet.Json(status, body));
        using var lamha = new TestLamha(net, new LamhaSettings { UiLang = "ar", AiProvider = AiProviderKind.Custom, CustomUrl = "https://openrouter.ai/api/v1", CustomModel = "m" }, new Dictionary<string, string> { ["custom"] = "or-key" });
        Assert.Equal(code, await lamha.ErrorOf(() => lamha.Writing.RunAsync(WritingTool.Improve, "hi")));
    }

    [Fact]
    public async Task ModelListsComeFromTheServerOpenAiKeepsItsChatModels()
    {
        var models = """{ "data": [ { "id": "gpt-5" }, { "id": "o4-mini" }, { "id": "text-embedding-3-large" }, { "id": "gpt-4o-realtime-preview" }, { "id": "dall-e-3" } ] }""";
        using var lamha = new TestLamha(new FakeNet(_ => FakeNet.Json(200, models)));
        var openAi = await lamha.Ai.Provider(AiProviderKind.OpenAi).ListModelsAsync(new AiConfig(AiProviderKind.OpenAi, "", "k"), TestContext.Current.CancellationToken);
        Assert.Equal(["gpt-5", "o4-mini"], openAi.Select(m => m.Name));
        var custom = await lamha.Ai.Provider(AiProviderKind.Custom).ListModelsAsync(new AiConfig(AiProviderKind.Custom, "", "", "http://localhost:1234/v1"), TestContext.Current.CancellationToken);
        Assert.Equal(5, custom.Count);
        Assert.Equal("http://localhost:1234/v1/models", lamha.Net.Calls[1].Url);
    }

    [Theory]
    [InlineData("https://openrouter.ai/api/v1/", "https://openrouter.ai/api/v1")]
    [InlineData("http://localhost:1234/v1", "http://localhost:1234/v1")]
    [InlineData("http://127.0.0.1:8080", "http://127.0.0.1:8080")]
    [InlineData("http://192.168.1.7:1234/v1", "http://192.168.1.7:1234/v1")]
    [InlineData("http://10.0.0.5/v1", "http://10.0.0.5/v1")]
    [InlineData("http://[::1]:1234", "http://[::1]:1234")]
    [InlineData("http://gaming-pc:1234/v1", "http://gaming-pc:1234/v1")]
    [InlineData("http://example.com/v1", null)]
    [InlineData("http://8.8.8.8/v1", null)]
    [InlineData("ftp://localhost/v1", null)]
    [InlineData("localhost:1234", null)]
    public void PlainHttpOnlyOnThisPcAndTheLocalNetwork(string url, string? expected) => Assert.Equal(expected, CustomServer.Normalize(url));
}
