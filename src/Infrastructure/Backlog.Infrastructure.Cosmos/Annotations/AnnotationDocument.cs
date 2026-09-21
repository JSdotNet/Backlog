using System.Text.Json.Serialization;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Cosmos.Annotations;

/// <summary>
/// One Devbook annotation as it sits in the <c>annotations</c> container: the
/// wire payload, plus the four things the service adds and the two Cosmos
/// maintains — the same envelope as <see cref="Tasks.TaskDocument"/>, for the
/// same reasons given there.
/// <para>
/// <see cref="OwnerId"/> is the partition key; the container is created on
/// <c>/ownerId</c> in both the AppHost and <c>infra/sync/main.bicep</c>. The
/// Cosmos-reserved names carry an explicit <see cref="JsonPropertyNameAttribute"/>
/// so a naming-policy change cannot silently unpartition the container or stop
/// the TTL working. The payload is stored whole and never indexed into by
/// anything this service does.
/// </para>
/// </summary>
internal sealed class AnnotationDocument
{
    /// <summary>The annotation's own id, as <c>Guid.ToString("D")</c>. Cosmos
    /// requires the property to be called exactly <c>id</c>.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Whose annotation this is, and the partition key.</summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>Which of the owner's devices wrote this version, so a device can
    /// recognise its own echo coming back down the feed.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>When the writing device says it changed — a device's clock, not
    /// the store's.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Set on a tombstone and absent otherwise.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Seconds until Cosmos removes this document, or absent to keep it
    /// forever. Only tombstones carry one.</summary>
    [JsonPropertyName("ttl")]
    public int? Ttl { get; set; }

    /// <summary>The annotation itself, stored whole.</summary>
    public AnnotationPayload? Annotation { get; set; }

    /// <summary>Cosmos's own write stamp, in unix seconds — the ordering two
    /// devices with skewed clocks still agree on.</summary>
    [JsonPropertyName("_ts")]
    public long Timestamp { get; set; }

    /// <summary>Cosmos's version tag. The write path reads it off the response
    /// rather than from here, and uses it only to notice that another device
    /// wrote between its read and its write — a race that is re-read and
    /// re-compared, never surfaced as an error (see
    /// <c>CosmosAnnotationReplica.WriteIfLaterAsync</c>). Mapped so it
    /// round-trips rather than being dropped and rewritten as null.</summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}
