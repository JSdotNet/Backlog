using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks;

namespace Backlog.Infrastructure.Sqlite;

/// <summary>
/// The Tasks context's answer to the startup probe (<see cref="INotificationSubjectProbe"/>):
/// a task a notification is about still lives while its row is there, not deleted, and not
/// Done or Archived. A task ended while the app was closed — ticked on the phone and merged
/// in before anything wrote it here, say — is caught this way. Read from the task store, so
/// Notifications never reads it.
/// </summary>
public sealed class TaskSubjectProbe(ITaskRepository tasks) : INotificationSubjectProbe
{
    private readonly ITaskRepository _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));

    public string Context => TaskNotificationSubjects.Context;

    public async Task<bool> IsLiveAsync(NotificationSubject subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);

        // An id that is not a task's is nothing this context can say ended.
        if (subject.Type != TaskNotificationSubjects.Task || !Guid.TryParse(subject.Id, out var id)) return true;

        var task = await _tasks.GetIncludingDeletedAsync(id, cancellationToken).ConfigureAwait(false);

        return task is not null && !TaskNotificationSubjects.HasEnded(task.Status, task.DeletedAt is not null);
    }
}
