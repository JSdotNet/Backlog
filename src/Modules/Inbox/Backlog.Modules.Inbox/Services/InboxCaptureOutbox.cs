using Backlog.Modules.Inbox.Abstractions.Services;

namespace Backlog.Modules.Inbox.Services;

/// <summary>
/// The published <see cref="IInboxCaptureOutbox"/> port, straight over the item
/// repository. Not a feature slice: draining an outbox decides nothing, and a
/// handler with no rule in it would be ceremony. The acknowledgement's stamp is
/// the item's <c>UpdatedAt</c>, which the routing or archiving step set — the
/// instant this desktop decided, which is what the tombstone should say.
/// </summary>
internal sealed class InboxCaptureOutbox(IInboxItemRepository items) : IInboxCaptureOutbox
{
    public async Task<IReadOnlyList<InboxCaptureAckDto>> ListPendingAsync(CancellationToken cancellationToken = default)
    {
        var pending = await items.ListPendingReplicaAckAsync(cancellationToken).ConfigureAwait(false);

        return [.. pending.Select(item => new InboxCaptureAckDto(item.Id, item.Title, item.CapturedAt, item.UpdatedAt))];
    }

    public async Task MarkSentAsync(IReadOnlyList<Guid> captureIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(captureIds);

        foreach (var id in captureIds)
        {
            var item = await items.GetAsync(id, cancellationToken).ConfigureAwait(false);
            if (item is null || !item.ReplicaAckPending) continue;

            item.MarkReplicaAcknowledged();
            await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);
        }
    }
}
