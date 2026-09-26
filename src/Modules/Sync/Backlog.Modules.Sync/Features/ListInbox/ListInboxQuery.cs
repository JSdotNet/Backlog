using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.ListInbox;

/// <summary>The captures waiting for the calling owner. There is no filter and
/// no paging parameter: the inbox is what has not been triaged yet, and a
/// person who has thousands of those has a different problem.</summary>
public sealed record ListInboxQuery(OwnerScope Scope);

/// <summary>
/// Projects the owner's live capture documents onto the inbox contract the
/// phone, the desktop pane, and the editor extension already read.
/// <para>
/// A capture is a task-shaped document with its own kind token
/// (<c>CaptureInboxItemCommandHandler.CaptureType</c>). That is what lets the
/// desktop pull it through the ordinary task feed and hand it to its inbox
/// rather than its task table, and what lets acknowledging one be a tombstone
/// (see <c>AcknowledgeInboxItemCommand</c>): the id names the capture and
/// nothing the desktop made from it.
/// </para>
/// <para>
/// The two fields this reads out of the payload, <c>Title</c> and
/// <c>CreatedAt</c>, are the whole of what the service understands about a task.
/// Nothing else is interpreted here, which is what keeps .devbook/arc42/adr/0005's "no
/// domain logic runs against the replica" true of the one view it serves.
/// </para>
/// </summary>
public sealed class ListInboxQueryHandler(ITaskReplica replica)
    : IQueryHandler<ListInboxQuery, Result<IReadOnlyList<InboxItem>>>
{
    public async Task<Result<IReadOnlyList<InboxItem>>> Handle(
        ListInboxQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var captures = await replica.ListCaptures(query.Scope.OwnerId, cancellationToken);

        IReadOnlyList<InboxItem> items =
        [
            .. captures.Select(record => new InboxItem(
                record.Change.Id,
                record.Change.Task.Title,
                record.Change.Task.SourceInboxId ?? string.Empty,
                record.Change.Task.CreatedAt)),
        ];

        return Result.Success(items);
    }
}
