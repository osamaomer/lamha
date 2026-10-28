using Lamha.App.ViewModels.Main;
using Lamha.App.Windows;
using Lamha.Core.Settings;
using Lamha.Windows.Shell;

namespace Lamha.App.Services;

/// <summary>
/// Opens the main window, or brings it to the front if it's open. Windows' close hides it, and the next open shows the
/// same window again (decision 0021): WinUI never frees a closed window, so a new one at every open added a few MB
/// each time. While it's hidden its working set goes back to Windows (IdleMemory); it ends when Lamha does, or when
/// the interface language changes.
/// </summary>
/// <param name="brain">Lamha's brain, for the pages.</param>
/// <param name="idle">Told when the window opens and closes.</param>
/// <param name="shortcuts">The shortcuts: their keys as the Translate page and Writing tools name them, and Settings' changes.</param>
/// <param name="clipboard">Clipboard history's switch and pause, for the Clipboard page.</param>
public sealed partial class MainWindowService(Brain brain, IdleMemory idle, ShortcutService shortcuts, ClipboardService clipboard) : IDisposable
{
    private MainWindow? _window;

    /// <summary>The window is shown (not hidden): IdleMemory counts it open.</summary>
    private bool _shown;
    private string? _sample;

    /// <summary>The window is being made (<see cref="Show"/> isn't answered again meanwhile).</summary>
    private bool _making;

    /// <summary><c>--theme</c>: the window's theme for this run instead of the setting.</summary>
    public string? Theme { get; init; }

    /// <summary><c>--page=translate --sample=text</c>: the text looked up in the box the first time the window opens.</summary>
    public string? Sample
    {
        get => _sample;
        init => _sample = value;
    }

    /// <summary>Start with Windows, for Settings → Lamha (the tray's switch too).</summary>
    public StartWithWindows? Startup { get; init; }

    /// <summary>The updates, for Settings → Lamha.</summary>
    public UpdateService? Updates { get; init; }

    /// <summary>Settings → Delete my data, after its question: the app deletes everything and quits.</summary>
    public Action? DeleteData { get; init; }

    /// <summary>Translate's mini translator: opens it with the box's text (the app closes this window).</summary>
    public Action<string>? Mini { get; set; }

    /// <summary>Shown, not hidden.</summary>
    public bool IsOpen => _window is not null && _shown;

    /// <summary>The accent changed (AccentService): the open window's Windows controls take it.</summary>
    public void AccentChanged() => _window?.DispatcherQueue.TryEnqueue(() => _window?.RefreshAccent(Theme ?? brain.Settings.Current.Theme));

    /// <summary>Open and in front (Lamha 2's <c>isVisible() &amp;&amp; isFocused()</c>): no review reminder then.</summary>
    public bool IsInUse => _window is { } window && ForegroundWindow.Current == window.Handle;

    /// <summary>
    /// The window, on the page <paramref name="page"/> ("translate", "settings"…); for Settings, at the category
    /// <paramref name="section"/> ("writing"…) when one is given (an error's Settings button).
    /// </summary>
    public void Show(string page = "translate", string section = "")
    {
        try
        {
            if (_making)
            {
                return; // asked again while it's being made: making a window lets Windows' messages through (a second click)
            }

            if (_window is { } open && _shown)
            {
                open.Go(page, section);
                open.BringToFront();
                return;
            }

            if (_window is { } hidden)
            {
                var again = Environment.TickCount64;
                Animations.Refresh(brain.Settings.Current);
                hidden.UseTheme(Theme ?? brain.Settings.Current.Theme);
                _shown = true;
                idle.WindowOpened();
                hidden.ShowAgain(page, section);
                AppLog.Info($"main window shown again in {Environment.TickCount64 - again} ms ({page})");
                return;
            }

            _making = true;
            try
            {
                Make(page, section);
            }
            finally
            {
                _making = false;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Called from the tray and a second start: an exception there would end Lamha without a word in the log.
            AppLog.Error("main window", ex);
        }
    }

    /// <summary>The window made, on the page asked for, and shown in the middle of the screen with the mouse.</summary>
    private void Make(string page, string section)
    {
        var started = Environment.TickCount64;
        var window = _window = new MainWindow(new MainViewModel(), brain, shortcuts, clipboard);
        window.UseTheme(Theme ?? brain.Settings.Current.Theme);
        window.Startup = Startup;
        window.Updates = Updates;
        window.DeleteData = DeleteData;
        window.Mini = Mini;
        window.Go(page, section);
        window.StartBackHere(); // opened on a page, it has nothing behind it (the Translate page it was built on)
        // Settings → Appearance applies to the open window at once (the card reads it each time it opens).
        void Apply(object? sender, LamhaSettings settings) => window.DispatcherQueue.TryEnqueue(() =>
        {
            window.UseTheme(Theme ?? settings.Theme);
            window.UseMica(settings.WindowMica);
            Animations.Refresh(settings);
        });
        brain.Settings.Changed += Apply;
        void OutOfSight()
        {
            if (_window == window && _shown)
            {
                _shown = false;
                idle.WindowClosed();
            }
        }

        window.Hidden += OutOfSight;
        window.Closed += (_, _) =>
        {
            brain.Settings.Changed -= Apply;
            window.Hidden -= OutOfSight;
            OutOfSight();
            if (_window == window)
            {
                _window = null;
            }
        };
        _shown = true;
        idle.WindowOpened();
        var cursor = Screens.Cursor;
        window.ShowCentred(Screens.WorkAreaAt(cursor), Screens.ScaleAt(cursor));
        if (_sample is { Length: > 0 } sample)
        {
            _sample = null;
            window.TypeSample(sample);
        }

        AppLog.Info($"main window shown in {Environment.TickCount64 - started} ms ({page})");
    }

    /// <summary>The Translate page with <paramref name="text"/> looked up in its box (the mini translator's Back to Lamha).</summary>
    public void ShowTranslate(string text)
    {
        Show("translate");
        if (text.Length > 0)
        {
            _window?.Translate(text);
        }
    }

    /// <summary>Out of sight, as Windows' close does (the mini translator takes its place).</summary>
    public void Hide() => _window?.Hide();

    /// <summary>
    /// The window really closes: the interface language changed (it's made again in the new one), or Lamha is ending.
    /// </summary>
    public void End() => _window?.End();

    public void Dispose() => End();
}
