using System.Globalization;

using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.UI;
using Backlog.SharedKernel;
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

    /// <summary>A window sized by effort is written as the band draws it: its gathered
    /// effort at the pace in use from its planned start, not the end stored when the
    /// import placed it (local ADR 0018).</summary>
    [Fact]
    public async Task An_item_sized_by_its_effort_is_written_with_the_end_its_effort_reaches_at_the_pace_in_use()
    {
        var planning = Planning();
        Assert.True((await planning.ImportPlanItemsAsync(
            [new PlanImportEntryDto("Ship sync", "sync", RepositoryAliases: ["backlog"])],
            cancellationToken: TestContext.Current.CancellationToken)).IsSuccess);
        var stored = Assert.Single((await planning.GetPlanAsync(TestContext.Current.CancellationToken)).Items);
        // Nothing gathered at import: one working week from the first worked day.
        Assert.Equal(EffortWindow.EndFrom(stored.Start, 0, 7m, WorkingHours.Default), stored.End);

        var content = await new RoadmapAiContentSource(planning, new EveryItemGathers(14), new GlobalPace(14m))
            .ComposeAsync(new AiContentRequest("when does sync ship?", 6000), TestContext.Current.CancellationToken);

        // 14 points at 14 a week: one working week from the planned start.
        Assert.Contains(
            $"Item: Ship sync, {Iso(stored.Start)} to {Iso(EffortWindow.EndFrom(stored.Start, 14, 14m, WorkingHours.Default))},",
            content.Body,
            StringComparison.Ordinal);
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed class EveryItemGathers(int effort) : IRoadmapItemRollup
    {
        private RoadmapItemRollupDto Rollup => new([new RoadmapGatheredLink("task-1", "Task", effort, RollupOrigin.Tag)], []);

        public Task<RoadmapItemRollupDto> GatherAsync(RoadmapItemDto item, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rollup);

        public Task<IReadOnlyDictionary<Guid, RoadmapItemRollupDto>> GatherPlanAsync(
            RoadmapPlanDto plan,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, RoadmapItemRollupDto>>(plan.Items.ToDictionary(item => item.Id, _ => Rollup));
    }

    private sealed class GlobalPace(decimal pointsPerWeek) : IPlanningVelocity
    {
        public Task<PacesInUseDto> ReadPacesInUseAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PacesInUseDto(pointsPerWeek, new Dictionary<string, decimal>()));

        public Task<decimal> GetStoryPointsPerWeekAsync(
            IReadOnlyCollection<string> repositoryAliases,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(pointsPerWeek);
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
