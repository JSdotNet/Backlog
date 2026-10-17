using System.Globalization;
using System.Text.Json;

using Backlog.Modules.Progress;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.DomainModels;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.Progress;

/// <summary>
/// Local-first <see cref="IImpedimentRepository"/>: one row per impediment in
/// <c>progress_impediments</c>, in the same SQLite database the tasks live in, each row the
/// whole impediment as one JSON document — <see cref="SqliteGoalRepository"/>'s pattern.
/// <para>
/// The row is also the travelling <c>impediment</c> document
/// (<see cref="IImpedimentReplicaStore"/>, local ADR 0035 §9): read and written as stored
/// text, so a copy from another device is kept verbatim at the stamp it arrived with.
/// </para>
/// <para>
/// This repository creates only its own table (inherited ADR 0014), under its own schema
/// stamp. A document that will not parse is skipped rather than rewritten, so a bad write
/// never becomes data loss.
/// </para>
/// </summary>
public sealed class SqliteImpedimentRepository : IImpedimentRepository, IImpedimentReplicaStore
{
    private readonly string _databasePath;

    /// <summary>A repository over the database in the given folder, or in the default
    /// per-user app-data folder when null — the same file the tasks use.</summary>
    public SqliteImpedimentRepository(string? rootDir = null)
    {
        var root = rootDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Backlog");

        _databasePath = SqliteTaskRepository.DatabasePathFor(root);
    }

    public string DatabasePath => _databasePath;

    public async Task<IReadOnlyList<Impediment>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT document FROM progress_impediments ORDER BY created_at, id;";

        var all = new List<Impediment>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Parse(reader.GetString(0)) is { } impediment) all.Add(impediment);
        }

        return all;
    }

    public async Task<Impediment?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT document FROM progress_impediments WHERE id = $id;";
        command.Parameters.AddWithValue("$id", Key(id));

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string document
            ? Parse(document)
            : null;
    }

    public async Task SaveAsync(Impediment impediment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(impediment);

        var document = JsonSerializer.Serialize(ImpedimentDocument.From(impediment), ImpedimentDocument.JsonOptions);

        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        var stamp = await NextStampAsync(connection, impediment.Id, cancellationToken).ConfigureAwait(false);

        await UpsertAsync(connection, impediment.Id, document, stamp, impediment.CreatedAt ?? stamp, cancellationToken).ConfigureAwait(false);
    }

    // --- The replicated documents -------------------------------------------

    public async Task<IReadOnlyList<ImpedimentReplicaCopyDto>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT id, document, updated_at FROM progress_impediments ORDER BY created_at, id;";

        var all = new List<ImpedimentReplicaCopyDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Copy(reader.GetString(0), reader.GetString(1), reader.GetString(2)) is { } copy) all.Add(copy);
        }

        return all;
    }

    public async Task<ImpedimentReplicaCopyDto?> ReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT id, document, updated_at FROM progress_impediments WHERE id = $id;";
        command.Parameters.AddWithValue("$id", Key(id));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? Copy(reader.GetString(0), reader.GetString(1), reader.GetString(2))
            : null;
    }

    /// <summary>Stores another device's impediment verbatim, at the stamp it arrived with.
    /// Refused, with nothing written, when the text is not an impediment this build can
    /// read or names another one than the copy claims.</summary>
    public async Task<bool> TryWriteAsync(ImpedimentReplicaCopyDto copy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(copy);

        if (ParseDocument(copy.Content) is not { } document || document.Id != copy.Id) return false;

        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
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
            INSERT INTO progress_impediments (id, document, created_at, updated_at)
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
    /// reason the goal's store gives.</summary>
    private static async Task<DateTimeOffset> NextStampAsync(SqliteConnection connection, Guid id, CancellationToken cancellationToken)
    {
        var stamp = DateTimeOffset.UtcNow;

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT updated_at FROM progress_impediments WHERE id = $id;";
        command.Parameters.AddWithValue("$id", Key(id));

        if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string stored)
        {
            var held = DateTimeOffset.Parse(stored, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (stamp <= held) stamp = held.AddTicks(1);
        }

        return stamp;
    }

    private static ImpedimentReplicaCopyDto? Copy(string id, string document, string stamp)
    {
        if (string.IsNullOrWhiteSpace(document) || !Guid.TryParse(id, out var key)) return null;

        return new ImpedimentReplicaCopyDto(
            key,
            ParseDocument(document)?.Title ?? string.Empty,
            document,
            DateTimeOffset.Parse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    private static Impediment? Parse(string document) => ParseDocument(document)?.ToImpediment();

    /// <summary>The stored document, or null for one this build cannot read.</summary>
    private static ImpedimentDocument? ParseDocument(string document)
    {
        if (string.IsNullOrWhiteSpace(document)) return null;

        try
        {
            var parsed = JsonSerializer.Deserialize<ImpedimentDocument>(document, ImpedimentDocument.JsonOptions);
            if (parsed is null) return null;

            // Read through once, so a document that will not load is refused here rather
            // than discovered on the next ListAsync.
            _ = parsed.ToImpediment();
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
    /// for (local ADR 0003, ADR 0006), under its own stamp. An existing <c>backlog.db</c>
    /// gains the table and loses nothing.
    /// </summary>
    internal static readonly SqliteSchemaDefinition Schema = new(
        owner: "progress_impediments",
        ddl: """
            CREATE TABLE IF NOT EXISTS progress_impediments (
                id         TEXT PRIMARY KEY NOT NULL,
                document   TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """);
}
