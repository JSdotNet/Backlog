using System.Text.Json;

using Backlog.Mobile.UI.Services;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Outbox;

/// <summary>
/// A reply the person made on the phone to a question a desktop owns, as an outbox entry: the
/// <see cref="NotificationReplyPost"/> posted to <c>POST /api/sync/notifications/replies</c>
/// (local ADR 0039, <c>.devbook/domain/notifications/domain.md#reply</c>).
/// <para>
/// The same queue, order, backoff and waiting marker as every other kind, so a reply made with
/// no signal is kept and sent once there is one. No idempotency key is needed beyond the reply's
/// own id: the entry carries the id the phone minted, and the service stores a reply id once
/// however often it is posted. The entry's id is the reply's, so a screen can ask whether a
/// reply is still waiting by its id alone.
/// </para>
/// <para>
/// Delivered is not accepted. Leaving the outbox means the service holds the reply; the owner
/// desktop accepts or rejects it on its next exchange, and the phone reads its verdict off the
/// notification when the feed brings it back.
/// </para>
/// </summary>
public sealed class ReplyOutboxKind(CloudSyncClient sync) : IOutboxKind
{
    /// <summary>A stored value — the <c>kind</c> column of every queued reply.</summary>
    public const string Token = "notification-reply";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Kind => Token;

    public static string Write(NotificationReplyPost reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        return JsonSerializer.Serialize(reply, Json);
    }

    public static NotificationReplyPost Read(OutboxEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return JsonSerializer.Deserialize<NotificationReplyPost>(entry.PayloadJson, Json)
            ?? throw new InvalidOperationException($"Outbox entry {entry.Id} holds no reply.");
    }

    public Task<OutboxDelivery> SendAsync(OutboxEntry entry, CancellationToken cancellationToken)
    {
        var reply = Read(entry);
        return OutboxDelivery.AttemptAsync(() => sync.PostNotificationReplyAsync(reply, cancellationToken), cancellationToken);
    }
}
