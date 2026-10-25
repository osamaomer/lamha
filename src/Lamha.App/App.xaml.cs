using Lamha.App.Services;
using Lamha.App.ViewModels.Card;
using Lamha.Windows.Input;
using Lamha.Windows.Selection;
using Lamha.Windows.Shell;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Lamha.App;

/// <summary>
/// Lamha lives in the tray: no window at start, only the tray icon, the shortcuts and the clipboard listener (while
/// clipboard history is on). Windows are created when they open: the card from the shortcuts, the main window from
/// the tray icon or by starting Lamha again.
/// </summary>
public partial class App : Application
{
    /// <summary>The beta's shortcuts, so it runs next to Lamha 2 (which owns Alt+Shift+L / W / V) until 3.0.</summary>
    private static readonly Hotkey LookupHotkey = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, VirtualKey.L);
    private static readonly Hotkey WriteHotkey = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, VirtualKey.W);
    private static readonly Hotkey ClipboardHotkey = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, VirtualKey.V);

    private readonly AppOptions _options;
    private readonly AppInstance _instance;
    private MessageWindow? _messages;
    private Hotkeys? _hotkeys;
    private readonly Brain _brain;
    private AccentService? _accent;
    private SentenceReader? _sentences;
    private ClipboardService? _clipboard;
    private TrayService? _tray;
    private CardService? _card;
    private ClipPanelService? _panel;
    private MainWindowService? _main;

    public App(AppOptions options, AppInstance instance)
    {
        _options = options;
        _instance = instance;
        // The settings decide the interface language, so they open before anything shows.
        _brain = new Brain(DataFolder.For(options.Profile), options.Profile is null ? "Lamha/" : "Lamha-profile/");
        Localization.Apply(options.Language ?? _brain.Localization.Language);
        InitializeComponent();
        // The tray keeps Lamha running with no window open; only Quit ends it.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        UnhandledException += (_, e) => AppLog.Error("unhandled", e.Exception);
        AppLog.Info($"start {Brain.Version} card={options.CardLifetime} measure={options.Measure} profile={(options.Profile is null ? "default" : "other")}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var queue = DispatcherQueue.GetForCurrentThread();
        var idle = new IdleMemory(queue);
        _messages = new MessageWindow();
        _accent = new AccentService(_brain.Settings, queue); // kept: it listens to Windows for as long as Lamha runs
        _clipboard = new ClipboardService(_messages, _brain, queue);
        _sentences = new SentenceReader();

        var startup = new StartWithWindows($"\"{Environment.ProcessPath}\"");
        if (_options.Profile is null)
        {
            startup.Refresh(); // a new install folder (an update keeps it, a reinstall may not)
        }

        var selection = new SelectionService(_messages);
        _card = new CardService(selection, _sentences, _clipboard, _brain, _options.CardLifetime, idle, Notify)
        {
            Sample = _options.Sample,
            Theme = _options.Theme,
        };
        _panel = new ClipPanelService(selection, _clipboard, _brain, _card, _options.CardLifetime, idle, Notify) { Theme = _options.Theme };

        _hotkeys = new Hotkeys(_messages);
        var shortcuts = new ShortcutService(
            _hotkeys,
            _brain.Settings,
            new() { [Shortcut.Lookup] = LookupHotkey, [Shortcut.Write] = WriteHotkey, [Shortcut.Clipboard] = ClipboardHotkey },
            kind =>
            {
                if (kind == Shortcut.Clipboard)
                {
                    _panel.Toggle();
                }
                else
                {
                    _ = _card.OpenAsync(kind == Shortcut.Write ? CardMode.Write : CardMode.Lookup);
                }
            });

        _main = new MainWindowService(_brain, idle, shortcuts, _clipboard)
        {
            Theme = _options.Theme,
            Sample = _options.Page is null ? null : _options.Sample,
            Startup = startup,
            DeleteData = DeleteDataAndQuit,
        };
        _card.SettingsOpener = section => _main.Show("settings", section);
        _panel.SettingsOpener = section => _main.Show("settings", section);

        _tray = new TrayService(_messages, _clipboard, startup, shortcuts, () => _main.Show(), () => _panel.Toggle(fromTray: true), _card.ShowSample, Quit);
        var taken = shortcuts.All.Where(s => !s.Registered).Select(s => s.Keys.ToString()).ToList();
        if (taken.Count > 0)
        {
            Notify(Localization.Get("Note_HotkeyTaken"), Localization.Format("Note_HotkeyTakenBody", string.Join(Localization.Get("Note_And"), taken)));
        }

        // A second start of Lamha lands here (on a background thread): it opens the main window.
        _instance.Activated += (_, _) => queue.TryEnqueue(() => _main.Show());

        if (_options.Page is { } page)
        {
            _main.Show(page);
        }
        else if (_options.Panel)
        {
            _panel.Toggle(fromTray: true);
        }

        if (_options.Measure)
        {
            MeasureTimeline.Start(queue, _card, _main, Quit);
        }
        else if (_brain.Settings.Current.UseContext)
        {
            _sentences.Warm(); // so the first lookup doesn't wait for UI Automation to start
        }

        idle.Schedule(); // start-up's own memory, once the tray is ready
    }

    private void Notify(string title, string body) => _tray?.Notify(title, body);

    /// <summary>
    /// Settings → Delete my data: the API keys, then, once everything is closed, the data folder (the database, the
    /// clipboard history, the rates, the log), and Lamha quits; the next start begins fresh. With <c>--profile</c>,
    /// only that folder.
    /// </summary>
    private void DeleteDataAndQuit()
    {
        AppLog.Info("delete my data");
        foreach (var name in new[] { "claude", "gemini", "openai", "custom" })
        {
            _brain.Secrets.Set(name, null);
        }

        Close();
        try
        {
            Directory.Delete(_brain.DataFolder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nothing to log to any more: what's left is gone at the next delete or uninstall's choice.
        }

        Exit();
    }

    private void Quit()
    {
        AppLog.Info("quit");
        Close();
        Exit();
    }

    /// <summary>Everything Lamha holds, closed: windows, shortcuts, the clipboard listener, the tray, the database.</summary>
    private void Close()
    {
        _main?.Dispose();
        _panel?.Dispose();
        _card?.Dispose();
        _hotkeys?.Dispose();
        _clipboard?.Dispose();
        _accent?.Dispose();
        _sentences?.Dispose();
        _tray?.Dispose();
        _messages?.Dispose();
        _brain.Dispose();
    }
}
