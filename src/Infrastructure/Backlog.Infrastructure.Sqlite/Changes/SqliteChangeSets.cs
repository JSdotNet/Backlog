using System.Globalization;
using Backlog.SharedKernel.Changes;
using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.Changes;

/// <summary>
/// The change journal's <c>change_sets</c> table (local ADR 0061 §2), shared across
/// bounded contexts the way Sync is, because one change set can span several.
/// <para>
/// A change set is recorded by the first journalled write made under it, inside that
/// write's own transaction, so a change set that wrote nothing leaves no row and one
/// that did can never be missing. Later writes under the same change set find the
/// row there and leave it as it is. The revisions that say what each change set
/// replaced come later in the plan; this records who wrote and when.
/// </para>
/// </summary>
internal static class SqliteChangeSets
{
    internal static readonly SqliteSchemaDefinition Schema = new(
        owner: "change_sets",
        ddl: """
            CREATE TABLE IF NOT EXISTS change_sets (
                id             TEXT PRIMARY KEY NOT NULL,
                actor_kind     TEXT NOT NULL,
                caller         TEXT NULL,
                session_id     TEXT NULL,
                correlation_id TEXT NULL,
                label          TEXT NULL,
                started_at     TEXT NOT NULL,
                undone_by      TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_change_sets_started_at ON change_sets (started_at);
            """);

    /// <summary>Sets the table up on a connection a journalled table's owner opened.
    /// Before the write's transaction begins.</summary>
    internal static Task EnsureAsync(SqliteConnection connection, CancellationToken cancellationToken) =>
        SqliteSchema.EnsureSetUpAsync(connection, Schema, cancellationToken);

    /// <summary>Records <see cref="ChangeContext.Current"/>, when there is one, in
    /// <paramref name="transaction"/>. A write with none records nothing.</summary>
    internal static async Task RecordAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (ChangeContext.Current is not { } changeSet) return;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO change_sets (id, actor_kind, caller, session_id, correlation_id, label, started_at)
            VALUES ($id, $actor_kind, $caller, $session_id, $correlation_id, $label, $started_at)
            ON CONFLICT(id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$id", changeSet.Id.ToString());
        command.Parameters.AddWithValue("$actor_kind", ChangeSet.ToWire(changeSet.Actor));
        command.Parameters.AddWithValue("$caller", (object?)changeSet.Caller ?? DBNull.Value);
        command.Parameters.AddWithValue("$session_id", (object?)changeSet.SessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$correlation_id", (object?)changeSet.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$label", (object?)changeSet.Label ?? DBNull.Value);
        command.Parameters.AddWithValue("$started_at", changeSet.StartedAt.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
