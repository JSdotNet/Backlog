using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Inbox.Abstractions.Services;

/// <summary>
/// The backlog's open tasks, as far as an item's text could name one: a title,
/// an issue, a link. What the batch proposal reads to see that an item waits on
/// work the backlog already has.
/// <para>
/// A port on the Inbox's own surface rather than a reference to Tasks, for the
/// reason <see cref="IBacklogTagSource"/> gives: the Inbox is upstream of Tasks
/// on the context map and may not see it, so an adapter under
/// <c>src/Infrastructure</c> that is allowed to see both answers this.
/// </para>
/// <para>
/// Open only — neither done nor archived. A dependency on finished work is one
/// nothing waits for, and one on archived work names a task that is not coming.
/// </para>
/// </summary>
public interface IInboxTaskReferences
{
    /// <summary>Every open task, in the backlog's order. Empty when there are none.</summary>
    Task<IReadOnlyList<InboxTaskReferenceDto>> OpenTasksAsync(CancellationToken cancellationToken = default);
}
