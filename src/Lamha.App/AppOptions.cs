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
/// <c>--profile=folder</c>: the user's data (lamha.db) in that folder instead of %LOCALAPPDATA%\Lamha, keys under their
/// own names, and the startup entry left alone; for measuring and trying things without touching the real data.
/// </param>
public sealed record AppOptions(CardLifetime CardLifetime, bool Measure, string? Profile = null)
{
    public static AppOptions FromArgs(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var lifetime = args.Contains("--card=recreate", StringComparer.OrdinalIgnoreCase) ? CardLifetime.Recreate : CardLifetime.Keep;
        var profile = args.FirstOrDefault(a => a.StartsWith("--profile=", StringComparison.OrdinalIgnoreCase))?["--profile=".Length..].Trim('"');
        return new AppOptions(lifetime, args.Contains("--measure", StringComparer.OrdinalIgnoreCase), string.IsNullOrWhiteSpace(profile) ? null : Path.GetFullPath(profile));
    }
}
