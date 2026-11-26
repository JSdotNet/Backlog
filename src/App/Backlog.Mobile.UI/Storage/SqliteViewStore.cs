using Microsoft.Data.Sqlite;

namespace Backlog.Mobile.UI.Storage;

/// <summary>
/// What every view the phone keeps in its SQLite file has in common: a <c>&lt;prefix&gt;_view</c>
/// table of rows keyed by id, a one-row <c>&lt;prefix&gt;_cursor</c> the next pull resumes from,
/// and a save that writes a page of rows and the cursor in one transaction, so a page is never
/// half kept. Each view store holds one and adds what is its own.
/// <para>
/// The tables are created additively and never migrated — the file outlives every build of the
/// app — so the names and columns a mapping declares are a phone's existing layout. Unpooled,
/// like the device store: a connection per call.
/// </para>
/// </summary>
internal sealed class SqliteViewStore<TRow>
{
    private readonly string _connectionString;
    private readonly string _view;
    private readonly string _cursor;
    private readonly SqliteViewMapping<TRow> _mapping;
    private readonly string _select;
    private readonly string _upsert;

    /// <param name="renamedFrom">The prefix the view was kept under before a rename (local
    /// ADR 0073 §10), or null. A file that still has those tables, and not yet the new ones,
    /// has them renamed, so its rows and its cursor carry over as they were.</param>
    public SqliteViewStore(string databasePath, string prefix, SqliteViewMapping<TRow> mapping, string? renamedFrom = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentNullException.ThrowIfNull(mapping);

        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString();

        _view = $"{prefix}_view";
        _cursor = $"{prefix}_cursor";
        _mapping = mapping;

        var names = mapping.Columns.Select(column => column.Name).ToArray();
        _select = $"SELECT id, {string.Join(", ", names)} FROM {_view}";
        _upsert = $"""
            INSERT INTO {_view} (id, {string.Join(", ", names)})
            VALUES ($id, {string.Join(", ", names.Select(name => "$" + name))})
            ON CONFLICT (id) DO UPDATE SET
                {string.Join(",\n    ", names.Select(name => $"{name} = excluded.{name}"))}
            {(mapping.KeepLater is null ? "" : "WHERE " + mapping.KeepLater(_view))}
            """;

        using var connection = Open();

        if (renamedFrom is not null)
        {
            RenameFrom(connection, $"{renamedFrom}_view", _view);
            RenameFrom(connection, $"{renamedFrom}_cursor", _cursor);
        }

        using var command = connection.CreateCommand();

        command.CommandText = $"""
            CREATE TABLE IF NOT EXISTS {_view} (
                id  TEXT NOT NULL PRIMARY KEY,
                {string.Join(",\n    ", mapping.Columns.Select(column => $"{column.Name}  {column.Declaration}"))}
            );
            CREATE TABLE IF NOT EXISTS {_cursor} (
                id     INTEGER NOT NULL PRIMARY KEY CHECK (id = 1),
                since  TEXT    NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>Renames <paramref name="from"/>, a table a build before the rename kept, to
    /// <paramref name="to"/> when the file has the one and not yet the other. Once renamed
    /// nothing is left under the old name, so a cursor reset later is never brought back
    /// from it.</summary>
    private static void RenameFrom(SqliteConnection connection, string from, string to)
    {
        if (!TableExists(connection, from) || TableExists(connection, to)) return;

        using var rename = connection.CreateCommand();
        rename.CommandText = $"ALTER TABLE {from} RENAME TO {to}";
        rename.ExecuteNonQuery();
    }

    private static bool TableExists(SqliteConnection connection, string table)
    {
        using var probe = connection.CreateCommand();
        probe.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name";
        probe.Parameters.AddWithValue("$name", table);
        return probe.ExecuteScalar() is not null;
    }

    /// <summary>Every row the mapping can read, tombstones included.</summary>
    public IReadOnlyList<TRow> ReadRows()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = _select;

        using var reader = command.ExecuteReader();
        var rows = new List<TRow>();

        while (reader.Read())
        {
            if (_mapping.Read(reader) is { } row) rows.Add(row);
        }

        return rows;
    }

    /// <summary>Where the next pull starts, or null for the beginning.</summary>
    public string? ReadCursor()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = $"SELECT since FROM {_cursor} WHERE id = 1";

        return command.ExecuteScalar() as string;
    }

    /// <summary>Upserts <paramref name="rows"/>, moves the cursor when one is given, and runs
    /// <paramref name="alsoWrite"/> — what the view keeps beside its rows — all in one
    /// transaction.</summary>
    public async Task SaveAsync(
        IReadOnlyList<TRow> rows,
        string? cursor,
        Func<SqliteConnection, SqliteTransaction, CancellationToken, Task>? alsoWrite,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);

        await using var connection = Open();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        foreach (var row in rows)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = _upsert;
            command.Parameters.AddWithValue("$id", _mapping.Id(row).ToString());
            foreach (var column in _mapping.Columns)
            {
                command.Parameters.AddWithValue("$" + column.Name, column.Value(row) ?? DBNull.Value);
            }

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (cursor is not null)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO {_cursor} (id, since) VALUES (1, $since)
                ON CONFLICT (id) DO UPDATE SET since = excluded.since
                """;
            command.Parameters.AddWithValue("$since", cursor);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (alsoWrite is not null) await alsoWrite(connection, transaction, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Forgets the cursor, so the next pull starts from the beginning. The rows stay:
    /// a full pull folds over them to the same result.</summary>
    public async Task ResetCursorAsync(CancellationToken cancellationToken)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = $"DELETE FROM {_cursor}";

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>A connection to the same file, for the tables a view keeps beside its own.</summary>
    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
