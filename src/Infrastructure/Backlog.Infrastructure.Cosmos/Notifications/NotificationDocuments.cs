using System.Text.Json;
using System.Text.Json.Serialization;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Infrastructure.Cosmos.Notifications;

/// <summary>
/// One notification as it sits in the <c>notifications</c> container (local ADR 0038): the
/// envelope the service reads, the device that wrote it, and the notification itself stored
/// whole. <see cref="OwnerId"/> is the partition key; the container is created on
/// <c>/ownerId</c> in both the AppHost and <c>infra/sync/main.bicep</c>.
/// </summary>
internal sealed class NotificationDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Whose notification this is, and the partition key.</summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>The sync device that last wrote it — the one that speaks for its owner
    /// instance, and may list and delete the replies addressed to that instance.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>The publishing installation, as the notification names it: its one writer.
    /// A push naming another instance does not overwrite it.</summary>
    public string OwnerInstanceId { get; set; } = string.Empty;

    /// <summary>Where it stands; anything but <see cref="NotificationStates.Open"/> starts
    /// the expiry.</summary>
    public string State { get; set; } = NotificationStates.Open;

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Seconds until Cosmos removes it: the open backstop while it is open, the
    /// 30-day retention once it has left Open.</summary>
    [JsonPropertyName("ttl")]
    public int? Ttl { get; set; }

    /// <summary>The notification itself, never read here.</summary>
    public JsonElement Notification { get; set; }

    [JsonPropertyName("_ts")]
    public long Timestamp { get; set; }

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}

/// <summary>
/// One reply as it sits in the <c>notificationReplies</c> container, partitioned on
/// <c>/ownerId</c>: what the viewer posted, and the owner and the device the service stamped
/// from its token.
/// </summary>
internal sealed class NotificationReplyDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    public string OwnerId { get; set; } = string.Empty;

    /// <summary>The owner instance the reply is addressed to; the device that speaks for it
    /// lists and deletes the reply.</summary>
    public string OwnerInstanceId { get; set; } = string.Empty;

    public string NotificationId { get; set; } = string.Empty;

    /// <summary>The sync device the person replied on.</summary>
    public string FromDeviceId { get; set; } = string.Empty;

    public IReadOnlyList<string> ChoiceIds { get; set; } = [];

    public string? Text { get; set; }

    public DateTimeOffset RepliedAt { get; set; }

    /// <summary>The backstop: a reply the owner never processes — an owner unpaired for
    /// good — expires after the configured retention.</summary>
    [JsonPropertyName("ttl")]
    public int? Ttl { get; set; }

    [JsonPropertyName("_ts")]
    public long Timestamp { get; set; }
}

/// <summary>Between the wire contract and the stored documents, in both directions, so the
/// partition key and the TTL rule can be pinned without an emulator.</summary>
internal static class NotificationDocumentFactory
{
    /// <summary>The document for one pushed notification. One that has left Open carries the
    /// 30-day retention, so it expires 30 days after its last push as settled; an open one
    /// carries the longer open backstop, which the owner's periodic re-push keeps moving, so
    /// only an owner that never comes back lets it lapse. As with every TTL here, the emulator
    /// does not honour it.</summary>
    public static NotificationDocument From(OwnerScope scope, NotificationChange change, CosmosOptions options) =>
        new()
        {
            Id = ReplicaDocumentSerialization.Key(change.Id),
            OwnerId = ReplicaDocumentSerialization.Key(scope.OwnerId.Value),
            DeviceId = ReplicaDocumentSerialization.Key(scope.DeviceId.Value),
            OwnerInstanceId = ReplicaDocumentSerialization.Key(change.OwnerInstanceId),
            State = change.State,
            UpdatedAt = change.UpdatedAt,
            Ttl = string.Equals(change.State, NotificationStates.Open, StringComparison.Ordinal)
                ? options.NotificationOpenTtlSeconds
                : options.NotificationSettledTtlSeconds,
            Notification = change.Notification.Clone(),
        };

    /// <summary>The record for one stored notification, or null for one this service did
    /// not write.</summary>
    public static NotificationChangeRecord? ToRecord(NotificationDocument document)
    {
        if (document.Notification.ValueKind is not JsonValueKind.Object
            || !Guid.TryParse(document.Id, out var id)
            || !Guid.TryParse(document.DeviceId, out var deviceId)
            || !Guid.TryParse(document.OwnerInstanceId, out var ownerInstanceId))
        {
            return null;
        }

        return new NotificationChangeRecord(
            new NotificationChange(id, ownerInstanceId, document.State, document.UpdatedAt, document.Notification),
            deviceId,
            document.Timestamp);
    }

    /// <summary>The document for one reply.</summary>
    public static NotificationReplyDocument From(OwnerScope scope, NotificationReplyPost reply, CosmosOptions options) =>
        new()
        {
            Id = ReplicaDocumentSerialization.Key(reply.Id),
            OwnerId = ReplicaDocumentSerialization.Key(scope.OwnerId.Value),
            OwnerInstanceId = ReplicaDocumentSerialization.Key(reply.OwnerInstanceId),
            NotificationId = ReplicaDocumentSerialization.Key(reply.NotificationId),
            FromDeviceId = ReplicaDocumentSerialization.Key(scope.DeviceId.Value),
            ChoiceIds = [.. reply.ChoiceIds ?? []],
            Text = reply.Text,
            RepliedAt = reply.RepliedAt,
            Ttl = options.NotificationReplyTtlSeconds,
        };

    /// <summary>The record for one stored reply, or null for one this service did not write.</summary>
    public static NotificationReplyRecord? ToRecord(NotificationReplyDocument document)
    {
        if (!Guid.TryParse(document.Id, out var id)
            || !Guid.TryParse(document.OwnerId, out var ownerId)
            || !Guid.TryParse(document.OwnerInstanceId, out var ownerInstanceId)
            || !Guid.TryParse(document.NotificationId, out var notificationId)
            || !Guid.TryParse(document.FromDeviceId, out var fromDeviceId))
        {
            return null;
        }

        return new NotificationReplyRecord(
            new NotificationReplyPost(id, notificationId, ownerInstanceId, [.. document.ChoiceIds ?? []], document.Text, document.RepliedAt),
            ownerId,
            fromDeviceId,
            document.Timestamp);
    }
}
