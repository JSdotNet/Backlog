using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Services;
using Backlog.SharedKernel.Handlers;

namespace Backlog.Modules.Inbox.Features.GetInbox;

/// <summary>Everything the pane draws: every item, every list, every group.</summary>
public sealed record GetInboxQuery;

/// <summary>
/// The snapshot, with a recording whose transcription failed three times marked so its row
/// can offer Retry (local ADR 0049 §6). The record store is an optional port, as on the
/// transcription handler: a host without it transcribes nothing and marks nothing.
/// </summary>
public sealed class GetInboxQueryHandler(
    IInboxItemRepository items,
    IInboxOrganizerRepository organizer,
    IInboxTranscriptionRecords? transcriptions = null)
    : IQueryHandler<GetInboxQuery, InboxSnapshotDto>
{
    public async Task<InboxSnapshotDto> Handle(GetInboxQuery query, CancellationToken cancellationToken = default)
    {
        var all = await items.ListAsync(cancellationToken).ConfigureAwait(false);
        var lists = await organizer.ListListsAsync(cancellationToken).ConfigureAwait(false);
        var groups = await organizer.ListGroupsAsync(cancellationToken).ConfigureAwait(false);

        var dtos = new List<InboxItemDto>(all.Count);
        foreach (var item in all)
        {
            dtos.Add(await WithTranscriptionFailuresAsync(item, cancellationToken).ConfigureAwait(false));
        }

        return new InboxSnapshotDto(
            dtos,
            [.. lists.Select(list => list.ToDto())],
            [.. groups.Select(group => group.ToDto())]);
    }

    private async Task<InboxItemDto> WithTranscriptionFailuresAsync(InboxItem item, CancellationToken cancellationToken)
    {
        var dto = item.ToDto();
        if (transcriptions is null || !item.IsNote || !item.Attachments.Any(attachment => attachment.IsRecording)) return dto;

        // The mapper keeps the item's order, so each published file pairs with its own.
        var attachments = new List<InboxAttachmentDto>(dto.Attachments.Count);
        foreach (var (recording, attachment) in item.Attachments.Zip(dto.Attachments))
        {
            var failure = recording.IsRecording
                ? await transcriptions.FindFailureAsync(recording.Sha256, cancellationToken).ConfigureAwait(false)
                : null;

            attachments.Add(failure is { WaitsForRetry: true } ? attachment with { TranscriptionFailure = failure } : attachment);
        }

        return dto with { Attachments = attachments };
    }
}
