using System.Text.Json;

using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Data.Sqlite;

namespace Backlog.Mobile.UI.Notifications;

/// <summary>
/// <see cref="INotificationViewStore"/> in the device's SQLite file, beside the outbox and the
/// other views: its own tables, the same file. Each record is kept whole as JSON, so a field
/// this build does not know is carried rather than dropped. Unpooled, like the device store: a
/// connection per call.
/// </summary>
public sealed class SqliteNotificationViewStore : INotificationViewStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _connectionString;

    public SqliteNotificationViewStore(string databasePath)
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

        // Additive only, as every table in this file is: the file outlives every build of the app.
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS notification_view (
                id      TEXT NOT NULL PRIMARY KEY,
                record  TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS notification_cursor (
                id     INTEGER NOT NULL PRIMARY KEY CHECK (id = 1),
                since  TEXT    NOT NULL
            );
            CREATE TABLE IF NOT EXISTS notification_reply_sent (
                id       TEXT NOT NULL PRIMARY KEY,
                sent_at  TEXT NOT NULL,
                reply    TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<NotificationChangeRecord> ReadRows()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT record FROM notification_view";

        using var reader = command.ExecuteReader();
        var rows = new List<NotificationChangeRecord>();

        while (reader.Read())
        {
            if (Deserialize<NotificationChangeRecord>(reader.GetString(0)) is { } record) rows.Add(record);
        }

        return rows;
    }

    public string? ReadCursor()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT since FROM notification_cursor WHERE id = 1";

        return command.ExecuteScalar() as string;
    }

    public async Task SaveAsync(IReadOnlyList<NotificationChangeRecord> rows, string? cursor = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);

        await using var connection = Open();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        foreach (var row in rows)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO notification_view (id, record) VALUES ($id, $record)
                ON CONFLICT (id) DO UPDATE SET record = excluded.record
                """;
            command.Parameters.AddWithValue("$id", row.Change.Id.ToString());
            command.Parameters.AddWithValue("$record", JsonSerializer.Serialize(row, Json));

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (cursor is not null)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO notification_cursor (id, since) VALUES (1, $since)
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

        command.CommandText = "DELETE FROM notification_cursor";

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public IReadOnlyList<NotificationReplyPost> ReadSentReplies()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT reply FROM notification_reply_sent ORDER BY sent_at, id";

        using var reader = command.ExecuteReader();
        var replies = new List<NotificationReplyPost>();

        while (reader.Read())
        {
            if (Deserialize<NotificationReplyPost>(reader.GetString(0)) is { } reply) replies.Add(reply);
        }

        return replies;
    }

    public async Task AddSentReplyAsync(NotificationReplyPost reply, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reply);

        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO notification_reply_sent (id, sent_at, reply) VALUES ($id, $sentAt, $reply)
            ON CONFLICT (id) DO NOTHING
            """;
        command.Parameters.AddWithValue("$id", reply.Id.ToString());
        command.Parameters.AddWithValue("$sentAt", reply.RepliedAt.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$reply", JsonSerializer.Serialize(reply, Json));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>A row this build cannot read is passed over rather than failing the list.</summary>
    private static T? Deserialize<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
