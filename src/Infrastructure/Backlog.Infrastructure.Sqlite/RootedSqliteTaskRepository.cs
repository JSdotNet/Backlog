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
/// </summary>
public sealed class RootedSqliteTaskRepository(Func<string> currentRootDirectory) : ITaskRepository
{
    private readonly Func<string> _currentRootDirectory =
        currentRootDirectory ?? throw new ArgumentNullException(nameof(currentRootDirectory));

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

    public Task SaveAsync(TaskItem task, CancellationToken cancellationToken = default) =>
        Current.SaveAsync(task, cancellationToken);

    public Task<TaskItem?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Current.GetAsync(id, cancellationToken);

    public Task<IReadOnlyList<TaskItem>> ListAsync(CancellationToken cancellationToken = default) =>
        Current.ListAsync(cancellationToken);
}
