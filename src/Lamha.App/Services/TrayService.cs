using System.Globalization;
using Lamha.Core.Settings;
using Lamha.Windows.Shell;

namespace Lamha.App.Services;

/// <summary>What the tray's items open: the main window (on a page), the clipboard panel, and Quit.</summary>
/// <param name="Open">The main window as it was (a click on the icon too), or first run while it's open.</param>
/// <param name="Page">The main window on a page ("review", "write", "settings").</param>
/// <param name="Clipboard">The clipboard panel, with nothing to paste into (a paste only copies).</param>
/// <param name="Quit">Lamha ends.</param>
public sealed record TrayActions(Action Open, Action<string> Page, Action Clipboard, Action Quit);

/// <summary>
/// The tray icon, its tooltip and its menu: Lamha 2's trayMenu() / trayTooltip() in main.js, as the tray design
/// (2026-10-25) has them, with Enable Lamha moved in from Lamha 2's window. The icon has a dot while words wait for
/// review, and is faded while Lamha is turned off. Check for updates comes with the updates (phase 5).
/// </summary>
public sealed partial class TrayService : IDisposable
{
    private static readonly string[] MotionLevels = ["auto", "full", "subtle", "off"];

    private readonly TrayIcon _icon;
    private readonly SettingsService _settings;
    private readonly ClipboardService _clipboard;
    private readonly StartWithWindows _startup;
    private readonly ShortcutService _shortcuts;
    private readonly TrayActions _actions;
    private int _waiting;

    public TrayService(
        MessageWindow window,
        SettingsService settings,
        ClipboardService clipboard,
        StartWithWindows startup,
        ShortcutService shortcuts,
        TrayActions actions)
    {
        _settings = settings;
        _clipboard = clipboard;
        _startup = startup;
        _shortcuts = shortcuts;
        _actions = actions;
        _icon = new TrayIcon(window, Path.Combine(AppContext.BaseDirectory, "Assets", "Lamha.ico"), Tooltip());
        _icon.SetMenu(Menu);
        _icon.Invoked += actions.Open;
        _clipboard.PauseChanged += Refresh;
        Refresh();
    }

    /// <summary>Enable Lamha was switched: the shortcuts are taken or let go, and the icon fades or comes back.</summary>
    public event Action? EnabledChanged;

    /// <summary>Words waiting for review: the dot, the tooltip and Word review's count.</summary>
    public void SetWaiting(int waiting)
    {
        _waiting = waiting;
        Refresh();
    }

    /// <summary>The tooltip and the icon again (also in a new interface language: first run's language step).</summary>
    public void Refresh()
    {
        _icon.SetTooltip(Tooltip());
        _icon.SetMarks(dot: _waiting > 0, faded: !_shortcuts.IsOn);
    }

    /// <summary>The tray icon's own notification, for when Windows' can't be used.</summary>
    public void Notify(string title, string body) => _icon.Notify(title, body);

    public void Dispose() => _icon.Dispose(); // before exiting, or a dead icon stays until the mouse passes over it

    /// <summary>"Lamha — 12 to review — Clipboard history paused" (Lamha 2's).</summary>
    private string Tooltip()
    {
        var parts = new List<string> { Localization.Get("Common_Lamha") };
        if (_waiting > 0)
        {
            parts.Add(Localization.Format("Tray_TipReview", _waiting));
        }

        if (_clipboard.PausedUntil is not null)
        {
            parts.Add(Localization.Get("Tray_TipPaused"));
        }

        return string.Join(" — ", parts);
    }

    private List<TrayMenuItem> Menu()
    {
        var items = new List<TrayMenuItem>
        {
            new(Localization.Get("Tray_Open"), _actions.Open) { Default = true },
            new(Localization.Get("Tray_Clipboard"), _actions.Clipboard),
            new(_waiting > 0 ? Localization.Format("Tray_ReviewCount", _waiting) : Localization.Get("Tray_Review"), () => _actions.Page("review")),
            new(Localization.Get("Tray_Write"), () => _actions.Page("write")),
            TrayMenuItem.Separator,
            new(Localization.Get("Tray_Enable"), ToggleEnabled) { Checked = _shortcuts.IsOn },
        };
        items.AddRange(PauseItems());
        items.Add(MotionItem());
        items.Add(new TrayMenuItem(Localization.Get("Tray_Settings"), () => _actions.Page("settings")));
        items.Add(TrayMenuItem.Separator);

        // The shortcuts, for information (as they are now: Settings may have changed them); one another program took is marked.
        foreach (var s in _shortcuts.All)
        {
            var text = $"{s.Keys} — {Localization.Get(s.LabelKey)}{(s.Taken ? Localization.Get("Tray_Unavailable") : "")}";
            items.Add(new TrayMenuItem(text) { Enabled = false });
        }

        items.Add(TrayMenuItem.Separator);
        items.Add(new TrayMenuItem(Localization.Get("Tray_StartWithWindows"), ToggleStartup) { Checked = _startup.IsOn });
        items.Add(TrayMenuItem.Separator);
        items.Add(new TrayMenuItem(Localization.Get("Tray_Quit"), _actions.Quit));
        return items;
    }

    /// <summary>Pause clipboard history ▸ 15 minutes / 1 hour / Until I resume, or Resume while paused (Lamha 2's pauseMenu()).</summary>
    private IEnumerable<TrayMenuItem> PauseItems()
    {
        if (!_clipboard.IsOn)
        {
            yield break;
        }

        if (_clipboard.PausedUntil is { } until)
        {
            yield return until == DateTimeOffset.MaxValue
                ? new TrayMenuItem(Localization.Get("Tray_Resume"), _clipboard.Resume)
                : new TrayMenuItem(Localization.Format("Tray_ResumeAt", Time(until)), _clipboard.Resume);
            yield break;
        }

        yield return new TrayMenuItem(Localization.Get("Tray_Pause"))
        {
            Items =
            [
                new(Localization.Get("Tray_Pause15"), () => _clipboard.Pause(TimeSpan.FromMinutes(15))),
                new(Localization.Get("Tray_Pause60"), () => _clipboard.Pause(TimeSpan.FromHours(1))),
                new(Localization.Get("Tray_PauseUntil"), () => _clipboard.Pause(null)),
            ],
        };
    }

    /// <summary>Animations ▸ Automatic / Full / Subtle / Off: Settings → Appearance's choice (Lamha 2's motionMenu()).</summary>
    private TrayMenuItem MotionItem()
    {
        var current = _settings.Current.Motion;
        return new TrayMenuItem(Localization.Get("Tray_Motion"))
        {
            Items = [.. MotionLevels.Select(level => new TrayMenuItem(
                Localization.Get("Settings_Motion" + char.ToUpperInvariant(level[0]) + level[1..]),
                () => _settings.Update(s => s with { Motion = level }))
            {
                Radio = true,
                Checked = current == level,
            })],
        };
    }

    private void ToggleEnabled()
    {
        _shortcuts.SetOn(!_shortcuts.IsOn);
        Refresh();
        EnabledChanged?.Invoke();
    }

    private void ToggleStartup()
    {
        var on = !_startup.IsOn;
        _startup.Set(on);
        AppLog.Info($"start with Windows: {(on ? "on" : "off")}");
    }

    /// <summary>"3:40 PM", as Lamha 2 wrote it (ar-EG or en-US, hour and minutes).</summary>
    private static string Time(DateTimeOffset when) =>
        when.ToLocalTime().ToString("h:mm tt", CultureInfo.GetCultureInfo(Localization.IsArabic ? "ar-EG" : "en-US"));
}
