using Lamha.App.ViewModels.FirstRun;
using Lamha.App.Windows;
using Lamha.Core.Dictionary;
using Lamha.Core.Import;
using Lamha.Data.Import;
using Lamha.Windows.Security;
using Lamha.Windows.Shell;
using Windows.Graphics;

namespace Lamha.App.Services;

/// <summary>
/// First run: the window at the first start (phase 1's design), and what its steps change. Lamha 2's
/// <c>installed.flag</c> in main.js marks it done, in the data folder, so Delete my data brings it back and a backup
/// doesn't carry it. Lamha 2's data, when its folder is found, is read once and offered after the language.
/// </summary>
/// <param name="brain">Lamha's brain: the settings, the writing tools' setup.</param>
/// <param name="shortcuts">The shortcuts, named on Try it and Ready.</param>
/// <param name="clipboard">Ready's clipboard history switch.</param>
/// <param name="idle">Told when the window opens and closes.</param>
/// <param name="notify">A notification (title, body): where Lamha's icon is, once first run ends.</param>
public sealed partial class FirstRunService(Brain brain, ShortcutService shortcuts, ClipboardService clipboard, IdleMemory idle, Action<string, string> notify)
    : IFirstRunHost, IDisposable
{
    /// <summary>Lamha 2's file: its first start wrote it, and its presence meant first run was done.</summary>
    private const string FlagName = "installed.flag";

    private FirstRunWindow? _window;
    private bool _lamha2Read;
    private Lamha2Data? _lamha2;
    private Lamha2Summary? _moved;

    /// <summary><c>--theme</c>: the window's theme for this run instead of the setting.</summary>
    public string? Theme { get; init; }

    /// <summary>Start with Windows, set by Ready's switch; null with <c>--profile</c>, which leaves the real entry alone.</summary>
    public StartWithWindows? Startup { get; init; }

    /// <summary>Opens Settings at a category ("keys"…) in the main window: Customise shortcuts.</summary>
    public Action<string>? SettingsOpener { get; init; }

    /// <summary>The interface language changed (step 1): the tray's words, and the card made afresh in it.</summary>
    public Action? LanguageChanged { get; init; }

    /// <summary><c>--lamha2</c>: Lamha 2's data read from that folder instead of %APPDATA%\Lamha (a copy, to try the move).</summary>
    public string? Lamha2Path { get; init; }

    public bool IsOpen => _window is not null;

    /// <summary>
    /// Whether first run is due: Lamha's data folder has no flag yet. A folder whose database was there before this
    /// start (a beta from before first run existed) is marked done without showing it.
    /// </summary>
    /// <param name="dataFolder">Lamha's data folder (or a <c>--profile</c> folder).</param>
    /// <param name="hadDatabase">Whether lamha.db existed before Lamha opened it this time.</param>
    public static bool IsDue(string dataFolder, bool hadDatabase)
    {
        if (File.Exists(Path.Combine(dataFolder, FlagName)))
        {
            return false;
        }

        if (hadDatabase)
        {
            MarkDone(dataFolder);
            return false;
        }

        return true;
    }

    /// <summary>The window, at <paramref name="step"/>; where <paramref name="at"/> was when it's made again in another language.</summary>
    public void Show(FirstRunStep step = FirstRunStep.Language, PointInt32? at = null)
    {
        try
        {
            if (_window is { } open)
            {
                open.BringToFront();
                return;
            }

            if (!_lamha2Read)
            {
                _lamha2Read = true;
                _lamha2 = ReadLamha2();
            }

            var settings = brain.Settings.Current;
            var model = new FirstRunViewModel(brain, shortcuts, this, step, startWithWindows: true, _lamha2, _moved);
            var window = _window = new FirstRunWindow(model, settings.WindowMica);
            window.UseTheme(Theme ?? settings.Theme);
            window.Closed += (_, _) =>
            {
                if (_window == window)
                {
                    _window = null;
                    idle.WindowClosed();
                    Done();
                }
            };
            idle.WindowOpened();
            var cursor = Screens.Cursor;
            window.Show(Screens.WorkAreaAt(cursor), Screens.ScaleAt(cursor), at);
            AppLog.Info($"first run shown ({step})");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // From start-up, the tray and a second start: an exception there would end Lamha without a word in the log.
            AppLog.Error("first run", ex);
        }
    }

    /// <summary>The window to the front, when it's open (the tray icon and a second start while first run is on).</summary>
    public bool BringToFront()
    {
        _window?.BringToFront();
        return _window is not null;
    }

    /// <summary>
    /// The lookup shortcut, while the window is in front on Try it: the word selected in its sentence, read from the
    /// window itself (its own selection isn't another program's). Null otherwise: the shortcut reads the program in front.
    /// </summary>
    public (string Text, WordContext? Context)? Selection() => _window is { IsInFront: true } window ? window.Selection() : null;

    /// <summary>The card opened on the sample sentence: "That's it" replaces the instruction.</summary>
    public void Tried()
    {
        if (_window is { } window)
        {
            window.ViewModel.Tried = true;
        }
    }

    public void Dispose()
    {
        var window = _window;
        _window = null; // quitting during first run doesn't end it: it shows again at the next start
        window?.Close();
    }

    void IFirstRunHost.ChangeLanguage(string language)
    {
        if (_window is not { } old)
        {
            return;
        }

        brain.Settings.Update(s => s with { UiLang = language });
        Localization.Apply(language);
        LanguageChanged?.Invoke();
        AppLog.Info($"first run: interface language {language}");

        // Every word and the window's direction are set when it's made: a new one, where this one was. Queued: the
        // choice is still being made in the old window.
        old.DispatcherQueue.TryEnqueue(() =>
        {
            _window = null;
            Show(FirstRunStep.Language, old.Position);
            old.Close();
            idle.WindowClosed();
        });
    }

    void IFirstRunHost.OpenShortcuts() => SettingsOpener?.Invoke("keys");

    void IFirstRunHost.Moved(Lamha2Summary summary)
    {
        _moved = summary;
        var settings = brain.Settings.Current;
        _window?.UseTheme(Theme ?? settings.Theme);
        Animations.Refresh(settings);
    }

    void IFirstRunHost.Finish(bool startWithWindows, bool clipboardOn)
    {
        if (Startup is { } startup && startup.IsOn != startWithWindows)
        {
            startup.Set(startWithWindows);
        }

        if (clipboardOn != clipboard.IsOn)
        {
            clipboard.SetOn(clipboardOn);
        }

        AppLog.Info($"first run: start with Windows {(startWithWindows ? "on" : "off")}{(Startup is null ? " (not set: another profile)" : "")}, clipboard history {(clipboardOn ? "on" : "off")}");
        _window?.Close();
    }

    /// <summary>The window closed (Start, or its close button): done, and where Lamha's icon is, once.</summary>
    private void Done()
    {
        MarkDone(brain.DataFolder);
        AppLog.Info("first run done");
        notify(Localization.Get("Note_ByTheClock"), Localization.Format("Note_ByTheClockBody", shortcuts.Keys(Shortcut.Lookup).ToString()));
    }

    /// <summary>Lamha 2's data, or null when it has none here (or it can't be read: first run goes on without the step).</summary>
    private Lamha2Data? ReadLamha2()
    {
        try
        {
            var data = new Lamha2Folder(Lamha2Path ?? Lamha2Folder.DefaultPath, Dpapi.UnprotectForUser).Read();
            AppLog.Info(data is null ? "first run: no Lamha 2 data" : $"first run: Lamha 2's data found{(data.KeysLost || data.ClipsLost ? " (some of it unreadable here)" : "")}");
            return data;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppLog.Error("first run: reading Lamha 2's data", ex);
            return null;
        }
    }

    private static void MarkDone(string dataFolder)
    {
        try
        {
            Directory.CreateDirectory(dataFolder);
            File.WriteAllText(Path.Combine(dataFolder, FlagName), DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("first run flag", ex);
        }
    }
}
