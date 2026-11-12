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
public sealed record McpGrant(
    string Id,
    string Name,
    IReadOnlyList<string> Scopes,
    int Version,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    bool Revoked);
