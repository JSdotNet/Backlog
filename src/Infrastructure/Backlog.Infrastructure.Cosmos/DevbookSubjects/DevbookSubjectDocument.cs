using System.Text.Json.Serialization;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Cosmos.DevbookSubjects;

/// <summary>
/// One devbook subject's state as it sits in the <c>devbookSubjects</c>
/// container: the wire payload, plus what the service adds and what Cosmos
/// maintains — <see cref="Annotations.AnnotationDocument"/>'s envelope without
/// its tombstone and TTL, because a subject's state is set and never deleted.
/// <para>
/// <see cref="OwnerId"/> is the partition key; the container is created on
/// <c>/ownerId</c> in both the AppHost and <c>infra/sync/main.bicep</c>. The
/// Cosmos-reserved names carry an explicit <see cref="JsonPropertyNameAttribute"/>
/// so a naming-policy change cannot silently unpartition the container.
/// </para>
/// </summary>
internal sealed class DevbookSubjectDocument
{
    /// <summary>The state's own id, as <c>Guid.ToString("D")</c> — derived from
    /// the alias and the subject on the device, so every device's copy of one
    /// subject is this one document.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Whose state this is, and the partition key.</summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>Which of the owner's devices wrote this version.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>When the writing device says it changed — a device's clock, not
    /// the store's — and what <c>DevbookSubjectChangePrecedence</c>
    /// compares.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The state itself, stored whole.</summary>
    public DevbookSubjectPayload? Subject { get; set; }

    /// <summary>Cosmos's own write stamp, in unix seconds.</summary>
    [JsonPropertyName("_ts")]
    public long Timestamp { get; set; }

    /// <summary>Cosmos's version tag, mapped so it round-trips; the write path
    /// reads it off the response for its compare-and-write.</summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}
