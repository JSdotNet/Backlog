using System.Globalization;
using System.Text.Json;

using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Data.Sqlite;

namespace Backlog.Mobile.UI.Outbox;

/// <summary>
/// <see cref="IDeviceStore"/> in one SQLite file: the MAUI head keeps it in the
/// app's data directory, the browser harness under its own <c>obj/local-development</c>
/// beside the device credential.
/// <para>
/// The reads are synchronous on purpose. The Inbox draws what the phone already
/// has before it asks the service for anything — that is the whole point of the
/// cache — and a first render cannot await. They are a handful of rows from a
/// local file.
/// </para>
/// <para>
/// Unpooled: a connection per call, closed when the call ends, so nothing holds
/// the file open between them. A phone writes a few rows a minute at most.
/// </para>
/// </summary>
public sealed class SqliteDeviceStore : IDeviceStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _connectionString;

    public SqliteDeviceStore(string databasePath)
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

        // Additive only: a column added later is an ALTER beside these, never a
        // change to them, because a phone that updates keeps its file.
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS outbox (
                id          TEXT    NOT NULL PRIMARY KEY,
                kind        TEXT    NOT NULL,
                payload     TEXT    NOT NULL,
                attempts    INTEGER NOT NULL DEFAULT 0,
                last_error  TEXT    NULL,
                created_at  TEXT    NOT NULL
            );
            CREATE TABLE IF NOT EXISTS inbox_cache (
                id          INTEGER NOT NULL PRIMARY KEY CHECK (id = 1),
                items       TEXT    NOT NULL,
                pulled_at   TEXT    NOT NULL
            );
            """;
        command.ExecuteNonQuery();

        AddColumnIfMissing(connection, "outbox", "refused", "INTEGER NOT NULL DEFAULT 0");
    }

    public IReadOnlyList<OutboxEntry> ReadOutbox()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        // rowid breaks a tie between two entries stamped in the same tick, in the
        // order they were written.
        command.CommandText = """
            SELECT id, kind, payload, attempts, last_error, created_at, refused
            FROM outbox
            ORDER BY created_at, rowid
            """;

        using var reader = command.ExecuteReader();
        var entries = new List<OutboxEntry>();

        while (reader.Read())
        {
            entries.Add(new OutboxEntry(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                ParseInstant(reader.GetString(5)),
                reader.GetInt64(6) != 0));
        }

        return entries;
    }

    public async Task AddAsync(OutboxEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO outbox (id, kind, payload, attempts, last_error, created_at, refused)
            VALUES ($id, $kind, $payload, $attempts, $lastError, $createdAt, $refused)
            """;
        command.Parameters.AddWithValue("$refused", entry.Refused ? 1 : 0);
        command.Parameters.AddWithValue("$id", entry.Id.ToString());
        command.Parameters.AddWithValue("$kind", entry.Kind);
        command.Parameters.AddWithValue("$payload", entry.PayloadJson);
        command.Parameters.AddWithValue("$attempts", entry.Attempts);
        command.Parameters.AddWithValue("$lastError", (object?)entry.LastError ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", FormatInstant(entry.CreatedAt));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateAsync(OutboxEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = "UPDATE outbox SET attempts = $attempts, last_error = $lastError, refused = $refused WHERE id = $id";
        command.Parameters.AddWithValue("$refused", entry.Refused ? 1 : 0);
        command.Parameters.AddWithValue("$id", entry.Id.ToString());
        command.Parameters.AddWithValue("$attempts", entry.Attempts);
        command.Parameters.AddWithValue("$lastError", (object?)entry.LastError ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = "DELETE FROM outbox WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public CachedInbox? ReadInbox()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT items, pulled_at FROM inbox_cache WHERE id = 1";

        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;

        var items = JsonSerializer.Deserialize<List<InboxItem>>(reader.GetString(0), Json) ?? [];
        return new CachedInbox(items, ParseInstant(reader.GetString(1)));
    }

    public async Task SaveInboxAsync(CachedInbox inbox, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inbox);

        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO inbox_cache (id, items, pulled_at) VALUES (1, $items, $pulledAt)
            ON CONFLICT (id) DO UPDATE SET items = excluded.items, pulled_at = excluded.pulled_at
            """;
        command.Parameters.AddWithValue("$items", JsonSerializer.Serialize(inbox.Items, Json));
        command.Parameters.AddWithValue("$pulledAt", FormatInstant(inbox.PulledAt));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>A column a later build added, added to a file an earlier build
    /// made. Never a rename or a drop: the file outlives every build of the app.</summary>
    private static void AddColumnIfMissing(SqliteConnection connection, string table, string column, string definition)
    {
        using var probe = connection.CreateCommand();
        probe.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}'";
        if (Convert.ToInt64(probe.ExecuteScalar(), CultureInfo.InvariantCulture) > 0) return;

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>UTC, round-trip format: fixed width, so text order is time
    /// order and <c>ORDER BY created_at</c> means what it says.</summary>
    private static string FormatInstant(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseInstant(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
