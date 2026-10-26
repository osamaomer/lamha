using Lamha.App.Controls;
using Lamha.App.Services;
using Lamha.Core.Search;

namespace Lamha.App.ViewModels.Main;

/// <summary>One of Settings' categories: its id, its icon, its name's string, and the strings of the settings named under it.</summary>
public sealed record SettingsCategory(string Id, Glyph Glyph, string Title, string[] Summary);

/// <summary>
/// Settings' categories, in Lamha 2's order (phase 1's design): Settings' home lists them, and the title bar's search
/// finds them and the settings each one names.
/// </summary>
public static class SettingsCategories
{
    public static readonly IReadOnlyList<SettingsCategory> All =
    [
        new("languages", Glyph.Translate, "Settings_Languages", ["Settings_UiLang", "Settings_TargetLang", "Settings_Reverse"]),
        new("card", Glyph.Card, "Settings_Card", ["Settings_Context", "Settings_Explain", "Settings_Wiki"]),
        // The design's summary names the clipboard panel's shortcut: back in it with the panel.
        new("keys", Glyph.Keyboard, "Settings_Keys", ["Settings_HkLookup", "Settings_HkWrite", "Settings_KeyClose"]),
        new("writing", Glyph.Sparkle, "Settings_WritingTools", ["Settings_AiProvider", "Settings_Model"]),
        new("translation", Glyph.Globe, "Settings_Translation", ["Settings_TrAuto", "Settings_TrProvider", "Settings_Model"]),
        new("review", Glyph.Review, "Settings_Review", ["Settings_CardsAuto", "Settings_NewPerDay", "Settings_Goal"]),
        new("offline", Glyph.Book, "Settings_Offline", ["Settings_LocalFirst", "Settings_Packs"]),
        new("clipboard", Glyph.Clipboard, "Settings_Clipboard", ["Settings_CbEnable", "Settings_CbMax", "Settings_CbApps"]),
        new("appearance", Glyph.Palette, "Settings_Appearance", ["Settings_Theme", "Settings_Motion", "Settings_CardBg"]),
        new("privacy", Glyph.Shield, "Settings_Privacy", ["Settings_SaveHistory", "Settings_Backup", "Settings_Report"]),
        new("about", Glyph.Info, "Common_Lamha", ["Settings_UpAuto", "Tray_StartWithWindows", "Settings_Credits"]),
    ];

    /// <summary>A category's line under its name: the settings it names, joined.</summary>
    public static string Summary(SettingsCategory category) => string.Join(" · ", category.Summary.Select(Localization.Get));

    /// <summary>What the search can find in Settings: each category, then each setting it names, under its category's name.</summary>
    public static IEnumerable<SettingEntry> Entries()
    {
        foreach (var category in All)
        {
            var title = Localization.Get(category.Title);
            yield return new SettingEntry(title, Summary(category), category.Id);
            foreach (var setting in category.Summary)
            {
                yield return new SettingEntry(Localization.Get(setting), title, category.Id);
            }
        }
    }
}
