using System.Text.Json.Nodes;
using Lamha.Core.Net;

namespace Lamha.Core.Ai;

/// <summary>
/// Claude through Anthropic's Messages API, over Lamha's own <see cref="Http"/> (decision 0010: the official SDK doesn't
/// publish with NativeAOT without warnings). Lamha 2's <c>callClaude</c> / <c>claudeText</c>, with the structured
/// answer in <c>output_config.format</c>.
/// </summary>
public sealed class ClaudeProvider(Http http) : IAiProvider
{
    public const string Url = "https://api.anthropic.com/v1/messages";

    /// <summary>
    /// When a safety classifier declines, the server answers with its recommended fallback model instead. Opus and
    /// Sonnet take it; Haiku has no server-side fallback.
    /// </summary>
    private const string FallbackBeta = "server-side-fallback-2026-07-01";

    public async Task<string> CompleteAsync(AiConfig config, AiRequest request, CancellationToken cancel = default)
    {
        var body = new JsonObject
        {
            ["model"] = config.Model,
            ["max_tokens"] = request.MaxTokens ?? 16000,
            ["system"] = request.System,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = request.Content }),
        };
        var output = new JsonObject { ["effort"] = request.Effort };
        if (request.Schema is not null)
        {
            output["format"] = new JsonObject { ["type"] = "json_schema", ["schema"] = request.Schema.DeepClone() };
        }

        body["output_config"] = output;
        var fallback = !config.Model.StartsWith("claude-haiku", StringComparison.Ordinal);
        if (fallback)
        {
            body["fallbacks"] = "default";
        }

        var data = await PostAsync(config, body, fallback, request.Timeout ?? TimeSpan.FromSeconds(120), cancel).ConfigureAwait(false);
        switch (data?["stop_reason"]?.GetValue<string>())
        {
            case "refusal":
                throw new LamhaException(ErrorCodes.AiRefused);
            case "max_tokens":
                throw new LamhaException(ErrorCodes.AiTooLong);
        }

        // Thinking blocks come first; the answer is the text block.
        foreach (var block in data?["content"]?.AsArray() ?? [])
        {
            if (block?["type"]?.GetValue<string>() == "text")
            {
                return block["text"]?.GetValue<string>() ?? "";
            }
        }

        return "";
    }

    public Task<IReadOnlyList<AiModelInfo>> ListModelsAsync(AiConfig config, CancellationToken cancel = default) =>
        Task.FromResult<IReadOnlyList<AiModelInfo>>([.. AiModels.Claude.Select(m => new AiModelInfo(m))]);

    /// <summary>Posts to the Messages API; a busy server gets one more try 1.5 s later.</summary>
    private async Task<JsonNode?> PostAsync(AiConfig config, JsonObject body, bool fallback, TimeSpan timeout, CancellationToken cancel)
    {
        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                var request = Http.PostJson(Url, body, ("x-api-key", config.Key), ("anthropic-version", "2023-06-01"));
                if (fallback)
                {
                    request.Headers.TryAddWithoutValidation("anthropic-beta", FallbackBeta);
                }

                response = await http.SendAsync(request, timeout, cancel).ConfigureAwait(false);
            }
            catch (HttpFailure failure)
            {
                throw new LamhaException(failure.Kind == HttpFailureKind.Timeout ? ErrorCodes.AiTimeout : ErrorCodes.Network);
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    return await ReadAsync(response, cancel).ConfigureAwait(false);
                }

                var error = Error((int)response.StatusCode, Http.ErrorMessage(await Http.ReadBodyAsync(response, cancel).ConfigureAwait(false)));
                if (error.Code == ErrorCodes.AiBusy && attempt < 1)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(1500), http.Time, cancel).ConfigureAwait(false);
                    continue;
                }

                throw error;
            }
        }
    }

    /// <summary>A failed answer as a code the interface can explain (Lamha 2's <c>claudeError</c>).</summary>
    internal static LamhaException Error(int status, string message) => status switch
    {
        401 => new LamhaException(ErrorCodes.AiBadKey),
        400 when message.Contains("credit", StringComparison.OrdinalIgnoreCase) => new LamhaException(ErrorCodes.AiNoCredit),
        403 => new LamhaException(ErrorCodes.AiForbidden),
        404 => new LamhaException(ErrorCodes.AiModel),
        429 => new LamhaException(ErrorCodes.AiRateLimited),
        >= 500 => new LamhaException(ErrorCodes.AiBusy),
        _ => LamhaException.AiError(message.Length > 0 ? message : "HTTP " + status),
    };

    private static async Task<JsonNode?> ReadAsync(HttpResponseMessage response, CancellationToken cancel)
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
}
