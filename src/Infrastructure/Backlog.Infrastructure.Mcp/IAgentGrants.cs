using System.Security.Claims;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// What the <c>agents:grant</c> tools ask of the host (local ADR 0052 §5, agents that grant other
/// agents): list the requests waiting, answer one, change or renew a grant the caller made, and
/// revoke one. A port, because the grants and the requests are the desktop head's; a host that
/// keeps no grants composes none, and the tools say so.
/// <para>
/// <b>Every rule is the host's to enforce, never the tool's:</b> a granter grants only scopes it
/// holds, never <c>devbook:decide</c>, <c>agents:grant</c> or <c>tasks:admin</c>; one level deep;
/// never its own grant or its granter's; and the grants the person or another granter made are
/// read-only to it.
/// </para>
/// </summary>
public interface IAgentGrants
{
    /// <summary>The requests waiting for an answer, and the grants <paramref name="caller"/> made.
    /// Refused for a caller that is no granter.</summary>
    AgentGrantListing List(ClaimsPrincipal? caller);

    /// <summary>Answers the open request <paramref name="requestId"/> as the granter
    /// <paramref name="caller"/>, with <paramref name="scopes"/> — or, when null, every scope it
    /// asked for that the granter may hand on.</summary>
    Task<AgentGrantAnswer> GrantAsync(
        ClaimsPrincipal? caller,
        string requestId,
        IReadOnlyList<string>? scopes,
        CancellationToken cancellationToken = default);

    /// <summary>Gives a grant <paramref name="caller"/> made new <paramref name="scopes"/>, and
    /// renews it for another seven days.</summary>
    Task<AgentGrantAnswer> EditScopesAsync(
        ClaimsPrincipal? caller,
        string grantId,
        IReadOnlyList<string> scopes,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes a grant <paramref name="caller"/> made.</summary>
    Task<AgentGrantAnswer> RevokeAsync(
        ClaimsPrincipal? caller,
        string grantId,
        CancellationToken cancellationToken = default);
}

/// <summary>A request waiting for an answer, as a granter sees it.</summary>
/// <param name="Id">The request's id, which <c>grant_agent</c> takes.</param>
/// <param name="GrantId">The grant it is for.</param>
/// <param name="Kind"><c>register</c> for a new agent, <c>more-scopes</c> for one that wants more.</param>
/// <param name="Name">The agent's name.</param>
/// <param name="Scopes">What it asked for.</param>
/// <param name="Executable">The program that asked, as a label.</param>
/// <param name="Folder">The folder it asked from, as it said.</param>
/// <param name="RequestedAt">When it asked.</param>
public sealed record AgentRequestView(
    string Id,
    string GrantId,
    string Kind,
    string Name,
    IReadOnlyList<string> Scopes,
    string? Executable,
    string? Folder,
    DateTimeOffset RequestedAt);

/// <summary>A grant the caller made.</summary>
/// <param name="Id">The grant's id, which <c>edit_agent_scopes</c> and <c>revoke_agent</c> take.</param>
/// <param name="Name">The agent's name.</param>
/// <param name="Scopes">The scopes it is stored with; a token carries only those its granter still holds.</param>
/// <param name="ExpiresAt">When it stops minting unless renewed.</param>
public sealed record AgentGrantView(string Id, string Name, IReadOnlyList<string> Scopes, DateTimeOffset? ExpiresAt);

/// <summary>What <see cref="IAgentGrants.List"/> answered.</summary>
public sealed record AgentGrantListing(
    IReadOnlyList<AgentRequestView> Requests,
    IReadOnlyList<AgentGrantView> Granted,
    string? Refusal,
    string Message)
{
    /// <summary>Whether it was refused.</summary>
    public bool Refused => Refusal is not null;
}

/// <summary>What a grant, an edit or a revoke answered: the grant as it stands, or the refusal's
/// code and sentence.</summary>
public sealed record AgentGrantAnswer(AgentGrantView? Grant, string? Refusal, string Message)
{
    /// <summary>Whether it was refused.</summary>
    public bool Refused => Refusal is not null;
}
