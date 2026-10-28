namespace Lamha.Core;

/// <summary>
/// A failure the user is told about, by code: "network", "ai_bad_key", "not_found_offline"… The interface turns the code
/// into words; the log keeps only the code. The codes are Lamha 2's (shared/lamha-ai.js <c>ERRORS</c>), so its strings
/// carry over.
/// </summary>
public sealed class LamhaException : Exception
{
    public LamhaException(string code, string? detail = null)
        : base(code)
    {
        Code = code;
        Detail = detail ?? "";
    }

    public LamhaException()
        : this("error")
    {
    }

    public LamhaException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = message;
        Detail = "";
    }

    /// <summary>The code: "ai_error" for a provider's own message, which is then in <see cref="Detail"/>.</summary>
    public string Code { get; }

    /// <summary>
    /// The provider's own words (Gemini's "API key not valid…"), shown to the user under the code's explanation. Never
    /// logged: it may quote the user's text.
    /// </summary>
    public string Detail { get; }

    /// <summary>A provider's own error message: Lamha 2's <c>"ai_error:" + msg</c>.</summary>
    public static LamhaException AiError(string detail) => new(ErrorCodes.AiError, detail);
}

/// <summary>Lamha's error codes.</summary>
public static class ErrorCodes
{
    public const string Empty = "empty";
    public const string TooLong = "too_long";
    public const string Network = "network";
    public const string RateLimited = "rate_limited";
    public const string OfflineMode = "offline_mode";
    public const string NotFoundOffline = "not_found_offline";
    public const string NotBackup = "not_backup";

    /// <summary>Language packs (Lamha 2's packs.js): not one Lamha offers, already downloading, couldn't be downloaded, arrived damaged.</summary>
    public const string PackUnknown = "pack_unknown";
    public const string PackBusy = "pack_busy";
    public const string PackDownload = "pack_download";
    public const string PackBad = "pack_bad";

    /// <summary>
    /// New in Lamha 3, the reader: a book that isn't what its name says or is damaged; a book locked by DRM to another app;
    /// a file the reader doesn't open (yet).
    /// </summary>
    public const string BookBad = "book_bad";
    public const string BookLocked = "book_locked";
    public const string BookUnsupported = "book_unsupported";

    /// <summary>Google answered with an error or something that isn't its JSON (shown as "something went wrong").</summary>
    public const string GoogleError = "google_error";

    public const string AiError = "ai_error";
    public const string AiNoKey = "ai_no_key";
    public const string AiBadKey = "ai_bad_key";
    public const string AiNoCredit = "ai_no_credit";
    public const string AiForbidden = "ai_forbidden";
    public const string AiModel = "ai_model";
    public const string AiRateLimited = "ai_rate_limited";
    public const string AiBusy = "ai_busy";
    public const string AiTimeout = "ai_timeout";
    public const string AiTooLong = "ai_too_long";
    public const string AiRefused = "ai_refused";

    public const string OllamaNoModel = "ollama_no_model";
    public const string OllamaOffline = "ollama_offline";
    public const string OllamaOrigin = "ollama_origin";
    public const string OllamaModel = "ollama_model";

    public const string GeminiNoKey = "gemini_no_key";
    public const string GeminiBadKey = "gemini_bad_key";
    public const string GeminiQuota = "gemini_quota";
    public const string GeminiBusy = "gemini_busy";
    public const string GeminiRegion = "gemini_region";

    /// <summary>New in Lamha 3: OpenAI chosen without a key.</summary>
    public const string OpenAiNoKey = "openai_no_key";

    /// <summary>New in Lamha 3: a custom server chosen without an address or a model.</summary>
    public const string CustomNoServer = "custom_no_server";

    /// <summary>New in Lamha 3: a custom server's address is plain http:// on the internet (decision 0007).</summary>
    public const string CustomInsecure = "custom_insecure";

    /// <summary>New in Lamha 3: the custom server can't be reached.</summary>
    public const string CustomOffline = "custom_offline";
}
