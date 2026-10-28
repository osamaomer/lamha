using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Lamha.Core.Reading;

/// <summary>
/// A book's XHTML part as the reading page's blocks: what the markup means (a heading, a paragraph, italics, a link, a
/// list, a quotation, a picture) without its CSS, since the page sets the text in the reader's own font, size and colours.
/// Spaces are collapsed as a browser does; scripts and styles are dropped.
/// </summary>
internal static partial class XhtmlBlocks
{
    private static readonly HashSet<string> Skipped = new(StringComparer.Ordinal) { "head", "script", "style", "title", "template", "noscript" };

    // Elements that make a block of their own, read as a paragraph when they hold text.
    private static readonly HashSet<string> Paragraphs = new(StringComparer.Ordinal)
    {
        "p", "div", "section", "article", "aside", "header", "footer", "main", "nav", "figure", "figcaption",
        "address", "center", "dl", "dt", "dd", "table", "thead", "tbody", "tfoot", "caption", "body",
    };

    private static readonly HashSet<string> XmlEntities = new(StringComparer.Ordinal) { "amp", "lt", "gt", "quot", "apos" };

    /// <summary>The blocks of a part. <paramref name="from"/> is the part's path, to resolve its pictures and links.</summary>
    public static IReadOnlyList<BookBlock> Read(XDocument document, string from)
    {
        var writer = new Writer(from);
        var root = document.Root;
        var body = root?.Elements().FirstOrDefault(e => e.Name.LocalName == "body") ?? root;
        if (body is not null)
        {
            writer.Walk(body, RunStyle.None, null);
        }

        writer.Flush();
        return writer.Done();
    }

    /// <summary>
    /// HTML's named entities (&amp;nbsp;, &amp;mdash;) as their characters, so a part parses as XML without the DTD that
    /// declares them; XML's own five are kept, and a name HTML doesn't know is dropped.
    /// </summary>
    public static string ReplaceNamedEntities(string text)
        => !text.Contains('&', StringComparison.Ordinal) ? text : NamedEntity().Replace(text, m =>
        {
            if (XmlEntities.Contains(m.Groups[1].Value))
            {
                return m.Value;
            }

            var decoded = WebUtility.HtmlDecode(m.Value);
            return decoded == m.Value ? "" : decoded;
        });

    [GeneratedRegex("&([A-Za-z][A-Za-z0-9]{1,31});")]
    private static partial Regex NamedEntity();

    // HTML collapses only its own spaces: a no-break space (U+00A0) stays, so "Mr. Darcy" isn't split across lines.
    [GeneratedRegex("[ \t\r\n\f]+")]
    private static partial Regex Spaces();

    private sealed class Writer(string from)
    {
        private readonly List<BookBlock> _blocks = [];
        private readonly List<BookRun> _runs = [];
        private readonly StringBuilder _text = new();
        private readonly List<string> _anchors = [];
        private readonly Stack<(bool Ordered, int Next)> _lists = new();
        private RunStyle _runStyle;
        private string? _runLink;
        private BlockKind _kind = BlockKind.Paragraph;
        private int _level;
        private string _marker = "";
        private int _quote;
        private bool _pre;
        private bool _hasText;

        // Whether the block so far ends in a space or a line break, so the next text's leading space is dropped.
        private bool _atSpace = true;

        public IReadOnlyList<BookBlock> Done()
        {
            if (_anchors.Count > 0 && _blocks.Count > 0)
            {
                _blocks[^1] = _blocks[^1] with { Anchors = [.. _blocks[^1].Anchors, .. _anchors] };
            }

            return _blocks;
        }

        public void Walk(XElement element, RunStyle style, string? link)
        {
            var name = element.Name.LocalName.ToLowerInvariant();
            if (Skipped.Contains(name))
            {
                return;
            }

            if ((string?)element.Attribute("id") is { Length: > 0 } id)
            {
                _anchors.Add(id);
            }

            if (name == "a" && (string?)element.Attribute("name") is { Length: > 0 } anchorName)
            {
                _anchors.Add(anchorName);
            }

            switch (name)
            {
                case "br":
                    Append("\n", style, link, raw: true);
                    return;
                case "hr":
                    Flush();
                    Add(BlockKind.Rule, [], null);
                    return;
                case "img":
                case "image":
                    Image(element);
                    return;
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    Block(element, BlockKind.Heading, name[1] - '0', "", style, link);
                    return;
                case "pre":
                    _pre = true;
                    Block(element, BlockKind.Preformatted, 0, "", style | RunStyle.Code, link);
                    _pre = false;
                    return;
                case "blockquote":
                    Flush();
                    _quote++;
                    Children(element, style, link);
                    Flush();
                    _quote--;
                    return;
                case "ul" or "ol":
                    Flush();
                    _lists.Push((name == "ol", int.TryParse((string?)element.Attribute("start"), out var start) ? start : 1));
                    Children(element, style, link);
                    Flush();
                    _lists.Pop();
                    return;
                case "li":
                    var (ordered, next) = _lists.Count > 0 ? _lists.Pop() : (false, 1);
                    _lists.Push((ordered, next + 1));
                    Block(element, BlockKind.ListItem, _lists.Count, ordered ? $"{next}." : "•", style, link);
                    return;
                case "tr":
                    // A table's row on one line, its cells apart: enough for the small tables novels have.
                    Flush();
                    var first = true;
                    foreach (var cell in element.Elements())
                    {
                        if (!first)
                        {
                            Append(" · ", style, link, raw: true);
                        }

                        first = false;
                        Children(cell, style, link);
                    }

                    Flush();
                    return;
            }

            if (Paragraphs.Contains(name))
            {
                Block(element, BlockKind.Paragraph, 0, "", style, link);
                return;
            }

            Children(element, Inline(name, style), name == "a" ? Link(element) ?? link : link);
        }

        public void Flush()
        {
            if (_text.Length > 0)
            {
                _runs.Add(new BookRun(_text.ToString(), _runStyle, _runLink));
                _text.Clear();
            }

            // The block's last run loses its trailing space and line breaks.
            while (_runs.Count > 0)
            {
                var last = _runs[^1];
                var trimmed = _pre ? last.Text.TrimEnd('\n') : last.Text.TrimEnd(' ', '\n');
                if (trimmed.Length > 0)
                {
                    _runs[^1] = last with { Text = trimmed };
                    break;
                }

                _runs.RemoveAt(_runs.Count - 1);
            }

            if (_runs.Count > 0 && _hasText)
            {
                Add(_kind, [.. _runs], _kind == BlockKind.ListItem || _kind == BlockKind.Heading ? _level : 0);
            }

            _runs.Clear();
            _hasText = false;
            _atSpace = true;
            _kind = BlockKind.Paragraph;
            _level = 0;
            _marker = "";
        }

        private static RunStyle Inline(string name, RunStyle style) => name switch
        {
            "em" or "i" or "cite" or "dfn" or "var" => style | RunStyle.Italic,
            "strong" or "b" => style | RunStyle.Bold,
            "sup" => style | RunStyle.Superscript,
            "sub" => style | RunStyle.Subscript,
            "code" or "kbd" or "samp" or "tt" => style | RunStyle.Code,
            _ => style,
        };

        private void Block(XElement element, BlockKind kind, int level, string marker, RunStyle style, string? link)
        {
            // A list item's first paragraph (<li><p>…</p></li>) is the item itself, not a new block under it.
            var keepItem = kind == BlockKind.Paragraph && _kind == BlockKind.ListItem && !_hasText;
            if (!keepItem)
            {
                Flush();
                _kind = kind;
                _level = level;
                _marker = marker;
            }

            Children(element, style, link);
            Flush();
        }

        private void Children(XElement element, RunStyle style, string? link)
        {
            foreach (var node in element.Nodes())
            {
                if (node is XText text)
                {
                    Append(text.Value, style, link, raw: false);
                }
                else if (node is XElement child)
                {
                    Walk(child, style, link);
                }
            }
        }

        private void Append(string text, RunStyle style, string? link, bool raw)
        {
            if (!raw && !_pre)
            {
                text = Spaces().Replace(text, " ");
                if (_atSpace)
                {
                    text = text.TrimStart(' ');
                }
            }

            if (text.Length == 0)
            {
                return;
            }

            if (style != _runStyle || link != _runLink)
            {
                if (_text.Length > 0)
                {
                    _runs.Add(new BookRun(_text.ToString(), _runStyle, _runLink));
                    _text.Clear();
                }

                _runStyle = style;
                _runLink = link;
            }

            _text.Append(text);
            _hasText |= text.Any(c => c is not (' ' or '\n'));
            _atSpace = text[^1] is ' ' or '\n';
        }

        private void Image(XElement element)
        {
            var src = (string?)element.Attribute("src")
                ?? element.Attributes().FirstOrDefault(a => a.Name.LocalName == "href")?.Value;
            if (string.IsNullOrEmpty(src) || BookPaths.IsExternal(src))
            {
                return;
            }

            var (kind, level) = (_kind, _level);
            Flush();
            var alt = ((string?)element.Attribute("alt") ?? "").Trim();
            Add(BlockKind.Image, alt.Length > 0 ? [new BookRun(alt, RunStyle.None, null)] : [], BookPaths.Resolve(from, src).Path);

            // Text after a picture in the same paragraph or heading carries on as a block of the same kind.
            if (kind != BlockKind.ListItem)
            {
                (_kind, _level) = (kind, level);
            }
        }

        private string? Link(XElement element)
        {
            var href = (string?)element.Attribute("href");
            if (string.IsNullOrEmpty(href))
            {
                return null;
            }

            if (BookPaths.IsExternal(href))
            {
                return href;
            }

            var (path, anchor) = BookPaths.Resolve(from, href);
            return anchor.Length > 0 ? $"{path}#{anchor}" : path;
        }

        private void Add(BlockKind kind, IReadOnlyList<BookRun> runs, int level) => Add(kind, runs, level, null);

        private void Add(BlockKind kind, IReadOnlyList<BookRun> runs, string? imagePath) => Add(kind, runs, 0, imagePath);

        private void Add(BlockKind kind, IReadOnlyList<BookRun> runs, int level, string? imagePath)
        {
            _blocks.Add(new BookBlock(kind, runs, level, _quote, kind == BlockKind.ListItem ? _marker : "", imagePath, [.. _anchors]));
            _anchors.Clear();
        }
    }
}
