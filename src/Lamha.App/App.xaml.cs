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

        _card = new CardService(new SelectionService(_messages), _sentences, _clipboard, _brain, _options.CardLifetime, idle, Notify)
        {
            Sample = _options.Sample,
            Theme = _options.Theme,
        };

        _main = new MainWindowService(_brain, idle, LookupHotkey.ToString(), WriteHotkey.ToString(), _clipboard)
        {
            Theme = _options.Theme,
            Sample = _options.Page is null ? null : _options.Sample,
        };
        _card.SettingsOpener = section => _main.Show("settings", section);

        _hotkeys = new Hotkeys(_messages);
        ShortcutInfo[] shortcuts =
        [
            Register(LookupHotkey, "Tray_ShortcutLookup", CardMode.Lookup),
            Register(WriteHotkey, "Tray_ShortcutWrite", CardMode.Write),
        ];

        _tray = new TrayService(_messages, _clipboard, startup, shortcuts, () => _main.Show(), _card.ShowSample, Quit);
        var taken = shortcuts.Where(s => !s.Registered).Select(s => s.Keys.ToString()).ToList();
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

    private ShortcutInfo Register(Hotkey keys, string label, CardMode mode)
    {
        var registered = _hotkeys!.Register(keys, () => _ = _card!.OpenAsync(mode));
        AppLog.Info($"hotkey {keys}: {(registered ? "registered" : "taken by another program")}");
        return new ShortcutInfo(keys, label, registered);
    }

    private void Notify(string title, string body) => _tray?.Notify(title, body);

    private void Quit()
    {
        AppLog.Info("quit");
        _main?.Dispose();
        _card?.Dispose();
        _hotkeys?.Dispose();
        _clipboard?.Dispose();
        _accent?.Dispose();
        _sentences?.Dispose();
        _tray?.Dispose();
        _messages?.Dispose();
        _brain.Dispose();
        Exit();
    }
}
