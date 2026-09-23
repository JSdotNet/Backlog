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
    /// <summary>
    /// The tools that only read, named. Everything else this library publishes
    /// writes.
    /// <para>
    /// Pinned as a list rather than derived from the attributes, which would make
    /// the assertion below agree with whatever the attribute happens to say. This
    /// is the statement the attributes are checked against, and the point of it is
    /// that turning a read into a write has to be done here as well as there.
    /// </para>
    /// </summary>
    private static readonly string[] ReadOnly =
    [
        WorkTools.ListEntries,
        WorkTools.GetPlanItems,
        TrackerTools.FindItem,
        TrackerTools.ReadItem,
        RoadmapTools.GetRoadmap,
        DevbookTools.ListKnowledgeContexts,
        DevbookTools.ReadKnowledgeChapter,
        DevbookTools.ListAnnotations,
        SessionTools.ListSessions
    ];

    /// <summary>
    /// Every tool builds, under the name the catalog claims, and reaches the wire
    /// saying honestly whether it writes.
    /// <para>
    /// This asserted <c>ReadOnlyHint == true</c> for every tool once, which was a
    /// true statement about a read-only assembly and became a false one when the
    /// tracker operations arrived. It is a per-tool expectation now rather than a
    /// check the writers are excused from: the hint is what a client shows a
    /// person before it runs something, so a write claiming to be a read is worse
    /// than an unstated hint, and the four writers are held to
    /// <c>DestructiveHint == false</c> besides — none of them destroys anything,
    /// there being no delete tool to.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_tool_builds_under_its_name_and_says_honestly_whether_it_writes()
    {
        var built = Tools().ToList();

        Assert.Equal([.. BacklogMcpTools.ToolNames.Order()], [.. built.Select(tool => tool.ProtocolTool.Name).Order()]);

        foreach (var tool in built)
        {
            var annotations = tool.ProtocolTool.Annotations;

            if (ReadOnly.Contains(tool.ProtocolTool.Name, StringComparer.Ordinal))
            {
                Assert.True(
                    annotations?.ReadOnlyHint,
                    $"{tool.ProtocolTool.Name} does not reach the wire as read-only.");
            }
            else
            {
                Assert.NotEqual(true, annotations?.ReadOnlyHint);

                Assert.False(
                    annotations?.DestructiveHint,
                    $"{tool.ProtocolTool.Name} reaches the wire as destructive, and nothing here destroys an entry.");
            }

            Assert.False(
                string.IsNullOrWhiteSpace(tool.ProtocolTool.Description),
                $"{tool.ProtocolTool.Name} reaches the wire with no description.");
        }
    }

    /// <summary>The list above is the whole catalog split in two, so a tool added
    /// to neither half is a tool nobody said anything about.</summary>
    [Fact]
    public void Every_published_tool_is_on_one_side_of_the_read_write_line()
    {
        Assert.All(ReadOnly, name => Assert.Contains(name, BacklogMcpTools.ToolNames));

        var writers = BacklogMcpTools.ToolNames.Except(ReadOnly, StringComparer.Ordinal).Order();

        Assert.Equal(
            [
                TrackerTools.Comment,
                TrackerTools.CreateItem,
                TrackerTools.LinkChange,
                TrackerTools.Transition
            ],
            [.. writers]);
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

        // Every selector and every filter is advertised, because a model has to
        // be told a narrowing exists to use it — and find_item's whole contract
        // is that the caller picks one of the three rather than the tool picking
        // for them.
        Assert.Equal(
            ["id", "repoId", "externalId", "title", "status", "repository", "tag"],
            schemas["find_item"]);
        Assert.Equal(["id"], schemas["read_item"]);
        Assert.Equal(["id", "status"], schemas["transition"]);
        Assert.Equal(["id", "text"], schemas["comment"]);
        Assert.Equal(["id", "repository", "externalId"], schemas["link_change"]);
        Assert.Equal(["rawText", "repository"], schemas["create_item"]);

        // find_item requires none of its arguments at the schema level, on
        // purpose: "exactly one of these three" is not something a JSON schema
        // `required` list can say, and marking any one of them required would
        // rule out the other two.
        Assert.Empty(Required(Tools().Single(tool => tool.ProtocolTool.Name == "find_item")));

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
            [typeof(TrackerTools)] = new TrackerTools(new FakeTaskItems(), new FakeRepositoryDirectory()),
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
