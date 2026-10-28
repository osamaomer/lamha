using Lamha.Core.Reading;

namespace Lamha.Core.Tests.Reading;

public class ReaderTextTests
{
    private const string Text = "“My dear Mr. Bennet,” said his lady to him one day, “have you heard that Netherfield Park is let at last?” Mr. Bennet replied that he had not.";

    [Theory]
    [InlineData(0, null)] // the opening quote
    [InlineData(1, "My")]
    [InlineData(2, "My")]
    [InlineData(3, "My")] // just past the word: still the word
    [InlineData(10, "Mr")]
    public void AClickFindsItsWord(int index, string? word)
    {
        var at = ReaderText.WordAt(Text, index);
        Assert.Equal(word, at is { } w ? Text.Substring(w.Start, w.Length) : null);
    }

    [Theory]
    [InlineData("It's well-known.", 2, "It's")]
    [InlineData("It's well-known.", 8, "well-known")]
    [InlineData("a - b", 2, null)] // a hyphen alone isn't a word
    [InlineData("قال مرحبًا للجميع", 6, "مرحبًا")]
    [InlineData("", 0, null)]
    public void WordsKeepTheirApostrophesHyphensAndMarks(string text, int index, string? word)
    {
        var at = ReaderText.WordAt(text, index);
        Assert.Equal(word, at is { } w ? text.Substring(w.Start, w.Length) : null);
    }

    [Fact]
    public void TheContextIsTheWordsSentence()
    {
        var let = Text.IndexOf("let", StringComparison.Ordinal);

        Assert.Equal((let, 3), ReaderText.WordAt(Text, let + 1));
        var context = ReaderText.ContextAround(Text, let, 3);

        // "Mr." doesn't end the sentence; the question mark does, with its closing quote.
        Assert.Equal("“My dear Mr. Bennet,” said his lady to him one day, “have you heard that Netherfield Park is ", context.Before);
        Assert.Equal(" at last?”", context.After);
    }

    [Fact]
    public void TheNextSentenceStartsAfterTheEndMark()
    {
        var replied = Text.IndexOf("replied", StringComparison.Ordinal);

        var context = ReaderText.ContextAround(Text, replied, 7);

        Assert.Equal(("Mr. Bennet ", " that he had not."), (context.Before, context.After));
    }

    [Fact]
    public void ALineBreakEndsTheSentenceAndALongOneIsCut()
    {
        var line = ReaderText.ContextAround("one\nline two", 4, 4); // "line"
        Assert.Equal(("", " two"), (line.Before, line.After));

        var longText = new string('a', 500) + " word " + new string('b', 500);
        var context = ReaderText.ContextAround(longText, 501, 4);
        Assert.True(context.Before.Length <= 300 && context.After.Length <= 300);
    }
}
