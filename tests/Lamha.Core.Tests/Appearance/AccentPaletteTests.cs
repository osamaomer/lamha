using Lamha.Core.Appearance;

namespace Lamha.Core.Tests.Appearance;

/// <summary>Lamha 2's test-desktop.mjs, "Windows' accent colour: readable in both themes whatever was picked".</summary>
public class AccentPaletteTests
{
    [Theory]
    [InlineData("9E6804FF")]
    [InlineData("FFD700")]
    [InlineData("FFFFFF")]
    [InlineData("000000")]
    [InlineData("0078D4")]
    [InlineData("00CC6A")]
    [InlineData("E81123")]
    [InlineData("#6B69D6")]
    public void ReadableInBothThemesWhateverWasPicked(string picked)
    {
        var (light, dark) = AccentPalette.From(picked)!.Value;
        Assert.True(Rgb.Contrast(light.Accent, AccentPalette.LightSurface) >= 4.5, "links on the light cards");
        Assert.True(Rgb.Contrast(dark.Accent, AccentPalette.DarkSurface) >= 4.5, "links on the dark cards");
        Assert.True(Rgb.Contrast(light.Button, Rgb.White) >= 4.5, "white text on its buttons");
        Assert.True(Rgb.Contrast(dark.Button, Rgb.White) >= 4.5, "white text on its buttons");
        Assert.True(Rgb.Contrast(light.Accent, light.Accent.Over(light.SoftAlpha, AccentPalette.LightSurface)) >= 4.5, "light accent on its tint");
        Assert.True(Rgb.Contrast(dark.Accent, dark.Accent.Over(dark.SoftAlpha, AccentPalette.DarkSurface)) >= 4.5, "dark accent on its tint");
    }

    [Fact]
    public void LamhaTwosExactColours()
    {
        Assert.Equal("#744da9", AccentPalette.From("744DA9FF")!.Value.Light.Accent.Hex); // readable: stays as it was
        Assert.Equal("#916004", AccentPalette.From("9E6804FF")!.Value.Light.Accent.Hex); // readable on white, not on its tint
        Assert.Equal("#006ec3", AccentPalette.From("0078D4")!.Value.Light.Accent.Hex); // Windows' blue, a shade darker
        Assert.Equal((0.09, 0.12), (AccentPalette.From("0078D4")!.Value.Light.SoftAlpha, AccentPalette.From("0078D4")!.Value.Dark.SoftAlpha));
    }

    [Theory]
    [InlineData("not a colour")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("12345")]
    public void NotAColourIsNoPalette(string? text) => Assert.Null(AccentPalette.From(text));
}
