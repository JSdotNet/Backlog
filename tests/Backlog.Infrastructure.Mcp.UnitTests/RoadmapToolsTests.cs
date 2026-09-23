using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The roadmap tool: which slice of the plan belongs to a repository, and what
/// it refuses.
/// </summary>
public class RoadmapToolsTests
{
    private static readonly TasksRepositoryRef Backlog = new("backlog", "JSdotNet", "Backlog");
    private static readonly TasksRepositoryRef Other = new("other", "JSdotNet", "Other");

    /// <summary>
    /// The plan files work under the alias a person types, where the backlog
    /// files it under the coordinate. Reading one with the other's key is the
    /// mistake this asserts against.
    /// </summary>
    [Fact]
    public async Task The_roadmap_narrows_on_the_alias_and_not_the_id()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var release = Guid.NewGuid();
        var planWide = Guid.NewGuid();

        var plan = new RoadmapPlanDto(
            [
                Item(mine, "Ours", ["backlog"]),
                Item(theirs, "Theirs", ["other"]),
                Item(Guid.NewGuid(), "Filed by id", ["JSdotNet/Backlog"])
            ],
            [
                Milestone(release, "Ship", ["backlog"]),
                Milestone(Guid.NewGuid(), "Their freeze", ["other"]),
                Milestone(planWide, "Quarter close", [], isPlanWide: true)
            ],
            [
                new PlanContradictionDto(mine, release, "Opens before the milestone it waits on."),
                new PlanContradictionDto(theirs, mine, "Crosses into another repository's slice.")
            ]);

        var tools = new RoadmapTools(new FakeRoadmapPlanning(plan), new FakeRepositoryDirectory([Backlog, Other]));

        var answer = await tools.GetRoadmapAsync("JSdotNet/Backlog", TestContext.Current.CancellationToken);

        Assert.Equal(["Ours"], answer.Items.Select(item => item.Title));

        // A plan-wide milestone is a line through every row, so it belongs to
        // this repository's reading of the plan as much as to anyone's.
        Assert.Equal(["Ship", "Quarter close"], answer.Milestones.Select(milestone => milestone.Title));

        // One end of an arrow is not a contradiction a reader can act on, so the
        // pair that reaches outside the slice does not travel.
        var contradiction = Assert.Single(answer.Contradictions);
        Assert.Equal(mine, contradiction.NodeId);
        Assert.Equal(release, contradiction.DependsOnId);
    }

    /// <summary>The same as every other scoped tool: the mapping is one helper
    /// and refusing is what it does — and it never registers what it was
    /// asked about.</summary>
    [Fact]
    public async Task The_roadmap_refuses_an_unknown_repository_without_registering_it()
    {
        var directory = new FakeRepositoryDirectory([Backlog]);
        var tools = new RoadmapTools(new FakeRoadmapPlanning(RoadmapPlanDto.Empty), directory);

        var failure = await Assert.ThrowsAsync<McpException>(() =>
            tools.GetRoadmapAsync("JSdotNet/Nowhere", TestContext.Current.CancellationToken));

        Assert.Contains("repository.not_found", failure.Message, StringComparison.Ordinal);
        Assert.Empty(directory.Registered);
    }

    private static RoadmapItemDto Item(Guid id, string title, IReadOnlyList<string> aliases) => new(
        id,
        title,
        new DateOnly(2026, 9, 1),
        new DateOnly(2026, 9, 30),
        PlanningPriority.Medium,
        aliases,
        Lane: null,
        TaskId: null,
        DependsOn: [],
        Tag: title.ToLowerInvariant());

    private static RoadmapMilestoneDto Milestone(Guid id, string title, IReadOnlyList<string> aliases, bool isPlanWide = false) => new(
        id,
        title,
        new DateOnly(2026, 9, 30),
        MilestoneKind.Release,
        aliases,
        Lane: null,
        DependsOn: [],
        isPlanWide);
}
