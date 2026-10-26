namespace Lamha.Core.Updates;

/// <summary>A newer version found: its number, and how to download it, ready to install.</summary>
/// <param name="Version">"3.0.0-beta.4".</param>
/// <param name="DownloadAsync">Downloads it into the install folder's packages (a delta when there's one).</param>
public sealed record FoundUpdate(string Version, Func<CancellationToken, Task> DownloadAsync);

/// <summary>
/// Where Lamha's new versions come from: Velopack and GitHub Releases in the app (decision 0004), a fake in tests.
/// </summary>
public interface IUpdateSource
{
    /// <summary>Lamha was installed by its setup and can update itself; false from a build folder (development).</summary>
    bool CanUpdate { get; }

    /// <summary>The newer version, or null when this one is the latest. Throws when it can't be asked (no internet).</summary>
    Task<FoundUpdate?> FindAsync(CancellationToken cancel);

    /// <summary>
    /// The version downloaded is installed once Lamha has exited (Lamha waits for nothing), and Lamha starts again when
    /// <paramref name="restart"/>.
    /// </summary>
    void InstallAfterExit(bool restart);
}
