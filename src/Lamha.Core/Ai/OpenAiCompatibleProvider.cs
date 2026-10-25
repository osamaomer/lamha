using System.Text.Json.Nodes;
using Lamha.Core.Net;
using Lamha.Core.Settings;

namespace Lamha.Core.Ai;

/// <summary>
/// OpenAI and every server that speaks its Chat Completions API: LM Studio, OpenRouter, Groq, Azure OpenAI… (decision
/// 0007). Only the address, key and model differ. "Compatible" servers differ in small ways, so the parsing is tolerant:
/// a server that refuses a JSON Schema answer is asked for plain JSON instead.
/// </summary>
public sealed class OpenAiCompatibleProvider(Http http) : IAiProvider
{
    public const string OpenAiUrl = "https://api.openai.com/v1";

    /// <summary>The API's address for <paramref name="config"/>: OpenAI's own, or the custom server's (checked).</summary>
    public static string BaseUrl(AiConfig config) => config.Provider == AiProviderKind.OpenAi
        ? OpenAiUrl
        : CustomServer.Normalize(config.BaseUrl) ?? throw new LamhaException(ErrorCodes.CustomInsecure);

    public async Task<string> CompleteAsync(AiConfig config, AiRequest request, CancellationToken cancel = default)
    {
        var body = new JsonObject
        {
            ["model"] = config.Model,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = request.System },
                new JsonObject { ["role"] = "user", ["content"] = request.Content }),
        };
        if (request.MaxTokens is { } max)
        {
            body["max_completion_tokens"] = max;
        }

        if (request.Schema is not null)
        {
            body["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = "answer", ["strict"] = true, ["schema"] = request.Schema.DeepClone() },
            };
        }

        JsonNode? data;
        try
        {
            data = await PostAsync(config, body, request.Timeout, cancel).ConfigureAwait(false);
        }
        catch (LamhaException e) when (request.Schema is not null && e.Code == ErrorCodes.AiError
            && (e.Detail.Contains("response_format", StringComparison.OrdinalIgnoreCase) || e.Detail.Contains("json_schema", StringComparison.OrdinalIgnoreCase)))
        {
            // A server without JSON Schema answers: plain JSON, asked for in words.
            body["response_format"] = new JsonObject { ["type"] = "json_object" };
            body["messages"]![1]!["content"] = request.Content + "\n\nAnswer with JSON only.";
            data = await PostAsync(config, body, request.Timeout, cancel).ConfigureAwait(false);
        }

        var choice = data?["choices"]?.AsArray().FirstOrDefault();
        var finish = choice?["finish_reason"]?.GetValue<string>();
        if (finish == "length")
        {
            throw new LamhaException(ErrorCodes.AiTooLong);
        }

        if (finish == "content_filter" || choice?["message"]?["refusal"] is JsonValue)
        {
            throw new LamhaException(ErrorCodes.AiRefused);
        }

        return choice?["message"]?["content"] is JsonValue content && content.TryGetValue<string>(out var text) ? text : "";
    }

    /// <summary>The models from <c>/models</c>: for OpenAI only its chat models, for a custom server all of them.</summary>
    public async Task<IReadOnlyList<AiModelInfo>> ListModelsAsync(AiConfig config, CancellationToken cancel = default)
    {
        var request = Http.Get(BaseUrl(config) + "/models");
        AddKey(request, config);
        var data = await SendAsync(config, request, TimeSpan.FromSeconds(15), cancel).ConfigureAwait(false);
        var ids = (data?["data"]?.AsArray() ?? [])
            .Select(m => m?["id"] is JsonValue v && v.TryGetValue<string>(out var id) ? id : null)
            .OfType<string>();
        if (config.Provider == AiProviderKind.OpenAi)
        {
            ids = ids.Where(IsOpenAiChatModel);
        }

        return [.. ids.Order(StringComparer.Ordinal).Select(id => new AiModelInfo(id))];
    }

    /// <summary>OpenAI's chat models, without its audio, image, embedding and other special ones.</summary>
    public static bool IsOpenAiChatModel(string id)
    {
        var chat = id.StartsWith("gpt-", StringComparison.Ordinal) || id.StartsWith("chatgpt-", StringComparison.Ordinal)
            || (id.Length > 1 && id[0] == 'o' && char.IsAsciiDigit(id[1]));
        string[] special = ["audio", "realtime", "transcribe", "tts", "image", "search", "embedding", "instruct", "moderation", "codex"];
        return chat && !special.Any(s => id.Contains(s, StringComparison.Ordinal));
    }

    private async Task<JsonNode?> PostAsync(AiConfig config, JsonObject body, TimeSpan? timeout, CancellationToken cancel)
    {
        var url = BaseUrl(config) + "/chat/completions";
        for (var attempt = 0; ; attempt++)
        {
            var request = Http.PostJson(url, body);
            AddKey(request, config);
            try
            {
                // Local models load into memory on their first request: as generous as Ollama.
                return await SendAsync(config, request, timeout ?? TimeSpan.FromSeconds(config.IsLocal ? 180 : 120), cancel).ConfigureAwait(false);
            }
            catch (LamhaException e) when (e.Code == ErrorCodes.AiBusy && attempt < 1)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1500), http.Time, cancel).ConfigureAwait(false);
            }
        }
    }

    private async Task<JsonNode?> SendAsync(AiConfig config, HttpRequestMessage request, TimeSpan timeout, CancellationToken cancel)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, timeout, cancel).ConfigureAwait(false);
        }
        catch (HttpFailure failure)
        {
            throw new LamhaException(failure.Kind == HttpFailureKind.Timeout ? ErrorCodes.AiTimeout
                : config.Provider == AiProviderKind.Custom ? ErrorCodes.CustomOffline : ErrorCodes.Network);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    return await Http.ReadJsonAsync(response, cancel).ConfigureAwait(false);
                }
                catch (HttpFailure)
                {
                    throw LamhaException.AiError("bad response");
                }
            }

            throw Error((int)response.StatusCode, await Http.ReadBodyAsync(response, cancel).ConfigureAwait(false));
        }
    }

    /// <summary>A failed answer as a code; the server's own words go in the detail.</summary>
    internal static LamhaException Error(int status, string body)
    {
        var message = Http.ErrorMessage(body);
        string? type = null;
        try
        {
            type = JsonNode.Parse(body)?["error"]?["code"]?.ToString();
        }
        catch (System.Text.Json.JsonException)
        {
            // Not JSON: the status decides.
        }

        var code = status switch
        {
            401 => ErrorCodes.AiBadKey,
            402 => ErrorCodes.AiNoCredit, // OpenRouter: no credit left
            429 when type == "insufficient_quota" => ErrorCodes.AiNoCredit,
            403 => ErrorCodes.AiForbidden,
            404 => ErrorCodes.AiModel,
            429 => ErrorCodes.AiRateLimited,
            >= 500 => ErrorCodes.AiBusy,
            _ => ErrorCodes.AiError,
        };
        return new LamhaException(code, message.Length > 0 ? message : "HTTP " + status);
    }

    private static void AddKey(HttpRequestMessage request, AiConfig config)
    {
        if (config.Key.Length > 0)
        {
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + config.Key);
        }
    }
}
