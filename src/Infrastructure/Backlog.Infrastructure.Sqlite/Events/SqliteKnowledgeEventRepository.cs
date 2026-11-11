using System.Globalization;
using System.Text.Json;

using Backlog.Modules.Events;
using Backlog.Modules.Events.DomainModels;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.Events;

/// <summary>
/// Local-first <see cref="IKnowledgeEventRepository"/>: one row per knowledge event in
/// <c>events_knowledge_events</c>, in the same SQLite database the tasks live in (local ADR 0003,
/// ADR 0050 §2), each row the whole event as one JSON document beside its state.
/// <para>
/// This repository creates only its own table (inherited ADR 0014), under its own schema stamp
/// (local ADR 0006). A document that will not parse is skipped rather than rewritten, so a bad
/// write never becomes data loss. <c>created_at</c> is stamped on the first save and kept;
/// <c>updated_at</c> on every save, which is what a later latest-write-wins sync compares.
/// </para>
/// <para>
/// It also answers <see cref="IKnowledgeEventReplicaStore"/> over the same rows: replication
/// reads what changed since a watermark and writes another desktop's copy at the stamp it
/// arrived with. Stamps are compared as the <c>"O"</c> UTC strings they are stored as, which
/// order as the instants do — the reasoning <c>SqliteTaskRepository.ListChangedSinceAsync</c>
/// records.
/// </para>
/// </summary>
public sealed class SqliteKnowledgeEventRepository : IKnowledgeEventRepository, IKnowledgeEventReplicaStore
{
    private readonly string _databasePath;
    private readonly TimeProvider _clock;

    /// <summary>A repository over the database in the given folder, or in the default per-user
    /// app-data folder when null — the same file the tasks use.</summary>
    public SqliteKnowledgeEventRepository(string? rootDir = null, TimeProvider? clock = null)
    {
        var root = rootDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Backlog");

        _databasePath = SqliteTaskRepository.DatabasePathFor(root);
        _clock = clock ?? TimeProvider.System;
    }

    public string DatabasePath => _databasePath;

    public async Task<KnowledgeEvent?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT document FROM events_knowledge_events WHERE id = $id;";
        command.Parameters.AddWithValue("$id", Key(id));

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string document
            ? Parse(document)
            : null;
    }

    public async Task<IReadOnlyList<KnowledgeEvent>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT document FROM events_knowledge_events ORDER BY created_at, id;";

        var all = new List<KnowledgeEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Parse(reader.GetString(0)) is { } knowledgeEvent) all.Add(knowledgeEvent);
        }

        return all;
    }

    public async Task SaveAsync(KnowledgeEvent knowledgeEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(knowledgeEvent);

        var document = JsonSerializer.Serialize(KnowledgeEventDocument.From(knowledgeEvent), KnowledgeEventDocument.JsonOptions);

        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        var now = Stamp(await NextStampAsync(connection, knowledgeEvent.Id, cancellationToken).ConfigureAwait(false));
        await using var command = connection.CreateCommand();

        // created_at is left out of the update, so the first save's stamp is the one kept.
        command.CommandText =
            """
            INSERT INTO events_knowledge_events (id, state, created_at, updated_at, document)
            VALUES ($id, $state, $now, $now, $document)
            ON CONFLICT(id) DO UPDATE SET
                state = excluded.state,
                updated_at = excluded.updated_at,
                document = excluded.document;
            """;

        command.Parameters.AddWithValue("$id", Key(knowledgeEvent.Id));
        command.Parameters.AddWithValue("$state", knowledgeEvent.State.ToString());
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$document", document);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StoredKnowledgeEvent>> ListChangedSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText =
            "SELECT document, updated_at FROM events_knowledge_events WHERE updated_at > $since ORDER BY updated_at, id;";
        command.Parameters.AddWithValue("$since", Stamp(since));

        var changed = new List<StoredKnowledgeEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Parse(reader.GetString(0)) is { } knowledgeEvent)
            {
                changed.Add(new StoredKnowledgeEvent(knowledgeEvent, ReadStamp(reader.GetString(1))));
            }
        }

        return changed;
    }

    public async Task<bool> SaveReplicatedAsync(
        KnowledgeEvent knowledgeEvent,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(knowledgeEvent);

        var document = JsonSerializer.Serialize(KnowledgeEventDocument.From(knowledgeEvent), KnowledgeEventDocument.JsonOptions);

        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // The WHERE on the update is the latest-write-wins rule, decided by SQLite in the same
        // statement as the write: a row saved here after the copy was read is never overwritten
        // by it. An update the WHERE stops counts no change.
        command.CommandText =
            """
            INSERT INTO events_knowledge_events (id, state, created_at, updated_at, document)
            VALUES ($id, $state, $now, $stamp, $document)
            ON CONFLICT(id) DO UPDATE SET
                state = excluded.state,
                updated_at = excluded.updated_at,
                document = excluded.document
            WHERE excluded.updated_at > events_knowledge_events.updated_at;
            """;

        command.Parameters.AddWithValue("$id", Key(knowledgeEvent.Id));
        command.Parameters.AddWithValue("$state", knowledgeEvent.State.ToString());
        command.Parameters.AddWithValue("$now", Stamp(_clock.GetUtcNow()));
        command.Parameters.AddWithValue("$stamp", Stamp(updatedAt));
        command.Parameters.AddWithValue("$document", document);

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <summary>
    /// Now, but never at or before the stamp the row already holds. A copy pulled from a desktop
    /// whose clock runs ahead keeps that desktop's stamp; an edit saved over it at this clock's
    /// earlier now would sit below the push watermark and be refused by every replica as no
    /// later, so it is lost to the other devices. One tick past the held stamp keeps it the
    /// latest write — the rule <c>SqliteGoalRepository</c> and the devbook subject store follow.
    /// </summary>
    private async Task<DateTimeOffset> NextStampAsync(SqliteConnection connection, Guid id, CancellationToken cancellationToken)
    {
        var stamp = _clock.GetUtcNow();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT updated_at FROM events_knowledge_events WHERE id = $id;";
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

    /// <summary>The stored event, or null for a document this build cannot read.</summary>
    private static KnowledgeEvent? Parse(string document)
    {
        if (string.IsNullOrWhiteSpace(document)) return null;

        try
        {
            return JsonSerializer.Deserialize<KnowledgeEventDocument>(document, KnowledgeEventDocument.JsonOptions)?.ToKnowledgeEvent();
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
        owner: "events_knowledge_events",
        ddl: """
            CREATE TABLE IF NOT EXISTS events_knowledge_events (
                id         TEXT PRIMARY KEY NOT NULL,
                state      TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                document   TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_events_knowledge_events_state ON events_knowledge_events (state);
            """);
}
