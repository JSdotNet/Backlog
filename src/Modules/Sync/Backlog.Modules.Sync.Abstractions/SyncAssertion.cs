namespace Backlog.Modules.Sync.Abstractions;

/// <summary>
/// What a device's <c>private_key_jwt</c> assertion (RFC 7523 §3) has to say for the
/// token exchange to take it (local ADR 0052 §2). Shared by the service that checks
/// an assertion and the client that signs one, so the two cannot drift apart.
/// <para>
/// An assertion is a compact JWT signed with the device's non-exportable P-256 key:
/// header <c>{"alg":"ES256","typ":"JWT"}</c>; claims <c>iss</c> and <c>sub</c> both the
/// device id, <c>aud</c> <see cref="Audience"/>, a random <c>jti</c>, and <c>iat</c> and
/// <c>exp</c> no more than <see cref="MaxLifetime"/> apart.
/// </para>
/// </summary>
public static class SyncAssertion
{
    /// <summary>The <c>aud</c> an assertion names. A stable identifier rather than the
    /// endpoint's absolute address, so a proxy or ingress that rewrites the host cannot
    /// make a correct assertion fail.</summary>
    public const string Audience = "backlog-sync/devices/token";

    /// <summary>The <c>client_assertion_type</c> a token request names beside the
    /// assertion (RFC 7523 §2.2).</summary>
    public const string ClientAssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    /// <summary>The only signing algorithm taken: ECDSA over P-256 with SHA-256, the
    /// signature as IEEE P1363 <c>r||s</c>.</summary>
    public const string Algorithm = "ES256";

    /// <summary>The longest span between <c>iat</c> and <c>exp</c> the service accepts,
    /// which bounds how long a seen <c>jti</c> has to be remembered.</summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromMinutes(5);
}
