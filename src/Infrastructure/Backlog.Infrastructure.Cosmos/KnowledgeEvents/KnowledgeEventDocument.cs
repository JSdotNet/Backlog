using System.Text.Json;
using System.Text.Json.Serialization;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Infrastructure.Cosmos.KnowledgeEvents;

/// <summary>
/// One knowledge event or event series as it sits in the <c>knowledgeEvents</c> container
/// (local ADR 0050 §2): the envelope the service reads, the device that wrote it, and the
/// event itself stored whole — <see cref="Notifications.NotificationDocument"/>'s opaque
/// document on <see cref="DevbookSubjects.DevbookSubjectDocument"/>'s terms, without a
/// tombstone or a TTL, because an event is never deleted on the feed.
/// <para>
/// <see cref="OwnerId"/> is the partition key; the container is created on <c>/ownerId</c>
/// in both the AppHost and <c>infra/sync/main.bicep</c>. The Cosmos-reserved names carry an
/// explicit <see cref="JsonPropertyNameAttribute"/> so a naming-policy change cannot silently
/// unpartition the container.
/// </para>
/// </summary>
internal sealed class KnowledgeEventDocument
{
    /// <summary>The event's or the series' own id, as <c>Guid.ToString("D")</c>.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Whose event this is, and the partition key.</summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>Which of the owner's devices wrote this version.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary><c>knowledge-event</c> or <c>event-series</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>When the writing desktop says it changed — a device's clock, not the
    /// store's — and what <c>KnowledgeEventChangePrecedence</c> compares.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The event or the series itself, never read here.</summary>
    public JsonElement Document { get; set; }

    /// <summary>Cosmos's own write stamp, in unix seconds.</summary>
    [JsonPropertyName("_ts")]
    public long Timestamp { get; set; }

    /// <summary>Cosmos's version tag, mapped so it round-trips; the write path reads it off
    /// the response for its compare-and-write.</summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}

/// <summary>
/// Between the wire contract and the stored document, in both directions. Separate from
/// <see cref="CosmosKnowledgeEventReplica"/> so the partition key spelling can be pinned
/// without an emulator.
/// </summary>
internal static class KnowledgeEventDocumentFactory
{
    /// <summary>The document to write for one change. No TTL: the container keeps an event
    /// for as long as its owner has one.</summary>
    public static KnowledgeEventDocument From(OwnerScope scope, KnowledgeEventChange change) =>
        new()
        {
            Id = ReplicaDocumentSerialization.Key(change.Id),
            OwnerId = ReplicaDocumentSerialization.Key(scope.OwnerId.Value),
            DeviceId = ReplicaDocumentSerialization.Key(scope.DeviceId.Value),
            Kind = change.Kind,
            UpdatedAt = change.UpdatedAt,
            Document = change.Document.Clone(),
        };

    /// <summary>
    /// The record to hand back for one stored document, or null for a document this service
    /// did not write — no event object, a kind the feed does not carry, or an id or device id
    /// that is not a GUID. Null rather than a throw, so one unreadable document cannot stop an
    /// owner from ever syncing again.
    /// </summary>
    public static KnowledgeEventChangeRecord? ToRecord(KnowledgeEventDocument document)
    {
        if (document.Document.ValueKind is not JsonValueKind.Object
            || !KnowledgeEventKinds.IsKnown(document.Kind)
            || !Guid.TryParse(document.Id, out var id)
            || !Guid.TryParse(document.DeviceId, out var deviceId))
        {
            return null;
        }

        return new KnowledgeEventChangeRecord(
            new KnowledgeEventChange(id, document.Kind, document.UpdatedAt, document.Document),
            deviceId,
            document.Timestamp);
    }
}
