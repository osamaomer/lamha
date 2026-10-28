using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace Lamha.Core.Reading;

/// <summary>
/// A Word document (.docx) as a book, read from its XML without Word or the Open XML SDK: paragraphs and their bold,
/// italic and raised runs; headings by their style (Word's built-in "heading 1"…, whatever the style is called in the
/// user's language); lists from its numbering; tables as rows; links and pictures. Deleted tracked changes are left out.
/// The title and author are the document's own, or the file's name.
/// </summary>
public sealed class DocxBook : IFlowingBook
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace V = "urn:schemas-microsoft-com:vml";
    private static readonly XNamespace Rels = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";

    private readonly ZipArchive _zip;
    private readonly BlockParts _parts;

    private DocxBook(ZipArchive zip, string fallbackTitle)
    {
        _zip = zip;
        var main = Relationships("_rels/.rels", "").FirstOrDefault(r => r.Type.EndsWith("/officeDocument", StringComparison.Ordinal)).Target
            ?? "word/document.xml";
        var document = Load(main) ?? throw new LamhaException(ErrorCodes.BookBad);
        var folder = main.Contains('/', StringComparison.Ordinal) ? main[..(main.LastIndexOf('/') + 1)] : "";
        var relsPath = $"{folder}_rels/{main[folder.Length..]}.rels";

        var reader = new Reader(
            Relationships(relsPath, folder).ToDictionary(r => r.Id, r => r, StringComparer.Ordinal),
            HeadingStyles(Load(folder + "styles.xml")),
            Numbering(Load(folder + "numbering.xml")));
        var body = document.Root?.Element(W + "body") ?? throw new LamhaException(ErrorCodes.BookBad);
        reader.Container(body);
        _parts = new BlockParts(reader.Blocks);

        var core = Load("docProps/core.xml")?.Root;
        var title = core?.Element(Dc + "title")?.Value.Trim();
        var author = core?.Element(Dc + "creator")?.Value.Trim();
        Info = new BookInfo(string.IsNullOrEmpty(title) ? fallbackTitle : title, string.IsNullOrEmpty(author) ? [] : [author],
            core?.Element(Dc + "language")?.Value.Trim() ?? "", null);
    }

    public BookInfo Info { get; }

    public IReadOnlyList<BookPart> Parts => _parts.Parts;

    public IReadOnlyList<ContentsEntry> Contents => _parts.Contents;

    /// <summary>Opens a Word document; the book owns the stream.</summary>
    /// <exception cref="LamhaException"><see cref="ErrorCodes.BookBad"/> when it isn't a readable .docx (a password-protected one isn't a ZIP).</exception>
    public static DocxBook Open(Stream file, string fallbackTitle)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);
        }
        catch (InvalidDataException e)
        {
            file.Dispose();
            throw new LamhaException(ErrorCodes.BookBad, e);
        }

        try
        {
            return new DocxBook(zip, fallbackTitle);
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

    public IReadOnlyList<BookBlock> ReadPart(BookPart part) => _parts.Read(part);

    public Stream? OpenResource(string path) => _zip.GetEntry(path)?.Open();

    public string MediaType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".tif" or ".tiff" => "image/tiff",
        _ => "",
    };

    public void Dispose() => _zip.Dispose();

    // Word's built-in heading styles by their English name, which a document keeps in every language ("heading 1"),
    // and any style with an outline level; a style based on one of them is a heading of its level too.
    private static Dictionary<string, int> HeadingStyles(XDocument? styles)
    {
        var levels = new Dictionary<string, int>(StringComparer.Ordinal);
        var basedOn = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var style in styles?.Root?.Elements(W + "style") ?? [])
        {
            if ((string?)style.Attribute(W + "styleId") is not { } id)
            {
                continue;
            }

            var name = ((string?)style.Element(W + "name")?.Attribute(W + "val") ?? "").ToLowerInvariant();
            var outline = (string?)style.Element(W + "pPr")?.Element(W + "outlineLvl")?.Attribute(W + "val");
            if (name == "title")
            {
                levels[id] = 1;
            }
            else if (name.StartsWith("heading ", StringComparison.Ordinal) && int.TryParse(name[8..], out var n) && n is >= 1 and <= 6)
            {
                levels[id] = n;
            }
            else if (int.TryParse(outline, out var o) && o is >= 0 and <= 5)
            {
                levels[id] = o + 1;
            }
            else if ((string?)style.Element(W + "basedOn")?.Attribute(W + "val") is { } parent)
            {
                basedOn[id] = parent;
            }
        }

        foreach (var (id, parent) in basedOn)
        {
            var p = parent;
            for (var depth = 0; depth < 10 && !levels.ContainsKey(id); depth++)
            {
                if (levels.TryGetValue(p, out var level))
                {
                    levels[id] = level;
                }
                else if (!basedOn.TryGetValue(p, out p!))
                {
                    break;
                }
            }
        }

        return levels;
    }

    // Each list (numId) and level: whether it's numbered and where it starts.
    private static Dictionary<(string Num, int Level), (bool Numbered, int Start)> Numbering(XDocument? numbering)
    {
        var lists = new Dictionary<(string, int), (bool, int)>();
        var root = numbering?.Root;
        if (root is null)
        {
            return lists;
        }

        var abstracts = root.Elements(W + "abstractNum").ToDictionary(a => (string?)a.Attribute(W + "abstractNumId") ?? "", a => a, StringComparer.Ordinal);
        foreach (var num in root.Elements(W + "num"))
        {
            var id = (string?)num.Attribute(W + "numId");
            var abstractId = (string?)num.Element(W + "abstractNumId")?.Attribute(W + "val");
            if (id is null || abstractId is null || !abstracts.TryGetValue(abstractId, out var definition))
            {
                continue;
            }

            foreach (var level in definition.Elements(W + "lvl"))
            {
                var ilvl = (int?)level.Attribute(W + "ilvl") ?? 0;
                var format = (string?)level.Element(W + "numFmt")?.Attribute(W + "val") ?? "bullet";
                var start = (int?)level.Element(W + "start")?.Attribute(W + "val") ?? 1;
                lists[(id, ilvl)] = (format is not ("bullet" or "none"), start);
            }
        }

        return lists;
    }

    private List<(string Id, string Type, string Target, bool External)> Relationships(string path, string folder) =>
        [.. (Load(path)?.Root?.Elements(Rels + "Relationship") ?? [])
            .Select(r =>
            {
                var target = (string?)r.Attribute("Target") ?? "";
                var external = (string?)r.Attribute("TargetMode") == "External";
                return ((string?)r.Attribute("Id") ?? "", (string?)r.Attribute("Type") ?? "",
                    external ? target : BookPaths.Resolve(folder + "x", target).Path, external);
            })];

    private XDocument? Load(string path)
    {
        var entry = _zip.GetEntry(path);
        if (entry is null)
        {
            return null;
        }

        if (entry.Length > EpubBook.MaxTextBytes)
        {
            throw new LamhaException(ErrorCodes.BookBad);
        }

        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var xml = XmlReader.Create(entry.Open(), settings);
        return XDocument.Load(xml);
    }

    private sealed class Reader(
        Dictionary<string, (string Id, string Type, string Target, bool External)> rels,
        Dictionary<string, int> headings,
        Dictionary<(string Num, int Level), (bool Numbered, int Start)> lists)
    {
        private readonly RunBuilder _runs = new();
        private readonly Dictionary<(string Num, int Level), int> _counters = [];

        public List<BookBlock> Blocks { get; } = [];

        public void Container(XElement container)
        {
            foreach (var child in container.Elements())
            {
                if (child.Name == W + "p")
                {
                    Paragraph(child);
                }
                else if (child.Name == W + "tbl")
                {
                    Table(child);
                }
                else if (child.Name == W + "sdt")
                {
                    Container(child.Element(W + "sdtContent") ?? new XElement("none"));
                }
            }
        }

        private void Paragraph(XElement p)
        {
            var properties = p.Element(W + "pPr");
            var style = (string?)properties?.Element(W + "pStyle")?.Attribute(W + "val");
            var level = style is not null && headings.TryGetValue(style, out var h) ? h : 0;
            var outline = (int?)properties?.Element(W + "outlineLvl")?.Attribute(W + "val");
            if (level == 0 && outline is >= 0 and <= 5)
            {
                level = outline.Value + 1;
            }

            var (kind, marker, depth) = (level > 0 ? BlockKind.Heading : BlockKind.Paragraph, "", level);
            var numbering = properties?.Element(W + "numPr");
            var numId = (string?)numbering?.Element(W + "numId")?.Attribute(W + "val");
            if (level == 0 && numId is not null and not "0")
            {
                var ilvl = (int?)numbering!.Element(W + "ilvl")?.Attribute(W + "val") ?? 0;
                (kind, marker, depth) = (BlockKind.ListItem, Marker(numId, ilvl), ilvl + 1);
            }

            Inline(p, RunStyle.None, null, kind, depth, marker);
            Flush(kind, depth, marker);
        }

        // "•", or the item's number: each list level counts on, and a level starts again under a new item above it.
        private string Marker(string numId, int ilvl)
        {
            var (numbered, start) = lists.TryGetValue((numId, ilvl), out var list) ? list : (false, 1);
            if (!numbered)
            {
                return "•";
            }

            var next = _counters.TryGetValue((numId, ilvl), out var n) ? n : start;
            _counters[(numId, ilvl)] = next + 1;
            foreach (var deeper in _counters.Keys.Where(k => k.Num == numId && k.Level > ilvl).ToList())
            {
                _counters.Remove(deeper);
            }

            return $"{next}.";
        }

        private void Inline(XElement parent, RunStyle style, string? link, BlockKind kind, int depth, string marker)
        {
            foreach (var child in parent.Elements())
            {
                var name = child.Name;
                if (name == W + "r")
                {
                    Run(child, style, link, kind, depth, marker);
                }
                else if (name == W + "hyperlink")
                {
                    var id = (string?)child.Attribute(R + "id");
                    var target = id is not null && rels.TryGetValue(id, out var rel) && rel.External ? rel.Target : link;
                    Inline(child, style, target, kind, depth, marker);
                }
                else if (name == W + "ins" || name == W + "smartTag" || name == W + "fldSimple" || name == W + "customXml")
                {
                    Inline(child, style, link, kind, depth, marker);
                }
                else if (name == W + "sdt")
                {
                    Inline(child.Element(W + "sdtContent") ?? new XElement("none"), style, link, kind, depth, marker);
                }

                // w:del (deleted in tracked changes), w:pPr, bookmarks and proofing marks have nothing to read.
            }
        }

        private void Run(XElement run, RunStyle style, string? link, BlockKind kind, int depth, string marker)
        {
            var props = run.Element(W + "rPr");
            if (On(props?.Element(W + "b")))
            {
                style |= RunStyle.Bold;
            }

            if (On(props?.Element(W + "i")))
            {
                style |= RunStyle.Italic;
            }

            var align = (string?)props?.Element(W + "vertAlign")?.Attribute(W + "val");
            style |= align == "superscript" ? RunStyle.Superscript : align == "subscript" ? RunStyle.Subscript : RunStyle.None;

            foreach (var part in run.Elements())
            {
                if (part.Name == W + "t")
                {
                    _runs.Add(part.Value, style, link);
                }
                else if (part.Name == W + "tab")
                {
                    _runs.Add(" ", style, link);
                }
                else if ((part.Name == W + "br" && (string?)part.Attribute(W + "type") is null or "textWrapping") || part.Name == W + "cr")
                {
                    _runs.Add("\n", style, link);
                }
                else if (part.Name == W + "drawing" || part.Name == W + "pict")
                {
                    var id = part.Descendants(A + "blip").Select(b => (string?)b.Attribute(R + "embed")).FirstOrDefault()
                        ?? part.Descendants(V + "imagedata").Select(i => (string?)i.Attribute(R + "id")).FirstOrDefault();
                    if (id is not null && rels.TryGetValue(id, out var rel) && !rel.External)
                    {
                        Flush(kind, depth, marker);
                        Blocks.Add(new BookBlock(BlockKind.Image, [], 0, 0, "", rel.Target, []));
                    }
                }
            }
        }

        // A table's row on one line, its cells apart, as XhtmlBlocks does.
        private void Table(XElement table)
        {
            foreach (var row in table.Elements(W + "tr"))
            {
                var first = true;
                foreach (var cell in row.Elements(W + "tc"))
                {
                    if (!first)
                    {
                        _runs.Add(" · ", RunStyle.None, null);
                    }

                    first = false;
                    var paragraphs = cell.Elements(W + "p").ToList();
                    for (var i = 0; i < paragraphs.Count; i++)
                    {
                        if (i > 0)
                        {
                            _runs.Add(" ", RunStyle.None, null);
                        }

                        Inline(paragraphs[i], RunStyle.None, null, BlockKind.Paragraph, 0, "");
                    }
                }

                Flush(BlockKind.Paragraph, 0, "");
            }
        }

        private void Flush(BlockKind kind, int depth, string marker)
        {
            if (!_runs.HasText)
            {
                _runs.Take();
                return;
            }

            Blocks.Add(new BookBlock(kind, _runs.Take(), depth, 0, kind == BlockKind.ListItem ? marker : "", null, []));
        }

        // <w:b/> is on; <w:b w:val="0"/> or "false" is off.
        private static bool On(XElement? toggle) =>
            toggle is not null && (string?)toggle.Attribute(W + "val") is null or "1" or "true" or "on";
    }
}
