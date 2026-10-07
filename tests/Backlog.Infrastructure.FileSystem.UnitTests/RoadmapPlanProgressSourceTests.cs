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

    private static readonly DateOnly Today = new(2026, 9, 1);

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

    /// <summary>An item sized by its effort is laid out again from today, so neither its
    /// stored end nor its stored start says where it reaches: it is read whatever its
    /// stored window, and narrowed once projected.</summary>
    [Fact]
    public void An_item_sized_by_its_effort_may_reach_the_window_whatever_its_stored_window_says()
    {
        var items = RoadmapPlanProgressSource.MayReach(
            [
                Item("Stored before, sized by effort", new DateOnly(2026, 8, 1), From.AddDays(-1), placement: ImportPlacement.Effort),
                Item("Stored before, placed by hand", new DateOnly(2026, 8, 1), From.AddDays(-1)),
                Item("Starts after, sized by effort", To.AddDays(1), To.AddDays(5), placement: ImportPlacement.Effort),
                Item("Spans it", new DateOnly(2026, 8, 1), new DateOnly(2026, 12, 1))
            ],
            From,
            To);

        Assert.Equal(["Stored before, sized by effort", "Starts after, sized by effort", "Spans it"], items.Select(item => item.Title));
    }

    /// <summary>The window reported for an item sized by its effort is the one the
    /// keep-up projection gives it today (ADR 0013, ruling 5) — its gathered effort at its
    /// pace in use from today — not the one last stored, which a pace change leaves as it
    /// was (local ADR 0018). A hand-placed one is reported as stored.</summary>
    [Fact]
    public void An_item_sized_by_its_effort_crosses_with_the_window_the_projection_gives_it_today()
    {
        var sized = Item("Sized", new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 7), ["backlog"], ImportPlacement.Effort);
        var placed = Item("Placed", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5), ["backlog"]);
        var rollup = new RoadmapItemRollupDto([Link("a", 18, RoadmapProgress.Ready)], []);

        var items = RoadmapPlanProgressSource.Read(
            new RoadmapPlanDto([sized, placed], [], []),
            new Dictionary<Guid, RoadmapItemRollupDto> { [sized.Id] = rollup, [placed.Id] = rollup },
            Paces,
            today: new DateOnly(2026, 9, 1),
            From,
            To);

        // 18 points at the backlog repository's 9 a week: two weeks from today, the 1st to the 14th.
        Assert.Equal(["Sized", "Placed"], items.Select(item => item.Title));
        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14)), (items[0].Start, items[0].End));
        Assert.Equal(new DateOnly(2026, 9, 5), items[1].End);
    }

    /// <summary>An item sized by its effort that waits on another is read after that one's
    /// projected end — so one stored inside the window can be read out of it, and is
    /// narrowed away once read.</summary>
    [Fact]
    public void An_item_sized_by_its_effort_is_read_after_what_it_waits_on_and_narrowed_once_read()
    {
        var before = Item("Before", new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 7), ["backlog"], ImportPlacement.Effort);
        var after = Item("After", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5), ["backlog"], ImportPlacement.Effort) with { DependsOn = [before.Id] };
        var gathered = new Dictionary<Guid, RoadmapItemRollupDto>
        {
            [before.Id] = new([Link("a", 45, RoadmapProgress.Ready)], []),
            [after.Id] = new([Link("b", 9, RoadmapProgress.Ready)], [])
        };

        var items = RoadmapPlanProgressSource.Read(new RoadmapPlanDto([before, after], [], []), gathered, Paces, today: From, From, To);

        // 45 points at 9 a week: five weeks from Thursday 27 August, past the window's end.
        Assert.Equal(["Before"], items.Select(item => item.Title));
    }

    /// <summary>Each item crosses with a part per repository it is filed under, each at
    /// that repository's own pace — the global one for an item filed under none, or under
    /// a repository nobody configured — never one lowest pace for the whole item.</summary>
    [Fact]
    public void Each_item_carries_a_part_per_repository_each_at_its_own_pace()
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
            Paces,
            Today);

        Assert.Equal(
            ["(none) 6", "backlog 9", "backlog 9 · backlog-ide 3", "(none) 6"],
            reading.Items.Select(item => string.Join(
                " · ",
                item.Parts.Select(part => FormattableString.Invariant($"{part.Alias ?? "(none)"} {part.PacePointsPerWeek}")))));
    }

    /// <summary>
    /// A hand-placed item, 12–16 October, with 8 open points in <c>app</c> at 8 a week and
    /// 4 in <c>site</c> at 4 a week, the <c>site</c> task waiting on the <c>app</c> task:
    /// the <c>site</c> part crosses waiting on the <c>app</c> part, so the outlook can lay
    /// <c>site</c> out after it — the way the roadmap draws the work once it begins, even
    /// though the stored window is drawn as one.
    /// </summary>
    [Fact]
    public void A_part_crosses_waiting_on_the_part_holding_the_work_its_own_work_waits_on()
    {
        var paces = new PacesInUseDto(
            7m,
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["app"] = 8m, ["site"] = 4m });
        var item = Item("Hand-placed", new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 16), ["app", "site"]);
        var rollup = new RoadmapItemRollupDto(
            [
                Link("a", 8, RoadmapProgress.Ready, repositories: ["app"]),
                Link("b", 4, RoadmapProgress.Ready, repositories: ["site"], dependsOn: ["a"])
            ],
            []);

        var reading = RoadmapPlanProgressSource.Map(
            [item],
            new Dictionary<Guid, RoadmapItemRollupDto> { [item.Id] = rollup },
            TwoWeeks,
            paces,
            new DateOnly(2026, 10, 12));

        var parts = Assert.Single(reading.Items).Parts;
        Assert.Equal(
            [("app", 8, 8m, ""), ("site", 4, 4m, "0")],
            parts.Select(part => (part.Alias, part.RemainingEffort, part.PacePointsPerWeek, string.Join(",", part.WaitsOn))));
    }

    /// <summary>A part's work left is its open work that carries an estimate: a done task
    /// counts nothing, nor does an open one nobody estimated — the outlook counts it
    /// nothing, as it does in the item's planned effort — and an in-progress task counts in
    /// full.</summary>
    [Fact]
    public void A_parts_work_left_is_its_open_estimated_work()
    {
        var item = Item("In flight", aliases: ["backlog"]);
        var rollup = new RoadmapItemRollupDto(
            [
                Link("a", 3, RoadmapProgress.Done, completedOn: new DateOnly(2026, 8, 28), repositories: ["backlog"]),
                Link("b", 5, RoadmapProgress.InProgress, repositories: ["backlog"]),
                Link("c", null, RoadmapProgress.Ready, repositories: ["backlog"])
            ],
            []);

        var reading = RoadmapPlanProgressSource.Map(
            [item],
            new Dictionary<Guid, RoadmapItemRollupDto> { [item.Id] = rollup },
            TwoWeeks,
            Paces,
            Today);

        var part = Assert.Single(Assert.Single(reading.Items).Parts);
        Assert.Equal(("backlog", 5, 9m), (part.Alias, part.RemainingEffort, part.PacePointsPerWeek));
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
            Paces,
            Today);

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
            Paces,
            Today);

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
            Paces,
            Today);

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
            Paces,
            Today);

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
        DateOnly? completedOn = null,
        string[]? repositories = null,
        string[]? dependsOn = null) =>
        new(key, key, effort, RollupOrigin.Direct, progress, DependsOn: dependsOn, RepositoryIds: repositories, CompletedOn: completedOn);
}
