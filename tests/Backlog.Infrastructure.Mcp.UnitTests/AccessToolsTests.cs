using System.Security.Claims;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// <c>request_access</c> (local ADR 0052 §5): it hands the caller's own principal and the scopes
/// it asks for to the host's desk and relays the answer, refuses what the desk refuses, and in a
/// host that keeps no grants says so. It holds no grant store, so it cannot change a grant.
/// </summary>
public sealed class AccessToolsTests
{
    private static readonly ClaimsPrincipal Caller = new(new ClaimsIdentity([new Claim("sub", "g_aaaaaaaaaaaaaaaa")], "mcp-grant"));

    [Fact]
    public async Task It_asks_for_the_scopes_on_behalf_of_the_calling_principal_and_answers_the_request()
    {
        var desk = new FakeDesk(new AgentAccessAnswer("r_bbbbbbbbbbbbbbbb", true, null, "Asked the person."));
        var tools = new AccessTools(new McpCallContext { User = Caller }, desk);

        var answer = await tools.RequestAsync([" runs:write ", "tasks:write"], @"D:\Repos\Backlog", TestContext.Current.CancellationToken);

        Assert.Equal("r_bbbbbbbbbbbbbbbb", answer.RequestId);
        Assert.True(answer.WindowOpened);
        var (caller, scopes, folder) = Assert.Single(desk.Asked);
        Assert.Same(Caller, caller);
        Assert.Equal(["runs:write", "tasks:write"], scopes);
        Assert.Equal(@"D:\Repos\Backlog", folder);
    }

    [Fact]
    public async Task A_refusal_from_the_desk_is_the_tool_s_refusal()
    {
        var desk = new FakeDesk(new AgentAccessAnswer(null, false, "declined-recently", "The person declined this agent today."));
        var tools = new AccessTools(new McpCallContext { User = Caller }, desk);

        var refused = await Assert.ThrowsAsync<McpException>(() => tools.RequestAsync(["runs:write"], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("declined this agent today", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_scope_is_refused_before_the_desk_is_asked()
    {
        var desk = new FakeDesk(new AgentAccessAnswer("r_x", false, null, "x"));
        var tools = new AccessTools(new McpCallContext { User = Caller }, desk);

        await Assert.ThrowsAsync<McpException>(() => tools.RequestAsync([], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(desk.Asked);
    }

    [Fact]
    public async Task A_host_that_keeps_no_grants_says_so()
    {
        var tools = new AccessTools(new McpCallContext { User = Caller });

        var refused = await Assert.ThrowsAsync<McpException>(() => tools.RequestAsync(["runs:write"], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("keeps no agent grants", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Any_admitted_caller_may_call_it_whatever_its_scopes()
    {
        Assert.Contains(AccessTools.RequestAccess, BacklogMcpTools.ToolNames);
        Assert.Contains(AccessTools.RequestAccess, McpToolScopes.AnyCaller);
        Assert.Null(McpToolScopes.ScopeOf(AccessTools.RequestAccess));
    }

    private sealed class FakeDesk(AgentAccessAnswer answer) : IAgentAccessRequests
    {
        public List<(ClaimsPrincipal? Caller, IReadOnlyList<string> Scopes, string? Folder)> Asked { get; } = [];

        public Task<AgentAccessAnswer> RequestMoreAsync(ClaimsPrincipal? caller, IReadOnlyList<string> scopes, string? folder, CancellationToken cancellationToken = default)
        {
            Asked.Add((caller, scopes, folder));
            return Task.FromResult(answer);
        }
    }
}
