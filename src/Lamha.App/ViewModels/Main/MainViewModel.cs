using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lamha.App.Controls;
using Lamha.App.Services;

namespace Lamha.App.ViewModels.Main;

/// <summary>A page in the side pane: its name, its icon, and its name as a tooltip once the pane is folded.</summary>
public sealed partial class NavItem(string id, string label, Glyph glyph) : ObservableObject
{
    public string Id { get; } = id;

    public string Label { get; } = label;

    public Glyph Glyph { get; } = glyph;

    /// <summary>The name, shown when the pane is only icons; null otherwise.</summary>
    [ObservableProperty]
    public partial string? Tooltip { get; set; }

    /// <summary>A count beside the name (the words to review: "١٥"), or "" for none.</summary>
    [ObservableProperty]
    public partial string Badge { get; set; } = "";

    /// <summary>What Narrator reads for the list item.</summary>
    public override string ToString() => Label;
}

/// <summary>
/// The main window (phase 1's design, signed off 2026-10-25): Translate, Write, Review, History and Clipboard in the
/// side pane, Settings at its foot, and the page that's open.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>How long a short message stays, and one with an action (Undo): Lamha 2's <c>flash()</c> in popup.js.</summary>
    private static readonly TimeSpan ToastTime = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan ToastActionTime = TimeSpan.FromSeconds(5);

    private CancellationTokenSource? _toast;
    private Action? _toastRun;

    public MainViewModel()
    {
        Pages =
        [
            new("translate", Localization.Get("Main_Translate"), Glyph.Translate),
            new("write", Localization.Get("Main_Write"), Glyph.Write),
            new("review", Localization.Get("Main_Review"), Glyph.Review),
            new("history", Localization.Get("Main_History"), Glyph.History),
            new("clipboard", Localization.Get("Main_Clipboard"), Glyph.Clipboard),
        ];
        Footer = [new("settings", Localization.Get("Common_Settings"), Glyph.Settings)];
        Page = Pages[0];
    }

    public List<NavItem> Pages { get; }

    /// <summary>Settings, at the foot of the pane.</summary>
    public List<NavItem> Footer { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial NavItem Page { get; set; }

    public string Title => Page.Label;

    /// <summary>The pane is only icons: the window is narrow.</summary>
    [ObservableProperty]
    public partial bool IsFolded { get; set; }

    /// <summary>"Copied", "History cleared": at the foot of the window for a moment.</summary>
    [ObservableProperty]
    public partial string Toast { get; private set; } = "";

    /// <summary>The toast's button ("Undo"), or "".</summary>
    [ObservableProperty]
    public partial string ToastAction { get; private set; } = "";

    /// <summary>Shows <paramref name="text"/>, with <paramref name="action"/> to <paramref name="run"/> if given; a new one replaces it.</summary>
    public async void ShowToast(string text, string? action = null, Action? run = null)
    {
        _toast?.Cancel();
        var mine = _toast = new CancellationTokenSource();
        Toast = text;
        ToastAction = run is null ? "" : action ?? "";
        _toastRun = run;
        try
        {
            await Task.Delay(run is null ? ToastTime : ToastActionTime, mine.Token).ConfigureAwait(true);
            HideToast();
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>The count on Review: words due and new ones for today (Lamha 2's <c>rvBadge</c>).</summary>
    public void SetReviewCount(int count) => Find("review").Badge = count > 0 ? Localization.Number(count) : "";

    /// <summary>The page <paramref name="id"/> ("translate", "settings"…), or Translate for one that doesn't exist.</summary>
    public NavItem Find(string id) => Pages.Concat(Footer).FirstOrDefault(p => p.Id == id) ?? Pages[0];

    [RelayCommand]
    private void RunToastAction()
    {
        var run = _toastRun;
        _toast?.Cancel();
        HideToast();
        run?.Invoke();
    }

    private void HideToast()
    {
        Toast = "";
        ToastAction = "";
        _toastRun = null;
    }

    partial void OnIsFoldedChanged(bool value)
    {
        foreach (var item in Pages.Concat(Footer))
        {
            item.Tooltip = value ? item.Label : null;
        }
    }
}
