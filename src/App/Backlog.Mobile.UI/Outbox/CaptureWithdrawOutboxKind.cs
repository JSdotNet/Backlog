using System.Text.Json;

using Backlog.Mobile.UI.Services;

namespace Backlog.Mobile.UI.Outbox;

/// <summary>What a <see cref="CaptureWithdrawOutboxKind"/> entry carries: the
/// capture the person deleted on the phone.</summary>
public sealed record CaptureWithdrawal(Guid CaptureId);

/// <summary>
/// The phone's Delete of a capture that has already left it, sent as the
/// capture's acknowledgement (<c>POST /api/sync/inbox/{id}/ack</c>): the service
/// tombstones it, and the desktop archives the item it made from it. A capture
/// still waiting in the outbox is taken out there instead
/// (<c>CaptureEdits</c>) and never reaches the service.
/// <para>
/// Queued rather than sent at once, so a Delete made with no signal is kept. The
/// entry's id is its own and the capture's travels in the payload. Answers are
/// counted as an edit's are (<see cref="CaptureEditOutboxKind"/>).
/// </para>
/// <para>
/// <see cref="Token"/> is a stored value and is never renamed.
/// </para>
/// </summary>
public sealed class CaptureWithdrawOutboxKind(CloudSyncClient sync, CaptureDealtWithNotices notices) : IOutboxKind
{
    public const string Token = "capture-withdraw";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Kind => Token;

    public static string Write(CaptureWithdrawal withdrawal) => JsonSerializer.Serialize(withdrawal, Json);

    public static CaptureWithdrawal Read(OutboxEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return JsonSerializer.Deserialize<CaptureWithdrawal>(entry.PayloadJson, Json)
            ?? throw new InvalidOperationException($"Outbox entry {entry.Id} holds no capture withdrawal.");
    }

    public Task<OutboxDelivery> SendAsync(OutboxEntry entry, CancellationToken cancellationToken)
    {
        var withdrawal = Read(entry);

        return CaptureEditOutboxKind.AttemptAsync(
            withdrawal.CaptureId,
            notices,
            () => sync.AcknowledgeAsync(withdrawal.CaptureId, cancellationToken),
            Astray,
            cancellationToken);
    }

    /// <summary>
    /// A bare 404 or 405 on the acknowledgement route. That route is as old as
    /// captures, so this is no older service but a request that went astray — a
    /// proxy, a misrouted call: held for the service, never counted as delivered.
    /// </summary>
    private static OutboxDelivery Astray { get; } =
        OutboxDelivery.Held("The cloud service did not recognise the request; this waits and tries again.");
}
