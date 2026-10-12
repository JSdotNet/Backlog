using System.Globalization;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The month calendar: which days it lays out, how a task is drawn on its day,
/// what folds behind "+N more", what waits in the tray, and what a press and a
/// drop report back.
/// <para>
/// The culture is pinned to en-US, whose own week starts on a Sunday, so the
/// Monday-first grid is shown to be the calendar's choice and not the culture's.
/// </para>
/// </summary>
public sealed class TaskCalendarTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    public TaskCalendarTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }

    private static IRenderedComponent<TaskCalendar> Render(
        BunitContext context,
        IReadOnlyList<CalendarTask> tasks,
        Action<ComponentParameterCollectionBuilder<TaskCalendar>>? configure = null)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context.Render<TaskCalendar>(parameters =>
        {
            parameters.Add(c => c.Tasks, tasks).Add(c => c.Today, Today).Add(c => c.TestId, "cal");
            configure?.Invoke(parameters);
        });
    }

    private static string[] Days(IRenderedComponent<TaskCalendar> calendar) =>
        [.. calendar.FindAll("[data-calendar-day]").Select(day => day.GetAttribute("data-calendar-day")!)];

    [Theory]
    // October 2026 opens on a Thursday and closes on a Saturday: five weeks, from
    // the Monday in September to the Sunday in November.
    [InlineData(2026, 10, "2026-09-28", "2026-11-01", 5)]
    // February 2027 opens on a Monday and closes on a Sunday: four weeks, nothing
    // borrowed from either side.
    [InlineData(2027, 2, "2027-02-01", "2027-02-28", 4)]
    // August 2026 opens on a Saturday and closes on a Monday: six weeks.
    [InlineData(2026, 8, "2026-07-27", "2026-09-06", 6)]
    public void The_grid_is_whole_monday_first_weeks_spanning_the_months_edges(
        int year, int month, string first, string last, int weeks)
    {
        using var context = new BunitContext();

        var calendar = Render(context, [], p => p.Add(c => c.Month, new DateOnly(year, month, 15)));

        var days = Days(calendar);
        Assert.Equal(weeks, calendar.FindAll("[data-testid='cal-week']").Count);
        Assert.Equal(weeks * 7, days.Length);
        Assert.Equal(first, days[0]);
        Assert.Equal(last, days[^1]);
        Assert.Equal(DayOfWeek.Monday, DateOnly.ParseExact(days[0], "yyyy-MM-dd", CultureInfo.InvariantCulture).DayOfWeek);
        Assert.Equal(
            ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"],
            calendar.FindAll(".task-calendar__weekday").Select(day => day.TextContent));

        Assert.Equal(weeks, CalendarMonth.WeekStarts(new DateOnly(year, month, 1)).Count);
    }

    [Fact]
    public void Days_outside_the_month_are_dimmed_weekends_shaded_and_today_ringed_with_the_my_day_count()
    {
        using var context = new BunitContext();

        var calendar = Render(context, [], p => p.Add(c => c.MyDayCount, 3));

        string Class(string day) => calendar.Find($"[data-calendar-day='{day}']").ClassName!;

        Assert.Contains("task-calendar__day--outside", Class("2026-09-28"));
        Assert.Contains("task-calendar__day--outside", Class("2026-11-01"));
        Assert.DoesNotContain("task-calendar__day--outside", Class("2026-10-01"));

        Assert.Contains("task-calendar__day--weekend", Class("2026-10-03"));
        Assert.Contains("task-calendar__day--weekend", Class("2026-10-04"));
        Assert.DoesNotContain("task-calendar__day--weekend", Class("2026-10-05"));

        var today = calendar.Find("[data-calendar-day='2026-10-06']");
        Assert.Contains("task-calendar__day--today", today.ClassName);
        Assert.Equal("date", today.GetAttribute("aria-current"));
        Assert.Contains("My Day · 3", calendar.Find("[data-testid='cal-my-day']").TextContent);
        Assert.Single(calendar.FindAll("[data-testid='cal-my-day']"));

        // The 1st says its month, so a row that crosses into the next one says where.
        Assert.Equal("Oct 1", calendar.Find("[data-testid='cal-day-2026-10-01-number']").TextContent);
        Assert.Equal("Nov 1", calendar.Find("[data-testid='cal-day-2026-11-01-number']").TextContent);
        Assert.Equal("2", calendar.Find("[data-testid='cal-day-2026-10-02-number']").TextContent);
    }

    [Fact]
    public void The_month_bar_steps_back_and_forward_and_returns_to_today()
    {
        using var context = new BunitContext();

        var calendar = Render(context, []);
        string Month() => calendar.Find("[data-testid='cal-month']").TextContent.Trim();

        Assert.Equal("October 2026", Month());

        calendar.Find("[data-testid='cal-next']").Click();
        Assert.Equal("November 2026", Month());
        Assert.Equal("2026-10-26", Days(calendar)[0]);

        calendar.Find("[data-testid='cal-previous']").Click();
        calendar.Find("[data-testid='cal-previous']").Click();
        Assert.Equal("September 2026", Month());

        calendar.Find("[data-testid='cal-today']").Click();
        Assert.Equal("October 2026", Month());
    }

    [Fact]
    public void An_open_task_past_its_due_date_is_overdue_and_a_done_one_is_struck_through_instead()
    {
        using var context = new BunitContext();

        var calendar = Render(context,
        [
            new("late", "Rotate the sync signing key", new DateOnly(2026, 10, 1), CalendarTaskState.Ready),
            new("finished", "Import plan dedupes by id", new DateOnly(2026, 10, 2), CalendarTaskState.Done),
            new("shelved", "An archived idea", new DateOnly(2026, 10, 2), CalendarTaskState.Archived),
            new("due", "Due today", Today, CalendarTaskState.InProgress),
            new("ahead", "Due later", new DateOnly(2026, 10, 9), CalendarTaskState.Draft)
        ]);

        var late = calendar.Find("[data-testid='cal-chip-late']");
        Assert.Contains("task-calendar__chip--overdue", late.ClassName);
        Assert.NotNull(late.QuerySelector(".task-calendar__dot--overdue"));
        Assert.Contains("overdue", late.GetAttribute("aria-label"));

        var finished = calendar.Find("[data-testid='cal-chip-finished']");
        Assert.Contains("task-calendar__chip--done", finished.ClassName);
        Assert.DoesNotContain("task-calendar__chip--overdue", finished.ClassName);
        Assert.NotNull(finished.QuerySelector(".task-calendar__dot--done"));

        // Archived is not open, so it is never overdue either.
        Assert.DoesNotContain("task-calendar__chip--overdue", calendar.Find("[data-testid='cal-chip-shelved']").ClassName);

        // Due today is not late yet.
        Assert.DoesNotContain("task-calendar__chip--overdue", calendar.Find("[data-testid='cal-chip-due']").ClassName);
        Assert.NotNull(calendar.Find("[data-testid='cal-chip-due']").QuerySelector(".task-calendar__dot--in-progress"));
        Assert.NotNull(calendar.Find("[data-testid='cal-chip-ahead']").QuerySelector(".task-calendar__dot--draft"));
    }

    [Fact]
    public void A_day_shows_two_chips_and_folds_the_rest_behind_more_which_opens_the_whole_day()
    {
        using var context = new BunitContext();
        var day = new DateOnly(2026, 10, 16);

        var calendar = Render(context,
        [
            new("a", "Board view: status columns", day, CalendarTaskState.Ready),
            new("b", "Drag follows status flow", day, CalendarTaskState.Ready),
            new("c", "Release notes for task views", day, CalendarTaskState.Draft)
        ]);

        var cell = calendar.Find("[data-calendar-day='2026-10-16']");
        Assert.Equal(2, cell.QuerySelectorAll(".task-calendar__chip").Length);
        Assert.Contains("+1 more", calendar.Find("[data-testid='cal-day-2026-10-16-more']").TextContent);
        Assert.Empty(calendar.FindAll("[data-testid='cal-chip-c']"));

        calendar.Find("[data-testid='cal-day-2026-10-16-more']").Click();

        Assert.Equal(3, calendar.Find("[data-calendar-day='2026-10-16']").QuerySelectorAll(".task-calendar__chip").Length);
        Assert.NotEmpty(calendar.FindAll("[data-testid='cal-chip-c']"));
        Assert.Empty(calendar.FindAll("[data-testid='cal-day-2026-10-16-more']"));

        calendar.Find("[data-testid='cal-day-2026-10-16-fewer']").Click();
        Assert.Empty(calendar.FindAll("[data-testid='cal-chip-c']"));
    }

    [Fact]
    public void The_tray_lists_the_open_tasks_without_a_due_date_and_nothing_else()
    {
        using var context = new BunitContext();

        var calendar = Render(context,
        [
            new("dated", "Has a date", Today, CalendarTaskState.Ready),
            new("undated", "Persist the last view", null, CalendarTaskState.Ready, Effort: "1 pt"),
            new("draft", "Explore swimlanes", null, CalendarTaskState.Draft),
            new("done", "Finished without a date", null, CalendarTaskState.Done),
            new("archived", "Archived without a date", null, CalendarTaskState.Archived)
        ]);

        var tray = calendar.Find("[data-testid='cal-tray']");
        Assert.Contains("Tasks without a due date", tray.TextContent);
        Assert.Equal("2", calendar.Find("[data-testid='cal-tray-count']").TextContent);
        Assert.Equal(
            ["undated", "draft"],
            tray.QuerySelectorAll("[data-calendar-task]").Select(item => item.GetAttribute("data-calendar-task")));
        Assert.Contains("1 pt", calendar.Find("[data-testid='cal-tray-undated']").TextContent);
    }

    [Fact]
    public void A_recurring_task_shows_on_its_due_date_only()
    {
        using var context = new BunitContext();

        var calendar = Render(context, [new("retro", "Weekly retro", Today, CalendarTaskState.Ready, Repeats: true)]);

        Assert.Single(calendar.FindAll("[data-calendar-task='retro']"));
        Assert.NotNull(calendar.Find("[data-calendar-day='2026-10-06']").QuerySelector("[data-calendar-task='retro']"));
    }

    [Fact]
    public void Pressing_a_chip_or_a_tray_item_opens_that_task()
    {
        using var context = new BunitContext();
        var opened = new List<string>();

        var calendar = Render(context,
            [
                new("chip", "On a day", Today, CalendarTaskState.Ready),
                new("tray", "In the tray", null, CalendarTaskState.Ready)
            ],
            p => p.Add(c => c.OnOpen, (string id) => opened.Add(id)).Add(c => c.SelectedId, "chip"));

        Assert.Equal("true", calendar.Find("[data-testid='cal-chip-chip']").GetAttribute("aria-current"));

        calendar.Find("[data-testid='cal-chip-chip']").Click();
        calendar.Find("[data-testid='cal-tray-tray']").Click();

        Assert.Equal(["chip", "tray"], opened);
    }

    [Fact]
    public async Task A_drop_on_another_day_reports_the_new_due_date_and_a_drop_in_place_reports_nothing()
    {
        using var context = new BunitContext();
        var changes = new List<CalendarDueChange>();

        var calendar = Render(context,
            [
                new("chip", "On a day", Today, CalendarTaskState.Ready),
                new("tray", "In the tray", null, CalendarTaskState.Ready)
            ],
            p => p.Add(c => c.OnDueChanged, (CalendarDueChange change) => changes.Add(change)));

        await calendar.InvokeAsync(() => calendar.Instance.DropOnDay("tray", "2026-10-20"));
        await calendar.InvokeAsync(() => calendar.Instance.DropOnDay("chip", "2026-10-08"));
        await calendar.InvokeAsync(() => calendar.Instance.DropOnDay("chip", "2026-10-06"));
        await calendar.InvokeAsync(() => calendar.Instance.DropOnDay("nobody", "2026-10-08"));
        await calendar.InvokeAsync(() => calendar.Instance.DropOnDay("chip", "not a day"));

        Assert.Equal(
            [new CalendarDueChange("tray", new DateOnly(2026, 10, 20)), new CalendarDueChange("chip", new DateOnly(2026, 10, 8))],
            changes);
    }

    [Fact]
    public void Every_chip_and_tray_item_is_a_drag_source_and_every_day_a_drop_target_for_the_script()
    {
        using var context = new BunitContext();

        var calendar = Render(context,
        [
            new("chip", "On a day", Today, CalendarTaskState.Ready),
            new("tray", "In the tray", null, CalendarTaskState.Ready)
        ]);

        var owner = calendar.Find("section.task-calendar").GetAttribute("data-calendar-owner");
        Assert.False(string.IsNullOrEmpty(owner));
        Assert.Equal(2, calendar.FindAll("[data-calendar-task]").Count);
        Assert.Equal(35, calendar.FindAll("[data-calendar-day]").Count);

        var attach = Assert.Single(context.JSInterop.Invocations, call => call.Identifier == "backlogCalendarDrag.attach");
        Assert.Equal(owner, attach.Arguments[1]);
    }

    [Fact]
    public async Task Focusing_the_selected_task_aims_at_its_chip_and_opens_a_folded_day_first()
    {
        using var context = new BunitContext();
        var day = new DateOnly(2026, 10, 16);

        var calendar = Render(context,
            [
                new("a", "First", day, CalendarTaskState.Ready),
                new("b", "Second", day, CalendarTaskState.Ready),
                new("c", "Third, folded", day, CalendarTaskState.Ready)
            ],
            p => p.Add(c => c.SelectedId, "c"));

        Assert.Empty(calendar.FindAll("[data-testid='cal-chip-c']"));

        await calendar.InvokeAsync(calendar.Instance.FocusSelectedAsync);

        var chip = calendar.Find("[data-testid='cal-chip-c']");
        var focus = Assert.Single(context.JSInterop.Invocations, call => call.Identifier == "backlogFocus");
        Assert.Equal(chip.Id, focus.Arguments[0]);
    }

    [Fact]
    public void The_repository_mark_is_the_hosts_and_is_drawn_only_when_given()
    {
        using var context = new BunitContext();

        var calendar = Render(context,
        [
            new("marked", "Filed against backlog", Today, CalendarTaskState.Ready, CssClass: "repo-mark repo-mark--2"),
            new("plain", "No colour handed in", Today, CalendarTaskState.Ready),
            new("tray", "Undated and marked", null, CalendarTaskState.Ready, CssClass: "repo-mark repo-mark--4")
        ]);

        Assert.Contains("repo-mark--2", calendar.Find("[data-testid='cal-chip-marked']").ClassName);
        Assert.DoesNotContain("repo-mark", calendar.Find("[data-testid='cal-chip-plain']").ClassName);
        Assert.Contains("repo-mark--4", calendar.Find("[data-testid='cal-tray-tray']").ClassName);
    }

    /// <summary>
    /// A calendar drag reaches a day scrolled out of view (#1042): once the drag
    /// is under way it hands itself to the task list's bounded edge-scroll loop,
    /// rather than running a second one, and lets go of it however the gesture
    /// ends. bUnit has no layout or pointer, so this holds the script to the
    /// wiring; the storybook's calendar page is the same check made with a pointer.
    /// </summary>
    [Fact]
    public void A_calendar_drag_borrows_the_task_lists_edge_scroll_loop()
    {
        var script = File.ReadAllText(RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.js"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        // One loop, offered to the calendar's half of the file.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(script, @"window\.backlogDragAutoscroll\s*="));
        Assert.Contains("follow: dragScrollFollow", script, StringComparison.Ordinal);
        Assert.Contains("release: dragScrollRelease", script, StringComparison.Ordinal);

        var calendar = script[script.IndexOf("let calendarDrag = null;", StringComparison.Ordinal)..];

        // Followed once the press becomes a drag, released when it ends.
        Assert.Contains("window.backlogDragAutoscroll?.follow(calendarDrag)", calendar, StringComparison.Ordinal);
        var end = calendar[calendar.IndexOf("function endCalendarDrag()", StringComparison.Ordinal)..];
        Assert.Contains("window.backlogDragAutoscroll?.release(calendarDrag)", end[..end.IndexOf("\n    }", StringComparison.Ordinal)], StringComparison.Ordinal);

        // And no frame loop of its own.
        Assert.DoesNotContain("requestAnimationFrame", calendar, StringComparison.Ordinal);
    }

    // --- The Week range --------------------------------------------------------
    // Today is Tuesday the 6th of October 2026, so today's week runs Monday the 5th to
    // Sunday the 11th.

    private static string Heading(IRenderedComponent<TaskCalendar> calendar) =>
        calendar.Find("[data-testid='cal-month']").TextContent.Trim();

    private static string Pressed(IRenderedComponent<TaskCalendar> calendar, string part) =>
        calendar.Find($"[data-testid='cal-range-{part}']").GetAttribute("aria-pressed")!;

    [Fact]
    public void The_range_control_is_a_fused_pair_with_month_pressed_until_week_is_picked()
    {
        using var context = new BunitContext();

        var calendar = Render(context, []);

        var group = calendar.Find("[data-testid='cal-range']");
        Assert.Contains("btn-group", group.ClassName);
        Assert.Equal("group", group.GetAttribute("role"));
        Assert.Equal("Range", group.GetAttribute("aria-label"));
        Assert.Equal(["Month", "Week"], group.QuerySelectorAll("button").Select(button => button.TextContent.Trim()));
        Assert.Equal("true", Pressed(calendar, "month"));
        Assert.Equal("false", Pressed(calendar, "week"));
        Assert.Equal("Previous month", calendar.Find("[data-testid='cal-previous']").GetAttribute("aria-label"));
    }

    [Fact]
    public void Week_shows_the_seven_days_of_todays_week_monday_first_as_one_tall_row_named_by_its_range()
    {
        using var context = new BunitContext();

        var calendar = Render(context, [], p => p.Add(c => c.Range, CalendarRange.Week));

        Assert.Contains("task-calendar--week", calendar.Find("[data-testid='cal']").ClassName);
        Assert.Single(calendar.FindAll("[data-testid='cal-week']"));
        Assert.Equal(
            ["2026-10-05", "2026-10-06", "2026-10-07", "2026-10-08", "2026-10-09", "2026-10-10", "2026-10-11"],
            Days(calendar));
        Assert.Equal(
            ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"],
            calendar.FindAll(".task-calendar__weekday").Select(day => day.TextContent));
        Assert.Equal("5 – 11 October 2026", Heading(calendar));
        Assert.Equal("true", Pressed(calendar, "week"));
        Assert.Equal("false", Pressed(calendar, "month"));
        Assert.Contains("task-calendar__day--today", calendar.Find("[data-calendar-day='2026-10-06']").ClassName);
        Assert.Contains("task-calendar__day--weekend", calendar.Find("[data-calendar-day='2026-10-10']").ClassName);
    }

    [Fact]
    public void In_week_the_steps_move_a_week_say_so_and_today_comes_back_to_todays_week()
    {
        using var context = new BunitContext();

        var calendar = Render(context, [], p => p.Add(c => c.Range, CalendarRange.Week));
        var previous = calendar.Find("[data-testid='cal-previous']");
        Assert.Equal("Previous week", previous.GetAttribute("aria-label"));
        Assert.Equal("Previous week", previous.GetAttribute("title"));
        Assert.Equal("Next week", calendar.Find("[data-testid='cal-next']").GetAttribute("aria-label"));

        calendar.Find("[data-testid='cal-next']").Click();
        Assert.Equal("12 – 18 October 2026", Heading(calendar));
        Assert.Equal("2026-10-12", Days(calendar)[0]);

        calendar.Find("[data-testid='cal-next']").Click();
        calendar.Find("[data-testid='cal-next']").Click();
        // A week across two months names both; neither of its days is dimmed.
        Assert.Equal("26 October – 1 November 2026", Heading(calendar));
        Assert.DoesNotContain("task-calendar__day--outside", calendar.Find("[data-calendar-day='2026-11-01']").ClassName);

        calendar.Find("[data-testid='cal-previous']").Click();
        Assert.Equal("19 – 25 October 2026", Heading(calendar));

        calendar.Find("[data-testid='cal-today']").Click();
        Assert.Equal("5 – 11 October 2026", Heading(calendar));
    }

    [Fact]
    public void A_week_across_the_new_year_names_both_years()
    {
        using var context = new BunitContext();

        // January 2027 opens on a Friday, so its first week starts on the 28th of December.
        var calendar = Render(context, [], p => p
            .Add(c => c.Month, new DateOnly(2027, 1, 15))
            .Add(c => c.Range, CalendarRange.Week));

        Assert.Equal("28 December 2026 – 3 January 2027", Heading(calendar));
        Assert.Equal("2026-12-28", Days(calendar)[0]);
    }

    /// <summary>The navigation rule the person settled (scope, decision 1): Week opens on
    /// the week holding today when today is in the month shown, else on the month's
    /// first week; Month after Week is the month holding that week's Thursday.</summary>
    [Fact]
    public void Week_opens_on_todays_week_in_todays_month_and_on_the_first_week_of_another()
    {
        using var context = new BunitContext();

        var calendar = Render(context, []);

        calendar.Find("[data-testid='cal-range-week']").Click();
        Assert.Equal("5 – 11 October 2026", Heading(calendar));

        calendar.Find("[data-testid='cal-range-month']").Click();
        calendar.Find("[data-testid='cal-next']").Click();
        Assert.Equal("November 2026", Heading(calendar));

        // The 1st of November 2026 is a Sunday: its week starts in October.
        calendar.Find("[data-testid='cal-range-week']").Click();
        Assert.Equal("26 October – 1 November 2026", Heading(calendar));
    }

    [Theory]
    // Thursday the 29th of October: back to October, though the week reaches November.
    [InlineData("2026-10-26", "October 2026")]
    // Thursday the 1st of October: forward to October, though the week opens in September.
    [InlineData("2026-09-28", "October 2026")]
    // Thursday the 3rd of December.
    [InlineData("2026-11-30", "December 2026")]
    public void Month_after_week_is_the_month_holding_the_weeks_thursday(string weekStart, string month)
    {
        using var context = new BunitContext();
        var start = DateOnly.ParseExact(weekStart, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        var calendar = Render(context, [], p => p.Add(c => c.Range, CalendarRange.Week));
        var steps = (start.DayNumber - new DateOnly(2026, 10, 5).DayNumber) / 7;
        var button = steps < 0 ? "cal-previous" : "cal-next";
        for (var step = 0; step < Math.Abs(steps); step++) calendar.Find($"[data-testid='{button}']").Click();
        Assert.Equal(weekStart, Days(calendar)[0]);

        calendar.Find("[data-testid='cal-range-month']").Click();

        Assert.Equal(month, Heading(calendar));
        Assert.Equal(CalendarMonth.MonthOfWeek(start), CalendarMonth.FirstOf(calendar.Instance.ShownMonth));
    }

    [Theory]
    [InlineData("2026-10-20", "2026-10-06", "2026-10-05")]
    [InlineData("2026-11-03", "2026-10-06", "2026-10-26")]
    [InlineData("2027-02-10", "2026-10-06", "2027-02-01")]
    [InlineData("2026-11-03", "2026-11-18", "2026-11-16")]
    public void The_opening_week_is_todays_in_todays_month_else_the_one_holding_the_first(string month, string today, string monday)
    {
        static DateOnly Date(string iso) => DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        Assert.Equal(Date(monday), CalendarMonth.OpeningWeek(Date(month), Date(today)));
    }

    [Fact]
    public void Picking_a_range_reports_it_and_picking_the_pressed_one_reports_nothing()
    {
        using var context = new BunitContext();
        var reported = new List<CalendarRange>();

        var calendar = Render(context, [], p => p.Add(c => c.RangeChanged, (CalendarRange range) => reported.Add(range)));

        calendar.Find("[data-testid='cal-range-month']").Click();
        Assert.Empty(reported);

        calendar.Find("[data-testid='cal-range-week']").Click();
        calendar.Find("[data-testid='cal-range-week']").Click();
        calendar.Find("[data-testid='cal-range-month']").Click();

        Assert.Equal([CalendarRange.Week, CalendarRange.Month], reported);
    }

    /// <summary>The host keeps the range and hands it back after a press. That echo is
    /// not a new choice, so it leaves the week the reader stepped to where it is; a
    /// range the host changes on its own does switch the calendar.</summary>
    [Fact]
    public void The_hosts_echo_of_a_picked_range_keeps_the_week_and_a_new_range_from_the_host_switches()
    {
        using var context = new BunitContext();

        var calendar = Render(context, []);
        calendar.Find("[data-testid='cal-range-week']").Click();
        calendar.Find("[data-testid='cal-next']").Click();
        Assert.Equal("12 – 18 October 2026", Heading(calendar));

        calendar.Render(p => p.Add(c => c.Range, CalendarRange.Week));
        Assert.Equal("12 – 18 October 2026", Heading(calendar));

        calendar.Render(p => p.Add(c => c.Range, CalendarRange.Month));
        Assert.Equal("October 2026", Heading(calendar));
        Assert.Equal(5, calendar.FindAll("[data-testid='cal-week']").Count);
    }

    [Fact]
    public void A_week_column_lists_every_chip_of_its_day_with_nothing_folded()
    {
        using var context = new BunitContext();
        var day = new DateOnly(2026, 10, 8);

        var calendar = Render(context,
        [
            new("a", "Board view: status columns", day, CalendarTaskState.Ready),
            new("b", "Drag follows status flow", day, CalendarTaskState.Ready),
            new("c", "Release notes for task views", day, CalendarTaskState.Draft),
            new("d", "Week view", day, CalendarTaskState.InProgress)
        ], p => p.Add(c => c.Range, CalendarRange.Week));

        Assert.Equal(4, calendar.Find("[data-calendar-day='2026-10-08']").QuerySelectorAll(".task-calendar__chip").Length);
        Assert.Empty(calendar.FindAll("[data-testid='cal-day-2026-10-08-more']"));
        Assert.Empty(calendar.FindAll("[data-testid='cal-day-2026-10-08-fewer']"));
    }

    [Fact]
    public void A_week_draws_the_plan_bars_milestones_hours_and_day_totals_as_the_month_does()
    {
        using var context = new BunitContext();

        var calendar = Render(context,
            [new("sync", "Refactor sync", null, CalendarTaskState.Ready)],
            p => p
                .Add(c => c.Range, CalendarRange.Week)
                .Add(c => c.PlansAvailable, true)
                .Add(c => c.ShowPlans, true)
                .Add(c => c.Plans, [new CalendarPlan("p", "Calendar plans", "task-views", new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 21))])
                .Add(c => c.Milestones, [new CalendarMilestone("m", "Week view ships", new DateOnly(2026, 10, 9))])
                .Add(c => c.HoursAvailable, true)
                .Add(c => c.HoursBlocks, [new CalendarHoursBlock("sync", "Refactor sync", new DateOnly(2026, 10, 7), 10m)])
                .Add(c => c.CapacityOn, (Func<DateOnly, decimal?>)(_ => 8m)));

        // The bar opens on Thursday, the 4th column, and carries on past Sunday.
        var week = calendar.Find("[data-testid='cal-week']");
        Assert.Contains("task-calendar__week--plans", week.ClassName);
        var bar = calendar.Find("[data-testid='cal-plan-p']");
        Assert.Contains("grid-column: 4 / span 4", bar.GetAttribute("style"));
        Assert.Contains("task-calendar__plan--continues-after", bar.ClassName);
        Assert.NotNull(calendar.Find("[data-calendar-day='2026-10-09']").QuerySelector("[data-testid='cal-milestone-m']"));

        var wednesday = calendar.Find("[data-calendar-day='2026-10-07']");
        Assert.NotNull(wednesday.QuerySelector("[data-testid='cal-block-sync-2026-10-07']"));
        var total = calendar.Find("[data-testid='cal-day-2026-10-07-hours']");
        Assert.Equal("true", total.GetAttribute("data-over"));
        Assert.Contains("task-calendar__day--over", wednesday.ClassName);

        // "Add hours…" opens the editor in its column.
        calendar.Find("[data-testid='cal-day-2026-10-08-add-hours']").Click();
        Assert.NotNull(calendar.Find("[data-calendar-day='2026-10-08']").QuerySelector("[data-testid='cal-hours-editor']"));
    }

    [Fact]
    public async Task A_drop_on_a_week_column_reports_the_new_due_date_as_on_the_month()
    {
        using var context = new BunitContext();
        var changes = new List<CalendarDueChange>();

        var calendar = Render(context,
            [new("undated", "Persist the last view", null, CalendarTaskState.Ready)],
            p => p
                .Add(c => c.Range, CalendarRange.Week)
                .Add(c => c.OnDueChanged, (CalendarDueChange change) => changes.Add(change)));

        // The drag's hit test finds a day by this attribute alone, so a column without
        // it is a column nothing can be dropped on.
        Assert.Equal(7, calendar.FindAll("[data-calendar-day]").Count);

        await calendar.InvokeAsync(() => calendar.Instance.DropOnDay("undated", "2026-10-09"));

        Assert.Equal([new CalendarDueChange("undated", new DateOnly(2026, 10, 9))], changes);
    }
}
