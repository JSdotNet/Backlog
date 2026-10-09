using System.Globalization;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The roadmap's plans on the month calendar: each window cut into its week rows and
/// stacked in lanes, the milestones as diamonds, the "Show plans" box, and the shelf of
/// plans without a window, whose drop reports the day the window opens on.
/// <para>
/// October 2026: Monday the 28th of September opens the grid, and the weeks start on
/// the 5th, 12th, 19th and 26th of October.
/// </para>
/// </summary>
public sealed class TaskCalendarPlansTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    public TaskCalendarPlansTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }

    private static DateOnly Oct(int day) => new(2026, 10, day);

    private static IRenderedComponent<TaskCalendar> Render(
        BunitContext context,
        IReadOnlyList<CalendarPlan>? plans = null,
        Action<ComponentParameterCollectionBuilder<TaskCalendar>>? configure = null,
        bool available = true)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context.Render<TaskCalendar>(parameters =>
        {
            parameters
                .Add(c => c.Tasks, [])
                .Add(c => c.Today, Today)
                .Add(c => c.TestId, "cal")
                .Add(c => c.PlansAvailable, available)
                .Add(c => c.Plans, plans ?? []);
            configure?.Invoke(parameters);
        });
    }

    // --- Splitting into weeks and lanes -------------------------------------

    [Fact]
    public void A_window_is_cut_at_every_week_row_and_each_later_piece_says_it_carries_on()
    {
        // Thursday the 8th to Wednesday the 21st: three week rows.
        var plan = new CalendarPlan("p", "Calendar plans", "task-views", Oct(8), Oct(21));

        var first = CalendarPlanLanes.Week(Oct(5), [plan]);
        var middle = CalendarPlanLanes.Week(Oct(12), [plan]);
        var last = CalendarPlanLanes.Week(Oct(19), [plan]);
        var after = CalendarPlanLanes.Week(Oct(26), [plan]);

        var a = Assert.Single(first.Segments);
        Assert.Equal((4, 4, 1, false, true), (a.Column, a.Span, a.Lane, a.ContinuesBefore, a.ContinuesAfter));
        Assert.Equal("Calendar plans", a.Label);

        var b = Assert.Single(middle.Segments);
        Assert.Equal((1, 7, true, true), (b.Column, b.Span, b.ContinuesBefore, b.ContinuesAfter));
        Assert.Equal("… Calendar plans", b.Label);

        var c = Assert.Single(last.Segments);
        Assert.Equal((1, 3, true, false), (c.Column, c.Span, c.ContinuesBefore, c.ContinuesAfter));

        Assert.Empty(after.Segments);
        Assert.Equal(0, after.Lanes);
    }

    [Fact]
    public void Overlapping_plans_take_a_lane_each_and_a_lane_is_reused_once_it_is_free()
    {
        CalendarPlan[] plans =
        [
            new("short", "Short", "s", Oct(12), Oct(13)),
            new("long", "Long", "l", Oct(12), Oct(16)),
            new("later", "Later", "t", Oct(14), Oct(18)),
            new("overlaps-both", "Both", "b", Oct(13), Oct(14))
        ];

        var (segments, lanes) = CalendarPlanLanes.Week(Oct(12), plans);
        int LaneOf(string id) => segments.Single(segment => segment.Plan.Id == id).Lane;

        // The longer of two starting the same day goes on top.
        Assert.Equal(1, LaneOf("long"));
        Assert.Equal(2, LaneOf("short"));
        // Tuesday to Wednesday meets both of those: a third lane.
        Assert.Equal(3, LaneOf("overlaps-both"));
        // Wednesday on: "short" ended on Tuesday, so its lane is free again.
        Assert.Equal(2, LaneOf("later"));
        Assert.Equal(3, lanes);
    }

    [Fact]
    public void A_window_outside_the_week_or_ending_before_it_starts_draws_nothing()
    {
        CalendarPlan[] plans =
        [
            new("before", "Before", "b", Oct(1), Oct(4)),
            new("backwards", "Backwards", "x", Oct(16), Oct(13))
        ];

        Assert.Empty(CalendarPlanLanes.Week(Oct(12), plans).Segments);
    }

    [Fact]
    public void The_bars_are_drawn_in_each_week_row_with_its_lanes_and_a_tooltip_of_title_tag_and_points()
    {
        using var context = new BunitContext();

        var calendar = Render(context,
        [
            new CalendarPlan("a", "Calendar plans", "task-views", Oct(8), Oct(14), DonePoints: 3, TotalPoints: 8),
            new CalendarPlan("b", "Board", "board", Oct(13), Oct(13))
        ]);

        var weeks = calendar.FindAll("[data-testid='cal-week']");
        // The week of the 5th: one lane. The week of the 12th: two, as both overlap the 13th.
        Assert.Equal("--task-calendar-lanes: 1", weeks[1].GetAttribute("style"));
        Assert.Equal("--task-calendar-lanes: 2", weeks[2].GetAttribute("style"));
        Assert.Null(weeks[3].GetAttribute("style"));

        var pieces = calendar.FindAll("[data-testid='cal-plan-a']");
        Assert.Equal(2, pieces.Count);
        Assert.Equal("grid-column: 4 / span 4; grid-row: 1", pieces[0].GetAttribute("style"));
        Assert.Equal("Calendar plans", pieces[0].TextContent.Trim());
        Assert.Equal("grid-column: 1 / span 3; grid-row: 1", pieces[1].GetAttribute("style"));
        Assert.Equal("… Calendar plans", pieces[1].TextContent.Trim());
        Assert.Equal("Calendar plans · +task-views · 3 of 8 pts", pieces[1].GetAttribute("title"));

        Assert.Equal("grid-column: 2 / span 1; grid-row: 2", calendar.Find("[data-testid='cal-plan-b']").GetAttribute("style"));
    }

    [Fact]
    public void A_bar_wears_its_band_colour_when_given_one_and_neutral_grey_otherwise()
    {
        using var context = new BunitContext();

        var calendar = Render(context,
        [
            new CalendarPlan("coloured", "Coloured", "c", Oct(13), Oct(14), Band: 3),
            new CalendarPlan("plain", "Plain", "p", Oct(20), Oct(21))
        ]);

        var coloured = calendar.Find("[data-testid='cal-plan-coloured']").ClassName!;
        Assert.Contains("task-calendar__plan--band-3", coloured);
        Assert.DoesNotContain("task-calendar__plan--neutral", coloured);
        Assert.Contains("task-calendar__plan--neutral", calendar.Find("[data-testid='cal-plan-plain']").ClassName);
    }

    [Fact]
    public void A_milestone_is_a_diamond_chip_on_its_day()
    {
        using var context = new BunitContext();

        var calendar = Render(context, configure: p => p.Add(c => c.Milestones, [new CalendarMilestone("m", "Release 1.0", Oct(16))]));

        var milestone = calendar.Find("[data-calendar-day='2026-10-16'] [data-testid='cal-milestone-m']");
        Assert.NotNull(milestone.QuerySelector(".task-calendar__diamond"));
        Assert.Equal("Release 1.0", milestone.QuerySelector(".task-calendar__milestone-text")!.TextContent);
        Assert.Single(calendar.FindAll("[data-testid='cal-milestone-m']"));
    }

    // --- Show plans ---------------------------------------------------------

    [Fact]
    public void Show_plans_is_ticked_by_default_and_unticking_it_reports_the_choice_and_hides_the_plans()
    {
        using var context = new BunitContext();
        bool? reported = null;

        var calendar = Render(context,
            [new CalendarPlan("a", "A", "a", Oct(13), Oct(14))],
            p => p
                .Add(c => c.Milestones, [new CalendarMilestone("m", "Release", Oct(16))])
                .Add(c => c.ShelfPlans, [new CalendarShelfPlan("shelved", "Shelved")])
                .Add(c => c.ShowPlansChanged, (bool shown) => reported = shown));

        var box = calendar.Find("[data-testid='cal-show-plans'] input");
        Assert.True(box.HasAttribute("checked"));
        Assert.Contains("Show plans", calendar.Find("[data-testid='cal-show-plans']").TextContent);

        box.Change(false);
        Assert.False(reported);

        calendar.Render(p => p.Add(c => c.ShowPlans, false));
        Assert.Empty(calendar.FindAll("[data-testid='cal-plan-a']"));
        Assert.Empty(calendar.FindAll("[data-testid='cal-milestone-m']"));
        Assert.Empty(calendar.FindAll("[data-testid='cal-shelf-shelved']"));
        Assert.Null(calendar.FindAll("[data-testid='cal-week']")[2].GetAttribute("style"));
    }

    [Fact]
    public void Without_plans_to_offer_there_is_no_box_no_bar_and_no_shelf()
    {
        using var context = new BunitContext();

        var calendar = Render(context,
            [new CalendarPlan("a", "A", "a", Oct(13), Oct(14))],
            p => p.Add(c => c.ShelfPlans, [new CalendarShelfPlan("shelved", "Shelved")]),
            available: false);

        Assert.Empty(calendar.FindAll("[data-testid='cal-show-plans']"));
        Assert.Empty(calendar.FindAll("[data-testid='cal-plan-a']"));
        Assert.DoesNotContain("Plans without a window", calendar.Markup, StringComparison.Ordinal);
    }

    // --- The shelf ----------------------------------------------------------

    [Fact]
    public void The_shelf_lists_the_plans_without_a_window_as_drag_sources_under_the_tasks()
    {
        using var context = new BunitContext();

        var calendar = Render(context, configure: p => p.Add(c => c.ShelfPlans,
        [
            new CalendarShelfPlan("release-q4", "Release q4", TaskCount: 3, TotalEffort: 8, UnestimatedCount: 1),
            new CalendarShelfPlan("docs", "Docs", TaskCount: 1, TotalEffort: 2)
        ]));

        var tray = calendar.Find("[data-testid='cal-tray']");
        Assert.True(tray.InnerHtml.IndexOf("Tasks without a due date", StringComparison.Ordinal)
                    < tray.InnerHtml.IndexOf("Plans without a window", StringComparison.Ordinal));
        Assert.Equal("2", calendar.Find("[data-testid='cal-shelf-count']").TextContent);

        var release = calendar.Find("[data-testid='cal-shelf-release-q4']");
        Assert.Equal("release-q4", release.GetAttribute("data-calendar-plan"));
        Assert.Contains("Release q4", release.TextContent);
        Assert.Contains("8 pts + 1 unsized", release.TextContent);
        Assert.Equal("Release q4, +release-q4, 3 tasks, 8 pts + 1 unsized", release.GetAttribute("aria-label"));
        Assert.Contains("2 pts", calendar.Find("[data-testid='cal-shelf-docs']").TextContent);
    }

    [Fact]
    public void An_empty_shelf_says_every_plan_has_a_window()
    {
        using var context = new BunitContext();

        var calendar = Render(context);

        Assert.Equal("0", calendar.Find("[data-testid='cal-shelf-count']").TextContent);
        Assert.Single(calendar.FindAll("[data-testid='cal-shelf-empty']"));
    }

    [Fact]
    public async Task A_shelf_plan_dropped_on_a_day_reports_that_day_as_its_start_and_nothing_else_does()
    {
        using var context = new BunitContext();
        var started = new List<CalendarPlanStart>();

        var calendar = Render(context, configure: p => p
            .Add(c => c.ShelfPlans, [new CalendarShelfPlan("release-q4", "Release q4")])
            .Add(c => c.OnPlanStarted, (CalendarPlanStart start) => started.Add(start)));

        await calendar.InvokeAsync(() => calendar.Instance.DropPlanOnDay("release-q4", "2026-10-19"));
        await calendar.InvokeAsync(() => calendar.Instance.DropPlanOnDay("not-on-the-shelf", "2026-10-19"));
        await calendar.InvokeAsync(() => calendar.Instance.DropPlanOnDay("release-q4", "not a day"));

        Assert.Equal([new CalendarPlanStart("release-q4", Oct(19))], started);

        calendar.Render(p => p.Add(c => c.ShowPlans, false));
        await calendar.InvokeAsync(() => calendar.Instance.DropPlanOnDay("release-q4", "2026-10-20"));
        Assert.Single(started);
    }
}
