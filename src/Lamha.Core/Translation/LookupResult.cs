namespace Lamha.Core.Translation;

/// <summary>Who answered a lookup.</summary>
public enum ResultSource
{
    /// <summary>Google.</summary>
    Online,

    /// <summary>The offline dictionary or a language pack.</summary>
    Local,

    /// <summary>The AI (the card says which one, and marks a guess).</summary>
    Ai,
}

/// <summary>
/// A part of speech: Lamha's code ("n", "v", "a"…, shown in the interface language by the app) or, for Google's
/// definitions, Google's own label (already in the interface language). One of the two is set.
/// </summary>
public readonly record struct PartOfSpeech(string Code, string Label)
{
    public static PartOfSpeech Of(string code) => new(code, "");

    public static PartOfSpeech FromGoogle(string? label) => new(PartsOfSpeech.CodeOf(label), label ?? "");

    public bool IsEmpty => Code.Length == 0 && Label.Length == 0;

    /// <summary>The same part of speech, whether named by code or by label.</summary>
    public bool SameAs(PartOfSpeech other) => Code.Length > 0 ? Code == other.Code : Label == other.Label;
}

/// <summary>A translation in the dictionary list: a word, its translations back, and the sense it's for.</summary>
public sealed record DictTerm
{
    public required string Word { get; init; }

    /// <summary>Google: the word's translations back into the source language.</summary>
    public IReadOnlyList<string> Back { get; init; } = [];

    public double Score { get; init; }

    /// <summary>The offline dictionary: the sense this translation is for ("financial institution").</summary>
    public string Hint { get; init; } = "";

    /// <summary>The offline dictionary: this translation fits the sentence.</summary>
    public bool Best { get; init; }
}

/// <summary>Translations grouped by part of speech.</summary>
public sealed record DictGroup(PartOfSpeech Pos, IReadOnlyList<DictTerm> Terms);

/// <summary>A definition, with its example and synonyms.</summary>
public sealed record Definition
{
    public required string Gloss { get; init; }

    public string Example { get; init; } = "";

    public IReadOnlyList<string> Synonyms { get; init; } = [];

    /// <summary>The definition translated (Arabic-first: Google's English definitions in the translation language).</summary>
    public string GlossTranslated { get; init; } = "";

    /// <summary>The offline dictionary: this sense's Arabic words (not shown in the English–English view).</summary>
    public IReadOnlyList<string> Arabic { get; init; } = [];

    /// <summary>The offline dictionary: the definition that fits the sentence.</summary>
    public bool Best { get; init; }
}

/// <summary>Definitions grouped by part of speech; <see cref="Base"/> is Google's base form ("mention" for "mentioned").</summary>
public sealed record DefinitionGroup(PartOfSpeech Pos, IReadOnlyList<Definition> Entries, string Base = "");

/// <summary>How the word reads inside its sentence's translation.</summary>
/// <param name="Word">The word's translation in the sentence ("ضفة" for "bank" in "the bank of the river").</param>
/// <param name="Before">The translated sentence before it.</param>
/// <param name="After">The translated sentence after it.</param>
/// <param name="Untranslated">The translation kept the word as it was: a name ("Gemini"), not its meaning.</param>
public sealed record ContextMeaning(string Word, string Before, string After, bool Untranslated = false);

/// <summary>
/// What a lookup gives the card: a word's meanings and definitions, or a text's translation. Lamha 2's lookup result
/// object, field for field (its names in the comments where they differ).
/// </summary>
public sealed record LookupResult
{
    /// <summary>The word or text (for a word, its dictionary form: "go" for "went").</summary>
    public required string Query { get; init; }

    /// <summary>A word (or short phrase) rather than a text (<c>type: "word"</c>).</summary>
    public bool IsWord { get; init; }

    /// <summary>The source language (<c>src</c>).</summary>
    public string Source { get; init; } = "";

    /// <summary>The language of <see cref="Translation"/> (<c>tl</c>).</summary>
    public string Target { get; init; } = "";

    /// <summary>The meaning, or in the explain view the leading definition.</summary>
    public string Translation { get; init; } = "";

    public string Translit { get; init; } = "";

    /// <summary>The source's pronunciation: IPA from the dictionary, Google's transliteration (<c>srcTranslit</c>).</summary>
    public string SourceTranslit { get; init; } = "";

    /// <summary>Google's "Did you mean" (<c>spell</c>).</summary>
    public string Spell { get; init; } = "";

    public IReadOnlyList<DictGroup> Dict { get; init; } = [];

    public IReadOnlyList<DefinitionGroup> Definitions { get; init; } = [];

    public IReadOnlyList<string> Examples { get; init; } = [];

    /// <summary>Who answered (<c>source</c>).</summary>
    public ResultSource From { get; init; }

    /// <summary>The AI that answered ("Gemini"), when it did (<c>ai</c>).</summary>
    public string AiName { get; init; } = "";

    /// <summary>The AI's translation still had stray letters: shown only because nothing else worked, and not kept.</summary>
    public bool Rough { get; init; }

    /// <summary>A single word only the AI answered: a guess, said so on the card and kept out of the history.</summary>
    public bool AiGuess { get; init; }

    /// <summary>The word explained in its own language, not translated (<c>mode: "explain"</c>).</summary>
    public bool Explain { get; init; }

    /// <summary>The explain view: the language of <see cref="Meaning"/>.</summary>
    public string Other { get; init; } = "";

    /// <summary>The explain view: the leading definition's example.</summary>
    public string HeroExample { get; init; } = "";

    public PartOfSpeech HeroPos { get; init; }

    /// <summary>The leading meaning or definition is the one that fits the sentence.</summary>
    public bool ContextSense { get; init; }

    /// <summary>The explain view: the word's translation, shown under the definition (<c>ar</c>).</summary>
    public string Meaning { get; init; } = "";

    /// <summary>Arabic explained in Arabic: its root letters ("ك ت ب").</summary>
    public string Root { get; init; } = "";

    /// <summary>Arabic explained in Arabic: its plural.</summary>
    public string Plural { get; init; } = "";

    /// <summary>The form the user selected when <see cref="Query"/> is its dictionary form ("maisons").</summary>
    public string Inflected { get; init; } = "";

    /// <summary>The offline dictionary: the word is itself a form of this one.</summary>
    public string FormOf { get; init; } = "";

    /// <summary>The offline dictionary: the fitting sense has no Arabic word; its definition instead.</summary>
    public string BestGloss { get; init; } = "";

    /// <summary>The word in its sentence's translation (<c>context</c>).</summary>
    public ContextMeaning? Context { get; init; }

    /// <summary>The word couldn't be explained in its own language: the card says why (<c>explainMissing</c>).</summary>
    public bool ExplainMissing { get; init; }

    /// <summary>This lookup reached a milestone of words looked up (10, 50, 100…).</summary>
    public int Milestone { get; init; }

    /// <summary>This lookup reached the day's goal.</summary>
    public int Goal { get; init; }
}
