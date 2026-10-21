using System.Globalization;
using System.Text.Json;

using Backlog.Modules.Notifications;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Notifications.DomainModels;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.Notifications;

/// <summary>
/// Local-first <see cref="INotificationRepository"/>: one row per notification in
/// <c>notifications</c>, in the same SQLite database the tasks live in (local ADR 0003, ADR 0038
/// §3), each row the whole notification as one JSON document beside the two columns the module
/// queries on — its state, and whether an accepted answer still waits for its handler.
/// <para>
/// This repository creates only its own table (inherited ADR 0014), under its own schema stamp
/// (local ADR 0006). A document that will not parse is skipped rather than rewritten, so a bad
/// write never becomes data loss.
/// </para>
/// </summary>
public sealed class SqliteNotificationRepository : INotificationRepository
{
    private readonly string _databasePath;

    /// <summary>A repository over the database in the given folder, or in the default per-user
    /// app-data folder when null — the same file the tasks use.</summary>
    public SqliteNotificationRepository(string? rootDir = null)
    {
        var root = rootDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Backlog");

        _databasePath = SqliteTaskRepository.DatabasePathFor(root);
    }

    public string DatabasePath => _databasePath;

    public async Task<Notification?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT document FROM notifications WHERE id = $id;";
        command.Parameters.AddWithValue("$id", Key(id));

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string document
            ? Parse(document)
            : null;
    }

    public Task<IReadOnlyList<Notification>> ListOpenAsync(CancellationToken cancellationToken = default) =>
        ListAsync($"WHERE state = '{nameof(NotificationState.Open)}'", cancellationToken);

    public Task<IReadOnlyList<Notification>> ListAwaitingDeliveryAsync(CancellationToken cancellationToken = default) =>
        ListAsync("WHERE awaits_delivery = 1", cancellationToken);

    public async Task SaveAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var document = JsonSerializer.Serialize(NotificationDocument.From(notification), NotificationDocument.JsonOptions);

        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO notifications (id, state, awaits_delivery, created_at, updated_at, document)
            VALUES ($id, $state, $awaits_delivery, $created_at, $updated_at, $document)
            ON CONFLICT(id) DO UPDATE SET
                state = excluded.state,
                awaits_delivery = excluded.awaits_delivery,
                updated_at = excluded.updated_at,
                document = excluded.document;
            """;

        command.Parameters.AddWithValue("$id", Key(notification.Id));
        command.Parameters.AddWithValue("$state", notification.State.ToString());
        command.Parameters.AddWithValue("$awaits_delivery", notification.AwaitsDelivery ? 1 : 0);
        command.Parameters.AddWithValue("$created_at", Stamp(notification.CreatedAt));
        command.Parameters.AddWithValue("$updated_at", Stamp(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$document", document);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<Notification>> ListAsync(string where, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = $"SELECT document FROM notifications {where} ORDER BY created_at, id;";

        var all = new List<Notification>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Parse(reader.GetString(0)) is { } notification) all.Add(notification);
        }

        return all;
    }

    /// <summary>The stored notification, or null for a document this build cannot read.</summary>
    private static Notification? Parse(string document)
    {
        if (string.IsNullOrWhiteSpace(document)) return null;

        try
        {
            return JsonSerializer.Deserialize<NotificationDocument>(document, NotificationDocument.JsonOptions)?.ToNotification();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Key(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    private static string Stamp(DateTimeOffset stamp) => stamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static Task<SqliteConnection> OpenAsync(string databasePath, CancellationToken cancellationToken) =>
        SqliteSchema.OpenAsync(databasePath, Schema, cancellationToken);

    /// <summary>
    /// The table's migration: idempotent DDL run on any open the file is not yet stamped for
    /// (local ADR 0003, ADR 0006), under its own stamp. An existing <c>backlog.db</c> gains the
    /// table and loses nothing.
    /// </summary>
    internal static readonly SqliteSchemaDefinition Schema = new(
        owner: "notifications",
        ddl: """
            CREATE TABLE IF NOT EXISTS notifications (
                id              TEXT PRIMARY KEY NOT NULL,
                state           TEXT NOT NULL,
                awaits_delivery INTEGER NOT NULL DEFAULT 0,
                created_at      TEXT NOT NULL,
                updated_at      TEXT NOT NULL,
                document        TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_notifications_state ON notifications (state);
            CREATE INDEX IF NOT EXISTS ix_notifications_awaits_delivery ON notifications (awaits_delivery);
            """);
}
