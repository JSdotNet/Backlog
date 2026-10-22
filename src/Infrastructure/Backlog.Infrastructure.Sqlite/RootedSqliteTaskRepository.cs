using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.DomainModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
/// <para>
/// For the same reason it is where a task's end reaches Notifications
/// (<c>.devbook/domain/notifications/domain.md#subject</c>): a write that takes a task
/// into Done or Archived, or deletes it, closes every notification about that task with
/// <see cref="TaskNotificationSubjects.EndedReason"/> — a person's tick, an MCP
/// <c>transition</c>, and a tick another device made that the sync merge applies alike.
/// Only the step into the end counts, read off the stored row before the write, so saving a
/// task that was already done closes nothing and a task that comes back reopens nothing.
/// The close comes after the write and never fails it.
/// </para>
/// </summary>
public sealed class RootedSqliteTaskRepository(
    Func<string> currentRootDirectory,
    ITaskChangeSignal? changes = null,
    Func<INotifications?>? notifications = null,
    ILogger? logger = null) : ITaskRepository
{
    private readonly Func<string> _currentRootDirectory =
        currentRootDirectory ?? throw new ArgumentNullException(nameof(currentRootDirectory));

    private readonly ITaskChangeSignal? _changes = changes;
    private readonly Func<INotifications?>? _notifications = notifications;
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

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
        ArgumentNullException.ThrowIfNull(task);

        var ending = await EndingAsync([task], cancellationToken).ConfigureAwait(false);

        await Current.SaveAsync(task, cancellationToken).ConfigureAwait(false);

        _changes?.Raise();

        await CloseAsync(ending, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>One write, so one signal: a listener reloading on it reloads once
    /// for a drag rather than once per row it moved.</summary>
    public async Task SaveManyAsync(IReadOnlyCollection<TaskItem> tasks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        if (tasks.Count == 0) return;

        var ending = await EndingAsync(tasks, cancellationToken).ConfigureAwait(false);

        await Current.SaveManyAsync(tasks, cancellationToken).ConfigureAwait(false);

        _changes?.Raise();

        await CloseAsync(ending, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The tasks this write takes into their end: ended as written, and stored not ended.
    /// A task with no stored row is new to this device and nothing here can be about it.
    /// Only an ended task costs a read, and only on a host whose Notifications answers.
    /// A read that fails leaves the write to go ahead without the close; the next start's
    /// probe asks about the task instead.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> EndingAsync(IEnumerable<TaskItem> tasks, CancellationToken cancellationToken)
    {
        var ending = new List<Guid>();

        try
        {
            if (_notifications?.Invoke() is null) return ending;

            foreach (var task in tasks)
            {
                if (!HasEnded(task)) continue;

                var stored = await Current.GetIncludingDeletedAsync(task.Id, cancellationToken).ConfigureAwait(false);
                if (stored is not null && !HasEnded(stored)) ending.Add(task.Id);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Reading which tasks this write ends failed; their notifications close at the next start.");
            ending.Clear();
        }

        return ending;
    }

    /// <summary>After the write has landed, so nothing here — the store refusing, the caller
    /// cancelling — reaches the caller as a failed save.</summary>
    private async Task CloseAsync(IReadOnlyList<Guid> ending, CancellationToken cancellationToken)
    {
        foreach (var id in ending)
        {
            var subject = new NotificationSubject(TaskNotificationSubjects.Context, TaskNotificationSubjects.Task, TaskNotificationSubjects.Id(id));

            try
            {
                if (_notifications?.Invoke() is not { } notifications) return;

                var closed = await notifications.CloseForSubjectAsync(subject, TaskNotificationSubjects.EndedReason, cancellationToken).ConfigureAwait(false);
                if (closed.IsFailure) _logger.LogDebug("Notifications about task {Id} were not closed: {Error}", id, closed.Error.Message);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Closing the notifications about task {Id} failed; the next start's probe asks again.", id);
            }
        }
    }

    private static bool HasEnded(TaskItem task) =>
        TaskNotificationSubjects.HasEnded(task.Status, task.DeletedAt is not null);

    public Task<TaskItem?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Current.GetAsync(id, cancellationToken);

    public Task<TaskItem?> GetIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default) =>
        Current.GetIncludingDeletedAsync(id, cancellationToken);

    public Task<IReadOnlyList<TaskItem>> ListAsync(CancellationToken cancellationToken = default) =>
        Current.ListAsync(cancellationToken);

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        Current.CountAsync(cancellationToken);

    public Task<IReadOnlyList<TaskItem>> ListDeletedAsync(CancellationToken cancellationToken = default) =>
        Current.ListDeletedAsync(cancellationToken);

    public Task<IReadOnlyList<TaskItem>> ListChangedSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default) =>
        Current.ListChangedSinceAsync(since, cancellationToken);
}
