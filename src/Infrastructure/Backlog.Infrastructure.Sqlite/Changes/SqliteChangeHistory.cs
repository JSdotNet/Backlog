using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.Changes;

/// <summary>
/// Keeps the change journal bounded (local ADR 0061 §6): drops the revisions and
/// change sets older than the retention period, or all of them when the person
/// clears the history.
/// <para>
/// A change set goes whole: its revisions are deleted together once the newest of
/// them is older than the cutoff, and the change set with them. One that went on
/// joining writes past the cutoff keeps every revision until the last ages out, so a
/// restore never meets a change set missing part of what it replaced.
/// </para>
/// <para>
/// Deleting leaves the freed pages inside the file, and a backup copies the file
/// whole (local ADR 0010). So a purge that deleted anything is followed by
/// <c>VACUUM</c> and a truncating checkpoint, and the file on disk shrinks.
/// </para>
/// <para>
/// A database file that is not there yet is left alone: there is no history in it,
/// and creating one to delete nothing would put an empty backlog where the person
/// expects none.
/// </para>
/// </summary>
public static class SqliteChangeHistory
{
    /// <summary>Deletes every change set whose newest revision was recorded before
    /// <paramref name="cutoff"/>, with all its revisions, and every change set started
    /// before it that holds none, in one transaction.</summary>
    public static async Task<ChangeHistoryPurge> PruneAsync(
        string databasePath,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        if (!File.Exists(databasePath)) return ChangeHistoryPurge.None;

        var instant = cutoff.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        return await PurgeAsync(
            databasePath,
            """
            DELETE FROM revisions
            WHERE change_set_id IN (
                SELECT change_set_id FROM revisions
                GROUP BY change_set_id
                HAVING max(recorded_at) < $cutoff);
            """,
            """
            DELETE FROM change_sets
            WHERE started_at < $cutoff
              AND NOT EXISTS (SELECT 1 FROM revisions WHERE revisions.change_set_id = change_sets.id);
            """,
            instant,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes every revision and every change set, in one transaction. It
    /// cannot be undone: nothing else keeps them.</summary>
    public static async Task<ChangeHistoryPurge> ClearAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        if (!File.Exists(databasePath)) return ChangeHistoryPurge.None;

        return await PurgeAsync(
            databasePath,
            "DELETE FROM revisions;",
            "DELETE FROM change_sets;",
            cutoff: null,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ChangeHistoryPurge> PurgeAsync(
        string databasePath,
        string revisionsSql,
        string changeSetsSql,
        string? cutoff,
        CancellationToken cancellationToken)
    {
        await using var connection = await SqliteSchema
            .OpenAsync(databasePath, SqliteChangeSets.Schema, cancellationToken).ConfigureAwait(false);
        await SqliteJournal.EnsureAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var revisions = await ExecuteAsync(connection, transaction, revisionsSql, cutoff, cancellationToken).ConfigureAwait(false);
        var changeSets = await ExecuteAsync(connection, transaction, changeSetsSql, cutoff, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        if (revisions + changeSets > 0)
        {
            // Outside the transaction: VACUUM refuses to run inside one. The
            // checkpoint folds the rewritten pages back and truncates the log, so
            // the file's own size is what was freed.
            await using var vacuum = connection.CreateCommand();
            vacuum.CommandText = "VACUUM; PRAGMA wal_checkpoint(TRUNCATE);";
            await vacuum.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return new ChangeHistoryPurge(revisions, changeSets);
    }

    private static async Task<int> ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        string? cutoff,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        if (cutoff is not null) command.Parameters.AddWithValue("$cutoff", cutoff);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>How many revisions and change sets one prune or clear deleted.</summary>
public sealed record ChangeHistoryPurge(int Revisions, int ChangeSets)
{
    /// <summary>Nothing deleted.</summary>
    public static ChangeHistoryPurge None { get; } = new(0, 0);
}
