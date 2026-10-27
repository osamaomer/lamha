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
/// <param name="Page">
/// <c>--page=translate</c> (write, review, history, clipboard, settings): the main window on that page at start, for
/// screenshots; <c>--page=settings:about</c> opens a Settings category ("languages", "card"…).
/// </param>
/// <param name="Panel"><c>--panel</c>: the clipboard panel at start (with nothing to paste into), for screenshots.</param>
/// <param name="FirstRun">
/// <c>--first-run</c>: first run at start even when it's done (<c>--first-run=lamha2</c>, <c>try</c>, <c>writing</c> or
/// <c>ready</c> at that step), for screenshots. Without it, first run shows only at the first start, and never with <c>--measure</c>,
/// <c>--page</c>, <c>--panel</c>, <c>--sample</c> or <c>--gallery</c>.
/// </param>
/// <param name="Updates">
/// <c>--updates=folder</c> (or an address): Velopack's releases from there instead of GitHub, to try an update with
/// packages made on this PC.
/// </param>
/// <param name="Packs">
/// <c>--packs=address</c>: the language packs downloaded from there instead of their GitHub release, to try packs made
/// on this PC before they're published.
/// </param>
/// <param name="Lamha2">
/// <c>--lamha2=folder</c>: first run's Lamha 2 step reads Lamha 2's data from that folder instead of %APPDATA%\Lamha,
/// to try the move on a copy (with <c>--first-run=lamha2</c> and a <c>--profile</c>).
/// </param>
/// <param name="RemoveLamha2">
/// <c>--remove-lamha2</c>: removing Lamha 2 (first run's Ready, the 30 days' clean-up) in a beta or a build from source,
/// which never do it otherwise (decision 0019). It removes the Lamha 2 installed on this PC.
/// </param>
/// <param name="Mini">
/// <c>--mini</c>: the mini translator at start (with <c>--sample=text</c>, that text looked up in it), for screenshots;
/// a second start with it opens the mini translator in the Lamha already running.
/// </param>
/// <param name="Gallery">
/// <c>--gallery</c>: the design system live, every component from its own styles (with <c>--lang</c> and <c>--theme</c>),
/// for checking a change in one place and for screenshots.
/// </param>
public sealed record AppOptions(CardLifetime CardLifetime, bool Measure, string? Profile = null, string? Sample = null, string? Language = null, string? Theme = null, string? Page = null, bool Panel = false, string? FirstRun = null, string? Updates = null, string? Packs = null, string? Lamha2 = null, bool RemoveLamha2 = false, bool Mini = false, bool Gallery = false)
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
        var updates = Value(args, "--updates=");
        var packs = Value(args, "--packs=");
        var lamha2 = Value(args, "--lamha2=");
        return new AppOptions(lifetime, args.Contains("--measure", StringComparer.OrdinalIgnoreCase), string.IsNullOrWhiteSpace(profile) ? null : Path.GetFullPath(profile), sample, language, theme, string.IsNullOrWhiteSpace(page) ? null : page, args.Contains("--panel", StringComparer.OrdinalIgnoreCase), firstRun, string.IsNullOrWhiteSpace(updates) ? null : updates,
            string.IsNullOrWhiteSpace(packs) ? null : packs.TrimEnd('/') + "/", string.IsNullOrWhiteSpace(lamha2) ? null : Path.GetFullPath(lamha2),
            args.Contains("--remove-lamha2", StringComparer.OrdinalIgnoreCase), args.Contains("--mini", StringComparer.OrdinalIgnoreCase),
            args.Contains("--gallery", StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>What Lamha starts with again after an update: the profile and the releases' folder, not a screenshot's page or sample.</summary>
    public string[] RestartArgs() =>
        [.. new[] { Profile is null ? null : $"--profile={Profile}", Updates is null ? null : $"--updates={Updates}" }.OfType<string>()];

    private static string? Value(IReadOnlyList<string> args, string name) =>
        args.FirstOrDefault(a => a.StartsWith(name, StringComparison.OrdinalIgnoreCase))?[name.Length..].Trim('"');
}
