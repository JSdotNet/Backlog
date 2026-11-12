using Backlog.SharedKernel.Mcp;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The tool-to-scope table of local ADR 0052 §4 against the tools this library serves: every
/// served tool has exactly one grantable scope, the table names nothing that is not served, and
/// no tool needs <c>devbook:decide</c>.
/// </summary>
public sealed class McpToolScopesTests
{
    [Fact]
    public void Every_served_tool_requires_a_grantable_scope()
    {
        Assert.All(BacklogMcpTools.ToolNames, tool =>
        {
            var scope = McpToolScopes.ScopeOf(tool);

            Assert.True(scope is not null, $"{tool} is served under no scope, so no grant could call it.");
            Assert.True(McpScopes.IsGrantable(scope), $"{tool} requires '{scope}', which no grant may hold.");
        });
    }

    [Fact]
    public void The_table_names_only_served_tools() =>
        Assert.All(McpToolScopes.All.Keys, tool => Assert.Contains(tool, BacklogMcpTools.ToolNames));

    [Fact]
    public void No_served_tool_needs_devbook_decide() =>
        Assert.DoesNotContain(McpScopes.DevbookDecide, McpToolScopes.All.Values);

    [Fact]
    public void Inbox_capture_is_write_only_and_serves_no_read() =>
        Assert.DoesNotContain(McpScopes.InboxCapture, McpToolScopes.All.Values);

    [Theory]
    [InlineData("find_item", McpScopes.TasksRead)]
    [InlineData("list_entries", McpScopes.TasksRead)]
    [InlineData("get_roadmap", McpScopes.TasksRead)]
    [InlineData("list_goals", McpScopes.TasksRead)]
    [InlineData("create_item", McpScopes.TasksWrite)]
    [InlineData("capture_note", McpScopes.TasksWrite)]
    [InlineData("attach_to_goal", McpScopes.TasksWrite)]
    [InlineData("delete_goal", McpScopes.TasksAdmin)]
    [InlineData("read_knowledge_chapter", McpScopes.DevbookRead)]
    [InlineData("list_sync_directions", McpScopes.DevbookRead)]
    [InlineData("devbook_set_state", McpScopes.DevbookWrite)]
    [InlineData("resolve_annotation", McpScopes.DevbookWrite)]
    [InlineData("start_run", McpScopes.RunsWrite)]
    [InlineData("link_change", McpScopes.RunsWrite)]
    [InlineData("list_run_usage", McpScopes.RunsWrite)]
    [InlineData("ask", McpScopes.NotifyAsk)]
    [InlineData("open_window", McpScopes.UiOpen)]
    [InlineData("claim_session_request", McpScopes.SessionsDispatch)]
    [InlineData("claim_delivery", McpScopes.SessionsDispatch)]
    [InlineData("read_inbox_item", McpScopes.TasksRead)]
    [InlineData("archive_inbox_item", McpScopes.TasksWrite)]
    public void Each_tool_requires_the_scope_the_adr_table_gives_it(string tool, string scope) =>
        Assert.Equal(scope, McpToolScopes.ScopeOf(tool));

    [Fact]
    public void A_name_is_matched_without_regard_to_case_and_an_unknown_one_has_no_scope()
    {
        Assert.Equal(McpScopes.TasksAdmin, McpToolScopes.ScopeOf("DELETE_GOAL"));
        Assert.Null(McpToolScopes.ScopeOf("devbook_approve"));
        Assert.Null(McpToolScopes.ScopeOf(null));
    }
}
