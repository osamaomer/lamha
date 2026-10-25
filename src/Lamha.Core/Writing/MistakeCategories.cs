namespace Lamha.Core.Writing;

/// <summary>
/// The kinds of mistake proofreading sorts into (Lamha 2's <c>CATEGORIES</c> in shared/lamha-ai.js). Their names and
/// rules in Arabic and English are interface strings (Strings/*/Resources.resw); here are only the ids and the English
/// names that go into the prompt.
/// </summary>
public static class MistakeCategories
{
    public const string Other = "other";

    /// <summary>Each category's id and its name in the proofreading prompt, in Lamha 2's order.</summary>
    public static readonly IReadOnlyList<(string Id, string PromptName)> All =
    [
        ("articles", "articles (a/an/the)"),
        ("prepositions", "prepositions"),
        ("verb_tense", "verb tenses"),
        ("agreement", "subject–verb agreement"),
        ("plurals", "singular and plural nouns"),
        ("word_choice", "word choice"),
        ("spelling", "spelling"),
        ("punctuation", "punctuation and capitalization"),
        ("word_order", "word order"),
        ("sentence_structure", "sentence structure (run-ons, fragments)"),
        (Other, "other"),
    ];

    /// <summary>A known category (Lamha 2's <c>isCategory</c>: "constructor" from a model's reply isn't one).</summary>
    public static bool IsKnown(string? id) => id is not null && All.Any(c => c.Id == id);

    public static string PromptName(string id) => All.FirstOrDefault(c => c.Id == id).PromptName ?? id;
}
