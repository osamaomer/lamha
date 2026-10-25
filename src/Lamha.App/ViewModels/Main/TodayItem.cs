using CommunityToolkit.Mvvm.Input;
using Lamha.Core.Review;
using Lamha.Core.Text;
using Microsoft.UI.Xaml;
using Localization = Lamha.App.Services.Localization;

namespace Lamha.App.ViewModels.Main;

/// <summary>
/// The Today panel on the Translate page (Lamha 2's <c>renderToday()</c> in popup.js): the day's goal as a ring, the
/// streak, and the word of the day. Built afresh each time the page shows it.
/// </summary>
public sealed class TodayItem
{
    private TodayItem(TodayInfo info, IRelayCommand<string> lookUp)
    {
        var (done, goal, streak, word) = (info.Done, info.Goal, info.Streak, info.Word);
        HasGoal = goal > 0;
        IsMet = HasGoal && done >= goal;
        Progress = HasGoal ? (double)done / goal : 0;
        Done = Localization.Number(done);
        RingName = HasGoal ? Localization.Format("Today_Ring", done, goal) : "";
        ProgressText = HasGoal ? Localization.Format("Today_Progress", done, goal) : "";
        LeftText = !HasGoal ? "" : IsMet ? Localization.Get("Today_Met") : Localization.Format("Today_Left", goal - done);
        HasStreak = streak >= 2;
        StreakText = HasStreak ? StreakDays(streak) : "";
        HasHead = HasGoal || HasStreak;

        if (word is not null)
        {
            Word = word.Word;
            WordLabel = Localization.Get(word.FromDeck ? "Today_WordDeck" : "Today_WordNew");
            WordLabelTooltip = word.FromDeck ? Localization.Get("Today_WordDeckTitle") : null;
            Meaning = word.Meaning;
            MeaningFlow = Scripts.IsArabicText(word.Meaning) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            Definition = word.Definition;
            Example = word.Example.Length > 0 ? $"“{word.Example}”" : "";
            LookUp = new RelayCommand(() => lookUp.Execute(word.Word));
        }
    }

    /// <summary>Today's panel, or null when it has nothing to show (no goal, no streak, no word of the day).</summary>
    public static TodayItem? From(TodayInfo info, IRelayCommand<string> lookUp)
    {
        ArgumentNullException.ThrowIfNull(info);
        var item = new TodayItem(info, lookUp);
        return item.HasHead || item.HasWord ? item : null;
    }

    /// <summary>The ring and its line: a goal is set (Settings → Review).</summary>
    public bool HasGoal { get; }

    /// <summary>The goal is reached: the ring and its line turn green.</summary>
    public bool IsMet { get; }

    /// <summary>0 to 1.</summary>
    public double Progress { get; }

    /// <summary>The number in the ring.</summary>
    public string Done { get; }

    /// <summary>What Narrator says for the ring: "7 of 10".</summary>
    public string RingName { get; }

    /// <summary>"7 of 10 today".</summary>
    public string ProgressText { get; }

    /// <summary>"3 to go: look up words or review your cards", or "Today's goal is done. Well done!".</summary>
    public string LeftText { get; }

    public bool HasStreak { get; }

    /// <summary>"🔥 5 days".</summary>
    public string StreakText { get; }

    /// <summary>The goal or the streak: the panel's first line.</summary>
    public bool HasHead { get; }

    public bool HasWord => Word.Length > 0;

    /// <summary>A line between the goal and the word of the day when both show.</summary>
    public bool HasDivider => HasHead && HasWord;

    public string Word { get; } = "";

    /// <summary>"Word of the day", or "Word of the day · one of yours" for a word from the review deck.</summary>
    public string WordLabel { get; } = "";

    public string? WordLabelTooltip { get; }

    public string Meaning { get; } = "";

    public FlowDirection MeaningFlow { get; }

    public string Definition { get; } = "";

    public string Example { get; } = "";

    /// <summary>"See its meaning": looks the word up on the page.</summary>
    public IRelayCommand? LookUp { get; }

    /// <summary>Lamha 2's <c>td.streak</c>, Arabic counting included: يومان, ٣ أيام … ١٠ أيام, ١١ يومًا.</summary>
    private static string StreakDays(int n) =>
        Localization.Format(n == 2 ? "Today_StreakTwo" : n <= 10 ? "Today_StreakFew" : "Today_StreakMany", n);
}
