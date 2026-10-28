namespace Lamha.Core.Reading;

/// <summary>What a book says about itself: its title, authors, language and cover.</summary>
/// <param name="Title">The title, or the file's name when the book has none.</param>
/// <param name="Authors">Its authors, in the book's order (often one).</param>
/// <param name="Language">The book's language tag ("en", "en-GB"), or empty.</param>
/// <param name="CoverPath">The cover picture's path inside the book, for <see cref="EpubBook.OpenResource"/>; null without one.</param>
public sealed record BookInfo(string Title, IReadOnlyList<string> Authors, string Language, string? CoverPath);

/// <summary>One part of the book's reading order (an EPUB's spine): a chapter, a title page, the notes.</summary>
/// <param name="Path">Its path inside the book.</param>
/// <param name="Linear">False for a part read only from a link (a note's page), not in turn.</param>
public sealed record BookPart(string Path, bool Linear);

/// <summary>A line of the book's contents, with the lines under it.</summary>
/// <param name="Title">The line as the book names it ("Chapter 1").</param>
/// <param name="Path">The part it opens, a <see cref="BookPart.Path"/>.</param>
/// <param name="Anchor">The place in that part (an element's id), or empty for its start.</param>
/// <param name="Children">The lines under it (a part's chapters), often none.</param>
public sealed record ContentsEntry(string Title, string Path, string Anchor, IReadOnlyList<ContentsEntry> Children);

/// <summary>What a <see cref="BookBlock"/> is on the page.</summary>
public enum BlockKind
{
    Paragraph,

    /// <summary>A heading; its level (1–6) is in <see cref="BookBlock.Level"/>.</summary>
    Heading,

    /// <summary>An item of a list; how deep it is in <see cref="BookBlock.Level"/>, its "•" or "3." in <see cref="BookBlock.Marker"/>.</summary>
    ListItem,

    /// <summary>Text kept as written, line by line (a poem set as preformatted text, code).</summary>
    Preformatted,

    /// <summary>A picture; its path in <see cref="BookBlock.ImagePath"/>, its words in the runs.</summary>
    Image,

    /// <summary>A line across the page between scenes (an &lt;hr&gt;).</summary>
    Rule,
}

/// <summary>How a run of text is set: kept as flags so a word can be bold and italic.</summary>
[Flags]
public enum RunStyle
{
    None = 0,
    Italic = 1,
    Bold = 2,
    Superscript = 4,
    Subscript = 8,
    Code = 16,
}

/// <summary>Text set one way.</summary>
/// <param name="Text">The text; a line break inside a block is "\n".</param>
/// <param name="Style">How it's set.</param>
/// <param name="Link">Where it links: a path inside the book with an optional "#anchor", or a web address; null when it doesn't.</param>
public sealed record BookRun(string Text, RunStyle Style, string? Link);

/// <summary>
/// A block of a chapter, as the reading page shows it: a paragraph, a heading, a list item, a picture. The page lays it
/// out in its own font (flowing text), so only what the text means is kept, not the book's CSS.
/// </summary>
/// <param name="Kind">What the block is.</param>
/// <param name="Runs">Its text, run by run; a picture's are its description (alt), often none.</param>
/// <param name="Level">A heading's level (1–6), a list item's depth (1 for the top), or 0.</param>
/// <param name="Quote">How many quotations (&lt;blockquote&gt;) it's inside, for the page's indent.</param>
/// <param name="Marker">A list item's "•" or "3."; empty for anything else.</param>
/// <param name="ImagePath">A picture's path inside the book; null for anything else.</param>
/// <param name="Anchors">The ids in the book's markup that point here, for the contents and the notes' links.</param>
public sealed record BookBlock(
    BlockKind Kind,
    IReadOnlyList<BookRun> Runs,
    int Level,
    int Quote,
    string Marker,
    string? ImagePath,
    IReadOnlyList<string> Anchors)
{
    /// <summary>The block's text without its styles, as the card and search read it.</summary>
    public string Text => string.Concat(Runs.Select(r => r.Text));
}
