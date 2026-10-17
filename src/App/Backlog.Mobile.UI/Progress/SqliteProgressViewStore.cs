using System.Globalization;

using Microsoft.Data.Sqlite;

namespace Backlog.Mobile.UI.Progress;

/// <summary>
/// The phone's <c>progress_view</c>: the fold of the task feed's goal, impediment and
/// stand-up context documents, and the cursor the next Progress pull resumes from. A
/// projection, as <see cref="Tasks.ITaskViewStore"/> is, with its own cursor so Progress and
/// the tasks never have to move together.
/// </summary>
public interface IProgressViewStore
{
    /// <summary>Every row, tombstones included. Synchronous, for the reason the task view's
    /// read is: a first render cannot await.</summary>
    IReadOnlyList<ProgressViewRow> ReadRows();

    /// <summary>Where the next pull starts, or null for the beginning.</summary>
    string? ReadCursor();

    /// <summary>Writes <paramref name="rows"/> over whatever is kept for their ids and, when
    /// <paramref name="cursor"/> is given, moves the cursor, in one transaction, so a page is
    /// never half kept.</summary>
    Task SaveAsync(IReadOnlyList<ProgressViewRow> rows, string? cursor = null, CancellationToken cancellationToken = default);

    /// <summary>Forgets the cursor, so the next pull starts from the beginning. The rows
    /// stay: a full pull folds over them to the same result.</summary>
    Task ResetCursorAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IProgressViewStore"/> in the device's SQLite file, beside the task and note
/// views: its own tables, the same file. Each document is kept verbatim, so a field this
/// build does not know is carried rather than dropped. Unpooled, like the device store: a
/// connection per call.
/// </summary>
public sealed class SqliteProgressViewStore : IProgressViewStore
{
    private readonly string _connectionString;

    public SqliteProgressViewStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString();

        using var connection = Open();
        using var command = connection.CreateCommand();

        // Additive only, as every table in this file is: the file outlives every build.
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS progress_view (
                id          TEXT    NOT NULL PRIMARY KEY,
                kind        TEXT    NOT NULL,
                updated_at  TEXT    NOT NULL,
                deleted_at  TEXT    NULL,
                server_ts   INTEGER NOT NULL,
                content     TEXT    NOT NULL
            );
            CREATE TABLE IF NOT EXISTS progress_cursor (
                id     INTEGER NOT NULL PRIMARY KEY CHECK (id = 1),
                since  TEXT    NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<ProgressViewRow> ReadRows()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT id, kind, updated_at, deleted_at, server_ts, content FROM progress_view";

        using var reader = command.ExecuteReader();
        var rows = new List<ProgressViewRow>();

        while (reader.Read())
        {
            rows.Add(new ProgressViewRow(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                ParseInstant(reader.GetString(2)),
                reader.IsDBNull(3) ? null : ParseInstant(reader.GetString(3)),
                reader.GetInt64(4),
                reader.GetString(5)));
        }

        return rows;
    }

    public string? ReadCursor()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT since FROM progress_cursor WHERE id = 1";

        return command.ExecuteScalar() as string;
    }

    public async Task SaveAsync(IReadOnlyList<ProgressViewRow> rows, string? cursor = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);

        await using var connection = Open();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        foreach (var row in rows)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO progress_view (id, kind, updated_at, deleted_at, server_ts, content)
                VALUES ($id, $kind, $updatedAt, $deletedAt, $serverTs, $content)
                ON CONFLICT (id) DO UPDATE SET
                    kind = excluded.kind,
                    updated_at = excluded.updated_at,
                    deleted_at = excluded.deleted_at,
                    server_ts = excluded.server_ts,
                    content = excluded.content
                """;
            command.Parameters.AddWithValue("$id", row.Id.ToString());
            command.Parameters.AddWithValue("$kind", row.Kind);
            command.Parameters.AddWithValue("$updatedAt", FormatInstant(row.UpdatedAt));
            command.Parameters.AddWithValue("$deletedAt", row.DeletedAt is { } deleted ? FormatInstant(deleted) : DBNull.Value);
            command.Parameters.AddWithValue("$serverTs", row.ServerTimestamp);
            command.Parameters.AddWithValue("$content", row.Content);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (cursor is not null)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO progress_cursor (id, since) VALUES (1, $since)
                ON CONFLICT (id) DO UPDATE SET since = excluded.since
                """;
            command.Parameters.AddWithValue("$since", cursor);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ResetCursorAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = "DELETE FROM progress_cursor";

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>UTC, round-trip: the stamps are compared, so none may lose a tick.</summary>
    private static string FormatInstant(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseInstant(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
