using Lamha.Core.Reading;

namespace Lamha.Core.Tests.Reading;

/// <summary>A Word document built in memory, as Word writes one (a German Word: its heading style is "berschrift1").</summary>
public class DocxBookTests
{
    private const string Ns = """xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture" """;

    private const string PackageRels = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
        </Relationships>
        """;

    private const string DocumentRels = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rIdLink" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://www.gutenberg.org/" TargetMode="External"/>
          <Relationship Id="rIdImg" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image1.png"/>
        </Relationships>
        """;

    private const string Styles = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:style w:type="paragraph" w:styleId="Titel"><w:name w:val="Title"/></w:style>
          <w:style w:type="paragraph" w:styleId="berschrift1"><w:name w:val="heading 1"/></w:style>
          <w:style w:type="paragraph" w:styleId="berschrift2"><w:name w:val="heading 2"/></w:style>
          <w:style w:type="paragraph" w:styleId="MyChapter"><w:name w:val="My chapter"/><w:basedOn w:val="berschrift1"/></w:style>
          <w:style w:type="paragraph" w:styleId="Standard"><w:name w:val="Normal"/></w:style>
        </w:styles>
        """;

    private const string Numbering = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:abstractNum w:abstractNumId="0"><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="bullet"/></w:lvl></w:abstractNum>
          <w:abstractNum w:abstractNumId="1">
            <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/></w:lvl>
            <w:lvl w:ilvl="1"><w:start w:val="1"/><w:numFmt w:val="lowerLetter"/></w:lvl>
          </w:abstractNum>
          <w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>
          <w:num w:numId="2"><w:abstractNumId w:val="1"/></w:num>
        </w:numbering>
        """;

    private const string Core = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/">
          <dc:title>Graduation project</dc:title><dc:creator>Osama</dc:creator>
        </cp:coreProperties>
        """;

    private static readonly string Document = $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document {Ns}><w:body>
          <w:p><w:pPr><w:pStyle w:val="Titel"/></w:pPr><w:r><w:t>Graduation project</w:t></w:r></w:p>
          <w:p><w:r><w:t xml:space="preserve">A short </w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>preface</w:t></w:r><w:r><w:rPr><w:b w:val="0"/></w:rPr><w:t>.</w:t></w:r></w:p>
          <w:p><w:pPr><w:pStyle w:val="berschrift1"/></w:pPr><w:r><w:t>Chapter 1</w:t></w:r></w:p>
          <w:p>
            <w:r><w:rPr><w:i/></w:rPr><w:t>Reading</w:t></w:r><w:r><w:t xml:space="preserve"> in </w:t></w:r>
            <w:del><w:r><w:delText>never shown </w:delText></w:r></w:del>
            <w:ins><w:r><w:t xml:space="preserve">English, </w:t></w:r></w:ins>
            <w:hyperlink r:id="rIdLink"><w:r><w:t>Project Gutenberg</w:t></w:r></w:hyperlink>
            <w:r><w:rPr><w:vertAlign w:val="superscript"/></w:rPr><w:t>1</w:t></w:r>
            <w:r><w:br/><w:t>new line</w:t><w:br w:type="page"/></w:r>
          </w:p>
          <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>A bullet</w:t></w:r></w:p>
          <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="2"/></w:numPr></w:pPr><w:r><w:t>First</w:t></w:r></w:p>
          <w:p><w:pPr><w:numPr><w:ilvl w:val="1"/><w:numId w:val="2"/></w:numPr></w:pPr><w:r><w:t>Under first</w:t></w:r></w:p>
          <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="2"/></w:numPr></w:pPr><w:r><w:t>Second</w:t></w:r></w:p>
          <w:p><w:pPr><w:numPr><w:ilvl w:val="1"/><w:numId w:val="2"/></w:numPr></w:pPr><w:r><w:t>Under second</w:t></w:r></w:p>
          <w:p><w:r><w:t>Before</w:t></w:r><w:r><w:drawing><wp:inline><a:graphic><a:graphicData><pic:pic><pic:blipFill><a:blip r:embed="rIdImg"/></pic:blipFill></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r><w:r><w:t>after</w:t></w:r></w:p>
          <w:tbl><w:tr><w:tc><w:p><w:r><w:t>Name</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>Year</w:t></w:r></w:p></w:tc></w:tr>
            <w:tr><w:tc><w:p><w:r><w:t>Emma</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>1815</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
          <w:p/>
          <w:p><w:pPr><w:pStyle w:val="MyChapter"/></w:pPr><w:r><w:t>Chapter 2</w:t></w:r></w:p>
          <w:p><w:pPr><w:pStyle w:val="berschrift2"/></w:pPr><w:r><w:t>A section</w:t></w:r></w:p>
          <w:sectPr/>
        </w:body></w:document>
        """;

    [Fact]
    public void TheTitleAndAuthorAreTheDocumentsOwn()
    {
        using var book = Open();

        Assert.Equal(("Graduation project", "Osama"), (book.Info.Title, Assert.Single(book.Info.Authors)));
    }

    [Fact]
    public void HeadingsByStyleCutThePartsAndMakeTheContents()
    {
        using var book = Open();

        // "Title" and "heading 1" (and the style based on it) are all level 1, so a part starts at each.
        Assert.Equal([("Graduation project", "part-1"), ("Chapter 1", "part-2"), ("Chapter 2", "part-3")], book.Contents.Select(c => (c.Title, c.Path)));
        Assert.Equal([("Graduation project", 1), ("Chapter 1", 1), ("Chapter 2", 1), ("A section", 2)],
            All(book).Where(b => b.Kind == BlockKind.Heading).Select(b => (b.Text, b.Level)));
        Assert.Equal(["Chapter 2", "A section"], book.ReadPart(book.Parts[2]).Select(b => b.Text));
    }

    [Fact]
    public void RunsKeepTheirStylesLinksAndLineBreaksButNotDeletedText()
    {
        using var book = Open();
        var blocks = All(book);

        Assert.Equal([new BookRun("A short ", RunStyle.None, null), new BookRun("preface", RunStyle.Bold, null), new BookRun(".", RunStyle.None, null)], blocks[1].Runs);

        var paragraph = blocks[3];
        Assert.Equal("Reading in English, Project Gutenberg1\nnew line", paragraph.Text);
        Assert.Contains(new BookRun("Reading", RunStyle.Italic, null), paragraph.Runs);
        Assert.Contains(new BookRun("Project Gutenberg", RunStyle.None, "https://www.gutenberg.org/"), paragraph.Runs);
        Assert.Contains(new BookRun("1", RunStyle.Superscript, null), paragraph.Runs);
    }

    [Fact]
    public void ListsCountByLevelAndTablesAreRows()
    {
        using var book = Open();
        var blocks = All(book);

        Assert.Equal([("•", 1, "A bullet"), ("1.", 1, "First"), ("1.", 2, "Under first"), ("2.", 1, "Second"), ("1.", 2, "Under second")],
            blocks.Where(b => b.Kind == BlockKind.ListItem).Select(b => (b.Marker, b.Level, b.Text)));
        Assert.Contains(blocks, b => b.Text == "Name · Year");
        Assert.Contains(blocks, b => b.Text == "Emma · 1815");
    }

    [Fact]
    public void APictureSplitsItsParagraphAndOpensFromTheDocument()
    {
        using var book = Open();
        var blocks = All(book);
        var picture = blocks.FindIndex(b => b.Kind == BlockKind.Image);

        Assert.Equal(("Before", "word/media/image1.png", "after"), (blocks[picture - 1].Text, blocks[picture].ImagePath, blocks[picture + 1].Text));
        using var image = book.OpenResource("word/media/image1.png");
        Assert.NotNull(image);
        Assert.Equal("image/png", book.MediaType("word/media/image1.png"));
    }

    [Fact]
    public void WithoutATitleStyleTheChaptersAreTheParts()
    {
        using var book = Open(Document.Replace("""<w:pStyle w:val="Titel"/>""", "", StringComparison.Ordinal), core: null);

        Assert.Equal("report", book.Info.Title);
        Assert.Empty(book.Info.Authors);
        Assert.Equal([("Chapter 1", "part-2"), ("Chapter 2", "part-3")], book.Contents.Select(c => (c.Title, c.Path)));
        Assert.Equal(["Graduation project", "A short preface."], book.ReadPart(book.Parts[0]).Select(b => b.Text)); // before the first chapter
    }

    [Fact]
    public void WhatIsntADocxIsBad()
    {
        Assert.Equal(ErrorCodes.BookBad, Assert.Throws<LamhaException>(() => DocxBook.Open(new MemoryStream([0xD0, 0xCF, 0x11, 0xE0]), "x")).Code);
        Assert.Equal(ErrorCodes.BookBad, Assert.Throws<LamhaException>(() => DocxBook.Open(EpubBookTests.Zip(("readme.txt", "no document")), "x")).Code);
    }

    private static List<BookBlock> All(DocxBook book) => [.. book.Parts.SelectMany(book.ReadPart)];

    private static DocxBook Open(string? document = null, string? core = Core)
    {
        var files = new List<(string, string)>
        {
            ("_rels/.rels", PackageRels), ("word/document.xml", document ?? Document), ("word/_rels/document.xml.rels", DocumentRels),
            ("word/styles.xml", Styles), ("word/numbering.xml", Numbering), ("word/media/image1.png", "png"),
        };
        if (core is not null)
        {
            files.Add(("docProps/core.xml", core));
        }

        return DocxBook.Open(EpubBookTests.Zip([.. files]), "report");
    }
}
