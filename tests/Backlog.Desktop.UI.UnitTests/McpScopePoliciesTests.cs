using System.Security.Claims;

using Backlog.Desktop.UI.Mcp;
using Backlog.SharedKernel.Mcp;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The named scope policies and the fallback-deny behind them (local ADR 0052 §4), asked
/// through the authorization service the hosts compose: one allowed and one refused caller
/// per scope, the routes' policies, the static callers' scopes, and <c>devbook:decide</c> in
/// no policy, no token and no grant. The call filter that asks these per tool is
/// McpScopeGateTests' over the harness's endpoint.
/// </summary>
public sealed class McpScopePoliciesTests
{
    private static readonly IAuthorizationService Authorization = new ServiceCollection()
        .AddLogging()
        .AddMcpScopePolicies()
        .BuildServiceProvider()
        .GetRequiredService<IAuthorizationService>();

    private static ClaimsPrincipal Holding(params string[] scopes) => McpScopePolicies.Static("Test (g_test)", scopes);

    public static TheoryData<string> GrantableScopes => [.. McpScopes.Grantable];

    [Theory]
    [MemberData(nameof(GrantableScopes))]
    public async Task A_caller_holding_the_scope_passes_its_policy(string scope)
    {
        var result = await Authorization.AuthorizeAsync(Holding(scope), McpScopePolicies.PolicyName(scope));

        Assert.True(result.Succeeded);
    }

    [Theory]
    [MemberData(nameof(GrantableScopes))]
    public async Task A_caller_holding_every_other_scope_is_refused_by_its_policy(string scope)
    {
        var others = McpScopes.Grantable.Where(other => other != scope).ToArray();

        var result = await Authorization.AuthorizeAsync(Holding(others), McpScopePolicies.PolicyName(scope));

        Assert.False(result.Succeeded);
    }

    [Theory]
    [MemberData(nameof(GrantableScopes))]
    public async Task A_caller_with_no_identity_is_refused_by_every_scope(string scope)
    {
        var result = await Authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), McpScopePolicies.PolicyName(scope));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Devbook_decide_has_no_policy_and_no_caller_holds_it()
    {
        var policies = new ServiceCollection().AddLogging().AddMcpScopePolicies().BuildServiceProvider()
            .GetRequiredService<IAuthorizationPolicyProvider>();

        Assert.Null(await policies.GetPolicyAsync(McpScopePolicies.PolicyName(McpScopes.DevbookDecide)));
        Assert.DoesNotContain(McpScopes.DevbookDecide, McpScopes.Grantable);

        // A principal that claims it anyway carries it nowhere a policy reads.
        Assert.DoesNotContain(McpScopes.DevbookDecide, McpScopePolicies.ScopesOf(Holding(McpScopes.DevbookDecide)));
        Assert.DoesNotContain(McpScopes.DevbookDecide, McpScopePolicies.ScopesOf(McpScopePolicies.SharedTokenCaller));
        Assert.DoesNotContain(McpScopes.DevbookDecide, McpScopePolicies.ScopesOf(McpScopePolicies.HarnessCaller));
    }

    [Fact]
    public async Task The_fallback_policy_refuses_every_caller_the_harness_included()
    {
        Assert.False((await Authorization.AuthorizeAsync(McpScopePolicies.HarnessCaller, McpScopePolicies.Fallback)).Succeeded);
        Assert.False((await Authorization.AuthorizeAsync(McpScopePolicies.SharedTokenCaller, McpScopePolicies.Fallback)).Succeeded);
    }

    [Fact]
    public async Task A_tool_no_scope_serves_meets_the_fallback()
    {
        Assert.Equal(McpScopePolicies.Fallback, McpScopePolicies.ToolPolicy("no_such_tool"));
        Assert.Equal(McpScopePolicies.Fallback, McpScopePolicies.ToolPolicy(null));
        Assert.False(await McpScopePolicies.AuthorizeToolAsync(Authorization, McpScopePolicies.HarnessCaller, "no_such_tool"));
    }

    [Fact]
    public async Task Open_window_and_so_its_link_tokens_need_ui_open()
    {
        Assert.Equal(McpScopePolicies.PolicyName(McpScopes.UiOpen), McpScopePolicies.ToolPolicy("open_window"));
        Assert.True(await McpScopePolicies.AuthorizeToolAsync(Authorization, Holding(McpScopes.UiOpen), "open_window"));
        Assert.False(await McpScopePolicies.AuthorizeToolAsync(Authorization, Holding(McpScopes.TasksRead), "open_window"));
    }

    [Theory]
    [InlineData("/mcp", McpScopePolicies.Caller)]
    [InlineData("/MCP/", McpScopePolicies.Caller)]
    [InlineData("/v1/logs", "mcp:" + McpScopes.TelemetryWrite)]
    [InlineData("/telemetry", "mcp:" + McpScopes.RunsWrite)]
    [InlineData("/session-requests/wait", "mcp:" + McpScopes.SessionsDispatch)]
    [InlineData("/hooks/ask-backlog.mjs", "mcp:" + McpScopes.NotifyAsk)]
    [InlineData("/mcp/other", McpScopePolicies.Fallback)]
    [InlineData("/anything", McpScopePolicies.Fallback)]
    [InlineData(null, McpScopePolicies.Fallback)]
    public void Each_route_names_its_policy_and_any_other_meets_the_fallback(string? path, string policy) =>
        Assert.Equal(policy, McpScopePolicies.RoutePolicy(path));

    [Fact]
    public async Task The_logs_route_admits_telemetry_write_and_refuses_the_rest()
    {
        Assert.True(await McpScopePolicies.AuthorizeRouteAsync(Authorization, Holding(McpScopes.TelemetryWrite), "/v1/logs"));
        Assert.False(await McpScopePolicies.AuthorizeRouteAsync(
            Authorization, Holding([.. McpScopes.Grantable.Where(scope => scope != McpScopes.TelemetryWrite)]), "/v1/logs"));
    }

    [Fact]
    public async Task The_wait_route_admits_sessions_dispatch_and_refuses_the_rest()
    {
        Assert.True(await McpScopePolicies.AuthorizeRouteAsync(Authorization, Holding(McpScopes.SessionsDispatch), "/session-requests/wait"));
        Assert.False(await McpScopePolicies.AuthorizeRouteAsync(Authorization, Holding(McpScopes.TasksRead), "/session-requests/wait"));
    }

    [Fact]
    public async Task Mcp_itself_admits_any_caller_and_refuses_no_identity()
    {
        Assert.True(await McpScopePolicies.AuthorizeRouteAsync(Authorization, Holding(), "/mcp"));
        Assert.False(await McpScopePolicies.AuthorizeRouteAsync(Authorization, null, "/mcp"));
    }

    [Fact]
    public async Task Agents_grant_is_a_policy_with_no_tool_yet()
    {
        Assert.True((await Authorization.AuthorizeAsync(Holding(McpScopes.AgentsGrant), McpScopePolicies.PolicyName(McpScopes.AgentsGrant))).Succeeded);
        Assert.False((await Authorization.AuthorizeAsync(Holding(McpScopes.TasksWrite), McpScopePolicies.PolicyName(McpScopes.AgentsGrant))).Succeeded);
    }

    [Fact]
    public void The_escape_hatch_and_the_harness_hold_every_grantable_scope()
    {
        // The BACKLOG_MCP_TOKEN escape hatch stands in for no particular agent (local ADR 0052,
        // Phase 1); the hook and telemetry callers went with the tokens they named.
        Assert.Equal(McpScopes.Grantable.Count, McpScopePolicies.ScopesOf(McpScopePolicies.SharedTokenCaller).Count);
        Assert.Equal(McpScopes.Grantable.Count, McpScopePolicies.ScopesOf(McpScopePolicies.HarnessCaller).Count);

        Assert.Equal(McpAudit.SharedTokenCaller, McpScopePolicies.CallerOf(McpScopePolicies.SharedTokenCaller));
        Assert.Equal(McpAudit.HarnessCaller, McpScopePolicies.CallerOf(McpScopePolicies.HarnessCaller));
        Assert.Null(McpScopePolicies.CallerOf(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Fact]
    public async Task A_grant_token_carries_its_scopes_but_never_devbook_decide_and_names_the_grant()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero));
        var grant = new McpGrant(
            "g_abcdefghijklmnop", "Backlog delivery",
            [McpScopes.TasksRead, McpScopes.DevbookDecide, "made:up", McpScopes.RunsWrite],
            1, "jobsc", clock.GetUtcNow(), Revoked: false);
        var tokens = new McpGrantTokens(new OneGrant(grant), clock);

        var minted = tokens.Mint(grant.Id);
        Assert.Equal("tasks:read runs:write", new JsonWebToken(minted.Token!.Value).GetClaim("scope").Value);

        var validated = await tokens.ValidateAsync(minted.Token.Value, TestContext.Current.CancellationToken);

        Assert.Equal("Backlog delivery (g_abcdefghijklmnop)", McpScopePolicies.CallerOf(validated.Principal));
        Assert.True(await McpScopePolicies.AuthorizeToolAsync(Authorization, validated.Principal, "read_item"));
        Assert.False(await McpScopePolicies.AuthorizeToolAsync(Authorization, validated.Principal, "comment"));
    }

    [Fact]
    public void The_grant_store_refuses_devbook_decide_and_any_scope_it_does_not_know()
    {
        var root = Path.Combine(Path.GetTempPath(), "mcp-scope-policies-tests-" + Guid.NewGuid().ToString("N"));

        try
        {
            var store = new FileMcpGrantStore(Path.Combine(root, FileMcpGrantStore.FileName), TimeProvider.System);

            Assert.Throws<ArgumentException>(() => store.Create("Agent", [McpScopes.TasksRead, McpScopes.DevbookDecide], "jobsc"));
            Assert.Throws<ArgumentException>(() => store.Create("Agent", ["Tasks:Write"], "jobsc"));
            Assert.Empty(store.List());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class OneGrant(McpGrant grant) : IMcpGrantStore
    {
        public event Action? Changed { add { } remove { } }

        public IReadOnlyList<McpGrant> List() => [grant];

        public McpGrant? Find(string id) => id == grant.Id ? grant : null;

        public McpGrant Create(string name, IReadOnlyList<string> scopes, string createdBy) => throw new NotSupportedException();

        public McpGrant? SetScopes(string id, IReadOnlyList<string> scopes) => throw new NotSupportedException();

        public McpGrant? Revoke(string id) => throw new NotSupportedException();
    }
}
