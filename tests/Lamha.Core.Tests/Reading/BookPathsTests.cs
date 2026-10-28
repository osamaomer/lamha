using Lamha.Core.Reading;

namespace Lamha.Core.Tests.Reading;

public class BookPathsTests
{
    [Theory]
    [InlineData("OEBPS/text/ch1.xhtml", "../images/map.jpg", "OEBPS/images/map.jpg", "")]
    [InlineData("OEBPS/text/ch1.xhtml", "ch2.xhtml#s3", "OEBPS/text/ch2.xhtml", "s3")]
    [InlineData("OEBPS/text/ch1.xhtml", "#note-3", "OEBPS/text/ch1.xhtml", "note-3")]
    [InlineData("OEBPS/text/ch1.xhtml", "./a%20b.xhtml", "OEBPS/text/a b.xhtml", "")]
    [InlineData("OEBPS/content.opf", "../../../../evil.xhtml", "evil.xhtml", "")] // never above the book's root
    [InlineData("content.opf", "text/ch1.xhtml", "text/ch1.xhtml", "")]
    [InlineData("OEBPS/text/ch1.xhtml", "/cover.xhtml", "cover.xhtml", "")]
    public void ALinkResolvesFromItsFile(string from, string href, string path, string anchor) =>
        Assert.Equal((path, anchor), BookPaths.Resolve(from, href));

    [Theory]
    [InlineData("https://www.gutenberg.org/", true)]
    [InlineData("mailto:someone@example.com", true)]
    [InlineData("text/ch1.xhtml", false)]
    [InlineData("#note", false)]
    [InlineData("a:b/c.xhtml", false)] // one letter before the colon reads as a path, not a scheme
    [InlineData("images/a:b.png", false)]
    public void WebAddressesAreExternal(string href, bool external) => Assert.Equal(external, BookPaths.IsExternal(href));
}
