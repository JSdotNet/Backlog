using System.ComponentModel;

using Backlog.SharedKernel.Results;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// The <c>agents:grant</c> tools (local ADR 0052 §5, agents that grant other agents): an
/// orchestrator the person made a granter — the Manager session, a delivery flow that starts a
/// worker — answers other agents' requests and grants, trims, renews or revokes them without the
/// person, inside the rules the host enforces. Every refusal names the rule it met.
/// <para>
/// <b>Ungated</b>, like <c>request_access</c>: the scope decides who may call them, and a host
/// that keeps no grants composes no <see cref="IAgentGrants"/>, so each tool says so.
/// </para>
/// </summary>
/// <param name="call">The principal the listener put on this call.</param>
/// <param name="grants">The host's grants, or null in a host that keeps none.</param>
[McpServerToolType]
public sealed class GrantTools(McpCallContext call, IAgentGrants? grants = null)
{
    internal const string ListAgentRequests = "list_agent_requests";
    internal const string GrantAgent = "grant_agent";
    internal const string EditAgentScopes = "edit_agent_scopes";
    internal const string RevokeAgent = "revoke_agent";

    /// <summary>The group the granting tools are served in: behind no key.</summary>
    public static BacklogMcpTools.McpToolGroup Group { get; } = new(
        FeatureKey: null,
        typeof(GrantTools),
        [ListAgentRequests, GrantAgent, EditAgentScopes, RevokeAgent]);

    [McpServerTool(
        Name = ListAgentRequests,
        Title = "List agents' access requests",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    [Description(
        "Lists the agents waiting for access to Backlog — new agents that registered themselves, and agents asking for "
        + "more scopes — and the agents this granter has granted, with their scopes and expiry. Needs agents:grant.")]
    public AgentRequestsPayload List()
    {
        var listing = Host().List(call.User);
        if (listing.Refused) throw Refusal(listing.Refusal!, listing.Message);

        return new AgentRequestsPayload(listing.Requests, listing.Granted, listing.Message);
    }

    [McpServerTool(
        Name = GrantAgent,
        Title = "Grant an agent's request",
        ReadOnly = false,
        Idempotent = false,
        Destructive = false,
        OpenWorld = false)]
    [Description(
        "Answers an agent's waiting access request as this granter. Only scopes this granter holds itself; never "
        + "devbook:decide, agents:grant or tasks:admin; never this granter's own grant or its granter's; and an agent "
        + "the person or another granter granted is read-only here. The grant expires after 7 days unless renewed with "
        + "edit_agent_scopes, and its tokens never carry more than this granter holds at the time. The person is told.")]
    public async Task<AgentGrantPayload> GrantAsync(
        [Description("The request's id, from list_agent_requests: r_ and 16 characters.")]
        string requestId,
        [Description("Optional. The scopes to grant, a subset of what it asked for. Omit to grant every scope it asked for that this granter may hand on.")]
        string[]? scopes = null,
        CancellationToken cancellationToken = default)
    {
        var answer = await Host()
            .GrantAsync(call.User, requestId?.Trim() ?? string.Empty, scopes is null ? null : Clean(scopes), cancellationToken)
            .ConfigureAwait(false);

        return Answer(answer);
    }

    [McpServerTool(
        Name = EditAgentScopes,
        Title = "Change a granted agent's scopes",
        ReadOnly = false,
        Idempotent = false,
        Destructive = false,
        OpenWorld = false)]
    [Description(
        "Replaces the scopes of an agent this granter granted, and renews it for another 7 days. Calling it with the "
        + "scopes it has is how a grant is renewed. Only for agents this granter granted; the same scope rules as "
        + "grant_agent apply. The change reaches the agent with its next token.")]
    public async Task<AgentGrantPayload> EditScopesAsync(
        [Description("The agent's grant id, from list_agent_requests: g_ and 16 characters.")]
        string grantId,
        [Description("The scopes it should hold, such as [\"tasks:read\", \"tasks:write\"].")]
        string[] scopes,
        CancellationToken cancellationToken = default)
    {
        if (scopes is not { Length: > 0 })
        {
            throw Refusal("scopes-required", "Name at least one scope. To take every scope away, revoke the agent.");
        }

        var answer = await Host()
            .EditScopesAsync(call.User, grantId?.Trim() ?? string.Empty, Clean(scopes), cancellationToken)
            .ConfigureAwait(false);

        return Answer(answer);
    }

    [McpServerTool(
        Name = RevokeAgent,
        Title = "Revoke a granted agent",
        ReadOnly = false,
        Idempotent = false,
        Destructive = true,
        OpenWorld = false)]
    [Description(
        "Revokes an agent this granter granted: its sessions lose access at once. Only for agents this granter "
        + "granted; the person revokes the others in Backlog.")]
    public async Task<AgentGrantPayload> RevokeAsync(
        [Description("The agent's grant id, from list_agent_requests: g_ and 16 characters.")]
        string grantId,
        CancellationToken cancellationToken = default)
    {
        var answer = await Host()
            .RevokeAsync(call.User, grantId?.Trim() ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);

        return Answer(answer);
    }

    private IAgentGrants Host() =>
        grants ?? throw Refusal(
            "not-available",
            "This host keeps no agent grants, so there is nothing to grant: every caller reaches every tool here.");

    private static AgentGrantPayload Answer(AgentGrantAnswer answer) =>
        answer.Refused ? throw Refusal(answer.Refusal!, answer.Message) : new AgentGrantPayload(answer.Grant, answer.Message);

    private static string[] Clean(string[] scopes) => [.. scopes.Select(scope => scope?.Trim() ?? string.Empty)];

    private static Exception Refusal(string code, string message) =>
        RepositoryScope.Failure(Error.Validation("agents." + code.Replace('-', '_'), message));
}

/// <summary>What <c>list_agent_requests</c> answers.</summary>
/// <param name="Requests">The requests waiting for an answer, oldest first.</param>
/// <param name="Granted">The agents this granter granted and has not revoked.</param>
/// <param name="Answer">The sentence to report.</param>
public sealed record AgentRequestsPayload(
    IReadOnlyList<AgentRequestView> Requests,
    IReadOnlyList<AgentGrantView> Granted,
    string Answer);

/// <summary>What <c>grant_agent</c>, <c>edit_agent_scopes</c> and <c>revoke_agent</c> answer.</summary>
/// <param name="Grant">The grant as it stands now.</param>
/// <param name="Answer">The sentence to report.</param>
public sealed record AgentGrantPayload(AgentGrantView? Grant, string Answer);
