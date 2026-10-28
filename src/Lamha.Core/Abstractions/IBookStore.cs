namespace Lamha.Core.Abstractions;

/// <summary>Where the reader is in a book: a part of its reading order and a block of that part.</summary>
public sealed record BookPlace(int Part, int Block)
{
    public static readonly BookPlace Start = new(0, 0);
}

/// <summary>A book in the library, as it's kept. Times are milliseconds since 1970 (UTC).</summary>
/// <param name="Id">The SHA-256 of the book's file (hex): the same file added twice is one book.</param>
/// <param name="Title">The book's title, or its file's name.</param>
/// <param name="Authors">Its authors, often one; none for a text file.</param>
/// <param name="Language">The book's language tag, or empty.</param>
/// <param name="Format">"epub" or "txt" (later "docx", "cbz", "cbr").</param>
/// <param name="File">Lamha's copy, by name in the books folder.</param>
/// <param name="Cover">The cover picture taken from the book, by name in the books folder; empty when it has none.</param>
/// <param name="OriginalName">The file's name as it was added ("pride-and-prejudice.epub").</param>
/// <param name="Source">"file" for a book the user added; the catalogue's id for a downloaded one ("gutenberg:1342").</param>
/// <param name="Size">The file's size in bytes.</param>
/// <param name="Words">How many words it has, for the time left.</param>
/// <param name="Added">When it was added.</param>
/// <param name="Opened">When it was last read; null until it's opened.</param>
/// <param name="Finished">When it was marked finished; null while it isn't.</param>
/// <param name="Place">Where the reader is.</param>
/// <param name="Progress">How far through it is, 0–1.</param>
/// <param name="Removed">When it was removed (it stays a while, for Undo); null while it's in the library.</param>
public sealed record BookRecord(
    string Id,
    string Title,
    IReadOnlyList<string> Authors,
    string Language,
    string Format,
    string File,
    string Cover,
    string OriginalName,
    string Source,
    long Size,
    int Words,
    long Added,
    long? Opened,
    long? Finished,
    BookPlace Place,
    double Progress,
    long? Removed);

public enum MarkKind
{
    Highlight,
    Bookmark,
}

/// <summary>A highlight (with or without a note) or a bookmark in a book.</summary>
/// <param name="Id">Its row; 0 before it's kept.</param>
/// <param name="Book">The book's <see cref="BookRecord.Id"/>.</param>
/// <param name="Kind">A highlight or a bookmark.</param>
/// <param name="Place">The part and block it's in.</param>
/// <param name="Start">Where it starts in the block's text (characters); 0 for a bookmark.</param>
/// <param name="End">Where it ends; 0 for a bookmark.</param>
/// <param name="Color">A highlight's colour (<see cref="Reading.Library.Colors"/>); empty for a bookmark.</param>
/// <param name="Note">The user's note; empty when there's none.</param>
/// <param name="Text">The words highlighted, or the start of the bookmarked block: shown in the panel and copied as text.</param>
/// <param name="Created">When it was made.</param>
/// <param name="Modified">When its colour or note last changed.</param>
public sealed record BookMark(
    long Id,
    string Book,
    MarkKind Kind,
    BookPlace Place,
    int Start,
    int End,
    string Color,
    string Note,
    string Text,
    long Created,
    long Modified);

/// <summary>The library's records: its books and their marks.</summary>
public interface IBookStore
{
    /// <summary>A book by id, removed or not.</summary>
    BookRecord? Get(string id);

    /// <summary>The books in the library (not the removed ones).</summary>
    IReadOnlyList<BookRecord> All();

    void Put(BookRecord book);

    void SetPlace(string id, BookPlace place, double progress, long opened);

    void SetFinished(string id, long? finished);

    void SetRemoved(string id, long? removed);

    /// <summary>The books removed before <paramref name="before"/>, for good.</summary>
    IReadOnlyList<BookRecord> RemovedBefore(long before);

    /// <summary>Deletes a book and its marks.</summary>
    void Delete(string id);

    /// <summary>A book's marks, in the book's order.</summary>
    IReadOnlyList<BookMark> Marks(string book);

    BookMark? Mark(long id);

    /// <summary>Keeps a new mark; returns it with its id.</summary>
    BookMark AddMark(BookMark mark);

    void UpdateMark(long id, string color, string note, long modified);

    bool DeleteMark(long id);
}

/// <summary>A book's file received into the books folder, not yet kept.</summary>
/// <param name="TempFile">Its name in the folder while it's checked.</param>
/// <param name="Hash">Its SHA-256 (hex, lower case).</param>
/// <param name="Size">Its size in bytes.</param>
public sealed record ReceivedBook(string TempFile, string Hash, long Size);

/// <summary>
/// The books folder: Lamha's own copy of each book, so a book moved or deleted where it came from still opens, with its
/// highlights and notes; and each book's cover, taken out once so the library doesn't open every book to show it.
/// </summary>
public interface IBookFiles
{
    /// <summary>Copies a book into the folder under a temporary name, hashing it on the way.</summary>
    Task<ReceivedBook> ReceiveAsync(Stream source, CancellationToken cancel = default);

    /// <summary>Opens a file of the folder to read.</summary>
    Stream Open(string name);

    /// <summary>Gives a received book its name for good.</summary>
    void Keep(ReceivedBook received, string name);

    /// <summary>Deletes a received book that isn't kept.</summary>
    void Discard(ReceivedBook received);

    /// <summary>Writes a book's cover picture under <paramref name="name"/>.</summary>
    void SaveCover(string name, Stream picture);

    /// <summary>Deletes a file of the folder; nothing when it's gone already.</summary>
    void Delete(string name);

    /// <summary>Deletes what a closed or crashed Lamha left half-received.</summary>
    void Tidy();
}
