namespace Lamha.Core.Reading;

/// <summary>
/// A book the reading page lays out as flowing text (decision 0022): an EPUB, a text file or a Word document, read as
/// parts of blocks. Dispose it when the book closes.
/// </summary>
public interface IFlowingBook : IDisposable
{
    BookInfo Info { get; }

    /// <summary>The parts, in reading order: an EPUB's own; a text or Word file's, cut at its chapters' headings.</summary>
    IReadOnlyList<BookPart> Parts { get; }

    /// <summary>The contents; empty when the book has neither a table nor headings.</summary>
    IReadOnlyList<ContentsEntry> Contents { get; }

    /// <summary>A part's blocks.</summary>
    /// <exception cref="LamhaException"><see cref="ErrorCodes.BookBad"/> when the part can't be read.</exception>
    IReadOnlyList<BookBlock> ReadPart(BookPart part);

    /// <summary>A picture of the book by its path (a block's <see cref="BookBlock.ImagePath"/>), or null.</summary>
    Stream? OpenResource(string path);

    /// <summary>A picture's media type ("image/png"), or empty when it isn't known.</summary>
    string MediaType(string path);
}

/// <summary>Opens a flowing book by its format, as the library names it.</summary>
public static class FlowingBook
{
    /// <summary>The library's formats that open as flowing text, by file extension.</summary>
    public static readonly IReadOnlyDictionary<string, string> Extensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".epub"] = "epub",
        [".txt"] = "txt",
        [".docx"] = "docx",
    };

    /// <summary>Opens <paramref name="file"/> as a book of <paramref name="format"/>; the book owns the stream.</summary>
    /// <exception cref="LamhaException"><see cref="ErrorCodes.BookBad"/>, <see cref="ErrorCodes.BookLocked"/> or
    /// <see cref="ErrorCodes.BookUnsupported"/> for a format that isn't flowing text.</exception>
    public static IFlowingBook Open(string format, Stream file, string fallbackTitle) => format switch
    {
        "epub" => EpubBook.Open(file, fallbackTitle),
        "txt" => TextBook.Open(file, fallbackTitle),
        "docx" => DocxBook.Open(file, fallbackTitle),
        _ => throw new LamhaException(ErrorCodes.BookUnsupported),
    };
}
