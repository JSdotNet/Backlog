using Backlog.Modules.Backlog;
using Backlog.Modules.Backlog.DomainModels;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// A <see cref="IBacklogRepository"/> that follows a folder somebody can move.
/// <para>
/// The desktop lets you point the app at a different backlog folder while it is
/// running. Handlers should not have to know that, and the container cannot
/// re-resolve a singleton on a settings change, so the current root is read per
/// call and the underlying repository is rebuilt only when it actually changes.
/// </para>
/// </summary>
public sealed class RootedFileBacklogRepository(Func<string> currentRootDirectory) : IBacklogRepository
{
    private readonly Func<string> _currentRootDirectory =
        currentRootDirectory ?? throw new ArgumentNullException(nameof(currentRootDirectory));

    private string? _rootDirectory;
    private FileBacklogRepository? _repository;

    public string RootDirectory => Current.RootDirectory;

    private FileBacklogRepository Current
    {
        get
        {
            var root = _currentRootDirectory();

            if (_repository is null
                || !string.Equals(_rootDirectory, root, StringComparison.OrdinalIgnoreCase))
            {
                _rootDirectory = root;
                _repository = new FileBacklogRepository(root);
            }

            return _repository;
        }
    }

    public Task SaveAsync(BacklogEntry entry, CancellationToken cancellationToken = default) =>
        Current.SaveAsync(entry, cancellationToken);

    public Task<BacklogEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Current.GetAsync(id, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        Current.DeleteAsync(id, cancellationToken);

    public Task<IReadOnlyList<BacklogEntrySummary>> ListAsync(CancellationToken cancellationToken = default) =>
        Current.ListAsync(cancellationToken);
}
