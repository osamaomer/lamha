using Microsoft.Data.Sqlite;

namespace Lamha.Data.Database;

/// <summary>
/// The user's database: <c>%LOCALAPPDATA%\Lamha\lamha.db</c> in the app, or a private in-memory one in tests. One
/// connection serves every store, one statement at a time: a statement here takes well under a millisecond, and a
/// single connection keeps transactions simple and SQLite's page cache small.
/// </summary>
public sealed class LamhaDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Lock _gate = new();
    private SqliteTransaction? _transaction;

    private LamhaDatabase(SqliteConnection connection, bool file)
    {
        _connection = connection;
        _connection.Open();
        if (file)
        {
            // Writes go to a log first: a crash or power cut mid-write never leaves a half-written database.
            Run(sql => sql.Execute("PRAGMA journal_mode = WAL"));
            Run(sql => sql.Execute("PRAGMA synchronous = NORMAL"));
        }

        // Small enough for the memory budget (decision 0008): SQLite's default cache grows to 2 MB.
        Run(sql => sql.Execute("PRAGMA cache_size = -512"));
        Migrations.Apply(this);
    }

    /// <summary>Opens (or creates) the database file at <paramref name="path"/>, bringing its schema up to date.</summary>
    public static LamhaDatabase Open(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var builder = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false };
        return new LamhaDatabase(new SqliteConnection(builder.ToString()), file: true);
    }

    /// <summary>A new, empty database that lives in memory until it's disposed (for tests).</summary>
    public static LamhaDatabase InMemory() => new(new SqliteConnection("Data Source=:memory:"), file: false);

    /// <summary>The schema version (SQLite's <c>user_version</c>).</summary>
    public int Version => Run(sql => (int)sql.Long("PRAGMA user_version"));

    /// <summary>Runs <paramref name="work"/> on the connection, alone.</summary>
    internal T Run<T>(Func<Sql, T> work)
    {
        lock (_gate)
        {
            return work(new Sql(_connection, _transaction));
        }
    }

    internal void Run(Action<Sql> work)
    {
        lock (_gate)
        {
            work(new Sql(_connection, _transaction));
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> in a transaction: all of it is written, or nothing if it throws. A transaction inside
    /// another one joins it.
    /// </summary>
    internal T Transaction<T>(Func<T> work)
    {
        lock (_gate)
        {
            if (_transaction is not null)
            {
                return work();
            }

            _transaction = _connection.BeginTransaction();
            try
            {
                var result = work();
                _transaction.Commit();
                return result;
            }
            finally
            {
                _transaction.Dispose();
                _transaction = null;
            }
        }
    }

    internal void Transaction(Action work) => Transaction(() =>
    {
        work();
        return true;
    });

    public void Dispose()
    {
        lock (_gate)
        {
            _connection.Dispose();
        }
    }
}

/// <summary>Statements on the database's connection, inside its open transaction when there is one.</summary>
internal readonly struct Sql(SqliteConnection connection, SqliteTransaction? transaction)
{
    public int Execute(string sql, params ReadOnlySpan<(string Name, object? Value)> parameters)
    {
        using var command = Command(sql, parameters);
        return command.ExecuteNonQuery();
    }

    /// <summary>The first column of the first row, or null.</summary>
    public object? Scalar(string sql, params ReadOnlySpan<(string Name, object? Value)> parameters)
    {
        using var command = Command(sql, parameters);
        var value = command.ExecuteScalar();
        return value is DBNull ? null : value;
    }

    public long Long(string sql, params ReadOnlySpan<(string Name, object? Value)> parameters) =>
        Scalar(sql, parameters) is long n ? n : 0;

    /// <summary>Every row, each read by <paramref name="read"/>.</summary>
    public List<T> Query<T>(string sql, Func<SqliteDataReader, T> read, params ReadOnlySpan<(string Name, object? Value)> parameters)
    {
        using var command = Command(sql, parameters);
        using var reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read())
        {
            rows.Add(read(reader));
        }

        return rows;
    }

    private SqliteCommand Command(string sql, ReadOnlySpan<(string Name, object? Value)> parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }
}
