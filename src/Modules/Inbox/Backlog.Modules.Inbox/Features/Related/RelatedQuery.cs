using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.Related;

/// <summary>What one item already has to do with the rest of the backlog: the
/// other items that look like the same capture, and the tasks that already carry
/// it. A read: the Inbox never acts on a relation.</summary>
public sealed record RelatedQuery(Guid Id);

/// <summary>
/// Reads the item, every other item, and the backlog's tasks, and hands them to
/// <see cref="RelationFinder"/>. Asked for every item, decided ones included —
/// an archived item still has a duplicate, and a routed one its tasks.
/// <para>
/// The task port is optional, like the tag source is for suggestions: a host
/// without it still gets the items that relate, and no tasks. The open tasks it
/// reads are handed back too, as what "Link to task…" offers after the related
/// ones.
/// </para>
/// </summary>
public sealed class RelatedQueryHandler(
    IInboxItemRepository items,
    IInboxTaskReferences? taskReferences = null)
    : IQueryHandler<RelatedQuery, Result<InboxRelationsDto>>
{
    public async Task<Result<InboxRelationsDto>> Handle(RelatedQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var item = await items.GetAsync(query.Id, cancellationToken).ConfigureAwait(false);
        if (item is null) return Result.Failure<InboxRelationsDto>(InboxErrors.ItemNotFound);

        var others = await items.ListAsync(cancellationToken).ConfigureAwait(false);
        var tasks = taskReferences is null
            ? []
            : await taskReferences.AllTasksAsync(cancellationToken).ConfigureAwait(false);

        return RelationFinder.Find(item, others, tasks) with
        {
            OpenTasks = [.. tasks.Where(task => task.IsOpen).Select(task => new InboxTaskOptionDto(task.Id, task.Title))],
        };
    }
}
