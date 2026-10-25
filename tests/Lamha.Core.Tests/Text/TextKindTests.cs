using Lamha.Core.Text;

namespace Lamha.Core.Tests.Text;

public class TextKindTests
{
    [Theory]
    [InlineData("bank")]
    [InlineData("  receive  ")]
    [InlineData("look up to")]
    [InlineData("piece of cake")]
    [InlineData("بنك")]
    [InlineData("e.g.")]
    public void WordsAndShortPhrasesAreLookedUp(string text) => Assert.True(TextKind.IsLookupCandidate(text));

    [Theory]
    [InlineData("it's a piece of cake")]
    [InlineData("Hi. Thanks")]
    [InlineData("one\ntwo")]
    [InlineData("wait: now")]
    [InlineData("pneumonoultramicroscopicsilicovolcanoconiosis")]
    public void SentencesAreTranslated(string text) => Assert.False(TextKind.IsLookupCandidate(text));
}
