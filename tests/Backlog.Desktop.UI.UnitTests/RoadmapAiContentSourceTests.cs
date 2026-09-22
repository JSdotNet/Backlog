using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.UI;
using Backlog.SharedKernel.Ai;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Roadmap's Ask AI content: the plan's milestones and items, as the
/// planning port holds them, in the shape the assistant reads.
/// </summary>
public sealed class RoadmapAiContentSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-roadmap-ai-content-tests", Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task Milestones_come_first_and_each_record_names_the_dates_and_the_scope()
    {
        var planning = Planning();
        var milestone = await planning.AddMilestoneAsync("1.0", new DateOnly(2026, 3, 31), MilestoneKind.Release, ["backlog"], cancellationToken: TestContext.Current.CancellationToken);
        var item = await planning.AddItemAsync(
            "Ship sync",
            new DateOnly(2026, 3, 2),
            new DateOnly(2026, 3, 20),
            PlanningPriority.High,
            repositoryAliases: ["backlog"],
            lane: "Platform",
            notes: "Needs the Cosmos emulator.",
            tag: "sync",
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await planning.AddDependencyAsync(milestone.Value.Id, item.Value.Id, TestContext.Current.CancellationToken) is { IsSuccess: true });

        var content = await new RoadmapAiContentSource(planning).ComposeAsync(new AiContentRequest("when does sync ship?", 6000), TestContext.Current.CancellationToken);

        Assert.Equal("roadmap", content.AreaKey);
        Assert.Equal(
            "Roadmap: 2 entries.\n"
            + "Milestone: 1.0, on 2026-03-31, release, repository backlog\n"
            + "---\n"
            + "Item: Ship sync, 2026-03-02 to 2026-03-20, high priority, repository backlog, lane Platform, tagged +sync\n"
            + "Needs the Cosmos emulator.",
            content.Body);
    }

    [Fact]
    public async Task An_item_that_waits_on_a_milestone_names_it()
    {
        var planning = Planning();
        var milestone = await planning.AddMilestoneAsync("Freeze", new DateOnly(2026, 4, 1), MilestoneKind.Freeze, cancellationToken: TestContext.Current.CancellationToken);
        var item = await planning.AddItemAsync("Polish", new DateOnly(2026, 4, 2), new DateOnly(2026, 4, 3), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await planning.AddDependencyAsync(item.Value.Id, milestone.Value.Id, TestContext.Current.CancellationToken) is { IsSuccess: true });

        var content = await new RoadmapAiContentSource(planning).ComposeAsync(new AiContentRequest("polish", 6000), TestContext.Current.CancellationToken);

        Assert.Contains("Item: Polish, 2026-04-02 to 2026-04-03, medium priority, before milestone Freeze", content.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_plan_is_an_empty_body_with_its_first_line()
    {
        var content = await new RoadmapAiContentSource(Planning()).ComposeAsync(new AiContentRequest("anything", 6000), TestContext.Current.CancellationToken);

        Assert.Equal("Roadmap: 0 entries.", content.Body);
        Assert.Equal(0, content.Total);
    }

    private IRoadmapPlanning Planning() =>
        TasksTestHost.PlanningFor(new WorkspaceSettingsStore(Path.Combine(_root, "store")));

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
