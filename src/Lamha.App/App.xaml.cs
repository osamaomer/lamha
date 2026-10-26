using Lamha.App.Services;
using Lamha.App.ViewModels.Card;
using Lamha.App.ViewModels.FirstRun;
using Lamha.Windows.Input;
using Lamha.Windows.Selection;
using Lamha.Windows.Shell;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using WinRT;
using ILaunchActivatedEventArgs = Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs;

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

    /// <summary>Who Lamha's notifications are from, in Windows (Lamha 2's was com.artworklab.lamha).</summary>
    private const string NotificationAppId = "ArtworkLab.Lamha";

    /// <summary>The scheme of Lamha's own links, which a notification's clicks open (lamha:review).</summary>
    private const string LinkScheme = "lamha";

    private readonly AppOptions _options;
    private readonly AppInstance _instance;
    private MessageWindow? _messages;
    private Hotkeys? _hotkeys;
    private readonly Brain _brain;
    private AccentService? _accent;
    private SentenceReader? _sentences;
    private ClipboardService? _clipboard;
    private TrayService? _tray;
    private NotificationService? _notes;
    private ReviewReminderService? _reminders;
    private CardService? _card;
    private ClipPanelService? _panel;
    private MainWindowService? _main;
    private FirstRunService? _firstRun;
    private readonly bool _hadDatabase;

    public App(AppOptions options, AppInstance instance)
    {
        _options = options;
        _instance = instance;
        // The settings decide the interface language, so they open before anything shows.
        var folder = DataFolder.For(options.Profile);
        _hadDatabase = File.Exists(Path.Combine(folder, "lamha.db")); // a new one means a first start (first run)
        _brain = new Brain(folder, options.Profile is null ? "Lamha/" : "Lamha-profile/");
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
        _notes = new NotificationService(Notifications(_options.Profile), (title, body) => _tray?.Notify(title, body));
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
                else if (kind == Shortcut.Lookup && _firstRun?.Selection() is { } sample)
                {
                    // First run's Try it: the word selected in its own sentence.
                    _card.LookUp(sample.Text, 0, sample.Context);
                    if (sample.Text.Length > 0)
                    {
                        _firstRun.Tried();
                    }
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
        _firstRun = new FirstRunService(_brain, shortcuts, _clipboard, idle, Notify)
        {
            Theme = _options.Theme,
            Startup = _options.Profile is null ? startup : null,
            SettingsOpener = section => _main.Show("settings", section),
            LanguageChanged = () =>
            {
                _tray?.Refresh();
                _card.Dispose(); // made again in the new language at the next lookup
                _panel.Dispose();
            },
        };

        _tray = new TrayService(_messages, _brain.Settings, _clipboard, startup, shortcuts, new TrayActions(OpenMain, OpenPage, () => _panel.Toggle(fromTray: true), Quit));
        _tray.EnabledChanged += () => NotifyTaken(shortcuts);
        _reminders = new ReviewReminderService(_brain, _notes, () => shortcuts.IsOn && !_main.IsInUse, queue) { OpenReview = () => OpenPage("review") };
        _reminders.Changed += _tray.SetWaiting;
        _notes.Register();
        NotifyTaken(shortcuts);
        var fromNotification = _notes.Open(Environment.CommandLine); // a click on a notification that started Lamha
        queue.TryEnqueue(DispatcherQueuePriority.Low, _reminders.Recount); // the count, and a reminder if one is due (Lamha 2's, at start)

        // A second start of Lamha lands here (on a background thread): a notification's link, or the main window.
        _instance.Activated += (_, e) =>
        {
            var arguments = Arguments(e);
            queue.TryEnqueue(() =>
            {
                if (_notes?.Open(arguments) != true)
                {
                    OpenMain();
                }
            });
        };

        if (_options.Page is { } page)
        {
            _main.Show(page);
        }
        else if (_options.Panel)
        {
            _panel.Toggle(fromTray: true);
        }
        else if (_options.Sample is not null && !_options.Measure)
        {
            _card.ShowSample();
        }
        else if (_options.FirstRun is { } step)
        {
            _firstRun.Show(Enum.TryParse<FirstRunStep>(step, ignoreCase: true, out var at) ? at : FirstRunStep.Language);
        }
        else if (!_options.Measure && _options.Sample is null && !fromNotification && FirstRunService.IsDue(_brain.DataFolder, _hadDatabase))
        {
            _firstRun.Show();
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

    /// <summary>
    /// Lamha's name, icon and links for its notifications. With <c>--profile</c>, names of their own (and the profile in
    /// the links' command), so trying something never takes over the installed Lamha's.
    /// </summary>
    internal static NotificationRegistration Notifications(string? profile)
    {
        var command = $"\"{Environment.ProcessPath}\"{(profile is null ? "" : $" --profile=\"{profile}\"")} \"%1\"";
        return profile is null
            ? new NotificationRegistration(NotificationAppId, LinkScheme, command)
            : new NotificationRegistration(NotificationAppId + ".Profile", LinkScheme + "-profile", command);
    }

    /// <summary>A second start's command line (where a notification's link is), or "".</summary>
    private static string Arguments(AppActivationArguments e)
    {
        if (e.Kind != ExtendedActivationKind.Launch || e.Data is null)
        {
            return "";
        }

        try
        {
            return e.Data.As<ILaunchActivatedEventArgs>().Arguments ?? "";
        }
        catch (InvalidCastException)
        {
            return "";
        }
    }

    private void Notify(string title, string body) => _notes?.Show(title, body);

    /// <summary>Shortcut in use: the keys another program holds, at start and when Lamha is enabled again (Lamha 2's).</summary>
    private void NotifyTaken(ShortcutService shortcuts)
    {
        var taken = shortcuts.All.Where(s => s.Taken).Select(s => s.Keys.ToString()).ToList();
        if (taken.Count > 0)
        {
            Notify(Localization.Get("Note_HotkeyTaken"), Localization.Format("Note_HotkeyTakenBody", string.Join(Localization.Get("Note_And"), taken)));
        }
    }

    /// <summary>The tray's Word review, Write and Settings, and a review notification: the main window on that page.</summary>
    private void OpenPage(string page)
    {
        if (page == "review")
        {
            _reminders?.Dismiss();
        }

        _main?.Show(page);
    }

    /// <summary>The tray icon and a second start: the main window, or first run while it's open.</summary>
    private void OpenMain()
    {
        if (_firstRun?.BringToFront() != true)
        {
            _main?.Show();
        }
    }

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
        _firstRun?.Dispose();
        _main?.Dispose();
        _panel?.Dispose();
        _card?.Dispose();
        _hotkeys?.Dispose();
        _clipboard?.Dispose();
        _accent?.Dispose();
        _sentences?.Dispose();
        _reminders?.Dispose();
        _tray?.Dispose();
        _messages?.Dispose();
        _brain.Dispose();
    }
}
