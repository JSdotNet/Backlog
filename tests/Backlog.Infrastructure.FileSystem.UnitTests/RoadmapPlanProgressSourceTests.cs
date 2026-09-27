using Backlog.Infrastructure.FileSystem.Dashboard;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// What the dashboard's tasks section is told about the plan: the roadmap items the
/// window shows, their gathered progress, the pace each is placed at, and the pace the
/// scope quotes.
/// </summary>
public class RoadmapPlanProgressSourceTests
{
    private static readonly DateOnly From = new(2026, 8, 27);
    private static readonly DateOnly To = new(2026, 9, 24);

    private static readonly PlanningPacesDto TwoWeeks = new(7m, 6m, 5m, 4m, PaceSource.LastTwoWeeks);

    private static readonly PacesInUseDto Paces = new(
        6m,
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["backlog"] = 9m, ["backlog-ide"] = 3m });

    [Fact]
    public void Only_items_whose_window_overlaps_the_dashboards_cross_both_ends_inclusive()
    {
        var items = RoadmapPlanProgressSource.InWindow(
            [
                Item("Ends on the first day", new DateOnly(2026, 8, 1), From),
                Item("Starts on the last day", To, new DateOnly(2026, 10, 30)),
                Item("Spans it", new DateOnly(2026, 8, 1), new DateOnly(2026, 12, 1)),
                Item("Ended before", new DateOnly(2026, 8, 1), From.AddDays(-1)),
                Item("Starts after", To.AddDays(1), new DateOnly(2026, 10, 30))
            ],
            From,
            To);

        Assert.Equal(["Ends on the first day", "Starts on the last day", "Spans it"], items.Select(item => item.Title));
    }

    [Fact]
    public void Each_item_carries_its_own_pace_the_lowest_across_its_repositories()
    {
        var reading = RoadmapPlanProgressSource.Map(
            [
                Item("Plan-wide"),
                Item("Backlog", aliases: ["backlog"]),
                Item("Both", aliases: ["BACKLOG", "backlog-ide"]),
                Item("Unknown", aliases: ["elsewhere"])
            ],
            new Dictionary<Guid, RoadmapItemRollupDto>(),
            TwoWeeks,
            Paces);

        Assert.Equal([6m, 9m, 3m, 6m], reading.Items.Select(item => item.PacePointsPerWeek));
    }

    [Fact]
    public void Only_an_item_the_importer_placed_by_effort_reads_as_placed_by_effort()
    {
        var reading = RoadmapPlanProgressSource.Map(
            [
                Item("By effort", placement: ImportPlacement.Effort),
                Item("By due date", placement: ImportPlacement.DueDate),
                Item("By hand")
            ],
            new Dictionary<Guid, RoadmapItemRollupDto>(),
            TwoWeeks,
            Paces);

        Assert.Equal([true, false, false], reading.Items.Select(item => item.PlacedByEffort));
    }

    [Fact]
    public void The_rollup_figures_cross_as_the_item_gathered_them()
    {
        var item = Item("Gathered", aliases: ["backlog"]);
        var rollup = new RoadmapItemRollupDto(
            [
                Link("a", 3, RoadmapProgress.Done, completedOn: new DateOnly(2026, 9, 10)),
                Link("b", 5, RoadmapProgress.InProgress),
                Link("c", null, RoadmapProgress.Done, completedOn: new DateOnly(2026, 9, 12))
            ],
            [Link("chapter", 2, progress: null)]);

        var reading = RoadmapPlanProgressSource.Map(
            [item],
            new Dictionary<Guid, RoadmapItemRollupDto> { [item.Id] = rollup },
            TwoWeeks,
            Paces);

        var only = Assert.Single(reading.Items);
        Assert.True(reading.RoadmapEnabled);
        Assert.Equal(item.Id, only.Id);
        Assert.Equal("Gathered", only.Title);
        Assert.Equal(item.Start, only.Start);
        Assert.Equal(item.End, only.End);
        Assert.Equal(["backlog"], only.RepositoryAliases);
        Assert.Equal(4, only.GatheredCount);
        Assert.Equal(2, only.DoneCount);
        Assert.Equal(10, only.TotalEffort);
        Assert.Equal(3, only.DoneEffort);
        Assert.Equal(1, only.Unestimated);
        Assert.False(only.IsFinished);
        Assert.Equal(new DateOnly(2026, 9, 12), only.LastCompletedOn);
    }

    [Fact]
    public void An_item_the_rollup_does_not_answer_for_crosses_as_having_gathered_nothing()
    {
        var reading = RoadmapPlanProgressSource.Map(
            [Item("Missing")],
            new Dictionary<Guid, RoadmapItemRollupDto>(),
            TwoWeeks,
            Paces);

        var only = Assert.Single(reading.Items);
        Assert.Equal(0, only.GatheredCount);
        Assert.Equal(0, only.DoneCount);
        Assert.Equal(0, only.TotalEffort);
        Assert.Equal(0, only.DoneEffort);
        Assert.Equal(0, only.Unestimated);
        Assert.False(only.IsFinished);
        Assert.Null(only.LastCompletedOn);
    }

    /// <summary>The pace quoted is the one the roadmap has in effect — the chosen
    /// stretch when it measured something, else the first that did, else the typed one.</summary>
    [Theory]
    [InlineData(PaceSource.LastTwoWeeks, 6, 5, 4, 6, PlanPaceBasis.LastTwoWeeks)]
    [InlineData(PaceSource.LastFourWeeks, 6, 5, 4, 5, PlanPaceBasis.LastFourWeeks)]
    [InlineData(PaceSource.LastEightWeeks, 6, 5, 4, 4, PlanPaceBasis.LastEightWeeks)]
    [InlineData(PaceSource.Manual, 6, 5, 4, 6, PlanPaceBasis.LastTwoWeeks)]
    [InlineData(PaceSource.LastTwoWeeks, null, 5, null, 5, PlanPaceBasis.LastFourWeeks)]
    [InlineData(PaceSource.LastFourWeeks, null, null, null, 7, PlanPaceBasis.Manual)]
    public void The_scopes_pace_crosses_with_the_stretch_it_was_measured_over(
        PaceSource chosen,
        int? twoWeeks,
        int? fourWeeks,
        int? eightWeeks,
        int expected,
        PlanPaceBasis basis)
    {
        var reading = RoadmapPlanProgressSource.Map(
            [],
            new Dictionary<Guid, RoadmapItemRollupDto>(),
            new PlanningPacesDto(7m, twoWeeks, fourWeeks, eightWeeks, chosen),
            Paces);

        Assert.Equal(new PlanPace(expected, basis), reading.Pace);
    }

    private static RoadmapItemDto Item(
        string title,
        DateOnly? start = null,
        DateOnly? end = null,
        string[]? aliases = null,
        ImportPlacement? placement = null) =>
        new(
            Guid.NewGuid(),
            title,
            start ?? new DateOnly(2026, 9, 1),
            end ?? new DateOnly(2026, 9, 30),
            PlanningPriority.Medium,
            aliases ?? [],
            Lane: null,
            TaskId: null,
            DependsOn: [],
            PlacedByImport: placement);

    private static RoadmapGatheredLink Link(
        string key,
        int? effort,
        RoadmapProgress? progress,
        DateOnly? completedOn = null) =>
        new(key, key, effort, RollupOrigin.Direct, progress, CompletedOn: completedOn);
}
