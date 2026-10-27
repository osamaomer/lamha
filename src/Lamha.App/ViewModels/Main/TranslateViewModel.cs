using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lamha.App.Controls;
using Lamha.App.Services;
using Lamha.App.ViewModels.Card;
using Lamha.Core;
using Lamha.Core.Clipboard;
using Lamha.Core.Review;
using Lamha.Core.Text;
using Lamha.Core.Translation;
using Lamha.Windows.Input;
using Microsoft.UI.Xaml;
using Localization = Lamha.App.Services.Localization;

namespace Lamha.App.ViewModels.Main;

/// <summary>What the Translate page shows under its box.</summary>
public enum TranslateView
{
    /// <summary>Today and the recent lookups: the box is empty.</summary>
    Today,

    /// <summary>Nothing yet: the user is typing (the answer comes when they pause).</summary>
    None,

    /// <summary>The word (or text) at once, and shapes where the answer will be.</summary>
    Waiting,
    Answer,

    /// <summary>A price, in the user's currency (as on the card).</summary>
    Price,
    Error,
}

/// <summary>A recent lookup: the word and its meaning, which looks it up again.</summary>
public sealed record RecentItem(string Word, string Meaning, FlowDirection WordFlow, FlowDirection MeaningFlow, TextAlignment MeaningAlignment, IRelayCommand Open)
{
    /// <summary>"Translate again".</summary>
    public string Tooltip { get; } = Localization.Get("Translate_Again");

    /// <summary>What Narrator reads for the row.</summary>
    public string Spoken => $"{Word}: {Meaning}";
}

/// <summary>A part of a shortcut as it's drawn (the quick lookup's line, Settings → Keyboard shortcuts): a key ("Ctrl") or the "+" between keys.</summary>
public sealed record KeyPart(string Text, bool IsKey)
{
    /// <summary>"Ctrl+Alt+L" as the design draws it: Ctrl, +, Alt, +, L.</summary>
    public static List<KeyPart> Of(Hotkey keys)
    {
        var parts = new List<KeyPart>();
        foreach (var key in keys.ToString().Split('+'))
        {
            if (parts.Count > 0)
            {
                parts.Add(new KeyPart("+", false));
            }

            parts.Add(new KeyPart(key, true));
        }

        return parts;
    }
}

/// <summary>
/// The main window's Translate page (phase 1's design): Lamha 2's box (popup.js's quick translate) with the whole
/// answer as the card shows it; with the box empty, Today (popup.js's <c>renderToday()</c>) and the recent lookups
/// (<c>renderHistory()</c>, 12 of them).
/// </summary>
public sealed partial class TranslateViewModel : ObservableObject, IAnswerActions
{
    /// <summary>Typing looks up after this pause (Lamha 2's <c>qTimer</c>); Enter looks up at once.</summary>
    private static readonly TimeSpan TypingPause = TimeSpan.FromMilliseconds(420);

    /// <summary>Recent lookups shown (Lamha 2's desktop window: 12).</summary>
    private const int RecentShown = 12;

    private readonly Brain _brain;
    private readonly IPageHost _host;
    private CancellationTokenSource? _lookup;
    private CancellationTokenSource? _pause;

    /// <summary>The text the answer (or the error) is for, and whether the AI was asked for it.</summary>
    private (string Text, bool ForceAi) _shown = ("", false);

    /// <summary>The copy the box offers, by its clipboard sequence number; and the one taken, which isn't offered again.</summary>
    private uint _pasteSequence;
    private uint _pasteTaken;

    /// <param name="brain">Lamha's brain: the lookup, Today and the history.</param>
    /// <param name="host">The main window.</param>
    /// <param name="lookupKeys">The quick-lookup shortcut, as the tray menu writes it ("Ctrl+Alt+L").</param>
    public TranslateViewModel(Brain brain, IPageHost host, string lookupKeys)
    {
        _brain = brain;
        _host = host;
        var keys = (lookupKeys ?? "").Split('+', StringSplitOptions.RemoveEmptyEntries);
        Keys = [.. keys.SelectMany((k, i) => i == 0 ? new[] { new KeyPart(k, true) } : [new KeyPart("+", false), new KeyPart(k, true)])];
        LookUpCommand = new RelayCommand<string>(word => Navigate(word ?? ""));
    }

    // ----- the box -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuery), nameof(ShowsPaste))]
    public partial string Query { get; set; } = "";

    public bool HasQuery => Query.Trim().Length > 0;

    // ----- paste and translate -----

    /// <summary>The copy the empty box offers ("" when none), and the chip's quote of it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsPaste), nameof(PasteSpoken))]
    public partial string PasteText { get; private set; } = "";

    [ObservableProperty]
    public partial string PastePreview { get; private set; } = "";

    [ObservableProperty]
    public partial FlowDirection PasteFlow { get; private set; }

    public bool ShowsPaste => PasteText.Length > 0 && !HasQuery;

    /// <summary>"Translate what you copied:"</summary>
    public string PasteLabel { get; } = Localization.Get("Translate_PasteChip");

    /// <summary>What Narrator reads for the chip: the label and the quote.</summary>
    public string PasteSpoken => $"{PasteLabel} {PastePreview}";

    /// <summary>
    /// The clipboard read again for the empty box's offer: when the page shows, when the box is emptied, and when the
    /// window comes back from another program.
    /// </summary>
    public void OfferPaste()
    {
        var copy = HasQuery ? null : _host.Copied(_pasteTaken);
        PastePreview = copy is null ? "" : PasteOffer.Preview(copy.Text);
        PasteFlow = copy is not null && Scripts.IsArabicText(PastePreview) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        _pasteSequence = copy?.Sequence ?? 0;
        PasteText = copy?.Text ?? "";
    }

    /// <summary>The chip: the copy into the box, looked up at once.</summary>
    [RelayCommand]
    private void Paste()
    {
        var text = PasteText;
        _pasteTaken = _pasteSequence;
        PasteText = "";
        Navigate(text);
    }

    /// <summary>Ctrl, +, Alt, +, L.</summary>
    public List<KeyPart> Keys { get; }

    // ----- the first-time hint -----

    /// <summary>Where it's remembered that the hint was shown.</summary>
    private const string LookupTipKey = "tip.lookup";

    /// <summary>
    /// Windows' teaching tip at the quick lookup's line, once (docs/design/system.md §6): Lamha's main gesture, which
    /// the main window otherwise only names in passing.
    /// </summary>
    public bool ShowsLookupTip => _brain.State.Get(LookupTipKey) is null;

    /// <summary>"Select a word in any program and press Ctrl+Alt+L."</summary>
    /// <remarks>The keys are isolated left to right (U+2066 … U+2069), so Arabic's full stop ends the sentence.</remarks>
    public string LookupTipText =>
        Localization.Format("Translate_TipText", "⁦" + string.Join("+", Keys.Where(k => k.IsKey).Select(k => k.Text)) + "⁩");

    /// <summary>The hint was shown (closed, or its window left): never again.</summary>
    public void LookupTipSeen() => _brain.State.Set(LookupTipKey, "1");

    // ----- under the box -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsToday), nameof(IsWaiting), nameof(IsAnswer), nameof(IsPrice), nameof(IsError))]
    public partial TranslateView View { get; private set; }

    public bool IsToday => View == TranslateView.Today;

    public bool IsWaiting => View == TranslateView.Waiting;

    public bool IsAnswer => View == TranslateView.Answer;

    public bool IsPrice => View == TranslateView.Price;

    public bool IsError => View == TranslateView.Error;

    /// <summary>While waiting: the word (as the headword) or the text.</summary>
    [ObservableProperty]
    public partial string WaitingText { get; private set; } = "";

    [ObservableProperty]
    public partial bool WaitingIsWord { get; private set; }

    [ObservableProperty]
    public partial WordAnswer? Answer { get; private set; }

    [ObservableProperty]
    public partial PriceAnswer? Price { get; private set; }

    /// <summary>The links at the foot of the answer.</summary>
    [ObservableProperty]
    public partial List<LinkItem> Links { get; private set; } = [];

    [ObservableProperty]
    public partial ErrorInfo? Error { get; private set; }

    /// <summary>Today's goal, streak and word, or null when there's nothing to show.</summary>
    [ObservableProperty]
    public partial TodayItem? Today { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecent))]
    public partial List<RecentItem> Recent { get; private set; } = [];

    public bool HasRecent => Recent.Count > 0;

    /// <summary>The box's text was put there by Lamha (a meaning, a recent lookup), not typed.</summary>
    public event Action? QueryReplaced;

    /// <summary>Looks a word up on the page: the word of the day's "See its meaning".</summary>
    public IRelayCommand<string> LookUpCommand { get; }

    /// <summary>The page shows: Today and the recent lookups are read again (a lookup on the card may have changed them).</summary>
    public void Refresh()
    {
        if (!HasQuery)
        {
            View = TranslateView.Today;
        }

        Today = TodayItem.From(_brain.Today.Info(), LookUpCommand);
        RefreshRecent();
        OfferPaste();
    }

    /// <summary>Enter in the box: no waiting for the pause.</summary>
    public void LookUpNow()
    {
        _pause?.Cancel();
        LookUp(Query.Trim(), forceAi: false);
    }

    /// <summary>The window closed: what the page was waiting for is no longer wanted.</summary>
    public void Unload()
    {
        _pause?.Cancel();
        _lookup?.Cancel();
    }

    // ----- IAnswerActions -----

    /// <summary>A meaning, a synonym, a recent lookup: into the box, and looked up.</summary>
    public void Navigate(string word)
    {
        if (word.Length == 0)
        {
            return;
        }

        Query = word;
        QueryReplaced?.Invoke();
        LookUpNow();
    }

    public void OpenLink(Uri uri) => _host.OpenLink(uri);

    /// <summary>"Writing tools" under a text's answer: the text in the Write page's draft.</summary>
    public void OpenWritingTools() => _host.Write(_shown.Text);

    public void BetterTranslation()
    {
        if (_shown.Text.Length > 0)
        {
            LookUp(_shown.Text, forceAi: true);
        }
    }

    public async Task ToggleReviewAsync(WordAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        if (!answer.CanReview || answer != Answer)
        {
            return;
        }

        // Typed, not selected: the word as typed, without a sentence (Lamha 2's cardFromLookup without a context).
        var on = await _brain.Review.ToggleAsync(CardFrom.Lookup(answer.Result, _shown.Text, null)).ConfigureAwait(true);
        answer.InReview = on;
        _host.Toast(Localization.Get(on ? "Card_AddedReview" : "Card_RemovedReview"));
    }

    public void Copy(string text)
    {
        _host.Copy(text);
        _host.Toast(Localization.Get("Card_Copied"));
    }

    // ----- commands -----

    /// <summary>The box's ×: empty, and Today back (Lamha 2's <c>clearQ</c>).</summary>
    [RelayCommand]
    private void Clear() => Query = "";

    /// <summary>
    /// The recent lookups' Clear: the history goes, and "History cleared" offers Undo for a few seconds (Lamha 2's, which
    /// asks no question).
    /// </summary>
    [RelayCommand]
    private void ClearHistory()
    {
        var history = _brain.History;
        var cleared = history.Recent();
        history.Clear();
        RefreshRecent();
        _host.Toast(Localization.Get("Translate_HistoryCleared"), Localization.Get("Common_Undo"), () =>
        {
            history.Restore(cleared);
            RefreshRecent();
        });
    }

    /// <summary>The error's button: Settings at the category that fixes it, or Try again.</summary>
    [RelayCommand]
    private void RunErrorAction()
    {
        if (Error is { Action: ErrorAction.Settings } error)
        {
            _host.OpenSettings(error.SettingsSection);
        }
        else if (_shown.Text.Length > 0)
        {
            LookUp(_shown.Text, _shown.ForceAi);
        }
    }

    // ----- looking up -----

    partial void OnQueryChanged(string value)
    {
        _pause?.Cancel();
        if (value.Trim().Length == 0)
        {
            // Lamha 2: an empty box hides the answer, and Today comes back.
            _lookup?.Cancel();
            _shown = ("", false);
            Answer = null;
            Price = null;
            Error = null;
            Links = [];
            Refresh();
            return;
        }

        if (View == TranslateView.Today)
        {
            View = TranslateView.None; // Today makes room for the answer
        }

        _ = AfterPauseAsync(_pause = new CancellationTokenSource());
    }

    private async Task AfterPauseAsync(CancellationTokenSource pause)
    {
        try
        {
            await Task.Delay(TypingPause, pause.Token).ConfigureAwait(true);
            LookUp(Query.Trim(), forceAi: false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void LookUp(string text, bool forceAi)
    {
        if (text.Length == 0)
        {
            return;
        }

        _lookup?.Cancel();
        var cancel = _lookup = new CancellationTokenSource();
        _shown = (text, forceAi);
        Links = [];
        WaitingText = text;
        WaitingIsWord = TextKind.IsLookupCandidate(text);
        View = TranslateView.Waiting;
        _ = LookUpAsync(text, forceAi, cancel);
    }

    private async Task LookUpAsync(string text, bool forceAi, CancellationTokenSource cancel)
    {
        try
        {
            // A price alone ("$49.99") in another currency than the user's: what it is in theirs, as on the card.
            if (!forceAi && await PriceAnswer.ForAsync(_brain, text, this, cancel.Token).ConfigureAwait(true) is { } price)
            {
                Error = null;
                Price = price;
                Links = price.Links;
                View = TranslateView.Price;
                _host.Announce(price.Spoken);
                return;
            }

            var result = await _brain.Lookup.LookupAsync(text, new LookupOptions { ForceAi = forceAi }, cancel.Token).ConfigureAwait(true);
            if (cancel.IsCancellationRequested)
            {
                return;
            }

            var answer = WordAnswer.For(_brain, result, this, writingTools: WritingReady());
            Error = null;
            Answer = answer;
            Links = answer.Links;
            View = TranslateView.Answer;
            _host.Announce(answer.Spoken);
            RefreshRecent();
            await answer.FillAsync(_brain, cancel.Token).ConfigureAwait(true);
        }
        catch (LamhaException e) when (!cancel.IsCancellationRequested)
        {
            var settings = _brain.Settings.Current;
            Error = ErrorText.ForLookup(e, settings.TrProvider ?? settings.AiProvider, settings, !_brain.Network.IsOffline);
            View = TranslateView.Error;
            _host.Announce(Error.Title);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is not OutOfMemoryException && !cancel.IsCancellationRequested)
        {
            // A fault of Lamha's own: said, and logged by its type, rather than waiting for ever (Lamha 2's p.failed).
            AppLog.Error("translate", e);
            Error = ErrorText.Unexpected;
            View = TranslateView.Error;
            _host.Announce(Error.Title);
        }
    }

    /// <summary>The writing tools are set up, so "Writing tools" can be offered under a text (as on the card).</summary>
    private bool WritingReady()
    {
        try
        {
            _brain.Ai.Config();
            return true;
        }
        catch (LamhaException)
        {
            return false;
        }
    }

    private void RefreshRecent()
    {
        Recent = [.. _brain.History.Recent(RecentShown).Select(e =>
        {
            var wordFlow = Localization.FlowOf(e.Language);
            var meaningFlow = Scripts.IsArabicText(e.Meaning) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            return new RecentItem(e.Word, e.Meaning, wordFlow, meaningFlow, Show.InterfaceAligned(meaningFlow), new RelayCommand(() => Navigate(e.Word)));
        })];
    }
}
