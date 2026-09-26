namespace Backlog.Mobile.UI.Outbox;

/// <summary>
/// One thing the phone has promised to send and has not yet been told arrived.
/// <para>
/// <paramref name="Kind"/> names which <see cref="IOutboxKind"/> sends it, and
/// <paramref name="PayloadJson"/> is whatever that kind wrote — the outbox
/// itself never reads it. That is what lets a later kind (an attachment upload
/// that has to go ahead of its capture, a task pushed to another endpoint) join
/// the same queue, in the same order, with the same backoff, without the queue
/// learning anything about it.
/// </para>
/// </summary>
/// <param name="Id">Minted once, before the first attempt, and sent on every
/// retry — which is what lets the service recognise a retry rather than store
/// the thing twice.</param>
/// <param name="Attempts">Failed attempts so far. At <see cref="DeviceOutbox.MaxAttempts"/>
/// the entry is parked until the person asks for another try.</param>
/// <param name="LastError">What the last failed attempt said, for the person.</param>
/// <param name="Refused">The service answered and will answer the same way
/// again. A refused entry is set aside rather than holding up the queue.</param>
public sealed record OutboxEntry(
    Guid Id,
    string Kind,
    string PayloadJson,
    int Attempts,
    string? LastError,
    DateTimeOffset CreatedAt,
    bool Refused = false)
{
    /// <summary>Waiting for a tap: refused, or tried as often as the outbox tries
    /// on its own.</summary>
    public bool IsParked => Refused || Attempts >= DeviceOutbox.MaxAttempts;
}
