using System.Text;
using Lamha.Core.Reading;

namespace Lamha.Core.Tests.Reading;

public class TextBookTests
{
    [Fact]
    public void TheEncodingIsFoundFromTheBytes()
    {
        Assert.Equal("café", TextBook.Decode([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("café")]));
        Assert.Equal("café", TextBook.Decode(Encoding.UTF8.GetBytes("café")));
        Assert.Equal("café", TextBook.Decode([0xFF, 0xFE, .. Encoding.Unicode.GetBytes("café")]));
        Assert.Equal("café", TextBook.Decode([0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes("café")]));
        Assert.Equal("café", TextBook.Decode([0x63, 0x61, 0x66, 0xE9])); // Windows-1252
        Assert.Equal("مرحبا", TextBook.Decode([0xE3, 0xD1, 0xCD, 0xC8, 0xC7])); // Windows-1256
    }

    [Fact]
    public void HandWrappedLinesJoinIntoParagraphsAndChaptersAreHeadings()
    {
        const string text = """
            PRIDE AND PREJUDICE

            Chapter 1

            It is a truth universally acknowledged, that a single man in possession
            of a good fortune, must be in want of a wife.

            However little known the feelings or views of such a man may be.

            Chapter 2

            Mr. Bennet was among the earliest of those who waited on Mr. Bingley.

              To Mrs. Bennet,
                Longbourn,
                Hertfordshire.
            """;

        var blocks = TextBook.Blocks(text);

        Assert.Equal(
            [(BlockKind.Heading, 3, "PRIDE AND PREJUDICE"), (BlockKind.Heading, 2, "Chapter 1"),
             (BlockKind.Paragraph, 0, "It is a truth universally acknowledged, that a single man in possession of a good fortune, must be in want of a wife."),
             (BlockKind.Paragraph, 0, "However little known the feelings or views of such a man may be."),
             (BlockKind.Heading, 2, "Chapter 2"),
             (BlockKind.Paragraph, 0, "Mr. Bennet was among the earliest of those who waited on Mr. Bingley."),
             (BlockKind.Paragraph, 0, "To Mrs. Bennet,\nLongbourn,\nHertfordshire.")],
            blocks.Select(b => (b.Kind, b.Level, b.Text)));
    }

    [Fact]
    public void AFileWithoutBlankLinesHasAParagraphPerLine()
    {
        var blocks = TextBook.Blocks("First paragraph, written on one line.\r\nSecond one.\r\nThird one.\r\n");

        Assert.Equal(["First paragraph, written on one line.", "Second one.", "Third one."], blocks.Select(b => b.Text));
    }

    [Fact]
    public void ProjectGutenbergsLicenceIsLeftOut()
    {
        const string text = """
            The Project Gutenberg eBook of Emma

            This eBook is for the use of anyone anywhere.

            *** START OF THE PROJECT GUTENBERG EBOOK EMMA ***

            VOLUME I

            CHAPTER I

            Emma Woodhouse, handsome, clever, and rich.

            *** END OF THE PROJECT GUTENBERG EBOOK EMMA ***

            Section 1. General Terms of Use.
            """;

        var blocks = TextBook.Blocks(text);

        Assert.Equal(["VOLUME I", "CHAPTER I", "Emma Woodhouse, handsome, clever, and rich."], blocks.Select(b => b.Text));
        Assert.Equal([1, 2, 0], blocks.Select(b => b.Level));
    }

    [Fact]
    public void PartsAreCutAtTheTopHeadingsWhichAreTheContents()
    {
        var text = "A preface.\n\nBOOK ONE\n\nCHAPTER I\n\nOne.\n\nCHAPTER II\n\nTwo.\n\nBOOK TWO\n\nCHAPTER III\n\nThree.";
        using var book = TextBook.Open(new MemoryStream(Encoding.UTF8.GetBytes(text)), "Notes");

        Assert.Equal(("Notes", 3), (book.Info.Title, book.Parts.Count));
        Assert.Equal([("BOOK ONE", "part-2"), ("BOOK TWO", "part-3")], book.Contents.Select(c => (c.Title, c.Path)));
        Assert.Equal(["A preface."], book.ReadPart(book.Parts[0]).Select(b => b.Text));
        Assert.Equal(["BOOK ONE", "CHAPTER I", "One.", "CHAPTER II", "Two."], book.ReadPart(book.Parts[1]).Select(b => b.Text));
        Assert.Equal(ErrorCodes.BookBad, Assert.Throws<LamhaException>(() => book.ReadPart(new BookPart("part-9", true))).Code);
    }

    [Fact]
    public void GutenbergsIllustrationMarksGoAndItsUnderscoresAreItalics()
    {
        const string text = """
            [Illustration]

            [Illustration:

            ·PRIDE AND PREJUDICE·

            Chapter I.]

            She was _not_ handsome enough to tempt _me_; snake_case stays.

            [Illustration: “He rode a black horse”]
            """;

        var blocks = TextBook.Blocks(text);

        Assert.Equal([(BlockKind.Heading, "·PRIDE AND PREJUDICE·"), (BlockKind.Heading, "Chapter I."),
                      (BlockKind.Paragraph, "She was not handsome enough to tempt me; snake_case stays."), (BlockKind.Paragraph, "“He rode a black horse”")],
            blocks.Select(b => (b.Kind, b.Text)));
        Assert.Equal([new BookRun("She was ", RunStyle.None, null), new BookRun("not", RunStyle.Italic, null), new BookRun(" handsome enough to tempt ", RunStyle.None, null),
                      new BookRun("me", RunStyle.Italic, null), new BookRun("; snake_case stays.", RunStyle.None, null)], blocks[2].Runs);
    }

    [Fact]
    public void CapitalsAreChaptersOnlyInAFileWithoutThem()
    {
        using var titled = TextBook.Open(new MemoryStream(Encoding.UTF8.GetBytes("PREFACE\n\nWords.\n\nCHAPTER I\n\nOne.\n\nCHAPTER II\n\nTwo.")), "x");
        Assert.Equal(["CHAPTER I", "CHAPTER II"], titled.Contents.Select(c => c.Title));

        using var untitled = TextBook.Open(new MemoryStream(Encoding.UTF8.GetBytes("THE START\n\nWords.\n\nTHE END\n\nMore.")), "x");
        Assert.Equal(["THE START", "THE END"], untitled.Contents.Select(c => c.Title));
    }

    [Fact]
    public void ABinaryFileIsntText() =>
        Assert.Equal(ErrorCodes.BookBad, Assert.Throws<LamhaException>(() => TextBook.Open(new MemoryStream([0x50, 0x4B, 0x03, 0x04, 0x00, 0x00]), "x")).Code);
}
