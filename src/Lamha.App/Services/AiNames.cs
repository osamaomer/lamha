using Lamha.Core.Ai;
using Lamha.Core.Settings;

namespace Lamha.App.Services;

/// <summary>An AI provider's name as the interface shows it ("Writing tools · Gemini").</summary>
public static class AiNames
{
    /// <summary>Its own name; another server by its ready-made name (LM Studio, OpenRouter, Groq) or "Another server".</summary>
    public static string Of(AiProviderKind provider, LamhaSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (provider != AiProviderKind.Custom)
        {
            return AiModels.Name(provider);
        }

        var url = CustomServer.Normalize(settings.CustomUrl);
        return CustomServer.Presets.FirstOrDefault(p => string.Equals(p.BaseUrl, url, StringComparison.OrdinalIgnoreCase))?.Name
            ?? Localization.Get("Ai_Custom");
    }
}
