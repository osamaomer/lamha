namespace Lamha.App;

/// <summary>How the card window lives between lookups (a phase 0 measurement: see docs/native-plan.md).</summary>
public enum CardLifetime
{
    /// <summary>Hidden after a lookup and shown again at once next time; costs memory in the tray.</summary>
    Keep,

    /// <summary>Closed after a lookup and created again next time; frees its memory, costs time at the next lookup.</summary>
    Recreate,
}

/// <summary>Start-up options, from the command line (development and measurement only, never needed by users).</summary>
/// <param name="CardLifetime"><c>--card=keep</c> (default) or <c>--card=recreate</c>.</param>
/// <param name="Measure"><c>--measure</c>: a fixed timeline (card shown, card closed, quit) for tools/Measure.</param>
/// <param name="Profile">
/// <c>--profile=folder</c>: the user's data (lamha.db) in that folder instead of %LOCALAPPDATA%\Lamha Data, keys under their
/// own names, and the startup entry left alone; for measuring and trying things without touching the real data.
/// </param>
/// <param name="Sample">
/// <c>--sample=text</c>: the sample card, shown at start (and by <c>--measure</c>); <c>--sample=write:text</c> opens
/// the writing tools on it, <c>--sample=write:</c> Write new, <c>--sample=error:code</c> the error a lookup would show
/// for that code (<c>network</c>, <c>rate_limited</c>…). For screenshots of the card's states. With <c>--page=translate</c>,
/// the text looked up in the main window's box instead.
/// </param>
/// <param name="Language"><c>--lang=ar</c> or <c>--lang=en</c>: the interface language for this run, not saved.</param>
/// <param name="Theme"><c>--theme=light</c> or <c>--theme=dark</c>: the windows' theme for this run, not saved.</param>
/// <param name="Page"><c>--page=translate</c> (write, review, history, clipboard, settings): the main window on that page at start, for screenshots.</param>
/// <param name="Panel"><c>--panel</c>: the clipboard panel at start (with nothing to paste into), for screenshots.</param>
/// <param name="FirstRun">
/// <c>--first-run</c>: first run at start even when it's done (<c>--first-run=try</c>, <c>writing</c> or <c>ready</c> at
/// that step), for screenshots. Without it, first run shows only at the first start, and never with <c>--measure</c>,
/// <c>--page</c>, <c>--panel</c> or <c>--sample</c>.
/// </param>
public sealed record AppOptions(CardLifetime CardLifetime, bool Measure, string? Profile = null, string? Sample = null, string? Language = null, string? Theme = null, string? Page = null, bool Panel = false, string? FirstRun = null)
{
    public static AppOptions FromArgs(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var lifetime = args.Contains("--card=recreate", StringComparer.OrdinalIgnoreCase) ? CardLifetime.Recreate : CardLifetime.Keep;
        var profile = Value(args, "--profile=");
        var sample = Value(args, "--sample=");
        var language = Value(args, "--lang=") is "ar" or "en" ? Value(args, "--lang=") : null;
        var theme = Value(args, "--theme=") is "light" or "dark" ? Value(args, "--theme=") : null;
        var page = Value(args, "--page=");
        var firstRun = args.Contains("--first-run", StringComparer.OrdinalIgnoreCase) ? "" : Value(args, "--first-run=");
        return new AppOptions(lifetime, args.Contains("--measure", StringComparer.OrdinalIgnoreCase), string.IsNullOrWhiteSpace(profile) ? null : Path.GetFullPath(profile), sample, language, theme, string.IsNullOrWhiteSpace(page) ? null : page, args.Contains("--panel", StringComparer.OrdinalIgnoreCase), firstRun);
    }

    private static string? Value(IReadOnlyList<string> args, string name) =>
        args.FirstOrDefault(a => a.StartsWith(name, StringComparison.OrdinalIgnoreCase))?[name.Length..].Trim('"');
}
