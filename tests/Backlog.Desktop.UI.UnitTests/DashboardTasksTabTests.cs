using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI.Parts;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Tasks tab against board Tasks (S6, entries 16, 19 and 20): the six figures in one
/// row, no pace said as no pace, the timeline running past today with today marked, an
/// outlook a keyboard or touch can reach, and the axis an insight without a window falls
/// back to.
/// </summary>
public sealed class DashboardTasksTabTests
{
    private static readonly DateOnly Monday14September = new(2026, 9, 14);
    private static readonly DateOnly Today = new(2026, 10, 12);

    /// <summary>
    /// Two verify findings bUnit can guard in the stylesheet. The "Today" word sat on the
    /// axis row and printed over the week label under it ("W42ay"), so the axis keeps a
    /// line above its labels for the word. The outlook bubble opened behind the next
    /// row's sticky name column, so a row holding the focus or the pointer is lifted
    /// above the rows after it.
    /// </summary>
    [Fact]
    public void The_today_word_has_its_own_line_and_an_open_outlook_rises_above_the_names()
    {
        var css = File.ReadAllText(RepositoryRoot.File("src", "Modules", "Dashboard", "Backlog.Modules.Dashboard.UI", "wwwroot", "dashboard.css")).Replace("\r\n", "\n");

        Assert.Contains(
            "padding-top: calc(var(--font-size-xs) * var(--line-height-normal));",
            Block(css, ".dashboard-timeline__axis {"),
            StringComparison.Ordinal);

        var raised = Block(css, ".dashboard-timeline__item:focus-within,");
        Assert.Contains(".dashboard-timeline__item:hover", raised, StringComparison.Ordinal);
        Assert.Contains("z-index: 2;", raised, StringComparison.Ordinal);
    }

    private static string Block(string css, string opening)
    {
        var start = css.IndexOf(opening, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{opening} should exist.");
        return css[start..(css.IndexOf('}', start) + 1)];
    }

    [Fact]
    public void The_six_figures_read_as_the_board_writes_them()
    {
        using var context = Context(Plan(pace: 2m));

        var part = context.Render<TaskFiguresPart>(parameters => parameters.Add(p => p.Scope, new DashboardScope(Period: DashboardPeriod.FourWeeks)));

        part.WaitForAssertion(() =>
        {
            Assert.Contains("2 a week", Text(part, "completed"), StringComparison.Ordinal);
            Assert.Contains("of 34 planned", Text(part, "points"), StringComparison.Ordinal);
            Assert.Contains("2", Text(part, "pace"), StringComparison.Ordinal);
            Assert.Contains("3 done · 1 behind", Text(part, "items"), StringComparison.Ordinal);
            Assert.Contains("34", Text(part, "planned"), StringComparison.Ordinal);
            Assert.Contains("~4 weeks at the pace in use", Text(part, "left"), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void With_no_pace_the_row_says_so_rather_than_zero_points_a_week()
    {
        using var context = Context(Plan(pace: 0m));

        var part = context.Render<TaskFiguresPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        part.WaitForAssertion(() =>
        {
            Assert.Contains("No pace", Text(part, "pace"), StringComparison.Ordinal);
            Assert.DoesNotContain("0 pts", Text(part, "pace"), StringComparison.Ordinal);
            Assert.Contains("No pace to say how long", Text(part, "left"), StringComparison.Ordinal);
        });
    }

    /// <summary>With both reads failing the row says both reasons: they can differ, and
    /// either alone would hide the other (review, Batch C).</summary>
    [Fact]
    public void With_both_reads_failing_the_row_gives_both_reasons()
    {
        using var context = new BunitContext();
        _ = context.Services.AddUnavailableDashboard("backlog");
        context.Services.AddSingleton<ITaskInsights>(new Failing());

        var part = context.Render<TaskFiguresPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        part.WaitForAssertion(() =>
        {
            Assert.Contains("The backlog is locked.", part.Markup, StringComparison.Ordinal);
            Assert.Contains("The plan file is unreadable.", part.Markup, StringComparison.Ordinal);
        });
    }

    /// <summary>The timeline runs past today to the latest item end, with today marked
    /// (AC17): W38 to W47, today in its fifth week, half a day into it.</summary>
    [Fact]
    public void The_timeline_runs_past_today_to_the_latest_end_and_marks_today()
    {
        using var context = Context(Plan(pace: 2m, timelineTo: new DateOnly(2026, 11, 20)));

        var part = context.Render<RoadmapPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        part.WaitForAssertion(() =>
        {
            var weeks = part.FindAll("[data-testid='dashboard-roadmap-week']");
            Assert.Equal("W38", weeks[0].TextContent.Trim());
            Assert.Equal("W47", weeks[^1].TextContent.Trim());
            Assert.Contains("dashboard-timeline__week--today", weeks[4].ClassList);
        });

        var today = part.Find("[data-testid='dashboard-roadmap-today']");
        Assert.Equal("Today", today.TextContent.Trim());
        Assert.Equal("--dashboard-timeline-today: 0.4071", today.GetAttribute("style"));
    }

    /// <summary>One item planned to end years out does not stretch the axis past half a
    /// year beyond today: it ends on the Monday 26 weeks on, W15 of 2027.</summary>
    [Fact]
    public void The_axis_stops_half_a_year_past_today()
    {
        using var context = Context(Plan(pace: 2m, timelineTo: new DateOnly(2028, 1, 1)));

        var part = context.Render<RoadmapPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        part.WaitForAssertion(() =>
        {
            var weeks = part.FindAll("[data-testid='dashboard-roadmap-week']");
            Assert.Equal(31, weeks.Count);
            Assert.Equal("W15", weeks[^1].TextContent.Trim());
        });
    }

    /// <summary>The outlook sentence is behind a real button, so a keyboard focuses it and
    /// a touch pins it open — not only a pointer hovering the bar (AC18).</summary>
    [Fact]
    public void Each_items_outlook_sentence_is_reachable_by_keyboard()
    {
        using var context = Context(Plan(pace: 2m));

        var part = context.Render<RoadmapPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        part.WaitForAssertion(() => Assert.NotEmpty(part.FindAll("[data-testid='dashboard-roadmap-outlook']")));
        var trigger = part.FindAll("[data-testid='dashboard-roadmap-outlook']")[0];
        Assert.Equal("BUTTON", trigger.TagName);
        Assert.Equal("How Late stands", trigger.GetAttribute("aria-label"));

        var sentence = part.Find("#" + trigger.GetAttribute("aria-describedby"));
        Assert.Contains("planned to end", sentence.TextContent, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An insight built without a window draws the items' own stretch, from the
    /// earliest start to the latest end, and has no today to mark (entry 20).</summary>
    [Fact]
    public void Without_a_window_the_axis_is_the_items_own_stretch()
    {
        using var context = Context(Plan(pace: 2m) with { WindowFrom = default, WindowTo = default, TimelineTo = default });

        var part = context.Render<RoadmapPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        part.WaitForAssertion(() => Assert.Equal(
            ["W36", "W37", "W38", "W39", "W40"],
            part.FindAll("[data-testid='dashboard-roadmap-week']").Select(week => week.TextContent.Trim())));
        Assert.Empty(part.FindAll("[data-testid='dashboard-roadmap-today']"));
    }

    private static string Text(IRenderedComponent<TaskFiguresPart> part, string figure) =>
        part.Find($"[data-testid='dashboard-tasks-figure-{figure}']").TextContent;

    private static PlanInsight Plan(decimal pace, DateOnly? timelineTo = null) =>
        new(
            true,
            new PlanPace(pace, PlanPaceBasis.LastFourWeeks),
            [
                Row("Late", PlanOutlook.Behind, total: 10, done: 2),
                Row("Shipped", PlanOutlook.Finished, total: 8, done: 8),
                Row("Shipped too", PlanOutlook.Finished, total: 8, done: 8),
                Row("Shipped three", PlanOutlook.Finished, total: 8, done: 8)
            ])
        {
            WindowFrom = Monday14September,
            WindowTo = Today,
            TimelineTo = timelineTo ?? Today
        };

    private static PlanItemInsight Row(string title, PlanOutlook outlook, int total, int done) =>
        new(
            new PlanItemProgress(
                Guid.NewGuid(),
                title,
                new DateOnly(2026, 9, 1),
                new DateOnly(2026, 9, 30),
                [],
                GatheredCount: 3,
                DoneCount: done > 0 ? 1 : 0,
                TotalEffort: total,
                DoneEffort: done,
                Unestimated: 0,
                IsFinished: outlook == PlanOutlook.Finished,
                LastCompletedOn: null,
                Parts: [],
                PlacedByEffort: false),
            outlook,
            null);

    private static BunitContext Context(PlanInsight plan)
    {
        var context = new BunitContext();
        _ = context.Services.AddUnavailableDashboard("backlog");
        context.Services.AddSingleton<ITaskInsights>(new Tasks(plan));
        return context;
    }

    private sealed class Failing : ITaskInsights
    {
        public Task<InsightResult<TaskThroughputInsight>> GetThroughputAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<TaskThroughputInsight>.Unavailable("The backlog is locked."));

        public Task<InsightResult<PlanInsight>> GetPlanAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<PlanInsight>.Unavailable("The plan file is unreadable."));
    }

    private sealed class Tasks(PlanInsight plan) : ITaskInsights
    {
        public Task<InsightResult<TaskThroughputInsight>> GetThroughputAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<TaskThroughputInsight>.Ready(new TaskThroughputInsight(
                [new InsightPoint("W41", 3m), new InsightPoint("W42", 5m)],
                [new InsightPoint("W41", 9m), new InsightPoint("W42", 12m)],
                0)));

        public Task<InsightResult<PlanInsight>> GetPlanAsync(DashboardScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<PlanInsight>.Ready(plan));
    }
}
