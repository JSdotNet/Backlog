using System.Text.Json.Serialization;

namespace Backlog.Infrastructure.Cosmos.Devices;

/// <summary>
/// One registered device as it sits in the <c>devices</c> container: the domain
/// record's five fields, plus the two Cosmos maintains.
/// <para>
/// <see cref="Id"/> is the partition key, and that is the one place this
/// container departs from the two replicas beside it. They partition on the
/// owner because every read they serve starts from a token that names one. The
/// registry's hot read does not: <c>IDeviceRegistry.FindById</c> runs on every
/// token mint, anonymously, with only the device id in hand — it is the read
/// that finds out who the owner is. Partitioned on <c>/id</c> that is a point
/// read; partitioned on <c>/ownerId</c> it would be a cross-partition query on
/// every sync a device makes. The price is paid by the two owner-scoped reads
/// instead, <c>CountByOwner</c> and <c>OwnerExists</c>, which become
/// cross-partition queries — but they run when somebody opens the Devices tab
/// or registers, and they are bounded by how many machines one person owns.
/// </para>
/// <para>
/// The camelCase naming policy in <see cref="ReplicaDocumentSerialization"/> is
/// what makes <see cref="OwnerId"/> serialise to the <c>ownerId</c> those two
/// queries filter on. The Cosmos-reserved names carry an explicit
/// <see cref="JsonPropertyNameAttribute"/> instead of relying on it, for the
/// reason <c>TaskDocument</c> gives: they are not camelCase to begin with, and
/// a policy change should not be able to unpartition the container.
/// </para>
/// <para>
/// No <c>ttl</c> and no <c>deletedAt</c>. A registration is kept until the
/// person forgets the device, and nothing here is ever soft-deleted: the
/// registry is not a replica, so there is no offline device that needs to
/// learn about a deletion later.
/// </para>
/// </summary>
internal sealed class DeviceDocument
{
    /// <summary>The device's own id, as <c>Guid.ToString("D")</c>, and the
    /// partition key. Cosmos requires the property to be called exactly
    /// <c>id</c>.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Whose device this is. Not the partition key here — see the type
    /// summary — but <c>"D"</c> format all the same, so it compares equal to the
    /// <c>ownerId</c> every other container is partitioned on.</summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>What a person calls it in the device list.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>SHA-256 of the registration credential, hex. The credential
    /// itself is on the device and nowhere else; a copy of this document is
    /// not a copy of the secret.</summary>
    public string CredentialHash { get; set; } = string.Empty;

    /// <summary>When the device first came in — first registration or pairing,
    /// the record does not distinguish.</summary>
    public DateTimeOffset RegisteredAt { get; set; }

    /// <summary>Cosmos's own write stamp, in unix seconds. Read-only and set by
    /// the store; mapped so it round-trips rather than being dropped.</summary>
    [JsonPropertyName("_ts")]
    public long Timestamp { get; set; }

    /// <summary>Cosmos's version tag. Not used for concurrency — a device is
    /// written once, on registration, and never replaced. Mapped so it
    /// round-trips rather than being rewritten as null.</summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}
