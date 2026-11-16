namespace Backlog.SharedKernel.Mcp;

/// <summary>
/// One registration's grant to the desktop MCP listener (local ADR 0052 §3): Claude Code at
/// user scope, a repository's <c>.mcp.json</c>, Copilot, the telemetry exporter or the
/// question hook. A registration carries only <see cref="Id"/>, never a secret, so a
/// registration copied to another machine grants nothing there.
/// <para>
/// <see cref="Version"/> is what makes a revoke take effect at once: a token names the version
/// it was minted under, and the listener refuses one older than the stored version. A revoke
/// bumps it, a change of scopes bumps it too, and so will a later rotation.
/// </para>
/// </summary>
/// <param name="Id">The label a registration carries: <c>g_</c> and 16 random characters.
/// A label, not a password (§5).</param>
/// <param name="Name">What the person sees the grant as in Settings.</param>
/// <param name="Scopes">What the grant's tokens claim, from <see cref="McpScopes.Grantable"/>; the
/// listener holds every tool and route to one of them by a named policy (§4).</param>
/// <param name="Version">Starts at 1; every revoke and every change of scopes raises it by one.</param>
/// <param name="CreatedBy">Who created it, for the Agents card.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="Revoked">Whether it was revoked. A revoked grant mints nothing and validates nothing.</param>
/// <param name="GrantedBy">The grant of the agent that granted this one (local ADR 0052 §5,
/// agents that grant other agents), or null for a grant the person made. A delegated grant is
/// one level deep, holds no <see cref="McpScopes.NonDelegable"/> scope, and mints only within
/// what its granter holds at the time.</param>
/// <param name="ExpiresAt">When a delegated grant stops minting unless its granter renews it;
/// null for a grant the person made, which does not expire.</param>
public sealed record McpGrant(
    string Id,
    string Name,
    IReadOnlyList<string> Scopes,
    int Version,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    bool Revoked,
    string? GrantedBy = null,
    DateTimeOffset? ExpiresAt = null)
{
    /// <summary>Whether another agent granted it, rather than the person.</summary>
    public bool IsDelegated => !string.IsNullOrWhiteSpace(GrantedBy);

    /// <summary>Whether it is delegated and past its expiry at <paramref name="now"/>. A delegated
    /// grant with no expiry — a hand edit — reads as expired: it fails closed.</summary>
    public bool IsExpired(DateTimeOffset now) => IsDelegated && (ExpiresAt is not { } expires || now >= expires);
}
