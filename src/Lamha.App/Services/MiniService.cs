using Lamha.App.ViewModels.Mini;
using Lamha.App.Windows;
using Lamha.Core.Layout;
using Lamha.Core.Settings;
using Lamha.Windows.Shell;

namespace Lamha.App.Services;

/// <summary>
/// Opens the mini translator, or brings it to the front with a new text. Closing it ends it, so its memory goes back
/// to Windows; Back to Lamha closes it and opens the main window's Translate page with what was in its box.
/// </summary>
/// <param name="brain">Lamha's brain: the lookup, the settings, where it was last.</param>
/// <param name="idle">Told when the window opens and closes.</param>
public sealed partial class MiniService(Brain brain, IdleMemory idle) : IDisposable
{
    private MiniWindow? _window;

    /// <summary><c>--theme</c>: the window's theme for this run instead of the setting.</summary>
    public string? Theme { get; init; }

    /// <summary>Back to Lamha: the main window's Translate page, with the box's text.</summary>
    public Action<string>? BackToMain { get; set; }

    public bool IsOpen => _window is not null;

    /// <summary>The mini translator, with <paramref name="text"/> looked up in its box when there's one.</summary>
    public void Show(string text = "")
    {
        try
        {
            if (_window is { } open)
            {
                if (text.Length > 0)
                {
                    open.ViewModel.Navigate(text);
                }

                open.Activate();
                ForegroundWindow.ForceForeground(open.Handle);
                return;
            }

            var started = Environment.TickCount64;
            Animations.Refresh(brain.Settings.Current);
            var window = _window = new MiniWindow(new MiniViewModel(brain), brain.Settings.Current.WindowMica);
            window.UseTheme(Theme ?? brain.Settings.Current.Theme);
            void Apply(object? sender, LamhaSettings settings) => window.DispatcherQueue.TryEnqueue(() =>
            {
                window.UseTheme(Theme ?? settings.Theme);
                window.UseMica(settings.WindowMica);
            });
            brain.Settings.Changed += Apply;
            window.BackRequested += back =>
            {
                window.Close();
                BackToMain?.Invoke(back);
            };
            window.Closed += (_, _) =>
            {
                brain.Settings.Changed -= Apply;
                if (_window == window)
                {
                    _window = null;
                    SavePlace(window);
                    idle.WindowClosed();
                }
            };
            idle.WindowOpened();
            var cursor = Screens.Cursor;
            window.ShowOn(Screens.WorkAreaAt(cursor), Screens.ScaleAt(cursor), WindowPlace.Parse(brain.State.Get(MiniWindow.PlaceKey)));
            if (text.Length > 0)
            {
                window.ViewModel.Navigate(text);
            }

            AppLog.Info($"mini translator shown in {Environment.TickCount64 - started} ms");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Called from the main window and a second start: an exception there would end Lamha without a word in the log.
            AppLog.Error("mini translator", ex);
        }
    }

    public void Close() => _window?.Close();

    /// <summary>
    /// Lamha is ending: where the window is gets saved now, while the database is open (Windows says Closed only
    /// later, after Quit has closed it), and then it closes.
    /// </summary>
    public void Dispose()
    {
        if (_window is { } window)
        {
            _window = null;
            SavePlace(window);
            window.Close();
        }
    }

    private void SavePlace(MiniWindow window)
    {
        if (window.Place is { } place)
        {
            brain.State.Set(MiniWindow.PlaceKey, place.ToString());
        }
    }
}
