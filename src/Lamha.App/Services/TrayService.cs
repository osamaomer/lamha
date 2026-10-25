using System.Globalization;
using Lamha.Core.Settings;
using Lamha.Windows.Input;
using Lamha.Windows.Shell;

namespace Lamha.App.Services;

/// <summary>A shortcut as the tray lists it: its keys, what it does, and whether Lamha got it.</summary>
public sealed record ShortcutInfo(Hotkey Keys, string LabelKey, bool Registered);

/// <summary>
/// The tray icon, its tooltip and its menu: Lamha 2's trayMenu() / trayTooltip() in main.js, with what exists so far.
/// Clicking the icon opens the main window; the menu's Open, Review, Write and Settings come with the tray's design
/// (phase 4), updates with phase 5. Until Settings exists, clipboard history and the accent colour have their switches
/// here (Proto_ items, removed with phase 4).
/// </summary>
public sealed partial class TrayService : IDisposable
{
    private readonly TrayIcon _icon;
    private readonly SettingsService _settings;
    private readonly ClipboardService _clipboard;
    private readonly StartWithWindows _startup;
    private readonly IReadOnlyList<ShortcutInfo> _shortcuts;
    private readonly Action _sample;
    private readonly Action _quit;

    public TrayService(
        MessageWindow window,
        SettingsService settings,
        ClipboardService clipboard,
        StartWithWindows startup,
        IReadOnlyList<ShortcutInfo> shortcuts,
        Action open,
        Action sample,
        Action quit)
    {
        _settings = settings;
        _clipboard = clipboard;
        _startup = startup;
        _shortcuts = shortcuts;
        _sample = sample;
        _quit = quit;
        _icon = new TrayIcon(window, Path.Combine(AppContext.BaseDirectory, "Assets", "Lamha.ico"), Tooltip());
        _icon.SetMenu(Menu);
        _icon.Invoked += open;
        _clipboard.PauseChanged += () => _icon.SetTooltip(Tooltip());
    }

    /// <summary>A notification from the tray icon.</summary>
    public void Notify(string title, string body) => _icon.Notify(title, body);

    public void Dispose() => _icon.Dispose(); // before exiting, or a dead icon stays until the mouse passes over it

    /// <summary>"Lamha 3 (prototype) — Clipboard history paused".</summary>
    private string Tooltip()
    {
        var parts = new List<string> { Localization.Get("Proto_Tooltip") };
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
            new(Localization.Get("Proto_Sample"), _sample),
            TrayMenuItem.Separator,
        };

        // The shortcuts, for information; one another program took is marked.
        foreach (var s in _shortcuts)
        {
            var text = $"{s.Keys} — {Localization.Get(s.LabelKey)}{(s.Registered ? "" : Localization.Get("Tray_Unavailable"))}";
            items.Add(new TrayMenuItem(text) { Enabled = false });
        }

        items.Add(TrayMenuItem.Separator);
        items.Add(new TrayMenuItem(Localization.Get("Proto_ClipboardHistory"), () => _clipboard.SetOn(!_clipboard.IsOn)) { Checked = _clipboard.IsOn });
        items.AddRange(PauseItems());
        items.Add(new TrayMenuItem(Localization.Get("Proto_Accent"), () => _settings.Update(s => s with { AccentWindows = !s.AccentWindows }))
        {
            Checked = _settings.Current.AccentWindows,
        });
        items.Add(TrayMenuItem.Separator);
        items.Add(new TrayMenuItem(Localization.Get("Tray_StartWithWindows"), ToggleStartup) { Checked = _startup.IsOn });
        items.Add(TrayMenuItem.Separator);
        items.Add(new TrayMenuItem(Localization.Get("Tray_Quit"), _quit));
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
