using System.Globalization;
using System.Text.Json.Nodes;
using Lamha.Core.Abstractions;
using Lamha.Core.Clipboard;
using Lamha.Core.Review;
using Lamha.Core.Settings;
using Lamha.Core.Writing;

namespace Lamha.Core.Import;

/// <summary>
/// Moves Lamha 2's data into Lamha 3 (first run's Lamha 2 step): its settings and API keys, the deck with its removals
/// and counters, the history, the mistake journal, the days' practice, the draft, and clipboard history. The deck and
/// what goes with it are merged as a saved copy is (<see cref="BackupService.RestoreAsync"/>), so nothing here is lost.
/// Lamha 2's folder isn't changed.
/// </summary>
public sealed class Lamha2Import(
    SettingsService settings,
    ISecretStore secrets,
    IStateStore state,
    BackupService backup,
    ReviewService review,
    ClipboardHistory clipboard)
{
    /// <summary>Lamha 2's API keys in storage.local (desktop/app-rules.js <c>SECRET_KEYS</c>), by Lamha 3's names.</summary>
    private static readonly (string From, string Name)[] Keys = [("aiKey", "claude"), ("geminiKey", "gemini")];

    /// <summary>Settings only the Firefox extension had (storage.sync).</summary>
    private static readonly string[] FirefoxSettings = ["triggerMode", "showInInputs", "aiInInputs", "writeOnDblClick", "disabledSites"];

    /// <summary>The link between the extension and the app, and page translation (storage.local).</summary>
    private static readonly string[] FirefoxLocal = ["firefoxLink", "appLink", "trPages", "deckSyncedAt"];

    /// <summary>What the Lamha 2 step lists: each count as Lamha 3 will have it.</summary>
    public static Lamha2Counts Count(Lamha2Data data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var local = data.Local;
        var cards = (local["cards"] as JsonObject ?? []).Count(c => BackupService.Clean(c.Value as JsonObject) is not null);
        var history = (local["history"] as JsonArray ?? []).Count(h => h is JsonObject o && Text(o["q"], 100) is not null);
        var mistakes = local["mistakes"] is JsonObject m
            ? (m["counts"] as JsonObject ?? []).Where(c => MistakeCategories.IsKnown(c.Key)).Sum(c => Whole(c.Value) ?? 0)
            : 0;
        return new Lamha2Counts(cards, history, (int)Math.Min(mistakes, int.MaxValue), Clips(data.Clips).Count);
    }

    /// <summary>
    /// Moves it all. The interface language isn't moved: first run asked for it just before. A key already saved in
    /// Lamha 3 is kept.
    /// </summary>
    public async Task<Lamha2Summary> MoveAsync(Lamha2Data data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var (sync, local) = (data.Sync, data.Local);
        var counts = Count(data);

        settings.Update(s => Moved(s, sync, local));

        var keys = new List<string>();
        foreach (var (from, name) in Keys)
        {
            if (Text(local[from], 1000) is { Length: > 0 } key && string.IsNullOrEmpty(secrets.Get(name)))
            {
                secrets.Set(name, key);
                keys.Add(name);
            }
        }

        // Write's draft and the clipboard summary's language: kept by name, as Lamha 2 kept them.
        if (Text(local["draft"], int.MaxValue) is { Length: > 0 } draft && state.Get("draft") is null)
        {
            state.Set("draft", draft);
        }

        if (OneOf(local["clipboardSummaryLang"], "ar", "en") is { } summaryLang)
        {
            state.Set("clipboardSummaryLang", summaryLang);
        }

        await backup.RestoreAsync(local).ConfigureAwait(false);
        await review.MoveFromLamha2Async(Stats(local["cardStats"] as JsonObject), Bool(local["cardsImported"]) == true).ConfigureAwait(false);
        clipboard.Import(Clips(data.Clips));

        var dropped = new List<Lamha2Dropped>();
        if (FirefoxSettings.Any(sync.ContainsKey) || FirefoxLocal.Any(local.ContainsKey))
        {
            dropped.Add(Lamha2Dropped.Firefox);
        }

        if (data.WikipediaFiles > 0)
        {
            dropped.Add(Lamha2Dropped.OfflineWikipedia);
        }

        return new Lamha2Summary(counts, keys, data.KeysLost, data.ClipsLost, dropped);
    }

    /// <summary>
    /// Lamha 2's settings over Lamha 3's, each only when it's there and of its kind. Not moved: <c>enabled</c> (its
    /// window's switch did nothing in the Windows app; in Lamha 3 it turns the shortcuts off: the user's call,
    /// 2026-10-26), <c>uiLang</c> (first run's own step), and <c>windowMica</c> (off by default in Lamha 2; Lamha 3's
    /// main window is designed on Mica).
    /// </summary>
    internal static LamhaSettings Moved(LamhaSettings s, JsonObject sync, JsonObject local) => s with
    {
        TargetLang = Code(sync["targetLang"]) ?? s.TargetLang,
        ReverseForArabic = Bool(sync["reverseForArabic"]) ?? s.ReverseForArabic,
        ShowWikipedia = Bool(sync["showWikipedia"]) ?? s.ShowWikipedia,
        TranslateDefinitions = Bool(sync["translateDefinitions"]) ?? s.TranslateDefinitions,
        AutoSpeak = Bool(sync["autoSpeak"]) ?? s.AutoSpeak,
        Theme = OneOf(sync["theme"], "auto", "light", "dark") ?? s.Theme,
        Motion = OneOf(sync["motion"], "auto", "full", "subtle", "off") ?? s.Motion,
        DictSource = OneOf(sync["dictSource"], "local", "offline", "online") switch
        {
            "local" => DictSource.Local,
            "offline" => DictSource.Offline,
            "online" => DictSource.Online,
            _ => s.DictSource,
        },
        UseContext = Bool(sync["useContext"]) ?? s.UseContext,
        SaveHistory = Bool(sync["saveHistory"]) ?? s.SaveHistory,
        EnDict = Bool(sync["enDict"]) ?? s.EnDict,
        ExplainLangs = sync["explainLangs"] is JsonArray explain
            ? [.. explain.Select(Code).OfType<string>().Where(c => c != "en").Distinct().Take(20)]
            : s.ExplainLangs,
        SaveMistakes = Bool(sync["saveMistakes"]) ?? s.SaveMistakes,
        CardsAuto = Bool(sync["cardsAuto"]) ?? s.CardsAuto,
        CardsNewPerDay = OneOf(sync["cardsNewPerDay"], 5, 10, 20, 30) ?? s.CardsNewPerDay,
        DailyGoal = OneOf(sync["dailyGoal"], 0, 5, 10, 20, 30) ?? s.DailyGoal,
        ClaudeModel = Text(sync["aiModel"], 100) ?? s.ClaudeModel,
        AiProvider = Provider(local["aiProvider"]) ?? s.AiProvider,
        GeminiModel = Text(local["geminiModel"], 100) ?? s.GeminiModel,
        OllamaUrl = Text(local["ollamaUrl"], 300) ?? s.OllamaUrl,
        OllamaModel = Text(local["ollamaModel"], 200) ?? s.OllamaModel,
        TrService = OneOf(local["trService"], "auto", "google", "ai") switch
        {
            "auto" => TranslateWith.Auto,
            "google" => TranslateWith.Google,
            "ai" => TranslateWith.Ai,
            _ => s.TrService,
        },
        TrProvider = Provider(local["trProvider"]) ?? s.TrProvider,
        TrModels = local["trModels"] is JsonObject models
            ? models.Where(m => Provider(JsonValue.Create(m.Key)) is not null && Text(m.Value, 200) is not null)
                .ToDictionary(m => m.Key, m => Text(m.Value, 200)!)
            : s.TrModels,
        ClipboardEnabled = Bool(local["clipboardEnabled"]) ?? s.ClipboardEnabled,
        ClipboardMaxItems = OneOf(local["clipboardMaxItems"], 100, 250, 500, 1000) ?? s.ClipboardMaxItems,
        ClipboardExpiryDays = OneOf(local["clipboardExpiryDays"], 0, 1, 7, 30, 90) ?? s.ClipboardExpiryDays,
        ClipboardSkipCards = Bool(local["clipboardSkipCards"]) ?? s.ClipboardSkipCards,
        ClipboardExcludedApps = local["clipboardExcludedApps"] is JsonArray apps
            ? [.. apps.Select(a => ClipPrivacy.ExeName(Text(a, 260))).Where(a => a.Length > 0).Distinct().Take(100)]
            : s.ClipboardExcludedApps,
        AccentWindows = Bool(local["accentWindows"]) ?? s.AccentWindows,
        UpdatesAuto = Bool(local["updatesAuto"]) ?? s.UpdatesAuto,
    };

    /// <summary>Lamha 2's <c>cardStats</c>: days as JavaScript's <c>toDateString()</c>.</summary>
    private static DeckStats Stats(JsonObject? stats) => stats is null ? new() : new()
    {
        Day = Day(stats["day"]),
        NewSeen = (int)Math.Min(Whole(stats["newSeen"]) ?? 0, int.MaxValue),
        ReviewDay = Day(stats["reviewDay"]),
        Streak = (int)Math.Min(Whole(stats["streak"]) ?? 0, int.MaxValue),
    };

    /// <summary>
    /// Lamha 2's copies (desktop/clipboard-store.js <c>upgrade()</c>) with what Lamha 3 keeps of their cache: the
    /// translations, "Write it in English" and the summaries. Proofreading is done again when asked.
    /// </summary>
    private static List<Clip> Clips(JsonArray clips)
    {
        var list = new List<Clip>();
        foreach (var c in clips.OfType<JsonObject>())
        {
            if (Text(c["id"], 100) is not { } id || Text(c["text"], int.MaxValue) is not { } text || text.Trim().Length == 0)
            {
                continue;
            }

            var created = Whole(c["createdAt"]) ?? 0;
            var cache = c["cache"] as JsonObject ?? [];
            list.Add(new Clip
            {
                Id = id,
                Text = text,
                Html = Text(c["html"], int.MaxValue),
                Label = Text(c["label"], ClipboardHistory.LabelMax) ?? "",
                SourceApp = ClipPrivacy.ExeName(Text(c["sourceApp"], 260)) is { Length: > 0 } app ? app : "unknown",
                Lang = "other",
                CreatedAt = created,
                LastCopiedAt = Whole(c["lastCopiedAt"]) ?? created,
                LastUsedAt = Whole(c["lastUsedAt"]) ?? 0,
                CopyCount = (int)Math.Clamp(Whole(c["copyCount"]) ?? 1, 1, int.MaxValue),
                UseCount = (int)Math.Min(Whole(c["useCount"]) ?? 0, int.MaxValue),
                Pinned = Bool(c["pinned"]) == true,
                Cache = new ClipCache
                {
                    Translation = Strings(cache["translation"] as JsonObject, s => Text(s, int.MaxValue)),
                    English = Text(cache["english"], int.MaxValue),
                    Summary = Strings(cache["summary"] as JsonObject, s => Text((s as JsonObject)?["text"], int.MaxValue)),
                },
            });
        }

        return list;
    }

    private static Dictionary<string, string> Strings(JsonObject? o, Func<JsonNode?, string?> value) =>
        (o ?? []).Where(p => Code(JsonValue.Create(p.Key)) is not null && value(p.Value) is { Length: > 0 })
            .ToDictionary(p => p.Key, p => value(p.Value)!);

    private static bool? Bool(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    /// <summary>A string up to <paramref name="max"/> characters, or null.</summary>
    private static string? Text(JsonNode? n, int max) => n is JsonValue v && v.TryGetValue<string>(out var s) && s.Length <= max ? s : null;

    /// <summary>A whole number of zero or more, or null.</summary>
    private static long? Whole(JsonNode? n) =>
        n is JsonValue v && v.TryGetValue<double>(out var d) && double.IsFinite(d) && d >= 0 ? (long)Math.Min(d, long.MaxValue / 2) : null;

    /// <summary>A language code as Lamha 2 kept them: "ar", "fr", "zh-CN".</summary>
    private static string? Code(JsonNode? n) =>
        Text(n, 10) is { } s && s.Length >= 2 && s.All(c => char.IsAsciiLetter(c) || c == '-') ? s : null;

    private static string? OneOf(JsonNode? n, params string[] allowed) => Text(n, 20) is { } s && allowed.Contains(s) ? s : null;

    private static int? OneOf(JsonNode? n, params int[] allowed) =>
        Whole(n) is { } w && w <= int.MaxValue && allowed.Contains((int)w) ? (int)w : null;

    private static AiProviderKind? Provider(JsonNode? n) => OneOf(n, "claude", "gemini", "ollama") switch
    {
        "claude" => AiProviderKind.Claude,
        "gemini" => AiProviderKind.Gemini,
        "ollama" => AiProviderKind.Ollama,
        _ => null,
    };

    private static DateOnly? Day(JsonNode? n) =>
        Text(n, 40) is { } s && DateOnly.TryParseExact(s, BackupService.DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
