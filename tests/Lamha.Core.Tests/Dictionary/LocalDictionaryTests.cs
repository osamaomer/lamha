using Lamha.Core.Dictionary;
using Lamha.Core.Tests.Support;
using Lamha.Core.Translation;

namespace Lamha.Core.Tests.Dictionary;

/// <summary>The offline dictionary on the real data: Lamha 2's local-dict.js behaviour from test-writing.mjs.</summary>
public class LocalDictionaryTests
{
    private static readonly LocalDictionary Dict = new(RealDictionary.Data);

    [Theory]
    [InlineData("went", "go")]
    [InlineData("children", "child")]
    [InlineData("mice", "mouse")]
    [InlineData("geese", "goose")]
    [InlineData("studied", "study")]
    [InlineData("taught", "teach")]
    public void IrregularAndInflectedFormsFindTheirWord(string form, string word) =>
        Assert.Equal(word, Dict.LookupEnglishToArabic(form, null)?.Query);

    [Fact]
    public void WordsNamedLikeObjectPropertiesAreOrdinaryWords()
    {
        var r = Dict.LookupEnglishToArabic("constructor", null)!;
        Assert.Equal(ResultSource.Local, r.From);
        Assert.Matches(@"\p{IsArabic}", r.Translation);
    }

    [Fact]
    public void EnglishEnglishOfflineLeadsWithTheDefinitionThatFitsTheSentence()
    {
        var plain = Dict.LookupEnglish("resilient", null)!;
        Assert.True(plain.Explain);
        Assert.Equal(("en", "مَرِن", "rɪˈzɪljənt", 0), (plain.Target, plain.Meaning, plain.SourceTranslit, plain.Dict.Count));
        Assert.StartsWith("recovering readily from adversity", plain.Translation, StringComparison.Ordinal);
        Assert.False(plain.ContextSense);
        Assert.All(plain.Definitions.SelectMany(d => d.Entries), e => Assert.Empty(e.Arabic)); // no Arabic in the English view

        var inSentence = Dict.LookupEnglish("resilient", new WordContext("Her hair was soft, bouncy and ", "."))!;
        Assert.Equal(("elastic; rebounds readily", true, "clean bouncy hair"), (inSentence.Translation, inSentence.ContextSense, inSentence.HeroExample));
        Assert.Equal("مَرِن", inSentence.Meaning); // a sense without its own Arabic word keeps the word's usual meaning
    }

    [Fact]
    public void TheSenseFollowsTheGrammar()
    {
        var context = new WordContext("As promised here is the summary of everything I ", ", your 3 options for the validation.");
        var verb = Dict.LookupEnglish("mentioned", context)!;
        Assert.Equal(("make reference to", "v", true), (verb.Translation, verb.HeroPos.Code, verb.ContextSense));
        Assert.Equal("v", verb.Definitions[0].Pos.Code); // the verb senses come first in the list too
        Assert.Equal("ذَكَرَ", Dict.LookupEnglishToArabic("mentioned", context)!.Translation); // the Arabic view agrees

        var noun = Dict.LookupEnglish("mention", new WordContext("It was only a ", " in passing."))!;
        Assert.Equal(("a remark that calls attention to something or someone", "n"), (noun.Translation, noun.HeroPos.Code));
    }

    [Theory]
    [InlineData("البنك", "financial institution")]
    [InlineData("الضفة", "sloping land")]
    public void GooglesArabicForTheWordInItsSentencePicksTheSameSense(string arabic, string lead)
    {
        var d = Dict.LookupEnglish("bank", new WordContext("We met near the ", " this morning."), arabic)!;
        Assert.Contains(lead, d.Translation, StringComparison.Ordinal);
        Assert.True(d.ContextSense);
    }

    [Fact]
    public void ArabicToEnglishListsTheEnglishWords()
    {
        var r = Dict.LookupArabic("كتاب")!;
        Assert.Equal(("ar", "en", "book"), (r.Source, r.Target, r.Translation));
        Assert.Equal("e", r.Dict[0].Pos.Code); // "in English", in the interface language
        Assert.Equal("book", Dict.LookupArabic("والكتاب")?.Translation);
    }

    [Fact]
    public void TheWordOfTheDayIsAPlainWordWithAMeaningAndADefinitionNotOneTheUserKnows()
    {
        var seed = 20_385L;
        var w = Dict.WordOfDay(seed, _ => false)!;
        Assert.Matches("^[a-z]{5,12}$", w.Word);
        Assert.NotEmpty(w.Meaning);
        Assert.NotEmpty(w.Definition);
        Assert.True(w.Example.Length == 0 || w.Example.Contains(w.Word[..4], StringComparison.OrdinalIgnoreCase));
        Assert.Equal(w, Dict.WordOfDay(seed, _ => false)); // the same all day
        Assert.NotEqual(w.Word, Dict.WordOfDay(seed, word => word == w.Word)!.Word);
    }

    [Fact]
    public void TheDictionaryHasItsFormsAndArabicIndex()
    {
        Assert.Contains("go", RealDictionary.Data.Lemmas("went"));
        Assert.Contains("\"forms\"", RealDictionary.Data.Meta, StringComparison.Ordinal);
        Assert.True(RealDictionary.Data.Shard("ba").Count > 400);
    }
}
