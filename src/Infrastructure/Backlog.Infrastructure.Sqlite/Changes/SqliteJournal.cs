using System.Text;
using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.Changes;

/// <summary>
/// The change journal's <c>revisions</c> and <c>journal_context</c> tables (local
/// ADR 0061 §2), and the triggers that fill <c>revisions</c> from a journalled table.
/// <para>
/// The triggers, not the repositories, write the revisions, so a write from any
/// process is caught. The app names its change set in the single
/// <c>journal_context</c> row inside the write's own transaction and deletes the row
/// before it commits, so the row is never seen outside that transaction. A write that
/// finds the table empty — another process, <c>sqlite3</c> — is recorded under an
/// unknown-writer change set the trigger creates on the spot, one per document
/// touched. A row with <c>skip</c> set keeps the write out of the journal.
/// </para>
/// <para>
/// Each change set keeps one revision per document: the document as it was before
/// the change set first touched it, null when the change set created it. A later
/// touch only moves the revision's <c>after_updated_at</c> on.
/// </para>
/// </summary>
internal static class SqliteJournal
{
    internal static readonly SqliteSchemaDefinition Schema = new(
        owner: "journal",
        ddl: """
            CREATE TABLE IF NOT EXISTS revisions (
                sequence         INTEGER PRIMARY KEY AUTOINCREMENT,
                change_set_id    TEXT NOT NULL,
                document_kind    TEXT NOT NULL,
                document_id      TEXT NOT NULL,
                before           TEXT NULL,
                after_updated_at TEXT NULL,
                recorded_at      TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_revisions_change_set_document
                ON revisions (change_set_id, document_kind, document_id);
            CREATE INDEX IF NOT EXISTS ix_revisions_document ON revisions (document_kind, document_id);

            CREATE TABLE IF NOT EXISTS journal_context (
                id            INTEGER PRIMARY KEY NOT NULL CHECK (id = 1),
                change_set_id TEXT NULL,
                skip          INTEGER NOT NULL DEFAULT 0
            );
            """);

    /// <summary>Now, in the <c>"O"</c> UTC form every instant in this file is stored
    /// in, so the trigger's timestamps sort with the app's.</summary>
    private const string Now = "(strftime('%Y-%m-%dT%H:%M:%f', 'now') || '0000+00:00')";

    /// <summary>A version-4 GUID in its usual text form, for a change set a trigger
    /// creates.</summary>
    private const string NewGuid =
        "lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)), 2) || '-' " +
        "|| substr('89ab', 1 + (abs(random()) % 4), 1) || substr(hex(randomblob(2)), 2) || '-' || hex(randomblob(6)))";

    /// <summary>Sets up the journal's tables, and the change sets they point at, on a
    /// connection a journalled table's owner opened. Outside any transaction.</summary>
    internal static async Task EnsureAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await SqliteChangeSets.EnsureAsync(connection, cancellationToken).ConfigureAwait(false);
        await SqliteSchema.EnsureSetUpAsync(connection, Schema, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Opens <paramref name="databasePath"/> set up for
    /// <paramref name="schema"/>, then the journal's tables and
    /// <paramref name="triggers"/> — after the table, because the triggers name its
    /// columns. How a journalled table's repository opens its connection.</summary>
    internal static async Task<SqliteConnection> OpenJournalledAsync(
        string databasePath,
        SqliteSchemaDefinition schema,
        SqliteSchemaDefinition triggers,
        CancellationToken cancellationToken)
    {
        var connection = await SqliteSchema.OpenAsync(databasePath, schema, cancellationToken).ConfigureAwait(false);

        try
        {
            await EnsureAsync(connection, cancellationToken).ConfigureAwait(false);
            await SqliteSchema.EnsureSetUpAsync(connection, triggers, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Names the change set the rest of <paramref name="transaction"/> writes
    /// under, or marks it as kept out of the journal when
    /// <paramref name="changeSetId"/> is null.</summary>
    internal static async Task EnterAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid? changeSetId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO journal_context (id, change_set_id, skip) VALUES (1, $change_set_id, $skip)
            ON CONFLICT(id) DO UPDATE SET change_set_id = excluded.change_set_id, skip = excluded.skip;
            """;
        command.Parameters.AddWithValue("$change_set_id", (object?)changeSetId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$skip", changeSetId is null ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Empties <c>journal_context</c> before <paramref name="transaction"/>
    /// commits, so no other writer ever finds this one's change set there.</summary>
    internal static async Task LeaveAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM journal_context;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The four triggers that journal <paramref name="table"/>: after an insert and
    /// after an update, each once for a write the app named and once for one it did
    /// not. Dropped and created again, so the setup is idempotent and a column added
    /// to the table — which changes <paramref name="columns"/> and with it the
    /// owner's fingerprint — reaches the before-image.
    /// </summary>
    /// <param name="table">The journalled table; a compile-time constant.</param>
    /// <param name="documentKind">What <c>revisions.document_kind</c> calls its rows.</param>
    /// <param name="columns">Every column of the table, the before-image's keys.</param>
    internal static string Triggers(string table, string documentKind, IReadOnlyList<string> columns)
    {
        var before = new StringBuilder("json_object(");
        for (var i = 0; i < columns.Count; i++)
        {
            if (i > 0) before.Append(", ");
            before.Append('\'').Append(columns[i]).Append("', OLD.").Append(columns[i]);
        }

        before.Append(')');

        var sql = new StringBuilder();
        foreach (var (operation, image) in new[] { ("insert", "NULL"), ("update", before.ToString()) })
        {
            var named = $"{table}_journal_{operation}";
            var unnamed = $"{table}_journal_{operation}_unknown";

            sql.Append($"""
                DROP TRIGGER IF EXISTS {named};
                CREATE TRIGGER {named} AFTER {operation.ToUpperInvariant()} ON {table}
                WHEN EXISTS (SELECT 1 FROM journal_context WHERE change_set_id IS NOT NULL AND skip = 0)
                BEGIN
                    INSERT INTO revisions (change_set_id, document_kind, document_id, before, after_updated_at, recorded_at)
                    SELECT change_set_id, '{documentKind}', NEW.id, {image}, NEW.updated_at, {Now}
                    FROM journal_context WHERE id = 1
                    ON CONFLICT (change_set_id, document_kind, document_id)
                    DO UPDATE SET after_updated_at = excluded.after_updated_at;
                END;

                DROP TRIGGER IF EXISTS {unnamed};
                CREATE TRIGGER {unnamed} AFTER {operation.ToUpperInvariant()} ON {table}
                WHEN NOT EXISTS (SELECT 1 FROM journal_context)
                BEGIN
                    INSERT INTO change_sets (id, actor_kind, started_at) VALUES ({NewGuid}, 'unknown', {Now});
                    INSERT INTO revisions (change_set_id, document_kind, document_id, before, after_updated_at, recorded_at)
                    SELECT id, '{documentKind}', NEW.id, {image}, NEW.updated_at, {Now}
                    FROM change_sets WHERE rowid = last_insert_rowid();
                END;

                """);
        }

        return sql.ToString();
    }
}
