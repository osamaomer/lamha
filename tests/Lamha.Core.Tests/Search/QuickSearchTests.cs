using Lamha.Core.Abstractions;
using Lamha.Core.Search;

namespace Lamha.Core.Tests.Search;

/// <summary>The main window's search (decision 0020): settings, words, copies, and "Translate …" last.</summary>
public class QuickSearchTests
{
    private static readonly SettingEntry[] Settings =
    [
        new("Appearance", "Theme · Animations", "appearance"),
        new("Theme", "Appearance", "appearance"),
        new("Animations", "Appearance", "appearance"),
        new("الإعدادات التلقائية", "لمحة", "about"),
    ];

    private static readonly HistoryEntry[] Words =
    [
        new("reluctant", "متردد", "en", 3),
        new("Reluctant", "غير راغب", "en", 2), // the same word again, older
        new("candid", "صريح", "en", 1),
    ];

    private static IReadOnlyList<QuickHit> Find(string query, params (string, string)[] copies) =>
        QuickSearch.Find(query, Settings, Words, copies);

    [Fact]
    public void ASettingIsFoundByItsNameWhateverTheCase()
    {
        var hit = Find("THEME")[0];
        Assert.Equal(new QuickHit(QuickKind.Setting, "Theme", "Appearance", "appearance"), hit);
    }

    [Fact]
    public void ArabicSpellingVariantsDontMatter()
    {
        Assert.Contains(Find("الاعدادات"), h => h.Kind == QuickKind.Setting && h.Target == "about"); // إ written as ا
    }

    [Fact]
    public void AWordIsFoundByItselfOrItsMeaningAndShownOnce()
    {
        var words = Find("relu").Where(h => h.Kind == QuickKind.Word).ToList();
        Assert.Equal(["reluctant"], words.Select(w => w.Text)); // the newest of the two
        Assert.Equal("متردد", words[0].Detail);
        Assert.Contains(Find("صريح"), h => h.Kind == QuickKind.Word && h.Text == "candid");
    }

    [Fact]
    public void CopiesAreTheClipboardsOwnFindingsOnOneLine()
    {
        var copies = Find("x", ("1", "First line\nsecond line"), ("2", new string('a', 120))).Where(h => h.Kind == QuickKind.Copy).ToList();
        Assert.Equal("First line", copies[0].Text);
        Assert.Equal("1", copies[0].Target);
        Assert.Equal(81, copies[1].Text.Length); // 80 and "…"
    }

    [Fact]
    public void AtMostThreeOfEachKindAndTranslateAlwaysLast()
    {
        SettingEntry[] many = [.. Enumerable.Range(1, 5).Select(i => new SettingEntry($"Setting x{i}", "", "about"))];
        HistoryEntry[] words = [.. Enumerable.Range(1, 5).Select(i => new HistoryEntry($"x{i}", "", "en", i))];
        var hits = QuickSearch.Find("x", many, words, [("1", "x"), ("2", "x"), ("3", "x"), ("4", "x")]);
        Assert.Equal(QuickSearch.PerKind, hits.Count(h => h.Kind == QuickKind.Setting));
        Assert.Equal(QuickSearch.PerKind, hits.Count(h => h.Kind == QuickKind.Word));
        Assert.Equal(QuickSearch.PerKind, hits.Count(h => h.Kind == QuickKind.Copy));
        Assert.Equal(new QuickHit(QuickKind.Translate, "x", "", "x"), hits[^1]);
    }

    [Fact]
    public void TranslateKeepsWhatWasTypedAndNothingIsSuggestedForNothing()
    {
        Assert.Equal("  Hello World ".Trim(), Find("  Hello World ")[^1].Target);
        Assert.Empty(Find("   "));
        Assert.Single(Find("zzz")); // only "Translate zzz"
    }
}
