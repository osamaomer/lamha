using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace Lamha.Core.Reading;

/// <summary>
/// An EPUB, read in place: a ZIP holding its package file (the book's details, its parts and their order), the parts as
/// XHTML, and their pictures. Reads EPUB 2 and 3. Books with DRM don't open (<see cref="ErrorCodes.BookLocked"/>); fonts
/// hidden by the EPUB's own font obfuscation are fine, since the page doesn't use the book's fonts.
/// </summary>
public sealed class EpubBook : IFlowingBook
{
    /// <summary>The most a part or the package file may unpack to: a novel's chapter is well under 1 MB.</summary>
    internal const long MaxTextBytes = 16 * 1024 * 1024;

    // The two font obfuscations EPUBs use (IDPF's and Adobe's): not DRM.
    private static readonly HashSet<string> FontObfuscation = new(StringComparer.Ordinal)
    {
        "http://www.idpf.org/2008/embedding",
        "http://ns.adobe.com/pdf/enc#RC",
    };

    private static readonly XNamespace Opf = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Ncx = "http://www.daisy.org/z3986/2005/ncx/";
    private static readonly XNamespace Container = "urn:oasis:names:tc:opendocument:xmlns:container";
    private static readonly XNamespace Xhtml = "http://www.w3.org/1999/xhtml";
    private static readonly XNamespace Ops = "http://www.idpf.org/2007/ops";
    private static readonly XNamespace XmlEnc = "http://www.w3.org/2001/04/xmlenc#";

    private readonly ZipArchive _zip;
    private readonly Dictionary<string, ZipArchiveEntry> _entries;
    private readonly Dictionary<string, string> _types;

    private EpubBook(ZipArchive zip, string fallbackTitle)
    {
        _zip = zip;
        _entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            _entries.TryAdd(entry.FullName, entry);
        }

        RefuseDrm();
        var packagePath = PackagePath();
        var package = LoadXml(packagePath);
        
        var root = package.Root ?? throw new LamhaException(ErrorCodes.BookBad);

        // The manifest: every file of the book by id, with its path from the book's root.
        var items = new Dictionary<string, (string Path, string Type, string Properties)>(StringComparer.Ordinal);
        _types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in root.Element(Opf + "manifest")?.Elements(Opf + "item") ?? [])
        {
            var id = (string?)item.Attribute("id");
            var href = (string?)item.Attribute("href");
            if (id is null || href is null)
            {
                continue;
            }

            var path = BookPaths.Resolve(packagePath, href).Path;
            var type = (string?)item.Attribute("media-type") ?? "";
            items.TryAdd(id, (path, type, (string?)item.Attribute("properties") ?? ""));
            _types.TryAdd(path, type);
        }

        var spine = root.Element(Opf + "spine");
        Parts = [.. (spine?.Elements(Opf + "itemref") ?? [])
            .Select(r => items.TryGetValue((string?)r.Attribute("idref") ?? "", out var it) ? new BookPart(it.Path, (string?)r.Attribute("linear") != "no") : null)
            .OfType<BookPart>()];
        if (Parts.Count == 0)
        {
            throw new LamhaException(ErrorCodes.BookBad);
        }

        var metadata = root.Element(Opf + "metadata");
        var title = metadata?.Elements(Dc + "title").Select(e => Clean(e.Value)).FirstOrDefault(t => t.Length > 0);
        var authors = metadata?.Elements(Dc + "creator").Select(e => Clean(e.Value)).Where(a => a.Length > 0).Distinct().ToList() ?? [];
        var language = metadata?.Elements(Dc + "language").Select(e => Clean(e.Value)).FirstOrDefault() ?? "";
        Info = new BookInfo(title ?? fallbackTitle, authors, language, CoverPath(metadata, items));

        Contents = ReadContents(spine, items);
    }

    /// <summary>The book's details.</summary>
    public BookInfo Info { get; }

    /// <summary>The book's parts, in reading order.</summary>
    public IReadOnlyList<BookPart> Parts { get; }

    /// <summary>The book's contents, from its own table (EPUB 3's nav, or EPUB 2's NCX); empty when it has none.</summary>
    public IReadOnlyList<ContentsEntry> Contents { get; }

    /// <summary>Opens an EPUB. The stream stays in use until the book is disposed.</summary>
    /// <param name="stream">The EPUB file.</param>
    /// <param name="fallbackTitle">The title when the book doesn't give one: its file's name.</param>
    /// <exception cref="LamhaException"><see cref="ErrorCodes.BookBad"/> when it isn't a readable EPUB, <see cref="ErrorCodes.BookLocked"/> when it has DRM.</exception>
    public static EpubBook Open(Stream stream, string fallbackTitle)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        }
        catch (InvalidDataException e)
        {
            // Not a ZIP: the archive never took the stream, so it's closed here (the file can then be deleted).
            stream.Dispose();
            throw new LamhaException(ErrorCodes.BookBad, e);
        }

        try
        {
            return new EpubBook(zip, fallbackTitle);
        }
        catch (Exception e) when (e is XmlException or InvalidDataException)
        {
            zip.Dispose();
            throw new LamhaException(ErrorCodes.BookBad, e);
        }
        catch
        {
            zip.Dispose();
            throw;
        }
    }

    /// <summary>A part of the book as blocks for the reading page.</summary>
    /// <exception cref="LamhaException"><see cref="ErrorCodes.BookBad"/> when the part is missing or isn't XHTML.</exception>
    public IReadOnlyList<BookBlock> ReadPart(BookPart part)
    {
        try
        {
            return XhtmlBlocks.Read(LoadXml(part.Path), part.Path);
        }
        catch (XmlException e)
        {
            throw new LamhaException(ErrorCodes.BookBad, e);
        }
    }

    /// <summary>A file of the book (a picture, the cover) by its path, or null when the book hasn't got it.</summary>
    public Stream? OpenResource(string path) => _entries.TryGetValue(path, out var entry) ? entry.Open() : null;

    /// <summary>A file's media type from the book's manifest ("image/jpeg"), or empty.</summary>
    public string MediaType(string path) => _types.TryGetValue(path, out var type) ? type : "";

    public void Dispose() => _zip.Dispose();

    // The words in a title or a name, without the line breaks and runs of spaces the package file is written with.
    private static string Clean(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private void RefuseDrm()
    {
        if (!_entries.ContainsKey("META-INF/encryption.xml"))
        {
            return;
        }

        var encryption = LoadXml("META-INF/encryption.xml");
        var methods = encryption.Descendants(XmlEnc + "EncryptionMethod").Select(m => (string?)m.Attribute("Algorithm") ?? "");
        if (methods.Any(m => !FontObfuscation.Contains(m)))
        {
            throw new LamhaException(ErrorCodes.BookLocked);
        }
    }

    // META-INF/container.xml names the package file (OPF); a book with several renditions is read by its first.
    private string PackagePath()
    {
        if (!_entries.ContainsKey("META-INF/container.xml"))
        {
            throw new LamhaException(ErrorCodes.BookBad);
        }

        var container = LoadXml("META-INF/container.xml");
        var path = container.Descendants(Container + "rootfile").Select(r => (string?)r.Attribute("full-path")).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        return path is not null && _entries.ContainsKey(path) ? path : throw new LamhaException(ErrorCodes.BookBad);
    }

    // EPUB 3 marks the cover "cover-image"; EPUB 2 names it in <meta name="cover">; some books only call it "cover".
    private static string? CoverPath(XElement? metadata, Dictionary<string, (string Path, string Type, string Properties)> items)
    {
        static bool IsImage((string Path, string Type, string Properties) it) => it.Type.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

        var marked = items.Values.FirstOrDefault(it => it.Properties.Split(' ').Contains("cover-image") && IsImage(it));
        if (marked.Path is not null)
        {
            return marked.Path;
        }

        var id = metadata?.Elements(Opf + "meta").FirstOrDefault(m => (string?)m.Attribute("name") == "cover")?.Attribute("content")?.Value;
        if (id is not null && items.TryGetValue(id, out var named) && IsImage(named))
        {
            return named.Path;
        }

        var called = items.FirstOrDefault(kv => kv.Key.Contains("cover", StringComparison.OrdinalIgnoreCase) && IsImage(kv.Value));
        return called.Value.Path;
    }

    private IReadOnlyList<ContentsEntry> ReadContents(XElement? spine, Dictionary<string, (string Path, string Type, string Properties)> items)
    {
        var nav = items.Values.FirstOrDefault(it => it.Properties.Split(' ').Contains("nav"));
        if (nav.Path is not null && _entries.ContainsKey(nav.Path))
        {
            var doc = LoadXml(nav.Path);
            var toc = doc.Descendants(Xhtml + "nav").FirstOrDefault(n => ((string?)n.Attribute(Ops + "type") ?? "").Split(' ').Contains("toc"))
                ?? doc.Descendants(Xhtml + "nav").FirstOrDefault();
            var list = toc?.Element(Xhtml + "ol");
            if (list is not null)
            {
                return NavEntries(list, nav.Path);
            }
        }

        var ncxId = (string?)spine?.Attribute("toc");
        var ncx = ncxId is not null && items.TryGetValue(ncxId, out var byId) ? byId.Path
            : items.Values.FirstOrDefault(it => it.Type == "application/x-dtbncx+xml").Path;
        if (ncx is not null && _entries.ContainsKey(ncx))
        {
            var map = LoadXml(ncx).Root?.Element(Ncx + "navMap");
            if (map is not null)
            {
                return NcxEntries(map, ncx);
            }
        }

        return [];
    }

    private static List<ContentsEntry> NavEntries(XElement list, string from)
    {
        var entries = new List<ContentsEntry>();
        foreach (var li in list.Elements(Xhtml + "li"))
        {
            var label = li.Element(Xhtml + "a") ?? li.Element(Xhtml + "span");
            var href = (string?)label?.Attribute("href");
            var children = li.Element(Xhtml + "ol") is { } sub ? NavEntries(sub, from) : [];
            if (label is null)
            {
                continue;
            }

            var (path, anchor) = href is null ? ("", "") : BookPaths.Resolve(from, href);
            entries.Add(new ContentsEntry(Clean(label.Value), path, anchor, children));
        }

        return entries;
    }

    private static List<ContentsEntry> NcxEntries(XElement parent, string from)
    {
        var entries = new List<ContentsEntry>();
        foreach (var point in parent.Elements(Ncx + "navPoint"))
        {
            var label = point.Element(Ncx + "navLabel")?.Element(Ncx + "text")?.Value ?? "";
            var src = (string?)point.Element(Ncx + "content")?.Attribute("src");
            var (path, anchor) = src is null ? ("", "") : BookPaths.Resolve(from, src);
            entries.Add(new ContentsEntry(Clean(label), path, anchor, NcxEntries(point, from)));
        }

        return entries;
    }

    // An XML file of the book, read safely: no DTDs fetched or expanded, a size limit, and HTML's named entities
    // (&nbsp;, &mdash;) turned into their characters, since XHTML parts use them without the DTD that declares them.
    private XDocument LoadXml(string path)
    {
        if (!_entries.TryGetValue(path, out var entry))
        {
            throw new LamhaException(ErrorCodes.BookBad);
        }

        if (entry.Length > MaxTextBytes)
        {
            throw new LamhaException(ErrorCodes.BookBad);
        }

        string text;
        using (var reader = new StreamReader(entry.Open(), detectEncodingFromByteOrderMarks: true))
        {
            text = reader.ReadToEnd();
        }

        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = MaxTextBytes };
        using var xml = XmlReader.Create(new StringReader(XhtmlBlocks.ReplaceNamedEntities(text)), settings);
        return XDocument.Load(xml);
    }
}
