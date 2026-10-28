using System.Text;
using Lamha.Core.Abstractions;
using Lamha.Core.Reading;
using Lamha.Data.Books;
using Lamha.Data.Database;
using Microsoft.Extensions.Time.Testing;

namespace Lamha.Core.Tests.Reading;

/// <summary>The library on the real SQLite store (in memory) and a real books folder (in the temp folder).</summary>
public sealed class LibraryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "lamha-books-" + Guid.NewGuid().ToString("N"));
    private readonly LamhaDatabase _db = LamhaDatabase.InMemory();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero));
    private readonly SqliteBookStore _store;
    private readonly Library _library;

    public LibraryTests()
    {
        _store = new SqliteBookStore(_db);
        _library = new Library(_store, new BookFolder(_folder), _clock);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task AnEpubIsKeptAsLamhasCopyWithItsDetailsAndCover()
    {
        var added = await _library.AddAsync(EpubBookTests.Book3(), "pride.epub", cancel: TestContext.Current.CancellationToken);

        Assert.True(added.IsNew);
        var book = added.Book;
        Assert.Equal(64, book.Id.Length);
        Assert.Equal(("Pride and Prejudice", "en", "epub", "pride.epub", "file"), (book.Title, book.Language, book.Format, book.OriginalName, book.Source));
        Assert.Equal(["Jane Austen"], book.Authors);
        Assert.Equal($"{book.Id}.epub", book.File);
        Assert.Equal($"{book.Id}.cover.jpg", book.Cover);
        Assert.True(book.Words > 50);
        Assert.Equal(_clock.GetUtcNow().ToUnixTimeMilliseconds(), book.Added);
        var kept = _library.Get(book.Id)!;
        Assert.Equal(book with { Authors = kept.Authors }, kept);
        Assert.Equal(book.Authors, kept.Authors);

        // Lamha's copy opens without the original, and the cover is read from the folder, not the book.
        using (var epub = _library.Open(book))
        {
            Assert.Equal(3, epub.Parts.Count);
        }

        using var cover = _library.OpenCover(book)!;
        Assert.Equal("jpg", new StreamReader(cover).ReadToEnd());
        Assert.Equal([book.Cover, book.File], Directory.GetFiles(_folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheSameFileAddedAgainIsTheSameBook()
    {
        var first = await _library.AddAsync(EpubBookTests.Book3(), "pride.epub", cancel: TestContext.Current.CancellationToken);
        var again = await _library.AddAsync(EpubBookTests.Book3(), "a copy of pride.epub", cancel: TestContext.Current.CancellationToken);

        Assert.False(again.IsNew);
        Assert.Equal(first.Book.Id, again.Book.Id);
        Assert.Single(_library.Books());
        Assert.Equal(2, Directory.GetFiles(_folder).Length); // the copy and its cover; the second file was discarded
    }

    [Fact]
    public async Task ATextFileIsABookNamedAfterItsFile()
    {
        var text = new MemoryStream(Encoding.UTF8.GetBytes("Chapter 1\n\nIt's a short, well-known story: 42 words? No, fewer."));

        var book = (await _library.AddAsync(text, "My notes.txt", cancel: TestContext.Current.CancellationToken)).Book;

        Assert.Equal(("My notes", "txt", "", 11), (book.Title, book.Format, book.Cover, book.Words));
        Assert.Empty(book.Authors);
    }

    [Fact]
    public async Task WhatTheReaderCantOpenIsntKept()
    {
        var unsupported = await Assert.ThrowsAsync<LamhaException>(() => _library.AddAsync(new MemoryStream([1, 2, 3]), "comic.cbz", cancel: TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.BookUnsupported, unsupported.Code);

        var bad = await Assert.ThrowsAsync<LamhaException>(() => _library.AddAsync(new MemoryStream(Encoding.UTF8.GetBytes("not a zip")), "broken.epub", cancel: TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.BookBad, bad.Code);

        Assert.Empty(_library.Books());
        Assert.Empty(Directory.Exists(_folder) ? Directory.GetFiles(_folder) : []);
        Assert.True(Library.CanAdd("Book.EPUB"));
        Assert.False(Library.CanAdd("book.mobi"));
    }

    [Fact]
    public async Task ThePlaceAndFinishedAreKeptAndTheLastReadComesFirst()
    {
        var pride = (await _library.AddAsync(EpubBookTests.Book3(), "pride.epub", cancel: TestContext.Current.CancellationToken)).Book;
        _clock.Advance(TimeSpan.FromMinutes(1));
        var notes = (await _library.AddAsync(new MemoryStream(Encoding.UTF8.GetBytes("notes")), "notes.txt", cancel: TestContext.Current.CancellationToken)).Book;
        Assert.Equal([notes.Id, pride.Id], _library.Books().Select(b => b.Id));

        _clock.Advance(TimeSpan.FromMinutes(1));
        _library.SavePlace(pride.Id, new BookPlace(1, 12), 1.4);
        _library.MarkFinished(pride.Id, finished: true);

        var read = _library.Get(pride.Id)!;
        Assert.Equal((new BookPlace(1, 12), 1.0), (read.Place, read.Progress));
        Assert.Equal(_clock.GetUtcNow().ToUnixTimeMilliseconds(), read.Opened);
        Assert.NotNull(read.Finished);
        Assert.Equal([pride.Id, notes.Id], _library.Books().Select(b => b.Id));

        _library.MarkFinished(pride.Id, finished: false);
        Assert.Null(_library.Get(pride.Id)!.Finished);
    }

    [Fact]
    public async Task ARemovedBookCanBeRestoredUntilItsPurged()
    {
        var book = (await _library.AddAsync(EpubBookTests.Book3(), "pride.epub", cancel: TestContext.Current.CancellationToken)).Book;
        _library.Highlight(book.Id, new BookPlace(0, 1), 0, 13, "yellow", "It is a truth");

        _library.Remove(book.Id);
        Assert.Empty(_library.Books());
        Assert.Null(_library.Get(book.Id));

        _library.Restore(book.Id);
        Assert.Single(_library.Books());

        _library.Remove(book.Id);
        Assert.Equal(0, _library.PurgeRemoved(TimeSpan.FromMinutes(1))); // too recent: Undo may still come
        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(1, _library.PurgeRemoved(TimeSpan.FromMinutes(1)));

        Assert.Null(_store.Get(book.Id));
        Assert.Empty(_library.Marks(book.Id));
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task AddingARemovedBookAgainBringsItBackWithItsMarks()
    {
        var book = (await _library.AddAsync(EpubBookTests.Book3(), "pride.epub", cancel: TestContext.Current.CancellationToken)).Book;
        _library.Highlight(book.Id, new BookPlace(0, 1), 0, 13, "green", "It is a truth");
        _library.Remove(book.Id);

        var again = await _library.AddAsync(EpubBookTests.Book3(), "pride.epub", cancel: TestContext.Current.CancellationToken);

        Assert.False(again.IsNew);
        Assert.Null(again.Book.Removed);
        Assert.Single(_library.Books());
        Assert.Single(_library.Marks(book.Id));
    }

    [Fact]
    public async Task HighlightsNotesAndBookmarksInTheBooksOrder()
    {
        var book = (await _library.AddAsync(EpubBookTests.Book3(), "pride.epub", cancel: TestContext.Current.CancellationToken)).Book;

        var later = _library.Highlight(book.Id, new BookPlace(1, 0), 0, 5, "blue", "Text.");
        var first = _library.Highlight(book.Id, new BookPlace(0, 1), 21, 32, "yellow", "universally", note: "  a key word ");
        var mark = _library.Bookmark(book.Id, new BookPlace(0, 3), "Before");

        Assert.Equal([first.Id, mark.Id, later.Id], _library.Marks(book.Id).Select(m => m.Id));
        Assert.Equal(("a key word", MarkKind.Highlight), (first.Note, first.Kind));
        Assert.Equal((MarkKind.Bookmark, "", 0, 0), (mark.Kind, mark.Color, mark.Start, mark.End));

        _clock.Advance(TimeSpan.FromSeconds(5));
        _library.UpdateMark(first.Id, "pink", "");
        var changed = _store.Mark(first.Id)!;
        Assert.Equal(("pink", "", first.Created + 5000), (changed.Color, changed.Note, changed.Modified));

        _library.UpdateMark(mark.Id, "pink", "a bookmark has no note");
        Assert.Equal("", _store.Mark(mark.Id)!.Note);

        Assert.True(_library.DeleteMark(later.Id));
        Assert.False(_library.DeleteMark(later.Id));
        Assert.Equal(2, _library.Marks(book.Id).Count);

        Assert.Throws<ArgumentOutOfRangeException>(() => _library.Highlight(book.Id, BookPlace.Start, 0, 4, "red", "Text"));
        Assert.Throws<ArgumentOutOfRangeException>(() => _library.Highlight(book.Id, BookPlace.Start, 4, 4, "yellow", ""));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("It's a well-known truth.", 4)]
    [InlineData("  one\ntwo\tthree  ", 3)]
    [InlineData("مرحبًا بكم في لمحة", 4)]
    [InlineData("— 1813 —", 1)]
    public void WordsAreRunsOfLetters(string text, int words) => Assert.Equal(words, Library.CountWords(text));

    [Fact]
    public async Task TheFolderKeepsOnlyPlainNamesAndTidiesHalfReceivedBooks()
    {
        var folder = new BookFolder(_folder);
        var received = await folder.ReceiveAsync(new MemoryStream([1, 2, 3]), TestContext.Current.CancellationToken);
        Assert.Equal(3, received.Size);
        Assert.Equal("039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81", received.Hash);

        folder.Tidy();
        Assert.Empty(Directory.GetFiles(_folder));
        Assert.Throws<ArgumentException>(() => folder.Open(@"..\lamha.db"));
        Assert.Throws<ArgumentException>(() => folder.Open("sub/book.epub"));
    }
}
