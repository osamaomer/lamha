using Lamha.Core.Review;
using Lamha.Core.Settings;
using Lamha.Windows.Shell;
using Microsoft.UI.Dispatching;

namespace Lamha.App.Services;

/// <summary>
/// The words waiting for review, for the tray, and Lamha 2's reminder (main.js <c>setBadge()</c>, Core's
/// <see cref="ReviewReminder"/>): counted again when the deck or the settings change (after Lamha 2's 300 ms) and every
/// 30 minutes (its <c>lamha-badge</c> alarm), so words falling due are noticed. New in Lamha 3, the reminder asks about
/// one word: Show meaning, then Forgot / Hard / Knew it, answered without opening Lamha (the tray design, 2026-10-25).
/// </summary>
public sealed partial class ReviewReminderService : IDisposable
{
    private const string Tag = "review";
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(30);

    private readonly Brain _brain;
    private readonly NotificationService _notes;
    private readonly Func<bool> _mayRemind;
    private readonly DispatcherQueue _queue;
    private readonly DispatcherQueueTimer _settle;
    private readonly DispatcherQueueTimer _every;

    /// <param name="brain">The deck, and Review for an answer.</param>
    /// <param name="notes">Windows' notifications.</param>
    /// <param name="mayRemind">Lamha is on and its window isn't in use.</param>
    /// <param name="queue">The UI thread.</param>
    public ReviewReminderService(Brain brain, NotificationService notes, Func<bool> mayRemind, DispatcherQueue queue)
    {
        _brain = brain;
        _notes = notes;
        _mayRemind = mayRemind;
        _queue = queue;
        _settle = queue.CreateTimer();
        _settle.Interval = Settle;
        _settle.IsRepeating = false;
        _settle.Tick += (_, _) => Recount();
        _every = queue.CreateTimer();
        _every.Interval = Every;
        _every.Tick += (_, _) => Recount();
        _every.Start();
        brain.ReviewChanged += Soon;
        brain.Settings.Changed += OnSettings;
        notes.Clicked += OnClicked;
    }

    /// <summary>The count changed: the tray's tooltip, dot and menu.</summary>
    public event Action<int>? Changed;

    /// <summary>Review is asked for: the main window on it.</summary>
    public Action? OpenReview { get; set; }

    public int Waiting => _brain.Reminder.Waiting;

    /// <summary>Counts the waiting words now, and reminds when it's time.</summary>
    public void Recount()
    {
        var before = Waiting;
        var remind = _brain.Reminder.Recount(_mayRemind());
        if (Waiting != before)
        {
            Changed?.Invoke(Waiting);
        }

        if (remind && _brain.Reminder.First() is { } card)
        {
            AppLog.Info($"review reminder: {Waiting} waiting");
            _notes.Show(Reminder(card, Waiting));
        }
    }

    /// <summary>Review opened from elsewhere (the tray): the reminder's no longer needed.</summary>
    public void Dismiss() => _notes.Remove(Tag);

    public void Dispose()
    {
        _settle.Stop();
        _every.Stop();
        _brain.ReviewChanged -= Soon;
        _brain.Settings.Changed -= OnSettings;
        _notes.Clicked -= OnClicked;
    }

    /// <summary>Time to review · 5 words are waiting for review. · the word and its sentence · Show meaning, Word review.</summary>
    private static Note Reminder(Card card, int waiting) => new(
        Localization.Get("Note_ReviewTime"),
        Localization.Format(waiting == 1 ? "Note_ReviewWaitingOne" : "Note_ReviewWaitingMany", waiting))
    {
        Word = card.Word,
        WordLine = card.Example.Length > 0 ? $"“{Cut(card.Example, 200)}”" : null,
        Action = "review",
        Buttons =
        [
            new(Localization.Get("Review_Show").Trim(), "show", Values(card.Key)),
            new(Localization.Get("Tray_Review"), "review"),
        ],
        Tag = Tag,
    };

    /// <summary>After Show meaning: the word, its meaning (or, for an English–English card, its definition), and the three answers.</summary>
    private static Note Meaning(Card card)
    {
        var meaning = card.EnglishFirst && card.Definition.Length > 0 ? card.Definition : card.Meaning.Length > 0 ? card.Meaning : card.Definition;
        return new Note(Localization.Get("Note_ReviewTime"))
        {
            Word = card.Word,
            WordLine = Cut(meaning, 200),
            Action = "review",
            Buttons =
            [
                new(Localization.Get("Review_Again"), "grade", Values(card.Key, "again")),
                new(Localization.Get("Review_Hard"), "grade", Values(card.Key, "hard")),
                new(Localization.Get("Review_Good"), "grade", Values(card.Key, "good")),
            ],
            Tag = Tag,
        };
    }

    private static Dictionary<string, string> Values(string key, string grade = "")
    {
        var values = new Dictionary<string, string> { ["key"] = key };
        if (grade.Length > 0)
        {
            values["grade"] = grade;
        }

        return values;
    }

    private static string Cut(string text, int max) => text.Length > max ? text[..max].TrimEnd() + "…" : text;

    private async void OnClicked(AppLink link)
    {
        try
        {
            var key = link.Values.GetValueOrDefault("key", "");
            switch (link.Action)
            {
                case "review":
                    OpenReview?.Invoke();
                    break;
                case "show" when _brain.Reminder.Find(key) is { } card:
                    _notes.Show(Meaning(card));
                    break;
                case "grade" when Grades(link.Values.GetValueOrDefault("grade")) is { } grade:
                    await _brain.Review.GradeAsync(key, grade).ConfigureAwait(true);
                    AppLog.Info($"review answered from a notification: {grade}");
                    break;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An exception in an async void would end Lamha.
            AppLog.Error("review notification", ex);
        }
    }

    private static Grade? Grades(string? name) => name switch
    {
        "again" => Grade.Again,
        "hard" => Grade.Hard,
        "good" => Grade.Good,
        _ => null,
    };

    private void OnSettings(object? sender, LamhaSettings settings) => Soon();

    /// <summary>From any thread: counted once things have settled.</summary>
    private void Soon() => _queue.TryEnqueue(() =>
    {
        _settle.Stop();
        _settle.Start();
    });
}
