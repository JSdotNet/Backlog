using Backlog.SharedKernel.Changes;
using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.Changes;

/// <summary>
/// The transaction a single-statement write of a journalled table runs in while a
/// change set is current: <see cref="SqliteChangeSets"/> records the change set in
/// it, and the write joins it (local ADR 0061 §2). With no change set current there
/// is no transaction, so the write stays the one statement it always was and
/// <see cref="Transaction"/> is null.
/// </summary>
internal sealed class ChangeSetTransaction : IAsyncDisposable
{
    private ChangeSetTransaction(SqliteTransaction? transaction) => Transaction = transaction;

    /// <summary>The transaction for the write's command, or null when none is
    /// needed.</summary>
    public SqliteTransaction? Transaction { get; }

    /// <summary>Opens the transaction and records the current change set in it, or
    /// does nothing when there is none. Call it before any other statement of the
    /// write's own that should commit with it.</summary>
    public static async Task<ChangeSetTransaction> BeginAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        if (ChangeContext.Current is null) return new ChangeSetTransaction(null);

        await SqliteChangeSets.EnsureAsync(connection, cancellationToken).ConfigureAwait(false);
        var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await SqliteChangeSets.RecordAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return new ChangeSetTransaction(transaction);
    }

    public Task CommitAsync(CancellationToken cancellationToken) =>
        Transaction?.CommitAsync(cancellationToken) ?? Task.CompletedTask;

    public ValueTask DisposeAsync() => Transaction?.DisposeAsync() ?? ValueTask.CompletedTask;
}
