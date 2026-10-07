using System.Globalization;
using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.Services;
using Backlog.SharedKernel;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Dashboard.UnitTests;

/// <summary>
/// The tasks section: tasks ticked off and their story points per ISO week, and the
/// roadmap items the window shows with how each one's work stands — both narrowed by the
/// repository scope in this module rather than by the source.
/// </summary>
public class TaskInsightsTests
{
    /// <summary>Thursday of ISO week 39, 2026. Four weeks back is Thursday of week 35,
    /// so the window's buckets are W35 to W39 and the first starts Monday 24 August.</summary>
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly DashboardScope FourWeeks = new(Period: DashboardPeriod.FourWeeks);

    [Fact]
    public async Task Counts_and_effort_land_in_the_iso_week_each_task_was_ticked_off_in()
    {
        var source = new StubSource(
            Ticked(2026, 9, 21, 3),
            Ticked(2026, 9, 22, null),
            Ticked(2026, 9, 14, 5),
            Ticked(2026, 8, 24, 2),
            Ticked(2026, 8, 23, null));

        var result = await Insights(source).GetThroughputAsync(FourWeeks, TestContext.Current.CancellationToken);

        Assert.True(result.HasValue);
        Assert.Equal(["W35", "W36", "W37", "W38", "W39"], result.Value!.CompletedPerWeek.Select(point => point.Label));
        Assert.Equal([1m, 0m, 0m, 1m, 2m], result.Value.CompletedPerWeek.Select(point => point.Value));
        Assert.Equal([2m, 0m, 0m, 5m, 3m], result.Value.EffortPerWeek.Select(point => point.Value));
        Assert.Equal(4, result.Value.Completed);
        Assert.Equal(10m, result.Value.Effort);

        // The unestimated task from the Sunday before the window is not counted as one.
        Assert.Equal(1, result.Value.Unestimated);
    }

    [Fact]
    public async Task The_source_is_asked_from_the_monday_the_first_week_starts_on()
    {
        var source = new StubSource();

        _ = await Insights(source).GetThroughputAsync(FourWeeks, TestContext.Current.CancellationToken);

        Assert.Equal(new DateOnly(2026, 8, 24), source.Since);
    }

    [Fact]
    public async Task A_task_counts_when_any_of_its_repositories_is_in_scope()
    {
        var source = new StubSource(
            Ticked(2026, 9, 21, 1, "backlog"),
            Ticked(2026, 9, 21, 2, "backlog-ide", "backlog"),
            Ticked(2026, 9, 21, 4),
            Ticked(2026, 9, 21, 8, "other"));

        var narrowed = await Insights(source).GetThroughputAsync(
            FourWeeks with { Repositories = RepositoryFocus.Of("BACKLOG") },
            TestContext.Current.CancellationToken);
        var all = await Insights(source).GetThroughputAsync(FourWeeks, TestContext.Current.CancellationToken);

        Assert.Equal(2, narrowed.Value!.Completed);
        Assert.Equal(3m, narrowed.Value.Effort);

        // No chips is the whole backlog, including a task that names no repository.
        Assert.Equal(4, all.Value!.Completed);
        Assert.Equal(15m, all.Value.Effort);
    }

    [Fact]
    public async Task A_source_that_fails_reads_as_unavailable_with_its_reason()
    {
        var source = new StubSource { Throw = new InvalidOperationException("The database is locked.") };

        var result = await Insights(source).GetThroughputAsync(FourWeeks, TestContext.Current.CancellationToken);

        Assert.False(result.HasValue);
        Assert.Contains("The database is locked.", result.Availability.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_plan_source_is_asked_for_the_window_dates_and_the_repositories_in_scope()
    {
        var plan = new StubPlanSource();

        _ = await Insights(plan).GetPlanAsync(
            FourWeeks with { Repositories = RepositoryFocus.Of("backlog", "backlog-ide") },
            TestContext.Current.CancellationToken);

        Assert.Equal(new DateOnly(2026, 8, 27), plan.From);
        Assert.Equal(new DateOnly(2026, 9, 24), plan.To);
        Assert.Equal(["backlog", "backlog-ide"], plan.Aliases);
    }

    [Fact]
    public async Task The_plan_carries_the_window_it_was_read_for_so_the_timeline_draws_its_weeks()
    {
        var plan = new StubPlanSource(Item("Sync MVP"));

        var result = await Insights(plan).GetPlanAsync(FourWeeks, TestContext.Current.CancellationToken);

        Assert.Equal(new DateOnly(2026, 8, 27), result.Value!.WindowFrom);
        Assert.Equal(new DateOnly(2026, 9, 24), result.Value.WindowTo);
    }

    [Fact]
    public async Task All_repositories_asks_the_plan_source_for_no_repository()
    {
        var plan = new StubPlanSource();

        _ = await Insights(plan).GetPlanAsync(FourWeeks, TestContext.Current.CancellationToken);

        Assert.Empty(plan.Aliases!);
    }

    [Fact]
    public async Task A_roadmap_that_is_switched_off_reads_as_ready_with_no_plan()
    {
        var plan = new StubPlanSource(Item("Ignored")) { Enabled = false };

        var result = await Insights(plan).GetPlanAsync(FourWeeks, TestContext.Current.CancellationToken);

        Assert.True(result.HasValue);
        Assert.False(result.Value!.RoadmapEnabled);
        Assert.Empty(result.Value.Items);
    }

    [Fact]
    public async Task An_item_naming_a_repository_out_of_scope_is_dropped_and_a_plan_wide_one_is_kept()
    {
        var plan = new StubPlanSource(
            Item("Elsewhere", aliases: ["other"]),
            Item("Plan-wide"),
            Item("Shared", aliases: ["other", "Backlog"]));

        var narrowed = await Insights(plan).GetPlanAsync(
            FourWeeks with { Repositories = RepositoryFocus.Of("backlog") },
            TestContext.Current.CancellationToken);
        var all = await Insights(plan).GetPlanAsync(FourWeeks, TestContext.Current.CancellationToken);

        Assert.Equal(["Plan-wide", "Shared"], narrowed.Value!.Items.Select(item => item.Item.Title));
        Assert.Equal(3, all.Value!.Items.Count);
    }

    [Fact]
    public async Task Items_are_ordered_by_start_then_title()
    {
        var plan = new StubPlanSource(
            Item("b", start: new DateOnly(2026, 9, 1)),
            Item("a", start: new DateOnly(2026, 9, 1)),
            Item("c", start: new DateOnly(2026, 8, 20)));

        var result = await Insights(plan).GetPlanAsync(FourWeeks, TestContext.Current.CancellationToken);

        Assert.Equal(["c", "a", "b"], result.Value!.Items.Select(item => item.Item.Title));
    }

    /// <summary>
    /// Today is Thursday 24 September. The projection lays the effort left out from
    /// today at the item's own pace, through the default working week's hours (local ADR
    /// 0019) — seven points a week is 42.5 hours, skipping the weekend — and judges the
    /// last day it lands on against the planned end.
    /// </summary>
    [Theory]
    [InlineData(true, false, 10, 4, 7, "2026-09-30", PlanOutlook.Finished, "2026-09-20")]
    [InlineData(false, true, 10, 0, 7, "2026-09-26", PlanOutlook.PlacedByEffort, "2026-09-26")]
    [InlineData(false, false, 0, 0, 7, "2026-09-30", PlanOutlook.Unsized, null)]
    [InlineData(false, false, 5, 0, 0, "2026-09-30", PlanOutlook.NoPace, null)]
    [InlineData(false, false, 5, 0, 7, "2026-09-23", PlanOutlook.Overdue, "2026-09-29")]
    [InlineData(false, false, 10, 5, 7, "2026-09-30", PlanOutlook.OnTrack, "2026-09-29")]
    [InlineData(false, false, 10, 0, 7, "2026-09-26", PlanOutlook.Behind, "2026-10-05")]
    [InlineData(false, false, 4, 4, 7, "2026-09-24", PlanOutlook.OnTrack, "2026-09-24")]
    [InlineData(false, false, 3, 0, 14, "2026-09-25", PlanOutlook.OnTrack, "2026-09-25")]
    public async Task The_outlook_projects_the_effort_left_at_the_items_own_pace(
        bool finished,
        bool placedByEffort,
        int totalEffort,
        int doneEffort,
        int pace,
        string end,
        PlanOutlook expected,
        string? projected)
    {
        var plan = new StubPlanSource(Item(
            "Item",
            end: DateOnly.Parse(end, CultureInfo.InvariantCulture),
            totalEffort: totalEffort,
            doneEffort: doneEffort,
            finished: finished,
            lastCompletedOn: new DateOnly(2026, 9, 20),
            pace: pace,
            placedByEffort: placedByEffort));

        var result = await Insights(plan).GetPlanAsync(FourWeeks, TestContext.Current.CancellationToken);

        var only = Assert.Single(result.Value!.Items);
        Assert.Equal(expected, only.Outlook);
        Assert.Equal(projected is null ? null : DateOnly.Parse(projected, CultureInfo.InvariantCulture), only.ProjectedEnd);
    }

    /// <summary>The outlook projects through the week the roadmap counts in: with
    /// Fridays off, 4 points at 7 a week need 19.4 of its 34 hours: Thursday, Monday and
    /// into Tuesday 29 September — where the default week would end on the Monday.</summary>
    [Fact]
    public async Task The_outlook_projects_through_the_roadmaps_working_week()
    {
        var plan = new StubPlanSource(Item("Item", end: new DateOnly(2026, 10, 2), totalEffort: 4, pace: 7))
        {
            Week = new WorkingHours
            {
                Days = [new WorkingDay(DayOfWeek.Friday, false, new TimeOnly(9, 0), new TimeOnly(17, 30))]
            }
        };

        var result = await Insights(plan).GetPlanAsync(FourWeeks, TestContext.Current.CancellationToken);

        var only = Assert.Single(result.Value!.Items);
        Assert.Equal(new DateOnly(2026, 9, 29), only.ProjectedEnd);
        Assert.Equal(PlanOutlook.OnTrack, only.Outlook);
    }

    /// <summary>Monday 12 October 2026, the day the per-part scenarios start on.</summary>
    private static readonly DateTimeOffset MondayTwelfth = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A hand-placed item, 12–16 October, with 8 open points in <c>app</c> at 8 a week and
    /// 4 in <c>site</c> at 4 a week, the <c>site</c> work waiting on the <c>app</c> work.
    /// Each part is projected at its own pace — <c>app</c> 12–16 October, then <c>site</c>
    /// 19–23 October — and the item is judged on the latest part end. The lowest pace
    /// across both would have put all 12 points at 4 a week, to 30 October.
    /// </summary>
    [Fact]
    public async Task An_item_in_several_repositories_is_projected_part_by_part_and_judged_on_the_latest_part_end()
    {
        var plan = new StubPlanSource(Item(
            "Item",
            start: new DateOnly(2026, 10, 12),
            end: new DateOnly(2026, 10, 16),
            totalEffort: 12,
            parts:
            [
                new PlanPartProgress("app", 8, 8m, []),
                new PlanPartProgress("site", 4, 4m, [0])
            ]));

        var result = await Insights(plan, MondayTwelfth).GetPlanAsync(FourWeeks, TestContext.Current.CancellationToken);

        var only = Assert.Single(result.Value!.Items);
        Assert.Equal(PlanOutlook.Behind, only.Outlook);
        Assert.Equal(new DateOnly(2026, 10, 23), only.ProjectedEnd);
    }

    /// <summary>Parts that wait on nothing run side by side from today: 8 points at 8 a
    /// week and 4 at 4 a week both land on Friday 16 October.</summary>
    [Fact]
    public async Task Parts_that_wait_on_nothing_are_projected_side_by_side_from_today()
    {
        var plan = new StubPlanSource(Item(
            "Item",
            start: new DateOnly(2026, 10, 12),
            end: new DateOnly(2026, 10, 16),
            totalEffort: 12,
            parts:
            [
                new PlanPartProgress("app", 8, 8m, []),
                new PlanPartProgress("site", 4, 4m, [])
            ]));

        var result = await Insights(plan, MondayTwelfth).GetPlanAsync(FourWeeks, TestContext.Current.CancellationToken);

        var only = Assert.Single(result.Value!.Items);
        Assert.Equal(PlanOutlook.OnTrack, only.Outlook);
        Assert.Equal(new DateOnly(2026, 10, 16), only.ProjectedEnd);
    }

    /// <summary>A part with nothing left takes no time, but what waits on it still waits
    /// on what it waited on: <c>docs</c> waits on <c>site</c>, which has nothing left and
    /// waited on <c>app</c>, so <c>docs</c> runs 19–23 October.</summary>
    [Fact]
    public async Task A_part_with_nothing_left_passes_its_own_waits_on_to_what_waits_on_it()
    {
        var plan = new StubPlanSource(Item(
            "Item",
            start: new DateOnly(2026, 10, 12),
            end: new DateOnly(2026, 10, 30),
            totalEffort: 16,
            doneEffort: 4,
            parts:
            [
                new PlanPartProgress("app", 8, 8m, []),
                new PlanPartProgress("site", 0, 4m, [0]),
                new PlanPartProgress("docs", 4, 4m, [1])
            ]));

        var result = await Insights(plan, MondayTwelfth).GetPlanAsync(FourWeeks, TestContext.Current.CancellationToken);

        var only = Assert.Single(result.Value!.Items);
        Assert.Equal(PlanOutlook.OnTrack, only.Outlook);
        Assert.Equal(new DateOnly(2026, 10, 23), only.ProjectedEnd);
    }

    /// <summary>There is no pace to project at only when no part with work left has one;
    /// a part with no pace otherwise counts nothing, as an unestimated entry does.</summary>
    [Theory]
    [InlineData(0, 4, PlanOutlook.OnTrack, "2026-10-16")]
    [InlineData(0, 0, PlanOutlook.NoPace, null)]
    public async Task There_is_no_pace_only_when_no_part_with_work_left_has_one(
        int appPace,
        int sitePace,
        PlanOutlook expected,
        string? projected)
    {
        var plan = new StubPlanSource(Item(
            "Item",
            start: new DateOnly(2026, 10, 12),
            end: new DateOnly(2026, 10, 16),
            totalEffort: 12,
            parts:
            [
                new PlanPartProgress("app", 8, appPace, []),
                new PlanPartProgress("site", 4, sitePace, [])
            ]));

        var result = await Insights(plan, MondayTwelfth).GetPlanAsync(FourWeeks, TestContext.Current.CancellationToken);

        var only = Assert.Single(result.Value!.Items);
        Assert.Equal(expected, only.Outlook);
        Assert.Equal(projected is null ? null : DateOnly.Parse(projected, CultureInfo.InvariantCulture), only.ProjectedEnd);
    }

    [Fact]
    public async Task The_plan_totals_add_up_the_items_in_scope()
    {
        var plan = new StubPlanSource(
            Item("One", totalEffort: 8, doneEffort: 3, unestimated: 1),
            Item("Two", totalEffort: 5, doneEffort: 5, unestimated: 2),
            Item("Out", aliases: ["other"], totalEffort: 13, doneEffort: 13, unestimated: 4))
        {
            Pace = new PlanPace(6m, PlanPaceBasis.LastFourWeeks)
        };

        var result = await Insights(plan).GetPlanAsync(
            FourWeeks with { Repositories = RepositoryFocus.Of("backlog") },
            TestContext.Current.CancellationToken);

        Assert.True(result.Value!.RoadmapEnabled);
        Assert.Equal(new PlanPace(6m, PlanPaceBasis.LastFourWeeks), result.Value.Pace);
        Assert.Equal(13, result.Value.PlannedEffort);
        Assert.Equal(8, result.Value.DoneEffort);
        Assert.Equal(3, result.Value.Unestimated);
    }

    [Fact]
    public async Task A_plan_source_that_fails_reads_as_unavailable_with_its_reason()
    {
        var plan = new StubPlanSource { Throw = new InvalidOperationException("The plan file is locked.") };

        var result = await Insights(plan).GetPlanAsync(FourWeeks, TestContext.Current.CancellationToken);

        Assert.False(result.HasValue);
        Assert.Contains("The plan file is locked.", result.Availability.Reason, StringComparison.Ordinal);
    }

    private static TaskInsights Insights(ICompletedTaskSource source) => new(source, new StubPlanSource(), new FakeTimeProvider(Now));

    private static TaskInsights Insights(IPlanProgressSource plan) => new(new StubSource(), plan, new FakeTimeProvider(Now));

    private static TaskInsights Insights(IPlanProgressSource plan, DateTimeOffset now) => new(new StubSource(), plan, new FakeTimeProvider(now));

    private static PlanItemProgress Item(
        string title,
        DateOnly? start = null,
        DateOnly? end = null,
        string[]? aliases = null,
        int totalEffort = 5,
        int doneEffort = 0,
        int unestimated = 0,
        bool finished = false,
        DateOnly? lastCompletedOn = null,
        decimal pace = 7m,
        bool placedByEffort = false,
        PlanPartProgress[]? parts = null) =>
        new(
            Guid.NewGuid(),
            title,
            start ?? new DateOnly(2026, 9, 1),
            end ?? new DateOnly(2026, 9, 30),
            aliases ?? [],
            GatheredCount: 3,
            DoneCount: finished ? 3 : 0,
            totalEffort,
            doneEffort,
            unestimated,
            finished,
            lastCompletedOn,
            parts ?? [new PlanPartProgress(null, Math.Max(0, totalEffort - doneEffort), pace, [])],
            placedByEffort);

    private static CompletedTask Ticked(int year, int month, int day, int? effort, params string[] aliases) =>
        new(new DateOnly(year, month, day), effort, aliases);

    private sealed class StubSource(params CompletedTask[] tasks) : ICompletedTaskSource
    {
        public DateOnly? Since { get; private set; }

        public Exception? Throw { get; init; }

        public Task<IReadOnlyList<CompletedTask>> GetCompletedAsync(
            DateOnly since,
            CancellationToken cancellationToken = default)
        {
            Since = since;
            if (Throw is not null) throw Throw;
            return Task.FromResult<IReadOnlyList<CompletedTask>>(tasks);
        }
    }

    private sealed class StubPlanSource(params PlanItemProgress[] items) : IPlanProgressSource
    {
        public DateOnly? From { get; private set; }

        public DateOnly? To { get; private set; }

        public IReadOnlyList<string>? Aliases { get; private set; }

        public bool Enabled { get; init; } = true;

        public PlanPace Pace { get; init; } = new(7m, PlanPaceBasis.LastTwoWeeks);

        public WorkingHours Week { get; init; } = WorkingHours.Default;

        public Exception? Throw { get; init; }

        public Task<PlanReading> ReadAsync(
            DateOnly from,
            DateOnly to,
            IReadOnlyList<string> repositoryAliases,
            CancellationToken cancellationToken = default)
        {
            From = from;
            To = to;
            Aliases = repositoryAliases;
            if (Throw is not null) throw Throw;
            return Task.FromResult(Enabled ? new PlanReading(true, Pace, items) { Week = Week } : PlanReading.Off);
        }
    }
}
