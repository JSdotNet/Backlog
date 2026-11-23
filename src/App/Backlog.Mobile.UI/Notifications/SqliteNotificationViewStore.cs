using System.Text.Json;

using Backlog.Mobile.UI.Storage;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

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

    private static readonly SqliteViewMapping<NotificationChangeRecord> Mapping = new(
        row => row.Change.Id,
        [new("record", "TEXT NOT NULL", row => JsonSerializer.Serialize(row, Json))],
        reader => Deserialize<NotificationChangeRecord>(reader.GetString(1)));

    private readonly SqliteViewStore<NotificationChangeRecord> _view;

    public SqliteNotificationViewStore(string databasePath)
    {
        _view = new SqliteViewStore<NotificationChangeRecord>(databasePath, "notification", Mapping);

        using var connection = _view.Open();
        using var command = connection.CreateCommand();

        // Additive only, as every table in this file is: the file outlives every build of the app.
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS notification_reply_sent (
                id       TEXT NOT NULL PRIMARY KEY,
                sent_at  TEXT NOT NULL,
                reply    TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<NotificationChangeRecord> ReadRows() => _view.ReadRows();

    public string? ReadCursor() => _view.ReadCursor();

    public Task SaveAsync(IReadOnlyList<NotificationChangeRecord> rows, string? cursor = null, CancellationToken cancellationToken = default) =>
        _view.SaveAsync(rows, cursor, alsoWrite: null, cancellationToken);

    public Task ResetCursorAsync(CancellationToken cancellationToken = default) =>
        _view.ResetCursorAsync(cancellationToken);

    public IReadOnlyList<NotificationReplyPost> ReadSentReplies()
    {
        using var connection = _view.Open();
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

        await using var connection = _view.Open();
        await using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO notification_reply_sent (id, sent_at, reply) VALUES ($id, $sentAt, $reply)
            ON CONFLICT (id) DO NOTHING
            """;
        command.Parameters.AddWithValue("$id", reply.Id.ToString());
        command.Parameters.AddWithValue("$sentAt", SqliteInstant.Format(reply.RepliedAt));
        command.Parameters.AddWithValue("$reply", JsonSerializer.Serialize(reply, Json));

        await command.ExecuteNonQueryAsync(cancellationToken);
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
