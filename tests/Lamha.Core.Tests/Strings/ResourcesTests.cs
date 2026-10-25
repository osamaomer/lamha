using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Lamha.Core.Tests.Support;

namespace Lamha.Core.Tests.Strings;

/// <summary>
/// The app's strings (src/Lamha.App/Strings/en|ar/Resources.resw), read as files: the two languages have the same
/// keys and placeholders, and a string whose comment names a Lamha 2 key keeps Lamha 2's wording word for word
/// (tools/Strings/lamha2-strings.json). A rewording the user approved says "reworded" in its comment.
/// </summary>
public partial class ResourcesTests
{
    private static readonly Lazy<Dictionary<string, (string Value, string Comment)>> English = new(() => Read("en"));
    private static readonly Lazy<Dictionary<string, (string Value, string Comment)>> Arabic = new(() => Read("ar"));

    [Fact]
    public void BothLanguagesHaveTheSameStrings()
    {
        var onlyEnglish = English.Value.Keys.Except(Arabic.Value.Keys).ToList();
        var onlyArabic = Arabic.Value.Keys.Except(English.Value.Keys).ToList();
        Assert.True(onlyEnglish.Count == 0 && onlyArabic.Count == 0, $"only in English: {string.Join(", ", onlyEnglish)}; only in Arabic: {string.Join(", ", onlyArabic)}");
    }

    [Fact]
    public void NoStringIsEmptyAndThePlaceholdersMatch()
    {
        foreach (var (name, en) in English.Value)
        {
            Assert.False(string.IsNullOrWhiteSpace(en.Value), $"{name} is empty in English");
            if (Arabic.Value.TryGetValue(name, out var ar))
            {
                Assert.False(string.IsNullOrWhiteSpace(ar.Value), $"{name} is empty in Arabic");
                Assert.True(Placeholders(en.Value).SetEquals(Placeholders(ar.Value)), $"{name}: the placeholders differ between English and Arabic");
            }
        }
    }

    [Fact]
    public void LamhaTwosStringsKeepTheirWording()
    {
        var lamha2 = Lamha2Strings();
        var checkedCount = 0;
        foreach (var (language, strings, index) in new[] { ("en", English.Value, 1), ("ar", Arabic.Value, 0) })
        {
            foreach (var (name, (value, comment)) in strings)
            {
                var key = Lamha2Key(comment);
                if (key is null || !lamha2.TryGetValue(key, out var pair) || comment.Contains("reworded", StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.True(Unnumbered(value) == Unnumbered(pair[index]), $"{language} {name}: \"{value}\", Lamha 2's {key} is \"{pair[index]}\"");
                checkedCount++;
            }
        }

        Assert.True(checkedCount > 0, "no string names a Lamha 2 key");
    }

    /// <summary>A comment that names a Lamha 2 key starts with it: "c.replace", or "d.hotkeyTakenBody: the keys joined".</summary>
    private static string? Lamha2Key(string comment)
    {
        var first = comment.Split([' ', ':', ','], 2)[0].Trim();
        return first.Contains('.', StringComparison.Ordinal) ? first : null;
    }

    private static HashSet<string> Placeholders(string value) => [.. Placeholder().Matches(value).Select(m => m.Value)];

    /// <summary>Lamha 2 names its placeholders ({keys}), the .resw files number them ({0}).</summary>
    private static string Unnumbered(string value) => Placeholder().Replace(value, "{}");

    private static Dictionary<string, (string Value, string Comment)> Read(string language)
    {
        var file = Path.Combine(RealDictionary.RepositoryRoot, "src", "Lamha.App", "Strings", language, "Resources.resw");
        return XDocument.Load(file).Root!.Elements("data").ToDictionary(
            d => (string)d.Attribute("name")!,
            d => ((string?)d.Element("value") ?? "", (string?)d.Element("comment") ?? ""));
    }

    private static Dictionary<string, string[]> Lamha2Strings()
    {
        var file = Path.Combine(RealDictionary.RepositoryRoot, "tools", "Strings", "lamha2-strings.json");
        using var json = JsonDocument.Parse(File.ReadAllText(file));
        return json.RootElement.GetProperty("strings").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.EnumerateArray().Select(v => v.GetString()!).ToArray());
    }

    [GeneratedRegex(@"\{\w*\}")]
    private static partial Regex Placeholder();
}
