using System.Globalization;
using System.Text.Json;

using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Data.Sqlite;

namespace Backlog.Mobile.UI.Tasks;

/// <summary>
/// <see cref="ITaskViewStore"/> in the device's SQLite file, beside the outbox and
/// the cached inbox. Its own tables and its own class, because a projection of the
/// task feed is not what the outbox is — but the same file, because the phone keeps
/// one.
/// <para>
/// The document is kept whole, as JSON, so a field this build does not know is
/// carried rather than dropped; the columns beside it are only what the list is
/// read by. Unpooled, like the device store: a connection per call.
/// </para>
/// </summary>
public sealed class SqliteTaskViewStore : ITaskViewStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _connectionString;

    public SqliteTaskViewStore(string databasePath)
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
        using var transaction = connection.BeginTransaction();

        var hadPreferences = HasTable(connection, transaction, "my_day_preferences");

        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        // Additive only, as the device store's tables are: the file outlives
        // every build of the app.
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS task_view (
                id            TEXT    NOT NULL PRIMARY KEY,
                updated_at    TEXT    NOT NULL,
                deleted_at    TEXT    NULL,
                server_ts     INTEGER NOT NULL,
                in_my_day_on  TEXT    NULL,
                status        TEXT    NOT NULL,
                payload       TEXT    NOT NULL
            );
            CREATE TABLE IF NOT EXISTS task_cursor (
                id     INTEGER NOT NULL PRIMARY KEY CHECK (id = 1),
                since  TEXT    NOT NULL
            );
            CREATE TABLE IF NOT EXISTS my_day_preferences (
                id          INTEGER NOT NULL PRIMARY KEY CHECK (id = 1),
                updated_at  TEXT    NOT NULL,
                server_ts   INTEGER NOT NULL,
                payload     TEXT    NOT NULL
            );
            """;
        command.ExecuteNonQuery();

        // The one-time replay (.devbook/arc42/adr/0032, Verification 10): a build
        // before the preferences table folded their document away and moved its
        // cursor past it, so the first build that keeps it pulls the feed again from
        // the start. The rows stay — a full pull folds over them to the same result.
        if (!hadPreferences)
        {
            using var reset = connection.CreateCommand();
            reset.Transaction = transaction;
            reset.CommandText = "DELETE FROM task_cursor";
            reset.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public IReadOnlyList<TaskViewRow> ReadRows()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT id, updated_at, deleted_at, server_ts, payload FROM task_view";

        using var reader = command.ExecuteReader();
        var rows = new List<TaskViewRow>();

        while (reader.Read())
        {
            var payload = JsonSerializer.Deserialize<TaskPayload>(reader.GetString(4), Json);
            if (payload is null) continue;

            rows.Add(new TaskViewRow(
                Guid.Parse(reader.GetString(0)),
                ParseInstant(reader.GetString(1)),
                reader.IsDBNull(2) ? null : ParseInstant(reader.GetString(2)),
                reader.GetInt64(3),
                payload));
        }

        return rows;
    }

    public string? ReadCursor()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT since FROM task_cursor WHERE id = 1";

        return command.ExecuteScalar() as string;
    }

    public MyDayPreferencesDocument? ReadMyDayPreferences()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT updated_at, server_ts, payload FROM my_day_preferences WHERE id = 1";

        using var reader = command.ExecuteReader();

        return reader.Read()
            ? new MyDayPreferencesDocument(ParseInstant(reader.GetString(0)), reader.GetInt64(1), reader.GetString(2))
            : null;
    }

    public Task SaveAsync(IReadOnlyList<TaskViewRow> rows, string? cursor = null, CancellationToken cancellationToken = default) =>
        SaveAsync(rows, cursor, preferences: null, cancellationToken);

    public async Task SaveAsync(IReadOnlyList<TaskViewRow> rows, string? cursor, MyDayPreferencesDocument? preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);

        await using var connection = Open();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        foreach (var row in rows)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO task_view (id, updated_at, deleted_at, server_ts, in_my_day_on, status, payload)
                VALUES ($id, $updatedAt, $deletedAt, $serverTs, $inMyDayOn, $status, $payload)
                ON CONFLICT (id) DO UPDATE SET
                    updated_at = excluded.updated_at,
                    deleted_at = excluded.deleted_at,
                    server_ts = excluded.server_ts,
                    in_my_day_on = excluded.in_my_day_on,
                    status = excluded.status,
                    payload = excluded.payload
                """;
            command.Parameters.AddWithValue("$id", row.Id.ToString());
            command.Parameters.AddWithValue("$updatedAt", FormatInstant(row.UpdatedAt));
            command.Parameters.AddWithValue("$deletedAt", row.DeletedAt is { } deleted ? FormatInstant(deleted) : DBNull.Value);
            command.Parameters.AddWithValue("$serverTs", row.ServerTimestamp);
            command.Parameters.AddWithValue("$inMyDayOn",
                row.Task.InMyDayOn is { } day ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value);
            command.Parameters.AddWithValue("$status", row.Task.Status);
            command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(row.Task, Json));

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (cursor is not null)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO task_cursor (id, since) VALUES (1, $since)
                ON CONFLICT (id) DO UPDATE SET since = excluded.since
                """;
            command.Parameters.AddWithValue("$since", cursor);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (preferences is not null)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO my_day_preferences (id, updated_at, server_ts, payload)
                VALUES (1, $updatedAt, $serverTs, $payload)
                ON CONFLICT (id) DO UPDATE SET
                    updated_at = excluded.updated_at,
                    server_ts = excluded.server_ts,
                    payload = excluded.payload
                """;
            command.Parameters.AddWithValue("$updatedAt", FormatInstant(preferences.UpdatedAt));
            command.Parameters.AddWithValue("$serverTs", preferences.ServerTimestamp);
            command.Parameters.AddWithValue("$payload", preferences.Content);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ResetCursorAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = "DELETE FROM task_cursor";

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Open();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText = "DELETE FROM task_view; DELETE FROM task_cursor; DELETE FROM my_day_preferences;";

        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static bool HasTable(SqliteConnection connection, SqliteTransaction transaction, string name)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", name);

        return command.ExecuteScalar() is not null;
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
