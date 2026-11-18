using System.Text.Json.Serialization;

namespace Backlog.Infrastructure.Cosmos.ConnectorCredentials;

/// <summary>
/// The remote connector's one client as it sits in the <c>connectorCredentials</c>
/// container: the domain record's five fields, its <see cref="Kind"/>, and the two fields
/// Cosmos maintains.
/// <para>
/// <see cref="Id"/> is the partition key: <c>client:</c> and the client id, so a client
/// and a refresh token can never share an address. It carries no <c>ttl</c> — the secret
/// hash has to outlive every restart, or the connector is disconnected.
/// </para>
/// <para>
/// <see cref="ETag"/> is what the last-used stamp is conditioned on, so a stamp racing a
/// revoke or a new secret cannot write the old document back.
/// </para>
/// </summary>
internal sealed class ConnectorClientDocument
{
    /// <summary><c>client:&lt;client id&gt;</c>, and the partition key.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Always <see cref="ConnectorCredentialDocumentFactory.ClientKind"/>.</summary>
    public string Kind { get; set; } = ConnectorCredentialDocumentFactory.ClientKind;

    /// <summary>The client id claude.ai presents.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>SHA-256 of the client secret, hex; absent when there is none.</summary>
    public string? SecretHash { get; set; }

    /// <summary>When the current secret was made.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>When the current secret last authenticated a token call.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>Bumped on every create and revoke of the secret.</summary>
    public int Generation { get; set; }

    /// <summary>Cosmos's write stamp, in unix seconds; mapped so it round-trips.</summary>
    [JsonPropertyName("_ts")]
    public long Timestamp { get; set; }

    /// <summary>Cosmos's version tag, which the last-used stamp is conditioned on.</summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}

/// <summary>
/// The marker that revokes one refresh-token family, in the <c>connectorCredentials</c>
/// container under <c>family:&lt;family id&gt;</c>.
/// <para>
/// One write rather than one per token: a revoke that patched each token could be raced by
/// a rotation inserting a fresh token the patches never saw. The marker is written first,
/// and every read of a token, every rotation and every add checks it — before and after its
/// own write — so a token of a revoked family cannot be live whenever it was written.
/// </para>
/// </summary>
internal sealed class ConnectorFamilyRevocationDocument
{
    /// <summary><c>family:&lt;family id&gt;</c>, and the partition key.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Always <see cref="ConnectorCredentialDocumentFactory.FamilyRevocationKind"/>.</summary>
    public string Kind { get; set; } = ConnectorCredentialDocumentFactory.FamilyRevocationKind;

    /// <summary>The family revoked, <c>"D"</c> format.</summary>
    public string FamilyId { get; set; } = string.Empty;

    /// <summary>When the family was revoked.</summary>
    public DateTimeOffset RevokedAt { get; set; }

    /// <summary>Seconds until Cosmos removes the marker — longer than any token of the
    /// family can live; see <see cref="ConnectorCredentialDocumentFactory.FamilyRevocationTtlSeconds"/>.</summary>
    [JsonPropertyName("ttl")]
    public int? Ttl { get; set; }

    /// <summary>Cosmos's write stamp, in unix seconds; mapped so it round-trips.</summary>
    [JsonPropertyName("_ts")]
    public long Timestamp { get; set; }
}

/// <summary>
/// One refresh token of the remote connector as it sits in the <c>connectorCredentials</c>
/// container.
/// <para>
/// <see cref="Id"/> is <c>refresh:</c> and the token hash: a refresh presents the token and
/// nothing else, so the hash is the whole address and every read is a point read.
/// <see cref="FamilyId"/> addresses the family's revocation marker, which is read beside it.
/// </para>
/// <para>
/// <see cref="ETag"/> is what rotation is conditioned on: two refreshes racing on one token
/// both read it live under the same etag, and the second replace fails its precondition.
/// </para>
/// <para>
/// <see cref="Ttl"/> is the token's remaining life plus a grace, stamped at every write.
/// The handler's own expiry check is what turns a stale token away; the TTL tidies up.
/// </para>
/// </summary>
internal sealed class ConnectorRefreshTokenDocument
{
    /// <summary><c>refresh:&lt;token hash&gt;</c>, and the partition key.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Always <see cref="ConnectorCredentialDocumentFactory.RefreshTokenKind"/>.</summary>
    public string Kind { get; set; } = ConnectorCredentialDocumentFactory.RefreshTokenKind;

    /// <summary>SHA-256 of the token, hex.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>The authorization the token descends from, <c>"D"</c> format.</summary>
    public string FamilyId { get; set; } = string.Empty;

    /// <summary>The client generation the token was issued under.</summary>
    public int ClientGeneration { get; set; }

    /// <summary>Whose token it is, <c>"D"</c> format like every owner id in the account.</summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>The scope granted, space-separated.</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>The resource the access tokens are for.</summary>
    public string Resource { get; set; } = string.Empty;

    /// <summary>When it was issued.</summary>
    public DateTimeOffset IssuedAt { get; set; }

    /// <summary>When it stops working however it is used.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>The token that rotated it out; absent while it is live.</summary>
    public string? ReplacedByHash { get; set; }

    /// <summary>Set on a token written into a family already revoked. The family marker is
    /// what decides; this flag only records what the write saw. Written as <c>false</c>
    /// rather than omitted: it is a value rotation checks.</summary>
    public bool Revoked { get; set; }

    /// <summary>Seconds until Cosmos removes this document. The container's
    /// <c>defaultTtl</c> is -1, so the property is what counts.</summary>
    [JsonPropertyName("ttl")]
    public int? Ttl { get; set; }

    /// <summary>Cosmos's write stamp, in unix seconds; mapped so it round-trips.</summary>
    [JsonPropertyName("_ts")]
    public long Timestamp { get; set; }

    /// <summary>Cosmos's version tag, which rotation is conditioned on.</summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}
