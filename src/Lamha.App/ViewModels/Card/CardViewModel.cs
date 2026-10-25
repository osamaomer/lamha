using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lamha.App.Services;
using Lamha.Core;
using Lamha.Core.Dictionary;
using Lamha.Core.Review;
using Lamha.Core.Text;
using Lamha.Core.Translation;
using Lamha.Core.Writing;
using Lamha.Windows.Selection;
using Localization = Lamha.App.Services.Localization;

namespace Lamha.App.ViewModels.Card;

/// <summary>What the card asks of the window it's in and the program it came from.</summary>
public interface ICardHost
{
    /// <summary>Closes the card and pastes the text into the program: over its selection, or at its cursor (Insert).</summary>
    Task ReplaceAsync(string text);

    void Copy(string text);

    void OpenLink(Uri uri);

    /// <summary>Settings exist to open (the main window, built after the card in phase 4).</summary>
    bool CanOpenSettings { get; }

    void OpenSettings(string section);

    void Close();

    /// <summary>Narrator says it.</summary>
    void Announce(string text);
}

/// <summary>Which shortcut opened the card.</summary>
public enum CardMode
{
    /// <summary>Look up and translate (Ctrl+Alt+L in the beta).</summary>
    Lookup,

    /// <summary>The writing tools (Ctrl+Alt+W): with nothing selected, Write new, which Insert pastes at the cursor.</summary>
    Write,
}

/// <summary>What the card's body shows.</summary>
public enum CardView
{
    /// <summary>"Select some text first", next to the mouse, without the focus.</summary>
    Hint,

    /// <summary>The word (or text) at once, and shapes where the answer will be.</summary>
    Waiting,
    Answer,

    /// <summary>A price, in the user's currency.</summary>
    Price,
    Error,
    Writing,
}

/// <summary>Where the answer came from, as the bar's small icon says.</summary>
public enum AnswerBadge
{
    None,

    /// <summary>The offline dictionary (a book).</summary>
    Local,

    /// <summary>The AI (a sparkle), named in its tooltip.</summary>
    Ai,
}

/// <summary>
/// The card: Lamha 2's content.js card (frame, lookup, the word's history, errors, the writing tools) on Lamha 3's brain,
/// as signed off in phase 1's design: Back and Forward as a pair, Pin, and a height that follows the answer.
/// </summary>
public sealed partial class CardViewModel(Brain brain, ICardHost host) : ObservableObject, IAnswerActions, IWritingHost
{
    /// <summary>The card's width at 100 %: the lookup's (phase 1's design), and the writing tools' wider one.</summary>
    public const int LookupWidth = 384;
    public const int WritingWidth = 440;

    /// <summary>A short message at the foot of the card ("Copied") stays this long (Lamha 2's toast).</summary>
    private static readonly TimeSpan ToastTime = TimeSpan.FromMilliseconds(1600);

    private readonly List<Entry> _history = [];
    private int _at = -1;
    private SelectionCapture _capture = new(0, "", false);
    private CancellationTokenSource? _lookup;
    private CancellationTokenSource? _toast;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHint), nameof(IsWaiting), nameof(IsAnswer), nameof(IsPrice), nameof(IsError), nameof(IsWriting), nameof(HasBar))]
    public partial CardView View { get; private set; }

    public bool IsHint => View == CardView.Hint;

    public bool IsWaiting => View == CardView.Waiting;

    public bool IsAnswer => View == CardView.Answer;

    public bool IsPrice => View == CardView.Price;

    public bool IsError => View == CardView.Error;

    public bool IsWriting => View == CardView.Writing;

    /// <summary>Everything but the hint has the bar.</summary>
    public bool HasBar => View != CardView.Hint;

    [ObservableProperty]
    public partial int Width { get; private set; } = LookupWidth;

    // ----- the bar -----

    /// <summary>"English → Arabic", "Writing tools · Claude"; "" shows Lamha's name.</summary>
    [ObservableProperty]
    public partial string BarTitle { get; private set; } = "";

    [ObservableProperty]
    public partial bool HasDictSwitch { get; private set; }

    /// <summary>The word's meaning in the translation language ("العربية"), or explained in its own ("English").</summary>
    [ObservableProperty]
    public partial string DictTranslatedLabel { get; private set; } = "";

    [ObservableProperty]
    public partial string DictExplainedLabel { get; private set; } = "";

    [ObservableProperty]
    public partial bool DictExplained { get; private set; }

    [ObservableProperty]
    public partial AnswerBadge Badge { get; private set; }

    [ObservableProperty]
    public partial string BadgeTooltip { get; private set; } = "";

    /// <summary>Back and Forward show as a pair once a word has been followed, so the bar doesn't jump.</summary>
    [ObservableProperty]
    public partial bool ShowNavigation { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    public partial bool CanGoBack { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ForwardCommand))]
    public partial bool CanGoForward { get; private set; }

    /// <summary>A pinned card stays open when the user clicks elsewhere, and moves by its bar.</summary>
    [ObservableProperty]
    public partial bool IsPinned { get; private set; }

    public string PinTooltip => Localization.Get(IsPinned ? "Card_Pinned" : "Card_Pin");

    public bool CanOpenSettings => host.CanOpenSettings;

    // ----- the body -----

    /// <summary>While waiting: the word (as the headword) or the text (as the source).</summary>
    [ObservableProperty]
    public partial string WaitingText { get; private set; } = "";

    [ObservableProperty]
    public partial bool WaitingIsWord { get; private set; }

    /// <summary>The language switch asked for the other view: the answer stays, dimmed, until it comes.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial WordAnswer? Answer { get; private set; }

    [ObservableProperty]
    public partial PriceAnswer? Price { get; private set; }

    /// <summary>The links at the foot: the answer's, or the price's credit for its rates.</summary>
    [ObservableProperty]
    public partial List<LinkItem> Links { get; private set; } = [];

    [ObservableProperty]
    public partial ErrorInfo? Error { get; private set; }

    [ObservableProperty]
    public partial WritingPanel? Writing { get; private set; }

    /// <summary>"Copied", "Added to review": shown at the foot of the card for a moment.</summary>
    [ObservableProperty]
    public partial string Toast { get; private set; } = "";

    /// <summary>"Select some text first" (the hint's text).</summary>
    public string HintText => Localization.Get("Card_SelectFirst");

    /// <summary>Opens the card on <paramref name="capture"/>: a lookup, the writing tools, Write new, or the hint.</summary>
    public void Load(SelectionCapture capture, CardMode mode, WordContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(capture);
        Unload();
        _capture = capture;
        _history.Clear();
        _at = -1;
        IsPinned = false;
        var text = capture.Text.Trim();
        if (mode == CardMode.Write)
        {
            ShowWriting(text);
        }
        else if (text.Length == 0)
        {
            Frame("");
            Width = LookupWidth;
            View = CardView.Hint;
        }
        else
        {
            Go(new Entry(text, context, ForceAi: false));
        }
    }

    /// <summary>The card closed: what it was waiting for is no longer wanted.</summary>
    public void Unload()
    {
        _lookup?.Cancel();
        _lookup = null;
        Writing?.Cancel();
    }

    /// <summary>A number key in the writing tools runs that tool (1–9; Arabic-Indic digits too, as in Lamha 2).</summary>
    public bool PickTool(int number) => View == CardView.Writing && Writing is not null && Writing.PickNumber(number);

    // ----- IAnswerActions -----

    public void Navigate(string word)
    {
        if (word.Length == 0)
        {
            return;
        }

        // Following a word from an earlier one drops the ones after it, as in a browser.
        _history.RemoveRange(_at + 1, _history.Count - _at - 1);
        Go(new Entry(word, null, ForceAi: false));
    }

    public void OpenLink(Uri uri) => host.OpenLink(uri);

    public void OpenWritingTools() => ShowWriting(_history.Count > 0 ? _history[0].Text : _capture.Text.Trim());

    public void BetterTranslation()
    {
        if (_at >= 0)
        {
            Load(_history[_at] with { ForceAi = true }, keep: false);
        }
    }

    // ----- IWritingHost -----

    public Task<WritingResult> RunAsync(WritingTool tool, string text, WritingOptions options, CancellationToken cancel) =>
        brain.Writing.RunAsync(tool, text, options, cancel);

    public Task ReplaceAsync(string text)
    {
        // Lamha 2: the spaces around the selection stay where they were.
        var raw = _capture.Text;
        var full = raw[..(raw.Length - raw.TrimStart().Length)] + text + raw[raw.TrimEnd().Length..];
        return host.ReplaceAsync(full);
    }

    public void Copy(string text)
    {
        host.Copy(text);
        ShowToast(Localization.Get("Card_Copied"));
    }

    public void OpenSettings(string section) => host.OpenSettings(section);

    public void Announce(string text) => host.Announce(text);

    // ----- commands -----

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        if (_at > 0)
        {
            _at--;
            Load(_history[_at], keep: false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void Forward()
    {
        if (_at < _history.Count - 1)
        {
            _at++;
            Load(_history[_at], keep: false);
        }
    }

    [RelayCommand]
    private void TogglePin() => IsPinned = !IsPinned;

    [RelayCommand]
    private void Settings() => host.OpenSettings("");

    [RelayCommand]
    private void Close() => host.Close();

    /// <summary>The bar's language switch: the word translated (false) or explained in its own language (true).</summary>
    [RelayCommand]
    private void SetDict(string explained)
    {
        var want = explained == "true";
        OnPropertyChanged(nameof(DictExplained));
        if (Answer is null || want == DictExplained || _at < 0)
        {
            return;
        }

        // Remembered for the next words in that language (Lamha 2's enDict / explainLangs).
        brain.Lookup.SetExplain(Answer.Result.Source, want);
        Load(_history[_at], keep: true);
    }

    [RelayCommand]
    private void RunErrorAction()
    {
        if (Error?.Action == ErrorAction.Settings)
        {
            host.OpenSettings(Error.SettingsSection);
        }
        else if (_at >= 0)
        {
            Load(_history[_at], keep: false);
        }
    }

    /// <summary>The bookmark: the word into the review deck, or out of it (Lamha 2's <c>reviewBtn()</c>).</summary>
    public async Task ToggleReviewAsync(WordAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        if (!answer.CanReview || answer != Answer)
        {
            return;
        }

        var first = _at == 0;
        var draft = CardFrom.Lookup(answer.Result, first ? _capture.Text.Trim() : "", first ? _history[0].Context : null);
        var on = await brain.Review.ToggleAsync(draft).ConfigureAwait(true);
        answer.InReview = on;
        ShowToast(Localization.Get(on ? "Card_AddedReview" : "Card_RemovedReview"));
    }

    // ----- loading -----

    private void Go(Entry entry)
    {
        _history.Add(entry);
        _at = _history.Count - 1;
        Load(entry, keep: false);
    }

    /// <summary>
    /// Looks <paramref name="entry"/> up (Lamha 2's <c>load()</c>). <paramref name="keep"/>: the language switch, whose
    /// answer stays (dimmed) until the other one comes, so the card doesn't shrink and grow back.
    /// </summary>
    private void Load(Entry entry, bool keep)
    {
        _lookup?.Cancel();
        var cancel = _lookup = new CancellationTokenSource();
        Writing?.Cancel();
        Writing = null;
        Links = [];
        Width = LookupWidth;
        ShowNavigation = _history.Count > 1;
        CanGoBack = _at > 0;
        CanGoForward = _at < _history.Count - 1;
        if (keep && Answer is not null)
        {
            IsBusy = true;
        }
        else
        {
            Frame("");
            WaitingText = entry.Text;
            WaitingIsWord = TextKind.IsLookupCandidate(entry.Text);
            View = CardView.Waiting;
        }

        _ = LookUpAsync(entry, cancel);
    }

    private async Task LookUpAsync(Entry entry, CancellationTokenSource cancel)
    {
        try
        {
            // A price alone ("$49.99") in another currency than the user's: what it is in theirs.
            if (_at == 0 && await ConvertAsync(entry.Text, cancel).ConfigureAwait(true))
            {
                return;
            }

            var result = await brain.Lookup.LookupAsync(entry.Text, new LookupOptions { Context = entry.Context, ForceAi = entry.ForceAi }, cancel.Token).ConfigureAwait(true);
            if (cancel.IsCancellationRequested)
            {
                return;
            }

            Show(result);
            await Answer!.FillAsync(brain, cancel.Token).ConfigureAwait(true);
        }
        catch (LamhaException e) when (!cancel.IsCancellationRequested)
        {
            ShowError(e);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is not OutOfMemoryException && !cancel.IsCancellationRequested)
        {
            // A fault of Lamha's own: said, and logged by its type, rather than shapes waiting for ever.
            AppLog.Error("card lookup", e);
            ShowError(ErrorText.Unexpected);
        }
    }

    /// <summary>
    /// The lookup's error in words, with the button that helps. Public for <c>--sample=error:code</c>, which shows an
    /// error without breaking the network; Try again then looks the text up for real.
    /// </summary>
    public void ShowError(LamhaException error)
    {
        var online = !brain.Network.IsOffline;
        ShowError(ErrorText.ForLookup(error, brain.Settings.Current.TrProvider ?? brain.Settings.Current.AiProvider, brain.Settings.Current, online));
    }

    private void ShowError(ErrorInfo error)
    {
        _lookup?.Cancel();
        IsBusy = false;
        Frame("");
        Error = error.Offering(host.CanOpenSettings);
        View = CardView.Error;
        host.Announce(Error.Title);
    }

    private void Show(LookupResult result)
    {
        var settings = brain.Settings.Current;
        var answer = WordAnswer.For(brain, result, this, WritingReady() is null && _capture.Text.Trim().Length > 0);

        // The bar: the language switch for a word in another language than the one it translates into, else the languages.
        var translatedInto = (result.Explain ? result.Other : result.Target) is { Length: > 0 } t ? t : settings.TargetLang;
        var dictSwitch = result.IsWord && result.Source.Length > 0 && result.Source != "auto" && result.Source != translatedInto;
        Frame(dictSwitch ? "" : $"{Localization.LanguageName(result.Source)}{(Localization.IsArabic ? " ← " : " → ")}{Localization.LanguageName(result.Target)}");
        HasDictSwitch = dictSwitch;
        DictTranslatedLabel = DictName(translatedInto);
        DictExplainedLabel = DictName(result.Source);
        DictExplained = result.Explain || result.ExplainMissing;
        (Badge, BadgeTooltip) = result.From switch
        {
            ResultSource.Local => (AnswerBadge.Local, Localization.Get("Card_LocalBadgeTitle")),
            ResultSource.Ai => (AnswerBadge.Ai, Localization.Format("Card_AiBadgeTitle", result.AiName)),
            _ => (AnswerBadge.None, ""),
        };

        IsBusy = false;
        Error = null;
        Answer = answer;
        Links = answer.Links;
        View = CardView.Answer;
        host.Announce(answer.Spoken);
    }

    /// <summary>
    /// "Prices in your currency" (phase 1's design): true when <paramref name="text"/> is a price in another currency
    /// and the card shows it; false to look the text up as usual (not a price, already the user's, an unknown currency).
    /// </summary>
    private async Task<bool> ConvertAsync(string text, CancellationTokenSource cancel)
    {
        if (await PriceAnswer.ForAsync(brain, text, this, cancel.Token).ConfigureAwait(true) is not { } price)
        {
            return false;
        }

        Frame("");
        Error = null;
        Price = price;
        Links = price.Links;
        View = CardView.Price;
        host.Announce(price.Spoken);
        return true;
    }

    private void ShowWriting(string text)
    {
        _lookup?.Cancel();
        Frame("");
        Answer = null;
        ShowNavigation = false;
        Writing = new WritingPanel(this, text, _capture.CanReplace, brain.Settings.Current, WritingReady());
        BarTitle = Writing.Title;
        Width = WritingWidth;
        View = CardView.Writing;
    }

    /// <summary>Why the writing tools can't run yet ("ai_no_key"), or null when they're set up.</summary>
    private LamhaException? WritingReady()
    {
        try
        {
            brain.Ai.Config();
            return null;
        }
        catch (LamhaException e)
        {
            return e;
        }
    }

    /// <summary>The bar without an answer's parts (the switch, the badge): Lamha's name, or <paramref name="title"/>.</summary>
    private void Frame(string title)
    {
        BarTitle = title;
        Links = [];
        HasDictSwitch = false;
        Badge = AnswerBadge.None;
    }

    /// <summary>The language switch's names: "English" as Lamha 2's short word for it, other languages by name.</summary>
    private static string DictName(string code) => code == "en" ? Localization.Get("Card_DictEn") : Localization.LanguageName(code);

    private async void ShowToast(string text)
    {
        _toast?.Cancel();
        var mine = _toast = new CancellationTokenSource();
        Toast = text;
        host.Announce(text);
        try
        {
            await Task.Delay(ToastTime, mine.Token).ConfigureAwait(true);
            Toast = "";
        }
        catch (OperationCanceledException)
        {
        }
    }

    partial void OnIsPinnedChanged(bool value) => OnPropertyChanged(nameof(PinTooltip));

    /// <summary>A word or text the card showed: Back and Forward go through them.</summary>
    private sealed record Entry(string Text, WordContext? Context, bool ForceAi);
}
