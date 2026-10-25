using Lamha.App.ViewModels.Main;
using Lamha.App.Windows;
using Lamha.Windows.Shell;

namespace Lamha.App.Services;

/// <summary>
/// Opens the main window, or brings it to the front if it's open. Closing it ends it, so its memory goes back to
/// Windows (the memory rules: only the tray icon, the shortcuts and the clipboard listener stay).
/// </summary>
/// <param name="brain">Lamha's brain, for the pages.</param>
/// <param name="idle">Told when the window opens and closes.</param>
/// <param name="lookupKeys">The quick-lookup shortcut ("Ctrl+Alt+L"), which the Translate page shows.</param>
/// <param name="clipboard">Clipboard history's switch and pause, for the Clipboard page.</param>
public sealed partial class MainWindowService(Brain brain, IdleMemory idle, string lookupKeys, ClipboardService clipboard) : IDisposable
{
    private MainWindow? _window;
    private string? _sample;

    /// <summary><c>--theme</c>: the window's theme for this run instead of the setting.</summary>
    public string? Theme { get; init; }

    /// <summary><c>--page=translate --sample=text</c>: the text looked up in the box the first time the window opens.</summary>
    public string? Sample
    {
        get => _sample;
        init => _sample = value;
    }

    public bool IsOpen => _window is not null;

    /// <summary>The window, on the page <paramref name="page"/> ("translate", "settings"…).</summary>
    public void Show(string page = "translate")
    {
        try
        {
            if (_window is { } open)
            {
                open.Go(page);
                open.BringToFront();
                return;
            }

            var started = Environment.TickCount64;
            var window = _window = new MainWindow(new MainViewModel(), brain, lookupKeys, clipboard);
            window.UseTheme(Theme ?? brain.Settings.Current.Theme);
            window.Go(page);
            window.Closed += (_, _) =>
            {
                if (_window == window)
                {
                    _window = null;
                    idle.WindowClosed();
                }
            };
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
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Called from the tray and a second start: an exception there would end Lamha without a word in the log.
            AppLog.Error("main window", ex);
        }
    }

    public void Close() => _window?.Close();

    public void Dispose() => Close();
}
