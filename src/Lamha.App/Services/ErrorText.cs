using Lamha.Core;
using Lamha.Core.Settings;

namespace Lamha.App.Services;

/// <summary>What the card does about an error: try again, open Settings, or nothing (the same text would fail again).</summary>
public enum ErrorAction
{
    None,
    Retry,
    Settings,
}

/// <summary>An error in words: what happened, what to do, and the one button that helps.</summary>
public sealed record ErrorInfo(string Title, string Hint, ErrorAction Action, string SettingsSection = "")
{
    /// <summary>Without a Settings to open, an error that Settings would fix has no button.</summary>
    public ErrorInfo Offering(bool settings) => Action == ErrorAction.Settings && !settings ? this with { Action = ErrorAction.None } : this;
}

/// <summary>
/// Error codes in the interface language: Lamha 2's <c>LamhaAI.errorInfo()</c> (shared/lamha-ai.js) for the AI's, and the
/// card's <c>renderError()</c> (content.js) for a lookup's. Lamha 3 names the provider in use where Lamha 2 said
/// "Claude" (approved at the start of phase 4), and adds OpenAI's and custom servers' codes.
/// </summary>
public static class ErrorText
{
    /// <summary>The AI's codes that Settings fixes (Lamha 2's third column), and Lamha 3's new ones.</summary>
    private static readonly HashSet<string> FixedInSettings =
    [
        ErrorCodes.AiNoKey, ErrorCodes.AiBadKey, ErrorCodes.AiNoCredit, ErrorCodes.AiForbidden, ErrorCodes.AiModel,
        ErrorCodes.OllamaNoModel, ErrorCodes.OllamaOffline, ErrorCodes.OllamaOrigin, ErrorCodes.OllamaModel,
        ErrorCodes.GeminiNoKey, ErrorCodes.GeminiBadKey, ErrorCodes.GeminiRegion,
        ErrorCodes.OpenAiNoKey, ErrorCodes.CustomNoServer, ErrorCodes.CustomInsecure, ErrorCodes.CustomOffline,
    ];

    /// <summary>The AI's codes that have words of their own; the rest are "Something went wrong".</summary>
    private static readonly HashSet<string> AiCodes =
    [
        .. FixedInSettings, ErrorCodes.AiRateLimited, ErrorCodes.AiBusy, ErrorCodes.AiTimeout, ErrorCodes.AiTooLong,
        ErrorCodes.AiRefused, ErrorCodes.GeminiQuota, ErrorCodes.GeminiBusy,
    ];

    /// <summary>A writing tool's (or the AI translator's) error: Lamha 2's <c>errorInfo(code, providerName)</c>.</summary>
    public static ErrorInfo ForAi(LamhaException error, AiProviderKind provider, LamhaSettings settings)
    {
        ArgumentNullException.ThrowIfNull(error);
        var code = error.Code;
        var name = AiNames.Of(provider, settings);
        if (code == ErrorCodes.AiError)
        {
            // The provider's own words, under "Couldn't reach Claude".
            return new ErrorInfo(Localization.Format("Error_ai_error_Title", name), error.Detail, ErrorAction.Retry);
        }

        if (!AiCodes.Contains(code))
        {
            return code == ErrorCodes.Network ? Network() : Other();
        }

        var title = code is ErrorCodes.AiBadKey or ErrorCodes.AiNoCredit
            ? Localization.Format($"Error_{code}_Title", name)
            : Localization.Get($"Error_{code}_Title");
        var hint = code switch
        {
            ErrorCodes.AiNoCredit => Localization.Format("Error_ai_no_credit_Hint", CreditSite(provider)),
            ErrorCodes.AiForbidden => Localization.Format("Error_ai_forbidden_Hint", Service(provider, settings)),
            _ => Localization.Get($"Error_{code}_Hint"),
        };
        return FixedInSettings.Contains(code)
            ? new ErrorInfo(title, hint, ErrorAction.Settings, "writing")
            : new ErrorInfo(title, hint, code == ErrorCodes.AiTooLong ? ErrorAction.None : ErrorAction.Retry);
    }

    /// <summary>
    /// A lookup's error on the card: the AI translator's own (its key, its quota…), or why nothing could be translated.
    /// Lamha 2's <c>renderError()</c>; <paramref name="online"/> is Windows' network state.
    /// </summary>
    public static ErrorInfo ForLookup(LamhaException error, AiProviderKind translator, LamhaSettings settings, bool online)
    {
        ArgumentNullException.ThrowIfNull(error);
        var code = error.Code;
        if (code is ErrorCodes.TooLong or ErrorCodes.AiTooLong)
        {
            return new ErrorInfo(Localization.Get("Error_ai_too_long_Title"), Localization.Get("Error_ai_too_long_Hint"), ErrorAction.None);
        }

        if (AiCodes.Contains(code) || code == ErrorCodes.AiError)
        {
            var ai = ForAi(error, translator, settings);
            return ai.Action == ErrorAction.Settings ? ai with { SettingsSection = "translation" } : ai;
        }

        return code switch
        {
            // "Offline only": trying again gives the same answer; the way out is the setting.
            ErrorCodes.NotFoundOffline => new ErrorInfo(Localization.Get("Card_ErrNotFound"), Localization.Get("Card_ErrLocalOnly"), ErrorAction.Settings, "offline"),
            ErrorCodes.OfflineMode => new ErrorInfo(Localization.Get("Card_ErrOfflineMode"), Localization.Get("Card_ErrLocalOnly"), ErrorAction.Settings, "offline"),
            _ when !online => Network(),
            ErrorCodes.RateLimited => new ErrorInfo(Localization.Get("Card_ErrBusy"), Localization.Get("Card_ErrTooMany"), ErrorAction.Retry),
            _ => new ErrorInfo(Localization.Get("Card_ErrFailed"), Localization.Get("Card_ErrService"), ErrorAction.Retry),
        };
    }

    /// <summary>What Lamha didn't expect (a fault of its own): Lamha 2's general error, with Try again.</summary>
    public static ErrorInfo Unexpected => Other();

    private static ErrorInfo Network() =>
        new(Localization.Get("Card_ErrOffline"), Localization.Get("Card_ErrCheckConn"), ErrorAction.Retry);

    private static ErrorInfo Other() =>
        new(Localization.Get("Error_other_Title"), Localization.Get("Error_other_Hint"), ErrorAction.Retry);

    /// <summary>Where to add credit: the provider's console, or "the service's website" for another server.</summary>
    private static string CreditSite(AiProviderKind provider) => provider switch
    {
        AiProviderKind.Claude => "console.anthropic.com",
        AiProviderKind.OpenAi => "platform.openai.com",
        AiProviderKind.Gemini => "aistudio.google.com",
        _ => Localization.Get("Error_ServiceSite"),
    };

    /// <summary>Whose website has the key's permissions (Lamha 2: "Anthropic or Google AI Studio").</summary>
    private static string Service(AiProviderKind provider, LamhaSettings settings) => provider switch
    {
        AiProviderKind.Claude => "Anthropic",
        AiProviderKind.Gemini => "Google AI Studio",
        _ => AiNames.Of(provider, settings),
    };
}
