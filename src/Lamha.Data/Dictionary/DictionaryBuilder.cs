using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Lamha.Data.Dictionary;

/// <summary>
/// Builds the dictionary file from the JSON shards tools/build_dict.py writes (<c>dict/</c>): <c>en/&lt;xx&gt;.json</c>
/// (words by their first two letters, most frequent first), <c>forms/&lt;xx&gt;.json</c> (inflected forms) and
/// <c>ar/&lt;code&gt;.json</c> (Arabic → English). Each entry is copied as its JSON.
/// </summary>
public static class DictionaryBuilder
{
    public static void Build(string jsonFolder, string outputPath)
    {
        var temp = outputPath + ".building";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.Delete(temp);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temp, Pooling = false }.ToString()))
        {
            connection.Open();
            Execute(connection, """
                PRAGMA journal_mode = OFF;
                PRAGMA synchronous = OFF;
                CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL) WITHOUT ROWID;
                CREATE TABLE words (word TEXT PRIMARY KEY, shard TEXT NOT NULL, rank INTEGER NOT NULL, entry TEXT NOT NULL) WITHOUT ROWID;
                CREATE TABLE forms (form TEXT PRIMARY KEY, lemmas TEXT NOT NULL) WITHOUT ROWID;
                CREATE TABLE arabic (key TEXT PRIMARY KEY, words TEXT NOT NULL) WITHOUT ROWID;
                """);
            using (var transaction = connection.BeginTransaction())
            {
                Insert(connection, transaction, "INSERT INTO meta (key, value) VALUES ($a, $b)", [("format", DictionaryFile.Format.ToString(CultureInfo.InvariantCulture))]);
                var meta = Path.Combine(jsonFolder, "meta.json");
                Insert(connection, transaction, "INSERT INTO meta (key, value) VALUES ($a, $b)", [("meta", File.Exists(meta) ? File.ReadAllText(meta).Trim() : "{}")]);

                using (var words = Command(connection, transaction, "INSERT OR REPLACE INTO words (word, shard, rank, entry) VALUES ($w, $s, $r, $e)", "$w", "$s", "$r", "$e"))
                {
                    foreach (var file in Shards(jsonFolder, "en"))
                    {
                        var shard = Path.GetFileNameWithoutExtension(file);
                        var rank = 0;
                        foreach (var (word, entry) in Properties(file))
                        {
                            Run(words, word, shard, rank++, entry);
                        }
                    }
                }

                using (var forms = Command(connection, transaction, "INSERT OR REPLACE INTO forms (form, lemmas) VALUES ($a, $b)", "$a", "$b"))
                {
                    foreach (var (form, lemmas) in Shards(jsonFolder, "forms").SelectMany(Properties))
                    {
                        Run(forms, form, lemmas);
                    }
                }

                using (var arabic = Command(connection, transaction, "INSERT OR REPLACE INTO arabic (key, words) VALUES ($a, $b)", "$a", "$b"))
                {
                    foreach (var (key, english) in Shards(jsonFolder, "ar").SelectMany(Properties))
                    {
                        Run(arabic, key, english);
                    }
                }

                transaction.Commit();
            }

            Execute(connection, "CREATE INDEX words_by_shard ON words (shard, rank); ANALYZE; VACUUM;");
        }

        SqliteConnection.ClearAllPools();
        File.Move(temp, outputPath, overwrite: true);
    }

    private static IEnumerable<string> Shards(string folder, string kind) =>
        Directory.EnumerateFiles(Path.Combine(folder, kind), "*.json").Order(StringComparer.Ordinal);

    /// <summary>A shard's names and their JSON values, in the file's order (for words: most frequent first).</summary>
    private static IEnumerable<(string Name, string Json)> Properties(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            yield return (property.Name, property.Value.GetRawText());
        }
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction transaction, string sql, params string[] names)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var name in names)
        {
            command.Parameters.Add(new SqliteParameter { ParameterName = name });
        }

        return command;
    }

    private static void Run(SqliteCommand command, params object[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            command.Parameters[i].Value = values[i];
        }

        command.ExecuteNonQuery();
    }

    private static void Insert(SqliteConnection connection, SqliteTransaction transaction, string sql, (string A, string B)[] rows)
    {
        using var command = Command(connection, transaction, sql, "$a", "$b");
        foreach (var (a, b) in rows)
        {
            Run(command, a, b);
        }
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
