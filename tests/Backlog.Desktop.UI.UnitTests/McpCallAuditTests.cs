using System.Text.Json;

using Backlog.Desktop.UI.Mcp;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Which id an MCP audit record names (local ADR 0052, Phase 0): the call's <c>id</c>,
/// else its first <c>…Id</c> argument, as text and never longer than the cap. The record
/// itself being written per call, refused or not, is McpEndpointGateTests' over the real
/// endpoint.
/// </summary>
public sealed class McpCallAuditTests
{
    private static Dictionary<string, JsonElement> Arguments(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    [Fact]
    public void The_id_argument_is_the_target()
    {
        Assert.Equal("4f1c", McpCallAudit.TargetId(Arguments("""{"sessionId":"s-1","id":"4f1c"}""")));
    }

    [Fact]
    public void Without_an_id_the_first_argument_named_for_one_is_the_target()
    {
        Assert.Equal("run-7", McpCallAudit.TargetId(Arguments("""{"worktree":"D:\\x","runId":"run-7"}""")));
    }

    [Fact]
    public void The_caller_s_own_session_id_is_never_the_target()
    {
        Assert.Equal("q-3", McpCallAudit.TargetId(Arguments("""{"sessionId":"s-1","questionId":"q-3"}""")));
        Assert.Null(McpCallAudit.TargetId(Arguments("""{"sessionId":"s-1","title":"Pick"}""")));
    }

    [Fact]
    public void A_numeric_id_is_kept_as_its_text()
    {
        Assert.Equal("582", McpCallAudit.TargetId(Arguments("""{"externalId":582}""")));
    }

    [Theory]
    [InlineData("""{"surface":"tasks"}""")]
    [InlineData("""{"id":""}""")]
    [InlineData("""{"id":null}""")]
    [InlineData("""{"Id":"bare"}""")]
    [InlineData("{}")]
    public void A_call_that_names_no_id_has_no_target(string json)
    {
        Assert.Null(McpCallAudit.TargetId(Arguments(json)));
    }

    [Fact]
    public void No_arguments_at_all_is_no_target()
    {
        Assert.Null(McpCallAudit.TargetId(null));
    }

    [Fact]
    public void An_overlong_id_is_cut_to_the_cap()
    {
        var target = McpCallAudit.TargetId(Arguments($$"""{"id":"{{new string('x', 500)}}"}"""));

        Assert.Equal(McpCallAudit.MaxTargetLength, target!.Length);
    }
}
