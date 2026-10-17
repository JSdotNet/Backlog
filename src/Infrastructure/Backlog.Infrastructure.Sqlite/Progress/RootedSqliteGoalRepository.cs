using Backlog.Modules.Progress;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.DomainModels;

namespace Backlog.Infrastructure.Sqlite.Progress;

/// <summary>
/// A <see cref="IGoalRepository"/> that follows a folder somebody can move: the current
/// root is read per call and the underlying repository rebuilt only when it changes — the
/// arrangement <see cref="RootedSqliteSprintSeriesRepository"/> has, following the same
/// root to the same file.
/// </summary>
public sealed class RootedSqliteGoalRepository(Func<string> currentRootDirectory)
    : IGoalRepository, IGoalReplicaStore
{
    private readonly Func<string> _currentRootDirectory =
        currentRootDirectory ?? throw new ArgumentNullException(nameof(currentRootDirectory));

    private string? _rootDirectory;
    private SqliteGoalRepository? _repository;

    public string DatabasePath => Current.DatabasePath;

    private SqliteGoalRepository Current
    {
        get
        {
            var root = _currentRootDirectory();

            if (_repository is null || !string.Equals(_rootDirectory, root, StringComparison.OrdinalIgnoreCase))
            {
                _rootDirectory = root;
                _repository = new SqliteGoalRepository(root);
            }

            return _repository;
        }
    }

    public Task<IReadOnlyList<Goal>> ListAsync(CancellationToken cancellationToken = default) =>
        Current.ListAsync(cancellationToken);

    public Task<Goal?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Current.GetAsync(id, cancellationToken);

    public Task SaveAsync(Goal goal, CancellationToken cancellationToken = default) =>
        Current.SaveAsync(goal, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        Current.DeleteAsync(id, cancellationToken);

    public Task<IReadOnlyList<GoalReplicaCopyDto>> ReadAllAsync(CancellationToken cancellationToken = default) =>
        Current.ReadAllAsync(cancellationToken);

    public Task<GoalReplicaCopyDto?> ReadAsync(Guid id, CancellationToken cancellationToken = default) =>
        Current.ReadAsync(id, cancellationToken);

    public Task<bool> TryWriteAsync(GoalReplicaCopyDto copy, CancellationToken cancellationToken = default) =>
        Current.TryWriteAsync(copy, cancellationToken);
}
