using Lamha.App.ViewModels;
using Lamha.App.Windows;
using Lamha.Core.Dictionary;
using Lamha.Core.Layout;
using Lamha.Core.Text;
using Lamha.Core.Translation;
using Lamha.Windows.Selection;
using Lamha.Windows.Shell;

namespace Lamha.App.Services;

/// <summary>
/// Opens the card on what's selected in the program in front, and carries out what the card asks (Replace / Insert,
/// Copy, Close). Lamha 2's onHotkey / showCard / card-replace in main.js. <c>notify</c> shows a notification (title,
/// body) when Replace couldn't paste.
/// </summary>
public sealed class CardService(
    SelectionService selection,
    SentenceReader sentences,
    ClipboardService clipboard,
    Brain brain,
    CardLifetime lifetime,
    IdleMemory idle,
    Action<string, string> notify) : ICardHost, IDisposable
{
    /// <summary>Clicking elsewhere this soon after the card opened doesn't close it (the shortcut's own key-up).</summary>
    private const long DeactivateGraceMs = 400;

    /// <summary>The longest the shortcut waits for the sentence around a word (Lamha 2's 700 ms).</summary>
    private static readonly TimeSpan SentenceTimeout = TimeSpan.FromMilliseconds(700);

    private CardWindow? _window;
    private SelectionCapture? _source;
    private long _shownAt;
    private bool _busy;
    private bool _visible;

    /// <summary>
    /// A shortcut: read the selection first (the user's program must still be in front), and for a word the sentence
    /// around it, then open the card.
    /// </summary>
    public async Task OpenAsync(CardMode mode)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            // Lamha's own Ctrl+C isn't a copy of the user's, so clipboard history doesn't record it.
            var capture = await clipboard.Monitor.SuppressAsync(selection.CaptureAsync).ConfigureAwait(true);
            var text = capture.Text.Trim();
            WordContext? context = null;
            if (mode == CardMode.Lookup && text.Length > 0 && TextKind.IsLookupCandidate(text) && brain.Settings.Current.UseContext)
            {
                // Still before the card takes the focus: UI Automation reads the program's focused text.
                context = await sentences.AroundAsync(capture.Window, text, SentenceTimeout).ConfigureAwait(true);
                AppLog.Info($"sentence around the word: {(context is null ? "not read" : $"{context.Before.Length} + {context.After.Length} characters")}");
            }

            Show(capture, mode, context);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppLog.Error("card shortcut", ex);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>The card with sample text and nowhere to paste (the tray's test item, and tools/Measure).</summary>
    public void ShowSample() => Show(new SelectionCapture(0, Localization.Get("Proto_SampleText"), IsTerminal: false), CardMode.Lookup, null);

    public void Hide()
    {
        if (_window is null || !_visible)
        {
            return;
        }

        _visible = false;
        _window.ViewModel.Unload();
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

    async Task<string> ICardHost.LookupAsync(string text, WordContext? context, CancellationToken cancel)
    {
        var started = Environment.TickCount64;
        var result = await brain.Lookup.LookupAsync(text, new LookupOptions { Context = context }, cancel).ConfigureAwait(true);
        AppLog.Info($"lookup answered in {Environment.TickCount64 - started} ms ({result.From}, {text.Length} characters{(brain.HasDictionary ? "" : ", no dictionary file")})");
        return result.Translation;
    }

    async Task ICardHost.ReplaceAsync(string text)
    {
        var source = _source;
        Hide();
        if (source is null || !await selection.PasteIntoAsync(source.Window, text).ConfigureAwait(true))
        {
            // The program went away or refused the focus: leave the text on the clipboard instead, and say so.
            selection.Copy(text);
            notify(Localization.Get("Note_ReplaceFailed"), Localization.Get("Note_ReplaceFailedBody"));
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

    private void Show(SelectionCapture capture, CardMode mode, WordContext? context)
    {
        var started = Environment.TickCount64;
        _source = capture;
        var created = _window is null;
        var window = _window ??= Create();
        window.ViewModel.Load(capture, mode, context);

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
        AppLog.Info($"card shown in {Environment.TickCount64 - started} ms ({(created ? "new window" : "kept window")}, {mode}, {capture.Text.Length} characters)");
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
