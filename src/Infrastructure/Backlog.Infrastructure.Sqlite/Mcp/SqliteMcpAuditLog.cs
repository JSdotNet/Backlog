using System.Globalization;

using Backlog.SharedKernel.Mcp;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.Mcp;

/// <summary>
/// The MCP audit log (local ADR 0052, Phase 0): one row per tool call in the
/// <c>mcp_calls</c> table of a database file of its own.
/// <para>
/// <b>Its own file, never <c>backlog.db</c>.</b> The log is this installation's record of
/// who called what here. <c>backlog.db</c> is the backlog: local ADR 0010 commits it to a
/// GitHub repository, and a person may move it with their workspace root. Neither should
/// carry this machine's call history, so the host names a per-installation path for it,
/// beside the device files.
/// </para>
/// <para>
/// <b>Thirty days, pruned on write.</b> Each append deletes every row older than
/// <see cref="McpAudit.Retention"/> in the same transaction, over the index on
/// <c>called_at</c>. No timer is needed, and a log nobody writes to stops growing.
/// </para>
/// </summary>
public sealed class SqliteMcpAuditLog : IMcpAuditLog
{
    private readonly string _databasePath;
    private readonly TimeProvider _time;

    /// <summary>A log over the database file at <paramref name="databasePath"/>,
    /// created with its folder on the first write.</summary>
    public SqliteMcpAuditLog(string databasePath, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _databasePath = databasePath;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The database file this log reads and writes.</summary>
    public string DatabasePath => _databasePath;

    public event Action? Changed;

    public async Task AppendAsync(McpAuditRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using (var connection = await OpenAsync(cancellationToken).ConfigureAwait(false))
        {
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText =
                    """
                    INSERT INTO mcp_calls (called_at, caller, tool, target_id, outcome, correlation_id)
                    VALUES ($called_at, $caller, $tool, $target_id, $outcome, $correlation_id);
                    """;
                insert.Parameters.AddWithValue("$called_at", Instant(record.At));
                insert.Parameters.AddWithValue("$caller", record.Caller);
                insert.Parameters.AddWithValue("$tool", record.Tool);
                insert.Parameters.AddWithValue("$target_id", (object?)record.TargetId ?? DBNull.Value);
                insert.Parameters.AddWithValue("$outcome", OutcomeText(record.Outcome));
                insert.Parameters.AddWithValue("$correlation_id", record.CorrelationId);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var prune = connection.CreateCommand())
            {
                prune.Transaction = transaction;
                prune.CommandText = "DELETE FROM mcp_calls WHERE called_at < $cutoff;";
                prune.Parameters.AddWithValue("$cutoff", Instant(_time.GetUtcNow() - McpAudit.Retention));
                await prune.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        RaiseChanged();
    }

    /// <summary>The record is committed by now, so a subscriber that throws must not
    /// make the write read as failed: each one is called on its own and its failure
    /// dropped.</summary>
    private void RaiseChanged()
    {
        if (Changed is not { } changed) return;

        foreach (var subscriber in changed.GetInvocationList().Cast<Action>())
        {
            try
            {
                subscriber();
            }
#pragma warning disable CA1031 // A listener's failure is not the log's.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }
    }

    public async Task<IReadOnlyList<McpAuditRecord>> LatestAsync(int count, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (count == 0) return [];

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // The cutoff on the read too, so a record past its thirty days is never shown
        // while the log waits for its next write to drop it.
        command.CommandText =
            """
            SELECT called_at, caller, tool, target_id, outcome, correlation_id
            FROM mcp_calls
            WHERE called_at >= $cutoff
            ORDER BY called_at DESC, id DESC
            LIMIT $count;
            """;
        command.Parameters.AddWithValue("$cutoff", Instant(_time.GetUtcNow() - McpAudit.Retention));
        command.Parameters.AddWithValue("$count", count);

        var records = new List<McpAuditRecord>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            records.Add(new McpAuditRecord(
                DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                Outcome(reader.GetString(4)),
                reader.GetString(5)));
        }

        return records;
    }

    /// <summary>Round-trip text in UTC, the form the tasks use: fixed width, so text
    /// order is time order and the index answers both the prune and the read.</summary>
    private static string Instant(DateTimeOffset at) =>
        at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string OutcomeText(McpCallOutcome outcome) => outcome switch
    {
        McpCallOutcome.Succeeded => "succeeded",
        McpCallOutcome.Failed => "failed",
        McpCallOutcome.Refused => "refused",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Not an MCP call outcome.")
    };

    /// <summary>A stored outcome this build does not know reads as failed: the row is
    /// still shown, and it is never shown as a success.</summary>
    private static McpCallOutcome Outcome(string text) => text switch
    {
        "succeeded" => McpCallOutcome.Succeeded,
        "refused" => McpCallOutcome.Refused,
        _ => McpCallOutcome.Failed
    };

    private Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken) =>
        SqliteSchema.OpenAsync(_databasePath, Schema, cancellationToken);

    internal static readonly SqliteSchemaDefinition Schema = new(
        owner: "mcp_calls",
        ddl: """
            CREATE TABLE IF NOT EXISTS mcp_calls (
                id             INTEGER PRIMARY KEY AUTOINCREMENT,
                called_at      TEXT NOT NULL,
                caller         TEXT NOT NULL,
                tool           TEXT NOT NULL,
                target_id      TEXT NULL,
                outcome        TEXT NOT NULL,
                correlation_id TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_mcp_calls_called_at ON mcp_calls (called_at);
            """);
}
