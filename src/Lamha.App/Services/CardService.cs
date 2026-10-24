using Lamha.App.ViewModels;
using Lamha.App.Windows;
using Lamha.Core.Layout;
using Lamha.Windows.Selection;
using Lamha.Windows.Shell;

namespace Lamha.App.Services;

/// <summary>
/// Opens the card on what's selected in the program in front, and carries out what the card asks (Replace, Copy,
/// Close). Lamha 2's onHotkey / showCard / card-replace in main.js.
/// </summary>
public sealed class CardService(SelectionService selection, CardLifetime lifetime, IdleMemory idle) : ICardHost, IDisposable
{
    /// <summary>Clicking elsewhere this soon after the card opened doesn't close it (the shortcut's own key-up).</summary>
    private const long DeactivateGraceMs = 400;

    private CardWindow? _window;
    private SelectionCapture? _source;
    private long _shownAt;
    private bool _busy;
    private bool _visible;

    /// <summary>The shortcut: read the selection first (the user's program must still be in front), then open the card.</summary>
    public async Task LookupSelectionAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            Show(await selection.CaptureAsync().ConfigureAwait(true));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppLog.Error("lookup shortcut", ex);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>The card with sample text and nowhere to paste (the tray's test item, and tools/Measure).</summary>
    public void ShowSample() => Show(new SelectionCapture(0, Localization.Get("Proto_SampleText"), IsTerminal: false));

    public void Hide()
    {
        if (_window is null || !_visible)
        {
            return;
        }

        _visible = false;
        idle.WindowClosed();

        if (lifetime == CardLifetime.Keep)
        {
            _window.Hide();
        }
        else
        {
            _window.Close();
            _window = null;
        }
    }

    async Task ICardHost.ReplaceAsync(string text)
    {
        var source = _source;
        Hide();
        if (source is null || !await selection.PasteIntoAsync(source.Window, text).ConfigureAwait(true))
        {
            // The program went away or refused the focus: leave the text on the clipboard instead.
            selection.Copy(text);
        }
    }

    void ICardHost.Copy(string text) => selection.Copy(text);

    void ICardHost.Close() => Hide();

    public void Dispose()
    {
        var window = _window;
        _window = null; // first, so Closing doesn't turn the close into a hide
        window?.Close();
    }

    private void Show(SelectionCapture capture)
    {
        var started = Environment.TickCount64;
        _source = capture;
        var created = _window is null;
        var window = _window ??= Create();
        window.ViewModel.Load(capture);

        var cursor = Screens.Cursor;
        var workArea = Screens.WorkAreaAt(cursor);
        var (width, height) = CardPlacement.Size(CardWindow.CardWidth, CardWindow.CardHeight, Screens.ScaleAt(cursor), workArea);
        _shownAt = Environment.TickCount64;
        if (!_visible)
        {
            _visible = true;
            idle.WindowOpened();
        }

        window.ShowAt(CardPlacement.Place(cursor, workArea, width, height).Window);
        AppLog.Info($"card shown in {Environment.TickCount64 - started} ms ({(created ? "new window" : "kept window")}, {capture.Text.Length} characters)");
    }

    private CardWindow Create()
    {
        var window = new CardWindow(new CardViewModel(this));
        window.Deactivated += () =>
        {
            if (Environment.TickCount64 - _shownAt > DeactivateGraceMs)
            {
                Hide();
            }
        };
        window.AppWindow.Closing += (_, args) =>
        {
            // Alt+F4 on a kept card only hides it.
            if (lifetime == CardLifetime.Keep && _window is not null)
            {
                args.Cancel = true;
                Hide();
            }
        };
        return window;
    }
}
