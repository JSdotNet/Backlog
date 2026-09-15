using Backlog.Modules.Inbox;
using Backlog.Modules.Inbox.DomainModels;

namespace Backlog.Infrastructure.Sqlite.Inbox;

/// <summary>
/// An inbox store that follows a folder somebody can move.
/// <para>
/// The desktop lets you point the app at a different workspace root while it
/// is running. Handlers should not have to know that, and the container cannot
/// re-resolve a singleton on a settings change, so the current root is read per
/// call and the underlying repository is rebuilt only when it actually changes —
/// the same arrangement as <see cref="RootedSqliteTaskRepository"/>, which
/// follows the same root to the same file.
/// </para>
/// </summary>
public sealed class RootedSqliteInboxRepository(Func<string> currentRootDirectory)
    : IInboxItemRepository, IInboxOrganizerRepository
{
    private readonly Func<string> _currentRootDirectory =
        currentRootDirectory ?? throw new ArgumentNullException(nameof(currentRootDirectory));

    private string? _rootDirectory;
    private SqliteInboxRepository? _repository;

    /// <summary>The database the repository is pointed at right now.</summary>
    public string DatabasePath => Current.DatabasePath;

    private SqliteInboxRepository Current
    {
        get
        {
            var root = _currentRootDirectory();

            if (_repository is null
                || !string.Equals(_rootDirectory, root, StringComparison.OrdinalIgnoreCase))
            {
                _rootDirectory = root;
                _repository = new SqliteInboxRepository(root);
            }

            return _repository;
        }
    }

    public Task SaveAsync(InboxItem item, CancellationToken cancellationToken = default) =>
        Current.SaveAsync(item, cancellationToken);

    public Task<InboxItem?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Current.GetAsync(id, cancellationToken);

    public Task<IReadOnlyList<InboxItem>> ListAsync(CancellationToken cancellationToken = default) =>
        Current.ListAsync(cancellationToken);

    public Task<IReadOnlyList<InboxItem>> ListPendingReplicaAckAsync(CancellationToken cancellationToken = default) =>
        Current.ListPendingReplicaAckAsync(cancellationToken);

    public Task<IReadOnlyList<InboxList>> ListListsAsync(CancellationToken cancellationToken = default) =>
        Current.ListListsAsync(cancellationToken);

    public Task<IReadOnlyList<InboxGroup>> ListGroupsAsync(CancellationToken cancellationToken = default) =>
        Current.ListGroupsAsync(cancellationToken);

    public Task SaveListAsync(InboxList list, CancellationToken cancellationToken = default) =>
        Current.SaveListAsync(list, cancellationToken);

    public Task DeleteListAsync(Guid id, CancellationToken cancellationToken = default) =>
        Current.DeleteListAsync(id, cancellationToken);

    public Task SaveGroupAsync(InboxGroup group, CancellationToken cancellationToken = default) =>
        Current.SaveGroupAsync(group, cancellationToken);

    public Task DeleteGroupAsync(Guid id, CancellationToken cancellationToken = default) =>
        Current.DeleteGroupAsync(id, cancellationToken);
}
