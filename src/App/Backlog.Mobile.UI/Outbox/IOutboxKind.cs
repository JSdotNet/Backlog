namespace Backlog.Mobile.UI.Outbox;

/// <summary>
/// Sends one kind of outbox entry. The outbox owns order, retries and backoff;
/// a kind owns only what an entry of its kind means on the wire and how the
/// answer reads.
/// <para>
/// Registered as one of many, keyed by <see cref="Kind"/>. The capture is the
/// first; attachment uploads and task pushes add their own.
/// </para>
/// </summary>
public interface IOutboxKind
{
    /// <summary>The token stored in the entry's <c>kind</c> column. A stored
    /// value, so it is never renamed.</summary>
    string Kind { get; }

    /// <summary>One attempt. Never throws for a failure it can classify: a
    /// network that is not there is <see cref="OutboxDelivery.Transient"/>, an
    /// answer the service will give again is <see cref="OutboxDelivery.Refused"/>.</summary>
    Task<OutboxDelivery> SendAsync(OutboxEntry entry, CancellationToken cancellationToken);
}

/// <summary>How one attempt went.</summary>
public sealed record OutboxDelivery(OutboxDeliveryKind Kind, string? Error = null)
{
    public static OutboxDelivery Delivered { get; } = new(OutboxDeliveryKind.Delivered);

    /// <summary>Worth trying again later: no network, a timeout, a 5xx.</summary>
    public static OutboxDelivery Transient(string error) => new(OutboxDeliveryKind.Transient, error);

    /// <summary>The service answered and will answer the same way next time — a
    /// 4xx. Retrying on a timer would only repeat it, so the entry is parked at
    /// once for the person to see.</summary>
    public static OutboxDelivery Refused(string error) => new(OutboxDeliveryKind.Refused, error);
}

public enum OutboxDeliveryKind
{
    Delivered,
    Transient,
    Refused
}
