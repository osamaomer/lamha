namespace Lamha.Core.Translation;

/// <summary>
/// Lamha's part-of-speech codes (local-dict.js <c>POS_AR</c> / <c>POS_EN</c>). The names are data for recognising
/// Google's labels, which arrive in the interface language; the app shows codes from its own strings.
/// </summary>
public static class PartsOfSpeech
{
    private static readonly (string Code, string Arabic, string English)[] Names =
    [
        ("n", "اسم", "noun"),
        ("v", "فعل", "verb"),
        ("a", "صفة", "adjective"),
        ("r", "ظرف", "adverb"),
        ("p", "حرف جر", "preposition"),
        ("o", "ضمير", "pronoun"),
        ("c", "حرف عطف", "conjunction"),
        ("i", "تعجّب", "interjection"),
        ("d", "أداة", "determiner"),
        ("u", "عدد", "number"),
        ("h", "عبارة", "phrase"),
        ("m", "اسم علم", "proper noun"),
        ("t", "أداة", "article"),
        ("e", "بالإنجليزية", "in English"), // Arabic → English results: the list of English words
    ];

    /// <summary>The code for a label in either language ("noun", "اسم" → "n"), or "".</summary>
    public static string CodeOf(string? label)
    {
        var l = (label ?? "").Trim().ToLowerInvariant();
        foreach (var (code, arabic, english) in Names)
        {
            if (l == english || l == arabic)
            {
                return code;
            }
        }

        return "";
    }

    /// <summary>A code's names in both languages.</summary>
    public static IReadOnlyList<string> NamesOf(string code) =>
        [.. Names.Where(n => n.Code == code).SelectMany(n => new[] { n.Arabic, n.English })];

    /// <summary>The AI's part-of-speech words for a sense (Lamha 2's <c>EXPLAIN_POS</c>): noun → "n"…; "other" → "".</summary>
    public static string FromAi(string? pos) => pos switch
    {
        "noun" => "n",
        "verb" => "v",
        "adjective" => "a",
        "adverb" => "r",
        "preposition" => "p",
        "pronoun" => "o",
        "conjunction" => "c",
        "interjection" => "i",
        "phrase" => "h",
        _ => "",
    };

    /// <summary>The words the AI may answer with, in its schema.</summary>
    public static readonly IReadOnlyList<string> AiWords = ["noun", "verb", "adjective", "adverb", "preposition", "pronoun", "conjunction", "interjection", "phrase", "other"];
}
