using Lamha.Core.Dictionary;
using Microsoft.Data.Sqlite;

namespace Lamha.Data.Dictionary;

/// <summary>
/// The offline dictionary: one read-only SQLite file (decision 0006), built from Lamha 2's JSON shards by
/// <see cref="DictionaryBuilder"/>. Words are read from disk when they're looked up; nothing stays in memory.
/// </summary>
public sealed class DictionaryFile : IDictionaryData, IDisposable
{
    /// <summary>The file's format; the builder writes it, and a file in another format isn't read.</summary>
    public const int Format = 1;

    private readonly SqliteConnection _connection;
    private readonly Lock _gate = new();

    public DictionaryFile(string path)
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
        _connection = new SqliteConnection(builder.ToString());
        _connection.Open();
        try
        {
            Execute("PRAGMA cache_size = -256");
            if (Scalar("SELECT value FROM meta WHERE key = 'format'") != Format.ToString(System.Globalization.CultureInfo.InvariantCulture))
            {
                throw new InvalidDataException("Not a Lamha dictionary file of format " + Format);
            }
        }
        catch (SqliteException e)
        {
            _connection.Dispose();
            throw new InvalidDataException("Not a Lamha dictionary file", e);
        }
        catch (InvalidDataException)
        {
            _connection.Dispose();
            throw;
        }
    }

    public DictionaryEntry? Entry(string word) =>
        Scalar("SELECT entry FROM words WHERE word = $w", ("$w", word)) is { } json ? EntryJson.Parse(json) : null;

    public IReadOnlyList<string> Lemmas(string form) =>
        Scalar("SELECT lemmas FROM forms WHERE form = $f", ("$f", form)) is { } json ? EntryJson.StringList(json) : [];

    public IReadOnlyList<string> English(string arabic) =>
        Scalar("SELECT words FROM arabic WHERE key = $k", ("$k", arabic)) is { } json ? EntryJson.StringList(json) : [];

    public IReadOnlyList<string> Shard(string shard)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT word FROM words WHERE shard = $s ORDER BY rank";
            command.Parameters.AddWithValue("$s", shard);
            using var reader = command.ExecuteReader();
            var words = new List<string>();
            while (reader.Read())
            {
                words.Add(reader.GetString(0));
            }

            return words;
        }
    }

    /// <summary>What the dictionary holds (entries, sources), as tools/build_dict.py described it.</summary>
    public string Meta => Scalar("SELECT value FROM meta WHERE key = 'meta'") ?? "{}";

    public void Dispose()
    {
        lock (_gate)
        {
            _connection.Dispose();
        }
    }

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private string? Scalar(string sql, params ReadOnlySpan<(string Name, string Value)> parameters)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return command.ExecuteScalar() as string;
        }
    }
}
