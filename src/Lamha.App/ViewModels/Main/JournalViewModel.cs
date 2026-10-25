using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lamha.App.Services;
using Lamha.Core.Text;
using Lamha.Core.Writing;
using Microsoft.UI.Xaml;
using Localization = Lamha.App.Services.Localization;

namespace Lamha.App.ViewModels.Main;

/// <summary>A kind of mistake in the journal: its name, how often, its bar, and whether its rule is shown.</summary>
public sealed partial class JournalBar(string category, string label, int count, int percent, IRelayCommand pick) : ObservableObject
{
    public string Category { get; } = category;

    public string Label { get; } = label;

    public string Count { get; } = Localization.Number(count);

    /// <summary>The bar, against the most frequent kind's (at least 4 %, so a kind found once still shows one).</summary>
    public GridLength Fill { get; } = new(percent, GridUnitType.Star);

    public GridLength Rest { get; } = new(100 - percent, GridUnitType.Star);

    public IRelayCommand Pick { get; } = pick;

    /// <summary>What Narrator reads for the row: "Verb tenses: 7".</summary>
    public string Spoken => $"{Label}: {Count}";

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    /// <summary>The names' column, as wide as the longest name, so every bar starts in the same place (the page measures it).</summary>
    [ObservableProperty]
    public partial GridLength LabelWidth { get; set; } = GridLength.Auto;
}

/// <summary>A mistake from the user's writing: what was wrong, its fix, and why.</summary>
public sealed record JournalEntryItem(string Original, string Fix, string Why, FlowDirection WhyFlow, bool IsFirst)
{
    /// <summary>A line between mistakes, none above the first (Lamha 2's <c>.jlist li</c>).</summary>
    public Thickness Edge => new(0, IsFirst ? 0 : 1, 0, 0);

    public Thickness Space => new(0, IsFirst ? 0 : 8, 0, 8);

    public bool HasWhy => Why.Length > 0;
}

/// <summary>
/// The mistake journal under the Write page's tools (moved from Lamha 2's Settings: options.js's <c>renderJournal()</c>):
/// the summary, a bar per kind, a kind's rule and examples once it's clicked, the recent mistakes, recording on or off,
/// and Clear the journal.
/// </summary>
public sealed partial class JournalViewModel(Brain brain, IPageHost host) : ObservableObject
{
    /// <summary>Mistakes listed (Lamha 2's 30).</summary>
    private const int RecentShown = 30;

    /// <summary>The kind whose rule and mistakes are shown; "" for all the recent ones.</summary>
    private string _category = "";

    [ObservableProperty]
    public partial string Summary { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBars))]
    public partial List<JournalBar> Bars { get; private set; } = [];

    public bool HasBars => Bars.Count > 0;

    /// <summary>The chosen kind's rule, or "".</summary>
    [ObservableProperty]
    public partial string Tip { get; private set; } = "";

    [ObservableProperty]
    public partial string RecentTitle { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecent))]
    public partial List<JournalEntryItem> Recent { get; private set; } = [];

    public bool HasRecent => Recent.Count > 0;

    /// <summary>Settings' "Record my mistakes" (<c>saveMistakes</c>).</summary>
    public bool SaveMistakes
    {
        get => brain.Settings.Current.SaveMistakes;
        set
        {
            if (value != SaveMistakes)
            {
                brain.Settings.Update(s => s with { SaveMistakes = value });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>The journal was cleared, or put back by Undo: the most frequent mistake has changed.</summary>
    public event Action? Replaced;

    /// <summary>The journal is read again (a proofread just added to it, or the page opened).</summary>
    public void Refresh()
    {
        var journal = brain.Writing.Journal();
        var total = journal.Counts.Values.Sum();

        // Most frequent first: the summary's "most frequent" and the bars' lengths are both measured against the first.
        var rows = journal.Counts.Where(c => MistakeCategories.IsKnown(c.Key) && c.Value > 0).OrderByDescending(c => c.Value).ToList();
        if (journal.Checks == 0)
        {
            Summary = Localization.Get("Journal_Empty");
        }
        else
        {
            var top = rows.Count > 0 && rows[0].Key != MistakeCategories.Other ? Localization.Format("Journal_Top", Name(rows[0].Key)) : "";
            Summary = Localization.Format("Journal_Summary", Counted("Journal_Texts", journal.Checks), Counted("Journal_Mistakes", total), top);
        }

        if (_category.Length > 0 && !rows.Any(r => r.Key == _category))
        {
            _category = "";
        }

        var max = rows.Count > 0 ? rows[0].Value : 1;
        Bars = [.. rows.Select(r =>
        {
            var percent = Math.Max(4, (int)Math.Round(r.Value * 100.0 / max, MidpointRounding.AwayFromZero));
            var category = r.Key;
            return new JournalBar(category, Name(category), r.Value, percent, new RelayCommand(() => Pick(category))) { IsOn = category == _category };
        })];

        Tip = _category.Length > 0 && _category != MistakeCategories.Other ? Localization.Get("Category_" + _category + "_Tip") : "";
        RecentTitle = _category.Length > 0 ? Localization.Format("Journal_ExamplesOf", Name(_category)) : Localization.Get("Journal_Recent");
        Recent = [.. journal.Recent
            .Where(m => _category.Length == 0 || m.Category == _category)
            .Take(RecentShown)
            .Select((m, i) => new JournalEntryItem(m.Original, m.Fix, m.Why, Scripts.IsArabicText(m.Why) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, i == 0))];
        OnPropertyChanged(nameof(SaveMistakes)); // Settings may have changed it
    }

    /// <summary>"Learn the rule": the kind's rule and examples (not a toggle, as a click on its bar is).</summary>
    public void Open(string category)
    {
        _category = category;
        Refresh();
    }

    /// <summary>
    /// Clear the journal: it goes, and "Mistake journal cleared" offers Undo for a few seconds, as History's Clear does
    /// (Lamha 2 asked first instead; answered 2026-10-25).
    /// </summary>
    [RelayCommand]
    private void Clear()
    {
        var cleared = brain.Writing.ClearJournal();
        _category = "";
        Refresh();
        Replaced?.Invoke();
        host.Toast(Localization.Get("Journal_Cleared"), Localization.Get("Common_Undo"), () =>
        {
            brain.Writing.RestoreJournal(cleared);
            Refresh();
            Replaced?.Invoke();
        });
    }

    /// <summary>A bar: its rule and examples, or all the recent mistakes again when it's the one shown.</summary>
    private void Pick(string category)
    {
        _category = _category == category ? "" : category;
        Refresh();
    }

    private static string Name(string category) => Localization.Category(category);

    /// <summary>Lamha 2's <c>arCount</c>: one, two, 3 to 10, and the rest each have their own words in Arabic.</summary>
    private static string Counted(string key, int n) => n switch
    {
        1 => Localization.Get(key + "One"),
        2 => Localization.Get(key + "Two"),
        >= 3 and <= 10 => Localization.Format(key + "Few", Localization.Number(n)),
        _ => Localization.Format(key + "Many", Localization.Number(n)),
    };
}
