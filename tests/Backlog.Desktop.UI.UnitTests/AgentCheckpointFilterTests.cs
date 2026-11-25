using Backlog.Desktop.UI.Mcp;

using ModelContextProtocol.Server;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Which tool calls take a checkpoint first: every tool not annotated read-only. The copy
/// itself, its interval and its failures are AgentCheckpointsTests'.
/// </summary>
public sealed class AgentCheckpointFilterTests
{
    private static McpServerTool Tool(bool? readOnly) =>
        McpServerTool.Create(() => "done", new McpServerToolCreateOptions { Name = "tool", ReadOnly = readOnly });

    [Fact]
    public void A_read_only_tool_takes_no_checkpoint() =>
        Assert.False(AgentCheckpointFilter.Writes(Tool(readOnly: true)));

    [Fact]
    public void A_write_tool_takes_one() =>
        Assert.True(AgentCheckpointFilter.Writes(Tool(readOnly: false)));

    [Fact]
    public void A_tool_that_does_not_say_is_treated_as_a_write() =>
        Assert.True(AgentCheckpointFilter.Writes(Tool(readOnly: null)));
}
