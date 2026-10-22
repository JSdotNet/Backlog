using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Modules.Sync.Ports;

/// <summary>
/// The realtime channel that tells an owner's desktops to pull the notification exchange now
/// (local ADR 0038 §4). Declared here and implemented outside — Azure Web PubSub, deployed —
/// beside <see cref="INotificationReplica"/>, so the module says what it needs without naming
/// the service.
/// <para>
/// It only nudges. The replica is the truth, and a nudge carries the kind of change and nothing
/// of it, so a missed one costs time and never data: the desktop's two-minute poll is the
/// fallback while its socket is down.
/// </para>
/// <para>
/// One group per owner. The owner comes from the caller's validated token, as it does for every
/// replica call, so a device can only ever be handed a connection to its own owner's group.
/// </para>
/// </summary>
public interface INotificationNudgeChannel
{
    /// <summary>
    /// A client access URL that joins the calling device to its owner's group and allows it
    /// nothing else, or <see langword="null"/> when this host runs without a channel — the
    /// desktop then keeps polling. A channel that exists but cannot mint one just now throws
    /// <see cref="SyncReplicaException"/> with <c>SyncErrorCodes.NudgeUnavailable</c>, so the
    /// desktop retries soon rather than taking it for "no channel".
    /// </summary>
    Task<Uri?> GetClientAccessUrlAsync(OwnerScope scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells every connected device of <paramref name="owner"/> to pull. Best effort: an
    /// implementation that cannot reach its service logs and returns, because the write that
    /// caused the nudge has already landed and must not be reported as failed.
    /// </summary>
    /// <param name="owner">Whose devices to nudge.</param>
    /// <param name="kind">One of <c>NotificationNudgeKinds</c>.</param>
    /// <param name="cancellationToken">The request's own token.</param>
    Task PublishAsync(OwnerId owner, string kind, CancellationToken cancellationToken = default);
}
