using System.Globalization;
using System.Text.Json;
using Backlog.Infrastructure.Sqlite.Progress;
using Backlog.Infrastructure.Sqlite.Roadmap;
using Backlog.Modules.Tasks;
using Backlog.SharedKernel.Changes;
using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.Changes;

/// <summary>
/// Restores a change set (local ADR 0061 §4) over the database in one folder: walks
/// its revisions newest first and writes each document back as a new, later save.
/// <para>
/// <b>A conflict is decided per document.</b> A document whose stamp no longer matches
/// the <c>after_updated_at</c> the change set left was changed since; it is kept as it
/// is unless the person chose to restore it anyway. The check runs again as each
/// document is written, and a task save is itself conditional on the version it was
/// read at, so a write that lands between the check and the save is kept too.
/// </para>
/// <para>
/// <b>Otherwise the prior document comes back, stamped now.</b> A task is rebuilt from
/// its before-image and saved through <see cref="ITaskRepository"/> — the store the app
/// writes through, so the pane and the sync hear it — after
/// <see cref="Modules.Tasks.DomainModels.TaskItem.Supersede"/> stamps it later than the
/// version it replaces; a tombstoned task comes back through
/// <see cref="Modules.Tasks.DomainModels.TaskItem.Restore"/>. A goal, an impediment or
/// the roadmap plan gets its prior stored document back verbatim, stamped past the one
/// held. A document the change set created is tombstoned: a task, or a goal. An
/// impediment and the roadmap plan have no tombstone, so one the change set created
/// stays, and the plan says so.
/// </para>
/// <para>
/// The restore is a change set of its own, actor <see cref="ChangeActor.Person"/>,
/// labelled "Restore of &lt;label&gt;", and the restored change set's
/// <c>undone_by</c> points at it once it wrote anything.
/// </para>
/// </summary>
public sealed class SqliteChangeRestore : IChangeRestore
{
    private const string TaskKind = "task";

    private readonly string _rootDirectory;
    private readonly string _databasePath;
    private readonly ITaskRepository _tasks;
    private readonly TimeProvider _time;

    /// <summary>A restore over the database in <paramref name="rootDirectory"/>, saving
    /// tasks through <paramref name="tasks"/>, which must write that same file.</summary>
    public SqliteChangeRestore(string rootDirectory, ITaskRepository tasks, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        _rootDirectory = rootDirectory;
        _databasePath = SqliteTaskRepository.DatabasePathFor(rootDirectory);
        _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The document kinds other than a task a restore writes back: their table,
    /// and the setup that journals it, so the restore's own write is journalled too.</summary>
    private static readonly IReadOnlyDictionary<string, DocumentTable> Tables = new Dictionary<string, DocumentTable>(StringComparer.Ordinal)
    {
        ["goal"] = new("progress_goals", SqliteGoalRepository.Schema, SqliteGoalRepository.JournalSchema, HasCreatedAt: true, CanTombstone: true),
        ["impediment"] = new("progress_impediments", SqliteImpedimentRepository.Schema, SqliteImpedimentRepository.JournalSchema, HasCreatedAt: true, CanTombstone: false),
        ["roadmap-plan"] = new("roadmap_plan", SqliteRoadmapPlanRepository.Schema, SqliteRoadmapPlanRepository.JournalSchema, HasCreatedAt: false, CanTombstone: false),
    };

    public async Task<RestorePlan?> PlanAsync(Guid changeSetId, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteTaskRepository.OpenForJournalAsync(_databasePath, cancellationToken).ConfigureAwait(false);

        if (await ReadHeaderAsync(connection, changeSetId, cancellationToken).ConfigureAwait(false) is not { } header) return null;

        var steps = new List<RestoreStep>();
        foreach (var revision in await ReadRevisionsAsync(connection, changeSetId, cancellationToken).ConfigureAwait(false))
        {
            steps.Add(await StepAsync(connection, revision, cancellationToken).ConfigureAwait(false));
        }

        return new RestorePlan(changeSetId, header.Label, header.UndoneBy, steps);
    }

    public async Task<RestoreOutcome> RestoreAsync(
        Guid changeSetId,
        IReadOnlySet<DocumentKey> restoreAnyway,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(restoreAnyway);

        Header header;
        IReadOnlyList<Revision> revisions;
        await using (var connection = await SqliteTaskRepository.OpenForJournalAsync(_databasePath, cancellationToken).ConfigureAwait(false))
        {
            header = await ReadHeaderAsync(connection, changeSetId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"The change journal holds no change set {changeSetId}.");

            // Restored once already: a second restore would write over the first and
            // move undone_by off the change set that did it.
            if (header.UndoneBy is not null)
            {
                throw new InvalidOperationException("That change set has already been restored.");
            }

            revisions = await ReadRevisionsAsync(connection, changeSetId, cancellationToken).ConfigureAwait(false);
        }

        var restore = ChangeSet.Start(ChangeActor.Person, RestorePlan.RestoreLabelFor(header.Label), _time);
        var restored = 0;
        var kept = 0;

        using (ChangeContext.Enter(restore))
        {
            foreach (var revision in revisions)
            {
                bool written;
                try
                {
                    written = await ApplyAsync(revision, restoreAnyway.Contains(revision.Key), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Each document is its own save, so one that fails is kept as it is
                    // and the rest go on: the outcome counts it, and undone_by below
                    // still records the documents that were written.
                    written = false;
                }

                if (written) restored++;
                else kept++;
            }
        }

        if (restored == 0) return new RestoreOutcome(null, restore.Label!, restored, kept);

        await MarkUndoneAsync(changeSetId, restore.Id, cancellationToken).ConfigureAwait(false);
        return new RestoreOutcome(restore.Id, restore.Label!, restored, kept);
    }

    // --- Planning -----------------------------------------------------------

    private async Task<RestoreStep> StepAsync(SqliteConnection connection, Revision revision, CancellationToken cancellationToken)
    {
        var held = await HeldStampAsync(connection, revision.Key, cancellationToken).ConfigureAwait(false);
        var conflict = !string.Equals(held, revision.AfterUpdatedAt, StringComparison.Ordinal);

        if (revision.Key.Kind == TaskKind)
        {
            var current = await SqliteTaskRepository.ReadCurrentAsync(connection, revision.Key.Id, cancellationToken).ConfigureAwait(false);
            var prior = revision.Before is null
                ? null
                : await SqliteTaskRepository.ReadBeforeImageAsync(connection, revision.Before, cancellationToken).ConfigureAwait(false);

            var action = revision.Before is null ? RestoreAction.Tombstone
                : prior is not null ? RestoreAction.Revert
                : RestoreAction.Unsupported;

            return new RestoreStep(revision.Key, current?.Title ?? prior?.Title ?? revision.Key.Id, action, conflict);
        }

        if (!Tables.TryGetValue(revision.Key.Kind, out var table))
        {
            return new RestoreStep(revision.Key, revision.Key.Id, RestoreAction.Unsupported, conflict);
        }

        var heldDocument = await HeldDocumentAsync(connection, table, revision.Key.Id, cancellationToken).ConfigureAwait(false);
        var priorDocument = DocumentOf(revision.Before);

        var documentAction = revision.Before is null
            ? table.CanTombstone && Guid.TryParse(revision.Key.Id, out _) ? RestoreAction.Tombstone : RestoreAction.Unsupported
            : priorDocument is not null ? RestoreAction.Revert : RestoreAction.Unsupported;

        var title = TitleOf(heldDocument) ?? TitleOf(priorDocument)
            ?? (revision.Key.Kind == "roadmap-plan" ? "Roadmap plan" : revision.Key.Id);

        return new RestoreStep(revision.Key, title, documentAction, conflict);
    }

    // --- Writing ------------------------------------------------------------

    /// <summary>Writes one revision back, or keeps the document: true when it was
    /// written.</summary>
    private async Task<bool> ApplyAsync(Revision revision, bool restoreAnyway, CancellationToken cancellationToken)
    {
        string? held;
        await using (var connection = await SqliteTaskRepository.OpenForJournalAsync(_databasePath, cancellationToken).ConfigureAwait(false))
        {
            held = await HeldStampAsync(connection, revision.Key, cancellationToken).ConfigureAwait(false);

            if (!restoreAnyway && !string.Equals(held, revision.AfterUpdatedAt, StringComparison.Ordinal)) return false;

            if (revision.Key.Kind == TaskKind)
            {
                return await ApplyTaskAsync(connection, revision, cancellationToken).ConfigureAwait(false);
            }
        }

        if (!Tables.TryGetValue(revision.Key.Kind, out var table)) return false;

        if (revision.Before is null)
        {
            // Only a goal has a tombstone among these kinds (CanTombstone); the
            // tombstone is conditional on the stamp just checked.
            if (!table.CanTombstone || !Guid.TryParse(revision.Key.Id, out var goalId)) return false;

            return await new SqliteGoalRepository(_rootDirectory)
                .TombstoneAsync(goalId, held, cancellationToken).ConfigureAwait(false);
        }

        return DocumentOf(revision.Before) is { } document
            && await WriteDocumentAsync(table, revision, document, held, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> ApplyTaskAsync(SqliteConnection connection, Revision revision, CancellationToken cancellationToken)
    {
        var current = await SqliteTaskRepository.ReadCurrentAsync(connection, revision.Key.Id, cancellationToken).ConfigureAwait(false);

        try
        {
            if (revision.Before is null)
            {
                // Created by the change set: tombstoned, so the deletion travels. One
                // already gone or already a tombstone has nothing left to take back.
                if (current is null || current.DeletedAt is not null) return false;

                current.MarkDeleted();
                await _tasks.SaveAsync(current, cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (await SqliteTaskRepository.ReadBeforeImageAsync(connection, revision.Before, cancellationToken).ConfigureAwait(false) is not { } prior)
            {
                return false;
            }

            // Saved against the version held now, and stamped past it; a row gone
            // altogether is written back as it was, stamped now.
            prior.Supersede(current?.UpdatedAt ?? prior.UpdatedAt, current is null ? prior.DeletedAt : current.DeletedAt);
            await _tasks.SaveAsync(prior, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (StaleTaskWriteException)
        {
            // Somebody saved the task between the check and this write: kept.
            return false;
        }
    }

    /// <summary>Writes <paramref name="document"/> back into <paramref name="table"/>,
    /// stamped now — or one tick past the held stamp when the clock says otherwise —
    /// and only while the row still holds <paramref name="held"/>.</summary>
    private async Task<bool> WriteDocumentAsync(
        DocumentTable table,
        Revision revision,
        string document,
        string? held,
        CancellationToken cancellationToken)
    {
        await using var connection = await SqliteJournal
            .OpenJournalledAsync(_databasePath, table.Schema, table.Journal, cancellationToken).ConfigureAwait(false);

        var stamp = _time.GetUtcNow();
        if (held is not null
            && DateTimeOffset.TryParse(held, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var heldStamp)
            && stamp <= heldStamp)
        {
            stamp = heldStamp.AddTicks(1);
        }

        var stampText = stamp.ToString("O", CultureInfo.InvariantCulture);

        await using var journal = await ChangeSetTransaction.BeginAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = journal.Transaction;

        if (held is null)
        {
            command.CommandText = table.HasCreatedAt
                ? $"INSERT OR IGNORE INTO {table.Name} (id, document, created_at, updated_at) VALUES ($id, $document, $created_at, $updated_at);"
                : $"INSERT OR IGNORE INTO {table.Name} (id, document, updated_at) VALUES ($id, $document, $updated_at);";
            if (table.HasCreatedAt) command.Parameters.AddWithValue("$created_at", FieldOf(revision.Before, "created_at") ?? stampText);
        }
        else
        {
            command.CommandText = $"UPDATE {table.Name} SET document = $document, updated_at = $updated_at WHERE id = $id AND updated_at = $held;";
            command.Parameters.AddWithValue("$held", held);
        }

        command.Parameters.AddWithValue("$id", revision.Key.Id);
        command.Parameters.AddWithValue("$document", document);
        command.Parameters.AddWithValue("$updated_at", stampText);

        var written = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (written == 0) return false;

        await journal.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task MarkUndoneAsync(Guid changeSetId, Guid restoreId, CancellationToken cancellationToken)
    {
        await using var connection = await SqliteTaskRepository.OpenForJournalAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE change_sets SET undone_by = $restore WHERE id = $id;";
        command.Parameters.AddWithValue("$restore", restoreId.ToString());
        command.Parameters.AddWithValue("$id", changeSetId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // --- Reading ------------------------------------------------------------

    private static async Task<Header?> ReadHeaderAsync(SqliteConnection connection, Guid changeSetId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT label, undone_by FROM change_sets WHERE id = $id;";
        command.Parameters.AddWithValue("$id", changeSetId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

        return new Header(
            reader.IsDBNull(0) ? null : reader.GetString(0),
            !reader.IsDBNull(1) && Guid.TryParse(reader.GetString(1), out var undoneBy) ? undoneBy : null);
    }

    /// <summary>The change set's revisions, newest first.</summary>
    private static async Task<IReadOnlyList<Revision>> ReadRevisionsAsync(SqliteConnection connection, Guid changeSetId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT document_kind, document_id, before, after_updated_at FROM revisions
            WHERE change_set_id = $id
            ORDER BY sequence DESC;
            """;
        command.Parameters.AddWithValue("$id", changeSetId.ToString());

        var revisions = new List<Revision>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            revisions.Add(new Revision(
                new DocumentKey(reader.GetString(0), reader.GetString(1)),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return revisions;
    }

    /// <summary>The stamp the document's row holds now, as stored text — a task's
    /// coalesced to its <c>created_at</c> as its store reads it — or null when there is
    /// no such row.</summary>
    private static async Task<string?> HeldStampAsync(SqliteConnection connection, DocumentKey key, CancellationToken cancellationToken)
    {
        string table, stamp;
        if (key.Kind == TaskKind)
        {
            (table, stamp) = ("tasks", "COALESCE(updated_at, created_at)");
        }
        else if (Tables.TryGetValue(key.Kind, out var documentTable))
        {
            if (!await TableExistsAsync(connection, documentTable.Name, cancellationToken).ConfigureAwait(false)) return null;
            (table, stamp) = (documentTable.Name, "updated_at");
        }
        else
        {
            return null;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {stamp} FROM {table} WHERE id = $id;";
        command.Parameters.AddWithValue("$id", key.Id);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
    }

    private static async Task<string?> HeldDocumentAsync(SqliteConnection connection, DocumentTable table, string id, CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, table.Name, cancellationToken).ConfigureAwait(false)) return null;

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT document FROM {table.Name} WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", table);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    /// <summary>The stored document a before-image holds, or null.</summary>
    private static string? DocumentOf(string? before) => FieldOf(before, "document");

    private static string? FieldOf(string? image, string field)
    {
        if (image is null) return null;

        try
        {
            using var parsed = JsonDocument.Parse(image);
            return parsed.RootElement.ValueKind is JsonValueKind.Object
                && parsed.RootElement.TryGetProperty(field, out var value)
                && value.ValueKind is JsonValueKind.String
                    ? value.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TitleOf(string? document) => FieldOf(document, "title") is { Length: > 0 } title ? title : null;

    private sealed record Header(string? Label, Guid? UndoneBy);

    private sealed record Revision(DocumentKey Key, string? Before, string? AfterUpdatedAt);

    private sealed record DocumentTable(
        string Name,
        SqliteSchemaDefinition Schema,
        SqliteSchemaDefinition Journal,
        bool HasCreatedAt,
        bool CanTombstone);
}

/// <summary>An <see cref="IChangeRestore"/> that follows a folder somebody can move,
/// the way <see cref="RootedSqliteChangeJournal"/> does.</summary>
public sealed class RootedSqliteChangeRestore(Func<string> currentRootDirectory, ITaskRepository tasks) : IChangeRestore
{
    private readonly Func<string> _currentRootDirectory =
        currentRootDirectory ?? throw new ArgumentNullException(nameof(currentRootDirectory));

    private readonly ITaskRepository _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));

    public Task<RestorePlan?> PlanAsync(Guid changeSetId, CancellationToken cancellationToken = default) =>
        Current.PlanAsync(changeSetId, cancellationToken);

    public Task<RestoreOutcome> RestoreAsync(Guid changeSetId, IReadOnlySet<DocumentKey> restoreAnyway, CancellationToken cancellationToken = default) =>
        Current.RestoreAsync(changeSetId, restoreAnyway, cancellationToken);

    private SqliteChangeRestore Current => new(_currentRootDirectory(), _tasks);
}
