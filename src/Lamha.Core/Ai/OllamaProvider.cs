using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using Lamha.Core.Net;

namespace Lamha.Core.Ai;

/// <summary>Ollama: free models running on this PC (Lamha 2's <c>ollamaText</c>).</summary>
public sealed partial class OllamaProvider(Http http) : IAiProvider
{
    /// <summary>The address tidied, or the default when it isn't a plain http(s)://host[:port] (Lamha 2's <c>ollamaBase</c>).</summary>
    public static string BaseUrl(string? url)
    {
        var u = (url ?? "").Trim().TrimEnd('/');
        return Address().IsMatch(u) ? u : AiModels.OllamaDefaultUrl;
    }

    public async Task<string> CompleteAsync(AiConfig config, AiRequest request, CancellationToken cancel = default)
    {
        var body = new JsonObject
        {
            ["model"] = config.Model,
            ["stream"] = false,
            ["think"] = false, // reasoning models are much slower and these tasks don't need it
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = request.System },
                new JsonObject { ["role"] = "user", ["content"] = request.Schema is null ? request.Content : request.Content + "\n\nAnswer with JSON only." }),
            // Ollama's default context is 4096 tokens: raised for long texts, capped to stay on a 6 GB graphics card.
            ["options"] = new JsonObject
            {
                ["temperature"] = 0.2,
                ["num_ctx"] = Math.Min(16384, Math.Max(4096, (int)Math.Ceiling(request.Chars / 3.0) + 2048)),
            },
        };
        if (request.Schema is not null)
        {
            body["format"] = request.Schema.DeepClone();
        }

        JsonNode? data;
        try
        {
            data = await PostAsync(config, "/api/chat", body, request.Timeout, cancel).ConfigureAwait(false);
        }
        catch (LamhaException e) when (e.Code == ErrorCodes.AiError && Think().IsMatch(e.Detail))
        {
            body.Remove("think"); // a model without a thinking switch
            data = await PostAsync(config, "/api/chat", body, request.Timeout, cancel).ConfigureAwait(false);
        }

        if (data?["done_reason"]?.GetValue<string>() == "length")
        {
            throw new LamhaException(ErrorCodes.AiTooLong);
        }

        return data?["message"]?["content"]?.GetValue<string>() ?? "";
    }

    public async Task<IReadOnlyList<AiModelInfo>> ListModelsAsync(AiConfig config, CancellationToken cancel = default)
    {
        var data = await SendAsync(config, Http.Get(BaseUrl(config.BaseUrl) + "/api/tags"), TimeSpan.FromSeconds(8), cancel).ConfigureAwait(false);
        return [.. (data?["models"]?.AsArray() ?? [])
            .Where(m => m?["name"] is JsonValue)
            .Select(m => new AiModelInfo(m!["name"]!.GetValue<string>(), m["size"]?.GetValue<long>() ?? 0))];
    }

    private Task<JsonNode?> PostAsync(AiConfig config, string path, JsonObject body, TimeSpan? timeout, CancellationToken cancel) =>
        // Generous: the first request loads the model into memory.
        SendAsync(config, Http.PostJson(BaseUrl(config.BaseUrl) + path, body), timeout ?? TimeSpan.FromSeconds(180), cancel);

    private async Task<JsonNode?> SendAsync(AiConfig config, HttpRequestMessage request, TimeSpan timeout, CancellationToken cancel)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, timeout, cancel).ConfigureAwait(false);
        }
        catch (HttpFailure failure)
        {
            throw new LamhaException(failure.Kind == HttpFailureKind.Timeout ? ErrorCodes.AiTimeout : ErrorCodes.OllamaOffline);
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

            var status = (int)response.StatusCode;
            var message = Http.ErrorMessage(await Http.ReadBodyAsync(response, cancel).ConfigureAwait(false));
            throw status switch
            {
                403 => new LamhaException(ErrorCodes.OllamaOrigin), // OLLAMA_ORIGINS doesn't allow the caller
                404 when message.Contains("model", StringComparison.OrdinalIgnoreCase) => new LamhaException(ErrorCodes.OllamaModel),
                _ => LamhaException.AiError(message.Length > 0 ? message : "HTTP " + status),
            };
        }
    }

    [GeneratedRegex(@"^https?://[^/\s]+$", RegexOptions.IgnoreCase)]
    private static partial Regex Address();

    [GeneratedRegex("think", RegexOptions.IgnoreCase)]
    private static partial Regex Think();
}
