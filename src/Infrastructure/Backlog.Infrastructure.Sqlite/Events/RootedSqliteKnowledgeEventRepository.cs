using Backlog.Modules.Events;
using Backlog.Modules.Events.DomainModels;

namespace Backlog.Infrastructure.Sqlite.Events;

/// <summary>
/// <see cref="SqliteKnowledgeEventRepository"/> following the storage folder, as every store in
/// <c>backlog.db</c> does: the root is asked per call, because somebody can move their backlog
/// while the app is open and the database follows the folder.
/// </summary>
public sealed class RootedSqliteKnowledgeEventRepository(Func<string> currentRootDirectory, TimeProvider? clock = null) : IKnowledgeEventRepository
{
    private readonly Func<string> _currentRootDirectory =
        currentRootDirectory ?? throw new ArgumentNullException(nameof(currentRootDirectory));

    private readonly Lock _lock = new();
    private string? _rootDirectory;
    private SqliteKnowledgeEventRepository? _repository;

    public string DatabasePath => Current.DatabasePath;

    private SqliteKnowledgeEventRepository Current
    {
        get
        {
            var root = _currentRootDirectory();

            lock (_lock)
            {
                if (_repository is null || !string.Equals(_rootDirectory, root, StringComparison.OrdinalIgnoreCase))
                {
                    _rootDirectory = root;
                    _repository = new SqliteKnowledgeEventRepository(root, clock);
                }

                return _repository;
            }
        }
    }

    public Task<KnowledgeEvent?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Current.GetAsync(id, cancellationToken);

    public Task<IReadOnlyList<KnowledgeEvent>> ListAsync(CancellationToken cancellationToken = default) =>
        Current.ListAsync(cancellationToken);

    public Task SaveAsync(KnowledgeEvent knowledgeEvent, CancellationToken cancellationToken = default) =>
        Current.SaveAsync(knowledgeEvent, cancellationToken);
}
