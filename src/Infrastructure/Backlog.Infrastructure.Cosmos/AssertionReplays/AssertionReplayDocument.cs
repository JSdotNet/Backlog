using System.Text.Json.Serialization;

namespace Backlog.Infrastructure.Cosmos.AssertionReplays;

/// <summary>
/// One spent device assertion as it sits in the <c>assertionReplays</c> container (local
/// ADR 0058 §2): the hash it is addressed by, the device that spent it, until when it
/// could still be taken, and the <c>ttl</c> that reaps it after that.
/// <para>
/// <see cref="Id"/> is the partition key: SHA-256 hex of the device id and the
/// assertion's <c>jti</c>. Spending is a create of that id and nothing else, so a second
/// spend of the same id by the same device is the store's own 409, on whichever
/// instance of the service it lands. Nothing is ever read back or queried.
/// </para>
/// </summary>
internal sealed class AssertionReplayDocument
{
    /// <summary>SHA-256 hex of <c>"&lt;device id&gt;:&lt;jti&gt;"</c>, and the partition
    /// key. Cosmos requires the property to be called exactly <c>id</c>.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>The device that spent the assertion, <c>"D"</c> format. Kept for
    /// whoever reads the container; nothing in the service reads it.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>The last moment the assertion could still be taken. The <see cref="Ttl"/>
    /// is derived from it.</summary>
    public DateTimeOffset AcceptableUntil { get; set; }

    /// <summary>Seconds until Cosmos removes this document. Always present; the
    /// container's <c>defaultTtl</c> is -1, which expires nothing a document does not
    /// ask to.</summary>
    [JsonPropertyName("ttl")]
    public int? Ttl { get; set; }
}
