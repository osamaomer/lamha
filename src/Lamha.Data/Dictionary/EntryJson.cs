using System.Text.Json;
using Lamha.Core.Dictionary;

namespace Lamha.Data.Dictionary;

/// <summary>
/// A dictionary entry as tools/build_dict.py writes it: <c>{ "p": ipa, "s": [[pos, gloss, example, [synonyms],
/// [arabic]]…], "t": { pos: [[senseHint, [arabic…]]…] } }</c>. The dictionary file keeps each entry in this JSON, so
/// the builder copies it as it is; it's read here only when a word is looked up.
/// </summary>
internal static class EntryJson
{
    public static DictionaryEntry Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var ipa = root.TryGetProperty("p", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : "";

        var senses = new List<Sense>();
        if (root.TryGetProperty("s", out var s) && s.ValueKind == JsonValueKind.Array)
        {
            foreach (var sense in s.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Array))
            {
                senses.Add(new Sense(Str(sense, 0), Str(sense, 1), Str(sense, 2), Strings(sense, 3), Strings(sense, 4)));
            }
        }

        var translations = new List<PosTranslations>();
        if (root.TryGetProperty("t", out var t) && t.ValueKind == JsonValueKind.Object)
        {
            foreach (var pos in t.EnumerateObject().Where(x => x.Value.ValueKind == JsonValueKind.Array))
            {
                var groups = pos.Value.EnumerateArray()
                    .Where(g => g.ValueKind == JsonValueKind.Array)
                    .Select(g => new SenseTranslations(Str(g, 0), Strings(g, 1)))
                    .ToList();
                translations.Add(new PosTranslations(pos.Name, groups));
            }
        }

        return new DictionaryEntry(ipa, senses, translations);
    }

    /// <summary>A JSON array of strings.</summary>
    public static IReadOnlyList<string> StringList(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.Array
            ? [.. doc.RootElement.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!)]
            : [];
    }

    private static string Str(JsonElement array, int index) =>
        array.GetArrayLength() > index && array[index].ValueKind == JsonValueKind.String ? array[index].GetString()! : "";

    private static List<string> Strings(JsonElement array, int index) =>
        array.GetArrayLength() > index && array[index].ValueKind == JsonValueKind.Array
            ? [.. array[index].EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!)]
            : [];
}
