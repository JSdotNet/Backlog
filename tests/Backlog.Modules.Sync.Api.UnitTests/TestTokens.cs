using Backlog.Modules.Sync.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// Tokens the service should refuse. Minted here rather than obtained from the
/// service, because the point of each one is a property the service will never
/// produce: the wrong signing key, or an expiry already in the past.
/// </summary>
internal static class TestTokens
{
    /// <summary>The issuer and audience the service is configured with. A token
    /// naming anything else is one of the cases below.</summary>
    internal const string Issuer = "https://backlog.jsdotnet.dev/sync";

    internal const string Audience = "backlog-sync";

    /// <param name="base64Key">The HMAC key to sign with.</param>
    /// <param name="expiresIn">How long the token is good for, negative for one
    /// that has already lapsed.</param>
    /// <param name="issuer">Who the token claims minted it.</param>
    /// <param name="audience">Who the token claims it is for.</param>
    /// <param name="algorithm">What it is signed with. The service pins HS256,
    /// so anything else here is a token it must refuse even when the key is
    /// right — algorithm confusion is a real attack and not covered by the
    /// wrong-key case.</param>
    internal static string SignedWith(
        string base64Key,
        TimeSpan? expiresIn = null,
        string? issuer = null,
        string? audience = null,
        string? algorithm = null)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer ?? Issuer,
            Audience = audience ?? Audience,
            IssuedAt = DateTime.UtcNow,
            NotBefore = DateTime.UtcNow.AddMinutes(-10),
            Expires = DateTime.UtcNow.Add(expiresIn ?? TimeSpan.FromMinutes(30)),
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [SyncClaims.DeviceId] = Guid.CreateVersion7().ToString("D"),
                [SyncClaims.OwnerId] = Guid.CreateVersion7().ToString("D"),
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Convert.FromBase64String(base64Key)),
                algorithm ?? SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
