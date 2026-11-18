using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Infrastructure.Cosmos.ConnectorCredentials;

/// <summary>
/// Between the connector's domain records and their stored documents, in both directions,
/// and the two decisions the stores' compare-and-swaps are built on.
/// <para>
/// Separate from the stores so all of it can be tested without Cosmos: the spelling of the
/// ids every point operation addresses, the TTL rule, and which writes a stale read must not
/// send. Nothing in the container is ever queried — it indexes nothing but the key — so the
/// ids are the whole addressing scheme, and a family is revoked by its marker document.
/// </para>
/// </summary>
internal static class ConnectorCredentialDocumentFactory
{
    /// <summary>The <c>kind</c> of the client document.</summary>
    public const string ClientKind = "client";

    /// <summary>The <c>kind</c> of a refresh-token document. Descriptive only: a reader
    /// knows the kind from the id it addressed.</summary>
    public const string RefreshTokenKind = "refreshToken";

    /// <summary>
    /// How long past its own expiry a refresh-token document is kept, in seconds — a day.
    /// Long enough for a token presented just after expiry to be answered as expired, or as
    /// a replay, rather than as unknown; the handler's expiry check is what decides.
    /// </summary>
    public const int ReapGraceSeconds = 86_400;

    /// <summary>The <c>kind</c> of a family revocation marker.</summary>
    public const string FamilyRevocationKind = "familyRevocation";

    /// <summary>
    /// How long a family revocation marker is kept, in seconds:
    /// <see cref="ConnectorTokenLimits.MaxRefreshTokenDays"/> — the top of the host's
    /// <c>RefreshTokenDays</c> range, so the longest a token of the family can live — plus
    /// twice the grace. A token's own document lives its remaining days plus one grace, so
    /// the marker is still there for as long as any token it revokes could be found. The
    /// range and this read the same constant, so they cannot drift apart.
    /// </summary>
    public const int FamilyRevocationTtlSeconds =
        (ConnectorTokenLimits.MaxRefreshTokenDays * 86_400) + (2 * ReapGraceSeconds);

    private const string ClientPrefix = "client:";
    private const string RefreshTokenPrefix = "refresh:";
    private const string FamilyRevocationPrefix = "family:";

    /// <summary>The id, and partition key, of a client's document.</summary>
    public static string ClientDocumentId(string clientId) => ClientPrefix + clientId;

    /// <summary>The id, and partition key, of a refresh token's document.</summary>
    public static string RefreshTokenDocumentId(string tokenHash) => RefreshTokenPrefix + tokenHash;

    /// <summary>The id, and partition key, of a family's revocation marker.</summary>
    public static string FamilyRevocationId(Guid familyId) =>
        FamilyRevocationPrefix + ReplicaDocumentSerialization.Key(familyId);

    /// <summary>The marker that revokes every token of <paramref name="familyId"/>, written
    /// at <paramref name="now"/>.</summary>
    public static ConnectorFamilyRevocationDocument FamilyRevocation(Guid familyId, DateTimeOffset now) => new()
    {
        Id = FamilyRevocationId(familyId),
        FamilyId = ReplicaDocumentSerialization.Key(familyId),
        RevokedAt = now,
        Ttl = FamilyRevocationTtlSeconds,
    };

    /// <summary>The document to write for a client.</summary>
    public static ConnectorClientDocument From(ConnectorClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        return new ConnectorClientDocument
        {
            Id = ClientDocumentId(client.ClientId),
            ClientId = client.ClientId,
            SecretHash = client.SecretHash,
            CreatedAt = client.CreatedAt,
            LastUsedAt = client.LastUsedAt,
            Generation = client.Generation,
        };
    }

    /// <summary>The client a stored document holds.</summary>
    public static ConnectorClient ToClient(ConnectorClientDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new ConnectorClient(
            document.ClientId,
            string.IsNullOrEmpty(document.SecretHash) ? null : document.SecretHash,
            document.CreatedAt,
            document.LastUsedAt,
            document.Generation);
    }

    /// <summary>
    /// Records the last use on <paramref name="document"/> when it still holds
    /// <paramref name="generation"/>, and reports whether it did. False means the secret
    /// that authenticated has since been replaced or revoked, and nothing may be written.
    /// </summary>
    public static bool Stamp(ConnectorClientDocument document, int generation, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Generation != generation)
        {
            return false;
        }

        document.LastUsedAt = at;
        return true;
    }

    /// <summary>The document to write for a refresh token, with its <c>ttl</c> computed from
    /// <paramref name="now"/>, the moment of the write.</summary>
    public static ConnectorRefreshTokenDocument From(ConnectorRefreshToken token, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(token);

        return new ConnectorRefreshTokenDocument
        {
            Id = RefreshTokenDocumentId(token.TokenHash),
            TokenHash = token.TokenHash,
            FamilyId = ReplicaDocumentSerialization.Key(token.FamilyId),
            ClientGeneration = token.ClientGeneration,
            OwnerId = ReplicaDocumentSerialization.Key(token.Owner.Value),
            Scope = token.Scope,
            Resource = token.Resource,
            IssuedAt = token.IssuedAt,
            ExpiresAt = token.ExpiresAt,
            ReplacedByHash = token.ReplacedByHash,
            Revoked = token.Revoked,
            Ttl = TtlFor(token.ExpiresAt, now),
        };
    }

    /// <summary>
    /// The refresh token a stored document holds, or null for one whose owner or family is
    /// not a GUID — not one this service wrote, and answered as unknown rather than thrown
    /// through the handler. <paramref name="familyRevoked"/> is whether the family's
    /// revocation marker exists; when it does the token is revoked, whatever its own
    /// document says.
    /// </summary>
    public static ConnectorRefreshToken? ToRefreshToken(ConnectorRefreshTokenDocument document, bool familyRevoked = false)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!Guid.TryParse(document.OwnerId, out var owner) || !Guid.TryParse(document.FamilyId, out var family))
        {
            return null;
        }

        return new ConnectorRefreshToken(
            document.TokenHash,
            family,
            document.ClientGeneration,
            new OwnerId(owner),
            document.Scope,
            document.Resource,
            document.IssuedAt,
            document.ExpiresAt,
            string.IsNullOrEmpty(document.ReplacedByHash) ? null : document.ReplacedByHash,
            document.Revoked || familyRevoked);
    }

    /// <summary>
    /// Marks <paramref name="document"/> as rotated out by <paramref name="replacementHash"/>
    /// when it is still live, and reports whether it did. False — a replay, or a token whose
    /// family was revoked — means the replace must not be sent. A write restarts the TTL
    /// clock, so the <c>ttl</c> is recomputed from <paramref name="now"/>.
    /// </summary>
    public static bool Rotate(ConnectorRefreshTokenDocument document, string replacementHash, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrEmpty(replacementHash);

        if (!string.IsNullOrEmpty(document.ReplacedByHash) || document.Revoked)
        {
            return false;
        }

        document.ReplacedByHash = replacementHash;
        document.Ttl = TtlFor(document.ExpiresAt, now);
        return true;
    }

    /// <summary>
    /// Seconds from <paramref name="now"/> until <paramref name="expiresAt"/>, plus the
    /// grace. Floored at the grace alone, because Cosmos reads a non-positive <c>ttl</c> as
    /// "expire on write".
    /// </summary>
    public static int TtlFor(DateTimeOffset expiresAt, DateTimeOffset now)
    {
        var remaining = Math.Ceiling((expiresAt - now).TotalSeconds);

        return (int)Math.Clamp(remaining, 0, int.MaxValue - ReapGraceSeconds) + ReapGraceSeconds;
    }
}
