using System.Text;

namespace Lamha.Core.Reading;

/// <summary>
/// A text or Word file's blocks cut into parts at its chapters, as an EPUB's are: a part starts at each heading of the
/// highest level the file uses (a "PART" above its "CHAPTER"s, or the chapters), and those headings are the contents.
/// </summary>
internal sealed class BlockParts
{
    private readonly List<IReadOnlyList<BookBlock>> _blocks = [];

    public BlockParts(IReadOnlyList<BookBlock> blocks)
    {
        var headings = blocks.Where(b => b.Kind == BlockKind.Heading).Select(b => b.Level).ToList();
        var top = headings.Count > 0 ? headings.Min() : 0;
        var parts = new List<BookPart>();
        var contents = new List<ContentsEntry>();
        var current = new List<BookBlock>();
        foreach (var block in blocks)
        {
            if (block.Kind == BlockKind.Heading && block.Level == top)
            {
                if (current.Count > 0)
                {
                    Close(current, parts);
                    current = [];
                }

                contents.Add(new ContentsEntry(block.Text.Replace('\n', ' '), PathOf(parts.Count), "", []));
            }

            current.Add(block);
        }

        if (current.Count > 0 || parts.Count == 0)
        {
            Close(current, parts);
        }

        Parts = parts;
        Contents = contents;
    }

    public IReadOnlyList<BookPart> Parts { get; }

    public IReadOnlyList<ContentsEntry> Contents { get; }

    public IReadOnlyList<BookBlock> Read(BookPart part)
    {
        var index = Parts.ToList().FindIndex(p => p.Path == part.Path);
        return index >= 0 ? _blocks[index] : throw new LamhaException(ErrorCodes.BookBad);
    }

    private static string PathOf(int index) => $"part-{index + 1}";

    private void Close(List<BookBlock> blocks, List<BookPart> parts)
    {
        parts.Add(new BookPart(PathOf(parts.Count), true));
        _blocks.Add(blocks);
    }
}

/// <summary>A block's runs as they're written: text of the same style and link joins one run; the ends are trimmed.</summary>
internal sealed class RunBuilder
{
    private readonly List<BookRun> _runs = [];
    private readonly StringBuilder _text = new();
    private RunStyle _style;
    private string? _link;

    public bool HasText => _runs.Any(r => r.Text.Any(c => !char.IsWhiteSpace(c))) || _text.ToString().Any(c => !char.IsWhiteSpace(c));

    public void Add(string text, RunStyle style, string? link)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (_text.Length > 0 && (style != _style || link != _link))
        {
            _runs.Add(new BookRun(_text.ToString(), _style, _link));
            _text.Clear();
        }

        (_style, _link) = (style, link);
        _text.Append(text);
    }

    /// <summary>The runs so far, without the block's leading and trailing spaces; the builder starts again empty.</summary>
    public List<BookRun> Take()
    {
        if (_text.Length > 0)
        {
            _runs.Add(new BookRun(_text.ToString(), _style, _link));
            _text.Clear();
        }

        var runs = new List<BookRun>(_runs);
        _runs.Clear();
        while (runs.Count > 0 && runs[0].Text.TrimStart(' ', '\t', '\n').Length == 0)
        {
            runs.RemoveAt(0);
        }

        while (runs.Count > 0 && runs[^1].Text.TrimEnd(' ', '\t', '\n').Length == 0)
        {
            runs.RemoveAt(runs.Count - 1);
        }

        if (runs.Count > 0)
        {
            runs[0] = runs[0] with { Text = runs[0].Text.TrimStart(' ', '\t', '\n') };
            runs[^1] = runs[^1] with { Text = runs[^1].Text.TrimEnd(' ', '\t', '\n') };
        }

        return runs;
    }
}
