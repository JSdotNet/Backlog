using Backlog.Modules.Notifications;
using Backlog.Modules.Notifications.DomainModels;

namespace Backlog.Infrastructure.Sqlite.Notifications;

/// <summary>
/// <see cref="SqliteNotificationRepository"/> following the storage folder, as every store in
/// <c>backlog.db</c> does: the root is asked per call, because somebody can move their backlog
/// while the app is open and the database follows the folder.
/// </summary>
public sealed class RootedSqliteNotificationRepository(Func<string> currentRootDirectory) : INotificationRepository
{
    private readonly Func<string> _currentRootDirectory =
        currentRootDirectory ?? throw new ArgumentNullException(nameof(currentRootDirectory));

    private readonly Lock _lock = new();
    private string? _rootDirectory;
    private SqliteNotificationRepository? _repository;

    public string DatabasePath => Current.DatabasePath;

    private SqliteNotificationRepository Current
    {
        get
        {
            var root = _currentRootDirectory();

            lock (_lock)
            {
                if (_repository is null || !string.Equals(_rootDirectory, root, StringComparison.OrdinalIgnoreCase))
                {
                    _rootDirectory = root;
                    _repository = new SqliteNotificationRepository(root);
                }

                return _repository;
            }
        }
    }

    public Task<Notification?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Current.GetAsync(id, cancellationToken);

    public Task<IReadOnlyList<Notification>> ListOpenAsync(CancellationToken cancellationToken = default) =>
        Current.ListOpenAsync(cancellationToken);

    public Task<IReadOnlyList<Notification>> ListAwaitingDeliveryAsync(CancellationToken cancellationToken = default) =>
        Current.ListAwaitingDeliveryAsync(cancellationToken);

    public Task<IReadOnlyList<Notification>> ListOpenOrSettledSinceAsync(DateTimeOffset settledSince, CancellationToken cancellationToken = default) =>
        Current.ListOpenOrSettledSinceAsync(settledSince, cancellationToken);

    public Task SaveAsync(Notification notification, CancellationToken cancellationToken = default) =>
        Current.SaveAsync(notification, cancellationToken);
}
