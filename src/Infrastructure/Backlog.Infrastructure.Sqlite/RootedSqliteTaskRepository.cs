using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.DomainModels;

namespace Backlog.Infrastructure.Sqlite;

/// <summary>
/// An <see cref="ITaskRepository"/> that follows a folder somebody can move.
/// <para>
/// The desktop lets you point the app at a different workspace root while it is
/// running. Handlers should not have to know that, and the container cannot
/// re-resolve a singleton on a settings change, so the current root is read per
/// call and the underlying repository is rebuilt only when it actually changes.
/// </para>
/// <para>
/// It is also where a local write is announced. This is the repository every
/// head registers, so every write on the machine - a person's edit, a drag, an
/// import, and the sync merge applying another device's document - passes
/// through <see cref="SaveAsync"/> here, which makes it the one place a
/// <see cref="ITaskChangeSignal"/> can be raised without a handler being
/// forgotten. The merge silences the signal around its own writes; see that
/// interface for why. Raised after the write and never before it, so a listener
/// that reads the store on the signal finds the row already there.
/// </para>
/// </summary>
public sealed class RootedSqliteTaskRepository(
    Func<string> currentRootDirectory,
    ITaskChangeSignal? changes = null) : ITaskRepository
{
    private readonly Func<string> _currentRootDirectory =
        currentRootDirectory ?? throw new ArgumentNullException(nameof(currentRootDirectory));

    private readonly ITaskChangeSignal? _changes = changes;

    private string? _rootDirectory;
    private SqliteTaskRepository? _repository;

    /// <summary>The database the repository is pointed at right now.</summary>
    public string DatabasePath => Current.DatabasePath;

    private SqliteTaskRepository Current
    {
        get
        {
            var root = _currentRootDirectory();

            if (_repository is null
                || !string.Equals(_rootDirectory, root, StringComparison.OrdinalIgnoreCase))
            {
                _rootDirectory = root;
                _repository = new SqliteTaskRepository(root);
            }

            return _repository;
        }
    }

    public async Task SaveAsync(TaskItem task, CancellationToken cancellationToken = default)
    {
        await Current.SaveAsync(task, cancellationToken).ConfigureAwait(false);

        _changes?.Raise();
    }

    public Task<TaskItem?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Current.GetAsync(id, cancellationToken);

    public Task<TaskItem?> GetIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default) =>
        Current.GetIncludingDeletedAsync(id, cancellationToken);

    public Task<IReadOnlyList<TaskItem>> ListAsync(CancellationToken cancellationToken = default) =>
        Current.ListAsync(cancellationToken);

    public Task<IReadOnlyList<TaskItem>> ListChangedSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default) =>
        Current.ListChangedSinceAsync(since, cancellationToken);
}
