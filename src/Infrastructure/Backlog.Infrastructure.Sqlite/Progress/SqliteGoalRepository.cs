using System.Globalization;
using System.Text.Json;

using Backlog.Modules.Progress;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.DomainModels;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.Progress;

/// <summary>
/// Local-first <see cref="IGoalRepository"/>: one row per goal in <c>progress_goals</c>,
/// in the same SQLite database the tasks live in, each row the whole goal as one JSON
/// document, because the goal is the consistency boundary for its attachments and the
/// document is what travels.
/// <para>
/// The row is also the travelling <c>goal</c> document (<see cref="IGoalReplicaStore"/>,
/// local ADR 0035 §9): read and written as stored text, so a copy from another device is
/// kept verbatim at the stamp it arrived with. A deleted goal leaves a tombstone row so
/// the deletion travels.
/// </para>
/// <para>
/// This repository creates only its own table (inherited ADR 0014), under its own schema
/// stamp. A document that will not parse is skipped rather than rewritten, so a bad write
/// never becomes data loss.
/// </para>
/// </summary>
public sealed class SqliteGoalRepository : IGoalRepository, IGoalReplicaStore
{
    private readonly string _databasePath;

    /// <summary>A repository over the database in the given folder, or in the default
    /// per-user app-data folder when null — the same file the tasks use.</summary>
    public SqliteGoalRepository(string? rootDir = null)
    {
        var root = rootDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Backlog");

        _databasePath = SqliteTaskRepository.DatabasePathFor(root);
    }

    public string DatabasePath => _databasePath;

    public async Task<IReadOnlyList<Goal>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT document, created_at FROM progress_goals;";

        var all = new List<Goal>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Parse(reader.GetString(0), reader.GetString(1)) is { } goal) all.Add(goal);
        }

        return [.. GoalOrder.Sort(all, goal => goal.Rank, goal => goal.CreatedAt, goal => goal.Id)];
    }

    private static DateTimeOffset? Moment(string stored) =>
        DateTimeOffset.TryParse(stored, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var moment) ? moment : null;

    public async Task<Goal?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT document, created_at FROM progress_goals WHERE id = $id;";
        command.Parameters.AddWithValue("$id", Key(id));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? Parse(reader.GetString(0), reader.GetString(1))
            : null;
    }

    public async Task SaveAsync(Goal goal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(goal);

        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);

        // What a newer build wrote into the stored goal and this one does not know is
        // written back with it, so an edit here does not strip it.
        var extra = await StoredExtraAsync(connection, goal.Id, cancellationToken).ConfigureAwait(false);
        var document = JsonSerializer.Serialize(GoalDocument.From(goal) with { Extra = extra }, GoalDocument.JsonOptions);

        var stamp = await NextStampAsync(connection, goal.Id, cancellationToken).ConfigureAwait(false);

        await UpsertAsync(connection, goal.Id, document, stamp, goal.CreatedAt ?? stamp, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Dictionary<string, JsonElement>?> StoredExtraAsync(SqliteConnection connection, Guid id, CancellationToken cancellationToken)
    {
        await using var read = connection.CreateCommand();
        read.CommandText = "SELECT document FROM progress_goals WHERE id = $id;";
        read.Parameters.AddWithValue("$id", Key(id));

        return await read.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string stored
               && ParseDocument(stored) is { IsTombstone: false, Extra: { Count: > 0 } extra }
            ? extra
            : null;
    }

    /// <summary>
    /// Replaces the goal's row with its tombstone: <see cref="ListAsync"/> and
    /// <see cref="GetAsync"/> skip it, and the row is stamped later than it was, as a save
    /// is — so the next push carries the deletion, and an older copy of the goal is refused
    /// on its stamp. An id with no row writes nothing.
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);

        string title;
        await using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT document FROM progress_goals WHERE id = $id;";
            read.Parameters.AddWithValue("$id", Key(id));
            if (await read.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not string stored) return;

            title = ParseDocument(stored)?.Title ?? string.Empty;
        }

        var stamp = await NextStampAsync(connection, id, cancellationToken).ConfigureAwait(false);
        var tombstone = JsonSerializer.Serialize(GoalDocument.Tombstone(id, title, stamp), GoalDocument.JsonOptions);

        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE progress_goals SET document = $document, updated_at = $updated_at WHERE id = $id;";
        command.Parameters.AddWithValue("$id", Key(id));
        command.Parameters.AddWithValue("$document", tombstone);
        command.Parameters.AddWithValue("$updated_at", Stamp(stamp));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // --- The replicated documents -------------------------------------------

    /// <summary>Every row's document and stamp exactly as stored, tombstones included, in
    /// the person's order; a tombstone, which holds no place, by when its row was set.</summary>
    public async Task<IReadOnlyList<GoalReplicaCopyDto>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT id, document, updated_at, created_at FROM progress_goals;";

        var all = new List<(GoalReplicaCopyDto Copy, long? Rank, DateTimeOffset? CreatedAt)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Copy(reader.GetString(0), reader.GetString(1), reader.GetString(2)) is not { } copy) continue;

            var document = ParseDocument(copy.Content);
            all.Add((copy, document?.Rank, document?.CreatedAt ?? Moment(reader.GetString(3))));
        }

        return [.. GoalOrder.Sort(all, entry => entry.Rank, entry => entry.CreatedAt, entry => entry.Copy.Id).Select(entry => entry.Copy)];
    }

    public async Task<GoalReplicaCopyDto?> ReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT id, document, updated_at FROM progress_goals WHERE id = $id;";
        command.Parameters.AddWithValue("$id", Key(id));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? Copy(reader.GetString(0), reader.GetString(1), reader.GetString(2))
            : null;
    }

    /// <summary>Stores another device's goal verbatim, at the stamp it arrived with.
    /// Refused, with nothing written, when the text is not a goal this build can read or
    /// names another goal than the copy claims. A tombstone is a copy like any other:
    /// written here, it deletes the goal here.</summary>
    public async Task<bool> TryWriteAsync(GoalReplicaCopyDto copy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(copy);

        if (ParseDocument(copy.Content) is not { } document || document.Id != copy.Id) return false;

        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        // A goal this device never held is placed by when it was set, as the document
        // says, not by the stamp of its last edit — so every device lists the goals alike.
        await UpsertAsync(connection, copy.Id, copy.Content, copy.UpdatedAt, document.CreatedAt ?? copy.UpdatedAt, cancellationToken).ConfigureAwait(false);

        return true;
    }

    private static async Task UpsertAsync(
        SqliteConnection connection,
        Guid id,
        string document,
        DateTimeOffset stamp,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO progress_goals (id, document, created_at, updated_at)
            VALUES ($id, $document, $created_at, $updated_at)
            ON CONFLICT(id) DO UPDATE SET
                document = excluded.document,
                updated_at = excluded.updated_at;
            """;

        command.Parameters.AddWithValue("$id", Key(id));
        command.Parameters.AddWithValue("$document", document);
        command.Parameters.AddWithValue("$updated_at", Stamp(stamp));
        command.Parameters.AddWithValue("$created_at", Stamp(createdAt));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Now, but never at or before the stamp the row already holds, for the
    /// reason the series gives: a copy from a device whose clock runs ahead must not make a
    /// later edit here read as older.</summary>
    private static async Task<DateTimeOffset> NextStampAsync(SqliteConnection connection, Guid id, CancellationToken cancellationToken)
    {
        var stamp = DateTimeOffset.UtcNow;

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT updated_at FROM progress_goals WHERE id = $id;";
        command.Parameters.AddWithValue("$id", Key(id));

        if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string stored)
        {
            var held = DateTimeOffset.Parse(stored, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (stamp <= held) stamp = held.AddTicks(1);
        }

        return stamp;
    }

    private static GoalReplicaCopyDto? Copy(string id, string document, string stamp)
    {
        if (string.IsNullOrWhiteSpace(document) || !Guid.TryParse(id, out var key)) return null;

        return new GoalReplicaCopyDto(
            key,
            ParseDocument(document)?.Title ?? string.Empty,
            document,
            DateTimeOffset.Parse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    /// <summary>The goal a row holds, or null for a tombstone and for a document this build
    /// cannot read. A document that does not say when the goal was set reads as set at its
    /// row's <c>created_at</c> — the stamp of its first save, where the row has always listed
    /// it — so the store's order and the order <see cref="Modules.Progress.Services.GoalOrdering"/> counts a move in are
    /// one order, both by <see cref="GoalOrder.Key"/>.</summary>
    private static Goal? Parse(string document, string rowCreatedAt) =>
        ParseDocument(document) is { IsTombstone: false } parsed ? parsed.ToGoal(Moment(rowCreatedAt)) : null;

    /// <summary>The stored document — a goal or a tombstone — or null for one this build
    /// cannot read.</summary>
    private static GoalDocument? ParseDocument(string document)
    {
        if (string.IsNullOrWhiteSpace(document)) return null;

        try
        {
            var parsed = JsonSerializer.Deserialize<GoalDocument>(document, GoalDocument.JsonOptions);
            if (parsed is null) return null;

            // Read through once, as a goal, so a document that will not load is refused
            // here rather than discovered on the next ListAsync.
            if (!parsed.IsTombstone) _ = parsed.ToGoal();
            return parsed;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Key(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    private static string Stamp(DateTimeOffset stamp) => stamp.ToString("O", CultureInfo.InvariantCulture);

    private static Task<SqliteConnection> OpenAsync(string databasePath, CancellationToken cancellationToken) =>
        SqliteSchema.OpenAsync(databasePath, Schema, cancellationToken);

    /// <summary>
    /// The table's migration: idempotent DDL run on any open the file is not yet stamped
    /// for (local ADR 0003, ADR 0006), under its own stamp so the series' setup and this
    /// one never re-run each other. An existing <c>backlog.db</c> gains the table and loses
    /// nothing.
    /// </summary>
    internal static readonly SqliteSchemaDefinition Schema = new(
        owner: "progress_goals",
        ddl: """
            CREATE TABLE IF NOT EXISTS progress_goals (
                id         TEXT PRIMARY KEY NOT NULL,
                document   TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """);
}
