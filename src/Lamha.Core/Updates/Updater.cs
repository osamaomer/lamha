namespace Lamha.Core.Updates;

/// <summary>
/// Where the update stands: Lamha 2's updater.js <c>state.status</c>, without the Portable's "available" (Velopack's
/// Portable updates itself).
/// </summary>
public enum UpdateStatus
{
    /// <summary>Not checked yet.</summary>
    Idle,
    Checking,

    /// <summary>A newer version was found and is downloading.</summary>
    Downloading,

    /// <summary>Downloaded: it installs when Lamha restarts or quits.</summary>
    Ready,

    /// <summary>This is the latest version.</summary>
    Latest,

    /// <summary>The last check failed (no internet, GitHub didn't answer).</summary>
    Error,

    /// <summary>Not installed by the setup (a build folder): Lamha never checks.</summary>
    Dev,
}

/// <summary>What a notification tells the user (Lamha 2's <c>notify()</c> calls in updater.js).</summary>
public enum UpdateNotice
{
    /// <summary>Update ready, with Restart now: always.</summary>
    Ready,

    /// <summary>You're up to date: only when the user asked.</summary>
    Latest,

    /// <summary>Couldn't check for updates: only when the user asked.</summary>
    Failed,

    /// <summary>Automatic updates work in the installed version only: only when the user asked.</summary>
    Dev,
}

/// <summary>The update's state, for the tray and Settings.</summary>
/// <param name="Status">Where it stands.</param>
/// <param name="Version">The version found (downloading, ready), or null.</param>
/// <param name="CheckedAt">When GitHub last answered, or null.</param>
/// <param name="Problem">The last failure's exception type, for the log (never its message).</param>
public sealed record UpdateState(UpdateStatus Status, string? Version = null, DateTimeOffset? CheckedAt = null, string? Problem = null);

/// <summary>
/// Lamha's updates: Lamha 2's desktop/updater.js. A check finds a newer version and downloads it in the background,
/// then it's ready: it installs when Lamha restarts (Restart now) or quits. Automatic checks are quiet; a check the
/// user asked for (the tray, Settings) also says when there's nothing new or the check failed. When to check (at
/// start, every 6 hours) is the app's: <see cref="CheckAsync"/> is called on its timer. Events may come on any thread.
/// </summary>
public sealed class Updater(IUpdateSource source, TimeProvider time)
{
    private readonly Lock _gate = new();
    private UpdateState _state = new(UpdateStatus.Idle);

    /// <summary>The state changed (the tray's item, Settings' line).</summary>
    public event Action<UpdateState>? Changed;

    /// <summary>A notification to show, with the version it's about (Ready) or null.</summary>
    public event Action<UpdateNotice, string?>? Notice;

    public UpdateState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// Looks for a newer version and downloads it. Nothing happens while a check or a download runs, or once one is
    /// ready (its Restart is the next step). <paramref name="byUser"/>: from the tray or Settings, so "up to date"
    /// and failures are told too. Cancelled (Lamha quits): back to where it was, quietly.
    /// </summary>
    public async Task CheckAsync(bool byUser, CancellationToken cancel = default)
    {
        if (!source.CanUpdate)
        {
            Set(new UpdateState(UpdateStatus.Dev));
            if (byUser)
            {
                Notice?.Invoke(UpdateNotice.Dev, null);
            }

            return;
        }

        UpdateState before;
        lock (_gate)
        {
            before = _state;
            if (before.Status is UpdateStatus.Checking or UpdateStatus.Downloading or UpdateStatus.Ready)
            {
                return;
            }

            _state = before with { Status = UpdateStatus.Checking, Problem = null };
        }

        Changed?.Invoke(State);
        try
        {
            var found = await source.FindAsync(cancel).ConfigureAwait(false);
            var checkedAt = time.GetUtcNow();
            if (found is null)
            {
                Set(new UpdateState(UpdateStatus.Latest, CheckedAt: checkedAt));
                if (byUser)
                {
                    Notice?.Invoke(UpdateNotice.Latest, null);
                }

                return;
            }

            Set(new UpdateState(UpdateStatus.Downloading, found.Version, checkedAt));
            await found.DownloadAsync(cancel).ConfigureAwait(false);
            Set(new UpdateState(UpdateStatus.Ready, found.Version, checkedAt));
            Notice?.Invoke(UpdateNotice.Ready, found.Version);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            Set(before);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Velopack's failures aren't one type (the network, GitHub's answer, the packages folder): all are "couldn't".
            Set(new UpdateState(UpdateStatus.Error, CheckedAt: before.CheckedAt, Problem: ex.GetType().Name));
            if (byUser)
            {
                Notice?.Invoke(UpdateNotice.Failed, null);
            }
        }
    }

    /// <summary>
    /// When a version is ready, has it installed after Lamha exits (and Lamha started again when
    /// <paramref name="restart"/>): Restart now, or Quit (Lamha 2's <c>autoInstallOnAppQuit</c>). False when none is
    /// ready: nothing to install.
    /// </summary>
    public bool InstallAfterExit(bool restart)
    {
        if (State.Status != UpdateStatus.Ready)
        {
            return false;
        }

        source.InstallAfterExit(restart);
        return true;
    }

    private void Set(UpdateState state)
    {
        lock (_gate)
        {
            _state = state;
        }

        Changed?.Invoke(state);
    }
}
