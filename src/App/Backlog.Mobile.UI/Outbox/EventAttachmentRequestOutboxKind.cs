using Backlog.Mobile.UI.Services;

namespace Backlog.Mobile.UI.Outbox;

/// <summary>
/// A Talk note attached to a knowledge event on the phone, as an outbox entry: the
/// <c>event-attachment-request</c> document <see cref="Events.EventNoteAttachments"/> builds,
/// pushed to <c>POST /api/sync/tasks</c> beside the tasks
/// (<c>.devbook/domain/events/domain.md#attachment-request</c>). A desktop with the
/// <c>events</c> area on applies it on its next pull and retires it.
/// <para>
/// The same queue, order, backoff and waiting marker as a task, and the same wire, on the
/// terms <see cref="ImpedimentEditOutboxKind"/> gives: only the token differs. No idempotency
/// key is needed: a retry sends the same document under the same request id, and the replica
/// keeps one. The phone never writes an event; this is its one write about one.
/// </para>
/// </summary>
public sealed class EventAttachmentRequestOutboxKind(CloudSyncClient sync) : IOutboxKind
{
    /// <summary>A stored value — the <c>kind</c> column of every queued attachment request.</summary>
    public const string Token = "event-attachment-request";

    public string Kind => Token;

    public Task<OutboxDelivery> SendAsync(OutboxEntry entry, CancellationToken cancellationToken)
    {
        var change = TaskOutboxKind.Read(entry);
        return OutboxDelivery.AttemptAsync(() => sync.PushTaskAsync(change, cancellationToken), cancellationToken);
    }
}
