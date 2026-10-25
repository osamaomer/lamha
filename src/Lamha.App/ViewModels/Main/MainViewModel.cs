using CommunityToolkit.Mvvm.ComponentModel;
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

    /// <summary>What Narrator reads for the list item.</summary>
    public override string ToString() => Label;
}

/// <summary>
/// The main window (phase 1's design, signed off 2026-10-25): Translate, Write, Review, History and Clipboard in the
/// side pane, Settings at its foot, and the page that's open.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
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

    /// <summary>The page <paramref name="id"/> ("translate", "settings"…), or Translate for one that doesn't exist.</summary>
    public NavItem Find(string id) => Pages.Concat(Footer).FirstOrDefault(p => p.Id == id) ?? Pages[0];

    partial void OnIsFoldedChanged(bool value)
    {
        foreach (var item in Pages.Concat(Footer))
        {
            item.Tooltip = value ? item.Label : null;
        }
    }
}
