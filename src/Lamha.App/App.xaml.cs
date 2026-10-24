using Lamha.App.Services;
using Lamha.Windows.Input;
using Lamha.Windows.Selection;
using Lamha.Windows.Shell;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Lamha.App;

/// <summary>
/// Lamha lives in the tray: no window at start, only the tray icon, the shortcuts and (later) the clipboard listener.
/// Windows are created when they open. Phase 0: the beta shortcut (Ctrl+Alt+L) and the prototype card.
/// </summary>
public partial class App : Application
{
    /// <summary>The beta's shortcut, so it runs next to Lamha 2 (which owns Alt+Shift+L) until 3.0.</summary>
    private static readonly Hotkey LookupHotkey = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, VirtualKey.L);

    private readonly AppOptions _options;
    private readonly AppInstance _instance;
    private MessageWindow? _messages;
    private Hotkeys? _hotkeys;
    private TrayIcon? _tray;
    private CardService? _card;

    public App(AppOptions options, AppInstance instance)
    {
        _options = options;
        _instance = instance;
        Localization.Apply();
        InitializeComponent();
        // The tray keeps Lamha running with no window open; only Quit ends it.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        UnhandledException += (_, e) => AppLog.Error("unhandled", e.Exception);
        AppLog.Info($"start {typeof(App).Assembly.GetName().Version} card={options.CardLifetime} measure={options.Measure}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var queue = DispatcherQueue.GetForCurrentThread();
        var idle = new IdleMemory(queue);
        _messages = new MessageWindow();
        var selection = new SelectionService(_messages);
        _card = new CardService(selection, _options.CardLifetime, idle);

        _hotkeys = new Hotkeys(_messages);
        var registered = _hotkeys.Register(LookupHotkey, () => _ = _card.LookupSelectionAsync());
        AppLog.Info($"hotkey {LookupHotkey}: {(registered ? "registered" : "taken by another program")}");

        var tooltip = registered
            ? Localization.Get("Proto_Tooltip")
            : Localization.Get("Proto_Tooltip") + " · " + Localization.Format("Proto_HotkeyTaken", LookupHotkey);
        _tray = new TrayIcon(_messages, Path.Combine(AppContext.BaseDirectory, "Assets", "Lamha.ico"), tooltip);
        _tray.SetMenu(
        [
            new TrayMenuItem(Localization.Get("Proto_Sample"), _card.ShowSample),
            TrayMenuItem.Separator,
            new TrayMenuItem(Localization.Get("Tray_Quit"), Quit),
        ]);
        _tray.Invoked += _card.ShowSample;

        // A second start of Lamha lands here (on a background thread). Phase 4: it opens the main window.
        _instance.Activated += (_, _) => queue.TryEnqueue(_card.ShowSample);

        if (_options.Measure)
        {
            MeasureTimeline.Start(queue, _card, Quit);
        }

        idle.Schedule(); // start-up's own memory, once the tray is ready
    }

    private void Quit()
    {
        AppLog.Info("quit");
        _card?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose(); // before exiting, or a dead icon stays in the tray until the mouse passes over it
        _messages?.Dispose();
        Exit();
    }
}
