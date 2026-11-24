using Backlog.SharedKernel.Changes;
using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.Changes;

/// <summary>
/// The transaction a write of a journalled table runs in (local ADR 0061 §2). While
/// a change set is current it records the change set and names it in
/// <c>journal_context</c>, so the table's triggers file their revisions under it;
/// inside <see cref="ChangeContext.Unjournalled"/> it marks the write as kept out of
/// the journal instead. <see cref="CommitAsync"/> empties <c>journal_context</c>
/// before it commits.
/// <para>
/// With neither, and no transaction asked for, there is none: the write stays the
/// one statement it always was, <see cref="Transaction"/> is null, and the triggers
/// record it under an unknown writer.
/// </para>
/// </summary>
internal sealed class ChangeSetTransaction : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly bool _journalled;

    private ChangeSetTransaction(SqliteConnection connection, SqliteTransaction? transaction, bool journalled)
    {
        _connection = connection;
        Transaction = transaction;
        _journalled = journalled;
    }

    /// <summary>The transaction for the write's commands, or null when none is
    /// needed.</summary>
    public SqliteTransaction? Transaction { get; }

    /// <summary>Opens the transaction and names the current change set in it, or does
    /// nothing when there is none and <paramref name="requireTransaction"/> is false.
    /// Call it before any other statement of the write's own that should commit with
    /// it.</summary>
    public static Task<ChangeSetTransaction> BeginAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken) =>
        BeginAsync(connection, requireTransaction: false, cancellationToken);

    /// <inheritdoc cref="BeginAsync(SqliteConnection, CancellationToken)"/>
    public static async Task<ChangeSetTransaction> BeginAsync(
        SqliteConnection connection,
        bool requireTransaction,
        CancellationToken cancellationToken)
    {
        var unjournalled = ChangeContext.IsUnjournalled;
        var changeSet = unjournalled ? null : ChangeContext.Current;
        var journalled = unjournalled || changeSet is not null;

        if (!journalled && !requireTransaction) return new ChangeSetTransaction(connection, null, false);

        if (journalled) await SqliteJournal.EnsureAsync(connection, cancellationToken).ConfigureAwait(false);

        var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (changeSet is not null)
            {
                await SqliteChangeSets.RecordAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            }

            if (journalled)
            {
                await SqliteJournal.EnterAsync(connection, transaction, changeSet?.Id, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return new ChangeSetTransaction(connection, transaction, journalled);
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (Transaction is null) return;

        if (_journalled)
        {
            await SqliteJournal.LeaveAsync(_connection, Transaction, cancellationToken).ConfigureAwait(false);
        }

        await Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => Transaction?.DisposeAsync() ?? ValueTask.CompletedTask;
}
