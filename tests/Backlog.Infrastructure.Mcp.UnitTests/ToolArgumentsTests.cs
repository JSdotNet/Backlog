using System.Reflection;
using System.Text.Json;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The arguments a delivery-surface tool is called with, read against the schema
/// the SDK built for it, before the SDK binds them.
/// <para>
/// Everything here is about the shapes agents actually send. An execute-plan worker
/// called <c>start_run</c> with its stages as a JSON-encoded string and no
/// worktree, and was answered "An error occurred invoking 'start_run'." — the
/// SDK's whole reply to any exception but an <see cref="McpException"/>, which left
/// it nothing to correct. The schemas are the real ones, built the way
/// <c>WithTools</c> builds them, so a parameter renamed or retyped on
/// <see cref="SurfaceTools"/> is a parameter these tests follow.
/// </para>
/// </summary>
public class ToolArgumentsTests
{
    private const string Worktree = @"D:\Repos\Backlog\.claude\worktrees\example";

    [Fact]
    public void Stages_sent_as_a_JSON_encoded_string_arrive_as_the_array_it_encodes()
    {
        var prepared = ToolArguments.Prepare(Tool(SurfaceTools.StartRun), Arguments(new
        {
            worktree = Worktree,
            skillId = "flow-code",
            title = "Tolerate a string",
            stages = """["Update Base","Scope","Implement"]"""
        }));

        Assert.Equal(JsonValueKind.Array, prepared["stages"].ValueKind);
        Assert.Equal(
            ["Update Base", "Scope", "Implement"],
            prepared["stages"].EnumerateArray().Select(stage => stage.GetString()));

        // Only the argument that needed it: a string the schema asks for as a
        // string stays the string it was, even when it would parse as JSON.
        Assert.Equal(Worktree, prepared["worktree"].GetString());
    }

    [Fact]
    public void A_stage_index_sent_as_a_string_arrives_as_the_integer_it_encodes()
    {
        var prepared = ToolArguments.Prepare(Tool(SurfaceTools.UpdateStage), Arguments(new
        {
            worktree = Worktree,
            runId = "run-1",
            stageIndex = "3",
            status = "done"
        }));

        Assert.Equal(3, prepared["stageIndex"].GetInt32());
    }

    [Fact]
    public void A_missing_worktree_is_refused_by_name()
    {
        var refused = Assert.Throws<McpException>(() => ToolArguments.Prepare(Tool(SurfaceTools.StartRun), Arguments(new
        {
            skillId = "flow-code",
            title = "No worktree",
            stages = new[] { "Scope" }
        })));

        Assert.Contains("worktree", refused.Message, StringComparison.Ordinal);
        Assert.Contains("start_run", refused.Message, StringComparison.Ordinal);

        // What the argument is, read off the parameter's own description, so a
        // caller that left it out learns what to send rather than only its name.
        Assert.Contains("start_run needs worktree — the full path of the worktree", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_required_argument_sent_as_null_is_refused_as_missing()
    {
        var refused = Assert.Throws<McpException>(() => ToolArguments.Prepare(Tool(SurfaceTools.FinishRun), Arguments(new
        {
            worktree = Worktree,
            runId = (string?)null,
            status = "done"
        })));

        Assert.Contains("runId", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"Scope, Implement\"", "a string")]
    [InlineData("42", "a number")]
    [InlineData("{\"0\":\"Scope\"}", "an object")]
    public void Stages_that_are_no_array_are_refused_by_name(string stages, string sentAs)
    {
        var arguments = Arguments(new { worktree = Worktree, skillId = "flow-code", title = "Bad stages" });
        arguments["stages"] = Element(stages);

        var refused = Assert.Throws<McpException>(() => ToolArguments.Prepare(Tool(SurfaceTools.StartRun), arguments));

        Assert.Contains("stages", refused.Message, StringComparison.Ordinal);
        Assert.Contains("an array", refused.Message, StringComparison.Ordinal);
        Assert.Contains(sentAs, refused.Message, StringComparison.Ordinal);
    }

    /// <summary>A null entry too: the binder would take one, and hand the port a
    /// stage with no name.</summary>
    [Theory]
    [InlineData("""["Scope", 2]""", "a number")]
    [InlineData("""["Scope", null]""", "null")]
    public void A_stage_that_is_no_string_is_refused_by_its_position(string stages, string sentAs)
    {
        var arguments = Arguments(new { worktree = Worktree, skillId = "flow-code", title = "Bad stage" });
        arguments["stages"] = Element(stages);

        var refused = Assert.Throws<McpException>(() => ToolArguments.Prepare(Tool(SurfaceTools.StartRun), arguments));

        Assert.Contains($"stages[1] must be a string; it was sent as {sentAs}.", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>The optional arguments stay optional: absent, or sent as JSON null,
    /// they pass through for the binder to default, and an argument the schema does
    /// not name is not this check's business.</summary>
    [Fact]
    public void Optional_and_unknown_arguments_pass_through_untouched()
    {
        var arguments = Arguments(new
        {
            worktree = Worktree,
            skillId = "flow-code",
            title = "Optional",
            stages = new[] { "Scope" },
            changeKind = (string?)null,
            // Not a start_run parameter: the argument the worker sent beside its
            // stages, which the binder ignores.
            resume = "false"
        });

        var prepared = ToolArguments.Prepare(Tool(SurfaceTools.StartRun), arguments);

        Assert.Equal(JsonValueKind.Null, prepared["changeKind"].ValueKind);
        Assert.Equal("false", prepared["resume"].GetString());
    }

    /// <summary>A <see cref="JsonElement"/> parameter's schema names no type, so its
    /// kind is left to the tool, which owns that refusal and that tolerance —
    /// <c>SurfaceToolsTests</c> holds both.</summary>
    [Fact]
    public void A_run_context_is_left_for_the_tool_to_read()
    {
        var arguments = Arguments(new { worktree = Worktree, runId = "run-1" });
        arguments["runContext"] = Element("\"{\\\"tracker\\\":{}}\"");

        var prepared = ToolArguments.Prepare(Tool(SurfaceTools.SetRunContext), arguments);

        Assert.Equal(JsonValueKind.String, prepared["runContext"].ValueKind);
    }

    /// <summary>Only the delivery surface's tools are read this way. Every other
    /// group keeps the SDK's own binding, whatever it is sent.</summary>
    [Fact]
    public void Only_the_delivery_surface_tools_are_prepared()
    {
        Assert.All(BacklogMcpTools.Surface.ToolNames, name => Assert.True(ToolArguments.Applies(name)));
        Assert.All(
            BacklogMcpTools.ToolNames.Except(BacklogMcpTools.Surface.ToolNames),
            name => Assert.False(ToolArguments.Applies(name)));
    }

    private static Tool Tool(string name)
    {
        var tools = new SurfaceTools(new FakeDeliverySurfaceLifecycle());

        return typeof(SurfaceTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name == name)
            .Select(method => McpServerTool.Create(method, tools).ProtocolTool)
            .Single();
    }

    private static Dictionary<string, JsonElement> Arguments(object arguments) =>
        JsonSerializer.SerializeToElement(arguments)
            .EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);

    private static JsonElement Element(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
