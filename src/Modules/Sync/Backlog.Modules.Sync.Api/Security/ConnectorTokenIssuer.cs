using System.Security.Cryptography;
using Backlog.Modules.Sync.Api.Options;
using Backlog.Modules.Sync.Services;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Backlog.Modules.Sync.Api.Security;

/// <summary>
/// Mints the remote connector's access token (local ADR 0052 §8): HS256, for the <c>/mcp</c>
/// resource, for 15 minutes by default.
/// <para>
/// Signed with a key derived from the device-token signing key —
/// <c>HMACSHA256(signingKey, "backlog.sync.connector.v1")</c> — the way
/// <see cref="HmacSyncCursorCodec"/> derives the cursor key. One secret to provision, and a
/// connector token can never pass as a device token: its signature is invalid under the
/// device key and its audience is not <c>backlog-sync</c>. The resource server that validates
/// these tokens uses <see cref="AcceptedKeys"/>, so the two stay in this one place.
/// </para>
/// </summary>
internal sealed class ConnectorTokenIssuer(
    IOptions<ConnectorOptions> options,
    SigningKeyRing keys,
    TimeProvider clock) : IConnectorTokenIssuer
{
    /// <summary>The claim naming the granted scopes, space-separated (RFC 9068 §2.2.3).</summary>
    internal const string ScopeClaim = "scope";

    /// <summary>The claim naming the client the token was issued to (RFC 9068 §2.2).</summary>
    internal const string ClientIdClaim = "client_id";

    /// <summary>The client generation the token was issued under, so the resource server
    /// can refuse a token minted before the secret was revoked.</summary>
    internal const string GenerationClaim = "gen";

    /// <summary>The domain separation label: changing it invalidates every connector token
    /// in flight and nothing else.</summary>
    private static readonly byte[] Purpose = "backlog.sync.connector.v1"u8.ToArray();

    private readonly JsonWebTokenHandler _handler = new();

    /// <summary>The connector key derived from one signing key.</summary>
    internal static byte[] KeyFor(byte[] signingKey) => HMACSHA256.HashData(signingKey, Purpose);

    /// <summary>Every key a connector token may carry a valid signature under, current first —
    /// what the resource server validates against.</summary>
    internal static IEnumerable<SecurityKey> AcceptedKeys(SigningKeyRing keys) =>
        keys.Accepted().Select(key => new SymmetricSecurityKey(KeyFor(key)));

    public ConnectorAccessToken Issue(ConnectorTokenClaims claims)
    {
        var settings = options.Value;
        var issuedAt = clock.GetUtcNow();
        var expiresAt = issuedAt.AddMinutes(settings.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = claims.Resource,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [JwtRegisteredClaimNames.Sub] = claims.Owner.Value.ToString("D"),
                [ScopeClaim] = claims.Scope,
                [ClientIdClaim] = claims.ClientId,
                [GenerationClaim] = claims.ClientGeneration,
                [JwtRegisteredClaimNames.Jti] = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)),
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(KeyFor(keys.Current)),
                SecurityAlgorithms.HmacSha256),
        };

        return new ConnectorAccessToken(_handler.CreateToken(descriptor), expiresAt);
    }
}
