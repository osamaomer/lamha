using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using Lamha.Core.Net;

namespace Lamha.Core.Ai;

/// <summary>Google's Gemini API (free tier with a key from aistudio.google.com): Lamha 2's <c>geminiText</c>.</summary>
public sealed partial class GeminiProvider(Http http) : IAiProvider
{
    public static string Url(string model) =>
        $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent";

    /// <summary>
    /// The chosen model; if Google says it's overloaded after two more tries, once more with the other free model (Lamha
    /// 2's <c>geminiWithFallback</c>).
    /// </summary>
    public async Task<string> CompleteAsync(AiConfig config, AiRequest request, CancellationToken cancel = default)
    {
        try
        {
            return await CompleteWithAsync(config.Model, config, request, cancel).ConfigureAwait(false);
        }
        catch (LamhaException e) when (e.Code == ErrorCodes.GeminiBusy)
        {
            var other = AiModels.Gemini.FirstOrDefault(m => m != config.Model) ?? config.Model;
            return await CompleteWithAsync(other, config, request, cancel).ConfigureAwait(false);
        }
    }

    public Task<IReadOnlyList<AiModelInfo>> ListModelsAsync(AiConfig config, CancellationToken cancel = default) =>
        Task.FromResult<IReadOnlyList<AiModelInfo>>([.. AiModels.Gemini.Select(m => new AiModelInfo(m))]);

    private async Task<string> CompleteWithAsync(string model, AiConfig config, AiRequest request, CancellationToken cancel)
    {
        var generation = new JsonObject { ["maxOutputTokens"] = request.MaxTokens ?? 8192 };
        if (request.Schema is not null)
        {
            generation["responseMimeType"] = "application/json";
            generation["responseSchema"] = Schema(request.Schema);
        }

        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = request.System }) },
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = request.Content }),
            }),
            ["generationConfig"] = generation,
        };

        var data = await PostAsync(model, config.Key, body, request.Timeout ?? TimeSpan.FromSeconds(120), cancel).ConfigureAwait(false);
        if (data?["promptFeedback"]?["blockReason"] is not null)
        {
            throw new LamhaException(ErrorCodes.AiRefused);
        }

        var candidate = data?["candidates"]?.AsArray().FirstOrDefault() ?? throw new LamhaException(ErrorCodes.AiRefused);
        var finish = candidate["finishReason"]?.GetValue<string>() ?? "";
        if (finish == "MAX_TOKENS")
        {
            throw new LamhaException(ErrorCodes.AiTooLong);
        }

        if (Blocked().IsMatch(finish))
        {
            throw new LamhaException(ErrorCodes.AiRefused);
        }

        // Thinking parts are marked `thought`; the answer is the rest.
        return string.Concat((candidate["content"]?["parts"]?.AsArray() ?? [])
            .Where(p => p?["thought"]?.GetValue<bool>() != true && p?["text"] is JsonValue)
            .Select(p => p!["text"]!.GetValue<string>()));
    }

    /// <summary>Posts to the model; an overloaded model is tried twice more, 1.5 s and then 4 s later.</summary>
    private async Task<JsonNode?> PostAsync(string model, string key, JsonObject body, TimeSpan timeout, CancellationToken cancel)
    {
        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(Http.PostJson(Url(model), body, ("x-goog-api-key", key)), timeout, cancel).ConfigureAwait(false);
            }
            catch (HttpFailure failure)
            {
                throw new LamhaException(failure.Kind == HttpFailureKind.Timeout ? ErrorCodes.AiTimeout : ErrorCodes.Network);
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

                var error = Error((int)response.StatusCode, Http.ErrorMessage(await Http.ReadBodyAsync(response, cancel).ConfigureAwait(false)));
                if (error.Code == ErrorCodes.GeminiBusy && attempt < 2)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(attempt == 0 ? 1500 : 4000), http.Time, cancel).ConfigureAwait(false);
                    continue;
                }

                throw error;
            }
        }
    }

    /// <summary>A failed answer as a code, with Google's own words as the detail (Lamha 2's <c>geminiError</c>).</summary>
    internal static LamhaException Error(int status, string message)
    {
        var detail = message.Length > 0 ? message : "HTTP " + status;
        var code = status switch
        {
            400 or 401 or 403 when ApiKey().IsMatch(message) => ErrorCodes.GeminiBadKey,
            _ when Region().IsMatch(message) => ErrorCodes.GeminiRegion,
            403 => ErrorCodes.AiForbidden,
            404 => ErrorCodes.AiModel,
            429 => ErrorCodes.GeminiQuota, // the free tier's per-minute or daily limit
            >= 500 => ErrorCodes.GeminiBusy, // "The model is overloaded" (the key itself was accepted)
            _ => ErrorCodes.AiError,
        };
        return new LamhaException(code, detail);
    }

    /// <summary>
    /// A JSON Schema as Gemini's <c>responseSchema</c>: an OpenAPI subset with upper-case types and no
    /// <c>additionalProperties</c> (Lamha 2's <c>geminiSchema</c>).
    /// </summary>
    internal static JsonNode? Schema(JsonNode? schema)
    {
        switch (schema)
        {
            case JsonArray array:
                return new JsonArray([.. array.Select(Schema)]);
            case JsonObject obj:
                var result = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    if (key == "additionalProperties")
                    {
                        continue;
                    }

                    if (key == "type" && value is JsonValue v && v.TryGetValue<string>(out var type))
                    {
                        result["type"] = type.ToUpperInvariant();
                    }
                    else if (key == "properties" && value is JsonObject properties)
                    {
                        var converted = new JsonObject();
                        foreach (var (name, property) in properties)
                        {
                            converted[name] = Schema(property);
                        }

                        result["properties"] = converted;
                    }
                    else
                    {
                        result[key] = Schema(value);
                    }
                }

                return result;
            default:
                return schema?.DeepClone();
        }
    }

    [GeneratedRegex("SAFETY|PROHIBITED|BLOCKLIST|RECITATION|SPII")]
    private static partial Regex Blocked();

    [GeneratedRegex("api[ _]key", RegexOptions.IgnoreCase)]
    private static partial Regex ApiKey();

    [GeneratedRegex("location is not supported|not available in your country", RegexOptions.IgnoreCase)]
    private static partial Regex Region();
}
