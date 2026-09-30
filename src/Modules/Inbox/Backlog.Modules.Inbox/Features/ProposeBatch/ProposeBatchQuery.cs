using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.Features.RouteBatchToBacklog;
using Backlog.Modules.Inbox.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.ProposeBatch;

/// <summary>What routing these items as one batch would do, asked before it is
/// done — the "Before you route" panel's question. <paramref name="ListId"/>
/// names the list the batch is, as it does on the route.</summary>
public sealed record ProposeBatchQuery(IReadOnlyList<Guid> Ids, Guid? ListId = null);

/// <summary>
/// Sorts the ids the way the route will, reads the backlog's open tasks, and
/// hands both to <see cref="DependencyProposal"/> — for the dependencies, and
/// for the ordering hints beneath them, which read the lists' names too. A read:
/// nothing is routed, the dependencies it proposes are written only if the
/// person confirms them on the route, and the hints are never written at all.
/// <para>
/// The plan tag is minted here rather than on the route, so the panel shows the
/// tag the import will actually write — the route takes it back as one of the
/// person's choices. Every proposal mints a new one, so a panel cancelled and
/// opened again is a new batch, as routing twice is.
/// </para>
/// <para>
/// The task port is optional, like the tag source is for suggestions: a host
/// without it still gets the dependencies the batch's own items state between
/// themselves.
/// </para>
/// </summary>
public sealed class ProposeBatchQueryHandler(
    IInboxItemRepository items,
    IInboxOrganizerRepository organizer,
    IInboxTaskReferences? taskReferences = null)
    : IQueryHandler<ProposeBatchQuery, Result<InboxBatchProposalDto>>
{
    public async Task<Result<InboxBatchProposalDto>> Handle(ProposeBatchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Ids);

        var planTag = await RouteBatchToBacklogCommandHandler.MintPlanTagAsync(organizer, query.ListId, cancellationToken).ConfigureAwait(false);
        if (planTag.IsFailure) return Result.Failure<InboxBatchProposalDto>(planTag.Error);

        var (failed, routable) = await RouteBatchToBacklogCommandHandler.SortAsync(items, query.Ids, cancellationToken).ConfigureAwait(false);
        var batch = routable.Select(entry => entry.Item).ToList();

        // With no item left to route there is nothing to read the backlog for.
        var tasks = batch.Count == 0 || taskReferences is null
            ? []
            : await taskReferences.OpenTasksAsync(cancellationToken).ConfigureAwait(false);

        // Deferred items stay behind when a list is routed — put aside is not
        // decided — and the panel says how many, as the list's confirm did.
        int? deferred = null;
        if (query.ListId is { } listId)
        {
            var all = await items.ListAsync(cancellationToken).ConfigureAwait(false);
            deferred = all.Count(item => item.ListId == listId && item.Status == InboxStatus.Deferred);
        }

        var dependencies = DependencyProposal.Propose(batch, tasks);

        // Hints need two items to say anything about an order, and the list
        // names only for the same-list hint.
        IReadOnlyList<OrderingHint> hints = [];
        if (batch.Count >= 2)
        {
            var lists = await organizer.ListListsAsync(cancellationToken).ConfigureAwait(false);
            hints = DependencyProposal.Hints(batch, dependencies, lists.ToDictionary(list => list.Id, list => list.Name));
        }

        return new InboxBatchProposalDto(
            planTag.Value,
            [.. batch.Select(item => new InboxBatchProposalItemDto(item.Id, item.Title, [.. item.RepoIds]))],
            dependencies,
            [.. failed.OrderBy(entry => entry.Position).Select(entry => entry.Failure)],
            deferred)
        {
            Hints = hints,
        };
    }
}
