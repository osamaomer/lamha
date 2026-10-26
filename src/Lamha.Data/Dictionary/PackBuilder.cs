using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Lamha.Data.Dictionary;

/// <summary>
/// Makes a <see cref="PackFile"/> from one of Lamha 2's language packs (tools/build_packs.py on release/2.x): a gzip'd
/// <c>{ "meta": { lang, format: 1, version, words, forms, built, source, license }, "shards": { "&lt;xx&gt;": { "w": {
/// word: entry }, "f": { form: lemma } } } }</c>. Each entry is copied as its JSON (the dictionary's shape without
/// the Arabic). The words are the same; only the way they're kept changes.
/// </summary>
public static partial class PackBuilder
{
    /// <summary>The format of Lamha 2's packs (build_packs.py <c>FORMAT</c>).</summary>
    public const int Lamha2Format = 1;

    private static readonly string[] MetaKeys = ["version", "words", "forms", "built", "source", "license"];

    /// <summary>Builds the pack file at <paramref name="outputPath"/> from Lamha 2's <paramref name="jsonGz"/>; returns its language.</summary>
    public static string Build(string jsonGz, string outputPath)
    {
        using var doc = Read(jsonGz);
        var meta = doc.RootElement.GetProperty("meta");
        var lang = meta.GetProperty("lang").GetString() ?? "";
        if (!Language().IsMatch(lang) || meta.GetProperty("format").GetInt32() != Lamha2Format)
        {
            throw new InvalidDataException("Not one of Lamha 2's language packs");
        }

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
                CREATE TABLE words (word TEXT PRIMARY KEY, entry TEXT NOT NULL) WITHOUT ROWID;
                CREATE TABLE forms (form TEXT PRIMARY KEY, lemma TEXT NOT NULL) WITHOUT ROWID;
                """);
            using (var transaction = connection.BeginTransaction())
            {
                using (var row = Command(connection, transaction, "INSERT INTO meta (key, value) VALUES ($a, $b)"))
                {
                    Run(row, "format", PackFile.Format.ToString(CultureInfo.InvariantCulture));
                    Run(row, "lang", lang);
                    foreach (var key in MetaKeys)
                    {
                        if (meta.TryGetProperty(key, out var value))
                        {
                            Run(row, key, value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText());
                        }
                    }
                }

                using var words = Command(connection, transaction, "INSERT OR REPLACE INTO words (word, entry) VALUES ($a, $b)");
                using var forms = Command(connection, transaction, "INSERT OR REPLACE INTO forms (form, lemma) VALUES ($a, $b)");
                foreach (var shard in doc.RootElement.GetProperty("shards").EnumerateObject().OrderBy(s => s.Name, StringComparer.Ordinal))
                {
                    // packs.js's check: only shard keys, only objects.
                    if (!Shard().IsMatch(shard.Name) || shard.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (shard.Value.TryGetProperty("w", out var w) && w.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var word in w.EnumerateObject().Where(x => x.Value.ValueKind == JsonValueKind.Object))
                        {
                            Run(words, word.Name, word.Value.GetRawText());
                        }
                    }

                    if (shard.Value.TryGetProperty("f", out var f) && f.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var form in f.EnumerateObject().Where(x => x.Value.ValueKind == JsonValueKind.String))
                        {
                            Run(forms, form.Name, form.Value.GetString()!);
                        }
                    }
                }

                transaction.Commit();
            }

            Execute(connection, "ANALYZE; VACUUM;");
        }

        SqliteConnection.ClearAllPools();
        File.Move(temp, outputPath, overwrite: true);
        return lang;
    }

    /// <summary>A file gzip'd for the release, as small as gzip makes it.</summary>
    public static void Compress(string path, string gzPath)
    {
        using var input = File.OpenRead(path);
        using var output = File.Create(gzPath);
        using var gzip = new GZipStream(output, CompressionLevel.SmallestSize);
        input.CopyTo(gzip);
    }

    private static JsonDocument Read(string jsonGz)
    {
        using var file = File.OpenRead(jsonGz);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    [GeneratedRegex("^[a-z]{2,3}$")]
    private static partial Regex Language();

    [GeneratedRegex("^[a-z_]{2}$")]
    private static partial Regex Shard();

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        command.Parameters.Add(new SqliteParameter { ParameterName = "$a" });
        command.Parameters.Add(new SqliteParameter { ParameterName = "$b" });
        return command;
    }

    private static void Run(SqliteCommand command, string a, string b)
    {
        command.Parameters[0].Value = a;
        command.Parameters[1].Value = b;
        command.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
