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
/// (title, body) when Replace couldn't paste. A pinned card stays where it is: the next lookup opens another card, and
/// the pinned one's window is kept for later cards once it's closed (WinUI never frees a window: decision 0021).
/// </summary>
public sealed partial class CardService(
    SelectionService selection,
    SentenceReader sentences,
    ClipboardService clipboard,
    Brain brain,
    CardLifetime lifetime,
    IdleMemory idle,
    Action<string, string> notify) : IDisposable
{
    /// <summary>Clicking elsewhere this soon after the card opened doesn't close it (the shortcut's own key-up).</summary>
    private const long DeactivateGraceMs = 400;

    /// <summary>The hint ("Select some text first") goes after this long (Lamha 2's 1600 ms).</summary>
    private static readonly TimeSpan HintTime = TimeSpan.FromMilliseconds(1600);

    /// <summary>The longest the shortcut waits for the sentence around a word (Lamha 2's 700 ms).</summary>
    private static readonly TimeSpan SentenceTimeout = TimeSpan.FromMilliseconds(700);

    /// <summary>The cards pinned and still open, besides the one the shortcuts use.</summary>
    private readonly List<Card> _pinned = [];

    /// <summary>Cards closed after being pinned, hidden, for the next lookup's card.</summary>
    private readonly List<Card> _spare = [];

    /// <summary>The card the shortcuts open (and the hint).</summary>
    private Card? _card;
    private bool _busy;
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
                _card?.Window.ViewModel.ShowError(new LamhaException(error));
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

    /// <summary>The shortcuts' card goes (tools/Measure); a pinned card stays.</summary>
    public void Hide()
    {
        if (_card is { } card)
        {
            Hide(card);
        }
    }

    /// <summary>Opens Settings at a category ("writing"…) in the main window; set once the main window's service exists.</summary>
    public Action<string>? SettingsOpener { get; set; }

    public bool CanOpenSettings => SettingsOpener is not null;

    public void Dispose()
    {
        var cards = new List<Card>(_pinned.Concat(_spare));
        if (_card is not null)
        {
            cards.Add(_card);
        }

        _card = null;
        _pinned.Clear();
        _spare.Clear();
        foreach (var card in cards)
        {
            Close(card);
        }
    }

    /// <summary>A card goes; it fades out first unless <paramref name="fade"/> is false (Replace).</summary>
    private void Hide(Card card, bool fade = true)
    {
        if (!card.Visible)
        {
            return;
        }

        card.Visible = false;
        idle.WindowClosed();
        var wasPinned = _pinned.Remove(card);
        if (lifetime == CardLifetime.Keep)
        {
            if (wasPinned)
            {
                _spare.Add(card);
            }

            // What was looked up goes from memory once the card is gone (not if it was shown again meanwhile).
            card.Window.Hide(fade, card.Window.ViewModel.Unload);
        }
        else
        {
            if (card == _card)
            {
                _card = null;
            }

            card.Window.ViewModel.Unload();
            Close(card);
        }
    }

    private void Copy(string text) => selection.Copy(text);

    private async Task ReplaceAsync(Card card, string text)
    {
        var source = card.Source;
        Hide(card, fade: false);
        if (source is null || !await selection.PasteIntoAsync(source.Window, text).ConfigureAwait(true))
        {
            // The program went away or refused the focus: leave the text on the clipboard instead, and say so.
            selection.Copy(text);
            notify(Localization.Get("Note_ReplaceFailed"), Localization.Get("Note_ReplaceFailedBody"));
        }
    }

    /// <summary>
    /// The card's Settings (its bar, or an error's button): Settings opens at the category, and the card stays over it
    /// (it would go as the main window takes the focus); once it's had the focus again, it goes as usual.
    /// </summary>
    private void OpenSettings(Card card, string section)
    {
        card.StayOnce = true;
        SettingsOpener?.Invoke(section);
    }

    private void Show(SelectionCapture capture, CardMode mode, WordContext? context)
    {
        var started = Environment.TickCount64;

        // A pinned card stays as it is: this lookup gets another card.
        if (_card is { Visible: true } current && current.Window.ViewModel.IsPinned)
        {
            _pinned.Add(current);
            _card = null;
        }

        // The card's background is chosen when its window is made (Acrylic or solid): a change makes new ones.
        var solid = brain.Settings.Current.CardSolid;
        if (_card is not null && _card.Solid != solid)
        {
            Close(_card);
            _card = null;
        }

        foreach (var old in _spare.Where(c => c.Solid != solid).ToList())
        {
            _spare.Remove(old);
            Close(old);
        }

        Animations.Refresh(brain.Settings.Current);
        var created = _card is null && _spare.Count == 0;
        if (_card is null)
        {
            _card = _spare.Count > 0 ? _spare[^1] : Create(solid);
            _spare.Remove(_card);
        }

        var card = _card;
        card.Source = capture;
        card.StayOnce = false;
        card.Window.UseTheme(Theme ?? brain.Settings.Current.Theme);
        card.Window.ViewModel.Load(capture, mode, context);

        var cursor = Screens.Cursor;
        card.ShownAt = Environment.TickCount64;
        if (!card.Visible)
        {
            card.Visible = true;
            idle.WindowOpened();
        }

        card.Window.ShowAt(cursor, Screens.WorkAreaAt(cursor), Screens.ScaleAt(cursor));
        AppLog.Info($"card shown in {Environment.TickCount64 - started} ms ({(created ? "new window" : "kept window")}, {mode}, {capture.Text.Length} characters, {_pinned.Count} pinned)");
        if (card.Window.ViewModel.IsHint)
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
        if (shown == _shown && _card is { } card && card.Window.ViewModel.IsHint)
        {
            Hide(card);
        }
    }

    /// <summary>Really closes a card's window: Windows' close isn't turned into a hide.</summary>
    private static void Close(Card card)
    {
        card.Closed = true;
        card.Window.Close();
    }

    private Card Create(bool solid)
    {
        var card = new Card(this, brain, solid);
        card.Window.Deactivated += () =>
        {
            // A pinned card stays while the user works elsewhere; so does one that has just opened Settings.
            var view = card.Window.ViewModel;
            if (card.StayOnce)
            {
                card.StayOnce = false;
            }
            else if (Environment.TickCount64 - card.ShownAt > DeactivateGraceMs && !view.IsPinned && !view.IsHint)
            {
                Hide(card);
            }
        };
        card.Window.AppWindow.Closing += (_, args) =>
        {
            // Alt+F4 on a kept card only hides it.
            if (lifetime == CardLifetime.Keep && !card.Closed)
            {
                args.Cancel = true;
                Hide(card);
            }
        };
        return card;
    }

    /// <summary>One card: its window, the text it was opened on (where Replace pastes), and its state.</summary>
    private sealed class Card : ICardHost
    {
        private readonly CardService _owner;

        public Card(CardService owner, Brain brain, bool solid)
        {
            _owner = owner;
            Solid = solid;
            Window = new CardWindow(new CardViewModel(brain, this), solid);
        }

        public CardWindow Window { get; }

        /// <summary>Acrylic or solid: chosen when the window is made.</summary>
        public bool Solid { get; }

        public SelectionCapture? Source { get; set; }

        public long ShownAt { get; set; }

        public bool Visible { get; set; }

        /// <summary>The next loss of focus leaves the card open (Settings, opened from it, takes the focus).</summary>
        public bool StayOnce { get; set; }

        /// <summary>Really closing (Lamha ending, another background).</summary>
        public bool Closed { get; set; }

        public bool CanOpenSettings => _owner.CanOpenSettings;

        public Task ReplaceAsync(string text) => _owner.ReplaceAsync(this, text);

        public void Copy(string text) => _owner.Copy(text);

        public void OpenLink(Uri uri) => _ = global::Windows.System.Launcher.LaunchUriAsync(uri);

        public void OpenSettings(string section) => _owner.OpenSettings(this, section);

        public void Close() => _owner.Hide(this);

        public void Announce(string text) => Window.Announce(text);
    }
}
