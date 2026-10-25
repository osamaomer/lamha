namespace Lamha.Core.Dictionary;

/// <summary>A sense of a word: WordNet's definition, example and synonyms, and Arabic WordNet's words for it.</summary>
public sealed record Sense(string Pos, string Gloss, string Example, IReadOnlyList<string> Synonyms, IReadOnlyList<string> Arabic);

/// <summary>Wiktionary's Arabic translations for one sense of a word, labelled by that sense ("financial institution").</summary>
public sealed record SenseTranslations(string Hint, IReadOnlyList<string> Arabic);

/// <summary>Wiktionary's translations for one part of speech, the most common sense first.</summary>
public sealed record PosTranslations(string Pos, IReadOnlyList<SenseTranslations> Senses);

/// <summary>
/// A word in the offline dictionary (Lamha 2's <c>{ p, s, t }</c> from tools/build_dict.py): its pronunciation, its
/// senses, and its Arabic translations by part of speech. Language packs use the same shape without the Arabic.
/// </summary>
public sealed record DictionaryEntry(string Ipa, IReadOnlyList<Sense> Senses, IReadOnlyList<PosTranslations> Translations);

/// <summary>The offline dictionary's data, read from disk on demand (decision 0006).</summary>
public interface IDictionaryData
{
    /// <summary>The entry for an English word or phrase in small letters, or null.</summary>
    DictionaryEntry? Entry(string word);

    /// <summary>The dictionary words an inflected form belongs to ("went" → "go").</summary>
    IReadOnlyList<string> Lemmas(string form);

    /// <summary>The English words for a normalized Arabic word.</summary>
    IReadOnlyList<string> English(string arabic);

    /// <summary>
    /// The words whose first two letters are <paramref name="shard"/> ("ac"), most frequent first: the word of the day
    /// picks from the middle of the list, useful without being basic.
    /// </summary>
    IReadOnlyList<string> Shard(string shard);
}
