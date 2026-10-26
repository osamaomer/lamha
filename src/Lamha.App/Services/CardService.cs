using Lamha.App.ViewModels.Card;
using Lamha.App.Windows;
using Lamha.Core;
using Lamha.Core.Dictionary;
using Lamha.Core.Text;
using Lamha.Windows.Selection;
using Lamha.Windows.Shell;

namespace Lamha.App.Services;

/// <summary>
/// Opens the card on what's selected in the program in front, and carries out what the card asks (Replace / Insert,
/// Copy, links, Close). Lamha 2's onHotkey / showCard / card-replace in main.js. <c>notify</c> shows a notification
/// (title, body) when Replace couldn't paste.
/// </summary>
public sealed partial class CardService(
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

    /// <summary>The hint ("Select some text first") goes after this long (Lamha 2's 1600 ms).</summary>
    private static readonly TimeSpan HintTime = TimeSpan.FromMilliseconds(1600);

    /// <summary>The longest the shortcut waits for the sentence around a word (Lamha 2's 700 ms).</summary>
    private static readonly TimeSpan SentenceTimeout = TimeSpan.FromMilliseconds(700);

    private CardWindow? _window;
    private bool _windowSolid;
    private SelectionCapture? _source;
    private long _shownAt;
    private bool _busy;
    private bool _visible;
    private int _shown;

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

    /// <summary>
    /// <c>--sample</c>: the sample card's text, "write:" and the text for the writing tools, or "error:" and an error
    /// code for the error the sample text's lookup would show.
    /// </summary>
    public string? Sample { get; init; }

    /// <summary><c>--theme</c>: the card's theme for this run instead of the setting.</summary>
    public string? Theme { get; init; }

    /// <summary>The card with sample text and nowhere to paste (<c>--sample</c> at start, and tools/Measure).</summary>
    public void ShowSample()
    {
        var sample = Sample ?? Localization.Get("Card_SampleText");
        var write = sample.StartsWith("write:", StringComparison.Ordinal);
        var error = sample.StartsWith("error:", StringComparison.Ordinal) ? sample["error:".Length..] : null;
        try
        {
            var text = write ? sample["write:".Length..] : error is null ? sample : Localization.Get("Card_SampleText");
            Show(new SelectionCapture(0, text, IsTerminal: false), write ? CardMode.Write : CardMode.Lookup, null);
            if (error is not null)
            {
                _window?.ViewModel.ShowError(new LamhaException(error));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Called from the tray's menu and a timer: an exception there would end Lamha without a word in the log.
            AppLog.Error("sample card", ex);
        }
    }

    /// <summary>
    /// The card on <paramref name="text"/>, as the lookup shortcut opens it: the clipboard panel's Look up (Lamha 2's
    /// <c>lookupClip</c>), and first run's sample sentence (with the sentence around the word). Its Replace pastes into
    /// <paramref name="window"/>, the program the panel was opened over; 0 for nowhere.
    /// </summary>
    public void LookUp(string text, nint window, WordContext? context = null)
    {
        try
        {
            Show(new SelectionCapture(window, text, IsTerminal: window != 0 && ForegroundWindow.IsTerminal(window)), CardMode.Lookup, context);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppLog.Error("card from Lamha's own text", ex);
        }
    }

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

    /// <summary>Opens Settings at a category ("writing"…) in the main window; set once the main window's service exists.</summary>
    public Action<string>? SettingsOpener { get; set; }

    public bool CanOpenSettings => SettingsOpener is not null;

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

    void ICardHost.OpenLink(Uri uri) => _ = global::Windows.System.Launcher.LaunchUriAsync(uri);

    /// <summary>The card's Settings (its bar, or an error's button): the card goes, Settings opens at the category.</summary>
    void ICardHost.OpenSettings(string section)
    {
        Hide();
        SettingsOpener?.Invoke(section);
    }

    void ICardHost.Close() => Hide();

    void ICardHost.Announce(string text) => _window?.Announce(text);

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

        // The card's background is chosen when its window is made (Acrylic or solid): a change makes a new one.
        var solid = brain.Settings.Current.CardSolid;
        if (_window is not null && solid != _windowSolid)
        {
            Dispose();
        }

        Animations.Refresh(brain.Settings.Current);
        var created = _window is null;
        var window = _window ??= Create(solid);
        window.UseTheme(Theme ?? brain.Settings.Current.Theme);
        window.ViewModel.Load(capture, mode, context);

        var cursor = Screens.Cursor;
        _shownAt = Environment.TickCount64;
        if (!_visible)
        {
            _visible = true;
            idle.WindowOpened();
        }

        window.ShowAt(cursor, Screens.WorkAreaAt(cursor), Screens.ScaleAt(cursor));
        AppLog.Info($"card shown in {Environment.TickCount64 - started} ms ({(created ? "new window" : "kept window")}, {mode}, {capture.Text.Length} characters)");
        if (window.ViewModel.IsHint)
        {
            _ = HideHintAsync(++_shown);
        }
        else
        {
            ++_shown;
        }
    }

    /// <summary>The hint goes by itself, unless something else opened the card meanwhile.</summary>
    private async Task HideHintAsync(int shown)
    {
        await Task.Delay(HintTime).ConfigureAwait(true);
        if (shown == _shown && _window?.ViewModel.IsHint == true)
        {
            Hide();
        }
    }

    private CardWindow Create(bool solid)
    {
        _windowSolid = solid;
        var window = new CardWindow(new CardViewModel(brain, this), solid);
        window.Deactivated += () =>
        {
            // A pinned card stays while the user works elsewhere.
            if (Environment.TickCount64 - _shownAt > DeactivateGraceMs && !window.ViewModel.IsPinned && !window.ViewModel.IsHint)
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
