using Backlog.SharedKernel.Changes;

namespace Backlog.Infrastructure.Sqlite.Changes;

/// <summary>
/// An <see cref="IChangeJournal"/> that follows a folder somebody can move, the way
/// <see cref="RootedSqliteTaskRepository"/> does: the root is read per call, so the
/// journal read is always the one beside the tasks the app is writing.
/// </summary>
public sealed class RootedSqliteChangeJournal(Func<string> currentRootDirectory) : IChangeJournal
{
    private readonly Func<string> _currentRootDirectory =
        currentRootDirectory ?? throw new ArgumentNullException(nameof(currentRootDirectory));

    public Task<IReadOnlyList<ChangeSetEntry>> ListAsync(
        string? sessionId = null,
        int limit = 200,
        CancellationToken cancellationToken = default) =>
        Current.ListAsync(sessionId, limit, cancellationToken);

    public Task<IReadOnlyList<ChangeSetEntry>> QueryAsync(ChangeSetQuery query, CancellationToken cancellationToken = default) =>
        Current.QueryAsync(query, cancellationToken);

    public Task<ChangeSetEntry?> FindAsync(Guid changeSetId, CancellationToken cancellationToken = default) =>
        Current.FindAsync(changeSetId, cancellationToken);

    public Task<IReadOnlyList<ChangedDocument>> ReadAsync(Guid changeSetId, CancellationToken cancellationToken = default) =>
        Current.ReadAsync(changeSetId, cancellationToken);

    private SqliteChangeJournal Current =>
        new(SqliteTaskRepository.DatabasePathFor(_currentRootDirectory()));
}
