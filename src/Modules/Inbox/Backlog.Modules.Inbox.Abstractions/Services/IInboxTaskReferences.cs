using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Inbox.Abstractions.Services;

/// <summary>
/// The backlog's tasks, as far as an item's text could name one: a title, an
/// issue, a link. What the batch proposal reads to see that an item waits on
/// work the backlog already has, and what an item's relations are read against
/// to see the tasks that already carry it.
/// <para>
/// A port on the Inbox's own surface rather than a reference to Tasks, for the
/// reason <see cref="IBacklogTagSource"/> gives: the Inbox is upstream of Tasks
/// on the context map and may not see it, so an adapter under
/// <c>src/Infrastructure</c> that is allowed to see both answers this.
/// </para>
/// <para>
/// Two reads for two questions. A dependency is only ever on open work — one on
/// finished work is one nothing waits for, and one on archived work names a task
/// that is not coming — so <see cref="OpenTasksAsync"/> answers neither done nor
/// archived. A relation is about what already exists, finished or not, so
/// <see cref="AllTasksAsync"/> answers done tasks too, and leaves out only the
/// archived ones, which the backlog has put away.
/// </para>
/// </summary>
public interface IInboxTaskReferences
{
    /// <summary>Every open task, in the backlog's order. Empty when there are none.</summary>
    Task<IReadOnlyList<InboxTaskReferenceDto>> OpenTasksAsync(CancellationToken cancellationToken = default);

    /// <summary>Every task but the archived ones, in the backlog's order, each
    /// saying whether it is still open. Empty when there are none.</summary>
    Task<IReadOnlyList<InboxTaskReferenceDto>> AllTasksAsync(CancellationToken cancellationToken = default);
}
