using Lamha.Core.Clipboard;
using Lamha.Core.Settings;
using Lamha.Windows.Clipboard;
using Lamha.Windows.Shell;
using Microsoft.UI.Dispatching;

namespace Lamha.App.Services;

/// <summary>
/// Clipboard history in the app: the listener on while the setting is on, the tray's pause, and the hourly expiry.
/// Lamha 2's startClipboard() / pauseClipboard() / runClipExpiry() in main.js. The log gets lengths and reasons, never
/// what was copied.
/// </summary>
public sealed partial class ClipboardService : IDisposable
{
    private readonly ClipboardHistory _history;
    private readonly SettingsService _settings;
    private readonly DispatcherQueueTimer _expiry;
    private readonly DispatcherQueueTimer _resume;
    private LamhaSettings _applied;

    public ClipboardService(MessageWindow window, Brain brain, DispatcherQueue queue)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(queue);
        _history = brain.ClipboardHistory;
        _settings = brain.Settings;
        Monitor = new ClipboardMonitor(window, _history, _settings, TimeProvider.System);
        Monitor.Recorded += length => AppLog.Info($"clipboard: recorded {length} characters");
        Monitor.Skipped += reason => AppLog.Info($"clipboard: skipped ({reason})");
        Monitor.Failed += ex => AppLog.Error("clipboard capture", ex);

        _expiry = queue.CreateTimer();
        _expiry.Interval = TimeSpan.FromHours(1);
        _expiry.Tick += (_, _) => Expire();
        _resume = queue.CreateTimer();
        _resume.IsRepeating = false;
        _resume.Tick += (_, _) => Resume();

        _applied = _settings.Current;
        _settings.Changed += (_, s) => queue.TryEnqueue(() => Apply(s));
        Monitor.Apply();
        Expire();
        _expiry.Start();
    }

    public ClipboardMonitor Monitor { get; }

    public bool IsOn => _settings.Current.ClipboardEnabled;

    /// <summary>Paused from the tray until this time; <see cref="DateTimeOffset.MaxValue"/> is "until I resume". Not kept across a restart.</summary>
    public DateTimeOffset? PausedUntil { get; private set; }

    /// <summary>The pause started or ended.</summary>
    public event Action? PauseChanged;

    /// <summary>Clipboard history on or off (Settings → Clipboard, and the Clipboard page's Turn on).</summary>
    public void SetOn(bool on) => _settings.Update(s => s with { ClipboardEnabled = on });

    /// <summary>The tray's pause: <paramref name="length"/>, or null for "until I resume".</summary>
    public void Pause(TimeSpan? length)
    {
        _resume.Stop();
        PausedUntil = length is { } l ? DateTimeOffset.Now + l : DateTimeOffset.MaxValue;
        Monitor.Paused = true;
        if (length is { } wait)
        {
            _resume.Interval = wait;
            _resume.Start();
        }

        PauseChanged?.Invoke();
    }

    public void Resume()
    {
        _resume.Stop();
        if (PausedUntil is null)
        {
            return;
        }

        PausedUntil = null;
        Monitor.Paused = false;
        PauseChanged?.Invoke();
    }

    public void Dispose()
    {
        _expiry.Stop();
        _resume.Stop();
        Monitor.Dispose();
    }

    private void Apply(LamhaSettings next)
    {
        var before = _applied;
        _applied = next;
        Monitor.Apply();
        if (!next.ClipboardEnabled && PausedUntil is not null)
        {
            Resume(); // off ends a pause, as in Lamha 2
        }

        if (next.ClipboardMaxItems != before.ClipboardMaxItems)
        {
            _history.ApplyLimit();
        }

        if (next.ClipboardExpiryDays != before.ClipboardExpiryDays)
        {
            Expire();
        }
    }

    private void Expire()
    {
        try
        {
            var gone = _history.Expire();
            if (gone > 0)
            {
                AppLog.Info($"clipboard: {gone} old copies expired");
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppLog.Error("clipboard expiry", ex);
        }
    }
}
