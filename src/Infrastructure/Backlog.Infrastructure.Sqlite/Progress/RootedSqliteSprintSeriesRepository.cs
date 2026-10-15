using Backlog.Modules.Progress;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.DomainModels;

namespace Backlog.Infrastructure.Sqlite.Progress;

/// <summary>
/// A <see cref="ISprintSeriesRepository"/> that follows a folder somebody can move: the
/// current root is read per call and the underlying repository rebuilt only when it
/// changes, the same arrangement as the task and roadmap repositories, which follow the
/// same root to the same file.
/// </summary>
public sealed class RootedSqliteSprintSeriesRepository(Func<string> currentRootDirectory)
    : ISprintSeriesRepository, ISprintSeriesReplicaStore
{
    private readonly Func<string> _currentRootDirectory =
        currentRootDirectory ?? throw new ArgumentNullException(nameof(currentRootDirectory));

    private string? _rootDirectory;
    private SqliteSprintSeriesRepository? _repository;

    public string DatabasePath => Current.DatabasePath;

    private SqliteSprintSeriesRepository Current
    {
        get
        {
            var root = _currentRootDirectory();

            if (_repository is null || !string.Equals(_rootDirectory, root, StringComparison.OrdinalIgnoreCase))
            {
                _rootDirectory = root;
                _repository = new SqliteSprintSeriesRepository(root);
            }

            return _repository;
        }
    }

    public Task<IReadOnlyList<SprintSeries>> ListAsync(CancellationToken cancellationToken = default) =>
        Current.ListAsync(cancellationToken);

    public Task<SprintSeries?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Current.GetAsync(id, cancellationToken);

    public Task SaveAsync(SprintSeries series, CancellationToken cancellationToken = default) =>
        Current.SaveAsync(series, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        Current.DeleteAsync(id, cancellationToken);

    public Task<IReadOnlyList<SprintSeriesReplicaCopyDto>> ReadAllAsync(CancellationToken cancellationToken = default) =>
        Current.ReadAllAsync(cancellationToken);

    public Task<SprintSeriesReplicaCopyDto?> ReadAsync(Guid id, CancellationToken cancellationToken = default) =>
        Current.ReadAsync(id, cancellationToken);

    public Task<bool> TryWriteAsync(SprintSeriesReplicaCopyDto copy, CancellationToken cancellationToken = default) =>
        Current.TryWriteAsync(copy, cancellationToken);
}
