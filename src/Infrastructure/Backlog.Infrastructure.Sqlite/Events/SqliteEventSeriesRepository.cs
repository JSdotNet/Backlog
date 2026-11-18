using System.Globalization;
using System.Text.Json;

using Backlog.Modules.Events;
using Backlog.Modules.Events.DomainModels;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.Events;

/// <summary>
/// Local-first <see cref="IEventSeriesRepository"/>: one row per event series in
/// <c>events_series</c>, in the same SQLite database the tasks live in (local ADR 0003, ADR 0050
/// §2), each row the whole series as one JSON document.
/// <para>
/// This repository creates only its own table (inherited ADR 0014), under its own schema stamp
/// (local ADR 0006). A document that will not parse is skipped rather than rewritten.
/// <c>created_at</c> is stamped on the first save and kept; <c>updated_at</c> on every save.
/// </para>
/// <para>
/// It also answers <see cref="IEventSeriesReplicaStore"/> over the same rows, on
/// <see cref="SqliteKnowledgeEventRepository"/>'s terms.
/// </para>
/// </summary>
public sealed class SqliteEventSeriesRepository : IEventSeriesRepository, IEventSeriesReplicaStore
{
    private readonly string _databasePath;
    private readonly TimeProvider _clock;

    /// <summary>A repository over the database in the given folder, or in the default per-user
    /// app-data folder when null — the same file the tasks use.</summary>
    public SqliteEventSeriesRepository(string? rootDir = null, TimeProvider? clock = null)
    {
        var root = rootDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Backlog");

        _databasePath = SqliteTaskRepository.DatabasePathFor(root);
        _clock = clock ?? TimeProvider.System;
    }

    public string DatabasePath => _databasePath;

    public async Task<EventSeries?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT document FROM events_series WHERE id = $id;";
        command.Parameters.AddWithValue("$id", Key(id));

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string document
            ? Parse(document)
            : null;
    }

    public async Task<IReadOnlyList<EventSeries>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT document FROM events_series ORDER BY created_at, id;";

        var all = new List<EventSeries>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Parse(reader.GetString(0)) is { } series) all.Add(series);
        }

        return all;
    }

    public async Task SaveAsync(EventSeries series, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(series);

        var document = JsonSerializer.Serialize(EventSeriesDocument.From(series), EventSeriesDocument.JsonOptions);

        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        var now = Stamp(await NextStampAsync(connection, series.Id, cancellationToken).ConfigureAwait(false));
        await using var command = connection.CreateCommand();

        // created_at is left out of the update, so the first save's stamp is the one kept.
        command.CommandText =
            """
            INSERT INTO events_series (id, created_at, updated_at, document)
            VALUES ($id, $now, $now, $document)
            ON CONFLICT(id) DO UPDATE SET
                updated_at = excluded.updated_at,
                document = excluded.document;
            """;

        command.Parameters.AddWithValue("$id", Key(series.Id));
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$document", document);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StoredEventSeries>> ListChangedSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText =
            "SELECT document, updated_at FROM events_series WHERE updated_at > $since ORDER BY updated_at, id;";
        command.Parameters.AddWithValue("$since", Stamp(since));

        var changed = new List<StoredEventSeries>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Parse(reader.GetString(0)) is { } series)
            {
                changed.Add(new StoredEventSeries(series, ReadStamp(reader.GetString(1))));
            }
        }

        return changed;
    }

    public async Task<bool> SaveReplicatedAsync(
        EventSeries series,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(series);

        var document = JsonSerializer.Serialize(EventSeriesDocument.From(series), EventSeriesDocument.JsonOptions);

        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // The latest-write-wins rule in the statement that writes, as for knowledge events.
        command.CommandText =
            """
            INSERT INTO events_series (id, created_at, updated_at, document)
            VALUES ($id, $now, $stamp, $document)
            ON CONFLICT(id) DO UPDATE SET
                updated_at = excluded.updated_at,
                document = excluded.document
            WHERE excluded.updated_at > events_series.updated_at;
            """;

        command.Parameters.AddWithValue("$id", Key(series.Id));
        command.Parameters.AddWithValue("$now", Stamp(_clock.GetUtcNow()));
        command.Parameters.AddWithValue("$stamp", Stamp(updatedAt));
        command.Parameters.AddWithValue("$document", document);

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <summary>Now, but never at or before the stamp the row already holds, for the reason
    /// <see cref="SqliteKnowledgeEventRepository"/> gives.</summary>
    private async Task<DateTimeOffset> NextStampAsync(SqliteConnection connection, Guid id, CancellationToken cancellationToken)
    {
        var stamp = _clock.GetUtcNow();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT updated_at FROM events_series WHERE id = $id;";
        command.Parameters.AddWithValue("$id", Key(id));

        if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string stored)
        {
            var held = ReadStamp(stored);
            if (stamp <= held) stamp = held.AddTicks(1);
        }

        return stamp;
    }

    private static DateTimeOffset ReadStamp(string stamp) =>
        DateTimeOffset.Parse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    /// <summary>The stored series, or null for a document this build cannot read.</summary>
    private static EventSeries? Parse(string document)
    {
        if (string.IsNullOrWhiteSpace(document)) return null;

        try
        {
            return JsonSerializer.Deserialize<EventSeriesDocument>(document, EventSeriesDocument.JsonOptions)?.ToEventSeries();
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
        owner: "events_series",
        ddl: """
            CREATE TABLE IF NOT EXISTS events_series (
                id         TEXT PRIMARY KEY NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                document   TEXT NOT NULL
            );
            """);
}
