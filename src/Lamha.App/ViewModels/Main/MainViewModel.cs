using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lamha.App.Services;

namespace Lamha.App.ViewModels.Main;

/// <summary>A page in the side pane: its id (which names its coloured icon, Controls/PaneIcon) and its name.</summary>
public sealed partial class NavItem(string id, string label) : ObservableObject
{
    public string Id { get; } = id;

    public string Label { get; } = label;

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
            new("translate", Localization.Get("Main_Translate")),
            new("write", Localization.Get("Main_Write")),
            new("review", Localization.Get("Main_Review")),
            new("history", Localization.Get("Main_History")),
            new("clipboard", Localization.Get("Main_Clipboard")),
        ];
        Footer = [new("settings", Localization.Get("Common_Settings"))];
        Page = Pages[0];
    }

    public List<NavItem> Pages { get; }

    /// <summary>Settings, at the foot of the pane.</summary>
    public List<NavItem> Footer { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(ShowsTitle))]
    public partial NavItem Page { get; set; }

    public string Title => Page.Label;

    /// <summary>The window shows the page's title; Settings shows its own breadcrumb instead.</summary>
    public bool ShowsTitle => Page.Id != "settings";

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
}
