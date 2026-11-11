namespace Backlog.SharedKernel.Mcp;

/// <summary>
/// The fixed values of a grant token (local ADR 0052 §3): who issues it, who it is for, how
/// long it lives and the two claims it adds to the registered ones. The broker that mints a
/// token and the listener that validates it read them from here, so the two cannot disagree.
/// </summary>
public static class McpGrantClaims
{
    /// <summary>The <c>iss</c> of every grant token: the desktop app itself.</summary>
    public const string Issuer = "backlog-desktop";

    /// <summary>The <c>aud</c> of every grant token: the loopback MCP listener, and nothing
    /// else — the sync service's tokens name another audience.</summary>
    public const string Audience = "backlog-mcp-local";

    /// <summary>How long a token lives. Short, because the broker hands out another one on
    /// the next 401 without asking anybody.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    /// <summary>The grant's scopes, space-separated, as RFC 8693 writes <c>scope</c>.</summary>
    public const string Scope = "scope";

    /// <summary>The grant version the token was minted under.</summary>
    public const string Version = "ver";

    /// <summary>What every grant id starts with.</summary>
    public const string IdPrefix = "g_";
}
