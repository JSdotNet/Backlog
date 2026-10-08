using Backlog.Modules.Inbox.Abstractions.Services;

namespace Backlog.Modules.Inbox.Services;

/// <summary>
/// The published <see cref="IInboxCaptureOutbox"/> port, straight over the item
/// repository. Not a feature slice: draining an outbox decides nothing, and a
/// handler with no rule in it would be ceremony. The acknowledgement's stamp is
/// the item's <c>UpdatedAt</c>, which the routing or archiving step set — the
/// instant this desktop decided, which is what the tombstone should say. A
/// deleted capture carries its own stamp, the instant it was deleted.
/// </summary>
internal sealed class InboxCaptureOutbox(IInboxItemRepository items) : IInboxCaptureOutbox
{
    public async Task<IReadOnlyList<InboxCaptureAckDto>> ListPendingAsync(CancellationToken cancellationToken = default)
    {
        var pending = await items.ListPendingReplicaAckAsync(cancellationToken).ConfigureAwait(false);
        var deleted = await items.ListDeletedCapturesAsync(cancellationToken).ConfigureAwait(false);

        return
        [
            .. pending
                // A note's tombstone carries the note's own stamp, the one its
                // live copy went out under, so it is the later of the two.
                .Select(item => new InboxCaptureAckDto(
                    item.Id, item.Title, item.CapturedAt, item.IsNote ? item.EditedAt : item.UpdatedAt, item.KindSlug))
                .Concat(deleted.Select(capture => new InboxCaptureAckDto(
                    capture.Id, capture.Title, capture.CapturedAt, capture.DeletedAt, capture.Kind)))
                .OrderBy(ack => ack.AcknowledgedAt),
        ];
    }

    public async Task MarkSentAsync(IReadOnlyList<Guid> captureIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(captureIds);

        foreach (var id in captureIds)
        {
            var item = await items.GetAsync(id, cancellationToken).ConfigureAwait(false);

            // No item: either a deleted capture, whose acknowledgement is all
            // that was left of it, or nothing — and forgetting nothing is free.
            if (item is null)
            {
                await items.ForgetDeletedCaptureAsync(id, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!item.ReplicaAckPending) continue;

            item.MarkReplicaAcknowledged();
            await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);
        }
    }
}
