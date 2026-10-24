using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Lamha.Core.Appearance;
using Lamha.Core.Tests.Support;

namespace Lamha.Core.Tests.Appearance;

/// <summary>
/// The app's colours (src/Lamha.App/Themes/Tokens.xaml), read as a file: they're Lamha 2's, text reaches WCAG AA in
/// both themes, and high contrast uses the user's own colours. Lamha 2's test-ui.mjs, "colours: text reaches 4.5:1 and
/// field edges 3:1 in both themes, tints included (WCAG 1.4.3, 1.4.11); high contrast has real edges", and its check
/// that the card's copy of the colours matches the pages'.
/// </summary>
public partial class TokensTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly Lazy<Dictionary<string, Dictionary<string, XElement>>> Themes = new(ReadThemes);

    /// <summary>Each brush and the Lamha 2 colour it carries: shared/ui.css, or the card's content/styles.js.</summary>
    public static TheoryData<string, string, string> FromLamha2 => new()
    {
        { "LamhaPageBrush", "ui", "bg" },
        { "LamhaSurfaceBrush", "ui", "surface" },
        { "LamhaTextBrush", "ui", "fg" },
        { "LamhaTextMutedBrush", "ui", "muted" },
        { "LamhaTextFaintBrush", "ui", "faint" },
        { "LamhaLineBrush", "ui", "line" },
        { "LamhaFieldLineBrush", "ui", "field-line" },
        { "LamhaHoverBrush", "ui", "hover" },
        { "LamhaChipBrush", "card", "chip" },
        { "LamhaSkeletonBrush", "card", "skeleton" },
        { "LamhaAccentBrush", "ui", "accent" },
        { "LamhaAccentSoftBrush", "ui", "accent-soft" },
        { "LamhaButtonBrush", "ui", "btn" },
        { "LamhaButtonTextBrush", "ui", "btn-fg" },
        { "LamhaAccentTextBrush", "ui", "accent-fg" },
        { "LamhaOkBrush", "ui", "ok" },
        { "LamhaOkSoftBrush", "ui", "ok-soft" },
        { "LamhaDangerBrush", "ui", "danger" },
        { "LamhaDangerSoftBrush", "ui", "danger-soft" },
        { "LamhaWarnBackgroundBrush", "ui", "warn-bg" },
        { "LamhaWarnTextBrush", "ui", "warn-fg" },
        { "LamhaToastBackgroundBrush", "ui", "toast-bg" },
        { "LamhaToastTextBrush", "ui", "toast-fg" },
        { "LamhaToastActionBrush", "ui", "toast-act" },
    };

    [Theory]
    [MemberData(nameof(FromLamha2))]
    public void TheColoursAreLamhaTwos(string key, string file, string variable)
    {
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var css = Lamha2Colours(file, theme)[variable];
            var (rgb, alpha) = Brush(theme, key);
            Assert.True(rgb == css.Rgb && alpha == css.Alpha, $"{theme} {key}: {rgb.Hex} at {alpha}, Lamha 2's --{variable} is {css.Rgb.Hex} at {css.Alpha}");
        }
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void TextReachesAaOnEveryGroundTintsIncluded(string theme)
    {
        var surface = Ground(theme, "LamhaSurfaceBrush", Rgb.White);
        var page = Ground(theme, "LamhaPageBrush", surface);
        var grounds = new Dictionary<string, Rgb>
        {
            ["LamhaPageBrush"] = page,
            ["LamhaSurfaceBrush"] = surface,
            ["LamhaAccentSoftBrush"] = Ground(theme, "LamhaAccentSoftBrush", surface),
            ["LamhaDangerSoftBrush"] = Ground(theme, "LamhaDangerSoftBrush", surface),
            ["LamhaOkSoftBrush"] = Ground(theme, "LamhaOkSoftBrush", surface),
            ["LamhaChipBrush"] = Ground(theme, "LamhaChipBrush", surface),
            ["LamhaButtonBrush"] = Ground(theme, "LamhaButtonBrush", surface),
            ["LamhaDangerSolidBrush"] = Ground(theme, "LamhaDangerSolidBrush", surface),
            ["LamhaWarnBackgroundBrush"] = Ground(theme, "LamhaWarnBackgroundBrush", surface),
            ["LamhaToastBackgroundBrush"] = Ground(theme, "LamhaToastBackgroundBrush", surface),
        };

        void Need(string text, string ground, double min = 4.5)
        {
            var under = grounds[ground];
            var ratio = Rgb.Contrast(Ground(theme, text, under), under);
            Assert.True(ratio >= min, $"{theme}: {text} on {ground} is {ratio:F2}:1, needs {min}");
        }

        foreach (var text in new[] { "LamhaTextBrush", "LamhaTextMutedBrush", "LamhaTextFaintBrush", "LamhaAccentBrush", "LamhaDangerBrush", "LamhaOkBrush" })
        {
            Need(text, "LamhaPageBrush");
            Need(text, "LamhaSurfaceBrush");
        }

        // Selected chips, the active section, "In this context"
        foreach (var text in new[] { "LamhaAccentBrush", "LamhaTextMutedBrush", "LamhaTextBrush" })
        {
            Need(text, "LamhaAccentSoftBrush");
        }

        Need("LamhaDangerBrush", "LamhaDangerSoftBrush");
        Need("LamhaOkBrush", "LamhaOkSoftBrush");

        // New in Lamha 3: the other text-on-colour pairs
        Need("LamhaTextBrush", "LamhaChipBrush");
        Need("LamhaTextMutedBrush", "LamhaChipBrush");
        Need("LamhaButtonTextBrush", "LamhaButtonBrush");
        Need("LamhaDangerSolidTextBrush", "LamhaDangerSolidBrush");
        Need("LamhaWarnTextBrush", "LamhaWarnBackgroundBrush");
        Need("LamhaToastTextBrush", "LamhaToastBackgroundBrush");
        Need("LamhaToastActionBrush", "LamhaToastBackgroundBrush");

        // A field's edge, drawn over its own fill (the page colour), against the surface around it and the fill inside;
        // the switch's track when on (WCAG 1.4.11)
        var edge = Ground(theme, "LamhaFieldLineBrush", page);
        Assert.True(Rgb.Contrast(edge, surface) >= 3, $"{theme}: field edge on the surface {Rgb.Contrast(edge, surface):F2}:1");
        Assert.True(Rgb.Contrast(edge, page) >= 3, $"{theme}: field edge on the page {Rgb.Contrast(edge, page):F2}:1");
        var on = Ground(theme, "LamhaSwitchOnBrush", surface);
        Assert.True(Rgb.Contrast(on, surface) >= 3, $"{theme}: the switch when on {Rgb.Contrast(on, surface):F2}:1");
    }

    [Fact]
    public void EveryThemeHasEveryToken()
    {
        var light = Themes.Value["Light"].Keys.Order();
        Assert.Equal(light, Themes.Value["Dark"].Keys.Order());
        Assert.Equal(light, Themes.Value["HighContrast"].Keys.Order());
    }

    [Fact]
    public void HighContrastUsesOnlyTheUsersColoursWithRealEdges()
    {
        foreach (var (key, element) in Themes.Value["HighContrast"])
        {
            var source = element.Name.LocalName switch
            {
                "SolidColorBrush" => SystemColour().Match(element.Attribute("Color")?.Value ?? "").Groups[1].Value,
                "StaticResource" => element.Attribute("ResourceKey")?.Value ?? "",
                "Double" => "a number",
                _ => "",
            };
            Assert.True(source.StartsWith("SystemColor", StringComparison.Ordinal) || source == "a number", $"{key} in high contrast: {element}");
        }

        // Lamha draws edges with the line brushes: in high contrast they're the text colour, so every box still shows
        foreach (var key in new[] { "LamhaLineBrush", "LamhaFieldLineBrush" })
        {
            Assert.Contains("SystemColorWindowTextColor", Themes.Value["HighContrast"][key].Attribute("Color")!.Value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheCardsTintIsTheSurface()
    {
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var surface = Brush(theme, "LamhaSurfaceBrush").Rgb;
            Assert.Equal(surface, Colour(Themes.Value[theme]["LamhaCardTintColor"].Value).Rgb);
            Assert.Equal(surface, Colour(Themes.Value[theme]["LamhaCardFallbackColor"].Value).Rgb);
        }
    }

    [GeneratedRegex(@"^\{ThemeResource (SystemColor\w+Color)\}$")]
    private static partial Regex SystemColour();

    [GeneratedRegex(@"--([\w-]+):\s*([^;]+);")]
    private static partial Regex CssVariable();

    [GeneratedRegex(@"/\*[\s\S]*?\*/")]
    private static partial Regex CssComment();

    private static Dictionary<string, Dictionary<string, XElement>> ReadThemes()
    {
        var path = Path.Combine(RealDictionary.RepositoryRoot, "src", "Lamha.App", "Themes", "Tokens.xaml");
        var themes = XDocument.Load(path).Root!.Elements().Single(e => e.Name.LocalName == "ResourceDictionary.ThemeDictionaries");
        return themes.Elements().ToDictionary(
            d => d.Attribute(X + "Key")!.Value,
            d => d.Elements().ToDictionary(e => e.Attribute(X + "Key")!.Value));
    }

    /// <summary>A brush's colour: "#RRGGBB" or XAML's "#AARRGGBB".</summary>
    private static (Rgb Rgb, int Alpha) Brush(string theme, string key) =>
        Colour(Themes.Value[theme][key].Attribute("Color")!.Value);

    private static (Rgb Rgb, int Alpha) Colour(string text)
    {
        var s = text.Trim().TrimStart('#');
        var v = uint.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return (new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v), s.Length == 8 ? (int)(v >> 24) : 255);
    }

    /// <summary>What a brush looks like over <paramref name="under"/>.</summary>
    private static Rgb Ground(string theme, string key, Rgb under)
    {
        var (rgb, alpha) = Brush(theme, key);
        return rgb.Over(alpha / 255.0, under);
    }

    /// <summary>Lamha 2's colours for a theme: the first block, with the dark block's on top for dark.</summary>
    private static Dictionary<string, (Rgb Rgb, int Alpha)> Lamha2Colours(string file, string theme)
    {
        var (path, light, dark) = file == "ui"
            ? (Path.Combine("legacy", "shared", "ui.css"), ":root {", ":root[data-theme=\"dark\"] {")
            : (Path.Combine("legacy", "content", "styles.js"), ".root {", ".root.dark {");
        var text = File.ReadAllText(Path.Combine(RealDictionary.RepositoryRoot, path));
        var colours = Variables(Block(text, light));
        if (theme == "Dark")
        {
            foreach (var (name, value) in Variables(Block(text, dark)))
            {
                colours[name] = value;
            }
        }

        return colours;
    }

    private static string Block(string text, string start)
    {
        var at = text.IndexOf(start, StringComparison.Ordinal);
        return text[at..text.IndexOf('}', at)];
    }

    private static Dictionary<string, (Rgb Rgb, int Alpha)> Variables(string block)
    {
        var found = new Dictionary<string, (Rgb, int)>();
        foreach (Match m in CssVariable().Matches(CssComment().Replace(block, "")))
        {
            var value = m.Groups[2].Value.Trim();
            if (Rgb.Parse(value) is { } hex && value.Length == 7)
            {
                found[m.Groups[1].Value] = (hex, 255);
            }
            else if (value.StartsWith("rgba(", StringComparison.Ordinal))
            {
                var p = value[5..^1].Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                found[m.Groups[1].Value] = (new Rgb((byte)p[0], (byte)p[1], (byte)p[2]), (int)Math.Round(p[3] * 255, MidpointRounding.AwayFromZero));
            }
        }

        return found;
    }
}
