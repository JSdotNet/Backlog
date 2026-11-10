using System.Globalization;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Knowledge events on the month calendar: each event a bar across its days, cut at
/// every week row and stacked in lanes above the plans, filled while decided and dashed
/// while still being weighed up, each key date a diamond on its day, and the "Show
/// events" box that draws them. The calendar writes nothing to an event.
/// <para>
/// May 2026: Monday the 27th of April opens the grid, and the weeks start on the 4th,
/// 11th, 18th and 25th of May.
/// </para>
/// </summary>
public sealed class TaskCalendarEventsTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 5, 6);

    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    public TaskCalendarEventsTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }

    private static DateOnly May(int day) => new(2026, 5, day);

    private static CalendarEvent Techorama(DateOnly start, DateOnly end, string id = "e0") =>
        new(id, "Techorama", "Going", "Antwerp", start, end, Tentative: false);

    // --- Splitting into weeks and lanes -------------------------------------

    [Fact]
    public void A_three_day_event_is_one_bar_across_its_days_saying_title_state_and_place()
    {
        var (segments, lanes) = CalendarEventLanes.Week(May(11), [Techorama(May(12), May(14))]);

        var bar = Assert.Single(segments);
        Assert.Equal((2, 3, 1, false, false), (bar.Column, bar.Span, bar.Lane, bar.ContinuesBefore, bar.ContinuesAfter));
        Assert.Equal("Techorama · Going · Antwerp", bar.Label);
        Assert.Equal("Techorama · Going · Antwerp", bar.Event.Label);
        Assert.Equal(1, lanes);
    }

    [Fact]
    public void An_event_across_a_week_break_is_cut_and_the_later_piece_says_it_carries_on()
    {
        // Thursday the 14th to Tuesday the 19th: two week rows.
        var conference = Techorama(May(14), May(19));

        var first = Assert.Single(CalendarEventLanes.Week(May(11), [conference]).Segments);
        Assert.Equal((4, 4, false, true), (first.Column, first.Span, first.ContinuesBefore, first.ContinuesAfter));
        Assert.Equal("Techorama · Going · Antwerp", first.Label);

        var second = Assert.Single(CalendarEventLanes.Week(May(18), [conference]).Segments);
        Assert.Equal((1, 2, true, false), (second.Column, second.Span, second.ContinuesBefore, second.ContinuesAfter));
        Assert.Equal("… Techorama · Going · Antwerp", second.Label);
    }

    [Fact]
    public void Overlapping_events_take_a_lane_each_and_a_lane_is_reused_once_it_is_free()
    {
        CalendarEvent[] events =
        [
            new("short", "Short", "Going", null, May(11), May(12), false),
            new("long", "Long", "Going", null, May(11), May(15), false),
            new("later", "Later", "Considering", null, May(13), May(17), true),
            new("both", "Both", "Going", null, May(12), May(13), false)
        ];

        var (segments, lanes) = CalendarEventLanes.Week(May(11), events);
        int LaneOf(string id) => segments.Single(segment => segment.Event.Id == id).Lane;

        Assert.Equal(1, LaneOf("long"));
        Assert.Equal(2, LaneOf("short"));
        Assert.Equal(3, LaneOf("both"));
        Assert.Equal(2, LaneOf("later"));
        Assert.Equal(3, lanes);
    }

    [Fact]
    public void An_event_outside_the_week_or_ending_before_it_starts_draws_nothing()
    {
        CalendarEvent[] events =
        [
            new("before", "Before", "Going", null, May(4), May(8), false),
            new("backwards", "Backwards", "Going", null, May(15), May(12), false)
        ];

        var (segments, lanes) = CalendarEventLanes.Week(May(11), events);

        Assert.Empty(segments);
        Assert.Equal(0, lanes);
    }

    [Fact]
    public void An_event_with_no_place_leaves_it_out_of_its_label()
    {
        var @event = new CalendarEvent("e", "NDC Oslo", "Considering", null, May(20), May(20), Tentative: true);

        Assert.Equal("NDC Oslo · Considering", @event.Label);
    }

    // --- Drawn on the calendar ----------------------------------------------

    private static IRenderedComponent<TaskCalendar> Render(
        BunitContext context,
        IReadOnlyList<CalendarEvent>? events = null,
        Action<ComponentParameterCollectionBuilder<TaskCalendar>>? configure = null,
        bool available = true,
        bool shown = true)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context.Render<TaskCalendar>(parameters =>
        {
            parameters
                .Add(c => c.Tasks, [])
                .Add(c => c.Today, Today)
                .Add(c => c.TestId, "cal")
                .Add(c => c.EventsAvailable, available)
                .Add(c => c.ShowEvents, shown)
                .Add(c => c.Events, events ?? []);
            configure?.Invoke(parameters);
        });
    }

    [Fact]
    public void A_three_day_conference_is_a_filled_bar_across_its_days_named_by_title_state_and_place()
    {
        using var context = new BunitContext();

        var calendar = Render(context, [Techorama(May(12), May(14))]);

        var bar = calendar.Find("[data-testid='cal-event-e0']");
        Assert.Equal("grid-column: 2 / span 3; grid-row: 1", bar.GetAttribute("style"));
        Assert.Equal("Techorama · Going · Antwerp", bar.TextContent.Trim());
        Assert.Equal("Techorama · Going · Antwerp", bar.GetAttribute("title"));
        Assert.Equal("Techorama · Going · Antwerp", bar.GetAttribute("aria-label"));
        Assert.Contains("task-calendar__event--filled", bar.ClassName);
        Assert.DoesNotContain("task-calendar__event--tentative", bar.ClassName);

        var week = calendar.FindAll("[data-testid='cal-week']")[2];
        Assert.Contains("task-calendar__week--bars", week.ClassName);
        Assert.Equal("--task-calendar-lanes: 1; --task-calendar-event-lanes: 1", week.GetAttribute("style"));
        Assert.NotNull(week.QuerySelector("ul.task-calendar__events[aria-label='Events this week']"));
    }

    [Fact]
    public void An_event_across_a_week_break_is_cut_and_its_later_piece_opens_with_an_ellipsis()
    {
        using var context = new BunitContext();

        var calendar = Render(context, [Techorama(May(14), May(19))]);

        var pieces = calendar.FindAll("[data-testid='cal-event-e0']");
        Assert.Equal(2, pieces.Count);
        Assert.Equal("grid-column: 4 / span 4; grid-row: 1", pieces[0].GetAttribute("style"));
        Assert.Contains("task-calendar__event--continues-after", pieces[0].ClassName);
        Assert.Equal("grid-column: 1 / span 2; grid-row: 1", pieces[1].GetAttribute("style"));
        Assert.Equal("… Techorama · Going · Antwerp", pieces[1].TextContent.Trim());
        Assert.Contains("task-calendar__event--continues-before", pieces[1].ClassName);
        // The tooltip names the event whole, on every piece.
        Assert.Equal("Techorama · Going · Antwerp", pieces[1].GetAttribute("title"));
    }

    [Fact]
    public void An_event_still_being_weighed_up_is_dashed_and_a_decided_one_is_filled()
    {
        using var context = new BunitContext();

        var calendar = Render(context,
        [
            new CalendarEvent("considering", "NDC Oslo", "Considering", "Oslo", May(19), May(21), Tentative: true),
            new CalendarEvent("attended", "Build", "Attended", "Online", May(5), May(5), Tentative: false),
            new CalendarEvent("harvested", "DevDays", "Harvested", null, May(26), May(26), Tentative: false)
        ]);

        var considering = calendar.Find("[data-testid='cal-event-considering']");
        Assert.Contains("task-calendar__event--tentative", considering.ClassName);
        Assert.DoesNotContain("task-calendar__event--filled", considering.ClassName);
        Assert.Equal("NDC Oslo · Considering · Oslo", considering.GetAttribute("aria-label"));

        Assert.Contains("task-calendar__event--filled", calendar.Find("[data-testid='cal-event-attended']").ClassName);
        Assert.Equal("Build · Attended · Online", calendar.Find("[data-testid='cal-event-attended']").GetAttribute("title"));
        Assert.Contains("task-calendar__event--filled", calendar.Find("[data-testid='cal-event-harvested']").ClassName);
        Assert.Equal("DevDays · Harvested", calendar.Find("[data-testid='cal-event-harvested']").GetAttribute("title"));
    }

    [Fact]
    public void Event_lanes_sit_above_the_plan_lanes_and_the_week_makes_room_for_both()
    {
        using var context = new BunitContext();

        var calendar = Render(context,
            [Techorama(May(12), May(14))],
            p => p
                .Add(c => c.PlansAvailable, true)
                .Add(c => c.Plans, [new CalendarPlan("p", "Calendar plans", "task-views", May(12), May(13))]));

        var week = calendar.FindAll("[data-testid='cal-week']")[2];
        Assert.Equal("--task-calendar-lanes: 2; --task-calendar-event-lanes: 1", week.GetAttribute("style"));

        var markup = week.InnerHtml;
        Assert.True(markup.IndexOf("task-calendar__events", StringComparison.Ordinal)
                    < markup.IndexOf("task-calendar__plans", StringComparison.Ordinal));
        // Each layer counts its own lanes; the stylesheet moves the plans below the events.
        Assert.Equal("grid-column: 2 / span 2; grid-row: 1", calendar.Find("[data-testid='cal-plan-p']").GetAttribute("style"));

        // A week with plans and no events keeps the plans' own style.
        calendar.Render(p => p.Add(c => c.ShowEvents, false));
        Assert.Equal("--task-calendar-lanes: 1", calendar.FindAll("[data-testid='cal-week']")[2].GetAttribute("style"));
        Assert.NotEmpty(calendar.FindAll("[data-testid='cal-plan-p']"));
    }

    // --- Key dates ----------------------------------------------------------

    [Fact]
    public void Early_bird_is_a_diamond_on_its_day_naming_the_event_and_hidden_while_the_box_is_unticked()
    {
        using var context = new BunitContext();
        var earlyBird = new CalendarKeyDate("e0-k0", "Early bird ends", "Techorama", new DateOnly(2026, 3, 3));

        var calendar = Render(context, configure: p => p
            .Add(c => c.Month, new DateOnly(2026, 3, 1))
            .Add(c => c.KeyDates, [earlyBird]));

        var chip = calendar.Find("[data-calendar-day='2026-03-03'] [data-testid='cal-key-date-e0-k0']");
        Assert.NotNull(chip.QuerySelector(".task-calendar__diamond"));
        Assert.Contains("task-calendar__milestone--key-date", chip.ClassName);
        Assert.Equal("Early bird ends", chip.QuerySelector(".task-calendar__milestone-text")!.TextContent);
        Assert.Equal("Early bird ends, Techorama, key date, Tuesday, March 3, 2026", chip.GetAttribute("title"));
        Assert.Single(calendar.FindAll("[data-testid='cal-key-date-e0-k0']"));

        calendar.Render(p => p.Add(c => c.ShowEvents, false));
        Assert.Empty(calendar.FindAll("[data-testid='cal-key-date-e0-k0']"));
    }

    [Fact]
    public void A_key_date_comes_before_a_milestone_on_the_same_day()
    {
        using var context = new BunitContext();

        var calendar = Render(context, configure: p => p
            .Add(c => c.PlansAvailable, true)
            .Add(c => c.Milestones, [new CalendarMilestone("m", "Release", May(20))])
            .Add(c => c.KeyDates, [new CalendarKeyDate("k", "Call for papers closes", "NDC Oslo", May(20))]));

        var day = calendar.Find("[data-calendar-day='2026-05-20']").InnerHtml;
        Assert.True(day.IndexOf("cal-key-date-k", StringComparison.Ordinal) < day.IndexOf("cal-milestone-m", StringComparison.Ordinal));
    }

    // --- Show events --------------------------------------------------------

    [Fact]
    public void Show_events_is_unticked_by_default_sits_after_show_plans_and_ticking_it_reports_the_choice()
    {
        using var context = new BunitContext();
        bool? reported = null;
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var calendar = context.Render<TaskCalendar>(parameters => parameters
            .Add(c => c.Tasks, [])
            .Add(c => c.Today, Today)
            .Add(c => c.TestId, "cal")
            .Add(c => c.PlansAvailable, true)
            .Add(c => c.EventsAvailable, true)
            .Add(c => c.Events, [Techorama(May(12), May(14))])
            .Add(c => c.ShowEventsChanged, (bool shown) => reported = shown));

        var box = calendar.Find("[data-testid='cal-show-events'] input");
        Assert.False(box.HasAttribute("checked"));
        Assert.Contains("Show events", calendar.Find("[data-testid='cal-show-events']").TextContent);
        Assert.Empty(calendar.FindAll("[data-testid='cal-event-e0']"));

        var bar = calendar.Find("[data-testid='cal-bar']").InnerHtml;
        Assert.True(bar.IndexOf("cal-show-plans", StringComparison.Ordinal) < bar.IndexOf("cal-show-events", StringComparison.Ordinal));

        box.Change(true);
        Assert.True(reported);

        calendar.Render(p => p.Add(c => c.ShowEvents, true));
        Assert.NotEmpty(calendar.FindAll("[data-testid='cal-event-e0']"));
    }

    [Fact]
    public void Show_plans_and_show_events_draw_their_own_layer_only()
    {
        using var context = new BunitContext();

        var calendar = Render(context,
            [Techorama(May(12), May(14))],
            p => p
                .Add(c => c.PlansAvailable, true)
                .Add(c => c.ShowPlans, false)
                .Add(c => c.Plans, [new CalendarPlan("p", "Plan", "plan", May(12), May(13))]));

        Assert.NotEmpty(calendar.FindAll("[data-testid='cal-event-e0']"));
        Assert.Empty(calendar.FindAll("[data-testid='cal-plan-p']"));
        Assert.Equal("--task-calendar-lanes: 1; --task-calendar-event-lanes: 1", calendar.FindAll("[data-testid='cal-week']")[2].GetAttribute("style"));
    }

    [Fact]
    public void Without_events_to_offer_there_is_no_box_no_bar_and_no_key_date_whatever_the_choice()
    {
        using var context = new BunitContext();

        var calendar = Render(context,
            [Techorama(May(12), May(14))],
            p => p.Add(c => c.KeyDates, [new CalendarKeyDate("k", "Early bird ends", "Techorama", May(8))]),
            available: false,
            shown: true);

        Assert.Empty(calendar.FindAll("[data-testid='cal-show-events']"));
        Assert.Empty(calendar.FindAll("[data-testid='cal-event-e0']"));
        Assert.Empty(calendar.FindAll("[data-testid='cal-key-date-k']"));
        Assert.Empty(calendar.FindAll(".task-calendar__events"));
        Assert.Null(calendar.FindAll("[data-testid='cal-week']")[2].GetAttribute("style"));
    }

    // --- The calendar changes no event --------------------------------------

    [Fact]
    public async Task An_event_bar_is_no_drag_source_and_no_drop_reports_a_change_for_it()
    {
        using var context = new BunitContext();
        var moved = new List<CalendarDueChange>();
        var started = new List<CalendarPlanStart>();

        var calendar = Render(context,
            [Techorama(May(12), May(14))],
            p => p
                .Add(c => c.PlansAvailable, true)
                .Add(c => c.OnDueChanged, (CalendarDueChange change) => moved.Add(change))
                .Add(c => c.OnPlanStarted, (CalendarPlanStart start) => started.Add(start)));

        var bar = calendar.Find("[data-testid='cal-event-e0']");
        Assert.Equal("e0", bar.GetAttribute("data-calendar-event"));
        Assert.False(bar.HasAttribute("data-calendar-task"));
        Assert.False(bar.HasAttribute("data-calendar-plan"));
        Assert.False(bar.HasAttribute("data-calendar-plan-window"));

        await calendar.InvokeAsync(() => calendar.Instance.DropOnDay("e0", "2026-05-20"));
        await calendar.InvokeAsync(() => calendar.Instance.DropPlanOnDay("e0", "2026-05-20"));
        await calendar.InvokeAsync(() => calendar.Instance.HoldOnDay("e0", "2026-05-20"));

        Assert.Empty(moved);
        Assert.Empty(started);
    }

    // --- Week -----------------------------------------------------------------

    [Fact]
    public void The_week_range_draws_the_same_bars_and_diamonds()
    {
        using var context = new BunitContext();

        // Today, the 6th, is in the month shown, so Week opens on the week of the 4th.
        var calendar = Render(context,
            [Techorama(May(5), May(7))],
            p => p
                .Add(c => c.Range, CalendarRange.Week)
                .Add(c => c.KeyDates, [new CalendarKeyDate("k", "Early bird ends", "Techorama", May(8))]));

        Assert.Equal(7, calendar.FindAll("[data-calendar-day]").Count);
        Assert.Equal("grid-column: 2 / span 3; grid-row: 1", calendar.Find("[data-testid='cal-event-e0']").GetAttribute("style"));
        Assert.NotNull(calendar.Find("[data-calendar-day='2026-05-08'] [data-testid='cal-key-date-k']"));
    }
}
