using System.ComponentModel;
using System.Reflection;

using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The catalog the hosts gate on, and the attributes it claims to describe.
/// <para>
/// The catalog is a second statement of what the classes below it say, which is
/// the sort of thing that drifts silently — a tool renamed in an attribute and
/// not in the table would be a tool the filter never hides. So the table is
/// checked against the attributes rather than trusted.
/// </para>
/// </summary>
public class BacklogMcpToolsTests
{
    [Fact]
    public void The_seven_read_only_tools_are_the_ones_the_item_names()
    {
        Assert.Equal(
            [
                "list_entries",
                "get_plan_items",
                "get_roadmap",
                "list_knowledge_contexts",
                "read_knowledge_chapter",
                "list_annotations",
                "list_sessions"
            ],
            BacklogMcpTools.ToolNames);
    }

    /// <summary>One check per group, each group behind its own context's flag
    /// (local ADR 0012 §7). Four groups because four bounded contexts answer
    /// here, and the roadmap is one of them: it has a key of its own and a person
    /// switching it off has switched off the thing that tool reads.</summary>
    [Fact]
    public void Each_group_reads_the_feature_key_its_area_owns()
    {
        Assert.Equal(TasksFeatures.Tasks, BacklogMcpTools.Work.FeatureKey);
        Assert.Equal(RoadmapFeatures.Roadmap, BacklogMcpTools.Roadmap.FeatureKey);
        Assert.Equal(DevbookFeatures.RepositoryDevbook, BacklogMcpTools.Devbook.FeatureKey);
        Assert.Equal(SessionFeatures.Sessions, BacklogMcpTools.Sessions.FeatureKey);

        // And no two groups share one, which is what makes the gate per area
        // rather than per tool class.
        Assert.Equal(
            BacklogMcpTools.Groups.Count,
            BacklogMcpTools.Groups.Select(group => group.FeatureKey).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>The table and the attributes are one statement. Every name the
    /// catalog claims is a name the SDK will actually publish, and every
    /// attributed method is in the catalog.</summary>
    [Fact]
    public void Every_group_lists_exactly_the_tools_its_class_is_attributed_with()
    {
        foreach (var group in BacklogMcpTools.Groups)
        {
            Assert.NotNull(group.ToolType.GetCustomAttribute<McpServerToolTypeAttribute>());

            var attributed = group.ToolType
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
                .Where(attribute => attribute is not null)
                .Select(attribute => attribute!.Name)
                .ToList();

            Assert.Equal([.. group.ToolNames.Order()], [.. attributed.Order()]);
        }
    }

    /// <summary>
    /// Every tool says it is read-only and says what it is for. The first is a
    /// claim a client shows a person before it runs anything; the second is what
    /// a model reads to decide whether to call it at all, and a tool with no
    /// description is a tool that gets called for the wrong reason.
    /// </summary>
    [Fact]
    public void Every_tool_is_declared_read_only_and_described()
    {
        foreach (var group in BacklogMcpTools.Groups)
        {
            foreach (var method in group.ToolType.GetMethods())
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() is not { } tool) continue;

                Assert.True(tool.ReadOnly, $"{tool.Name} does not declare itself read-only.");
                Assert.False(
                    string.IsNullOrWhiteSpace(method.GetCustomAttribute<DescriptionAttribute>()?.Description),
                    $"{tool.Name} has no description for a model to read.");
            }
        }
    }

    /// <summary>
    /// The tools that can reach GitHub say so, and the tools that cannot say
    /// that.
    /// <para>
    /// <c>OpenWorld</c> is contract metadata a client acts on, so it has to
    /// describe what the tool does rather than what the group would like to be
    /// true of it. The two devbook tools that resolve a knowledge folder can meet
    /// a repository whose devbook is read from a branch snapshot (local ADR
    /// 0008): <c>read_knowledge_chapter</c> fetches the named file through
    /// <c>PrepareContentAsync</c> and <c>list_knowledge_contexts</c> asks the
    /// auto-fetch to make sure the branch has been indexed. Every other tool here
    /// reads this machine and nothing else — the SQLite backlog, the roadmap
    /// store, the annotation store, the session files — and the replicated
    /// session source "reads a store and never the network".
    /// </para>
    /// <para>
    /// Pinned rather than derived, deliberately: the point of the assertion is
    /// that changing what a tool reaches for makes somebody change what it claims.
    /// </para>
    /// </summary>
    [Fact]
    public void Only_the_tools_that_can_reach_github_are_declared_open_world()
    {
        string[] openWorld = [DevbookTools.ListKnowledgeContexts, DevbookTools.ReadKnowledgeChapter];

        foreach (var group in BacklogMcpTools.Groups)
        {
            foreach (var method in group.ToolType.GetMethods())
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() is not { } tool) continue;

                Assert.Equal(openWorld.Contains(tool.Name, StringComparer.Ordinal), tool.OpenWorld);
            }
        }
    }

    /// <summary>
    /// The exposure predicate both host filters read. A group whose feature is
    /// off is absent from <c>tools/list</c> — not present and refusing — which is
    /// what feature enablement asks of every switchable capability.
    /// </summary>
    [Fact]
    public void A_group_whose_feature_is_off_is_not_exposed()
    {
        var features = Everything();

        Assert.All(BacklogMcpTools.ToolNames, name => Assert.True(BacklogMcpTools.IsExposed(name, features)));

        features.SetEnabled(TasksFeatures.Tasks, enabled: false);

        Assert.All(
            BacklogMcpTools.Work.ToolNames,
            name => Assert.False(BacklogMcpTools.IsExposed(name, features)));

        // And only that group goes.
        Assert.All(
            BacklogMcpTools.Groups
                .Where(group => group != BacklogMcpTools.Work)
                .SelectMany(group => group.ToolNames),
            name => Assert.True(BacklogMcpTools.IsExposed(name, features)));
    }

    /// <summary>
    /// The roadmap answers to the roadmap's own switch and not the backlog's.
    /// It rode with the task tools once, which meant switching the roadmap off
    /// left its tool listed and switching the backlog off took the roadmap with
    /// it — two ways for the gate to say something nobody meant.
    /// </summary>
    [Fact]
    public void The_roadmap_tool_follows_the_roadmap_switch_and_not_the_backlog()
    {
        var features = Everything();

        features.SetEnabled(RoadmapFeatures.Roadmap, enabled: false);

        Assert.False(BacklogMcpTools.IsExposed(RoadmapTools.GetRoadmap, features));
        Assert.All(BacklogMcpTools.Work.ToolNames, name => Assert.True(BacklogMcpTools.IsExposed(name, features)));

        features.SetEnabled(RoadmapFeatures.Roadmap, enabled: true);
        features.SetEnabled(TasksFeatures.Tasks, enabled: false);

        Assert.True(BacklogMcpTools.IsExposed(RoadmapTools.GetRoadmap, features));
    }

    /// <summary>A name this library does not publish is left alone: a host may
    /// map other tools onto the same server, and a filter that hid everything it
    /// did not recognise would take them with it.</summary>
    [Fact]
    public void A_tool_this_library_does_not_publish_is_left_alone()
    {
        var features = Everything();

        features.SetEnabled(TasksFeatures.Tasks, enabled: false);

        Assert.True(BacklogMcpTools.IsExposed("somebody_elses_tool", features));
        Assert.True(BacklogMcpTools.IsCallable("somebody_elses_tool", features));
    }

    /// <summary>
    /// The call path recognises this library's own names whatever case they
    /// arrive in, and refuses one whose group is off.
    /// <para>
    /// <see cref="BacklogMcpTools.IsExposed"/> answers <c>true</c> for a name it
    /// does not recognise, which is what keeps another host's tools on the same
    /// server. Matched ordinally, <c>list_ENTRIES</c> is such a name — so a
    /// dispatcher that ever routed it to <c>list_entries</c> would be routing a
    /// call the gate had waved through as somebody else's. The list filter keeps
    /// the ordinal comparison, because a published name is exact.
    /// </para>
    /// </summary>
    [Fact]
    public void The_call_path_refuses_this_librarys_own_names_in_any_case()
    {
        var features = Everything();

        features.SetEnabled(TasksFeatures.Tasks, enabled: false);

        var shouted = WorkTools.ListEntries.ToUpperInvariant();
        var mixed = "List_Entries";

        Assert.False(BacklogMcpTools.IsCallable(WorkTools.ListEntries, features));
        Assert.False(BacklogMcpTools.IsCallable(shouted, features));
        Assert.False(BacklogMcpTools.IsCallable(mixed, features));

        // The list filter is unchanged: those spellings are not names this
        // library publishes, and publishing is exact.
        Assert.True(BacklogMcpTools.IsExposed(shouted, features));
        Assert.True(BacklogMcpTools.IsExposed(mixed, features));

        // And with the group on, the call path allows them again — this is a
        // gate, not a second refusal of its own.
        features.SetEnabled(TasksFeatures.Tasks, enabled: true);

        Assert.True(BacklogMcpTools.IsCallable(shouted, features));
    }

    /// <summary>Every key the catalog names, all on. Built from the catalog so a
    /// new group cannot be added without this knowing about it — the fake throws
    /// for a key it has never heard of, the way the real store does.</summary>
    private static FakeAppFeatureSettings Everything() =>
        new([.. BacklogMcpTools.Groups.Select(group => group.FeatureKey)]);
}
