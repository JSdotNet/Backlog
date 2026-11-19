using Backlog.Mobile.UI.Events;
using Backlog.Mobile.UI.Notes;
using Backlog.Mobile.UI.Notifications;
using Backlog.Mobile.UI.Progress;
using Backlog.Mobile.UI.Tasks;
using Backlog.Tests;

using Microsoft.Data.Sqlite;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The view tables a phone already has on disk: every name, column, type, nullability and key
/// the five view stores create, pinned, so a change to how the stores are written can never
/// change the file a phone keeps. The file is never migrated, so a store that created a
/// different layout would leave an existing phone reading a table it does not match.
/// </summary>
public sealed class SqliteViewStoreSchemaTests : IDisposable
{
    private readonly TemporaryDirectory _folder = new("view-store-schema");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void Each_view_store_creates_the_tables_a_phone_already_keeps()
    {
        var path = _folder.Combine("device.db");

        _ = new SqliteTaskViewStore(path);
        _ = new SqliteProgressViewStore(path);
        _ = new SqliteNotificationViewStore(path);
        _ = new SqliteNoteViewStore(path);
        _ = new SqliteEventViewStore(path);

        Assert.Equal(
            [
                "id TEXT notnull pk",
                "updated_at TEXT notnull",
                "deleted_at TEXT",
                "server_ts INTEGER notnull",
                "in_my_day_on TEXT",
                "status TEXT notnull",
                "payload TEXT notnull",
            ],
            Columns(path, "task_view"));
        Assert.Equal(
            [
                "id INTEGER notnull pk",
                "updated_at TEXT notnull",
                "server_ts INTEGER notnull",
                "payload TEXT notnull",
            ],
            Columns(path, "my_day_preferences"));
        Assert.Equal(
            [
                "id TEXT notnull pk",
                "kind TEXT notnull",
                "updated_at TEXT notnull",
                "deleted_at TEXT",
                "server_ts INTEGER notnull",
                "content TEXT notnull",
            ],
            Columns(path, "progress_view"));
        Assert.Equal(
            [
                "id TEXT notnull pk",
                "updated_at TEXT notnull",
                "deleted_at TEXT",
                "server_ts INTEGER notnull",
                "payload TEXT notnull",
            ],
            Columns(path, "note_view"));
        Assert.Equal(["id TEXT notnull pk", "record TEXT notnull"], Columns(path, "notification_view"));
        Assert.Equal(
            ["id TEXT notnull pk", "sent_at TEXT notnull", "reply TEXT notnull"],
            Columns(path, "notification_reply_sent"));
        Assert.Equal(["id TEXT notnull pk", "record TEXT notnull"], Columns(path, "event_view"));

        foreach (var cursor in new[] { "task_cursor", "progress_cursor", "notification_cursor", "note_cursor", "event_cursor" })
        {
            Assert.Equal(["id INTEGER notnull pk", "since TEXT notnull"], Columns(path, cursor));
            Assert.Contains("CHECK (id = 1)", Definition(path, cursor), StringComparison.Ordinal);
        }
    }

    private static string Definition(string path, string table)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", table);

        return (string)command.ExecuteScalar()!;
    }

    private static string[] Columns(string path, string table)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name, type, \"notnull\", pk FROM pragma_table_info('{table}') ORDER BY cid";

        using var reader = command.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
        {
            var column = $"{reader.GetString(0)} {reader.GetString(1)}";
            if (reader.GetInt64(2) == 1) column += " notnull";
            if (reader.GetInt64(3) > 0) column += " pk";
            columns.Add(column);
        }

        return [.. columns];
    }
}
