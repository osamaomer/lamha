using Lamha.Core.Settings;
using Lamha.Core.Tests.Support;

namespace Lamha.Core.Tests;

/// <summary>Settings kept as JSON: read back as saved, and with defaults for what an older version didn't save.</summary>
public class SettingsTests
{
    [Fact]
    public void SettingsSavedBeforeASettingExistedGetItsDefault()
    {
        using var lamha = new TestLamha(new FakeNet(_ => FakeNet.Json(200, "{}")));
        lamha.State.Set("settings", """{ "dictSource": "Offline", "dailyGoal": 3 }""");
        var s = new SettingsService(lamha.State).Current;
        var defaults = new LamhaSettings();

        Assert.Equal((DictSource.Offline, 3), (s.DictSource, s.DailyGoal));
        Assert.Equal(defaults.TargetLang, s.TargetLang);
        Assert.Equal(defaults.Currency, s.Currency);
        Assert.Equal(defaults.ShowWikipedia, s.ShowWikipedia);
        Assert.NotNull(s.TrModels);
        Assert.Equal(defaults.ClipboardExcludedApps, s.ClipboardExcludedApps);
        Assert.True(s.WindowMica); // on by default, unlike Lamha 2's: the main window is designed on Mica
    }

    [Fact]
    public void DamagedSettingsStartOverFromTheDefaults()
    {
        using var lamha = new TestLamha(new FakeNet(_ => FakeNet.Json(200, "{}")));
        lamha.State.Set("settings", "[1, 2]");
        Assert.Equal(new LamhaSettings().TargetLang, new SettingsService(lamha.State).Current.TargetLang);
    }
}
