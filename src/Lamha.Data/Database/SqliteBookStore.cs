using Lamha.Core.Abstractions;
using Microsoft.Data.Sqlite;

namespace Lamha.Data.Database;

/// <summary>The reader's library in the <c>books</c> and <c>marks</c> tables.</summary>
public sealed class SqliteBookStore(LamhaDatabase db) : IBookStore
{
    private const string Columns =
        "id, title, authors, language, format, file, cover, original_name, source, size, words, added, opened, finished, place_part, place_block, progress, removed";

    private const string MarkColumns = "id, book, kind, part, block, start_at, end_at, color, note, text, created, modified";

    public BookRecord? Get(string id) => db.Run(sql => sql.Query($"SELECT {Columns} FROM books WHERE id = $id", Read, ("$id", id))).FirstOrDefault();

    public IReadOnlyList<BookRecord> All() => db.Run(sql => sql.Query($"SELECT {Columns} FROM books WHERE removed IS NULL", Read));

    public void Put(BookRecord book) => db.Run(sql => sql.Execute(
        $"""
        INSERT OR REPLACE INTO books ({Columns})
        VALUES ($id, $title, $authors, $language, $format, $file, $cover, $original, $source, $size, $words, $added, $opened, $finished, $part, $block, $progress, $removed)
        """,
        ("$id", book.Id), ("$title", book.Title), ("$authors", string.Join('\n', book.Authors)), ("$language", book.Language),
        ("$format", book.Format), ("$file", book.File), ("$cover", book.Cover), ("$original", book.OriginalName), ("$source", book.Source),
        ("$size", book.Size), ("$words", book.Words), ("$added", book.Added), ("$opened", book.Opened), ("$finished", book.Finished),
        ("$part", book.Place.Part), ("$block", book.Place.Block), ("$progress", book.Progress), ("$removed", book.Removed)));

    public void SetPlace(string id, BookPlace place, double progress, long opened) => db.Run(sql => sql.Execute(
        "UPDATE books SET place_part = $part, place_block = $block, progress = $progress, opened = $opened WHERE id = $id",
        ("$id", id), ("$part", place.Part), ("$block", place.Block), ("$progress", progress), ("$opened", opened)));

    public void SetFinished(string id, long? finished) =>
        db.Run(sql => sql.Execute("UPDATE books SET finished = $f WHERE id = $id", ("$id", id), ("$f", finished)));

    public void SetRemoved(string id, long? removed) =>
        db.Run(sql => sql.Execute("UPDATE books SET removed = $r WHERE id = $id", ("$id", id), ("$r", removed)));

    public IReadOnlyList<BookRecord> RemovedBefore(long before) =>
        db.Run(sql => sql.Query($"SELECT {Columns} FROM books WHERE removed IS NOT NULL AND removed < $before", Read, ("$before", before)));

    public void Delete(string id) => db.Transaction(() => db.Run(sql =>
    {
        sql.Execute("DELETE FROM marks WHERE book = $id", ("$id", id));
        sql.Execute("DELETE FROM books WHERE id = $id", ("$id", id));
    }));

    public IReadOnlyList<BookMark> Marks(string book) => db.Run(sql => sql.Query(
        $"SELECT {MarkColumns} FROM marks WHERE book = $book ORDER BY part, block, start_at, id", ReadMark, ("$book", book)));

    public BookMark? Mark(long id) => db.Run(sql => sql.Query($"SELECT {MarkColumns} FROM marks WHERE id = $id", ReadMark, ("$id", id))).FirstOrDefault();

    public BookMark AddMark(BookMark mark) => db.Transaction(() => db.Run(sql =>
    {
        sql.Execute(
            """
            INSERT INTO marks (book, kind, part, block, start_at, end_at, color, note, text, created, modified)
            VALUES ($book, $kind, $part, $block, $start, $end, $color, $note, $text, $created, $modified)
            """,
            ("$book", mark.Book), ("$kind", mark.Kind == MarkKind.Bookmark ? "bookmark" : "highlight"), ("$part", mark.Place.Part),
            ("$block", mark.Place.Block), ("$start", mark.Start), ("$end", mark.End), ("$color", mark.Color), ("$note", mark.Note),
            ("$text", mark.Text), ("$created", mark.Created), ("$modified", mark.Modified));
        return mark with { Id = sql.Long("SELECT last_insert_rowid()") };
    }));

    public void UpdateMark(long id, string color, string note, long modified) => db.Run(sql => sql.Execute(
        "UPDATE marks SET color = $color, note = $note, modified = $modified WHERE id = $id",
        ("$id", id), ("$color", color), ("$note", note), ("$modified", modified)));

    public bool DeleteMark(long id) => db.Run(sql => sql.Execute("DELETE FROM marks WHERE id = $id", ("$id", id)) > 0);

    private static BookRecord Read(SqliteDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2).Length == 0 ? [] : r.GetString(2).Split('\n'), r.GetString(3), r.GetString(4),
        r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8), r.GetInt64(9), r.GetInt32(10), r.GetInt64(11),
        r.IsDBNull(12) ? null : r.GetInt64(12), r.IsDBNull(13) ? null : r.GetInt64(13), new BookPlace(r.GetInt32(14), r.GetInt32(15)),
        r.GetDouble(16), r.IsDBNull(17) ? null : r.GetInt64(17));

    private static BookMark ReadMark(SqliteDataReader r) => new(
        r.GetInt64(0), r.GetString(1), r.GetString(2) == "bookmark" ? MarkKind.Bookmark : MarkKind.Highlight, new BookPlace(r.GetInt32(3), r.GetInt32(4)),
        r.GetInt32(5), r.GetInt32(6), r.GetString(7), r.GetString(8), r.GetString(9), r.GetInt64(10), r.GetInt64(11));
}
