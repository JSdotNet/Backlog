using System.Security.Claims;
using System.Security.Cryptography;

using Backlog.SharedKernel.Mcp;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Backlog.Desktop.UI.Mcp;

/// <summary>
/// Mints and validates the grant tokens of local ADR 0052 §3: a JWT for one grant, HS256,
/// <see cref="McpGrantClaims.Lifetime"/> long, signed with 32 random bytes this process draws
/// at construction and never writes anywhere.
/// <para>
/// <b>One instance per process, and the key dies with it.</b> Every token outstanding when the
/// app restarts is refused after it; Claude Code runs its <c>headersHelper</c> again on the
/// 401, so a restart heals itself. The broker that mints and the listener that validates are
/// in the same process and so share one instance, which is also why the clock skew is zero.
/// </para>
/// <para>
/// <b>Lifetime is checked here, against the injected <see cref="TimeProvider"/>,</b> rather
/// than by the handler, whose clock cannot be injected: the handler validates the signature,
/// the algorithm, the issuer and the audience with its own lifetime check off, and
/// <see cref="ValidateAsync"/> then reads <c>nbf</c> and <c>exp</c> itself.
/// </para>
/// <para>
/// <b>The grant is looked up on every validation,</b> because that is what makes a revoke
/// immediate: a token is refused once its grant is revoked, and once the stored version has
/// moved past the token's <c>ver</c>.
/// </para>
/// <para>
/// No method logs, and no refusal carries the token: the caller logs the grant id and the
/// refusal and nothing else.
/// </para>
/// </summary>
public sealed class McpGrantTokens
{
    /// <summary>What a validated principal's identity is authenticated as.</summary>
    public const string AuthenticationType = "mcp-grant";

    private readonly IMcpGrantStore _grants;
    private readonly TimeProvider _clock;
    private readonly SymmetricSecurityKey _key;
    private readonly JsonWebTokenHandler _handler = new();
    private readonly TokenValidationParameters _parameters;

    public McpGrantTokens(IMcpGrantStore grants, TimeProvider clock)
        : this(grants, clock, RandomNumberGenerator.GetBytes(32))
    {
    }

    /// <summary>With the signing key given rather than drawn, so a test can sign a token this
    /// instance did not mint. Internal, so the container never picks it.</summary>
    internal McpGrantTokens(IMcpGrantStore grants, TimeProvider clock, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(key);

        _grants = grants;
        _clock = clock;
        _key = new SymmetricSecurityKey(key);
        _parameters = new TokenValidationParameters
        {
            ValidIssuer = McpGrantClaims.Issuer,
            ValidAudience = McpGrantClaims.Audience,
            IssuerSigningKey = _key,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            // Read in ValidateAsync against the injected clock; see the class summary.
            ValidateLifetime = false,
            ClockSkew = TimeSpan.Zero,
            AuthenticationType = AuthenticationType,
            NameClaimType = JwtRegisteredClaimNames.Sub
        };
    }

    /// <summary>
    /// A token for the grant <paramref name="grantId"/>, or the reason there is none: no such
    /// grant, or a revoked one.
    /// </summary>
    public McpGrantMint Mint(string grantId)
    {
        var grant = string.IsNullOrWhiteSpace(grantId) ? null : _grants.Find(grantId);

        if (grant is null) return McpGrantMint.Refused(McpGrantRefusal.UnknownGrant);
        if (grant.Revoked) return McpGrantMint.Refused(McpGrantRefusal.Revoked);

        // Whole seconds, because that is all a JWT's times hold: the expiry this reports is
        // then exactly the one the token carries.
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(_clock.GetUtcNow().ToUnixTimeSeconds());
        var expiresAt = issuedAt + McpGrantClaims.Lifetime;

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = McpGrantClaims.Issuer,
            Audience = McpGrantClaims.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [JwtRegisteredClaimNames.Sub] = grant.Id,
                [McpGrantClaims.Scope] = string.Join(' ', grant.Scopes),
                [McpGrantClaims.Version] = grant.Version
            },
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256)
        });

        return McpGrantMint.Minted(new McpGrantToken(token, expiresAt));
    }

    /// <summary>
    /// The grant and principal <paramref name="token"/> stands for, or the first reason it is
    /// refused: not a token this process signed for this audience with HS256, outside its
    /// lifetime, or for a grant that is unknown, revoked or at a later version.
    /// </summary>
    public async Task<McpGrantValidation> ValidateAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return McpGrantValidation.Refused(McpGrantRefusal.BadToken);

        cancellationToken.ThrowIfCancellationRequested();

        var result = await _handler.ValidateTokenAsync(token, _parameters).ConfigureAwait(false);

        if (!result.IsValid || result.SecurityToken is not JsonWebToken jwt || result.ClaimsIdentity is null)
        {
            return McpGrantValidation.Refused(McpGrantRefusal.BadToken);
        }

        if (!jwt.TryGetPayloadValue<long>(JwtRegisteredClaimNames.Exp, out _)) return McpGrantValidation.Refused(McpGrantRefusal.BadToken);

        var now = _clock.GetUtcNow().UtcDateTime;

        if (now < jwt.ValidFrom) return McpGrantValidation.Refused(McpGrantRefusal.BadToken);
        if (now >= jwt.ValidTo) return McpGrantValidation.Refused(McpGrantRefusal.Expired);

        McpGrant? grant;

        try
        {
            grant = string.IsNullOrWhiteSpace(jwt.Subject) ? null : _grants.Find(jwt.Subject);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A grant file that cannot be read now — locked, denied, or one a write refused —
            // is a refusal the client can retry after, never an error that escapes the guard.
            return McpGrantValidation.Refused(McpGrantRefusal.StoreUnavailable);
        }

        if (grant is null) return McpGrantValidation.Refused(McpGrantRefusal.UnknownGrant);
        if (grant.Revoked) return McpGrantValidation.Refused(McpGrantRefusal.Revoked);

        if (!jwt.TryGetPayloadValue<int>(McpGrantClaims.Version, out var version) || version < grant.Version)
        {
            return McpGrantValidation.Refused(McpGrantRefusal.StaleVersion);
        }

        return McpGrantValidation.Valid(grant, new ClaimsPrincipal(result.ClaimsIdentity));
    }
}

/// <summary>Why a grant token was not minted or not accepted.</summary>
public enum McpGrantRefusal
{
    /// <summary>Not a token this process signed with HS256 for this listener, or not yet valid.</summary>
    BadToken,

    /// <summary>Past its <c>exp</c>.</summary>
    Expired,

    /// <summary>For a grant this app does not keep.</summary>
    UnknownGrant,

    /// <summary>For a revoked grant.</summary>
    Revoked,

    /// <summary>Minted under a grant version older than the stored one, or naming none.</summary>
    StaleVersion,

    /// <summary>The grant store could not be read, so the grant could not be checked.</summary>
    StoreUnavailable
}

/// <summary>A minted token and when it expires.</summary>
public sealed record McpGrantToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>What <see cref="McpGrantTokens.Mint"/> answered: a token, or why there is none.</summary>
public sealed record McpGrantMint(McpGrantToken? Token, McpGrantRefusal? Refusal)
{
    internal static McpGrantMint Minted(McpGrantToken token) => new(token, null);

    internal static McpGrantMint Refused(McpGrantRefusal refusal) => new(null, refusal);
}

/// <summary>What <see cref="McpGrantTokens.ValidateAsync"/> answered: the grant and the
/// principal the listener puts on the request, or why the token is refused.</summary>
public sealed record McpGrantValidation(McpGrant? Grant, ClaimsPrincipal? Principal, McpGrantRefusal? Refusal)
{
    internal static McpGrantValidation Valid(McpGrant grant, ClaimsPrincipal principal) => new(grant, principal, null);

    internal static McpGrantValidation Refused(McpGrantRefusal refusal) => new(null, null, refusal);
}
