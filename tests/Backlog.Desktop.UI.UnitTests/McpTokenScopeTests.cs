using System.Text;

using Backlog.Desktop.UI.Mcp;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Which of the listener's three tokens reaches which route (the <c>split-hook-tokens</c>
/// item of the <c>zero-trust-auth</c> plan): the MCP token everywhere but the OTLP logs
/// endpoint, the telemetry token on that endpoint alone, the question hook's on the
/// question tools alone. The predicates <c>McpServerWorker.GuardAsync</c> refuses on.
/// </summary>
public sealed class McpTokenScopeTests
{
    private const string Mcp = "mcp-token-0000000000000000000000000000000000";
    private const string Hook = "hook-token-000000000000000000000000000000000";
    private const string Telemetry = "telemetry-token-0000000000000000000000000000";

    [Theory]
    [InlineData("Bearer " + Mcp, McpCredential.Mcp)]
    [InlineData("Bearer " + Hook, McpCredential.QuestionHook)]
    [InlineData("Bearer " + Telemetry, McpCredential.Telemetry)]
    [InlineData("Bearer someone-else", McpCredential.None)]
    [InlineData(null, McpCredential.None)]
    public void Identify_names_the_token_presented(string? authorization, McpCredential expected) =>
        Assert.Equal(expected, McpLoopbackGuard.Identify(authorization, Mcp, Hook, Telemetry));

    [Theory]
    [InlineData("Bearer aaa.bbb.ccc", "aaa.bbb.ccc")]
    [InlineData("bearer aaa.bbb.ccc ", "aaa.bbb.ccc")]
    [InlineData("Bearer aaa.bbb.", "aaa.bbb.")]
    [InlineData("Bearer " + Mcp, null)]
    [InlineData("Bearer aaa.bbb", null)]
    [InlineData("Bearer a.b.c.d", null)]
    [InlineData("Basic aaa.bbb.ccc", null)]
    [InlineData("Bearer ", null)]
    [InlineData(null, null)]
    public void Only_a_bearer_value_shaped_like_a_JWT_is_a_grant_token_candidate(string? authorization, string? expected) =>
        Assert.Equal(expected, McpLoopbackGuard.GrantTokenCandidate(authorization));

    [Fact]
    public void A_token_not_minted_yet_matches_nothing() =>
        Assert.Equal(McpCredential.None, McpLoopbackGuard.Identify("Bearer ", Mcp, null, ""));

    [Theory]
    // The MCP token: every route but the logs endpoint.
    [InlineData(McpCredential.Mcp, "POST", "/mcp", true)]
    [InlineData(McpCredential.Mcp, "GET", "/session-requests/wait", true)]
    [InlineData(McpCredential.Mcp, "POST", "/telemetry", true)]
    [InlineData(McpCredential.Mcp, "POST", "/v1/logs", false)]
    [InlineData(McpCredential.Mcp, "POST", "/V1/Logs/", false)]
    // The telemetry token: a POST to the logs endpoint alone.
    [InlineData(McpCredential.Telemetry, "POST", "/v1/logs", true)]
    [InlineData(McpCredential.Telemetry, "GET", "/v1/logs", false)]
    [InlineData(McpCredential.Telemetry, "POST", "/mcp", false)]
    [InlineData(McpCredential.Telemetry, "POST", "/telemetry", false)]
    [InlineData(McpCredential.Telemetry, "GET", "/session-requests/wait", false)]
    // The question hook's token: a POST to /mcp alone.
    [InlineData(McpCredential.QuestionHook, "POST", "/mcp", true)]
    [InlineData(McpCredential.QuestionHook, "GET", "/mcp", false)]
    [InlineData(McpCredential.QuestionHook, "POST", "/mcp/other", false)]
    [InlineData(McpCredential.QuestionHook, "POST", "/v1/logs", false)]
    [InlineData(McpCredential.QuestionHook, "GET", "/session-requests/wait", false)]
    // A grant token (local ADR 0052 §4): every path here, because its scopes decide next.
    [InlineData(McpCredential.Grant, "POST", "/mcp", true)]
    [InlineData(McpCredential.Grant, "GET", "/session-requests/wait", true)]
    [InlineData(McpCredential.Grant, "POST", "/telemetry", true)]
    [InlineData(McpCredential.Grant, "POST", "/v1/logs", true)]
    [InlineData(McpCredential.None, "POST", "/mcp", false)]
    public void Each_token_reaches_its_own_routes(McpCredential credential, string method, string path, bool reaches) =>
        Assert.Equal(reaches, McpLoopbackGuard.Reaches(credential, method, path));

    [Theory]
    [InlineData("notify")]
    [InlineData("ask")]
    [InlineData("get_answer")]
    [InlineData("close")]
    public void The_hook_token_calls_a_question_tool(string tool) =>
        Assert.True(McpLoopbackGuard.IsQuestionHookCall(Body(Call(tool))));

    [Theory]
    [InlineData("list_entries")]
    [InlineData("transition")]
    [InlineData("devbook_approve")]
    [InlineData("claim_delivery")]
    [InlineData("Ask")]
    [InlineData("")]
    public void The_hook_token_calls_no_other_tool(string tool) =>
        Assert.False(McpLoopbackGuard.IsQuestionHookCall(Body(Call(tool))));

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""", true)]
    [InlineData("""{"jsonrpc":"2.0","method":"notifications/initialized"}""", true)]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"ping"}""", true)]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", false)]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"resources/read","params":{"uri":"x"}}""", false)]
    [InlineData("""[{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"ask"}}]""", false)]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":42}}""", false)]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call"}""", false)]
    [InlineData("""{"method":"tools/call","params":{"name":"ask"}} trailing""", false)]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"ask","Name":"create_item"}}""", false)]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"NAME":"ask"}}""", false)]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"ask","name":"ask"}}""", false)]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","Method":"tools/call","params":{"name":"ask"}}""", false)]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"ask"},"Params":{"name":"create_item"}}""", false)]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"initialize","Params":{}}""", false)]
    [InlineData("not json", false)]
    [InlineData("", false)]
    public void Only_housekeeping_and_question_calls_pass(string body, bool passes) =>
        Assert.Equal(passes, McpLoopbackGuard.IsQuestionHookCall(Body(body)));

    private static string Call(string tool) =>
        """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":""" + "\"" + tool + "\"" + ""","arguments":{}}}""";

    private static byte[] Body(string json) => Encoding.UTF8.GetBytes(json);
}
