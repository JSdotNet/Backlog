using System.Globalization;
using System.Text.Json;

using Backlog.Modules.Progress;
using Backlog.Modules.Progress.DomainModels;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.Progress;

/// <summary>
/// Local-first <see cref="ISprintSeriesRepository"/>: one row per sprint series in the
/// same SQLite database the tasks live in, each row the whole series as one JSON
/// document. Fully offline.
/// <para>
/// A row per series rather than one row for all of them, as the roadmap keeps its one
/// plan: a series is the consistency boundary, and a document per series is what can
/// later travel between devices with the newer write winning per document. The
/// <c>updated_at</c> stamp is written from the first day for that reason.
/// </para>
/// <para>
/// This repository creates only its own table (inherited ADR 0014): the tables share a
/// file, not an owner. A document that will not parse is skipped rather than rewritten,
/// so a bad write never becomes data loss.
/// </para>
/// </summary>
public sealed class SqliteSprintSeriesRepository : ISprintSeriesRepository
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

    private static SprintSeries? Parse(string document)
    {
        if (string.IsNullOrWhiteSpace(document)) return null;

        try
        {
            return JsonSerializer.Deserialize<SprintSeriesDocument>(document, SprintSeriesDocument.JsonOptions)?.ToSeries();
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
        SqliteSchema.OpenAsync(databasePath, EnsureSchemaAsync, cancellationToken);

    /// <summary>Idempotent DDL run on every open (local ADR 0003): an existing
    /// <c>backlog.db</c> gains this table and loses nothing.</summary>
    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await SqliteSchema.EnsureAsync(connection, """
            CREATE TABLE IF NOT EXISTS progress_series (
                id         TEXT PRIMARY KEY NOT NULL,
                document   TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """, cancellationToken).ConfigureAwait(false);
    }
}
