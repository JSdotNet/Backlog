using System.Security.Claims;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// What the <c>request_access</c> tool asks of the host (local ADR 0052 §5): record that the
/// calling agent wants more scopes, and bring the request window forward. A port, because the
/// grants, the requests and the window are the desktop head's. A host that keeps no grants —
/// the web harness — composes none, and the tool says so.
/// <para>
/// <b>It never changes the caller's grant.</b> A request for more is a new request, which only
/// the person answers, in the desktop window.
/// </para>
/// </summary>
public interface IAgentAccessRequests
{
    /// <summary>
    /// Opens a request for <paramref name="scopes"/> on behalf of the grant
    /// <paramref name="caller"/> holds, and opens or focuses its window. Refused for a caller that
    /// holds no grant, a pending grant, a scope no grant may hold, scopes it holds already, and
    /// by the desk's limits: one open request per name, five open, a day after a decline.
    /// </summary>
    Task<AgentAccessAnswer> RequestMoreAsync(
        ClaimsPrincipal? caller,
        IReadOnlyList<string> scopes,
        string? folder,
        CancellationToken cancellationToken = default);
}

/// <summary>What a request for access answered: the request and whether its window opened, or
/// the refusal's code and sentence.</summary>
/// <param name="RequestId">The request opened, or the one already open for the agent; null when refused.</param>
/// <param name="WindowOpened">Whether the request window is on screen now.</param>
/// <param name="Refusal">The refusal's code, such as <c>already-pending</c>; null when accepted.</param>
/// <param name="Message">What happened, in a sentence a model can relay.</param>
public sealed record AgentAccessAnswer(string? RequestId, bool WindowOpened, string? Refusal, string Message)
{
    /// <summary>Whether the request was refused.</summary>
    public bool Refused => Refusal is not null;
}

/// <summary>
/// The principal the listener put on the current MCP call, for a tool that must know which
/// grant called it (local ADR 0052 §5). Scoped to the request: the host's call filter sets it
/// before the tool runs, and a tool takes it from its constructor.
/// </summary>
public sealed class McpCallContext
{
    /// <summary>The caller's principal: a grant's, a static token's, or null for none.</summary>
    public ClaimsPrincipal? User { get; set; }
}
