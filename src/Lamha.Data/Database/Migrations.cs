namespace Lamha.Data.Database;

/// <summary>
/// The database's schema, one step per version. A new version adds a step at the end; a released step is never
/// changed, because users' databases have already run it. The version is SQLite's <c>user_version</c>.
/// </summary>
internal static class Migrations
{
    private static readonly string[] Steps =
    [
        // 1: Lamha 3's first schema. Times are milliseconds since 1970 (UTC), as in Lamha 2.
        """
        CREATE TABLE state (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        ) WITHOUT ROWID;

        CREATE TABLE history (
            id INTEGER PRIMARY KEY,
            key TEXT NOT NULL UNIQUE,
            word TEXT NOT NULL,
            meaning TEXT NOT NULL,
            language TEXT NOT NULL,
            time INTEGER NOT NULL
        );
        CREATE INDEX history_by_time ON history (time DESC, id DESC);

        CREATE TABLE cards (
            key TEXT PRIMARY KEY,
            word TEXT NOT NULL,
            meaning TEXT NOT NULL,
            example TEXT NOT NULL,
            form TEXT NOT NULL,
            definition TEXT NOT NULL,
            english_first INTEGER NOT NULL,
            added INTEGER NOT NULL,
            due INTEGER NOT NULL,
            interval INTEGER NOT NULL,
            ease REAL NOT NULL,
            reps INTEGER NOT NULL,
            lapses INTEGER NOT NULL,
            last INTEGER,
            modified INTEGER NOT NULL
        ) WITHOUT ROWID;
        CREATE INDEX cards_reviewed_by_due ON cards (due) WHERE last IS NOT NULL;
        CREATE INDEX cards_new_by_added ON cards (added) WHERE last IS NULL;

        CREATE TABLE cards_removed (
            key TEXT PRIMARY KEY,
            removed INTEGER NOT NULL
        ) WITHOUT ROWID;

        CREATE TABLE activity (
            day TEXT PRIMARY KEY,
            count INTEGER NOT NULL
        ) WITHOUT ROWID;

        CREATE TABLE mistakes (
            id INTEGER PRIMARY KEY,
            category TEXT NOT NULL,
            original TEXT NOT NULL,
            fix TEXT NOT NULL,
            why TEXT NOT NULL,
            time INTEGER NOT NULL
        );

        CREATE TABLE mistake_counts (
            category TEXT PRIMARY KEY,
            count INTEGER NOT NULL
        ) WITHOUT ROWID;

        CREATE TABLE errors (
            id INTEGER PRIMARY KEY,
            time INTEGER NOT NULL,
            place TEXT NOT NULL,
            code TEXT NOT NULL
        );
        """,
    ];

    /// <summary>The schema version this build of Lamha writes.</summary>
    public static int Latest => Steps.Length;

    public static void Apply(LamhaDatabase db)
    {
        var version = db.Version;
        if (version > Latest)
        {
            // Written by a newer Lamha: reading it could lose what that version added, so refuse rather than guess.
            throw new InvalidOperationException($"The database is version {version}; this Lamha knows up to {Latest}.");
        }

        for (var v = version; v < Latest; v++)
        {
            var step = Steps[v];
            var next = v + 1;
            db.Transaction(() => db.Run(sql =>
            {
                sql.Execute(step);
                sql.Execute($"PRAGMA user_version = {next}");
            }));
        }
    }
}
