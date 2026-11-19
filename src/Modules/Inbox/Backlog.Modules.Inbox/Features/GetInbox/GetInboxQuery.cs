using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.Services;
using Backlog.SharedKernel.Handlers;

namespace Backlog.Modules.Inbox.Features.GetInbox;

/// <summary>Everything the pane draws: every item, every list, every group, and on each
/// recording that waits for Retry its last transcription failure.</summary>
public sealed record GetInboxQuery;

public sealed class GetInboxQueryHandler(IInboxItemRepository items, IInboxOrganizerRepository organizer, IInboxTranscriptionRecords? transcriptions = null)
    : IQueryHandler<GetInboxQuery, InboxSnapshotDto>
{
    public async Task<InboxSnapshotDto> Handle(GetInboxQuery query, CancellationToken cancellationToken = default)
    {
        var all = await items.ListAsync(cancellationToken).ConfigureAwait(false);
        var lists = await organizer.ListListsAsync(cancellationToken).ConfigureAwait(false);
        var groups = await organizer.ListGroupsAsync(cancellationToken).ConfigureAwait(false);
        var failures = transcriptions is null
            ? null
            : (await transcriptions.ListFailuresAsync(cancellationToken).ConfigureAwait(false))
                .Where(failure => failure.WaitsForRetry)
                .GroupBy(failure => failure.Sha256, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return new InboxSnapshotDto(
            [.. all.Select(item => item.ToDto(failures))],
            [.. lists.Select(list => list.ToDto())],
            [.. groups.Select(group => group.ToDto())]);
    }
}
