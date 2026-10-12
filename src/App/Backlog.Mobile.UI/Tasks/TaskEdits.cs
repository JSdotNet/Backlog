using Backlog.Mobile.UI.Outbox;

namespace Backlog.Mobile.UI.Tasks;

/// <summary>
/// The three edits the phone makes to a task already in My Day — done or
/// undone, a step ticked or unticked, and the task moved to tomorrow — as the
/// one service the Tasks tab's screens call
/// (<c>.devbook/arc42/06-runtime-view.md#mobile-my-day-and-task-push</c>).
/// <para>
/// Each rewrites the task's entry text the desktop's way
/// (<see cref="TaskEntryRewrite"/>), and <see cref="TaskViewProjection.EditAsync"/>
/// queues the whole document in the device outbox and shows the edit in the
/// view at once. The day is the phone's own local date, as My Day's is.
/// </para>
/// <para>
/// Every method answers with the task's row as it now stands, or null when the
/// view holds no live task by that id. An edit that changes nothing — ticking a
/// task already ticked — queues nothing and answers with the row unchanged.
/// </para>
/// </summary>
public sealed class TaskEdits(TaskViewProjection view, DeviceOutbox outbox)
{
    /// <summary>Ticks the task off today, the way the desktop's checkbox does.</summary>
    public Task<TaskViewRow?> MarkDoneAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var today = view.Today;
        return view.EditAsync(taskId, task => TaskEntryRewrite.MarkDone(task, today), cancellationToken);
    }

    /// <summary>Unticks the task, leaving its status where it is.</summary>
    public Task<TaskViewRow?> MarkUndoneAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        view.EditAsync(taskId, TaskEntryRewrite.MarkUndone, cancellationToken);

    /// <summary>Ticks or unticks one of the task's steps.</summary>
    public Task<TaskViewRow?> SetStepDoneAsync(Guid taskId, Guid stepId, bool done, CancellationToken cancellationToken = default) =>
        view.EditAsync(taskId, task => TaskEntryRewrite.SetStepDone(task, stepId, done), cancellationToken);

    /// <summary>Picks the task for tomorrow, taking it out of today's My Day.</summary>
    public Task<TaskViewRow?> MoveToTomorrowAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var today = view.Today;
        return view.EditAsync(taskId, task => TaskEntryRewrite.MoveToTomorrow(task, today), cancellationToken);
    }

    /// <summary>
    /// The newest outbox entry still carrying the task, or null when none waits.
    /// An edit's entry has an id of its own, so a screen marking a row waiting
    /// asks here rather than looking the task's id up in the outbox — that finds
    /// only the entry that added it.
    /// </summary>
    public OutboxEntry? Pending(Guid taskId) =>
        outbox.Entries
            .Where(entry => entry.Kind == TaskOutboxKind.Token && CarriesTask(entry, taskId))
            .LastOrDefault();

    private static bool CarriesTask(OutboxEntry entry, Guid taskId)
    {
        if (entry.Id == taskId) return true;

        try
        {
            return TaskOutboxKind.Read(entry).Id == taskId;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            // An entry this build cannot read is not this task's; the outbox
            // still sends or parks it on its own.
            return false;
        }
    }
}
