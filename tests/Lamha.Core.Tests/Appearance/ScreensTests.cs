using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Lamha.Core.Tests.Support;

namespace Lamha.Core.Tests.Appearance;

/// <summary>
/// The design system's rule (docs/design/system.md, "How to use it"): a screen uses only what the system names. Its
/// XAML (src/Lamha.App/Views and Windows) never types a colour, a font size, a corner radius or a glyph, and every gap
/// it types is on the 4 px grid, unless it's one of Windows' own measurements, named below with where it comes from.
/// The screens' C# (their code-behind and the view models) types no colour, font size, radius or glyph either. The
/// system itself (Themes/, Controls/) is where those values live, so it isn't read here.
/// </summary>
public partial class ScreensTests
{
    /// <summary>
    /// Gaps off the grid that are Windows' measurements, taken exactly (system.md: "Where Windows has a measurement, Lamha
    /// takes it exactly, even off the 4 px grid"): "file: attribute=value", and where Windows has it.
    /// </summary>
    private static readonly Dictionary<string, string> WindowsMeasurements = new(StringComparer.Ordinal)
    {
        ["Views/ClipboardPage.xaml: Margin=4,2"] = "a list row's inset, as the pane's rows: WinUI's NavigationViewItemButtonMargin",
    };

    /// <summary>Attributes that hold a gap: a thickness (one, two or four numbers) or a spacing.</summary>
    private static readonly HashSet<string> GapAttributes =
    [
        "Margin", "Padding", "Spacing", "ColumnSpacing", "RowSpacing", "HorizontalSpacing", "VerticalSpacing", "FocusVisualMargin",
    ];

    [Fact]
    public void AScreenTypesNoColour() =>
        AssertNone(Attributes().Where(a => a.Value.TrimStart().StartsWith('#')), "a colour (use a brush from Tokens.xaml or Windows')");

    [Fact]
    public void AScreenTypesNoFontSize() =>
        AssertNone(Attributes().Where(a => a.Name == "FontSize" && IsNumber(a.Value)), "a font size (use a style or a LamhaFontSize token)");

    [Fact]
    public void AScreenTypesNoCornerRadius() =>
        AssertNone(Attributes().Where(a => a.Name == "CornerRadius" && Numbers(a.Value) is { } n && n.Any(v => v != 0)), "a corner radius (use LamhaRadiusControl or LamhaRadiusOverlay)");

    [Fact]
    public void AScreenTypesNoGlyph() =>
        AssertNone(Attributes().Where(a => a.Value.Any(IsPrivateUse)), "a glyph (use a FluentIcon by its Glyph name)");

    [Fact]
    public void EveryGapIsOnTheGrid() =>
        AssertNone(
            Attributes().Where(a => GapAttributes.Contains(a.Name) && Numbers(a.Value) is { } n && n.Any(v => v % 4 != 0)
                && !WindowsMeasurements.ContainsKey($"{a.File}: {a.Name}={a.Value}")),
            "a gap off the 4 px grid (use a LamhaSpace token or a multiple of 4; a Windows measurement is named in WindowsMeasurements)");

    [Fact]
    public void EachWindowsMeasurementIsStillUsed()
    {
        var used = Attributes().Select(a => $"{a.File}: {a.Name}={a.Value}").ToHashSet(StringComparer.Ordinal);
        var gone = WindowsMeasurements.Keys.Where(k => !used.Contains(k)).ToList();
        Assert.True(gone.Count == 0, "Named as Windows' measurements but no longer in the screens:\n" + string.Join('\n', gone));
    }

    /// <summary>
    /// An element with keyboard shortcuts hides them (<c>KeyboardAcceleratorPlacementMode="Hidden"</c>): otherwise WinUI
    /// shows the keys as its tooltip, and on a window's root that's "Alt+Left" wherever the mouse rests.
    /// </summary>
    [Fact]
    public void AShortcutIsNeverShownAsATooltip()
    {
        var found = new List<string>();
        foreach (var file in XamlFiles())
        {
            foreach (var shortcuts in XDocument.Load(file, LoadOptions.SetLineInfo).Descendants().Where(e => e.Name.LocalName.EndsWith(".KeyboardAccelerators", StringComparison.Ordinal)))
            {
                if (shortcuts.Parent is { } owner && (string?)owner.Attribute("KeyboardAcceleratorPlacementMode") != "Hidden")
                {
                    found.Add($"{Relative(file)}:{((System.Xml.IXmlLineInfo)owner).LineNumber}: {owner.Name.LocalName}");
                }
            }
        }

        Assert.True(found.Count == 0, "Keyboard shortcuts shown as a tooltip (add KeyboardAcceleratorPlacementMode=\"Hidden\"):\n" + string.Join('\n', found));
    }

    [Fact]
    public void TheScreensCodeTypesNoColourSizeRadiusOrGlyph()
    {
        var found = new List<string>();
        foreach (var file in CodeFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (TypedInCode().IsMatch(lines[i]) || lines[i].Any(IsPrivateUse))
                {
                    found.Add($"{Relative(file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(found.Count == 0, "Typed in a screen's code (a colour, font size, radius or glyph belongs to the system):\n" + string.Join('\n', found));
    }

    /// <summary>
    /// The XAML screens: every attribute, with its file and line; a setter in a screen's own style counts as the
    /// attribute it sets (<c>&lt;Setter Property="Padding" Value="8,6" /&gt;</c> as <c>Padding="8,6"</c>).
    /// </summary>
    private static IEnumerable<(string File, int Line, string Name, string Value)> Attributes()
    {
        foreach (var file in XamlFiles())
        {
            var document = XDocument.Load(file, LoadOptions.SetLineInfo);
            foreach (var element in document.Descendants())
            {
                var line = ((System.Xml.IXmlLineInfo)element).LineNumber;
                if (element.Name.LocalName == "Setter" && element.Attribute("Property") is { } property && element.Attribute("Value") is { } value)
                {
                    yield return (Relative(file), line, property.Value, value.Value);
                    continue;
                }

                foreach (var attribute in element.Attributes())
                {
                    yield return (Relative(file), ((System.Xml.IXmlLineInfo)attribute).LineNumber, attribute.Name.LocalName, attribute.Value);
                }
            }
        }
    }

    private static void AssertNone(IEnumerable<(string File, int Line, string Name, string Value)> found, string what)
    {
        var list = found.Select(a => $"{a.File}:{a.Line}: {a.Name}=\"{a.Value}\"").ToList();
        Assert.True(list.Count == 0, $"{list.Count} typed {what}:\n" + string.Join('\n', list));
    }

    private static string App => Path.Combine(RealDictionary.RepositoryRoot, "src", "Lamha.App");

    private static IEnumerable<string> XamlFiles() =>
        new[] { "Views", "Windows" }.SelectMany(d => Directory.EnumerateFiles(Path.Combine(App, d), "*.xaml")).Order(StringComparer.Ordinal);

    private static IEnumerable<string> CodeFiles() =>
        new[] { "Views", "Windows", "ViewModels" }.SelectMany(d => Directory.EnumerateFiles(Path.Combine(App, d), "*.cs", SearchOption.AllDirectories))
            .Order(StringComparer.Ordinal);

    private static string Relative(string file) => Path.GetRelativePath(App, file).Replace('\\', '/');

    /// <summary>A value of numbers only ("12", "16,8", "0 4 0 4"), or null for a binding, a resource or a word.</summary>
    private static double[]? Numbers(string value)
    {
        var parts = value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
        var numbers = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return null;
            }
        }

        return parts.Length == 0 ? null : numbers;
    }

    private static bool IsNumber(string value) => Numbers(value) is { Length: 1 };

    /// <summary>Segoe Fluent Icons' glyphs are in Unicode's private use area.</summary>
    private static bool IsPrivateUse(char c) => c is >= '' and <= '';

    /// <summary>A colour, a font size or a corner radius typed in C#.</summary>
    [GeneratedRegex(@"Color\.FromArgb\(|ColorHelper\.FromArgb\(|FontSize\s*=\s*[0-9]|new CornerRadius\(\s*[1-9]|""\\u[eEfF][0-9a-fA-F]{3}""")]
    private static partial Regex TypedInCode();
}
