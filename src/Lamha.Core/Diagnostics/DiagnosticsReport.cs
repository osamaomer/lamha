using System.Globalization;
using Lamha.Core.Abstractions;
using Lamha.Core.Ai;
using Lamha.Core.Dictionary;
using Lamha.Core.Settings;

namespace Lamha.Core.Diagnostics;

/// <summary>About this PC and the app, from the app (Windows' version, memory…): lines for the report, without user data.</summary>
public interface IHostInfo
{
    /// <summary>"Windows app (Windows 11 26200, x64)".</summary>
    string Host { get; }

    /// <summary>Extra lines: memory, the clipboard history switch….</summary>
    IReadOnlyList<string> Lines { get; }
}

/// <summary>
/// Settings → Report a problem: what the user sends, after reading it, as plain text. Settings and counts, never words,
/// texts, keys or addresses (Lamha 2's <c>diagnostics()</c>, without its Firefox lines).
/// </summary>
public sealed class DiagnosticsReport(
    SettingsService settings,
    Localization localization,
    ISecretStore secrets,
    IDeckStore deck,
    IHistoryStore history,
    IMistakeStore mistakes,
    ILanguagePacks packs,
    ErrorLog errors,
    IHostInfo host,
    string appVersion)
{
    public string Build()
    {
        var s = settings.Current;
        static string On(bool v) => v ? "on" : "off";
        static string When(long t) => DateTimeOffset.FromUnixTimeMilliseconds(t).UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        string Saved(string name) => string.IsNullOrEmpty(secrets.Get(name)) ? "none" : "saved";
        var custom = s.CustomUrl.Trim().Length == 0 ? "none" : CustomServer.IsLocal(s.CustomUrl) ? "on the local network" : "on the internet";
        var errorLines = errors.Recent().Select(e => $"  {When(e.Time)}  {e.Where}: {e.Code}").ToList();
        string[] lines =
        [
            $"Lamha {appVersion} · {host.Host} · interface {localization.Language} ({s.UiLang})",
            $"Dictionary: {Name(s.DictSource)}, sentence context {On(s.UseContext)}, English–English {On(s.EnDict)}, explained: {(s.ExplainLangs.Count > 0 ? string.Join(',', s.ExplainLangs) : "-")}, translate into {s.TargetLang}, Arabic → English {On(s.ReverseForArabic)}",
            $"Card: Wikipedia {On(s.ShowWikipedia)}, speak {On(s.AutoSpeak)}, theme {s.Theme}, animations {s.Motion}",
            $"Translation: {Name(s.TrService)} via {(s.TrProvider is { } p ? AiModels.Name(p) : "the writing tools' AI")}",
            $"Writing tools: {AiModels.Name(s.AiProvider)} (Claude key {Saved("claude")}, Gemini key {Saved("gemini")}, OpenAI key {Saved("openai")}, custom server {custom}, Gemini model {Or(s.GeminiModel, "default")}, Ollama model {Or(s.OllamaModel, "none")} at {(s.OllamaUrl.Trim().Length > 0 ? "a custom address" : "the default address")})",
            $"Data: {deck.Counts(0).Total} cards, {history.Count()} in history, journal {mistakes.Read().Checks} checks, packs: {(packs.Languages.Count > 0 ? string.Join(',', packs.Languages) : "-")}",
            .. host.Lines,
            "",
            "Recent errors (newest first):",
            .. errorLines.Count > 0 ? errorLines : ["  none"],
        ];
        return string.Join('\n', lines);
    }

    private static string Name(DictSource source) => source.ToString().ToLowerInvariant();

    private static string Name(TranslateWith service) => service.ToString().ToLowerInvariant();

    private static string Or(string value, string fallback) => value.Trim().Length > 0 ? value.Trim() : fallback;
}
