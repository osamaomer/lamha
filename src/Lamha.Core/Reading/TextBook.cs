using System.Text;
using System.Text.RegularExpressions;

namespace Lamha.Core.Reading;

/// <summary>
/// A plain text file as a book: its encoding found (UTF-8 or UTF-16, or Windows' Arabic or Western code page for an
/// old file), its paragraphs and chapter headings read from its blank lines, and Project Gutenberg's licence header and
/// footer left out. The title is the file's name.
/// </summary>
public sealed partial class TextBook : IFlowingBook
{
    /// <summary>The largest text file the reader opens: a long novel is under 2 MB.</summary>
    internal const int MaxBytes = 64 * 1024 * 1024;

    private readonly BlockParts _parts;

    private TextBook(string text, string title)
    {
        Info = new BookInfo(title, [], "", null);
        _parts = new BlockParts(Blocks(text));
    }

    public BookInfo Info { get; }

    public IReadOnlyList<BookPart> Parts => _parts.Parts;

    public IReadOnlyList<ContentsEntry> Contents => _parts.Contents;

    /// <summary>Reads a text file (the stream is read and closed).</summary>
    /// <exception cref="LamhaException"><see cref="ErrorCodes.BookBad"/> when it's over 64 MB or isn't text.</exception>
    public static TextBook Open(Stream file, string title)
    {
        byte[] bytes;
        using (file)
        {
            using var copy = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = file.Read(buffer)) > 0)
            {
                if (copy.Length + read > MaxBytes)
                {
                    throw new LamhaException(ErrorCodes.BookBad);
                }

                copy.Write(buffer, 0, read);
            }

            bytes = copy.ToArray();
        }

        var text = Decode(bytes);
        return text.Contains('\0', StringComparison.Ordinal) ? throw new LamhaException(ErrorCodes.BookBad) : new TextBook(text, title);
    }

    public IReadOnlyList<BookBlock> ReadPart(BookPart part) => _parts.Read(part);

    public Stream? OpenResource(string path) => null;

    public string MediaType(string path) => "";

    public void Dispose()
    {
    }

    /// <summary>
    /// The text in its encoding: a byte order mark's; else UTF-8 when the bytes are valid UTF-8; else Windows-1256 when
    /// most of the high bytes are where its Arabic letters are, and Windows-1252 otherwise.
    /// </summary>
    internal static string Decode(byte[] bytes)
    {
        if (bytes is [0xEF, 0xBB, 0xBF, ..])
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes is [0xFF, 0xFE, ..])
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes is [0xFE, 0xFF, ..])
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Windows-1256 has its Arabic letters at C1–DA; in Windows-1252 those are capitals with accents, rarer than
            // the small letters at E0–FF that Western text uses.
            var arabic = bytes.Count(b => b is >= 0xC1 and <= 0xDA);
            var western = bytes.Count(b => b >= 0xE0);
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(arabic > western ? 1256 : 1252).GetString(bytes);
        }
    }

    /// <summary>
    /// Paragraphs from the lines. A file whose paragraphs are set apart by blank lines, with short lines, has them
    /// wrapped by hand (Project Gutenberg's are 70 characters), so a paragraph's lines join, unless they're indented
    /// (verse, a letter's address); a file without blank lines between its text, or whose lines are mostly long (a
    /// paragraph per line), has a paragraph per line. A short line alone that names a chapter, or is in capitals, is a
    /// heading.
    /// </summary>
    internal static List<BookBlock> Blocks(string text)
    {
        var lines = Gutenberg(text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')).Split('\n');
        var written = lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var first = Array.FindIndex(lines, l => l.Trim().Length > 0);
        var last = Array.FindLastIndex(lines, l => l.Trim().Length > 0);
        var between = first < 0 ? 0 : lines[first..last].Count(l => l.Trim().Length == 0);
        var byBlankLines = between > 0 && written.Count(l => l.Length > 100) * 2 < written.Count;

        var paragraphs = new List<List<string>>();
        var current = new List<string>();
        foreach (var line in lines)
        {
            if (line.Trim().Length == 0 || !byBlankLines)
            {
                if (current.Count > 0)
                {
                    paragraphs.Add(current);
                    current = [];
                }

                if (line.Trim().Length == 0)
                {
                    continue;
                }
            }

            current.Add(line.TrimEnd());
        }

        if (current.Count > 0)
        {
            paragraphs.Add(current);
        }

        var blocks = new List<BookBlock>();
        var inIllustration = false;
        foreach (var paragraph in paragraphs)
        {
            var indented = paragraph.Count > 1 && paragraph.Skip(1).Any(l => l.Length > 0 && char.IsWhiteSpace(l[0]));
            var joined = string.Join(indented ? "\n" : " ", paragraph.Select(l => l.Trim()));

            // Project Gutenberg's "[Illustration: a caption]" marks a picture the text file hasn't got, sometimes over
            // several paragraphs: the brackets go, the words in them stay (a chapter's heading can be one).
            var illustration = IllustrationStart().Match(joined);
            if (illustration.Success)
            {
                joined = joined[illustration.Length..].TrimStart();
                inIllustration = true;
            }

            if (inIllustration && joined.EndsWith(']'))
            {
                joined = joined[..^1].TrimEnd();
                inIllustration = false;
            }

            if (joined.Length == 0)
            {
                continue;
            }

            var level = paragraph.Count == 1 || illustration.Success ? HeadingLevel(joined) : 0;
            blocks.Add(new BookBlock(level > 0 ? BlockKind.Heading : BlockKind.Paragraph, Runs(joined), level, 0, "", null, []));
        }

        return blocks;
    }

    // Project Gutenberg writes italics as _words_.
    private static List<BookRun> Runs(string text)
    {
        var runs = new List<BookRun>();
        var at = 0;
        foreach (Match m in Underscored().Matches(text))
        {
            if (m.Index > at)
            {
                runs.Add(new BookRun(text[at..m.Index], RunStyle.None, null));
            }

            runs.Add(new BookRun(m.Groups[1].Value, RunStyle.Italic, null));
            at = m.Index + m.Length;
        }

        if (at < text.Length)
        {
            runs.Add(new BookRun(text[at..], RunStyle.None, null));
        }

        return runs;
    }

    // "BOOK ONE", "PART II", "VOLUME 3" above "CHAPTER I", "Chapter 12. The Ball", "LETTER 4"; below them, a short line in
    // capitals, which cuts the parts only in a file without chapters.
    private static int HeadingLevel(string line)
    {
        if (line.Length > 70)
        {
            return 0;
        }

        if (Division().IsMatch(line))
        {
            return 1;
        }

        if (Chapter().IsMatch(line))
        {
            return 2;
        }

        var letters = line.Where(char.IsLetter).ToList();
        return letters.Count >= 3 && letters.All(char.IsUpper) && !line.EndsWith('.') ? 3 : 0;
    }

    [GeneratedRegex(@"^\[Illustration:?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex IllustrationStart();

    [GeneratedRegex(@"(?<![\w_])_([^_\n]+)_(?![\w_])")]
    private static partial Regex Underscored();

    // Project Gutenberg's own text before "*** START OF …" and after "*** END OF …": its licence, not the book.
    private static string Gutenberg(string text)
    {
        var start = GutenbergStart().Match(text);
        if (!start.Success)
        {
            return text;
        }

        var body = text[(start.Index + start.Length)..];
        var end = GutenbergEnd().Match(body);
        return end.Success ? body[..end.Index] : body;
    }

    [GeneratedRegex(@"^(BOOK|Book|PART|Part|VOLUME|Volume)\s+([0-9]+|[IVXLC]+|[A-Za-z]+)\b")]
    private static partial Regex Division();

    [GeneratedRegex(@"^(CHAPTER|Chapter|LETTER|Letter|STAVE|Stave)\s+([0-9]+|[IVXLC]+|[A-Za-z]+)\b")]
    private static partial Regex Chapter();

    [GeneratedRegex(@"^\*\*\* ?START OF (THE|THIS) PROJECT GUTENBERG EBOOK[^\n]*\n", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GutenbergStart();

    [GeneratedRegex(@"^\*\*\* ?END OF (THE|THIS) PROJECT GUTENBERG EBOOK", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GutenbergEnd();
}
