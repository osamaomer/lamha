using System.Globalization;
using Lamha.Core.Abstractions;
using Lamha.Core.Review;
using Microsoft.Data.Sqlite;

namespace Lamha.Data.Database;

/// <summary>Small named values in the <c>state</c> table.</summary>
public sealed class SqliteStateStore(LamhaDatabase db) : IStateStore
{
    public string? Get(string key) => db.Run(sql => sql.Scalar("SELECT value FROM state WHERE key = $k", ("$k", key)) as string);

    public void Set(string key, string? value) => db.Run(sql =>
    {
        if (value is null)
        {
            sql.Execute("DELETE FROM state WHERE key = $k", ("$k", key));
        }
        else
        {
            sql.Execute("INSERT INTO state (key, value) VALUES ($k, $v) ON CONFLICT (key) DO UPDATE SET value = $v", ("$k", key), ("$v", value));
        }
    });
}

/// <summary>The lookup history in the <c>history</c> table.</summary>
public sealed class SqliteHistoryStore(LamhaDatabase db) : IHistoryStore
{
    public bool Add(HistoryEntry entry, int keep) => db.Transaction(() => db.Run(sql =>
    {
        var known = sql.Scalar("SELECT 1 FROM history WHERE key = $k", ("$k", entry.Key)) is not null;
        Upsert(sql, entry);
        Trim(sql, keep);
        return !known;
    }));

    public int Merge(IReadOnlyList<HistoryEntry> entries, int keep) => db.Transaction(() => db.Run(sql =>
    {
        var added = 0;
        foreach (var entry in entries)
        {
            if (sql.Scalar("SELECT 1 FROM history WHERE key = $k", ("$k", entry.Key)) is null)
            {
                Upsert(sql, entry);
                added++;
            }
        }

        Trim(sql, keep);
        return added;
    }));

    public IReadOnlyList<HistoryEntry> Recent(int limit) => db.Run(sql => sql.Query(
        "SELECT word, meaning, language, time FROM history ORDER BY time DESC, id DESC LIMIT $n",
        r => new HistoryEntry(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3)),
        ("$n", limit)));

    public bool Contains(string word) =>
        db.Run(sql => sql.Scalar("SELECT 1 FROM history WHERE key = $k", ("$k", word.ToLowerInvariant())) is not null);

    public int Count() => db.Run(sql => (int)sql.Long("SELECT count(*) FROM history"));

    public void Clear() => db.Run(sql => sql.Execute("DELETE FROM history"));

    /// <summary>Writes the entry as the newest row: entries looked up in the same millisecond keep their order.</summary>
    private static void Upsert(Sql sql, HistoryEntry entry)
    {
        sql.Execute("DELETE FROM history WHERE key = $k", ("$k", entry.Key));
        sql.Execute(
            "INSERT INTO history (key, word, meaning, language, time) VALUES ($k, $w, $m, $l, $t)",
            ("$k", entry.Key), ("$w", entry.Word), ("$m", entry.Meaning), ("$l", entry.Language), ("$t", entry.Time));
    }

    private static void Trim(Sql sql, int keep) => sql.Execute(
        "DELETE FROM history WHERE id NOT IN (SELECT id FROM history ORDER BY time DESC, id DESC LIMIT $n)", ("$n", keep));
}

/// <summary>The review deck in the <c>cards</c> and <c>cards_removed</c> tables.</summary>
public sealed class SqliteDeckStore(LamhaDatabase db) : IDeckStore
{
    private const string Columns = "word, meaning, example, form, definition, english_first, added, due, interval, ease, reps, lapses, last, modified";

    public Card? Get(string key) => db.Run(sql => sql.Query($"SELECT {Columns} FROM cards WHERE key = $k", Read, ("$k", key))).FirstOrDefault();

    public void Put(Card card) => db.Run(sql => sql.Execute(
        $"""
        INSERT OR REPLACE INTO cards (key, {Columns})
        VALUES ($key, $word, $meaning, $example, $form, $definition, $en, $added, $due, $interval, $ease, $reps, $lapses, $last, $modified)
        """,
        ("$key", card.Key), ("$word", card.Word), ("$meaning", card.Meaning), ("$example", card.Example), ("$form", card.Form),
        ("$definition", card.Definition), ("$en", card.EnglishFirst ? 1 : 0), ("$added", card.Added), ("$due", card.Due),
        ("$interval", card.Interval), ("$ease", card.Ease), ("$reps", card.Reps), ("$lapses", card.Lapses), ("$last", card.Last),
        ("$modified", card.Modified)));

    public bool Delete(string key) => db.Run(sql => sql.Execute("DELETE FROM cards WHERE key = $k", ("$k", key)) > 0);

    public IReadOnlyList<Card> Due(long now, int limit) => db.Run(sql => sql.Query(
        $"SELECT {Columns} FROM cards WHERE last IS NOT NULL AND due <= $now ORDER BY due, key LIMIT $n", Read, ("$now", now), ("$n", limit)));

    public IReadOnlyList<Card> NewestNew(int limit) => db.Run(sql => sql.Query(
        $"SELECT {Columns} FROM cards WHERE last IS NULL ORDER BY added DESC, key LIMIT $n", Read, ("$n", limit)));

    public DeckCounts Counts(long now) => db.Run(sql => sql.Query(
        """
        SELECT
            count(*) FILTER (WHERE last IS NOT NULL AND due <= $now),
            count(*) FILTER (WHERE last IS NULL),
            count(*),
            count(*) FILTER (WHERE interval >= 21),
            min(due) FILTER (WHERE last IS NOT NULL AND due > $now)
        FROM cards
        """,
        r => new DeckCounts(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.IsDBNull(4) ? null : r.GetInt64(4)),
        ("$now", now))[0]);

    public Card? FirstDue(long before, string exceptKey) => db.Run(sql => sql.Query(
        $"""
        SELECT {Columns} FROM cards
        WHERE last IS NOT NULL AND due <= $before AND key <> $except AND (meaning <> '' OR definition <> '')
        ORDER BY due, key LIMIT 1
        """,
        Read, ("$before", before), ("$except", exceptKey))).FirstOrDefault();

    public IReadOnlyList<Card> All() => db.Run(sql => sql.Query($"SELECT {Columns} FROM cards ORDER BY key", Read));

    public void Clear(long when) => db.Transaction(() => db.Run(sql =>
    {
        sql.Execute(
            "INSERT INTO cards_removed (key, removed) SELECT key, $t FROM cards WHERE true ON CONFLICT (key) DO UPDATE SET removed = $t",
            ("$t", when));
        sql.Execute("DELETE FROM cards");
    }));

    public long? RemovedAt(string key) => db.Run(sql => sql.Scalar("SELECT removed FROM cards_removed WHERE key = $k", ("$k", key)) as long?);

    public void SetRemoved(string key, long when) => db.Run(sql => sql.Execute(
        "INSERT INTO cards_removed (key, removed) VALUES ($k, $t) ON CONFLICT (key) DO UPDATE SET removed = $t", ("$k", key), ("$t", when)));

    public void ForgetRemoved(string key) => db.Run(sql => sql.Execute("DELETE FROM cards_removed WHERE key = $k", ("$k", key)));

    public void PruneRemoved(long before) => db.Run(sql => sql.Execute("DELETE FROM cards_removed WHERE removed <= $t", ("$t", before)));

    public IReadOnlyDictionary<string, long> AllRemoved() => db.Run(sql => sql
        .Query("SELECT key, removed FROM cards_removed", r => (Key: r.GetString(0), At: r.GetInt64(1)))
        .ToDictionary(x => x.Key, x => x.At, StringComparer.Ordinal));

    public T Batch<T>(Func<T> work) => db.Transaction(work);

    private static Card Read(SqliteDataReader r) => new()
    {
        Word = r.GetString(0),
        Meaning = r.GetString(1),
        Example = r.GetString(2),
        Form = r.GetString(3),
        Definition = r.GetString(4),
        EnglishFirst = r.GetInt64(5) != 0,
        Added = r.GetInt64(6),
        Due = r.GetInt64(7),
        Interval = r.GetInt32(8),
        Ease = r.GetDouble(9),
        Reps = r.GetInt32(10),
        Lapses = r.GetInt32(11),
        Last = r.IsDBNull(12) ? null : r.GetInt64(12),
        Modified = r.GetInt64(13),
    };
}

/// <summary>The mistake journal in the <c>mistakes</c> and <c>mistake_counts</c> tables, with its totals in <c>state</c>.</summary>
public sealed class SqliteMistakeStore(LamhaDatabase db) : IMistakeStore
{
    private const string ChecksKey = "mistakes.checks";
    private const string SinceKey = "mistakes.since";

    public void Record(IReadOnlyList<MistakeEntry> mistakes, long now, int keepRecent) => db.Transaction(() => db.Run(sql =>
    {
        if (StateLong(sql, SinceKey) is null)
        {
            SetState(sql, SinceKey, now);
        }

        SetState(sql, ChecksKey, (StateLong(sql, ChecksKey) ?? 0) + 1);
        foreach (var m in mistakes)
        {
            sql.Execute(
                "INSERT INTO mistake_counts (category, count) VALUES ($c, 1) ON CONFLICT (category) DO UPDATE SET count = count + 1",
                ("$c", m.Category));
        }

        // Read newest first (the highest id): the text's last mistake goes in first, so its first one ends on top.
        for (var i = mistakes.Count - 1; i >= 0; i--)
        {
            Insert(sql, mistakes[i]);
        }

        Trim(sql, keepRecent);
    }));

    public MistakeJournal Read() => db.Run(sql =>
    {
        var counts = sql.Query("SELECT category, count FROM mistake_counts", r => (Category: r.GetString(0), Count: r.GetInt32(1)))
            .ToDictionary(x => x.Category, x => x.Count, StringComparer.Ordinal);
        var recent = sql.Query(
            "SELECT category, original, fix, why, time FROM mistakes ORDER BY id DESC",
            r => new MistakeEntry(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt64(4)));
        return new MistakeJournal((int)(StateLong(sql, ChecksKey) ?? 0), counts, recent, StateLong(sql, SinceKey) ?? 0);
    });

    public void Replace(MistakeJournal journal) => db.Transaction(() => db.Run(sql =>
    {
        sql.Execute("DELETE FROM mistake_counts");
        sql.Execute("DELETE FROM mistakes");
        foreach (var (category, count) in journal.Counts)
        {
            sql.Execute("INSERT INTO mistake_counts (category, count) VALUES ($c, $n)", ("$c", category), ("$n", count));
        }

        for (var i = journal.Recent.Count - 1; i >= 0; i--)
        {
            Insert(sql, journal.Recent[i]);
        }

        SetState(sql, ChecksKey, journal.Checks);
        SetState(sql, SinceKey, journal.Since);
    }));

    private static void Insert(Sql sql, MistakeEntry m) => sql.Execute(
        "INSERT INTO mistakes (category, original, fix, why, time) VALUES ($c, $o, $f, $w, $t)",
        ("$c", m.Category), ("$o", m.Original), ("$f", m.Fix), ("$w", m.Why), ("$t", m.Time));

    private static void Trim(Sql sql, int keep) => sql.Execute(
        "DELETE FROM mistakes WHERE id NOT IN (SELECT id FROM mistakes ORDER BY id DESC LIMIT $n)", ("$n", keep));

    private static long? StateLong(Sql sql, string key) =>
        sql.Scalar("SELECT value FROM state WHERE key = $k", ("$k", key)) is string s && long.TryParse(s, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static void SetState(Sql sql, string key, long value) => sql.Execute(
        "INSERT INTO state (key, value) VALUES ($k, $v) ON CONFLICT (key) DO UPDATE SET value = $v",
        ("$k", key), ("$v", value.ToString(CultureInfo.InvariantCulture)));
}

/// <summary>Each day's practice in the <c>activity</c> table, by the local calendar day ("2026-04-24").</summary>
public sealed class SqliteActivityStore(LamhaDatabase db) : IActivityStore
{
    public int Add(DateOnly day, DateOnly oldestKept) => db.Transaction(() => db.Run(sql =>
    {
        sql.Execute("DELETE FROM activity WHERE day < $oldest", ("$oldest", Key(oldestKept)));
        sql.Execute("INSERT INTO activity (day, count) VALUES ($d, 1) ON CONFLICT (day) DO UPDATE SET count = count + 1", ("$d", Key(day)));
        return (int)sql.Long("SELECT count FROM activity WHERE day = $d", ("$d", Key(day)));
    }));

    public int Get(DateOnly day) => db.Run(sql => (int)sql.Long("SELECT count FROM activity WHERE day = $d", ("$d", Key(day))));

    public IReadOnlyDictionary<DateOnly, int> All() => db.Run(sql => sql
        .Query("SELECT day, count FROM activity", r => (Day: DateOnly.ParseExact(r.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture), Count: r.GetInt32(1)))
        .ToDictionary(x => x.Day, x => x.Count));

    public void RaiseTo(DateOnly day, int count) => db.Run(sql => sql.Execute(
        "INSERT INTO activity (day, count) VALUES ($d, $n) ON CONFLICT (day) DO UPDATE SET count = max(count, $n)",
        ("$d", Key(day)), ("$n", count)));

    private static string Key(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>The latest failures in the <c>errors</c> table.</summary>
public sealed class SqliteErrorLogStore(LamhaDatabase db) : IErrorLogStore
{
    public void Add(ErrorLogEntry entry, int keep) => db.Transaction(() => db.Run(sql =>
    {
        sql.Execute("INSERT INTO errors (time, place, code) VALUES ($t, $p, $c)", ("$t", entry.Time), ("$p", entry.Where), ("$c", entry.Code));
        sql.Execute("DELETE FROM errors WHERE id NOT IN (SELECT id FROM errors ORDER BY id DESC LIMIT $n)", ("$n", keep));
    }));

    public IReadOnlyList<ErrorLogEntry> Recent() => db.Run(sql => sql.Query(
        "SELECT time, place, code FROM errors ORDER BY id DESC", r => new ErrorLogEntry(r.GetInt64(0), r.GetString(1), r.GetString(2))));
}

/// <summary>Clipboard history in the <c>clips</c> table: contents encrypted, sorted by when each copy was last active.</summary>
public sealed class SqliteClipStore(LamhaDatabase db) : IClipStore
{
    private const string Columns = "id, text_key, body, source_app, lang, created, last_copied, last_used, copy_count, use_count, pinned";

    /// <summary>The least recent last; the same moment, the older id last (as the list shows them).</summary>
    private const string ByActivity = "ORDER BY max(last_copied, last_used) DESC, id DESC";

    public ClipRecord? Get(string id) => db.Run(sql => sql.Query($"SELECT {Columns} FROM clips WHERE id = $id", Read, ("$id", id))).FirstOrDefault();

    public ClipRecord? FindByKey(byte[] textKey) =>
        db.Run(sql => sql.Query($"SELECT {Columns} FROM clips WHERE text_key = $k", Read, ("$k", textKey))).FirstOrDefault();

    public IReadOnlyList<ClipRecord> All() => db.Run(sql => sql.Query($"SELECT {Columns} FROM clips {ByActivity}", Read));

    public void Put(ClipRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        db.Run(sql => sql.Execute(
            $"""
            INSERT OR REPLACE INTO clips ({Columns})
            VALUES ($id, $key, $body, $app, $lang, $created, $copied, $used, $copies, $uses, $pinned)
            """,
            ("$id", record.Id), ("$key", record.TextKey), ("$body", record.Body), ("$app", record.SourceApp), ("$lang", record.Lang),
            ("$created", record.CreatedAt), ("$copied", record.LastCopiedAt), ("$used", record.LastUsedAt),
            ("$copies", record.CopyCount), ("$uses", record.UseCount), ("$pinned", record.Pinned ? 1 : 0)));
    }

    public bool Delete(string id) => db.Run(sql => sql.Execute("DELETE FROM clips WHERE id = $id", ("$id", id)) > 0);

    public int CountPinned() => db.Run(sql => (int)sql.Long("SELECT count(*) FROM clips WHERE pinned = 1"));

    public int TrimUnpinned(int keep) => db.Run(sql => sql.Execute(
        $"DELETE FROM clips WHERE pinned = 0 AND id NOT IN (SELECT id FROM clips WHERE pinned = 0 {ByActivity} LIMIT $n)", ("$n", keep)));

    public int ExpireUnpinned(long before) =>
        db.Run(sql => sql.Execute("DELETE FROM clips WHERE pinned = 0 AND max(last_copied, last_used) < $t", ("$t", before)));

    public int DeleteApp(string app) => db.Run(sql => sql.Execute("DELETE FROM clips WHERE source_app = $a", ("$a", app)));

    public int Clear(bool keepPinned) => db.Run(sql =>
    {
        sql.Execute(keepPinned ? "DELETE FROM clips WHERE pinned = 0" : "DELETE FROM clips");
        return (int)sql.Long("SELECT count(*) FROM clips");
    });

    public IReadOnlyList<(string App, int Count)> Apps() => db.Run(sql => sql.Query(
        "SELECT source_app, count(*) AS n FROM clips GROUP BY source_app ORDER BY n DESC, source_app",
        r => (r.GetString(0), r.GetInt32(1))));

    private static ClipRecord Read(SqliteDataReader r) => new(
        r.GetString(0), (byte[])r.GetValue(1), (byte[])r.GetValue(2), r.GetString(3), r.GetString(4),
        r.GetInt64(5), r.GetInt64(6), r.GetInt64(7), r.GetInt32(8), r.GetInt32(9), r.GetInt64(10) != 0);
}
