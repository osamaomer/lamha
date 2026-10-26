using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lamha.App.Controls;
using Lamha.App.Services;
using Lamha.Core.Abstractions;
using Lamha.Core.Review;
using Lamha.Core.Text;
using Microsoft.UI.Xaml;
using Localization = Lamha.App.Services.Localization;

namespace Lamha.App.ViewModels.Main;

/// <summary>A word in the history: the word, its meaning and when it was looked up; a click looks it up again.</summary>
public sealed partial class HistoryRow : ObservableObject
{
    public HistoryRow(HistoryEntry entry, string time, bool first, bool inReview, IRelayCommand open, IRelayCommand<HistoryRow> review)
    {
        Word = entry.Word;
        Meaning = entry.Meaning;
        Time = time;
        WordFlow = Localization.FlowOf(entry.Language);
        MeaningFlow = Scripts.IsArabicText(entry.Meaning) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        MeaningAlignment = Show.InterfaceAligned(MeaningFlow);
        // Lamha 2's cards are English words with a meaning; Arabic lookups aren't cards.
        CanReview = entry.Language == "en" && entry.Meaning.Length > 0;
        IsFirst = first;
        InReview = inReview;
        Open = open;
        Review = review;
    }

    public string Word { get; }

    public string Meaning { get; }

    /// <summary>"10:24 AM".</summary>
    public string Time { get; }

    public FlowDirection WordFlow { get; }

    public FlowDirection MeaningFlow { get; }

    public TextAlignment MeaningAlignment { get; }

    public bool CanReview { get; }

    /// <summary>The day's first word: no line above it, under the day's name.</summary>
    public bool IsFirst { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReviewTooltip))]
    public partial bool InReview { get; set; }

    public string ReviewTooltip => Localization.Get(InReview ? "Card_InReview" : "Card_AddReview");

    /// <summary>"Translate again".</summary>
    public string Tooltip { get; } = Localization.Get("Translate_Again");

    /// <summary>What Narrator reads for the row.</summary>
    public string Spoken => $"{Word}: {Meaning}, {Time}";

    public IRelayCommand Open { get; }

    /// <summary>The bookmark: into the review list, or out of it.</summary>
    public IRelayCommand<HistoryRow> Review { get; }
}

/// <summary>A day of the history: its name ("Today", "Yesterday", "Saturday", "20 October") and its words.</summary>
public sealed record HistoryDayItem(string Name, List<HistoryRow> Rows, Thickness NameMargin);

/// <summary>
/// The main window's History page (phase 1's design, new in Lamha 3; Lamha 2 showed only the last 12): every word in
/// the history by day, newest first, with search and Clear.
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    /// <summary>A day's name, 20 px from the panel's edges; the first one nearer the top.</summary>
    private static readonly Thickness FirstNameMargin = new(20, 12, 20, 6);
    private static readonly Thickness NameMargin = new(20, 16, 20, 6);

    private readonly Brain _brain;
    private readonly IPageHost _host;
    private readonly IRelayCommand<HistoryRow> _review;
    private int _version;

    /// <param name="brain">Lamha's brain: the history and the review list.</param>
    /// <param name="host">The main window.</param>
    public HistoryViewModel(Brain brain, IPageHost host)
    {
        _brain = brain;
        _host = host;
        _review = new AsyncRelayCommand<HistoryRow>(ToggleReviewAsync);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuery))]
    public partial string Query { get; set; } = "";

    public bool HasQuery => Query.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDays), nameof(IsNoMatch))]
    public partial List<HistoryDayItem> Days { get; private set; } = [];

    /// <summary>Anything in the history at all (whatever the search): the search and Clear are offered.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(IsNoMatch))]
    public partial bool HasHistory { get; private set; }

    /// <summary>The history has been read once: until then neither "No words yet" nor "No results" is true.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(IsNoMatch))]
    public partial bool IsRead { get; private set; }

    public bool HasDays => Days.Count > 0;

    /// <summary>Nothing looked up yet.</summary>
    public bool IsEmpty => IsRead && !HasHistory;

    /// <summary>Words in the history, but none that match the search.</summary>
    public bool IsNoMatch => IsRead && HasHistory && Days.Count == 0;

    /// <summary>The page shows: the history is read again (a lookup on the card or the Translate page may have changed it).</summary>
    public void Refresh() => _ = RefreshAsync();

    // ----- commands -----

    /// <summary>
    /// Clear: the whole history goes, and "History cleared" offers Undo for a few seconds (Lamha 2's, as on the Translate
    /// page).
    /// </summary>
    /// <summary>The empty state's way on: the Translate page, its box ready.</summary>
    [RelayCommand]
    private void TranslateWord() => _host.Translate("");

    [RelayCommand]
    private void ClearHistory()
    {
        var history = _brain.History;
        var cleared = history.Recent();
        history.Clear();
        Refresh();
        _host.Toast(Localization.Get("Translate_HistoryCleared"), Localization.Get("Common_Undo"), () =>
        {
            history.Restore(cleared);
            Refresh();
        });
    }

    // ----- the list -----

    partial void OnQueryChanged(string value) => Refresh();

    private async Task RefreshAsync()
    {
        var version = ++_version;
        try
        {
            var history = _brain.History;
            var days = history.ByDay(Query);
            var inReview = await _brain.Review.InReviewAsync(days.SelectMany(d => d.Entries).Select(e => e.Word)).ConfigureAwait(true);
            if (version != _version)
            {
                return; // the search changed while the review list was read
            }

            var today = history.Today;
            HasHistory = days.Count > 0 || history.Count() > 0;
            Days = [.. days.Select((day, i) => new HistoryDayItem(
                DayName(day.Day, today),
                [.. day.Entries.Select((e, k) => new HistoryRow(e, Localization.Time(history.LocalTime(e)), k == 0, inReview.Contains(Lamha.Core.Review.Card.KeyOf(e.Word)),
                    new RelayCommand(() => _host.Translate(e.Word)), _review))],
                i == 0 ? FirstNameMargin : NameMargin))];
            IsRead = true;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            AppLog.Error("history", e);
        }
    }

    private async Task ToggleReviewAsync(HistoryRow? row)
    {
        if (row is not { CanReview: true })
        {
            return;
        }

        // The word and the meaning it was looked up with: there's no sentence or definition in the history.
        var on = await _brain.Review.ToggleAsync(new CardDraft(row.Word, row.Meaning)).ConfigureAwait(true);
        row.InReview = on;
        _host.Toast(Localization.Get(on ? "Card_AddedReview" : "Card_RemovedReview"));
    }

    /// <summary>Today, Yesterday, the weekday for the rest of the week, then the date (with the year when it isn't this year's).</summary>
    private static string DayName(DateOnly day, DateOnly today)
    {
        var ago = today.DayNumber - day.DayNumber;
        return ago switch
        {
            0 => Localization.Get("History_Today"),
            1 => Localization.Get("History_Yesterday"),
            > 1 and < 7 => Localization.Weekday(day),
            _ => day.Year == today.Year ? Localization.DayAndMonth(day) : Localization.FullDate(day),
        };
    }
}
