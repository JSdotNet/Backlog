using System.Text.Json.Serialization;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Cosmos.Tasks;

/// <summary>
/// One task as it sits in the <c>tasks</c> container: the wire payload, plus the
/// four things the service adds and the two Cosmos maintains.
/// <para>
/// <see cref="OwnerId"/> is the partition key. The container is created on
/// <c>/ownerId</c> in both the AppHost and <c>infra/sync/main.bicep</c>, and the
/// camelCase naming policy in <see cref="ReplicaDocumentSerialization"/> is what
/// makes this property serialise to exactly that name. The four Cosmos-reserved
/// names below carry an explicit <see cref="JsonPropertyNameAttribute"/> instead
/// of relying on that policy, because they are not camelCase in the first place
/// and because a policy change should not be able to silently unpartition the
/// container or stop the TTL working.
/// </para>
/// <para>
/// The payload is stored whole and never indexed into. The service reads
/// <see cref="Id"/>, <see cref="UpdatedAt"/>, <see cref="DeletedAt"/>, and — for
/// the inbox alone — the payload's title, source and creation time. Nothing else
/// here is interpreted, which is what keeps .arc42/adr/0005's "no domain logic
/// runs against the replica" true.
/// </para>
/// </summary>
internal sealed class TaskDocument
{
    /// <summary>The task's own id, as <c>Guid.ToString("D")</c> — the same text
    /// the local SQLite store writes, so the two never have to be translated.
    /// Cosmos requires the property to be called exactly <c>id</c>.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Whose task this is, and the partition key. Also <c>"D"</c>
    /// format: a partition key is a string here rather than a GUID, because
    /// Cosmos partition keys are strings and a client that formatted the same
    /// GUID differently would write into a second partition for one
    /// person.</summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>Which of the owner's devices wrote this version, so a device can
    /// recognise its own echo coming back down the feed.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>When the writing device says it changed. A device's clock, not
    /// the store's — the store's own stamp is <see cref="Timestamp"/>.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Set on a tombstone and absent otherwise. A deleted task is a
    /// document that says so rather than a document that is gone, because a
    /// device that has been offline cannot tell "deleted" from "never
    /// seen".</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Seconds until Cosmos removes this document, or absent to keep it
    /// forever. Only tombstones carry one. The container's <c>defaultTtl</c> is
    /// -1, which means the feature is on and nothing expires unless its own
    /// document asks to.</summary>
    [JsonPropertyName("ttl")]
    public int? Ttl { get; set; }

    /// <summary>The task itself, stored whole.</summary>
    public TaskPayload? Task { get; set; }

    /// <summary>Cosmos's own write stamp, in unix seconds. Read-only and set by
    /// the store, which is what makes it usable as an ordering two devices with
    /// skewed clocks still agree on.</summary>
    [JsonPropertyName("_ts")]
    public long Timestamp { get; set; }

    /// <summary>Cosmos's version tag. Not used for concurrency — the replica is
    /// whole-document last-write-wins and an etag check would turn a normal race
    /// between two of a person's own devices into an error. Mapped so it
    /// round-trips rather than being dropped and rewritten as null.</summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}
