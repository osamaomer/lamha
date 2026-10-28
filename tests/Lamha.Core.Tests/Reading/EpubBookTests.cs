using System.IO.Compression;
using System.Text;
using Lamha.Core.Reading;

namespace Lamha.Core.Tests.Reading;

/// <summary>EPUBs built in memory, EPUB 3 and EPUB 2, read as the reader will: details, order, contents, blocks.</summary>
public class EpubBookTests
{
    // &nbsp; stays a no-break space, so a name isn't split across lines.
    private const char Nbsp = (char)0xA0;

    private const string Container = """
        <?xml version="1.0"?>
        <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
          <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
        </container>
        """;

    private const string Package3 = """
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="id">
          <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
            <dc:identifier id="id">urn:uuid:1</dc:identifier>
            <dc:title>
              Pride and
              Prejudice
            </dc:title>
            <dc:creator>Jane Austen</dc:creator>
            <dc:language>en</dc:language>
          </metadata>
          <manifest>
            <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
            <item id="cover" href="images/cover.jpg" media-type="image/jpeg" properties="cover-image"/>
            <item id="c1" href="text/chapter%201.xhtml" media-type="application/xhtml+xml"/>
            <item id="c2" href="text/ch2.xhtml" media-type="application/xhtml+xml"/>
            <item id="notes" href="text/notes.xhtml" media-type="application/xhtml+xml"/>
          </manifest>
          <spine><itemref idref="c1"/><itemref idref="c2"/><itemref idref="notes" linear="no"/></spine>
        </package>
        """;

    private const string Nav = """
        <?xml version="1.0" encoding="utf-8"?>
        <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops"><body>
          <nav epub:type="landmarks"><ol><li><a href="text/ch2.xhtml">Not the contents</a></li></ol></nav>
          <nav epub:type="toc"><ol>
            <li><a href="text/chapter%201.xhtml">Volume I</a>
              <ol><li><a href="text/chapter%201.xhtml#ch1">Chapter 1</a></li><li><a href="text/ch2.xhtml">Chapter 2</a></li></ol>
            </li>
            <li><span>Unlinked part</span></li>
          </ol></nav>
        </body></html>
        """;

    private const string Chapter1 = """
        <?xml version="1.0" encoding="utf-8"?>
        <!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.1//EN" "http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd">
        <html xmlns="http://www.w3.org/1999/xhtml"><head><title>Chapter 1</title><style>p { color: red }</style></head><body>
          <h1 id="ch1">Chapter&nbsp;1</h1>
          <p>It is a truth   universally
             acknowledged, that a <em>single</em> man in <em>possession</em><em> of</em> a good fortune&mdash;<strong>must</strong>
             be in want of a wife.<a href="notes.xhtml#n1"><sup>1</sup></a></p>
          <p>Netherfield&nbsp;Park<br/>is let at last.</p>
          <script>alert("no")</script>
          <p>Before <img src="../images/map.jpg" alt="A map of Hertfordshire"/> and after.</p>
          <ul><li><p>Mr. Bingley</p></li><li>Mr. Darcy</li></ul>
          <ol start="3"><li>Third</li><li>Fourth</li></ol>
          <blockquote><p>A quotation.</p></blockquote>
          <hr/>
          <pre>line one
          line two</pre>
          <p>   </p>
          <p>See <a href="https://www.gutenberg.org/">Project Gutenberg</a>.</p>
          <span id="end"></span>
        </body></html>
        """;

    private const string Simple = """
        <?xml version="1.0" encoding="utf-8"?>
        <html xmlns="http://www.w3.org/1999/xhtml"><body><p>Text.</p></body></html>
        """;

    [Fact]
    public void ReadsTheDetailsOfAnEpub3()
    {
        using var book = OpenBook3();

        Assert.Equal("Pride and Prejudice", book.Info.Title);
        Assert.Equal(["Jane Austen"], book.Info.Authors);
        Assert.Equal("en", book.Info.Language);
        Assert.Equal("OEBPS/images/cover.jpg", book.Info.CoverPath);
        Assert.Equal("image/jpeg", book.MediaType("OEBPS/images/cover.jpg"));
        using var cover = book.OpenResource(book.Info.CoverPath!);
        Assert.NotNull(cover);
        Assert.Null(book.OpenResource("OEBPS/images/missing.png"));
    }

    [Fact]
    public void TheReadingOrderFollowsTheSpine()
    {
        using var book = OpenBook3();

        Assert.Equal(
            [new BookPart("OEBPS/text/chapter 1.xhtml", true), new BookPart("OEBPS/text/ch2.xhtml", true), new BookPart("OEBPS/text/notes.xhtml", false)],
            book.Parts);
    }

    [Fact]
    public void TheContentsComeFromTheNavsToc()
    {
        using var book = OpenBook3();

        var volume = Assert.Single(book.Contents, c => c.Title == "Volume I");
        Assert.Equal("OEBPS/text/chapter 1.xhtml", volume.Path);
        Assert.Equal([("Chapter 1", "OEBPS/text/chapter 1.xhtml", "ch1"), ("Chapter 2", "OEBPS/text/ch2.xhtml", "")],
            volume.Children.Select(c => (c.Title, c.Path, c.Anchor)));
        var unlinked = book.Contents[1];
        Assert.Equal(("Unlinked part", ""), (unlinked.Title, unlinked.Path));
    }

    [Fact]
    public void AChapterBecomesBlocks()
    {
        using var book = OpenBook3();

        var blocks = book.ReadPart(book.Parts[0]);

        Assert.Equal(
            [BlockKind.Heading, BlockKind.Paragraph, BlockKind.Paragraph, BlockKind.Paragraph, BlockKind.Image, BlockKind.Paragraph,
             BlockKind.ListItem, BlockKind.ListItem, BlockKind.ListItem, BlockKind.ListItem, BlockKind.Paragraph, BlockKind.Rule,
             BlockKind.Preformatted, BlockKind.Paragraph],
            blocks.Select(b => b.Kind));

        var heading = blocks[0];
        Assert.Equal(("Chapter" + Nbsp + "1", 1), (heading.Text, heading.Level));
        Assert.Equal(["ch1"], heading.Anchors);
    }

    [Fact]
    public void SpacesCollapseAndStylesBecomeRuns()
    {
        using var book = OpenBook3();

        var first = book.ReadPart(book.Parts[0])[1];

        Assert.Equal("It is a truth universally acknowledged, that a single man in possession of a good fortune—must be in want of a wife.1", first.Text);
        Assert.Contains(new BookRun("single", RunStyle.Italic, null), first.Runs);
        Assert.Contains(new BookRun("possession of", RunStyle.Italic, null), first.Runs); // two <em>s in a row are one run
        Assert.Contains(new BookRun("must", RunStyle.Bold, null), first.Runs);
        Assert.Equal(new BookRun("1", RunStyle.Superscript, "OEBPS/text/notes.xhtml#n1"), first.Runs[^1]);
    }

    [Fact]
    public void LineBreaksPicturesListsAndQuotations()
    {
        using var book = OpenBook3();
        var blocks = book.ReadPart(book.Parts[0]);

        Assert.Equal("Netherfield" + Nbsp + "Park\nis let at last.", blocks[2].Text);

        Assert.Equal("Before", blocks[3].Text);
        Assert.Equal(("OEBPS/images/map.jpg", "A map of Hertfordshire"), (blocks[4].ImagePath, blocks[4].Text));
        Assert.Equal("and after.", blocks[5].Text);

        Assert.Equal([("•", "Mr. Bingley"), ("•", "Mr. Darcy"), ("3.", "Third"), ("4.", "Fourth")],
            blocks.Where(b => b.Kind == BlockKind.ListItem).Select(b => (b.Marker, b.Text)));
        Assert.All(blocks.Where(b => b.Kind == BlockKind.ListItem), b => Assert.Equal(1, b.Level));

        Assert.Equal(("A quotation.", 1), (blocks[10].Text, blocks[10].Quote));
        Assert.Equal(0, blocks[11].Quote);
    }

    [Fact]
    public void PreformattedTextKeepsItsLinesAndWebLinksStayAsTheyAre()
    {
        using var book = OpenBook3();
        var blocks = book.ReadPart(book.Parts[0]);

        Assert.Contains("line one\n", blocks[12].Text, StringComparison.Ordinal);
        Assert.EndsWith("line two", blocks[12].Text, StringComparison.Ordinal);
        Assert.Equal(new BookRun("Project Gutenberg", RunStyle.None, "https://www.gutenberg.org/"), blocks[13].Runs[1]);
        Assert.Equal(["end"], blocks[13].Anchors); // an id after the last text points at the last block
        Assert.DoesNotContain(blocks, b => b.Text.Contains("alert", StringComparison.Ordinal) || b.Text.Contains("color", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEpub2NamesItsCoverInMetaAndItsContentsInTheNcx()
    {
        const string package = """
            <?xml version="1.0"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="2.0">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:opf="http://www.idpf.org/2007/opf">
                <dc:creator>Arthur Conan Doyle</dc:creator><dc:creator>Arthur Conan Doyle</dc:creator>
                <meta name="cover" content="img1"/>
              </metadata>
              <manifest>
                <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>
                <item id="img1" href="front.png" media-type="image/png"/>
                <item id="a" href="a.html" media-type="application/xhtml+xml"/>
              </manifest>
              <spine toc="ncx"><itemref idref="a"/></spine>
            </package>
            """;
        const string ncx = """
            <?xml version="1.0"?>
            <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1"><navMap>
              <navPoint id="p1"><navLabel><text>A Scandal in Bohemia</text></navLabel><content src="a.html#s1"/>
                <navPoint id="p2"><navLabel><text>Part II</text></navLabel><content src="a.html#s2"/></navPoint>
              </navPoint>
            </navMap></ncx>
            """;

        using var book = EpubBook.Open(Zip(("META-INF/container.xml", Container), ("OEBPS/content.opf", package), ("OEBPS/toc.ncx", ncx),
            ("OEBPS/front.png", "png"), ("OEBPS/a.html", Simple)), "holmes.epub");

        Assert.Equal("holmes.epub", book.Info.Title); // no <dc:title>: the file's name
        Assert.Equal(["Arthur Conan Doyle"], book.Info.Authors);
        Assert.Equal("OEBPS/front.png", book.Info.CoverPath);
        var story = Assert.Single(book.Contents);
        Assert.Equal(("A Scandal in Bohemia", "OEBPS/a.html", "s1"), (story.Title, story.Path, story.Anchor));
        Assert.Equal("Part II", Assert.Single(story.Children).Title);
    }

    [Fact]
    public void WhatIsntAReadableEpubIsBad()
    {
        Assert.Equal(ErrorCodes.BookBad, Assert.Throws<LamhaException>(() => EpubBook.Open(new MemoryStream(Encoding.UTF8.GetBytes("not a zip")), "x")).Code);
        Assert.Equal(ErrorCodes.BookBad, Assert.Throws<LamhaException>(() => EpubBook.Open(Zip(("a.txt", "no container")), "x")).Code);
        Assert.Equal(ErrorCodes.BookBad, Assert.Throws<LamhaException>(() => EpubBook.Open(Zip(("META-INF/container.xml", Container), ("OEBPS/content.opf", "<package")), "x")).Code);
    }

    [Fact]
    public void DrmLocksABookButFontObfuscationDoesnt()
    {
        static string Encryption(string algorithm) => $"""
            <?xml version="1.0"?>
            <encryption xmlns="urn:oasis:names:tc:opendocument:xmlns:container" xmlns:enc="http://www.w3.org/2001/04/xmlenc#">
              <enc:EncryptedData><enc:EncryptionMethod Algorithm="{algorithm}"/>
                <enc:CipherData><enc:CipherReference URI="OEBPS/text/ch2.xhtml"/></enc:CipherData></enc:EncryptedData>
            </encryption>
            """;

        var locked = Assert.Throws<LamhaException>(() => OpenBook3(("META-INF/encryption.xml", Encryption("http://www.w3.org/2001/04/xmlenc#aes128-cbc"))));
        Assert.Equal(ErrorCodes.BookLocked, locked.Code);

        using var fonts = OpenBook3(("META-INF/encryption.xml", Encryption("http://www.idpf.org/2008/embedding")));
        Assert.Equal("Pride and Prejudice", fonts.Info.Title);
    }

    [Fact]
    public void AMissingPartIsBad()
    {
        using var book = OpenBook3();
        Assert.Equal(ErrorCodes.BookBad, Assert.Throws<LamhaException>(() => book.ReadPart(new BookPart("OEBPS/text/gone.xhtml", true))).Code);
    }

    private static EpubBook OpenBook3(params (string Path, string Text)[] extra) => EpubBook.Open(Zip(
        [("META-INF/container.xml", Container), ("OEBPS/content.opf", Package3), ("OEBPS/nav.xhtml", Nav), ("OEBPS/images/cover.jpg", "jpg"),
         ("OEBPS/text/chapter 1.xhtml", Chapter1), ("OEBPS/text/ch2.xhtml", Simple), ("OEBPS/text/notes.xhtml", Simple), .. extra]), "book.epub");

    private static MemoryStream Zip(params (string Path, string Text)[] files)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("mimetype", CompressionLevel.NoCompression).Open()))
            {
                writer.Write("application/epub+zip");
            }

            foreach (var (path, text) in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open());
                writer.Write(text);
            }
        }

        stream.Position = 0;
        return stream;
    }
}
