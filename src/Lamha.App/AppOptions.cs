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
public sealed record AppOptions(CardLifetime CardLifetime, bool Measure)
{
    public static AppOptions FromArgs(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var lifetime = args.Contains("--card=recreate", StringComparer.OrdinalIgnoreCase) ? CardLifetime.Recreate : CardLifetime.Keep;
        return new AppOptions(lifetime, args.Contains("--measure", StringComparer.OrdinalIgnoreCase));
    }
}
