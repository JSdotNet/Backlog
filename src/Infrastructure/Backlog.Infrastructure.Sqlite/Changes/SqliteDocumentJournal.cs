using System.Globalization;
using Backlog.SharedKernel.Changes;
using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.Changes;

/// <summary>
/// <see cref="IDocumentRevisionJournal"/> over <c>backlog.db</c> in a folder somebody can
/// move: the revisions a JSON file store records before it writes (local ADR 0061 §2), in
/// the same <c>revisions</c> and <c>change_sets</c> tables the triggers fill, by the same
/// rules — one revision per document per change set, keeping the document as it was before
/// the change set first touched it, and an unknown writer's change set for a write no entry
/// point named.
/// <para>
/// A journal that cannot be written records nothing and says so with null: the remark or
/// the state still lands, which costs its history rather than the person's write.
/// </para>
/// </summary>
public sealed class SqliteDocumentJournal(Func<string> currentRootDirectory) : IDocumentRevisionJournal
{
    private readonly Func<string> _currentRootDirectory =
        currentRootDirectory ?? throw new ArgumentNullException(nameof(currentRootDirectory));

    /// <summary>A journal over one fixed folder, for a test or a harness.</summary>
    public SqliteDocumentJournal(string rootDirectory)
        : this(() => rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
    }

    public JournalRevision? Record(string documentKind, string documentId, string? before, DateTimeOffset? afterUpdatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

        if (ChangeContext.IsUnjournalled) return null;

        try
        {
            // Waited on under the caller's lock. Run on the pool, so a caller on the UI
            // thread never waits on a continuation posted back to itself; the change set
            // flows with the execution context.
            return Task.Run(() => RecordAsync(documentKind, documentId, before, JournalStamp.Format(afterUpdatedAt)))
                .GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Withdraw(JournalRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);

        try
        {
            Task.Run(() => WithdrawAsync(revision)).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            // Nothing else to do: the revision names a write the file does not hold, and
            // a restore of it finds the stamp does not match and keeps the document.
        }
    }

    private string DatabasePath => SqliteTaskRepository.DatabasePathFor(_currentRootDirectory());

    private async Task<JournalRevision> RecordAsync(string kind, string id, string? before, string? after)
    {
        await using var connection = await SqliteTaskRepository.OpenForJournalAsync(DatabasePath, CancellationToken.None).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);

        var changeSet = ChangeContext.Current;
        Guid changeSetId;
        bool created;

        if (changeSet is not null)
        {
            changeSetId = changeSet.Id;
            created = !await ChangeSetExistsAsync(connection, transaction, changeSetId).ConfigureAwait(false);
            if (created) await SqliteChangeSets.RecordAsync(connection, transaction, CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            // As the unknown-writer trigger does: a change set of its own per document.
            changeSetId = Guid.CreateVersion7();
            created = true;

            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO change_sets (id, actor_kind, started_at) VALUES ($id, 'unknown', $now);";
            insert.Parameters.AddWithValue("$id", changeSetId.ToString());
            insert.Parameters.AddWithValue("$now", JournalStamp.Format(DateTimeOffset.UtcNow));
            await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        JournalRevision revision;

        await using (var find = connection.CreateCommand())
        {
            find.Transaction = transaction;
            find.CommandText = """
                SELECT sequence, after_updated_at FROM revisions
                WHERE change_set_id = $change_set_id AND document_kind = $kind AND document_id = $id;
                """;
            find.Parameters.AddWithValue("$change_set_id", changeSetId.ToString());
            find.Parameters.AddWithValue("$kind", kind);
            find.Parameters.AddWithValue("$id", id);

            await using var reader = await find.ExecuteReaderAsync().ConfigureAwait(false);
            revision = await reader.ReadAsync().ConfigureAwait(false)
                ? new JournalRevision(changeSetId, reader.GetInt64(0), false, reader.IsDBNull(1) ? null : reader.GetString(1), created)
                : new JournalRevision(changeSetId, 0, true, null, created);
        }

        await using (var write = connection.CreateCommand())
        {
            write.Transaction = transaction;
            write.Parameters.AddWithValue("$after", (object?)after ?? DBNull.Value);

            if (revision.Inserted)
            {
                write.CommandText = """
                    INSERT INTO revisions (change_set_id, document_kind, document_id, before, after_updated_at, recorded_at)
                    VALUES ($change_set_id, $kind, $id, $before, $after, $now);
                    SELECT last_insert_rowid();
                    """;
                write.Parameters.AddWithValue("$change_set_id", changeSetId.ToString());
                write.Parameters.AddWithValue("$kind", kind);
                write.Parameters.AddWithValue("$id", id);
                write.Parameters.AddWithValue("$before", (object?)before ?? DBNull.Value);
                write.Parameters.AddWithValue("$now", JournalStamp.Format(DateTimeOffset.UtcNow));

                var sequence = Convert.ToInt64(await write.ExecuteScalarAsync().ConfigureAwait(false), CultureInfo.InvariantCulture);
                revision = revision with { Sequence = sequence };
            }
            else
            {
                write.CommandText = "UPDATE revisions SET after_updated_at = $after WHERE sequence = $sequence;";
                write.Parameters.AddWithValue("$sequence", revision.Sequence);
                await write.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync().ConfigureAwait(false);
        return revision;
    }

    private async Task WithdrawAsync(JournalRevision revision)
    {
        await using var connection = await SqliteTaskRepository.OpenForJournalAsync(DatabasePath, CancellationToken.None).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            if (revision.Inserted)
            {
                command.CommandText = "DELETE FROM revisions WHERE sequence = $sequence;";
            }
            else
            {
                command.CommandText = "UPDATE revisions SET after_updated_at = $after WHERE sequence = $sequence;";
                command.Parameters.AddWithValue("$after", (object?)revision.PreviousAfterUpdatedAt ?? DBNull.Value);
            }

            command.Parameters.AddWithValue("$sequence", revision.Sequence);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        if (revision.ChangeSetCreated)
        {
            // Only a change set this write recorded, and only once nothing else filed
            // under it since.
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                DELETE FROM change_sets
                WHERE id = $id AND NOT EXISTS (SELECT 1 FROM revisions WHERE change_set_id = $id);
                """;
            command.Parameters.AddWithValue("$id", revision.ChangeSetId.ToString());
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);
    }

    private static async Task<bool> ChangeSetExistsAsync(SqliteConnection connection, SqliteTransaction transaction, Guid id)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM change_sets WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        return await command.ExecuteScalarAsync().ConfigureAwait(false) is not null;
    }
}
