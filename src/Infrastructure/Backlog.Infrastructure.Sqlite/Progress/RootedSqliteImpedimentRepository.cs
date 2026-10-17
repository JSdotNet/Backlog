using Backlog.Modules.Progress;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.DomainModels;

namespace Backlog.Infrastructure.Sqlite.Progress;

/// <summary>
/// A <see cref="IImpedimentRepository"/> that follows a folder somebody can move: the
/// current root is read per call and the underlying repository rebuilt only when it
/// changes — the arrangement <see cref="RootedSqliteGoalRepository"/> has.
/// </summary>
public sealed class RootedSqliteImpedimentRepository(Func<string> currentRootDirectory)
    : IImpedimentRepository, IImpedimentReplicaStore
{
    private readonly Func<string> _currentRootDirectory =
        currentRootDirectory ?? throw new ArgumentNullException(nameof(currentRootDirectory));

    private string? _rootDirectory;
    private SqliteImpedimentRepository? _repository;

    public string DatabasePath => Current.DatabasePath;

    private SqliteImpedimentRepository Current
    {
        get
        {
            var root = _currentRootDirectory();

            if (_repository is null || !string.Equals(_rootDirectory, root, StringComparison.OrdinalIgnoreCase))
            {
                _rootDirectory = root;
                _repository = new SqliteImpedimentRepository(root);
            }

            return _repository;
        }
    }

    public Task<IReadOnlyList<Impediment>> ListAsync(CancellationToken cancellationToken = default) =>
        Current.ListAsync(cancellationToken);

    public Task<Impediment?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Current.GetAsync(id, cancellationToken);

    public Task SaveAsync(Impediment impediment, CancellationToken cancellationToken = default) =>
        Current.SaveAsync(impediment, cancellationToken);

    public Task<IReadOnlyList<ImpedimentReplicaCopyDto>> ReadAllAsync(CancellationToken cancellationToken = default) =>
        Current.ReadAllAsync(cancellationToken);

    public Task<ImpedimentReplicaCopyDto?> ReadAsync(Guid id, CancellationToken cancellationToken = default) =>
        Current.ReadAsync(id, cancellationToken);

    public Task<bool> TryWriteAsync(ImpedimentReplicaCopyDto copy, CancellationToken cancellationToken = default) =>
        Current.TryWriteAsync(copy, cancellationToken);
}
