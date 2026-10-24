using Lamha.Core.Settings;

namespace Lamha.Core.Ai;

/// <summary>The models Lamha offers for each provider, and the defaults.</summary>
public static class AiModels
{
    /// <summary>
    /// Claude's models for the writing tools, the default first. Lamha 2 offered claude-opus-5, claude-sonnet-5 and
    /// claude-haiku-4-5; Lamha 3 offers their successors (a saved older one falls back to the default, as in Lamha 2).
    /// </summary>
    public static readonly IReadOnlyList<string> Claude = ["claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5"];

    /// <summary>Gemini's models, the default first; both have a free tier.</summary>
    public static readonly IReadOnlyList<string> Gemini = ["gemini-3.8-flash", "gemini-3.5-flash-lite"];

    /// <summary>Claude translates with its quickest model unless told otherwise.</summary>
    public const string ClaudeTranslation = "claude-haiku-5-5";

    /// <summary>Gemini translates with Flash-Lite: quick, and it has the roomier free quota.</summary>
    public const string GeminiTranslation = "gemini-3.5-flash-lite";

    /// <summary>Ollama's address when none is set.</summary>
    public const string OllamaDefaultUrl = "http://localhost:11434";

    public static string Name(AiProviderKind provider) => provider switch
    {
        AiProviderKind.Claude => "Claude",
        AiProviderKind.Gemini => "Gemini",
        AiProviderKind.OpenAi => "OpenAI",
        AiProviderKind.Custom => "Custom",
        AiProviderKind.Ollama => "Ollama",
        _ => provider.ToString(),
    };

    /// <summary>The provider's id in settings and secret names: "claude", "gemini", "openai", "custom", "ollama".</summary>
    public static string Id(AiProviderKind provider) => provider.ToString().ToLowerInvariant();
}
