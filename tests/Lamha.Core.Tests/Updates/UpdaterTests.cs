using Lamha.Core.Tests.Support;
using Lamha.Core.Updates;
using Microsoft.Extensions.Time.Testing;

namespace Lamha.Core.Tests.Updates;

/// <summary>A source that answers as the test says, and records what was asked of it.</summary>
internal sealed class FakeUpdateSource : IUpdateSource
{
    /// <summary>The version GitHub has; null: this one is the latest.</summary>
    public string? Newer { get; set; }

    /// <summary>Thrown by the check (no internet).</summary>
    public Exception? Fails { get; set; }

    /// <summary>Lets a test hold the download.</summary>
    public TaskCompletionSource? Download { get; set; }

    public bool CanUpdate { get; set; } = true;

    public int Finds { get; private set; }

    public List<string> Downloads { get; } = [];

    /// <summary>Set when a download starts.</summary>
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>InstallAfterExit's calls: their restart.</summary>
    public List<bool> Installs { get; } = [];

    public async Task<FoundUpdate?> FindAsync(CancellationToken cancel)
    {
        Finds++;
        await Task.Yield();
        if (Fails is not null)
        {
            throw Fails;
        }

        return Newer is { } version
            ? new FoundUpdate(version, async c =>
            {
                Downloads.Add(version);
                Started.TrySetResult();
                if (Download is not null)
                {
                    await Download.Task.WaitAsync(c);
                }
            })
            : null;
    }

    public void InstallAfterExit(bool restart) => Installs.Add(restart);
}

/// <summary>Lamha 2's tools/test-updater.mjs, with a fake source instead of electron-updater.</summary>
/// <remarks>
/// Not ported: the version comparison and the Portable's notice (Velopack compares versions, and its Portable updates
/// itself), and the English notices (the words are the app's: ResourcesTests holds them to Lamha 2's).
/// </remarks>
public class UpdaterTests
{
    private static (Updater Updater, FakeUpdateSource Source, List<(UpdateNotice Notice, string? Version)> Notes, List<UpdateStatus> Changes) Make(Action<FakeUpdateSource>? setup = null)
    {
        var source = new FakeUpdateSource();
        setup?.Invoke(source);
        var updater = new Updater(source, new FakeTimeProvider(TestLamha.Start));
        var notes = new List<(UpdateNotice, string?)>();
        var changes = new List<UpdateStatus>();
        updater.Notice += (notice, version) => notes.Add((notice, version));
        updater.Changed += state => changes.Add(state.Status);
        return (updater, source, notes, changes);
    }

    [Fact]
    public async Task ANewVersionDownloadsThenIsReadyWithANotificationThatRestartsIntoIt()
    {
        var (updater, source, notes, changes) = Make(s => s.Newer = "3.0.1");
        await updater.CheckAsync(byUser: false, TestContext.Current.CancellationToken);
        Assert.Equal(UpdateStatus.Ready, updater.State.Status);
        Assert.Equal("3.0.1", updater.State.Version);
        Assert.Equal(TestLamha.Start, updater.State.CheckedAt);
        Assert.Equal(["3.0.1"], source.Downloads);
        Assert.Equal([(UpdateNotice.Ready, (string?)"3.0.1")], notes);
        Assert.Equal([UpdateStatus.Checking, UpdateStatus.Downloading, UpdateStatus.Ready], changes); // the tray and Settings were told

        Assert.True(updater.InstallAfterExit(restart: true));
        Assert.Equal([true], source.Installs); // installed, then Lamha starts again
    }

    [Fact]
    public async Task UpToDateIsQuietWhenAutomaticAndToldWhenTheUserAsked()
    {
        var (auto, _, autoNotes, _) = Make();
        await auto.CheckAsync(byUser: false, TestContext.Current.CancellationToken);
        Assert.Equal(UpdateStatus.Latest, auto.State.Status);
        Assert.Empty(autoNotes);

        var (byUser, _, notes, _) = Make();
        await byUser.CheckAsync(byUser: true, TestContext.Current.CancellationToken);
        Assert.Equal([(UpdateNotice.Latest, (string?)null)], notes);
    }

    [Fact]
    public async Task NoInternetIsAnErrorToldOnlyWhenTheUserAsked()
    {
        var (auto, autoSource, autoNotes, _) = Make(s => s.Fails = new HttpRequestException());
        await auto.CheckAsync(byUser: false, TestContext.Current.CancellationToken);
        Assert.Equal(UpdateStatus.Error, auto.State.Status);
        Assert.Equal("HttpRequestException", auto.State.Problem); // the type for the log, never the message
        Assert.Empty(autoNotes);
        Assert.Empty(autoSource.Downloads);

        var (byUser, _, notes, _) = Make(s => s.Fails = new HttpRequestException());
        await byUser.CheckAsync(byUser: true, TestContext.Current.CancellationToken);
        Assert.Equal([(UpdateNotice.Failed, (string?)null)], notes);
    }

    [Fact]
    public async Task NotInstalledNeverChecks()
    {
        var (updater, source, notes, _) = Make(s => { s.CanUpdate = false; s.Newer = "9.9.9"; });
        await updater.CheckAsync(byUser: false, TestContext.Current.CancellationToken);
        Assert.Equal(UpdateStatus.Dev, updater.State.Status);
        Assert.Empty(notes);
        await updater.CheckAsync(byUser: true, TestContext.Current.CancellationToken);
        Assert.Equal([(UpdateNotice.Dev, (string?)null)], notes); // "work in the installed version only"
        Assert.Equal(0, source.Finds);
        Assert.False(updater.InstallAfterExit(restart: true));
        Assert.Empty(source.Installs);
    }

    [Fact]
    public async Task ACheckWhileOneRunsOrOnceReadyAsksNothingMore()
    {
        var hold = new TaskCompletionSource();
        var (updater, source, notes, _) = Make(s => { s.Newer = "3.0.1"; s.Download = hold; });
        var first = updater.CheckAsync(byUser: false, TestContext.Current.CancellationToken);
        await source.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(UpdateStatus.Downloading, updater.State.Status);
        await updater.CheckAsync(byUser: true, TestContext.Current.CancellationToken); // the tray's item is greyed, but a second path could ask
        Assert.Equal(1, source.Finds);

        hold.SetResult();
        await first;
        await updater.CheckAsync(byUser: true, TestContext.Current.CancellationToken);
        Assert.Equal(1, source.Finds);
        Assert.Equal(UpdateStatus.Ready, updater.State.Status);
        Assert.Single(notes);
    }

    [Fact]
    public async Task QuittingDuringADownloadGoesBackQuietly()
    {
        using var quit = new CancellationTokenSource();
        var (updater, source, notes, _) = Make(s => { s.Newer = "3.0.1"; s.Download = new TaskCompletionSource(); });
        var check = updater.CheckAsync(byUser: true, quit.Token);
        await source.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await quit.CancelAsync();
        await check;
        Assert.Equal(UpdateStatus.Idle, updater.State.Status);
        Assert.Empty(notes);
        Assert.False(updater.InstallAfterExit(restart: false)); // nothing downloaded: nothing installs at quit
        Assert.Empty(source.Installs);
    }

    [Fact]
    public async Task AfterAFailureTheNextCheckTriesAgain()
    {
        var (updater, source, _, _) = Make(s => s.Fails = new HttpRequestException());
        await updater.CheckAsync(byUser: false, TestContext.Current.CancellationToken);
        source.Fails = null;
        source.Newer = "3.0.1";
        await updater.CheckAsync(byUser: false, TestContext.Current.CancellationToken);
        Assert.Equal(UpdateStatus.Ready, updater.State.Status);
        Assert.Null(updater.State.Problem);
        Assert.Equal(2, source.Finds);
    }
}
