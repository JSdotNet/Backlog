using System.Net;

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

    /// <summary>
    /// How an HTTP answer reads, the same for every kind that posts to the sync
    /// service. 401 is the token, not the entry: a restarted service rejects
    /// tokens it signed before, and the next attempt carries a fresh one — so it
    /// is transient, with 408 and 429, while every other 4xx is a refusal.
    /// </summary>
    public static OutboxDelivery FromAnswer(HttpStatusCode status, string? detail) => status switch
    {
        _ when (int)status is >= 200 and < 300 => Delivered,
        HttpStatusCode.Unauthorized or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests =>
            Transient(detail ?? $"Cloud sync answered {(int)status}."),
        _ when (int)status is >= 400 and < 500 =>
            Refused(detail ?? $"Cloud sync refused it ({(int)status})."),
        _ => Transient(detail ?? $"Cloud sync answered {(int)status}.")
    };

    /// <summary>One attempt that may never reach the service: its answer, or the
    /// network failure a retry can cure.</summary>
    public static async Task<OutboxDelivery> AttemptAsync(
        Func<Task<(HttpStatusCode Status, string? Detail)>> send,
        CancellationToken cancellationToken)
    {
        try
        {
            var (status, detail) = await send();
            return FromAnswer(status, detail);
        }
        catch (HttpRequestException ex)
        {
            return Transient(ex.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Transient("Cloud sync did not answer in time.");
        }
    }
}

public enum OutboxDeliveryKind
{
    Delivered,
    Transient,
    Refused
}
