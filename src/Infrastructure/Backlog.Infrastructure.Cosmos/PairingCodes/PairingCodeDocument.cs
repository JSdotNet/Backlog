using System.Text.Json.Serialization;

namespace Backlog.Infrastructure.Cosmos.PairingCodes;

/// <summary>
/// One live pairing code as it sits in the <c>pairingCodes</c> container: the
/// domain record's five fields, the <c>ttl</c> that reaps it, and the two
/// Cosmos maintains.
/// <para>
/// <see cref="Id"/> is the partition key and it is the code hash. A code is
/// looked up by hash and by nothing else — the plaintext was shown once on the
/// issuing device and never stored, so the hash is the whole address — which
/// makes every operation here a point read or a conditional replace of one
/// document, and makes <c>/id</c> the only partition key that would not add a
/// query to a path that has none.
/// </para>
/// <para>
/// <see cref="ETag"/> is used, and it is the one document type in this project
/// where it is. Burning a code is a replace conditioned on the etag the
/// preceding read returned: two devices racing on one code both read
/// <c>redeemed: false</c> with the same etag, and the second replace fails its
/// precondition. That is what makes <c>TryBurn</c>'s false mean "somebody else
/// got there first" rather than "probably".
/// </para>
/// <para>
/// <see cref="Ttl"/> is what makes a code short-lived storage rather than a
/// table that grows by one row per pairing for ever. The number is the window
/// plus a grace, so the handler's own expiry check — the authoritative one,
/// because TTL reaping is lazy and the emulator does not do it at all — still
/// finds the document for a while after the window closes and can answer
/// "expired" rather than "not found".
/// </para>
/// </summary>
internal sealed class PairingCodeDocument
{
    /// <summary>SHA-256 of the normalized code, hex, and the partition key.
    /// Cosmos requires the property to be called exactly <c>id</c>.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>The owner a redeeming device joins. <c>"D"</c> format, like
    /// every other owner id in the account.</summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>Which already-paired device read the code out.</summary>
    public string IssuedBy { get; set; } = string.Empty;

    /// <summary>When the window closes. The handler compares against this; the
    /// <see cref="Ttl"/> beside it is derived from it and is only what tidies
    /// up afterwards.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set once, by the redemption that consumed the code. Written as
    /// <c>false</c> rather than omitted — it is the value the burn's read
    /// checks, and an absent property would read as "not redeemed" only by
    /// accident of a default.</summary>
    public bool Redeemed { get; set; }

    /// <summary>Seconds until Cosmos removes this document. Always present on a
    /// code; the container's <c>defaultTtl</c> is -1, which turns the feature
    /// on and expires nothing unless its own document asks to.</summary>
    [JsonPropertyName("ttl")]
    public int? Ttl { get; set; }

    /// <summary>Cosmos's own write stamp, in unix seconds. Read-only; mapped so
    /// it round-trips rather than being dropped.</summary>
    [JsonPropertyName("_ts")]
    public long Timestamp { get; set; }

    /// <summary>Cosmos's version tag, and the precondition the burn is
    /// conditioned on — see the type summary.</summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}
