using System.Text.RegularExpressions;
using Lamha.Core.Text;
using Lamha.Core.Translation;

namespace Lamha.Core.Dictionary;

/// <summary>The sentence a word was selected in: up to 300 characters on each side.</summary>
public sealed record WordContext(string Before, string After);

/// <summary>The word of the day from the dictionary.</summary>
public sealed record DictionaryWord(string Word, string Meaning, string Definition, string Example, PartOfSpeech Pos, string Ipa);

/// <summary>
/// The offline dictionary: English → Arabic, Arabic → English, and English–English, picking the sense that fits the
/// sentence. Lamha 2's local-dict.js, rule for rule, over <see cref="IDictionaryData"/> instead of JSON shards.
/// </summary>
public sealed partial class LocalDictionary(IDictionaryData data)
{
    /// <summary>
    /// Shards whose middles hold useful words for the word of the day (Lamha 2's <c>WOTD_SHARDS</c>).
    /// </summary>
    private static readonly string[] WordOfDayShards =
    [
        "ac", "ad", "ap", "ba", "be", "ca", "ch", "co", "cr", "de", "di", "ef", "en", "ev", "ex", "fa", "fr", "ge",
        "gr", "ha", "im", "in", "ma", "me", "mo", "ob", "pa", "pe", "pr", "re", "se", "st", "su", "tr", "va", "wi",
    ];

    private static readonly HashSet<string> Stop = [.. ("the a an and or but of to in on at for with by from as is are was were be been being it its this that " +
        "these those his her their our your my we you they he she not no so than then there here what which who whom whose when " +
        "where why how all any some more most other such only own same too very can will just should now into over under again " +
        "once about against between through during before after above below up down out off also may might must would could has " +
        "have had do does did get got one").Split(' ')];

    private static readonly HashSet<string> Determiners = [.. "a an the my your his its our their this that these those every each another no some any".Split(' ')];

    private static readonly HashSet<string> BeforeVerb = [.. "i you we they he she to will would can could should must may might shall did do does didn't don't doesn't won't can't cannot never always often also just please".Split(' ')];

    private static readonly HashSet<string> Auxiliary = [.. "have has had having am is are was were be been being".Split(' ')];

    /// <summary>WordNet "morphy"-style suffix rules plus a few extras (doubling, -ly, -ied).</summary>
    private static readonly (string Suffix, string Replacement)[] Rules =
    [
        ("ies", "y"), ("ied", "y"), ("ier", "y"), ("iest", "y"), ("ily", "y"),
        ("ches", "ch"), ("shes", "sh"), ("sses", "ss"), ("xes", "x"), ("zes", "z"), ("ses", "s"), ("men", "man"),
        ("ing", ""), ("ing", "e"), ("ed", ""), ("ed", "e"), ("es", ""), ("es", "e"), ("s", ""),
        ("est", ""), ("est", "e"), ("er", ""), ("er", "e"), ("ly", ""), ("'s", ""), ("s'", "s"),
    ];

    /// <summary>An English word or short phrase → its Arabic meanings and definitions, or null when it isn't here.</summary>
    public LookupResult? LookupEnglishToArabic(string text, WordContext? context)
    {
        var hit = Find(text);
        return hit is null ? null : ToResult(text.Trim().ToLowerInvariant(), hit.Value.Lemma, hit.Value.Entry, hit.Value.FormOf, context, "");
    }

    /// <summary>
    /// An English word → its English–English view, or null when it has no definition here. The leading definition is the
    /// one that fits the sentence, else the first; <see cref="LookupResult.Meaning"/> keeps the Arabic for the review card.
    /// <paramref name="contextArabic"/>: how Google translated the word inside its sentence (when online), to pick the
    /// sense with that meaning.
    /// </summary>
    public LookupResult? LookupEnglish(string text, WordContext? context, string contextArabic = "")
    {
        var hit = Find(text);
        if (hit is null)
        {
            return null;
        }

        var (lemma, entry, formOf) = hit.Value;
        var r = ToResult(text.Trim().ToLowerInvariant(), lemma, entry, formOf, context, contextArabic);
        var entries = r.Definitions.SelectMany(d => d.Entries).ToList();
        if (entries.Count == 0)
        {
            return null;
        }

        var top = entries.FirstOrDefault(e => e.Best) ?? entries[0];
        var heroPos = r.Definitions.First(d => d.Entries.Contains(top)).Pos;
        return r with
        {
            Explain = true,
            Target = "en",
            Dict = [],
            BestGloss = "",
            Translation = top.Gloss,
            HeroExample = top.Example,
            ContextSense = top.Best,
            HeroPos = heroPos,
            // The sense's Arabic, else the word's usual one.
            Meaning = r.Translation.Length > 0 ? r.Translation : context is not null ? ToResult(r.Query, lemma, entry, formOf, null, "").Translation : "",
            Definitions = [.. r.Definitions.Select(d => d with { Entries = [.. d.Entries.Select(e => e with { Arabic = [] })] })], // no Arabic in this view
        };
    }

    /// <summary>An Arabic word → its English words, or null (local-dict.js <c>lookupAr</c>).</summary>
    public LookupResult? LookupArabic(string text)
    {
        var normalized = ArabicText.Normalize(text);
        List<string> candidates = [normalized];
        if (normalized.StartsWith("ال", StringComparison.Ordinal))
        {
            candidates.Add(normalized[2..]);
        }

        if (normalized.Length >= 3 && normalized[0] is 'و' or 'ف' or 'ب' && normalized[1..3] == "ال")
        {
            candidates.Add(normalized[3..]);
        }

        if (normalized.Length > 0 && normalized[0] is 'و' or 'ف')
        {
            candidates.Add(normalized[1..]);
        }

        foreach (var candidate in candidates.Where(c => c.Length > 0))
        {
            var english = data.English(candidate);
            if (english.Count > 0)
            {
                return new LookupResult
                {
                    Query = text.Trim(),
                    IsWord = true,
                    Source = "ar",
                    Target = "en",
                    Translation = english[0],
                    Dict = [new DictGroup(PartOfSpeech.Of("e"), [.. english.Take(12).Select(w => new DictTerm { Word = w })])],
                    From = ResultSource.Local,
                };
            }
        }

        return null;
    }

    /// <summary>
    /// The word of day number <paramref name="seed"/>: from the middle of a shard's frequency list, with an example that
    /// uses it and an Arabic meaning, none the user knows, the same all day. Lamha 2's <c>wordOfDay</c>.
    /// </summary>
    public DictionaryWord? WordOfDay(long seed, Func<string, bool> known)
    {
        for (var i = 0; i < 4; i++)
        {
            // Another shard if this one has nothing left (the user knows them all).
            var shard = WordOfDayShards[(seed + i * 7) % WordOfDayShards.Length];
            var pool = data.Shard(shard).Where(w => PlainWord().IsMatch(w)).Skip(40).Take(360)
                .Select(w => (Word: w, Entry: known(w) ? null : data.Entry(w)))
                .Where(x => x.Entry is not null && SenseFor(x.Entry, x.Word) is not null && ArabicOf(x.Entry).Length > 0)
                .ToList();
            if (pool.Count == 0)
            {
                continue;
            }

            // Spread over the pool, the same all day (Math.imul(seed + i, 2654435761) >>> 0).
            var spread = unchecked((uint)((int)(seed + i) * (int)2654435761u));
            var (word, entry) = pool[(int)(spread % (uint)pool.Count)];
            var sense = SenseFor(entry!, word)!;
            return new DictionaryWord(word, ArabicOf(entry!), sense.Gloss, sense.Example, PartOfSpeech.Of(sense.Pos), entry!.Ipa);
        }

        return null;
    }

    /// <summary>
    /// "n" / "v" / "" for a form from the word before it (a determiner, a pronoun or "to", a helping verb) and its ending
    /// (-ed, -ing): "I mentioned" is a verb, "a mention" a noun. Lamha 2's <c>posHint</c>.
    /// </summary>
    public static string PosHint(string form, string lemma, WordContext? context)
    {
        var m = LastWord().Match((context?.Before ?? "").ToLowerInvariant());
        var previous = m.Success ? m.Groups[1].Value : "";
        var inflected = form != lemma;
        if (Determiners.Contains(previous))
        {
            return inflected && form.EndsWith("ed", StringComparison.Ordinal) ? "" : "n"; // "the mentioned item": an adjective
        }

        if (BeforeVerb.Contains(previous))
        {
            return "v";
        }

        if (inflected && form.EndsWith("ing", StringComparison.Ordinal) && Auxiliary.Contains(previous))
        {
            return "v"; // "is running" (a bare -ing may be a noun)
        }

        return inflected && form.EndsWith("ed", StringComparison.Ordinal) ? "v" : "";
    }

    /// <summary>The dictionary word for <paramref name="raw"/>: itself, the word it's a form of, or a suffix rule's.</summary>
    private (string Lemma, DictionaryEntry Entry, string FormOf)? Find(string raw)
    {
        var w = Whitespace().Replace(raw.ToLowerInvariant().Replace('’', '\'').Replace('`', '\''), " ").Trim();
        var lemmas = data.Lemmas(w).Where(l => l != w).ToList();
        var entry = data.Entry(w);
        if (entry is not null)
        {
            // "studied" (adj.) is rarely what a reader wants when "study" exists and has Arabic meanings.
            if (entry.Translations.Count == 0 && lemmas.Count > 0 && data.Entry(lemmas[0]) is { Translations.Count: > 0 } lemmaEntry)
            {
                return (lemmas[0], lemmaEntry, "");
            }

            return (w, entry, lemmas.FirstOrDefault() ?? "");
        }

        foreach (var lemma in lemmas)
        {
            if (data.Entry(lemma) is { } e)
            {
                return (lemma, e, "");
            }
        }

        var tried = new HashSet<string> { w };
        foreach (var (suffix, replacement) in Rules)
        {
            if (!w.EndsWith(suffix, StringComparison.Ordinal) || w.Length <= suffix.Length + 1)
            {
                continue;
            }

            var stem = w[..^suffix.Length] + replacement;
            List<string> candidates = [stem];
            if (replacement.Length == 0 && Doubled().IsMatch(stem))
            {
                candidates.Add(stem[..^1]); // running → run
            }

            foreach (var candidate in candidates)
            {
                if (tried.Add(candidate) && data.Entry(candidate) is { } e)
                {
                    return (candidate, e, "");
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The entry as a result, with the sense that fits the sentence first (local-dict.js <c>toResult</c>). The sense is
    /// chosen by the sentence's words, the part of speech the grammar suggests, and how Google translated the word inside
    /// its sentence (<paramref name="contextArabic"/>).
    /// </summary>
    private static LookupResult ToResult(string query, string lemma, DictionaryEntry entry, string formOf, WordContext? context, string contextArabic)
    {
        var score = Scorer(context, lemma);
        var hint = PosHint(query, lemma, context);

        // Parts of speech whose Arabic matches Google's word; one alone decides ("ذكرته": ذِكْر the noun and ذَكَرَ the verb both match, so no).
        var arabicPos = new HashSet<string>();
        var arabicHints = new HashSet<string>();
        if (contextArabic.Length > 0)
        {
            foreach (var group in entry.Translations)
            {
                foreach (var sense in group.Senses.Where(s => s.Arabic.Any(a => ArabicMatch(contextArabic, a))))
                {
                    arabicPos.Add(group.Pos);
                    arabicHints.UnionWith(Tokens(sense.Hint));
                }
            }

            foreach (var sense in entry.Senses.Where(s => s.Arabic.Any(a => ArabicMatch(contextArabic, a))))
            {
                arabicPos.Add(sense.Pos);
            }
        }

        var onlyPos = arabicPos.Count == 1 ? arabicPos.First() : "";
        double Bonus(string pos, IEnumerable<string> arabic) =>
            (hint.Length > 0 && pos == hint ? 2 : 0) + (onlyPos.Length > 0 && pos == onlyPos ? 2 : 0)
            + (contextArabic.Length > 0 && arabic.Any(a => ArabicMatch(contextArabic, a)) ? 3 : 0);
        var choosing = score is not null || hint.Length > 0 || contextArabic.Length > 0;

        // Wiktionary's translations grouped by sense, then Arabic WordNet's words per sense.
        var groups = new List<TermGroup>();
        TermGroup GroupFor(string pos)
        {
            var g = groups.Find(x => x.Pos == pos);
            if (g is null)
            {
                g = new TermGroup(pos);
                groups.Add(g);
            }

            return g;
        }

        foreach (var group in entry.Translations)
        {
            var g = GroupFor(group.Pos);
            foreach (var sense in group.Senses)
            {
                foreach (var arabic in sense.Arabic.Where(a => g.Seen.Add(ArabicText.Normalize(a))))
                {
                    g.Terms.Add(new Term(arabic, sense.Hint, group.Pos));
                }
            }
        }

        foreach (var sense in entry.Senses.Where(s => s.Arabic.Count > 0))
        {
            var g = GroupFor(sense.Pos);
            var label = sense.Gloss.Length > 60 ? sense.Gloss[..57] + "…" : sense.Gloss;
            foreach (var arabic in sense.Arabic.Where(a => g.Seen.Add(ArabicText.Normalize(a))))
            {
                g.Terms.Add(new Term(arabic, label, sense.Pos));
            }
        }

        // The best sense for this sentence: Wiktionary's sense labels and WordNet's definitions compete.
        (double Score, string Arabic, string Hint, string Gloss)? best = null;
        (double Score, string Gloss)? bestDefinition = null;
        if (choosing)
        {
            foreach (var g in groups)
            {
                foreach (var t in g.Terms)
                {
                    t.Score = (score?.Invoke(t.Hint) ?? 0) + Bonus(t.Pos, [t.Word]);
                    if (t.Score > 0 && (best is null || t.Score > best.Value.Score))
                    {
                        best = (t.Score, t.Word, t.Hint, "");
                    }
                }
            }

            foreach (var sense in entry.Senses)
            {
                var sc = (score?.Invoke($"{sense.Gloss} {sense.Example} {string.Join(' ', sense.Synonyms)}") ?? 0) + Bonus(sense.Pos, sense.Arabic);
                if (sc > 0 && (best is null || sc > best.Value.Score))
                {
                    best = (sc, sense.Arabic.FirstOrDefault() ?? "", "", sense.Gloss);
                }

                // The definition to lead with is also helped by the label of the Arabic sense that matched ("institution" → "a financial institution").
                var definitionScore = sc + (Tokens(sense.Gloss).Count(arabicHints.Contains) * 1.5);
                if (definitionScore > 0 && (bestDefinition is null || definitionScore > bestDefinition.Value.Score))
                {
                    bestDefinition = (definitionScore, sense.Gloss);
                }
            }

            if (best is { } b)
            {
                foreach (var g in groups)
                {
                    g.Terms = [.. g.Terms.OrderByDescending(t => t.Score)];
                    g.Terms.ForEach(t => t.Best = b.Hint.Length > 0 && t.Hint == b.Hint);
                }

                groups = [.. groups.OrderByDescending(g => g.Terms.Count > 0 ? g.Terms[0].Score : 0)];
            }
        }

        var definitions = new List<(string Pos, List<Definition> Entries)>();
        foreach (var sense in entry.Senses)
        {
            var index = definitions.FindIndex(d => d.Pos == sense.Pos);
            if (index < 0)
            {
                definitions.Add((sense.Pos, []));
                index = definitions.Count - 1;
            }

            if (definitions[index].Entries.Count < 5)
            {
                definitions[index].Entries.Add(new Definition
                {
                    Gloss = sense.Gloss,
                    Example = sense.Example,
                    Synonyms = sense.Synonyms,
                    Arabic = sense.Arabic,
                    Best = bestDefinition is { } bd && bd.Gloss == sense.Gloss,
                });
            }
        }

        if (bestDefinition is not null)
        {
            definitions = [.. definitions
                .Select(d => (d.Pos, Entries: d.Entries.OrderByDescending(e => e.Best).ToList()))
                .OrderByDescending(d => d.Entries[0].Best)];
        }

        // The main meaning: 1. the sense that fits the sentence (if it has an Arabic word; otherwise say so honestly),
        // 2. Wiktionary's first translation (it lists the most common use first), 3. Arabic WordNet only for the first
        // two WordNet senses: a rare sense is never shown as "the" meaning.
        var main = "";
        var bestGloss = "";
        if (best is { Arabic.Length: 0, Gloss.Length: > 0 } wordNetSense)
        {
            // A WordNet sense without Arabic: borrow the Wiktionary sense whose label matches its definition.
            var glossTokens = Tokens(wordNetSense.Gloss);
            Term? pick = null;
            var top = 0;
            foreach (var t in groups.SelectMany(g => g.Terms))
            {
                var overlap = Tokens(t.Hint).Count(glossTokens.Contains);
                if (overlap > top)
                {
                    top = overlap;
                    pick = t;
                }
            }

            if (pick is not null)
            {
                best = wordNetSense with { Arabic = pick.Word };
                foreach (var g in groups)
                {
                    g.Terms.ForEach(t => t.Best = t.Hint == pick.Hint);
                    g.Terms = [.. g.Terms.OrderByDescending(t => t.Best)];
                }

                groups = [.. groups.OrderByDescending(g => g.Terms.Count > 0 && g.Terms[0].Best)];
            }
        }

        if (best is { } chosen)
        {
            if (chosen.Arabic.Length > 0)
            {
                main = chosen.Arabic;
            }
            else
            {
                bestGloss = chosen.Gloss.Length > 0 ? chosen.Gloss : chosen.Hint;
            }
        }
        else if (entry.Translations.Count > 0)
        {
            main = entry.Translations[0].Senses.FirstOrDefault()?.Arabic.FirstOrDefault() ?? "";
        }
        else
        {
            main = entry.Senses.Take(2).FirstOrDefault(s => s.Arabic.Count > 0)?.Arabic[0] ?? "";
        }

        return new LookupResult
        {
            Query = lemma,
            Inflected = lemma != query ? query : "",
            FormOf = formOf,
            IsWord = true,
            Source = "en",
            Target = "ar",
            Translation = main,
            BestGloss = bestGloss,
            ContextSense = best is not null,
            SourceTranslit = entry.Ipa,
            Dict = [.. groups.Where(g => g.Terms.Count > 0).Select(g => new DictGroup(PartOfSpeech.Of(g.Pos), [.. g.Terms.Select(t => new DictTerm { Word = t.Word, Hint = t.Hint, Score = t.Score, Best = t.Best })]))],
            Definitions = [.. definitions.Select(d => new DefinitionGroup(PartOfSpeech.Of(d.Pos), d.Entries))],
            From = ResultSource.Local,
        };
    }

    /// <summary>
    /// How well a text fits the sentence: one point for each of the sentence's words it shares, more for a technical
    /// sense in a technical sentence. Null when the sentence has nothing to go by. Lamha 2's <c>scorer</c>.
    /// </summary>
    private static Func<string, double>? Scorer(WordContext? context, string word)
    {
        if (context is null)
        {
            return null;
        }

        var around = $"{context.Before} {context.After}";
        var sentence = Tokens(around);
        sentence.ExceptWith(Tokens(word));
        var technical = TechnicalSentence().IsMatch(around);
        if (sentence.Count == 0 && !technical)
        {
            return null;
        }

        return text =>
        {
            double s = Tokens(text).Count(sentence.Contains);
            return s + (technical && TechnicalSense().IsMatch(text) ? 1.5 : 0);
        };
    }

    /// <summary>A text's words of three letters or more, without the common ones, roughly stemmed.</summary>
    private static HashSet<string> Tokens(string? text) => [.. Letters3().Matches((text ?? "").ToLowerInvariant())
        .Select(m => m.Value).Where(w => !Stop.Contains(w)).Select(Stem)];

    private static string Stem(string word) => Ending().Replace(Ies().Replace(word, "y"), "");

    /// <summary>Does the Arabic of the sentence's translation (Google's word) match a dictionary Arabic word?</summary>
    private static bool ArabicMatch(string contextArabic, string candidate)
    {
        var a = Whitespace().Replace(ArabicStem(contextArabic), "");
        var b = ArabicStem(candidate).Split(' ')[0]; // "أشار إلى" → أشار
        return b.Length >= 3 && a.Length >= 3 && (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal));
    }

    private static string ArabicStem(string text) => ArticlePrefix().Replace(ArabicText.Normalize(text), "");

    /// <summary>The first sense whose example uses the word itself (WordNet's examples are often for a synonym).</summary>
    private static Sense? SenseFor(DictionaryEntry entry, string word)
    {
        var stem = word[..Math.Max(4, word.Length - 3)];
        return entry.Senses.FirstOrDefault(s => s.Gloss.Length > 0 && s.Example.Length > 0 && s.Example.Contains(stem, StringComparison.OrdinalIgnoreCase));
    }

    private static string ArabicOf(DictionaryEntry entry) =>
        entry.Translations.SelectMany(t => t.Senses).Select(s => s.Arabic.FirstOrDefault()).FirstOrDefault(a => !string.IsNullOrEmpty(a))
        ?? entry.Senses.FirstOrDefault(s => s.Arabic.Count > 0 && s.Arabic[0].Length > 0)?.Arabic[0] ?? "";

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex("([bdfgklmnprtvz])\\1$")]
    private static partial Regex Doubled();

    [GeneratedRegex("([a-z']+)[^a-z']*$")]
    private static partial Regex LastWord();

    [GeneratedRegex("[a-z]{3,}")]
    private static partial Regex Letters3();

    [GeneratedRegex("ies$")]
    private static partial Regex Ies();

    [GeneratedRegex("(ing|ed|es|s|ly)$")]
    private static partial Regex Ending();

    [GeneratedRegex("^(وال|فال|بال|لل|ال)")]
    private static partial Regex ArticlePrefix();

    [GeneratedRegex("^[a-z]{5,12}$")]
    private static partial Regex PlainWord();

    [GeneratedRegex(@"\b(software|computers?|apps?|programs?|code|coding|data|files?|models?|prompts?|settings?|users?|browsers?|systems?|devices?|servers?|click|options?|config\w*|install\w*|api|web|websites?|online|digital|network|database|algorithms?|ai|llm|keyboard|screen|menu|download\w*)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TechnicalSentence();

    [GeneratedRegex("(comput|software|program|option|setting|automatic|electronic|device|data|file|internet|web|digital|machine|selected)", RegexOptions.IgnoreCase)]
    private static partial Regex TechnicalSense();

    /// <summary>Translations of one part of speech while they're ranked.</summary>
    private sealed class TermGroup(string pos)
    {
        public string Pos { get; } = pos;

        public HashSet<string> Seen { get; } = [];

        public List<Term> Terms { get; set; } = [];
    }

    private sealed class Term(string word, string hint, string pos)
    {
        public string Word { get; } = word;

        public string Hint { get; } = hint;

        public string Pos { get; } = pos;

        public double Score { get; set; }

        public bool Best { get; set; }
    }
}
