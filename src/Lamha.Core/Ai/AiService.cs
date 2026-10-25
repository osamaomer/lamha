using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lamha.Core.Abstractions;
using Lamha.Core.Net;
using Lamha.Core.Settings;

namespace Lamha.Core.Ai;

/// <summary>
/// Which AI to call and how: the writing tools' choice or the translator's, with its key or address, and one request
/// to it. Lamha 2's <c>aiProviderConfig()</c>, <c>aiComplete()</c>, <c>parseAiJSON()</c> and <c>aiTest()</c>.
/// </summary>
public sealed partial class AiService(SettingsService settings, ISecretStore secrets, Http http)
{
    private readonly ClaudeProvider _claude = new(http);
    private readonly GeminiProvider _gemini = new(http);
    private readonly OpenAiCompatibleProvider _openAi = new(http);
    private readonly OllamaProvider _ollama = new(http);

    /// <summary>
    /// The AI to call: the writing tools' provider and model by default; translation passes its own. Throws the "not set
    /// up" code when that provider has no key, address or model.
    /// </summary>
    public AiConfig Config(AiProviderKind? provider = null, string? model = null)
    {
        var s = settings.Current;
        var kind = provider ?? s.AiProvider;
        switch (kind)
        {
            case AiProviderKind.Ollama:
                var ollamaModel = Pick(model, s.OllamaModel) ?? throw new LamhaException(ErrorCodes.OllamaNoModel);
                return new AiConfig(kind, ollamaModel, BaseUrl: OllamaProvider.BaseUrl(s.OllamaUrl));
            case AiProviderKind.Gemini:
                var geminiKey = secrets.Get("gemini") is { Length: > 0 } g ? g : throw new LamhaException(ErrorCodes.GeminiNoKey);
                var geminiModel = AiModels.Gemini.Contains(model) ? model! : AiModels.Gemini.Contains(s.GeminiModel) ? s.GeminiModel : AiModels.Gemini[0];
                return new AiConfig(kind, geminiModel, geminiKey);
            case AiProviderKind.OpenAi:
                var openAiKey = secrets.Get("openai") is { Length: > 0 } o ? o : throw new LamhaException(ErrorCodes.OpenAiNoKey);
                return new AiConfig(kind, Pick(model, s.OpenAiModel) ?? throw new LamhaException(ErrorCodes.AiModel), openAiKey);
            case AiProviderKind.Custom:
                if (s.CustomUrl.Trim().Length == 0)
                {
                    throw new LamhaException(ErrorCodes.CustomNoServer);
                }

                var url = CustomServer.Normalize(s.CustomUrl) ?? throw new LamhaException(ErrorCodes.CustomInsecure);
                var customModel = Pick(model, s.CustomModel) ?? throw new LamhaException(ErrorCodes.CustomNoServer);
                return new AiConfig(kind, customModel, secrets.Get("custom") ?? "", url);
            default:
                var claudeKey = secrets.Get("claude") is { Length: > 0 } c ? c : throw new LamhaException(ErrorCodes.AiNoKey);
                var claudeModel = AiModels.Claude.Contains(model) ? model! : AiModels.Claude.Contains(s.ClaudeModel) ? s.ClaudeModel : AiModels.Claude[0];
                return new AiConfig(AiProviderKind.Claude, claudeModel, claudeKey);
        }
    }

    /// <summary>
    /// The translator's AI (Settings → Translation service), or null when that provider isn't set up (Google only).
    /// Each provider translates with its quickest model unless one is chosen; Ollama, OpenAI and custom servers with the
    /// writing tools' model. Lamha 2's <c>trSettings()</c>.
    /// </summary>
    public AiConfig? TranslatorConfig()
    {
        var s = settings.Current;
        var provider = s.TrProvider ?? s.AiProvider;
        var pick = s.TrModels.TryGetValue(AiModels.Id(provider), out var m) ? m : "";
        var model = provider switch
        {
            AiProviderKind.Gemini => AiModels.Gemini.Contains(pick) ? pick : AiModels.GeminiTranslation,
            AiProviderKind.Claude => AiModels.Claude.Contains(pick) ? pick : AiModels.ClaudeTranslation,
            _ => pick,
        };
        try
        {
            return Config(provider, model.Length > 0 ? model : null);
        }
        catch (LamhaException)
        {
            return null;
        }
    }

    public IAiProvider Provider(AiProviderKind kind) => kind switch
    {
        AiProviderKind.Gemini => _gemini,
        AiProviderKind.OpenAi or AiProviderKind.Custom => _openAi,
        AiProviderKind.Ollama => _ollama,
        _ => _claude,
    };

    /// <summary>One request to the AI in <paramref name="config"/>: the answer's text.</summary>
    public Task<string> CompleteAsync(AiConfig config, AiRequest request, CancellationToken cancel = default) =>
        Provider(config.Provider).CompleteAsync(config, request, cancel);

    /// <summary>One request whose answer is the JSON object its schema asked for.</summary>
    public async Task<JsonObject> CompleteJsonAsync(AiConfig config, AiRequest request, CancellationToken cancel = default) =>
        ParseJson(await CompleteAsync(config, request, cancel).ConfigureAwait(false));

    /// <summary>
    /// Checks a key, address or model with a tiny request before Settings saves it. Returns a note when it works with a
    /// caveat ("gemini_busy": Google accepted the key, its models are overloaded right now), else null; throws when it
    /// doesn't work.
    /// </summary>
    public async Task<string?> TestAsync(AiConfig config, CancellationToken cancel = default)
    {
        var request = new AiRequest("Reply with the word OK.", null, "You are a helpful assistant.")
        {
            MaxTokens = config.Provider switch
            {
                AiProviderKind.Gemini => 1024,
                AiProviderKind.Ollama => null,
                _ => 256,
            },
            Timeout = config.Provider == AiProviderKind.Ollama ? null : TimeSpan.FromSeconds(30),
        };
        try
        {
            await CompleteAsync(config, request, cancel).ConfigureAwait(false);
        }
        catch (LamhaException e) when (config.Provider == AiProviderKind.Gemini && e.Code == ErrorCodes.AiTooLong)
        {
            return null; // thinking used the small test budget: the key works
        }
        catch (LamhaException e) when (config.Provider == AiProviderKind.Gemini && e.Code == ErrorCodes.GeminiBusy)
        {
            return ErrorCodes.GeminiBusy;
        }

        return null;
    }

    /// <summary>An answer as JSON, without the ``` fences some models put around it (Lamha 2's <c>parseAiJSON</c>).</summary>
    public static JsonObject ParseJson(string reply)
    {
        try
        {
            return JsonNode.Parse(Fences().Replace(reply.Trim(), "")) as JsonObject ?? throw LamhaException.AiError("bad response");
        }
        catch (JsonException)
        {
            throw LamhaException.AiError("bad response");
        }
    }

    private static string? Pick(string? model, string saved) =>
        !string.IsNullOrWhiteSpace(model) ? model : saved.Trim().Length > 0 ? saved.Trim() : null;

    [GeneratedRegex(@"^```(?:json)?\s*|\s*```$")]
    private static partial Regex Fences();
}
