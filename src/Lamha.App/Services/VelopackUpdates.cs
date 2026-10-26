using Lamha.Core.Updates;
using Velopack;
using Velopack.Sources;

namespace Lamha.App.Services;

/// <summary>
/// Lamha's new versions from its GitHub Releases, through Velopack (decision 0004): the channel Lamha was packed with
/// (<c>beta-win-x64</c>…), pre-releases too for a beta. Delta packages when the release has them. Velopack's objects are
/// made for a check and dropped after it (only a downloaded version's are kept, to install it), so nothing stays in
/// memory between checks.
/// </summary>
internal sealed class VelopackUpdates : Core.Updates.IUpdateSource
{
    private const string Repository = "https://github.com/osamaomer/lamha";

    private readonly string? _from;
    private readonly string[] _restartArgs;
    private bool? _canUpdate;
    private UpdateManager? _manager;
    private VelopackAsset? _downloaded;

    /// <param name="from">A folder or address of Velopack releases instead of GitHub (<c>--updates=</c>, to try an update).</param>
    /// <param name="restartArgs">Lamha's own arguments, given again when it restarts into the new version (a <c>--profile</c>).</param>
    public VelopackUpdates(string? from, string[] restartArgs)
    {
        _from = from;
        _restartArgs = restartArgs;
    }

    public bool CanUpdate => _canUpdate ??= Manager().IsInstalled;

    public async Task<FoundUpdate?> FindAsync(CancellationToken cancel)
    {
        var manager = Manager();
        var found = await manager.CheckForUpdatesAsync().WaitAsync(cancel).ConfigureAwait(false);
        return found is null
            ? null
            : new FoundUpdate(found.TargetFullRelease.Version.ToString(), async c =>
            {
                await manager.DownloadUpdatesAsync(found, cancelToken: c).ConfigureAwait(false);
                _manager = manager;
                _downloaded = found.TargetFullRelease;
            });
    }

    /// <summary>
    /// A version downloaded earlier but not installed is installed now, and Lamha started again (Velopack exits this
    /// process). False, and nothing done, when there's none or Lamha isn't installed.
    /// </summary>
    public bool InstallPending()
    {
        var manager = Manager();
        if (!manager.IsInstalled || manager.UpdatePendingRestart is not { } pending)
        {
            return false;
        }

        AppLog.Info($"update: installing {pending.Version} at start");
        manager.ApplyUpdatesAndRestart(pending, _restartArgs);
        return true;
    }

    /// <summary>Velopack's updater waits for Lamha to exit (up to a minute), installs without a window, and starts it again.</summary>
    public void InstallAfterExit(bool restart)
    {
        if (_manager is not null && _downloaded is not null)
        {
            _manager.WaitExitThenApplyUpdates(_downloaded, silent: true, restart, _restartArgs);
        }
    }

    private UpdateManager Manager() => _from is not null
        ? new UpdateManager(_from)
        : new UpdateManager(new GithubSource(Repository, accessToken: null, prerelease: Brain.Version.Contains('-', StringComparison.Ordinal)));
}
