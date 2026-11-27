using Backlog.SharedKernel.Mcp;

namespace Backlog.SharedKernel.UnitTests;

/// <summary>
/// The Work scopes went by <c>tasks:*</c> before local ADR 0073 §10 renamed them. An old name
/// still means its new scope wherever one arrives, so a grant, a token or a request written
/// before the rename keeps working without the person approving it again.
/// </summary>
public sealed class McpScopesTests
{
    [Theory]
    [InlineData("tasks:read", "work:read")]
    [InlineData("tasks:write", "work:write")]
    [InlineData("tasks:admin", "work:admin")]
    public void An_old_scope_name_resolves_to_its_new_one(string old, string renamed)
    {
        Assert.Equal(renamed, McpScopes.Resolve(old));
        Assert.True(McpScopes.IsGrantable(old));
    }

    [Theory]
    [InlineData("work:read")]
    [InlineData("devbook:read")]
    [InlineData("Tasks:Read")]
    [InlineData("tasks:delete")]
    public void Any_other_name_resolves_to_itself(string scope) => Assert.Equal(scope, McpScopes.Resolve(scope));

    [Fact]
    public void The_scope_list_names_the_new_scopes_and_none_of_the_old()
    {
        Assert.Contains("work:read", McpScopes.All);
        Assert.Contains("work:write", McpScopes.All);
        Assert.Contains("work:admin", McpScopes.All);
        Assert.DoesNotContain(McpScopes.All, scope => scope.StartsWith("tasks:", StringComparison.Ordinal));
    }

    [Fact]
    public void An_old_and_a_new_name_asked_for_together_are_one_scope()
    {
        Assert.Equal(["work:read", "devbook:read"], McpScopes.Resolve(["tasks:read", "devbook:read", "work:read"]));
    }

    [Fact]
    public void The_old_admin_name_is_still_never_delegable_nor_offered_without_a_token()
    {
        Assert.False(McpScopes.IsDelegable("tasks:admin"));
        Assert.False(McpScopes.IsUnauthenticatedOfferable("tasks:admin"));
        Assert.True(McpScopes.IsDelegable("tasks:write"));
        Assert.True(McpScopes.IsUnauthenticatedOfferable("tasks:read"));
    }

    [Fact]
    public void A_grant_holding_only_an_old_name_may_still_ask_for_more()
    {
        Assert.True(McpScopes.MayRequestAccess(["tasks:read"]));
    }
}
