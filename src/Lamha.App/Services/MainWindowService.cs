using Lamha.App.ViewModels.Main;
using Lamha.App.Windows;
using Lamha.Core.Settings;
using Lamha.Windows.Shell;

namespace Lamha.App.Services;

/// <summary>
/// Opens the main window, or brings it to the front if it's open. Closing it ends it, so its memory goes back to
/// Windows (the memory rules: only the tray icon, the shortcuts and the clipboard listener stay).
/// </summary>
/// <param name="brain">Lamha's brain, for the pages.</param>
/// <param name="idle">Told when the window opens and closes.</param>
/// <param name="lookupKeys">The quick-lookup shortcut ("Ctrl+Alt+L"), which the Translate page shows.</param>
/// <param name="writeKeys">The writing tools' shortcut ("Ctrl+Alt+W"), which Settings → Writing tools shows.</param>
/// <param name="clipboard">Clipboard history's switch and pause, for the Clipboard page.</param>
public sealed partial class MainWindowService(Brain brain, IdleMemory idle, string lookupKeys, string writeKeys, ClipboardService clipboard) : IDisposable
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

    /// <summary>Start with Windows, for Settings → Lamha (the tray's switch too).</summary>
    public StartWithWindows? Startup { get; init; }

    public bool IsOpen => _window is not null;

    /// <summary>
    /// The window, on the page <paramref name="page"/> ("translate", "settings"…); for Settings, at the category
    /// <paramref name="section"/> ("writing"…) when one is given (an error's Settings button).
    /// </summary>
    public void Show(string page = "translate", string section = "")
    {
        try
        {
            if (_window is { } open)
            {
                open.Go(page, section);
                open.BringToFront();
                return;
            }

            var started = Environment.TickCount64;
            var window = _window = new MainWindow(new MainViewModel(), brain, lookupKeys, writeKeys, clipboard);
            window.UseTheme(Theme ?? brain.Settings.Current.Theme);
            window.Startup = Startup;
            window.Go(page, section);
            // Settings → Appearance applies to the open window at once (the card reads it each time it opens).
            void Apply(object? sender, LamhaSettings settings) => window.DispatcherQueue.TryEnqueue(() =>
            {
                window.UseTheme(Theme ?? settings.Theme);
                window.UseMica(settings.WindowMica);
                Animations.Refresh(settings);
            });
            brain.Settings.Changed += Apply;
            window.Closed += (_, _) =>
            {
                brain.Settings.Changed -= Apply;
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
