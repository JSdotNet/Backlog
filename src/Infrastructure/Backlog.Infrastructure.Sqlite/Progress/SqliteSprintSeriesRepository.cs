using System.Globalization;
using System.Text.Json;

using Backlog.Modules.Progress;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.DomainModels;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.Progress;

/// <summary>
/// Local-first <see cref="ISprintSeriesRepository"/>: one row per sprint series in the
/// same SQLite database the tasks live in, each row the whole series as one JSON
/// document. Fully offline.
/// <para>
/// A row per series rather than one row for all of them, as the roadmap keeps its one
/// plan: a series is the consistency boundary, and a document per series is what
/// travels between devices with the newer write winning per document.
/// </para>
/// <para>
/// The same row is that travelling document (<see cref="ISprintSeriesReplicaStore"/>):
/// read and written as stored text, so a copy from another device is kept verbatim at
/// the stamp it arrived with.
/// </para>
/// <para>
/// This repository creates only its own table (inherited ADR 0014): the tables share a
/// file, not an owner. A document that will not parse is skipped rather than rewritten,
/// so a bad write never becomes data loss.
/// </para>
/// </summary>
public sealed class SqliteSprintSeriesRepository : ISprintSeriesRepository, ISprintSeriesReplicaStore
{
    private readonly string _databasePath;

    /// <summary>A repository over the database in the given folder, or in the default
    /// per-user app-data folder when null — the same file the tasks use.</summary>
    public SqliteSprintSeriesRepository(string? rootDir = null)
    {
        var root = rootDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Backlog");

        _databasePath = SqliteTaskRepository.DatabasePathFor(root);
    }

    public string DatabasePath => _databasePath;

    public async Task<IReadOnlyList<SprintSeries>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT document FROM progress_series ORDER BY created_at, id;";

        var all = new List<SprintSeries>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Parse(reader.GetString(0)) is { } series) all.Add(series);
        }

        return all;
    }

    public async Task<SprintSeries?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT document FROM progress_series WHERE id = $id;";
        command.Parameters.AddWithValue("$id", Key(id));

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string document
            ? Parse(document)
            : null;
    }

    public async Task SaveAsync(SprintSeries series, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(series);

        var document = JsonSerializer.Serialize(SprintSeriesDocument.From(series), SprintSeriesDocument.JsonOptions);

        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);

        // Never at or before the stamp the row already holds, for the reason the roadmap
        // plan gives: a copy from a device whose clock runs ahead must not make a later
        // edit here read as older.
        var stamp = DateTimeOffset.UtcNow;
        if (await StampAsync(connection, series.Id, cancellationToken).ConfigureAwait(false) is { } held && stamp <= held)
        {
            stamp = held.AddTicks(1);
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO progress_series (id, document, created_at, updated_at)
            VALUES ($id, $document, $updated_at, $updated_at)
            ON CONFLICT(id) DO UPDATE SET
                document = excluded.document,
                updated_at = excluded.updated_at;
            """;

        command.Parameters.AddWithValue("$id", Key(series.Id));
        command.Parameters.AddWithValue("$document", document);
        command.Parameters.AddWithValue("$updated_at", stamp.ToString("O", CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Replaces the series' row with its tombstone (<see cref="SprintSeriesDocument.Tombstone"/>):
    /// every sprint it held is gone, <see cref="ListAsync"/> and <see cref="GetAsync"/>
    /// skip it, and the row is stamped later than it was, as a save is — so the next push
    /// carries the deletion, and an older copy of the series is refused on its stamp. The
    /// row is kept rather than removed because a removed row would take a pulled copy
    /// of the series as one this device never held. An id with no row writes nothing.
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);

        var name = string.Empty;
        await using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT document FROM progress_series WHERE id = $id;";
            read.Parameters.AddWithValue("$id", Key(id));
            if (await read.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not string stored) return;

            name = ParseDocument(stored)?.Name ?? string.Empty;
        }

        var stamp = DateTimeOffset.UtcNow;
        if (await StampAsync(connection, id, cancellationToken).ConfigureAwait(false) is { } held && stamp <= held)
        {
            stamp = held.AddTicks(1);
        }

        var tombstone = JsonSerializer.Serialize(SprintSeriesDocument.Tombstone(id, name, stamp), SprintSeriesDocument.JsonOptions);

        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE progress_series SET document = $document, updated_at = $updated_at WHERE id = $id;";
        command.Parameters.AddWithValue("$id", Key(id));
        command.Parameters.AddWithValue("$document", tombstone);
        command.Parameters.AddWithValue("$updated_at", stamp.ToString("O", CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // --- The replicated documents (the arrangement of local ADR 0018) -------

    /// <summary>Every row's document and stamp exactly as stored, in the order the
    /// series were created — a deleted series' tombstone included, since the deletion has
    /// to travel. A blank document is nothing to send and is left out.</summary>
    public async Task<IReadOnlyList<SprintSeriesReplicaCopyDto>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT id, document, updated_at FROM progress_series ORDER BY created_at, id;";

        var all = new List<SprintSeriesReplicaCopyDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Copy(reader.GetString(0), reader.GetString(1), reader.GetString(2)) is { } copy) all.Add(copy);
        }

        return all;
    }

    /// <summary>One row's document and stamp exactly as stored, or <c>null</c> when this
    /// device holds no series with that id.</summary>
    public async Task<SprintSeriesReplicaCopyDto?> ReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT id, document, updated_at FROM progress_series WHERE id = $id;";
        command.Parameters.AddWithValue("$id", Key(id));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? Copy(reader.GetString(0), reader.GetString(1), reader.GetString(2))
            : null;
    }

    /// <summary>
    /// Stores another device's series verbatim, at the stamp it arrived with — not this
    /// machine's clock, which would make the copy look newer than it is.
    /// <para>
    /// Refused, with nothing written, when the text is not a series this build can read,
    /// or is a series under another id than the copy claims: a row is never overwritten
    /// with something <see cref="GetAsync"/> would skip, nor with another series. A
    /// series this device never held is added, created at the stamp it arrived with. A
    /// tombstone is a copy like any other: written here, it deletes the series here.
    /// </para>
    /// </summary>
    public async Task<bool> TryWriteAsync(SprintSeriesReplicaCopyDto copy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(copy);

        if (ParseDocument(copy.Content) is not { } document || document.Id != copy.Id) return false;

        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO progress_series (id, document, created_at, updated_at)
            VALUES ($id, $document, $updated_at, $updated_at)
            ON CONFLICT(id) DO UPDATE SET
                document = excluded.document,
                updated_at = excluded.updated_at;
            """;

        command.Parameters.AddWithValue("$id", Key(copy.Id));
        command.Parameters.AddWithValue("$document", copy.Content);
        command.Parameters.AddWithValue("$updated_at", copy.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>A stored row as a copy to send, named for the sync log by the series'
    /// own name where this build can read it.</summary>
    private static SprintSeriesReplicaCopyDto? Copy(string id, string document, string stamp)
    {
        if (string.IsNullOrWhiteSpace(document) || !Guid.TryParse(id, out var key)) return null;

        return new SprintSeriesReplicaCopyDto(
            key,
            ParseDocument(document)?.Name ?? string.Empty,
            document,
            DateTimeOffset.Parse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    /// <summary>The series a row holds, or null for a tombstone and for a document this
    /// build cannot read.</summary>
    private static SprintSeries? Parse(string document) =>
        ParseDocument(document) is { IsTombstone: false } parsed ? parsed.ToSeries() : null;

    /// <summary>The stored document — a series or a tombstone — or null for one this build
    /// cannot read.</summary>
    private static SprintSeriesDocument? ParseDocument(string document)
    {
        if (string.IsNullOrWhiteSpace(document)) return null;

        try
        {
            var parsed = JsonSerializer.Deserialize<SprintSeriesDocument>(document, SprintSeriesDocument.JsonOptions);
            if (parsed is null) return null;

            // Read through once, as a series, so a document that will not load is
            // refused here as it always was, not discovered on the next ListAsync.
            if (!parsed.IsTombstone) _ = parsed.ToSeries();
            return parsed;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<DateTimeOffset?> StampAsync(SqliteConnection connection, Guid id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT updated_at FROM progress_series WHERE id = $id;";
        command.Parameters.AddWithValue("$id", Key(id));

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string stored
            ? DateTimeOffset.Parse(stored, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            : null;
    }

    private static string Key(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    private static Task<SqliteConnection> OpenAsync(string databasePath, CancellationToken cancellationToken) =>
        SqliteSchema.OpenAsync(databasePath, Schema, cancellationToken);

    /// <summary>Idempotent DDL run on any open the file is not yet stamped for
    /// (local ADR 0003): an existing <c>backlog.db</c> gains this table and loses
    /// nothing.</summary>
    internal static readonly SqliteSchemaDefinition Schema = new(
        owner: "progress",
        ddl: """
            CREATE TABLE IF NOT EXISTS progress_series (
                id         TEXT PRIMARY KEY NOT NULL,
                document   TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """);
}
