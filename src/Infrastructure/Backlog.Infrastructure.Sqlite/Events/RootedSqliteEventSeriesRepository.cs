using Backlog.Modules.Events;
using Backlog.Modules.Events.DomainModels;

namespace Backlog.Infrastructure.Sqlite.Events;

/// <summary>
/// <see cref="SqliteEventSeriesRepository"/> following the storage folder, as every store in
/// <c>backlog.db</c> does: the root is asked per call, because somebody can move their backlog
/// while the app is open and the database follows the folder.
/// </summary>
public sealed class RootedSqliteEventSeriesRepository(Func<string> currentRootDirectory, TimeProvider? clock = null) : IEventSeriesRepository
{
    private readonly Func<string> _currentRootDirectory =
        currentRootDirectory ?? throw new ArgumentNullException(nameof(currentRootDirectory));

    private readonly Lock _lock = new();
    private string? _rootDirectory;
    private SqliteEventSeriesRepository? _repository;

    public string DatabasePath => Current.DatabasePath;

    private SqliteEventSeriesRepository Current
    {
        get
        {
            var root = _currentRootDirectory();

            lock (_lock)
            {
                if (_repository is null || !string.Equals(_rootDirectory, root, StringComparison.OrdinalIgnoreCase))
                {
                    _rootDirectory = root;
                    _repository = new SqliteEventSeriesRepository(root, clock);
                }

                return _repository;
            }
        }
    }

    public Task<EventSeries?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Current.GetAsync(id, cancellationToken);

    public Task<IReadOnlyList<EventSeries>> ListAsync(CancellationToken cancellationToken = default) =>
        Current.ListAsync(cancellationToken);

    public Task SaveAsync(EventSeries series, CancellationToken cancellationToken = default) =>
        Current.SaveAsync(series, cancellationToken);
}
