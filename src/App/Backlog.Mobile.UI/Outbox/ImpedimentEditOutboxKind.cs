using Backlog.Mobile.UI.Services;

namespace Backlog.Mobile.UI.Outbox;

/// <summary>
/// An impediment edit asked for on the phone, as an outbox entry: the <c>impediment-edit</c>
/// document <see cref="Progress.ProgressViewProjection"/> builds, pushed to
/// <c>POST /api/sync/tasks</c> beside the tasks (local ADR 0035 §8). A desktop applies it on
/// its next pull and retires it.
/// <para>
/// The same queue, order, backoff and waiting marker as a task, and the same wire: only the
/// token differs, so a queued edit is told from a queued task. No idempotency key is needed:
/// a retry sends the same document under the same id, and the replica keeps one.
/// </para>
/// </summary>
public sealed class ImpedimentEditOutboxKind(CloudSyncClient sync) : IOutboxKind
{
    /// <summary>A stored value — the <c>kind</c> column of every queued impediment edit.</summary>
    public const string Token = "impediment-edit";

    public string Kind => Token;

    public Task<OutboxDelivery> SendAsync(OutboxEntry entry, CancellationToken cancellationToken)
    {
        var change = TaskOutboxKind.Read(entry);
        return OutboxDelivery.AttemptAsync(() => sync.PushTaskAsync(change, cancellationToken), cancellationToken);
    }
}
