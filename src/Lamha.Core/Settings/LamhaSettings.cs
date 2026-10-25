using System.Text.Json.Serialization;

namespace Lamha.Core.Settings;

/// <summary>Where word meanings come from (Lamha 2's <c>dictSource</c>).</summary>
public enum DictSource
{
    /// <summary>The offline dictionary first, the internet for what it lacks ("local").</summary>
    Local,

    /// <summary>Never online for words: the dictionary, language packs and a local AI only ("offline").</summary>
    Offline,

    /// <summary>Google first, the dictionary when it can't answer in time ("online").</summary>
    Online,
}

/// <summary>Who translates (Lamha 2's <c>trService</c>).</summary>
public enum TranslateWith
{
    /// <summary>Google, then the AI when Google can't.</summary>
    Auto,

    /// <summary>Google only.</summary>
    Google,

    /// <summary>The AI, then Google when it fails. Words keep Google's dictionary first.</summary>
    Ai,
}

/// <summary>The AI providers (decision 0007).</summary>
public enum AiProviderKind
{
    Claude,
    Gemini,
    OpenAi,

    /// <summary>Any server that speaks OpenAI's Chat Completions: LM Studio, OpenRouter, Groq, Azure OpenAI…</summary>
    Custom,
    Ollama,
}

/// <summary>
/// Lamha's settings, with Lamha 2's defaults (background.js <c>DEFAULT_SETTINGS</c> and its per-device values in
/// storage.local). The Firefox-only ones (per-site switches, the pill, page translation) are gone. API keys are never
/// here: they're in <see cref="Abstractions.ISecretStore"/>.
/// </summary>
public sealed record LamhaSettings
{
    /// <summary>The translation language (<c>targetLang</c>).</summary>
    public string TargetLang { get; init; } = "ar";

    /// <summary>Selected Arabic text is translated into English (<c>reverseForArabic</c>).</summary>
    public bool ReverseForArabic { get; init; } = true;

    public bool ShowWikipedia { get; init; } = true;

    /// <summary>Google's English definitions are translated too (<c>translateDefinitions</c>).</summary>
    public bool TranslateDefinitions { get; init; } = true;

    public bool AutoSpeak { get; init; }

    /// <summary>"auto" | "light" | "dark".</summary>
    public string Theme { get; init; } = "auto";

    /// <summary>Animations: "auto" | "full" | "subtle" | "off".</summary>
    public string Motion { get; init; } = "auto";

    public DictSource DictSource { get; init; } = DictSource.Local;

    /// <summary>The sentence around a selected word is sent so the right meaning is chosen (<c>useContext</c>).</summary>
    public bool UseContext { get; init; } = true;

    public bool SaveHistory { get; init; } = true;

    /// <summary>English words get the English–English dictionary instead of a translation (<c>enDict</c>).</summary>
    public bool EnDict { get; init; }

    /// <summary>Other languages explained in their own language ("ar", "fr"…); English stays in <see cref="EnDict"/>.</summary>
    public IReadOnlyList<string> ExplainLangs { get; init; } = [];

    /// <summary>The mistake journal keeps what proofreading finds and personalizes explanations.</summary>
    public bool SaveMistakes { get; init; } = true;

    /// <summary>
    /// Every looked-up English word goes into review. Off by default (the user's call, 2026-10-02): they pick words
    /// with the bookmark.
    /// </summary>
    public bool CardsAuto { get; init; }

    /// <summary>New words introduced per day in review.</summary>
    public int CardsNewPerDay { get; init; } = 10;

    /// <summary>The day's goal: words looked up plus review answers; 0 = no goal.</summary>
    public int DailyGoal { get; init; } = 10;

    /// <summary>Interface language: "auto" (the system's, Arabic or English) | "ar" | "en".</summary>
    public string UiLang { get; init; } = "auto";

    /// <summary>The writing tools' AI (<c>aiProvider</c>).</summary>
    public AiProviderKind AiProvider { get; init; } = AiProviderKind.Claude;

    public string ClaudeModel { get; init; } = "";

    public string GeminiModel { get; init; } = "";

    public string OpenAiModel { get; init; } = "";

    /// <summary>A custom server's address, e.g. "http://localhost:1234/v1".</summary>
    public string CustomUrl { get; init; } = "";

    public string CustomModel { get; init; } = "";

    /// <summary>Ollama's address; "" is the default (http://localhost:11434).</summary>
    public string OllamaUrl { get; init; } = "";

    public string OllamaModel { get; init; } = "";

    public TranslateWith TrService { get; init; } = TranslateWith.Auto;

    /// <summary>The AI that translates; null = the writing tools' (<c>trProvider</c>).</summary>
    public AiProviderKind? TrProvider { get; init; }

    /// <summary>The model each provider translates with; missing or "" = its default (<c>trModels</c>).</summary>
    public IReadOnlyDictionary<string, string> TrModels { get; init; } = new Dictionary<string, string>();

    /// <summary>Clipboard history records what the user copies. Off unless turned on (<c>clipboardEnabled</c>).</summary>
    public bool ClipboardEnabled { get; init; }

    /// <summary>Unpinned copies kept: 100, 250, 500 or 1000 (<c>clipboardMaxItems</c>).</summary>
    public int ClipboardMaxItems { get; init; } = 500;

    /// <summary>Days an unpinned copy is kept after it was last copied or used: 1, 7, 30, 90, or 0 for ever.</summary>
    public int ClipboardExpiryDays { get; init; } = 30;

    /// <summary>Bank-card numbers aren't recorded (<c>clipboardSkipCards</c>).</summary>
    public bool ClipboardSkipCards { get; init; } = true;

    /// <summary>Programs whose copies are never recorded, by file name ("keepassxc.exe").</summary>
    public IReadOnlyList<string> ClipboardExcludedApps { get; init; } = Clipboard.ClipPrivacy.DefaultExcludedApps;

    /// <summary>Lamha's colours follow Windows' accent colour (<c>accentWindows</c>). Off unless turned on.</summary>
    public bool AccentWindows { get; init; }

    /// <summary>
    /// New in Lamha 3: the card and the clipboard panel on a solid background instead of Acrylic (Settings →
    /// Appearance → the card's background). Off: translucent.
    /// </summary>
    public bool CardSolid { get; init; }

    /// <summary>
    /// Mica behind the main window (Settings → Appearance; Lamha 2's <c>windowMica</c>). On by default, unlike Lamha 2's:
    /// phase 1's main window design is drawn on Mica.
    /// </summary>
    public bool WindowMica { get; init; } = true;

    /// <summary>
    /// New in Lamha 3 (Settings → Keyboard shortcuts; Lamha 2's were fixed): the quick lookup's keys ("Ctrl+Alt+L"),
    /// or "" for the app's own.
    /// </summary>
    public string LookupKeys { get; init; } = "";

    /// <summary>The writing tools' keys, or "" for the app's own.</summary>
    public string WriteKeys { get; init; } = "";

    /// <summary>
    /// New in Lamha 3: the currency a selected price is shown in (an ISO 4217 code, "SAR"); "" follows Windows' region
    /// (Settings → Languages).
    /// </summary>
    public string Currency { get; init; } = "";

    /// <summary>The interface language in use: "ar" or "en", given the system's language (e.g. "ar-SA").</summary>
    public string InterfaceLanguage(string systemLanguage) => UiLang switch
    {
        "ar" or "en" => UiLang,
        _ => systemLanguage.StartsWith("ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en",
    };
}

/// <summary>Settings as JSON, without reflection (NativeAOT).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(LamhaSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
