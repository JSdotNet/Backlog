using System.Globalization;
using System.Text.Json;

using Backlog.Mobile.UI.Storage;
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

    private static readonly SqliteViewMapping<TaskViewRow> Mapping = new(
        row => row.Id,
        [
            new("updated_at", "TEXT    NOT NULL", row => SqliteInstant.Format(row.UpdatedAt)),
            new("deleted_at", "TEXT    NULL", row => SqliteInstant.FormatOrNull(row.DeletedAt)),
            new("server_ts", "INTEGER NOT NULL", row => row.ServerTimestamp),
            new("in_my_day_on", "TEXT    NULL",
                row => row.Task.InMyDayOn is { } day ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null),
            new("status", "TEXT    NOT NULL", row => row.Task.Status),
            new("payload", "TEXT    NOT NULL", row => JsonSerializer.Serialize(row.Task, Json)),
        ],
        // in_my_day_on and status are only what the list is read by; the payload carries both.
        reader => JsonSerializer.Deserialize<TaskPayload>(reader.GetString(reader.GetOrdinal("payload")), Json) is { } payload
            ? new TaskViewRow(
                Guid.Parse(reader.GetString(0)),
                SqliteInstant.Parse(reader.GetString(1)),
                SqliteInstant.ParseOrNull(reader, 2),
                reader.GetInt64(3),
                payload)
            : null);

    private readonly SqliteViewStore<TaskViewRow> _view;

    public SqliteTaskViewStore(string databasePath)
    {
        _view = new SqliteViewStore<TaskViewRow>(databasePath, "task", Mapping);

        using var connection = _view.Open();
        using var transaction = connection.BeginTransaction();

        var hadPreferences = HasTable(connection, transaction, "my_day_preferences");
        var hadRoadmapPlan = HasTable(connection, transaction, "roadmap_plan");

        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        // Additive only, as the device store's tables are: the file outlives
        // every build of the app.
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS my_day_preferences (
                id          INTEGER NOT NULL PRIMARY KEY CHECK (id = 1),
                updated_at  TEXT    NOT NULL,
                server_ts   INTEGER NOT NULL,
                payload     TEXT    NOT NULL
            );
            CREATE TABLE IF NOT EXISTS roadmap_plan (
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
        // The roadmap plan document takes the same replay (local ADR 0062 §8): every
        // build before its table folded it away too.
        if (!hadPreferences || !hadRoadmapPlan)
        {
            using var reset = connection.CreateCommand();
            reset.Transaction = transaction;
            reset.CommandText = "DELETE FROM task_cursor";
            reset.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public IReadOnlyList<TaskViewRow> ReadRows() => _view.ReadRows();

    public string? ReadCursor() => _view.ReadCursor();

    public MyDayPreferencesDocument? ReadMyDayPreferences() =>
        ReadDocument("my_day_preferences", (updatedAt, serverTs, payload) => new MyDayPreferencesDocument(updatedAt, serverTs, payload));

    public RoadmapPlanCopy? ReadRoadmapPlan() =>
        ReadDocument("roadmap_plan", (updatedAt, serverTs, payload) => new RoadmapPlanCopy(updatedAt, serverTs, payload));

    private T? ReadDocument<T>(string table, Func<DateTimeOffset, long, string, T> create)
        where T : class
    {
        using var connection = _view.Open();
        using var command = connection.CreateCommand();

        command.CommandText = $"SELECT updated_at, server_ts, payload FROM {table} WHERE id = 1";

        using var reader = command.ExecuteReader();

        return reader.Read()
            ? create(SqliteInstant.Parse(reader.GetString(0)), reader.GetInt64(1), reader.GetString(2))
            : null;
    }

    public Task SaveAsync(IReadOnlyList<TaskViewRow> rows, string? cursor = null, CancellationToken cancellationToken = default) =>
        SaveAsync(rows, cursor, preferences: null, cancellationToken);

    public Task SaveAsync(IReadOnlyList<TaskViewRow> rows, string? cursor, MyDayPreferencesDocument? preferences, CancellationToken cancellationToken = default) =>
        SaveAsync(rows, cursor, preferences, roadmapPlan: null, cancellationToken);

    public Task SaveAsync(
        IReadOnlyList<TaskViewRow> rows,
        string? cursor,
        MyDayPreferencesDocument? preferences,
        RoadmapPlanCopy? roadmapPlan,
        CancellationToken cancellationToken = default) =>
        _view.SaveAsync(
            rows,
            cursor,
            preferences is null && roadmapPlan is null
                ? null
                : async (connection, transaction, ct) =>
                {
                    if (preferences is not null)
                    {
                        await SaveDocumentAsync(connection, transaction, "my_day_preferences", preferences.UpdatedAt, preferences.ServerTimestamp, preferences.Content, ct);
                    }

                    if (roadmapPlan is not null)
                    {
                        await SaveDocumentAsync(connection, transaction, "roadmap_plan", roadmapPlan.UpdatedAt, roadmapPlan.ServerTimestamp, roadmapPlan.Content, ct);
                    }
                },
            cancellationToken);

    public Task ResetCursorAsync(CancellationToken cancellationToken = default) =>
        _view.ResetCursorAsync(cancellationToken);

    /// <summary>Writes one of the whole documents kept beside the rows over the one kept.</summary>
    private static async Task SaveDocumentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        DateTimeOffset updatedAt,
        long serverTimestamp,
        string content,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {table} (id, updated_at, server_ts, payload)
            VALUES (1, $updatedAt, $serverTs, $payload)
            ON CONFLICT (id) DO UPDATE SET
                updated_at = excluded.updated_at,
                server_ts = excluded.server_ts,
                payload = excluded.payload
            """;
        command.Parameters.AddWithValue("$updatedAt", SqliteInstant.Format(updatedAt));
        command.Parameters.AddWithValue("$serverTs", serverTimestamp);
        command.Parameters.AddWithValue("$payload", content);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static bool HasTable(SqliteConnection connection, SqliteTransaction transaction, string name)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", name);

        return command.ExecuteScalar() is not null;
    }
}
