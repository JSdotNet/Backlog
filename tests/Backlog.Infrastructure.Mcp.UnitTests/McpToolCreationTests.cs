using System.Reflection;
using System.Text.Json;

using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Sessions.Abstractions;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The SDK's own reading of these tool classes.
/// <para>
/// Everything else here tests what a tool answers once it has been called. This
/// tests that it can be registered at all — the schema the SDK builds from a
/// method's parameters is built by reflection at startup, so a return type or a
/// parameter shape it will not take is a failure the host meets on its first
/// <c>tools/list</c> and no unit test about behaviour would ever see.
/// </para>
/// <para>
/// <c>McpServerTool.Create</c> rather than <c>WithTools&lt;T&gt;()</c> because
/// the latter lives in the hosting package, which this library deliberately does
/// not reference. It is the same call <c>WithTools</c> makes.
/// </para>
/// </summary>
public class McpToolCreationTests
{
    /// <summary>Every tool builds, under the name the catalog claims, declaring
    /// itself read-only to a client that shows a person what it is about to
    /// run.</summary>
    [Fact]
    public void Every_tool_builds_with_the_name_and_the_read_only_hint_it_claims()
    {
        var built = Tools().ToList();

        Assert.Equal([.. BacklogMcpTools.ToolNames.Order()], [.. built.Select(tool => tool.ProtocolTool.Name).Order()]);

        foreach (var tool in built)
        {
            Assert.True(
                tool.ProtocolTool.Annotations?.ReadOnlyHint,
                $"{tool.ProtocolTool.Name} does not reach the wire as read-only.");

            Assert.False(
                string.IsNullOrWhiteSpace(tool.ProtocolTool.Description),
                $"{tool.ProtocolTool.Name} reaches the wire with no description.");
        }
    }

    /// <summary>
    /// The input schema is the arguments a session has to supply, and nothing
    /// else. A <see cref="CancellationToken"/> is bound by the SDK and must not
    /// appear as a parameter a model is invited to fill in.
    /// </summary>
    [Fact]
    public void The_schemas_ask_for_the_arguments_and_not_the_cancellation_token()
    {
        var schemas = Tools().ToDictionary(
            tool => tool.ProtocolTool.Name,
            tool => Properties(tool.ProtocolTool.InputSchema));

        Assert.Equal(["repository"], schemas["list_entries"]);
        Assert.Equal(["planId"], schemas["get_plan_items"]);
        Assert.Equal(["repository"], schemas["get_roadmap"]);
        Assert.Equal(["repository"], schemas["list_knowledge_contexts"]);
        Assert.Equal(["repository", "chapterPath", "review"], schemas["read_knowledge_chapter"]);
        Assert.Equal(["repository", "chapterPath"], schemas["list_annotations"]);

        // Optional, and optional means absent from `required` rather than absent
        // from the schema: a model has to be told the narrowing exists to use it.
        Assert.Equal(["repository"], schemas["list_sessions"]);
        Assert.DoesNotContain("repository", Required(Tools().Single(tool => tool.ProtocolTool.Name == "list_sessions")));
    }

    private static IEnumerable<McpServerTool> Tools()
    {
        var targets = new Dictionary<Type, object>
        {
            [typeof(WorkTools)] = new WorkTools(new FakeTaskItems(), new FakeRepositoryDirectory()),
            [typeof(RoadmapTools)] = new RoadmapTools(
                new FakeRoadmapPlanning(RoadmapPlanDto.Empty),
                new FakeRepositoryDirectory()),
            [typeof(DevbookTools)] = new DevbookTools(
                new FakeDevbookFolderSource(Path.GetTempPath(), new DevbookFolderSetting(".arc42", "Architecture", ".arc42")),
                new FakeDevbookAnnotationStore(),
                new FakeRepositoryDirectory()),
            [typeof(SessionTools)] = new SessionTools(
                new FakeAgentSessionSource(AgentSessionCatalog.Empty),
                new FakeRepositoryDirectory())
        };

        foreach (var group in BacklogMcpTools.Groups)
        {
            foreach (var method in group.ToolType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() is null) continue;

                yield return McpServerTool.Create(method, targets[group.ToolType]);
            }
        }
    }

    private static IReadOnlyList<string> Required(McpServerTool tool) =>
        tool.ProtocolTool.InputSchema.TryGetProperty("required", out var required)
            ? [.. required.EnumerateArray().Select(name => name.GetString() ?? string.Empty)]
            : [];

    private static IReadOnlyList<string> Properties(JsonElement schema) =>
        schema.TryGetProperty("properties", out var properties)
            ? [.. properties.EnumerateObject().Select(property => property.Name)]
            : [];
}
