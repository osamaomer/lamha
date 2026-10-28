using Lamha.Core.Abstractions;

namespace Lamha.Core.Reading;

/// <summary>What adding a book did.</summary>
/// <param name="Book">The book, as kept.</param>
/// <param name="IsNew">False when the same file was in the library already (it's brought back if it had been removed).</param>
public sealed record LibraryAdd(BookRecord Book, bool IsNew);

/// <summary>
/// The library behind the Read page: books added from a file (or, later, the catalogue), each kept as Lamha's own copy
/// and known by its file's hash, with the reader's place, highlights, notes and bookmarks. A removed book stays a while
/// so Undo can bring it back, then <see cref="PurgeRemoved"/> deletes it and its copy.
/// </summary>
public sealed class Library(IBookStore store, IBookFiles files, TimeProvider clock)
{
    /// <summary>The highlight colours, as the reading page names them.</summary>
    public static readonly IReadOnlyList<string> Colors = ["yellow", "green", "blue", "pink"];

    /// <summary>Whether a file of this name is one the library takes (the comics come with their stage).</summary>
    public static bool CanAdd(string fileName) => FlowingBook.Extensions.ContainsKey(Path.GetExtension(fileName));

    /// <summary>
    /// Adds a book: copies it into the books folder, reads its details and cover, and keeps it. Run it off the UI thread:
    /// a large book takes a few hundred milliseconds to read.
    /// </summary>
    /// <param name="file">The book's file, read to its end.</param>
    /// <param name="fileName">Its name: the format is told by its extension, and the title falls back to it.</param>
    /// <param name="source">"file", or the catalogue's id for a downloaded book.</param>
    /// <param name="cancel">Stops the copy.</param>
    /// <exception cref="LamhaException"><see cref="ErrorCodes.BookUnsupported"/> for a format the reader doesn't open,
    /// <see cref="ErrorCodes.BookBad"/> or <see cref="ErrorCodes.BookLocked"/> from the book itself.</exception>
    public async Task<LibraryAdd> AddAsync(Stream file, string fileName, string source = "file", CancellationToken cancel = default)
    {
        if (!FlowingBook.Extensions.TryGetValue(Path.GetExtension(fileName), out var format))
        {
            throw new LamhaException(ErrorCodes.BookUnsupported);
        }

        var received = await files.ReceiveAsync(file, cancel).ConfigureAwait(false);
        var known = store.Get(received.Hash);
        if (known is not null)
        {
            files.Discard(received);
            if (known.Removed is not null)
            {
                store.SetRemoved(known.Id, null);
                known = known with { Removed = null };
            }

            return new LibraryAdd(known, IsNew: false);
        }

        var name = $"{received.Hash}.{format}";
        var cover = "";
        try
        {
            BookInfo info;
            int words;
            using (var book = FlowingBook.Open(format, files.Open(received.TempFile), Path.GetFileNameWithoutExtension(fileName)))
            {
                info = book.Info;
                words = book.Parts.Sum(p => book.ReadPart(p).Sum(b => CountWords(b.Text)));
                cover = SaveCover(book, received.Hash);
            }

            files.Keep(received, name);
            var record = new BookRecord(received.Hash, info.Title, info.Authors, info.Language, format, name, cover, Path.GetFileName(fileName), source,
                received.Size, words, Now(), null, null, BookPlace.Start, 0, null);
            store.Put(record);
            return new LibraryAdd(record, IsNew: true);
        }
        catch
        {
            files.Discard(received);
            if (cover.Length > 0)
            {
                files.Delete(cover);
            }

            throw;
        }
    }

    /// <summary>The books, the most recently read or added first.</summary>
    public IReadOnlyList<BookRecord> Books() => [.. store.All().OrderByDescending(b => Math.Max(b.Opened ?? 0, b.Added)).ThenBy(b => b.Title, StringComparer.CurrentCulture)];

    public BookRecord? Get(string id) => store.Get(id) is { Removed: null } book ? book : null;

    /// <summary>Opens a book of the library to read. Dispose it when the book closes.</summary>
    public IFlowingBook Open(BookRecord book) => FlowingBook.Open(book.Format, files.Open(book.File), book.Title);

    /// <summary>Opens a book's cover picture, or null when it has none.</summary>
    public Stream? OpenCover(BookRecord book) => book.Cover.Length > 0 ? files.Open(book.Cover) : null;

    /// <summary>Keeps where the reader is, and that the book was read now.</summary>
    public void SavePlace(string id, BookPlace place, double progress) => store.SetPlace(id, place, Math.Clamp(progress, 0, 1), Now());

    public void MarkFinished(string id, bool finished) => store.SetFinished(id, finished ? Now() : null);

    /// <summary>Takes a book out of the library, for now: <see cref="Restore"/> is Undo.</summary>
    public void Remove(string id) => store.SetRemoved(id, Now());

    public void Restore(string id) => store.SetRemoved(id, null);

    /// <summary>
    /// Deletes for good the books removed more than <paramref name="after"/> ago, their copies, covers and marks, and what
    /// a closed Lamha left half-received. Returns how many books went.
    /// </summary>
    public int PurgeRemoved(TimeSpan after)
    {
        files.Tidy();
        var gone = store.RemovedBefore(Now() - (long)after.TotalMilliseconds);
        foreach (var book in gone)
        {
            store.Delete(book.Id);
            files.Delete(book.File);
            if (book.Cover.Length > 0)
            {
                files.Delete(book.Cover);
            }
        }

        return gone.Count;
    }

    /// <summary>A book's highlights, notes and bookmarks, in the book's order.</summary>
    public IReadOnlyList<BookMark> Marks(string bookId) => store.Marks(bookId);

    /// <summary>Highlights <paramref name="text"/>, found from <paramref name="start"/> to <paramref name="end"/> in the block.</summary>
    public BookMark Highlight(string bookId, BookPlace place, int start, int end, string color, string text, string note = "")
    {
        if (start < 0 || end <= start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "A highlight covers at least one character.");
        }

        var now = Now();
        return store.AddMark(new BookMark(0, bookId, MarkKind.Highlight, place, start, end, CheckColor(color), note.Trim(), text, now, now));
    }

    /// <summary>Bookmarks a block; <paramref name="text"/> is its start, shown in the panel.</summary>
    public BookMark Bookmark(string bookId, BookPlace place, string text)
    {
        var now = Now();
        return store.AddMark(new BookMark(0, bookId, MarkKind.Bookmark, place, 0, 0, "", "", text, now, now));
    }

    /// <summary>Changes a highlight's colour or note; an empty note takes it away.</summary>
    public void UpdateMark(long id, string color, string note)
    {
        if (store.Mark(id) is { Kind: MarkKind.Highlight })
        {
            store.UpdateMark(id, CheckColor(color), note.Trim(), Now());
        }
    }

    public bool DeleteMark(long id) => store.DeleteMark(id);

    /// <summary>
    /// Words as the reader counts them for the time left: runs of letters or digits, with their marks (Arabic's vowels:
    /// "مرحبًا" is one word) and the apostrophes and hyphens inside them ("it's", "well-known").
    /// </summary>
    internal static int CountWords(string text)
    {
        var words = 0;
        var inWord = false;
        foreach (var c in text)
        {
            var letter = char.IsLetterOrDigit(c)
                || (inWord && char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.SpacingCombiningMark);
            if (letter && !inWord)
            {
                words++;
            }

            inWord = letter || (inWord && c is '\'' or '’' or '-');
        }

        return words;
    }

    private static string CheckColor(string color) =>
        Colors.Contains(color) ? color : throw new ArgumentOutOfRangeException(nameof(color), "Not one of the highlight colours.");

    private string SaveCover(IFlowingBook book, string hash)
    {
        if (book.Info.CoverPath is not { } path || book.OpenResource(path) is not { } picture)
        {
            return "";
        }

        var extension = book.MediaType(path) switch
        {
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/svg+xml" => ".svg",
            _ => ".jpg",
        };
        var name = $"{hash}.cover{extension}";
        using (picture)
        {
            files.SaveCover(name, picture);
        }

        return name;
    }

    private long Now() => clock.GetUtcNow().ToUnixTimeMilliseconds();
}
