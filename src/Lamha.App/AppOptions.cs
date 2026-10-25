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
/// <c>--sample=text</c>: the sample card's text (the tray's item, <c>--measure</c>); <c>--sample=write:text</c> opens
/// the writing tools on it, <c>--sample=write:</c> Write new. For screenshots of the card's states.
/// </param>
/// <param name="Language"><c>--lang=ar</c> or <c>--lang=en</c>: the interface language for this run, not saved.</param>
public sealed record AppOptions(CardLifetime CardLifetime, bool Measure, string? Profile = null, string? Sample = null, string? Language = null)
{
    public static AppOptions FromArgs(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var lifetime = args.Contains("--card=recreate", StringComparer.OrdinalIgnoreCase) ? CardLifetime.Recreate : CardLifetime.Keep;
        var profile = Value(args, "--profile=");
        var sample = Value(args, "--sample=");
        var language = Value(args, "--lang=") is "ar" or "en" ? Value(args, "--lang=") : null;
        return new AppOptions(lifetime, args.Contains("--measure", StringComparer.OrdinalIgnoreCase), string.IsNullOrWhiteSpace(profile) ? null : Path.GetFullPath(profile), sample, language);
    }

    private static string? Value(IReadOnlyList<string> args, string name) =>
        args.FirstOrDefault(a => a.StartsWith(name, StringComparison.OrdinalIgnoreCase))?[name.Length..].Trim('"');
}
