using System.Security.Claims;

using Backlog.SharedKernel.Mcp;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The <c>agents:grant</c> tools (local ADR 0052 §5): each hands the caller's own principal to
/// the host and relays its answer or its refusal; every rule a granter is held to is the host's
/// (McpGrantDelegationTests). Here: the scope that guards them, the hand-off, and a host that
/// keeps no grants.
/// </summary>
public sealed class GrantToolsTests
{
    private static readonly ClaimsPrincipal Caller = new(new ClaimsIdentity([new Claim("sub", "g_aaaaaaaaaaaaaaaa")], "mcp-grant"));

    private static readonly AgentGrantView Worker = new("g_bbbbbbbbbbbbbbbb", "Worker", ["tasks:read"], new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public void All_four_tools_are_served_and_need_agents_grant()
    {
        string[] tools = ["list_agent_requests", "grant_agent", "edit_agent_scopes", "revoke_agent"];

        Assert.All(tools, tool =>
        {
            Assert.Contains(tool, BacklogMcpTools.ToolNames);
            Assert.Equal(McpScopes.AgentsGrant, McpToolScopes.ScopeOf(tool));
            Assert.DoesNotContain(tool, McpToolScopes.AnyCaller);
        });
    }

    [Fact]
    public async Task Grant_agent_hands_the_caller_the_request_and_the_trimmed_scopes_to_the_host()
    {
        var host = new FakeGrants { Answer = new AgentGrantAnswer(Worker, null, "Worker may use tasks:read.") };
        var tools = new GrantTools(new McpCallContext { User = Caller }, host);

        var answer = await tools.GrantAsync(" r_cccccccccccccccc ", [" tasks:read "], Cancel);

        Assert.Same(Worker, answer.Grant);
        var (caller, target, scopes) = Assert.Single(host.Calls);
        Assert.Same(Caller, caller);
        Assert.Equal("grant:r_cccccccccccccccc", target);
        Assert.Equal(["tasks:read"], scopes);
    }

    [Fact]
    public async Task Grant_agent_without_scopes_leaves_the_choice_to_the_host()
    {
        var host = new FakeGrants { Answer = new AgentGrantAnswer(Worker, null, "ok") };

        await new GrantTools(new McpCallContext { User = Caller }, host).GrantAsync("r_c", null, Cancel);

        Assert.Null(Assert.Single(host.Calls).Scopes);
    }

    [Fact]
    public async Task A_refusal_from_the_host_is_the_tool_s_refusal()
    {
        var host = new FakeGrants { Answer = new AgentGrantAnswer(null, "not-delegable", "agents:grant is never granted by an agent.") };
        var tools = new GrantTools(new McpCallContext { User = Caller }, host);

        var refused = await Assert.ThrowsAsync<McpException>(() => tools.EditScopesAsync("g_b", ["agents:grant"], Cancel));

        Assert.Contains("never granted by an agent", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Edit_agent_scopes_with_none_is_refused_before_the_host_is_asked()
    {
        var host = new FakeGrants { Answer = new AgentGrantAnswer(Worker, null, "ok") };

        await Assert.ThrowsAsync<McpException>(() => new GrantTools(new McpCallContext { User = Caller }, host).EditScopesAsync("g_b", [], Cancel));

        Assert.Empty(host.Calls);
    }

    [Fact]
    public async Task Revoke_and_list_hand_the_caller_on()
    {
        var host = new FakeGrants
        {
            Answer = new AgentGrantAnswer(Worker, null, "revoked"),
            Listing = new AgentGrantListing([], [Worker], null, "0 requests waiting; 1 agent granted."),
        };
        var tools = new GrantTools(new McpCallContext { User = Caller }, host);

        await tools.RevokeAsync("g_bbbbbbbbbbbbbbbb", Cancel);
        var listed = tools.List();

        Assert.Equal("revoke:g_bbbbbbbbbbbbbbbb", Assert.Single(host.Calls).Target);
        Assert.Same(Caller, host.Listed);
        Assert.Equal([Worker], listed.Granted);
    }

    [Fact]
    public async Task A_host_that_keeps_no_grants_says_so()
    {
        var tools = new GrantTools(new McpCallContext { User = Caller });

        var refused = await Assert.ThrowsAsync<McpException>(() => tools.RevokeAsync("g_b", Cancel));

        Assert.Contains("keeps no agent grants", refused.Message, StringComparison.Ordinal);
        Assert.Throws<McpException>(() => tools.List());
    }

    private sealed class FakeGrants : IAgentGrants
    {
        public AgentGrantAnswer Answer { get; init; } = new(null, "unset", "unset");

        public AgentGrantListing Listing { get; init; } = new([], [], "unset", "unset");

        public List<(ClaimsPrincipal? Caller, string Target, IReadOnlyList<string>? Scopes)> Calls { get; } = [];

        public ClaimsPrincipal? Listed { get; private set; }

        public AgentGrantListing List(ClaimsPrincipal? caller)
        {
            Listed = caller;
            return Listing;
        }

        public Task<AgentGrantAnswer> GrantAsync(ClaimsPrincipal? caller, string requestId, IReadOnlyList<string>? scopes, CancellationToken cancellationToken = default)
        {
            Calls.Add((caller, "grant:" + requestId, scopes));
            return Task.FromResult(Answer);
        }

        public Task<AgentGrantAnswer> EditScopesAsync(ClaimsPrincipal? caller, string grantId, IReadOnlyList<string> scopes, CancellationToken cancellationToken = default)
        {
            Calls.Add((caller, "edit:" + grantId, scopes));
            return Task.FromResult(Answer);
        }

        public Task<AgentGrantAnswer> RevokeAsync(ClaimsPrincipal? caller, string grantId, CancellationToken cancellationToken = default)
        {
            Calls.Add((caller, "revoke:" + grantId, null));
            return Task.FromResult(Answer);
        }
    }
}
