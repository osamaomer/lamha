using System.Text.Json.Nodes;
using Lamha.Core.Settings;

namespace Lamha.Core.Ai;

/// <summary>
/// The AI to call: the provider, its model, and its key or address. Lamha 2's <c>aiProviderConfig()</c> result. The key
/// stays in Core's memory for the request: it never reaches a page, the log or the database.
/// </summary>
public sealed record AiConfig(AiProviderKind Provider, string Model, string Key = "", string BaseUrl = "")
{
    /// <summary>Identifies the provider and model in cache keys (never the key itself).</summary>
    public string Id => $"{Provider}:{Model}".ToLowerInvariant();

    /// <summary>The provider's name, as the card shows who answered.</summary>
    public string Name => AiModels.Name(Provider);

    /// <summary>The AI runs on this PC or the local network: allowed when the dictionary is on "Local only".</summary>
    public bool IsLocal => Provider == AiProviderKind.Ollama || (Provider == AiProviderKind.Custom && CustomServer.IsLocal(BaseUrl));

    /// <summary>Never shows the key, wherever the config is printed.</summary>
    public override string ToString() => $"{Provider} {Model}";
}

/// <summary>One request to an AI.</summary>
/// <param name="Content">The user message: the task, then the material inside tags.</param>
/// <param name="Schema">The JSON Schema the answer must follow, or null for plain text.</param>
/// <param name="System">The system prompt.</param>
/// <param name="Effort">How hard to think, for providers that take it ("low", "medium").</param>
/// <param name="Chars">The material's length, to size a local model's context.</param>
public sealed record AiRequest(string Content, JsonObject? Schema, string System, string Effort = "low", int Chars = 0)
{
    /// <summary>The longest answer; a key check asks for a short one.</summary>
    public int? MaxTokens { get; init; }

    /// <summary>How long to wait; null is the provider's default.</summary>
    public TimeSpan? Timeout { get; init; }
}

/// <summary>An AI provider: Claude, Gemini, OpenAI and compatible servers, or Ollama (decision 0007).</summary>
public interface IAiProvider
{
    /// <summary>The answer's text (the JSON the schema asked for). Failures are <see cref="LamhaException"/> codes.</summary>
    Task<string> CompleteAsync(AiConfig config, AiRequest request, CancellationToken cancel = default);

    /// <summary>The models the account or server offers, for Settings.</summary>
    Task<IReadOnlyList<AiModelInfo>> ListModelsAsync(AiConfig config, CancellationToken cancel = default);
}

/// <summary>A model a provider offers; <paramref name="Size"/> in bytes for local models, else 0.</summary>
public sealed record AiModelInfo(string Name, long Size = 0);
