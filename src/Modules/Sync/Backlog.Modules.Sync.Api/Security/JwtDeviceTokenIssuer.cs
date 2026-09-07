using System.Security.Cryptography;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Api.Options;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Services;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Backlog.Modules.Sync.Api.Security;

/// <summary>
/// Mints the short-lived device token, signed with HS256.
/// <para>
/// Symmetric signing, which is the unusual choice and the right one here: this
/// service is the only party that issues these tokens and the only party that
/// validates them. An asymmetric key pair exists so that a verifier who must
/// not be able to mint tokens can still check them — a resource server, a
/// gateway, a partner. There is no such verifier. RS256 would buy a public key
/// nobody fetches, in exchange for key management this service would then have
/// to do. If a second service ever validates these tokens, that is the moment
/// this becomes RS256, and the only thing that changes is inside this class and
/// the matching validation parameters.
/// </para>
/// <para>
/// Registered by the host rather than by <c>AddSyncModule</c>, so the signing
/// key and the <c>TokenValidationParameters</c> that check it stay in one
/// place.
/// </para>
/// </summary>
internal sealed class JwtDeviceTokenIssuer(IOptions<SyncTokenOptions> options, TimeProvider clock) : IDeviceTokenIssuer
{
    private readonly JsonWebTokenHandler _handler = new();

    public DeviceToken Issue(OwnerScope scope)
    {
        var settings = options.Value;
        var issuedAt = clock.GetUtcNow();
        var expiresAt = issuedAt.AddMinutes(settings.LifetimeMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                // The device is the subject; the owner is the only claim that
                // decides what the token can reach.
                [SyncClaims.DeviceId] = scope.DeviceId.Value.ToString("D"),
                [SyncClaims.OwnerId] = scope.OwnerId.Value.ToString("D"),

                // A token identifier, so one token can be named in a log line
                // without the log line carrying the token.
                [JwtRegisteredClaimNames.Jti] = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)),
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(settings.DecodeSigningKey()),
                SecurityAlgorithms.HmacSha256),
        };

        return new DeviceToken(_handler.CreateToken(descriptor), expiresAt);
    }
}
