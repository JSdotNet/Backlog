using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Services;
using Backlog.SharedKernel.Handlers;

namespace Backlog.Modules.Inbox.Features.GetInbox;

/// <summary>Everything the pane draws: every item, every list, every group.</summary>
public sealed record GetInboxQuery;

public sealed class GetInboxQueryHandler(IInboxItemRepository items, IInboxOrganizerRepository organizer)
    : IQueryHandler<GetInboxQuery, InboxSnapshotDto>
{
    public async Task<InboxSnapshotDto> Handle(GetInboxQuery query, CancellationToken cancellationToken = default)
    {
        var all = await items.ListAsync(cancellationToken).ConfigureAwait(false);
        var lists = await organizer.ListListsAsync(cancellationToken).ConfigureAwait(false);
        var groups = await organizer.ListGroupsAsync(cancellationToken).ConfigureAwait(false);

        return new InboxSnapshotDto(
            [.. all.Select(item => item.ToDto())],
            [.. lists.Select(list => list.ToDto())],
            [.. groups.Select(group => group.ToDto())]);
    }
}
