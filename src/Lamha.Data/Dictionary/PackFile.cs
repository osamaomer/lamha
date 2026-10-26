using System.Globalization;
using Lamha.Core.Abstractions;
using Lamha.Core.Dictionary;
using Microsoft.Data.Sqlite;

namespace Lamha.Data.Dictionary;

/// <summary>
/// A downloaded language pack: one read-only SQLite file, as the offline dictionary is (decision 0006), made from
/// Lamha 2's pack by <see cref="PackBuilder"/>. Words are read from disk when they're looked up.
/// </summary>
public sealed class PackFile : IPackData
{
    /// <summary>The pack's format: Lamha 2's JSON packs were 1, these are 2. A file in another format isn't read.</summary>
    public const int Format = 2;

    private readonly SqliteConnection _connection;
    private readonly Lock _gate = new();

    /// <param name="path">The file.</param>
    /// <param name="lang">The language it must be for, or null for any.</param>
    /// <param name="check">Read every page first (a download): a damaged file is refused now, not at a lookup.</param>
    public PackFile(string path, string? lang = null, bool check = false)
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
        _connection = new SqliteConnection(builder.ToString());
        try
        {
            _connection.Open();
            Execute("PRAGMA cache_size = -128");
            if (check && Scalar("PRAGMA quick_check") != "ok")
            {
                throw new InvalidDataException("A damaged language pack");
            }

            if (Scalar("SELECT value FROM meta WHERE key = 'format'") != Format.ToString(CultureInfo.InvariantCulture))
            {
                throw new InvalidDataException("Not a Lamha language pack of format " + Format);
            }

            var found = Scalar("SELECT value FROM meta WHERE key = 'lang'") ?? "";
            if (found.Length == 0 || lang is not null && found != lang)
            {
                throw new InvalidDataException("Not the language pack asked for");
            }

            _ = int.TryParse(Scalar("SELECT value FROM meta WHERE key = 'words'"), NumberStyles.None, CultureInfo.InvariantCulture, out var words);
            Meta = new PackMeta(found, Scalar("SELECT value FROM meta WHERE key = 'version'") ?? "", words);
        }
        catch (SqliteException e)
        {
            _connection.Dispose();
            throw new InvalidDataException("Not a Lamha language pack", e);
        }
        catch (InvalidDataException)
        {
            _connection.Dispose();
            throw;
        }
    }

    public PackMeta Meta { get; }

    public DictionaryEntry? Entry(string word) =>
        Scalar("SELECT entry FROM words WHERE word = $w", ("$w", word)) is { } json ? EntryJson.Parse(json) : null;

    public string? Lemma(string form) => Scalar("SELECT lemma FROM forms WHERE form = $f", ("$f", form));

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
