using Lamha.Core.Text;

namespace Lamha.Core.Tests.Text;

/// <summary>Lamha 2's test-writing.mjs: diffParts, isArabicText, ttsChunks, splitLong, strayLetters.</summary>
public class TextToolTests
{
    [Fact]
    public void DiffMarksRemovedAndInsertedWords()
    {
        var parts = WordDiff.Compare("I go to market", "I went to the market")!;
        var removed = parts.Where(p => p.Kind == DiffKind.Removed).Select(p => p.Text.Trim()).Where(t => t.Length > 0);
        var inserted = parts.Where(p => p.Kind == DiffKind.Inserted).Select(p => p.Text.Trim()).Where(t => t.Length > 0);
        Assert.Equal(["go"], removed);
        Assert.Equal(["went", "the"], string.Join(' ', inserted).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal("I went to the market", string.Concat(parts.Where(p => p.Kind != DiffKind.Removed).Select(p => p.Text)));
    }

    [Fact]
    public void DiffGivesUpOnTextsTooLongToCompareQuickly()
    {
        var long1 = string.Join(' ', Enumerable.Repeat("a", 400));
        Assert.Null(WordDiff.Compare(long1, long1 + " b"));
    }

    [Fact]
    public void ArabicDraftsAreRecognised()
    {
        Assert.True(Scripts.IsArabicText("أريد أن أطلب إجازة يوم الأحد"));
        Assert.False(Scripts.IsArabicText("Please send the file today"));
    }

    [Fact]
    public void SpeechChunksKeepEveryWordAndStayUnderTheVoiceLimit()
    {
        var one = "4. Don't tell him \"I am not in the mood tonight, I am tired, my head is paining. Tomorrow morning we will make love\" then tomorrow morning you act like you forgot. Keep your promise. ";
        var text = string.Concat(Enumerable.Repeat(one, 3)).Trim();
        var chunks = Pieces.ForSpeech(text);
        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Length <= 190, $"chunk too long ({c.Length})"));
        Assert.Equal(System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " "), string.Join(' ', chunks));
    }

    [Fact]
    public void SplitLongCutsAtLineBreaksThenSentenceEndsThenSpacesAndLosesNothing()
    {
        static string Join(IReadOnlyList<TextPiece> ps) => string.Concat(ps.Select(p => p.Text + p.Separator));
        var sentences = string.Concat(Enumerable.Repeat("One sentence here. ", 40)).Trim();
        var bySentence = Pieces.SplitLong(sentences, 100);
        Assert.Equal(sentences, Join(bySentence));
        Assert.All(bySentence, p =>
        {
            Assert.True(p.Text.Length <= 100);
            Assert.EndsWith(".", p.Text, StringComparison.Ordinal);
        });
        var noPunctuation = string.Concat(Enumerable.Repeat("word ", 100)).Trim();
        Assert.Equal(noPunctuation, Join(Pieces.SplitLong(noPunctuation, 64)));

        var paragraphs = "First paragraph here.\n\nSecond one, a little longer than the first.";
        var byBreak = Pieces.SplitLong(paragraphs, 40);
        Assert.Equal(new TextPiece("First paragraph here.", "\n\n"), byBreak[0]);
    }

    [Fact]
    public void TidyKeepsParagraphsAndTidiesSpaces() =>
        Assert.Equal("Hello there.\n\nGood morning.", Pieces.Tidy("Hello   there.\r\n\n\n\nGood morning.  "));

    [Fact]
    public void LettersThatWereInTheSourceMayStayOthersAreStray()
    {
        Assert.False(Scripts.StrayLetters("Σωκράτης said", "قال Σωκράτης", "ar"));
        Assert.True(Scripts.StrayLetters("I saw her duck", "رأיתها تنحني", "ar"));
        Assert.True(Scripts.StrayLetters("It's a piece of cake", "إنها قطعة من 캐시", "ar"));
        Assert.False(Scripts.StrayLetters("Paris", "باريس Paris café", "ar"));
        Assert.True(Scripts.StrayLetters("hello", "مرحبا", "fr"), "Arabic letters in a French translation");
    }

    [Fact]
    public void EntitiesAndTagsAreUndone()
    {
        Assert.Equal("a resilient economy", Html.StripTags("a <b>resilient</b> economy"));
        Assert.Equal("\"it's\" <&> é 😀", Html.DecodeEntities("&quot;it&#39;s&quot; &lt;&amp;&gt; &#233; &#x1F600;"));
        Assert.Equal("a &lt;b&gt; &amp;", Html.Escape("a <b> &"));
    }
}
