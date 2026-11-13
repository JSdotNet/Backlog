using Backlog.Desktop.UI.Mcp;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Which credential a request presented, now that the shared token and the hook and telemetry
/// tokens beside it are retired (local ADR 0052, Phase 1): the <c>BACKLOG_MCP_TOKEN</c> escape
/// hatch's token where a debug setting switched it on, a grant token, or none. The predicates
/// <c>McpServerWorker.GuardAsync</c> refuses on.
/// </summary>
public sealed class McpTokenScopeTests
{
    private const string Environment = "env-token-00000000000000000000000000000000000";

    [Theory]
    [InlineData("Bearer " + Environment, McpCredential.Mcp)]
    [InlineData("Bearer someone-else", McpCredential.None)]
    [InlineData(null, McpCredential.None)]
    public void Identify_names_the_escape_hatch_token(string? authorization, McpCredential expected) =>
        Assert.Equal(expected, McpLoopbackGuard.Identify(authorization, Environment));

    /// <summary>With the escape hatch off there is no static token to match: a retired shared
    /// token, or any other, is none.</summary>
    [Theory]
    [InlineData("Bearer retired-shared-token-000000000000000000000")]
    [InlineData("Bearer ")]
    [InlineData(null)]
    public void With_the_escape_hatch_off_no_static_token_matches(string? authorization)
    {
        Assert.Equal(McpCredential.None, McpLoopbackGuard.Identify(authorization, null));
        Assert.Equal(McpCredential.None, McpLoopbackGuard.Identify(authorization, ""));
    }

    [Theory]
    [InlineData("Bearer aaa.bbb.ccc", "aaa.bbb.ccc")]
    [InlineData("bearer aaa.bbb.ccc ", "aaa.bbb.ccc")]
    [InlineData("Bearer aaa.bbb.", "aaa.bbb.")]
    [InlineData("Bearer " + Environment, null)]
    [InlineData("Bearer aaa.bbb", null)]
    [InlineData("Bearer a.b.c.d", null)]
    [InlineData("Basic aaa.bbb.ccc", null)]
    [InlineData("Bearer ", null)]
    [InlineData(null, null)]
    public void Only_a_bearer_value_shaped_like_a_JWT_is_a_grant_token_candidate(string? authorization, string? expected) =>
        Assert.Equal(expected, McpLoopbackGuard.GrantTokenCandidate(authorization));
}
