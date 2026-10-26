using Lamha.Core.Settings;
using Lamha.Core.Updates;
using Lamha.Windows.Shell;
using Microsoft.UI.Dispatching;

namespace Lamha.App.Services;

/// <summary>
/// Lamha's updates as Lamha 2 had them (main.js <c>startUpdates()</c>, updater.js <c>schedule()</c>): with Automatic
/// updates on, a check 15 seconds after start and every 6 hours, downloaded in the background. Update ready says so,
/// with Restart now; the tray's item and Settings' line follow the state; Quit installs a version that's ready. Core's
/// <see cref="Updater"/> decides; this times it and tells the user. Its events come on the UI thread.
/// </summary>
public sealed partial class UpdateService : IDisposable
{
    private const string Tag = "update";
    private const string RestartAction = "update-restart";
    private static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Every = TimeSpan.FromHours(6);

    private readonly Updater _updater;
    private readonly SettingsService _settings;
    private readonly NotificationService _notes;
    private readonly IdleMemory _idle;
    private readonly DispatcherQueue _queue;
    private readonly DispatcherQueueTimer _timer;
    private readonly CancellationTokenSource _quit = new();
    private readonly bool _automatic;
    private bool _on;

    /// <param name="source">Velopack and GitHub Releases.</param>
    /// <param name="settings">Automatic updates' switch.</param>
    /// <param name="notes">Windows' notifications.</param>
    /// <param name="idle">Memory handed back after a check (its HTTP and Velopack's objects).</param>
    /// <param name="queue">The UI thread.</param>
    /// <param name="automatic">Checks by themselves at all (never with <c>--measure</c>, as Lamha 2's self-test).</param>
    public UpdateService(Core.Updates.IUpdateSource source, SettingsService settings, NotificationService notes, IdleMemory idle, DispatcherQueue queue, bool automatic)
    {
        _updater = new Updater(source, TimeProvider.System);
        _settings = settings;
        _notes = notes;
        _idle = idle;
        _queue = queue;
        _automatic = automatic;
        _timer = queue.CreateTimer();
        _timer.Tick += (_, _) =>
        {
            // The first check, then every 6 hours.
            _timer.Stop();
            _timer.Interval = Every;
            _timer.Start();
            _ = CheckAsync(byUser: false);
        };
        _updater.Changed += state => queue.TryEnqueue(() => OnChanged(state));
        _updater.Notice += (notice, version) => queue.TryEnqueue(() => Show(notice, version));
        settings.Changed += OnSettings;
        notes.Clicked += OnClicked;
        Schedule(settings.Current.UpdatesAuto);
    }

    /// <summary>The state changed (the tray's item, Settings' line).</summary>
    public event Action? Changed;

    /// <summary>Restart now: the app closes everything and exits, and Velopack starts the new version.</summary>
    public Action? Exit { get; set; }

    public UpdateState State => _updater.State;

    /// <summary>This version, "3.0.0-beta.4" (without the commit).</summary>
    public static string Current { get; } = Brain.Version.Split('+')[0];

    /// <summary>A version in a line of text, kept "3.0.0-beta.2" in Arabic (not "beta.2-3.0.0").</summary>
    public static string Shown(string version) => "‎" + version + "‎";

    /// <summary>Check for updates (the tray, Settings): told even when there's nothing new.</summary>
    public void Check() => _ = CheckAsync(byUser: true);

    /// <summary>Restart now (the notification, the tray, Settings): installs the version that's ready and starts it.</summary>
    public void Restart()
    {
        if (_updater.InstallAfterExit(restart: true))
        {
            AppLog.Info($"update: restart into {State.Version}");
            _notes.Remove(Tag);
            Exit?.Invoke();
        }
    }

    /// <summary>Lamha quits: a version that's ready installs once it has (Lamha 2's <c>autoInstallOnAppQuit</c>).</summary>
    public void Quitting()
    {
        _quit.Cancel();
        if (_updater.InstallAfterExit(restart: false))
        {
            AppLog.Info($"update: installs {State.Version} after quit");
            _notes.Remove(Tag);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _quit.Cancel();
        _quit.Dispose();
        _settings.Changed -= OnSettings;
        _notes.Clicked -= OnClicked;
    }

    private async Task CheckAsync(bool byUser)
    {
        await _updater.CheckAsync(byUser, _quit.Token).ConfigureAwait(true);
        _idle.Schedule(); // its HTTP and Velopack's objects are gone: their memory goes back too (only if no window is open)
    }

    /// <summary>Automatic updates on or off (Lamha 2's <c>schedule(on)</c>): the first check 15 seconds later.</summary>
    private void Schedule(bool on)
    {
        _on = on;
        _timer.Stop();
        if (on && _automatic)
        {
            _timer.Interval = FirstCheck;
            _timer.Start();
        }
    }

    private void OnSettings(object? sender, LamhaSettings settings)
    {
        if (settings.UpdatesAuto != _on)
        {
            _queue.TryEnqueue(() => Schedule(settings.UpdatesAuto));
        }
    }

    private void OnChanged(UpdateState state)
    {
        AppLog.Info($"update: {state.Status.ToString().ToLowerInvariant()}{(state.Version is { } v ? " " + v : "")}{(state.Problem is { } p ? " " + p : "")}");
        Changed?.Invoke();
    }

    private void OnClicked(AppLink link)
    {
        if (link.Action == RestartAction)
        {
            Restart();
        }
    }

    /// <summary>Lamha 2's update notifications, its words (the tray design: Update ready with Restart now).</summary>
    private void Show(UpdateNotice notice, string? version) => _notes.Show(notice switch
    {
        UpdateNotice.Ready => new Note(Localization.Get("Note_UpReady"), Localization.Format("Note_UpReadyBody", Shown(version ?? "")))
        {
            Action = RestartAction,
            Buttons = [new(Localization.Get("Note_UpRestartNow"), RestartAction)],
            Tag = Tag,
        },
        UpdateNotice.Latest => new Note(Localization.Get("Note_UpLatest"), Localization.Format("Note_UpLatestBody", Shown(Current))) { Tag = Tag },
        UpdateNotice.Failed => new Note(Localization.Get("Note_UpFailed"), Localization.Get("Note_UpFailedBody")) { Tag = Tag },
        _ => new Note(Localization.Get("Note_Updates"), Localization.Get("Note_UpDevBody")) { Tag = Tag },
    });
}
