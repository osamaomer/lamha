using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lamha.App.Services;
using Lamha.Core.Review;
using Microsoft.UI.Xaml;
using Localization = Lamha.App.Services.Localization;

namespace Lamha.App.ViewModels.Main;

/// <summary>
/// The main window's Review page: Lamha 2's Review tab (popup.js <c>loadReview</c>, <c>renderReview</c>, <c>answer</c>),
/// as signed off in phase 1's design. The due and new counts, the progress through this sitting, the flashcard (the
/// word, its sentence with the word marked; Show meaning; Forgot / Hard / Knew it with when each brings it back), and
/// when nothing is left, what this sitting did and when the next review is.
/// </summary>
public sealed partial class ReviewViewModel : ObservableObject
{
    private readonly Brain _brain;
    private readonly IPageHost _host;
    private ReviewQueue? _queue;

    /// <summary>Answers since the window opened: the progress bar and the summary at the end (Lamha 2's <c>rvSession</c>).</summary>
    private int _answered;
    private int _good;
    private int _hard;
    private int _again;
    private bool _answering;

    /// <param name="brain">Lamha's brain: the review deck.</param>
    /// <param name="host">The main window.</param>
    public ReviewViewModel(Brain brain, IPageHost host)
    {
        _brain = brain;
        _host = host;
    }

    // ----- the head: due, new, the totals; the progress -----

    [ObservableProperty]
    public partial string DueText { get; private set; } = "";

    [ObservableProperty]
    public partial bool HasDue { get; private set; }

    [ObservableProperty]
    public partial string FreshText { get; private set; } = "";

    [ObservableProperty]
    public partial bool HasFresh { get; private set; }

    /// <summary>"٢٤٠ كلمة · ١٨٥ محفوظة".</summary>
    [ObservableProperty]
    public partial string Totals { get; private set; } = "";

    /// <summary>How far through the cards waiting now, from 0 to 1; shown once a card has been answered.</summary>
    [ObservableProperty]
    public partial double Progress { get; private set; }

    [ObservableProperty]
    public partial bool HasProgress { get; private set; }

    // ----- what the page shows -----

    /// <summary>The deck has been read once: until then neither the card nor "No words yet" shows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCard), nameof(IsDone), nameof(IsEmpty))]
    public partial QueuedCard? Current { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCard), nameof(IsDone), nameof(IsEmpty))]
    public partial bool IsRead { get; private set; }

    public bool IsCard => IsRead && Current is not null;

    /// <summary>Words in the deck, none waiting now.</summary>
    public bool IsDone => IsRead && Current is null && _queue is { Total: > 0 };

    /// <summary>No words in the deck yet.</summary>
    public bool IsEmpty => IsRead && Current is null && _queue is not { Total: > 0 };

    // ----- the flashcard -----

    [ObservableProperty]
    public partial bool IsNew { get; private set; }

    [ObservableProperty]
    public partial string Word { get; private set; } = "";

    /// <summary>The sentence it was found in, around the form of the word that was selected (marked).</summary>
    [ObservableProperty]
    public partial string ExampleBefore { get; private set; } = "";

    [ObservableProperty]
    public partial string ExampleWord { get; private set; } = "";

    [ObservableProperty]
    public partial string ExampleAfter { get; private set; } = "";

    [ObservableProperty]
    public partial bool HasExample { get; private set; }

    /// <summary>Show meaning was pressed: the back of the card and the three answers.</summary>
    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    /// <summary>
    /// The back's big line and the one under it: the meaning and the definition, or, for a word looked up in the
    /// English–English dictionary, its definition first (Lamha 2's <c>c.en</c>).
    /// </summary>
    [ObservableProperty]
    public partial string BackMain { get; private set; } = "";

    [ObservableProperty]
    public partial FlowDirection BackMainFlow { get; private set; }

    [ObservableProperty]
    public partial string BackSub { get; private set; } = "";

    [ObservableProperty]
    public partial FlowDirection BackSubFlow { get; private set; }

    /// <summary>When each answer brings the word back: "١٠ د", "يوم", "٤ أيام".</summary>
    [ObservableProperty]
    public partial string AgainIn { get; private set; } = "";

    [ObservableProperty]
    public partial string HardIn { get; private set; } = "";

    [ObservableProperty]
    public partial string GoodIn { get; private set; } = "";

    // ----- nothing left -----

    /// <summary>"Reviewed 3 words · knew 2 · hard 1 · forgot 0": what this sitting did; "" before any answer.</summary>
    [ObservableProperty]
    public partial string Session { get; private set; } = "";

    /// <summary>"Next review in 4 hours."</summary>
    [ObservableProperty]
    public partial string Next { get; private set; } = "";

    /// <summary>"🔥 5-day review streak", from two days in a row.</summary>
    [ObservableProperty]
    public partial string Streak { get; private set; } = "";

    /// <summary>Words due and new ones for today changed: the count on the Review item.</summary>
    public event Action<int>? CountChanged;

    /// <summary>
    /// Called with the answer before the card changes, so the page can send it off the way it was answered (Lamha 2's
    /// <c>flyOff</c>) while the next one loads.
    /// </summary>
    public Func<Grade, Task>? Answering { get; set; }

    /// <summary>An answer was saved and what comes next is showing: the next card, or "Well done" (to bring it in).</summary>
    public event Action? Answered;

    /// <summary>The page shows: the deck is read again (a word added on the card, or a day passed).</summary>
    public void Refresh() => _ = LoadAsync();

    /// <summary>Space or Enter: the back of the card.</summary>
    [RelayCommand]
    private void Reveal()
    {
        if (Current is not null && !IsShown)
        {
            IsShown = true;
        }
    }

    [RelayCommand]
    private Task GradeAgain() => GradeAsync(Grade.Again);

    [RelayCommand]
    private Task GradeHard() => GradeAsync(Grade.Hard);

    [RelayCommand]
    private Task GradeGood() => GradeAsync(Grade.Good);

    /// <summary>1, 2 or 3 (Arabic-Indic too, as in Lamha 2), once the meaning shows.</summary>
    public bool PickGrade(int number)
    {
        if (!IsShown || Current is null || number is < 1 or > 3)
        {
            return false;
        }

        _ = GradeAsync(number switch { 1 => Grade.Again, 2 => Grade.Hard, _ => Grade.Good });
        return true;
    }

    /// <summary>The word out of the deck (Lamha 2's <c>removeCurrent</c>).</summary>
    [RelayCommand]
    private async Task Remove()
    {
        if (Current is not { } card)
        {
            return;
        }

        await _brain.Review.RemoveAsync(card.Card.Key).ConfigureAwait(true);
        _host.Toast(Localization.Get("Review_Removed"));
        await LoadAsync().ConfigureAwait(true);
    }

    private async Task GradeAsync(Grade grade)
    {
        // Once per card: a second key while the card leaves would answer it twice.
        if (Current is not { } card || !IsShown || _answering)
        {
            return;
        }

        _answering = true;
        try
        {
            if (Answering is { } answering)
            {
                await answering(grade).ConfigureAwait(true);
            }
        }
        finally
        {
            _answering = false;
        }

        IsShown = false;
        _answered++;
        _ = grade switch
        {
            Grade.Again => _again++,
            Grade.Hard => _hard++,
            _ => _good++,
        };
        var goal = 0;
        try
        {
            goal = await _brain.Review.GradeAsync(card.Card.Key, grade).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }
        finally
        {
            Answered?.Invoke(); // also if it failed, so the page doesn't stay waiting for a card that isn't coming
        }

        if (goal > 0)
        {
            _host.Celebrate(Localization.Format("Card_GoalDone", goal)); // Lamha 2's flash and burst
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            var queue = _queue = await _brain.Review.QueueAsync().ConfigureAwait(true);
            HasDue = queue.Due > 0;
            HasFresh = queue.New > 0;
            DueText = Localization.Format("Review_Due", queue.Due);
            FreshText = Localization.Format("Review_Fresh", queue.New);
            Totals = Localization.Format("Review_Totals", Counted("Review_Words", queue.Total), Localization.Number(queue.Learned));

            // This sitting: answers so far out of answers and the cards left.
            var all = _answered + queue.Cards.Count;
            HasProgress = _answered > 0 && queue.Cards.Count > 0;
            Progress = all > 0 ? (double)_answered / all : 0;

            Show(queue.Cards.Count > 0 ? queue.Cards[0] : null, queue);
            IsRead = true;
            CountChanged?.Invoke(queue.Due + queue.New);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            AppLog.Error("review", e);
        }
    }

    private void Show(QueuedCard? queued, ReviewQueue queue)
    {
        IsShown = false;
        Current = queued;
        if (queued is null)
        {
            Session = _answered > 0
                ? Localization.Format("Review_Session", CountedAfter("Review_Words", _answered), Localization.Number(_good), Localization.Number(_hard), Localization.Number(_again))
                : "";
            Next = queue.NextDue is { } due ? Localization.Format("Review_Next", Span(TimeSpan.FromMilliseconds(due - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), after: true)) : "";
            Streak = queue.Streak >= 2 ? Streaks(queue.Streak) : "";
            return;
        }

        var c = queued.Card;
        IsNew = c.IsNew;
        Word = c.Word;
        (ExampleBefore, ExampleWord, ExampleAfter) = Mark(c.Example, c.Form.Length > 0 ? c.Form : c.Word);
        HasExample = c.Example.Length > 0;
        var (main, sub) = c.EnglishFirst ? (c.Definition, c.Meaning) : (c.Meaning, c.Definition);
        BackMain = main;
        BackSub = sub;
        BackMainFlow = FlowOf(main);
        BackSubFlow = FlowOf(sub);
        AgainIn = Span(queued.Again);
        HardIn = Span(queued.Hard);
        GoodIn = Span(queued.Good);
    }

    /// <summary>The sentence with the looked-up form of the word marked (Lamha 2's <c>highlight</c>).</summary>
    private static (string Before, string Word, string After) Mark(string sentence, string word)
    {
        var i = word.Length == 0 ? -1 : sentence.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        return i < 0 ? (sentence, "", "") : (sentence[..i], sentence.Substring(i, word.Length), sentence[(i + word.Length)..]);
    }

    private static FlowDirection FlowOf(string text) =>
        Lamha.Core.Text.Scripts.IsArabicText(text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>
    /// "١٠ د", "ساعة", "يومان", "٥ أيام"… or "10 min", "1 hour", "5 days"… (Lamha 2's <c>spanLabel</c>, with its
    /// rounding). <paramref name="after"/>: it follows بعد ("بعد يومين").
    /// </summary>
    private static string Span(TimeSpan span, bool after = false)
    {
        var min = (long)Math.Round(span.TotalMinutes, MidpointRounding.AwayFromZero);
        if (min < 60)
        {
            return Localization.Format("Span_Min", Math.Max(1, min));
        }

        var hours = (long)Math.Round(min / 60.0, MidpointRounding.AwayFromZero);
        if (hours < 24)
        {
            return Counted("Span_Hour", hours, after);
        }

        var days = (long)Math.Round(hours / 24.0, MidpointRounding.AwayFromZero);
        if (days < 30)
        {
            return Counted("Span_Day", days, after);
        }

        var months = (long)Math.Round(days / 30.0, MidpointRounding.AwayFromZero);
        return months < 12 ? Counted("Span_Month", months, after) : Counted("Span_Year", (long)Math.Round(days / 365.0, MidpointRounding.AwayFromZero), after);
    }

    /// <summary>Lamha 2's <c>arCount</c>: one, two, 3 to 10, and the rest each have their own words in Arabic.</summary>
    private static string Counted(string key, long n, bool after = false) => n switch
    {
        1 => Localization.Get(key + "One"),
        2 => Localization.Get(key + (after ? "TwoAfter" : "Two")),
        >= 3 and <= 10 => Localization.Format(key + "Few", Localization.Number(n)),
        _ => Localization.Format(key + "Many", Localization.Number(n)),
    };

    /// <summary>The words reviewed, after "راجعت": two of them are "كلمتين".</summary>
    private static string CountedAfter(string key, long n) => Counted(key, n, after: true);

    private static string Streaks(int n) =>
        Localization.Format(n == 2 ? "Review_StreakTwo" : n <= 10 ? "Review_StreakFew" : "Review_StreakMany", n);
}
