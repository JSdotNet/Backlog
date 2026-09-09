using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.Services;
using Backlog.SharedKernel;

namespace Backlog.Modules.Dashboard.UnitTests;

/// <summary>
/// The sessions part: two local reads, scoped by window and by machine in this module
/// rather than by the sources.
/// <para>
/// The arithmetic is what these facts are about. Every figure here understates rather
/// than invents — a stretch of agent activity that ran past the edge of the window is
/// clipped to it, a session the activity source could not describe contributes no
/// duration at all, and both of those are said out loud rather than smoothed over.
/// </para>
/// <para>
/// Time on this part is <em>parsed activity</em> and never a session's span. The four
/// facts that used to assert the span arithmetic are gone rather than adapted, because
/// the arithmetic they described no longer exists; their intent lives on in the
/// clipping and overlap facts below, phrased in runs.
/// </para>
/// <para>
/// One figure here does not follow the period control, and it is asserted on its own:
/// the grid is the last seven dated days whichever window the reader picked. See
/// <see cref="Changing_the_period_moves_the_tiles_and_leaves_the_grid_alone"/>.
/// </para>
/// </summary>
public class SessionInsightsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    private const string Tower = "tower";
    private const string Laptop = "laptop";

    [Fact]
    public async Task An_unavailable_source_gives_the_part_the_sources_own_words()
    {
        var insights = Insights(new StubAssistantSessionSource
        {
            Availability = InsightAvailability.Unavailable("No agent folder was found.")
        });

        var result = await insights.GetSessionsAsync(DashboardScope.Default);

        Assert.False(result.HasValue);
        Assert.Equal("No agent folder was found.", result.Availability.Reason);
    }

    [Fact]
    public async Task A_source_that_throws_becomes_a_reason_rather_than_an_exception()
    {
        var insights = Insights(new StubAssistantSessionSource
        {
            Throw = new InvalidOperationException("The profile could not be read.")
        });

        var result = await insights.GetSessionsAsync(DashboardScope.Default);

        Assert.False(result.HasValue);
        Assert.Equal("The profile could not be read.", result.Availability.Reason);
    }

    /// <summary>
    /// Cancellation is the reader closing the dashboard or moving a filter, not a
    /// source failing, so it travels — the same shape the productivity derivation uses.
    /// </summary>
    [Fact]
    public async Task Cancelling_a_fetch_is_not_reported_as_an_unavailable_source()
    {
        var insights = Insights(new StubAssistantSessionSource
        {
            Throw = new OperationCanceledException()
        });

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => insights.GetSessionsAsync(DashboardScope.Default));
    }

    [Fact]
    public async Task A_session_inside_the_window_is_counted_and_its_time_summed()
    {
        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-1), "one"))
            },
            Activity(Ran("one", Tower, "Claude", (Now.AddHours(-3), Now.AddHours(-1)))));

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(1, value.Sessions);
        Assert.Equal(TimeSpan.FromHours(2), value.ActiveTime);
        Assert.Equal(Now.AddHours(-1), value.LastActivityAt);
        Assert.Equal(0, value.WithoutActivity);
    }

    /// <summary>
    /// The correction this part was rebuilt for. A session left open for eight hours in
    /// which an agent worked for twenty minutes is twenty minutes of agent-active time,
    /// and the figure this replaced would have read seven hours — which is how a week of
    /// transcripts came to report many times the hours there are in a week.
    /// </summary>
    [Fact]
    public async Task Active_time_is_the_activity_the_source_reported_rather_than_the_session_spans()
    {
        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Claude", Now.AddHours(-8), Now.AddHours(-1), "one"))
            },
            Activity(Ran("one", Tower, "Claude", (Now.AddHours(-3), Now.AddHours(-3).AddMinutes(20)))));

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(TimeSpan.FromMinutes(20), value.ActiveTime);
        Assert.NotEqual(TimeSpan.FromHours(7), value.ActiveTime);
    }

    /// <summary>
    /// A run that began before the window contributes only the part inside it, or two
    /// readings of the same quarter would disagree depending on what came before.
    /// </summary>
    [Fact]
    public async Task A_run_that_began_before_the_window_is_clipped_to_it()
    {
        var scope = new DashboardScope(Period: DashboardPeriod.FourWeeks);
        var (from, _) = scope.Window(Now);

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Claude", from.AddHours(-5), from.AddHours(3), "one"))
            },
            Activity(Ran("one", Tower, "Claude", (from.AddHours(-5), from.AddHours(3)))));

        var value = await ValueOf(insights, scope);

        Assert.Equal(1, value.Sessions);
        Assert.Equal(TimeSpan.FromHours(3), value.ActiveTime);
    }

    /// <summary>
    /// And the far edge, the same way: a run still going when the window closed adds the
    /// part the reader asked to see and nothing beyond it.
    /// </summary>
    [Fact]
    public async Task A_run_that_ran_past_the_end_of_the_window_is_clipped_to_it()
    {
        var scope = new DashboardScope(Period: DashboardPeriod.FourWeeks);
        var (_, to) = scope.Window(Now);

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Claude", to.AddHours(-2), to.AddHours(-1), "one"))
            },
            Activity(Ran("one", Tower, "Claude", (to.AddHours(-2), to.AddHours(3)))));

        var value = await ValueOf(insights, scope);

        Assert.Equal(TimeSpan.FromHours(2), value.ActiveTime);
    }

    /// <summary>
    /// The arithmetic the tile's permanent footnote is about. Two agents producing
    /// through the same hour is two hours of agent work, not one hour of wall clock —
    /// the figure is deliberately capable of exceeding the window it sits under, which
    /// is why the screen says so rather than leaving the reader to find out.
    /// </summary>
    [Fact]
    public async Task Runs_that_overlap_are_summed_rather_than_merged()
    {
        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(
                    Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-2), "one"),
                    Session(Tower, "Copilot", Now.AddHours(-3), Now.AddHours(-2), "two"))
            },
            Activity(
                Ran("one", Tower, "Claude", (Now.AddHours(-3), Now.AddHours(-2))),
                Ran("two", Tower, "Copilot", (Now.AddHours(-3), Now.AddHours(-2)))));

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(TimeSpan.FromHours(2), value.ActiveTime);

        // And the grid says the same thing about the same hour, because it is the same
        // sweep: two at once, and two agent-hours.
        var cell = Assert.Single(value.ActivityByHour.Where(hour => hour.Active > TimeSpan.Zero));
        Assert.Equal(2, cell.PeakSessions);
        Assert.Equal(TimeSpan.FromHours(2), cell.Active);
    }

    /// <summary>
    /// A session the activity source could not describe is real and is counted; what it
    /// cannot do is contribute a duration. Reporting it as zero-length activity would be
    /// inventing a fact, and leaving it out entirely would understate how many sessions
    /// there were — so it counts, adds nothing, and is named.
    /// <para>
    /// It covers more than a missing transcript. A session whose stretches all fell
    /// outside the horizon leaves no entry either, and it belongs in the same count for
    /// the same reason: the tile has nothing from it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_session_that_left_no_activity_counts_but_adds_no_time()
    {
        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(
                    Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-1), "one"),
                    Session(Tower, "Copilot", Now.AddHours(-2), Now.AddHours(-2), "two"))
            },
            Activity(Ran("one", Tower, "Claude", (Now.AddHours(-3), Now.AddHours(-1)))));

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(2, value.Sessions);
        Assert.Equal(TimeSpan.FromHours(2), value.ActiveTime);
        Assert.Equal(1, value.WithoutActivity);
    }

    /// <summary>
    /// The window is a filter, not a suggestion. A session whose last activity is older
    /// than the window has nothing to say about it.
    /// </summary>
    [Fact]
    public async Task A_session_that_stopped_before_the_window_is_left_out()
    {
        var insights = Insights(new StubAssistantSessionSource
        {
            Report = Report(Session(Tower, "Claude", Now.AddDays(-200), Now.AddDays(-199)))
        });

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(0, value.Sessions);
        Assert.Equal(TimeSpan.Zero, value.ActiveTime);
        Assert.Null(value.LastActivityAt);
    }

    [Fact]
    public async Task Focusing_a_machine_narrows_the_figures_to_that_machine()
    {
        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(
                    Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-1), "one"),
                    Session(Laptop, "Copilot", Now.AddHours(-9), Now.AddHours(-8), "two"))
            },
            Activity(
                Ran("one", Tower, "Claude", (Now.AddHours(-3), Now.AddHours(-1))),
                Ran("two", Laptop, "Copilot", (Now.AddHours(-9), Now.AddHours(-8)))));

        var value = await ValueOf(insights, DashboardScope.Default with { MachineId = Laptop });

        Assert.Equal(1, value.Sessions);
        Assert.Equal(TimeSpan.FromHours(1), value.ActiveTime);
    }

    /// <summary>
    /// The grid follows the machine filter as the tiles do. It refuses the period
    /// control and nothing else — a grid that quietly ignored the machine filter as well
    /// would be drawing another machine's hours under this one's heading.
    /// </summary>
    [Fact]
    public async Task Focusing_a_machine_narrows_the_grid_as_well_as_the_tiles()
    {
        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(
                    Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-2), "one"),
                    Session(Laptop, "Copilot", Now.AddHours(-9), Now.AddHours(-8), "two"))
            },
            Activity(
                Ran("one", Tower, "Claude", (Now.AddHours(-3), Now.AddHours(-2))),
                Ran("two", Laptop, "Copilot", (Now.AddHours(-9), Now.AddHours(-8)))));

        var everywhere = await ValueOf(insights, DashboardScope.Default);
        var focused = await ValueOf(insights, DashboardScope.Default with { MachineId = Laptop });

        Assert.Equal(2, everywhere.ActivityByHour.Count(hour => hour.Active > TimeSpan.Zero));

        var cell = Assert.Single(focused.ActivityByHour.Where(hour => hour.Active > TimeSpan.Zero));
        Assert.Equal(Now.AddHours(-9).Hour, cell.Hour);
    }

    /// <summary>
    /// A filter that fails open is worse than one that shows an empty part — the same
    /// rule the repository filter follows.
    /// </summary>
    [Fact]
    public async Task A_machine_id_that_matches_nothing_yields_nothing_rather_than_everything()
    {
        var insights = Insights(new StubAssistantSessionSource
        {
            Report = Report(Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-1)))
        });

        var value = await ValueOf(insights, DashboardScope.Default with { MachineId = "a-machine-that-left" });

        Assert.Equal(0, value.Sessions);
    }

    /// <summary>
    /// The breakdown answers whichever question the filter has not already answered.
    /// With every machine in view the interesting split is by machine; with one machine
    /// focused, the machine column would be a restatement of the filter, so the split
    /// is by assistant instead.
    /// </summary>
    [Fact]
    public async Task Every_machine_in_view_breaks_down_by_machine()
    {
        var insights = Insights(new StubAssistantSessionSource
        {
            Report = Report(
                Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-1)),
                Session(Tower, "Copilot", Now.AddHours(-5), Now.AddHours(-4)),
                Session(Laptop, "Copilot", Now.AddHours(-9), Now.AddHours(-8)))
        });

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(["DEV-TOWER", "DEV-LAPTOP"], value.Breakdown.Select(row => row.Name));
        Assert.Equal(2, value.Breakdown[0].Sessions);
    }

    [Fact]
    public async Task One_machine_focused_breaks_down_by_assistant()
    {
        var insights = Insights(new StubAssistantSessionSource
        {
            Report = Report(
                Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-1)),
                Session(Tower, "Copilot", Now.AddHours(-5), Now.AddHours(-4)),
                Session(Laptop, "Copilot", Now.AddHours(-9), Now.AddHours(-8)))
        });

        var value = await ValueOf(insights, DashboardScope.Default with { MachineId = Tower });

        // Not re-sorted here. The order is the derivation's promise — busiest first,
        // ties by name — and a test that sorted the answer before looking at it would
        // pass just as happily if that promise were dropped.
        Assert.Equal(["Claude", "Copilot"], value.Breakdown.Select(row => row.Name));
        Assert.All(value.Breakdown, row => Assert.Equal(1, row.Sessions));

        // Under an assistant breakdown the assistant is what makes a row one row.
        Assert.Equal(["Claude", "Copilot"], value.Breakdown.Select(row => row.Key));
    }

    /// <summary>
    /// Two machines called the same thing are two machines, and the breakdown has to be
    /// able to say so twice. The rows carry one label and two keys — anything that keyed
    /// them on the label would either merge two machines' figures or, in a table, hand
    /// the renderer two siblings with one key and take the surface down with it.
    /// </summary>
    [Fact]
    public async Task Two_machines_sharing_a_name_are_two_rows_with_two_keys()
    {
        var insights = Insights(new StubAssistantSessionSource
        {
            Report = Report(
                Session("first", "DEV-TOWER", "Claude", Now.AddHours(-3), Now.AddHours(-1)),
                Session("second", "DEV-TOWER", "Claude", Now.AddHours(-9), Now.AddHours(-8)))
        });

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(2, value.Breakdown.Count);
        Assert.Equal(["DEV-TOWER", "DEV-TOWER"], value.Breakdown.Select(row => row.Name));
        Assert.Equal(2, value.Breakdown.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The window is half-open, and both edges are asserted rather than assumed. A
    /// session whose last activity landed exactly as the window opened is in it; one that
    /// began exactly as it closed is not.
    /// </summary>
    [Fact]
    public async Task A_session_whose_last_activity_is_the_moment_the_window_opened_is_in_it()
    {
        var scope = new DashboardScope(Period: DashboardPeriod.FourWeeks);
        var (from, _) = scope.Window(Now);

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Claude", from.AddHours(-1), from, "one"))
            },
            Activity(Ran("one", Tower, "Claude", (from.AddHours(-1), from))));

        var value = await ValueOf(insights, scope);

        Assert.Equal(1, value.Sessions);

        // In the window and contributing nothing to it: every minute it ran was before
        // the window opened, so the run is clipped away entirely rather than the source
        // having had nothing to say about it.
        Assert.Equal(TimeSpan.Zero, value.ActiveTime);
    }

    [Fact]
    public async Task A_session_that_began_the_moment_the_window_closed_is_out_of_it()
    {
        var scope = new DashboardScope(Period: DashboardPeriod.FourWeeks);
        var (_, to) = scope.Window(Now);

        var insights = Insights(new StubAssistantSessionSource
        {
            Report = Report(Session(Tower, "Claude", to, to.AddHours(1)))
        });

        var value = await ValueOf(insights, scope);

        Assert.Equal(0, value.Sessions);
    }

    /// <summary>
    /// The weekly series the part draws as columns, and the one instant it is allowed
    /// to bucket on.
    /// <para>
    /// A session is one mark in the ISO week it last moved. That is the only week every
    /// session has: the start is optional and there is no end recorded at all, so any
    /// other rule would either invent a fact or drop the sessions missing one. The cost
    /// is that a session running across a week boundary is drawn once rather than in
    /// both weeks, which the chart's own caption says out loud.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Sessions_are_counted_into_the_week_they_last_moved()
    {
        var insights = Insights(new StubAssistantSessionSource
        {
            Report = Report(
                // Two in the ISO week before the one Now falls in...
                Session(Tower, "Claude", Now.AddDays(-8), Now.AddDays(-8)),
                Session(Tower, "Copilot", Now.AddDays(-7), Now.AddDays(-7)),
                // ...and one in Now's own week.
                Session(Tower, "Claude", Now.AddHours(-2), Now.AddHours(-1)))
        });

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal([2m, 1m], value.SessionsPerWeek.TakeLast(2).Select(point => point.Value));

        // ISO week labels, not a rolling seven-day count back from today: the last
        // bucket has to mean the same thing tomorrow as it does now, or two readings a
        // day apart would put the same session in different columns.
        Assert.Equal(["W33", "W34"], value.SessionsPerWeek.TakeLast(2).Select(point => point.Label));
    }

    /// <summary>
    /// A quiet week is a fact about the window and is drawn as one. Dropping it would
    /// close the gap and draw two weeks either side of it as consecutive, which is the
    /// one thing a reader takes off a column chart without checking.
    /// </summary>
    [Fact]
    public async Task A_week_nobody_worked_is_a_zero_point_not_a_missing_one()
    {
        var scope = DashboardScope.Default;

        var insights = Insights(new StubAssistantSessionSource
        {
            Report = Report(
                Session(Tower, "Claude", Now.AddDays(-15), Now.AddDays(-15)),
                Session(Tower, "Claude", Now.AddHours(-2), Now.AddHours(-1)))
        });

        var value = await ValueOf(insights, scope);

        // One bucket per ISO week the window touches. The window is a whole number of
        // weeks back from a day boundary, so it opens on the same weekday it closes and
        // the axis carries one bucket more than it has weeks.
        Assert.Equal(scope.Weeks + 1, value.SessionsPerWeek.Count);

        Assert.Equal([1m, 0m, 1m], value.SessionsPerWeek.TakeLast(3).Select(point => point.Value));
    }

    [Fact]
    public async Task The_series_follows_the_machine_filter()
    {
        var insights = Insights(new StubAssistantSessionSource
        {
            Report = Report(
                Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-1)),
                Session(Laptop, "Copilot", Now.AddHours(-3), Now.AddHours(-1)),
                Session(Laptop, "Claude", Now.AddDays(-8), Now.AddDays(-8)))
        });

        var value = await ValueOf(insights, DashboardScope.Default with { MachineId = Laptop });

        // One week each for the laptop. The tower's session sits in the same week as
        // one of them, so a filter that failed open would read three here rather than
        // two — which is what makes the total worth asserting beside the tail.
        Assert.Equal([1m, 1m], value.SessionsPerWeek.TakeLast(2).Select(point => point.Value));
        Assert.Equal(2m, value.SessionsPerWeek.Sum(point => point.Value));
    }

    /// <summary>
    /// The columns inherit the part's refusal of the repository dimension rather than
    /// quietly honouring it. Claude records no repository against a session, so a
    /// series that moved with the repository filter would be drawing the absence of
    /// Claude's data as a fall in sessions.
    /// </summary>
    [Fact]
    public async Task The_series_ignores_the_repository_filter()
    {
        var insights = Insights(new StubAssistantSessionSource
        {
            Report = Report(
                Session(Tower, "Claude", Now.AddDays(-8), Now.AddDays(-8)),
                Session(Tower, "Copilot", Now.AddHours(-3), Now.AddHours(-1)))
        });

        var everywhere = await ValueOf(insights, DashboardScope.Default);
        var focused = await ValueOf(insights, DashboardScope.Default with { RepositoryAlias = "backlog" });

        Assert.Equal(everywhere.SessionsPerWeek, focused.SessionsPerWeek);

        // Not two empty series agreeing with each other.
        Assert.Equal(2m, focused.SessionsPerWeek.Sum(point => point.Value));
    }

    /// <summary>
    /// The bucketing instant, pinned against the plausible alternative. A session that
    /// began before the window opened is bucketed on its last activity, so it lands in
    /// the week it last moved — bucketed on its start it would fall off the axis
    /// altogether and the chart would draw nothing for a session the tiles above it
    /// are counting.
    /// </summary>
    [Fact]
    public async Task A_session_that_started_before_the_window_still_counts_in_the_week_it_last_moved()
    {
        var scope = DashboardScope.Default;
        var (from, _) = scope.Window(Now);

        var insights = Insights(new StubAssistantSessionSource
        {
            Report = Report(Session(Tower, "Claude", from.AddDays(-5), Now.AddHours(-1)))
        });

        var value = await ValueOf(insights, scope);

        Assert.Equal(1, value.Sessions);
        Assert.Equal(1m, value.SessionsPerWeek[^1].Value);

        // Once, and only in the last bucket.
        Assert.Equal(1m, value.SessionsPerWeek.Sum(point => point.Value));
    }

    [Fact]
    public async Task A_capped_read_is_reported_as_capped_with_whatever_it_could_not_read()
    {
        var insights = Insights(new StubAssistantSessionSource
        {
            Report = new AssistantSessionReport(
                [Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-1))],
                ["Copilot"],
                Capped: true,
                CapPerAssistant: 100)
        });

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.True(value.Capped);
        Assert.Equal(["Copilot"], value.Unreadable);

        // The number the sentence on screen names, carried from the source rather than
        // kept as a second copy on the surface.
        Assert.Equal(100, value.CapPerAssistant);
    }

    /// <summary>
    /// One read serves every scope. Moving the machine filter or the window must not
    /// send this part back to the disk, because the answer to both is already in the
    /// report it has — and that covers the availability question as well as the report,
    /// or the claim would hold for only half of every call.
    /// </summary>
    [Fact]
    public async Task Changing_the_scope_derives_again_rather_than_reading_again()
    {
        var source = new StubAssistantSessionSource();
        var insights = Insights(source);

        _ = await insights.GetSessionsAsync(DashboardScope.Default);
        _ = await insights.GetSessionsAsync(DashboardScope.Default with { MachineId = Tower });
        _ = await insights.GetSessionsAsync(new DashboardScope(Period: DashboardPeriod.FourWeeks));

        Assert.Equal(1, source.Calls);
        Assert.Equal(1, source.AvailabilityCalls);
    }

    [Fact]
    public async Task Refreshing_goes_back_to_the_source()
    {
        var source = new StubAssistantSessionSource();
        var insights = Insights(source);

        _ = await insights.GetSessionsAsync(DashboardScope.Default);
        insights.Invalidate();
        _ = await insights.GetSessionsAsync(DashboardScope.Default);

        Assert.Equal(2, source.Calls);
        Assert.Equal(2, source.AvailabilityCalls);
    }

    /// <summary>
    /// A source that refuses is asked once and remembered, the same as one that answers.
    /// A refusal re-asked on every filter move would make an unconfigured part the one
    /// thing on the surface that does work when nothing changed.
    /// </summary>
    [Fact]
    public async Task An_unavailable_source_is_not_re_asked_on_every_scope_change()
    {
        var source = new StubAssistantSessionSource
        {
            Availability = InsightAvailability.Unavailable("No agent folder was found.")
        };

        var insights = Insights(source);

        _ = await insights.GetSessionsAsync(DashboardScope.Default);
        var second = await insights.GetSessionsAsync(DashboardScope.Default with { MachineId = Tower });

        Assert.False(second.HasValue);
        Assert.Equal("No agent folder was found.", second.Availability.Reason);
        Assert.Equal(1, source.AvailabilityCalls);
        Assert.Equal(0, source.Calls);
    }


    /// <summary>
    /// The tile and the grid come off one sweep, so with the activity inside the seven
    /// days the grid covers, the cells add up to the number above them exactly. Two
    /// passes over the same intervals would be two definitions of "at once", and this is
    /// what would go red the day somebody wrote the second one.
    /// </summary>
    [Fact]
    public async Task The_grid_totals_to_the_active_time_tile()
    {
        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Claude", Now.AddDays(-2), Now.AddHours(-1), "one"))
            },
            Activity(Ran(
                "one",
                Tower,
                "Claude",
                (Now.AddDays(-2), Now.AddDays(-2).AddMinutes(90)),
                (Now.AddHours(-3), Now.AddHours(-1)))));

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(
            value.ActiveTime,
            value.ActivityByHour.Aggregate(TimeSpan.Zero, (running, hour) => running + hour.Active));

        // Not two zeros agreeing with each other.
        Assert.Equal(TimeSpan.FromMinutes(210), value.ActiveTime);
    }

    /// <summary>
    /// The day column counts sessions, and a session is counted once however many hours
    /// it spanned. This is the arithmetic the column exists to avoid: adding the row's
    /// peaks would report one long session once per hour it was running in.
    /// </summary>
    [Fact]
    public async Task A_day_counts_each_session_once_however_long_it_ran()
    {
        var ran = (Now.AddHours(-6), Now.AddHours(-1));

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(
                    Session(Tower, "Claude", Now.AddHours(-6), Now.AddHours(-1), "one"),
                    Session(Tower, "Claude", Now.AddHours(-6), Now.AddHours(-1), "two"))
            },
            Activity(
                Ran("one", Tower, "Claude", ran),
                Ran("two", Tower, "Claude", ran)));

        var value = await ValueOf(insights, DashboardScope.Default);

        var today = value.ActivityByDay.Single(day => day.Day == DateOnly.FromDateTime(Now.UtcDateTime));

        Assert.Equal(2, today.Sessions);

        // And the row it sits beside spans five hours, so a column that had summed the
        // peaks would read ten rather than two.
        Assert.True(value.ActivityByHour.Count(hour => hour.Day == today.Day && hour.PeakSessions > 0) >= 5);
    }

    /// <summary>
    /// A session that ran past midnight belongs to both days. Neither day is wrong and
    /// neither is the whole of it, which is why this is a count per day rather than a
    /// total anybody could add up across the grid.
    /// </summary>
    [Fact]
    public async Task A_session_that_ran_past_midnight_is_one_session_on_each_day()
    {
        var midnight = new DateTimeOffset(Now.UtcDateTime.Date, TimeSpan.Zero);

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Claude", midnight.AddHours(-1), midnight.AddHours(1), "owl"))
            },
            Activity(Ran("owl", Tower, "Claude", (midnight.AddHours(-1), midnight.AddHours(1)))));

        var value = await ValueOf(insights, DashboardScope.Default);

        var yesterday = DateOnly.FromDateTime(midnight.UtcDateTime.AddDays(-1));
        var today = DateOnly.FromDateTime(midnight.UtcDateTime);

        Assert.Equal(1, value.ActivityByDay.Single(day => day.Day == yesterday).Sessions);
        Assert.Equal(1, value.ActivityByDay.Single(day => day.Day == today).Sessions);
    }

    /// <summary>A day nobody ran on is a zero in the column, not a gap — the same rule the
    /// cells beside it are drawn under.</summary>
    [Fact]
    public async Task A_day_nobody_ran_on_is_a_zero_rather_than_a_missing_row()
    {
        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-1), "one"))
            },
            Activity(Ran("one", Tower, "Claude", (Now.AddHours(-3), Now.AddHours(-1)))));

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(7, value.ActivityByDay.Count);
        Assert.Contains(value.ActivityByDay, day => day.Sessions == 0);
    }

    /// <summary>
    /// A session waiting on a prompt is on the go and is not producing, so the open count
    /// is above the peak in the hour it was waiting through. This is the whole reason both
    /// travel: one grid without the other cannot show the gap.
    /// </summary>
    [Fact]
    public async Task A_session_waiting_on_a_prompt_is_open_and_is_not_producing()
    {
        var ran = (Now.AddHours(-4), Now.AddHours(-4).AddMinutes(10));
        var waited = (Now.AddHours(-4).AddMinutes(10), Now.AddHours(-2));

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Claude", Now.AddHours(-4), Now.AddHours(-2), "one"))
            },
            Activity(RanAndWaited("one", Tower, "Claude", [ran], [waited])));

        var value = await ValueOf(insights, DashboardScope.Default);

        var waitingHour = value.ActivityByHour.Single(hour =>
            hour.Day == DateOnly.FromDateTime(Now.UtcDateTime) && hour.Hour == Now.AddHours(-3).Hour);

        Assert.Equal(0, waitingHour.PeakSessions);
        Assert.Equal(1, waitingHour.OpenSessions);
    }

    /// <summary>
    /// Silence that never resumed is not an open session. Nothing records an end, so
    /// counting to the last thing that happened would hold an abandoned window open for
    /// days — the span-shaped overstatement the active time was corrected for.
    /// </summary>
    [Fact]
    public async Task A_session_that_went_quiet_and_never_resumed_is_not_open_through_the_silence()
    {
        var ran = (Now.AddHours(-6), Now.AddHours(-6).AddMinutes(5));

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Claude", Now.AddHours(-6), Now.AddHours(-6), "one"))
            },
            Activity(Ran("one", Tower, "Claude", ran)));

        var value = await ValueOf(insights, DashboardScope.Default);

        var later = value.ActivityByHour.Single(hour =>
            hour.Day == DateOnly.FromDateTime(Now.UtcDateTime) && hour.Hour == Now.AddHours(-2).Hour);

        Assert.Equal(0, later.OpenSessions);
    }

    /// <summary>
    /// The day's count splits on the same question the grid outlines on, and the two parts
    /// are counted independently: a session that ran across the edge of the working day is
    /// in both, so they do not sum to the whole.
    /// </summary>
    [Fact]
    public async Task A_session_across_the_edge_of_the_working_day_counts_on_both_sides_of_it()
    {
        // The Monday inside the grid's seven days, so the default week has it working
        // 09:00 to 17:30. The run starts inside those hours and ends after them.
        var monday = new DateTimeOffset(2026, 8, 17, 16, 0, 0, TimeSpan.Zero);

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Claude", monday, monday.AddHours(4), "one"))
            },
            Activity(Ran("one", Tower, "Claude", (monday, monday.AddHours(4)))),
            zone: TimeZoneInfo.Utc);

        var value = await ValueOf(insights, DashboardScope.Default);

        var day = value.ActivityByDay.Single(entry => entry.Day == new DateOnly(2026, 8, 17));

        Assert.Equal(1, day.Sessions);
        Assert.Equal(1, day.SessionsInWorkingHours);
        Assert.Equal(1, day.SessionsOutsideWorkingHours);

        // One session, two answers, and they deliberately do not add to the first.
        Assert.NotEqual(day.Sessions, day.SessionsInWorkingHours + day.SessionsOutsideWorkingHours);
    }

    /// <summary>A day the reader does not work has every hour outside it, so nothing is
    /// outlined and nothing counts as in hours.</summary>
    [Fact]
    public async Task A_day_the_reader_does_not_work_counts_everything_outside_hours()
    {
        // The Sunday inside the grid's seven days, which the default week has off.
        var sunday = new DateTimeOffset(2026, 8, 16, 11, 0, 0, TimeSpan.Zero);

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Claude", sunday, sunday.AddHours(1), "one"))
            },
            Activity(Ran("one", Tower, "Claude", (sunday, sunday.AddHours(1)))),
            zone: TimeZoneInfo.Utc);

        var value = await ValueOf(insights, DashboardScope.Default);

        var day = value.ActivityByDay.Single(entry => entry.Day == new DateOnly(2026, 8, 16));

        Assert.Equal(0, day.SessionsInWorkingHours);
        Assert.Equal(1, day.SessionsOutsideWorkingHours);

        Assert.All(
            value.ActivityByHour.Where(hour => hour.Day == new DateOnly(2026, 8, 16)),
            hour => Assert.False(hour.InWorkingHours));
    }

    /// <summary>
    /// The hours a working day covers, on the cell the grid outlines from. Half past five
    /// leaves the seventeenth hour half worked and there is no half-outlined cell, so it
    /// is marked whole and eighteen is the first hour outside.
    /// </summary>
    [Fact]
    public async Task An_hour_only_partly_inside_the_working_day_still_counts_as_inside_it()
    {
        var monday = new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero);

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Claude", monday, monday.AddHours(1), "one"))
            },
            Activity(Ran("one", Tower, "Claude", (monday, monday.AddHours(1)))),
            zone: TimeZoneInfo.Utc);

        var value = await ValueOf(insights, DashboardScope.Default);

        var day = value.ActivityByHour.Where(hour => hour.Day == new DateOnly(2026, 8, 17)).ToList();

        Assert.False(day.Single(hour => hour.Hour == 8).InWorkingHours);
        Assert.True(day.Single(hour => hour.Hour == 9).InWorkingHours);
        Assert.True(day.Single(hour => hour.Hour == 17).InWorkingHours);
        Assert.False(day.Single(hour => hour.Hour == 18).InWorkingHours);
    }

    /// <summary>The column and the grid appear together or not at all. A count beside an
    /// axis the part declined to draw would be a figure with nothing to read it
    /// against.</summary>
    [Fact]
    public async Task A_scope_with_no_activity_carries_no_day_counts_either()
    {
        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-1), "one"))
            },
            Activity());

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Empty(value.ActivityByHour);
        Assert.Empty(value.ActivityByDay);
    }

    /// <summary>
    /// The grid is the one figure on this surface drawn on the reader's own clock, and a
    /// zone two hours east puts the same run two columns along. Both zones are asserted,
    /// so the fact cannot pass by the zone being ignored.
    /// </summary>
    [Fact]
    public async Task The_grid_is_drawn_on_local_hours_rather_than_UTC()
    {
        var ran = (Now.AddDays(-1).AddHours(-2), Now.AddDays(-1).AddHours(-1));

        var sessions = new StubAssistantSessionSource
        {
            Report = Report(Session(Tower, "Claude", ran.Item1, ran.Item2, "one"))
        };

        var utc = await ValueOf(Insights(sessions, Activity(Ran("one", Tower, "Claude", ran))), DashboardScope.Default);

        var east = await ValueOf(
            Insights(sessions, Activity(Ran("one", Tower, "Claude", ran)), PlusTwo),
            DashboardScope.Default);

        Assert.Equal(ran.Item1.Hour, Worked(utc).Hour);
        Assert.Equal(ran.Item1.Hour + 2, Worked(east).Hour);
    }

    /// <summary>
    /// <b>The asymmetry the part is obliged to state on screen.</b> Every tile widens
    /// when the reader moves from four weeks to twelve; the grid does not move at all,
    /// because it is the last seven dated days and nothing else. Nothing but this asserts
    /// it, and an implementation that quietly let the grid follow the period would pass
    /// every other fact in this file.
    /// </summary>
    [Fact]
    public async Task Changing_the_period_moves_the_tiles_and_leaves_the_grid_alone()
    {
        // One run six weeks back — inside twelve weeks, outside four — and one
        // yesterday, inside the seven days the grid draws.
        var older = (Now.AddDays(-42), Now.AddDays(-42).AddHours(1));
        var recent = (Now.AddDays(-1), Now.AddDays(-1).AddHours(1));

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(
                    Session(Tower, "Claude", older.Item1, older.Item2, "old"),
                    Session(Tower, "Claude", recent.Item1, recent.Item2, "new"))
            },
            Activity(
                Ran("old", Tower, "Claude", older),
                Ran("new", Tower, "Claude", recent)));

        var four = await ValueOf(insights, new DashboardScope(Period: DashboardPeriod.FourWeeks));
        var twelve = await ValueOf(insights, new DashboardScope(Period: DashboardPeriod.TwelveWeeks));

        // The tiles move.
        Assert.Equal(TimeSpan.FromHours(1), four.ActiveTime);
        Assert.Equal(TimeSpan.FromHours(2), twelve.ActiveTime);

        // The grid does not — same days, same hours, same readings, cell for cell.
        Assert.Equal(four.ActivityByHour, twelve.ActivityByHour);
        Assert.Equal(168, four.ActivityByHour.Count);
        Assert.Equal(TimeSpan.FromHours(1), Worked(four).Active);
    }

    /// <summary>
    /// The property the horizon exists for. Moving the period control derives again over
    /// the reading already in hand; it does not send the expensive source back to the
    /// disk, which is the whole reason the horizon is a constant rather than the scope's
    /// own window.
    /// </summary>
    [Fact]
    public async Task Changing_the_period_derives_again_rather_than_reading_again()
    {
        var activity = new StubAssistantActivitySource();
        var insights = Insights(new StubAssistantSessionSource(), activity);

        _ = await insights.GetSessionsAsync(new DashboardScope(Period: DashboardPeriod.FourWeeks));
        _ = await insights.GetSessionsAsync(new DashboardScope(Period: DashboardPeriod.TwelveWeeks));

        Assert.Equal(1, activity.Calls);
    }

    /// <summary>
    /// And the horizon it is asked for is the widest window the surface can be moved to,
    /// not whichever one it happens to be showing. A source asked for four weeks would
    /// make the twelve-week reading wrong or make it a second read.
    /// </summary>
    [Fact]
    public async Task The_activity_source_is_asked_for_the_widest_window_the_surface_can_show()
    {
        var activity = new StubAssistantActivitySource();
        var insights = Insights(new StubAssistantSessionSource(), activity);

        _ = await insights.GetSessionsAsync(new DashboardScope(Period: DashboardPeriod.FourWeeks));

        Assert.Equal(Now.AddDays(-7 * 12), Assert.Single(activity.Horizons));
    }

    /// <summary>
    /// The expensive source having a bad minute must not take the part down, and the
    /// reader gets the source's own words rather than a blank grid with no explanation.
    /// </summary>
    [Fact]
    public async Task An_activity_source_that_throws_becomes_a_reason_rather_than_an_exception()
    {
        var insights = Insights(
            new StubAssistantSessionSource(),
            new StubAssistantActivitySource { Throw = new InvalidOperationException("The transcripts could not be read.") });

        var result = await insights.GetSessionsAsync(DashboardScope.Default);

        Assert.False(result.HasValue);
        Assert.Equal("The transcripts could not be read.", result.Availability.Reason);
    }

    /// <summary>
    /// The threshold that ended a run is the source's judgement, and the sentence on
    /// screen has to name that number rather than a copy of it kept here — the answer
    /// moves under it, so a stale copy would explain one figure with another's rule.
    /// </summary>
    [Fact]
    public async Task The_threshold_the_source_folded_at_travels_to_the_surface()
    {
        var insights = Insights(
            new StubAssistantSessionSource(),
            new StubAssistantActivitySource
            {
                Report = new AssistantActivityReport([], [], Now.AddDays(-7 * 12), TimeSpan.FromMinutes(5))
            });

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(TimeSpan.FromMinutes(5), value.IdleAfter);
    }

    /// <summary>
    /// Waiting is reported per breakdown row as well as in total, because a zero on one
    /// row beside a real figure on the next is where a reader actually meets the limit of
    /// what one of the assistants records.
    /// </summary>
    [Fact]
    public async Task Waiting_is_reported_per_row_as_well_as_in_total()
    {
        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(
                    Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-1), "one"),
                    Session(Laptop, "Claude", Now.AddHours(-9), Now.AddHours(-8), "two"))
            },
            Activity(
                Waited("one", Tower, "Claude", (Now.AddHours(-3), Now.AddHours(-1))),
                Ran("two", Laptop, "Claude", (Now.AddHours(-9), Now.AddHours(-8)))));

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(TimeSpan.FromHours(2), value.Waiting);
        Assert.Equal(TimeSpan.FromHours(2), Row(value, "DEV-TOWER").Waiting);
        Assert.Equal(TimeSpan.Zero, Row(value, "DEV-LAPTOP").Waiting);
    }

    /// <summary>
    /// One of the two assistants cannot mark the boundary a wait needs, so its rows read
    /// zero. That is a limit of what it writes down rather than a claim that nobody
    /// waited, and the breakdown is where the two sit side by side.
    /// </summary>
    [Fact]
    public async Task A_row_whose_assistant_records_no_prompt_boundary_reports_no_waiting()
    {
        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(
                    Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-1), "one"),
                    Session(Tower, "Copilot", Now.AddHours(-9), Now.AddHours(-8), "two"))
            },
            Activity(
                Waited("one", Tower, "Claude", (Now.AddHours(-3), Now.AddHours(-1))),
                Ran("two", Tower, "Copilot", (Now.AddHours(-9), Now.AddHours(-8)))));

        var value = await ValueOf(insights, DashboardScope.Default with { MachineId = Tower });

        Assert.Equal(TimeSpan.FromHours(2), Row(value, "Claude").Waiting);
        Assert.Equal(TimeSpan.Zero, Row(value, "Copilot").Waiting);

        // The Copilot row still reports the time it did work, so the zero above reads as
        // "no wait recorded" rather than "no row worth drawing".
        Assert.Equal(TimeSpan.FromHours(1), Row(value, "Copilot").ActiveTime);
    }

    /// <summary>
    /// A part with nothing parsable draws no grid rather than an axis of 168 zeros, the
    /// rule the weekly columns are already rendered under.
    /// </summary>
    [Fact]
    public async Task A_scope_with_no_activity_at_all_carries_no_grid()
    {
        var insights = Insights(new StubAssistantSessionSource
        {
            Report = Report(Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-1), "one"))
        });

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Empty(value.ActivityByHour);
        Assert.Equal(1, value.Sessions);
    }

    /// <summary>
    /// And once there is something to draw, every hour of the seven days is on the axis —
    /// an hour nobody worked is a zero and not a gap, because a heatmap draws those two
    /// differently.
    /// </summary>
    [Fact]
    public async Task An_hour_nobody_worked_is_still_a_cell_of_the_grid()
    {
        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-2), "one"))
            },
            Activity(Ran("one", Tower, "Claude", (Now.AddHours(-3), Now.AddHours(-2)))));

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(168, value.ActivityByHour.Count);
        Assert.Equal(167, value.ActivityByHour.Count(hour => hour.Active == TimeSpan.Zero));
        Assert.All(value.ActivityByHour, hour => Assert.InRange(hour.Hour, 0, 23));
    }

    /// <summary>The tile is a read-out of the same sweep the first grid is drawn from, so
    /// it counts sessions that were producing and cannot have measured differently from
    /// the cells under it.</summary>
    [Fact]
    public async Task The_most_sessions_at_once_is_the_peak_of_the_sessions_that_were_producing()
    {
        var ran = (Now.AddHours(-3), Now.AddHours(-2));

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(
                    Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-2), "one"),
                    Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-2), "two"))
            },
            Activity(
                Ran("one", Tower, "Claude", ran),
                Ran("two", Tower, "Claude", ran)));

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(2, value.MostSessionsAtOnce!.Peak);
    }

    /// <summary>
    /// Producing, not open. A session stopped with nothing having prompted it yet is on
    /// the go and is not working, and a tile that counted those would be the second grid's
    /// figure under the first grid's name.
    /// </summary>
    [Fact]
    public async Task The_most_sessions_at_once_ignores_sessions_that_were_only_waiting()
    {
        var span = (Now.AddHours(-3), Now.AddHours(-2));

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(
                    Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-2), "one"),
                    Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-2), "two"),
                    Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-2), "three"))
            },
            Activity(
                Ran("one", Tower, "Claude", span),
                Waited("two", Tower, "Claude", span),
                Waited("three", Tower, "Claude", span)));

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(1, value.MostSessionsAtOnce!.Peak);
    }

    /// <summary>
    /// The hour is the reader's own, which makes it the one figure on this part not given
    /// in UTC — the same clock the grid under it is drawn on, named with the same pair, so
    /// a tile and a cell point at one cell in one set of words.
    /// </summary>
    [Fact]
    public async Task The_most_sessions_at_once_names_the_local_hour_it_happened_in()
    {
        // 06:00 UTC, which is 08:00 on a clock two hours ahead.
        var ran = (Now.AddHours(-3), Now.AddHours(-2));

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-2), "one"))
            },
            Activity(Ran("one", Tower, "Claude", ran)),
            zone: PlusTwo);

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(new DateOnly(2026, 8, 19), value.MostSessionsAtOnce!.Day);
        Assert.Equal(8, value.MostSessionsAtOnce!.Hour);
    }

    /// <summary>
    /// Two hours that both reached the peak are separated by when they happened, not by
    /// whatever order a dictionary enumerated in. "When it first got that busy" has one
    /// answer; "one of the times it got that busy" is a figure free to move between two
    /// refreshes of an unchanged profile.
    /// </summary>
    [Fact]
    public async Task The_most_sessions_at_once_names_the_earliest_hour_when_two_hours_tie()
    {
        var early = (Now.AddHours(-6), Now.AddHours(-5));
        var late = (Now.AddHours(-3), Now.AddHours(-2));

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(
                    Session(Tower, "Claude", Now.AddHours(-6), Now.AddHours(-2), "one"),
                    Session(Tower, "Claude", Now.AddHours(-6), Now.AddHours(-2), "two"))
            },
            Activity(
                Ran("one", Tower, "Claude", early, late),
                Ran("two", Tower, "Claude", early, late)));

        var value = await ValueOf(insights, DashboardScope.Default);

        // Both hours reached two, and the earlier of them is the one named.
        Assert.Equal(2, value.MostSessionsAtOnce!.Peak);
        Assert.Equal(Now.AddHours(-6).Hour, value.MostSessionsAtOnce!.Hour);
    }

    /// <summary>Unlike the grid below it, this figure follows the period control — it is a
    /// tile, and it widens with the tiles beside it.</summary>
    [Fact]
    public async Task The_most_sessions_at_once_widens_with_the_period()
    {
        var recent = (Now.AddHours(-3), Now.AddHours(-2));
        var older = (Now.AddDays(-42), Now.AddDays(-42).AddHours(1));

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(
                    Session(Tower, "Claude", Now.AddDays(-42), Now.AddHours(-2), "one"),
                    Session(Tower, "Claude", Now.AddDays(-42), Now.AddHours(-2), "two"),
                    Session(Tower, "Claude", Now.AddDays(-42), Now.AddDays(-42), "three"))
            },
            Activity(
                Ran("one", Tower, "Claude", recent, older),
                Ran("two", Tower, "Claude", recent, older),
                Ran("three", Tower, "Claude", older)));

        var four = await ValueOf(insights, new DashboardScope(Period: DashboardPeriod.FourWeeks));
        var twelve = await ValueOf(insights, new DashboardScope(Period: DashboardPeriod.TwelveWeeks));

        Assert.Equal(2, four.MostSessionsAtOnce!.Peak);
        Assert.Equal(3, twelve.MostSessionsAtOnce!.Peak);
    }

    /// <summary>
    /// Null, never a zero-peak. A window nothing produced in has no busiest hour, and
    /// inventing one would put a real day and hour beside a nothing — the scalar form of
    /// the empty-versus-168-zeros rule the grid already follows.
    /// </summary>
    [Fact]
    public async Task There_is_no_most_sessions_at_once_when_nothing_produced()
    {
        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-2), "one"))
            },
            Activity(Waited("one", Tower, "Claude", (Now.AddHours(-3), Now.AddHours(-2)))));

        var value = await ValueOf(insights, DashboardScope.Default);

        // Not an empty read: something was waiting, and nothing was producing.
        Assert.Equal(TimeSpan.FromHours(1), value.Waiting);
        Assert.Null(value.MostSessionsAtOnce);
    }

    /// <summary>The fourth and last sweep, over the subagents' stretches alone. They are
    /// never merged with the sessions' and never compared to them.</summary>
    [Fact]
    public async Task The_most_agents_at_once_is_the_peak_of_the_subagents()
    {
        var ran = (Now.AddHours(-3), Now.AddHours(-2));

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(
                    Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-2), "one"),
                    Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-2), "two"))
            },
            Activity(
                [Ran("one", Tower, "Claude", ran), Ran("two", Tower, "Claude", ran)],
                [Spawned("a1", "one", Tower, ran), Spawned("a2", "two", Tower, ran)]));

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(2, value.MostAgentsAtOnce!.Peak);
    }

    /// <summary>
    /// The agent id is what makes two overlapping stretches two agents, not the session
    /// they were spawned from. One session holding five at once is the ordinary case, and
    /// counting by session would report it as one.
    /// </summary>
    [Fact]
    public async Task Two_subagents_of_one_session_running_together_are_two_at_once()
    {
        var ran = (Now.AddHours(-3), Now.AddHours(-2));

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-2), "one"))
            },
            Activity(
                [Ran("one", Tower, "Claude", ran)],
                [Spawned("a1", "one", Tower, ran), Spawned("a2", "one", Tower, ran)]));

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(2, value.MostAgentsAtOnce!.Peak);
    }

    /// <summary>
    /// The two populations are counted apart. A subagent always overlaps the session that
    /// spawned it — that is what spawning means — so one sweep over both would report two
    /// of something that never existed.
    /// </summary>
    [Fact]
    public async Task A_subagent_and_the_session_that_spawned_it_are_one_of_each_rather_than_two_of_either()
    {
        var ran = (Now.AddHours(-3), Now.AddHours(-2));

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-2), "one"))
            },
            Activity([Ran("one", Tower, "Claude", ran)], [Spawned("a1", "one", Tower, ran)]));

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(1, value.MostSessionsAtOnce!.Peak);
        Assert.Equal(1, value.MostAgentsAtOnce!.Peak);
    }

    /// <inheritdoc cref="The_most_sessions_at_once_names_the_earliest_hour_when_two_hours_tie"/>
    [Fact]
    public async Task The_most_agents_at_once_names_the_earliest_hour_when_two_hours_tie()
    {
        var early = (Now.AddHours(-6), Now.AddHours(-5));
        var late = (Now.AddHours(-3), Now.AddHours(-2));

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Claude", Now.AddHours(-6), Now.AddHours(-2), "one"))
            },
            Activity(
                [Ran("one", Tower, "Claude", early, late)],
                [Spawned("a1", "one", Tower, early, late), Spawned("a2", "one", Tower, early, late)]));

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(2, value.MostAgentsAtOnce!.Peak);
        Assert.Equal(Now.AddHours(-6).Hour, value.MostAgentsAtOnce!.Hour);
    }

    /// <inheritdoc cref="The_most_sessions_at_once_widens_with_the_period"/>
    [Fact]
    public async Task The_most_agents_at_once_widens_with_the_period()
    {
        var recent = (Now.AddHours(-3), Now.AddHours(-2));
        var older = (Now.AddDays(-42), Now.AddDays(-42).AddHours(1));

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Claude", Now.AddDays(-42), Now.AddHours(-2), "one"))
            },
            Activity(
                [Ran("one", Tower, "Claude", recent, older)],
                [
                    Spawned("a1", "one", Tower, recent, older),
                    Spawned("a2", "one", Tower, recent, older),
                    Spawned("a3", "one", Tower, older)
                ]));

        var four = await ValueOf(insights, new DashboardScope(Period: DashboardPeriod.FourWeeks));
        var twelve = await ValueOf(insights, new DashboardScope(Period: DashboardPeriod.TwelveWeeks));

        Assert.Equal(2, four.MostAgentsAtOnce!.Peak);
        Assert.Equal(3, twelve.MostAgentsAtOnce!.Peak);
    }

    /// <summary>A period nobody spawned one in shows no peak rather than a zero, exactly
    /// as the sessions figure does.</summary>
    [Fact]
    public async Task There_is_no_most_agents_at_once_when_no_session_spawned_one()
    {
        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-2), "one"))
            },
            Activity(Ran("one", Tower, "Claude", (Now.AddHours(-3), Now.AddHours(-2)))));

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.NotNull(value.MostSessionsAtOnce);
        Assert.Null(value.MostAgentsAtOnce);
    }

    /// <summary>The machine filter drives these figures exactly as it drives the session
    /// ones, which is the whole reason the machine id is carried on a subagent at
    /// all.</summary>
    [Fact]
    public async Task Subagents_of_a_machine_the_reader_filtered_out_are_not_counted()
    {
        var ran = (Now.AddHours(-3), Now.AddHours(-2));

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(
                    Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-2), "one"),
                    Session(Laptop, "Claude", Now.AddHours(-3), Now.AddHours(-2), "two"))
            },
            Activity(
                [Ran("one", Tower, "Claude", ran), Ran("two", Laptop, "Claude", ran)],
                [
                    Spawned("a1", "one", Tower, ran),
                    Spawned("a2", "one", Tower, ran),
                    Spawned("a3", "one", Tower, ran),
                    Spawned("b1", "two", Laptop, ran)
                ]));

        var value = await ValueOf(insights, DashboardScope.Default with { MachineId = Laptop });

        Assert.Equal(1, value.MostAgentsAtOnce!.Peak);
    }

    /// <summary>The agents' figure is a third number in the same 168 cells, dated the same
    /// way and outlined by the same working-hours answer — never a parallel record with a
    /// second list of days in it.</summary>
    [Fact]
    public async Task Peak_agents_by_hour_fills_every_cell_of_the_last_seven_days()
    {
        var ran = (Now.AddHours(-3), Now.AddHours(-2));

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-2), "one"))
            },
            Activity(
                [Ran("one", Tower, "Claude", ran)],
                [Spawned("a1", "one", Tower, ran), Spawned("a2", "one", Tower, ran)]));

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(168, value.ActivityByHour.Count);

        var worked = value.ActivityByHour.Single(hour =>
            hour.Day == DateOnly.FromDateTime(Now.UtcDateTime) && hour.Hour == Now.AddHours(-3).Hour);

        Assert.Equal(2, worked.PeakAgents);

        // Not a share of the sessions figure and not bounded by it: one session held both
        // of them, so the two are freely in any ratio.
        Assert.Equal(1, worked.PeakSessions);
    }

    /// <summary>An hour that happened and had no agent in it is a zero, never a gap — the
    /// rule the cells beside it are already drawn under.</summary>
    [Fact]
    public async Task Peak_agents_by_hour_is_zero_in_an_hour_no_agent_ran()
    {
        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-1), "one"))
            },
            Activity(
                [Ran("one", Tower, "Claude", (Now.AddHours(-3), Now.AddHours(-1)))],
                [Spawned("a1", "one", Tower, (Now.AddHours(-3), Now.AddHours(-2)))]));

        var value = await ValueOf(insights, DashboardScope.Default);

        var quiet = value.ActivityByHour.Single(hour =>
            hour.Day == DateOnly.FromDateTime(Now.UtcDateTime) && hour.Hour == Now.AddHours(-2).Hour);

        // The session was still producing through that hour, so this is an hour with a
        // reading rather than an hour outside the grid.
        Assert.Equal(1, quiet.PeakSessions);
        Assert.Equal(0, quiet.PeakAgents);
    }

    /// <summary>The third grid is the same seven dated days as the other two and refuses
    /// the period control for the same reason: eighty-four dated rows is not a grid.</summary>
    [Fact]
    public async Task Peak_agents_by_hour_stays_seven_days_when_the_period_widens()
    {
        var ran = (Now.AddHours(-3), Now.AddHours(-2));

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Claude", Now.AddDays(-42), Now.AddHours(-2), "one"))
            },
            Activity(
                [Ran("one", Tower, "Claude", ran)],
                [Spawned("a1", "one", Tower, ran)]));

        var four = await ValueOf(insights, new DashboardScope(Period: DashboardPeriod.FourWeeks));
        var twelve = await ValueOf(insights, new DashboardScope(Period: DashboardPeriod.TwelveWeeks));

        Assert.Equal(168, four.ActivityByHour.Count);
        Assert.Equal(
            four.ActivityByHour.Select(hour => (hour.Day, hour.Hour, hour.PeakAgents)),
            twelve.ActivityByHour.Select(hour => (hour.Day, hour.Hour, hour.PeakAgents)));
    }

    /// <summary>The agents' figure travels in the same cells as the session ones, so it is
    /// drawn or not drawn with them and never on an axis the part has declined.</summary>
    [Fact]
    public async Task Peak_agents_by_hour_is_empty_when_the_session_grids_are_empty()
    {
        var ran = (Now.AddHours(-3), Now.AddHours(-2));

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-2), "one"))
            },
            Activity([], [Spawned("a1", "one", Tower, ran)]));

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Empty(value.ActivityByHour);
    }

    /// <summary>
    /// The emptiness gate is deliberately not widened to include the agents. A subagent
    /// exists because a session was producing, so a profile with agents and no session
    /// activity is not a thing that happens — and widening the gate would let an agent
    /// grid appear beside two grids the part declined to draw. The tile still reports,
    /// because a tile is not an axis.
    /// </summary>
    [Fact]
    public async Task Agents_alone_do_not_bring_a_grid_into_existence()
    {
        var ran = (Now.AddHours(-3), Now.AddHours(-2));

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-2), "one"))
            },
            Activity([], [Spawned("a1", "one", Tower, ran), Spawned("a2", "one", Tower, ran)]));

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Empty(value.ActivityByHour);
        Assert.Empty(value.ActivityByDay);
        Assert.Equal(2, value.MostAgentsAtOnce!.Peak);
    }

    [Fact]
    public async Task Subagents_add_nothing_to_the_session_count()
    {
        var value = await ValueOf(WithSpawnedAgents(), DashboardScope.Default);

        Assert.Equal(1, value.Sessions);
    }

    [Fact]
    public async Task Subagents_add_nothing_to_the_agent_active_time()
    {
        var value = await ValueOf(WithSpawnedAgents(), DashboardScope.Default);

        Assert.Equal(TimeSpan.FromHours(2), value.ActiveTime);
    }

    [Fact]
    public async Task Subagents_add_nothing_to_the_waiting_time()
    {
        var value = await ValueOf(WithSpawnedAgents(), DashboardScope.Default);

        Assert.Equal(TimeSpan.FromHours(1), value.Waiting);
    }

    [Fact]
    public async Task Subagents_add_nothing_to_the_last_activity()
    {
        var value = await ValueOf(WithSpawnedAgents(), DashboardScope.Default);

        Assert.Equal(Now.AddHours(-1), value.LastActivityAt);
    }

    [Fact]
    public async Task Subagents_add_nothing_to_the_sessions_per_week()
    {
        var value = await ValueOf(WithSpawnedAgents(), DashboardScope.Default);

        Assert.Equal(1, value.SessionsPerWeek.Sum(point => point.Value));
    }

    [Fact]
    public async Task Subagents_add_nothing_to_the_peak_sessions_in_an_hour()
    {
        var value = await ValueOf(WithSpawnedAgents(), DashboardScope.Default);

        Assert.All(value.ActivityByHour, hour => Assert.InRange(hour.PeakSessions, 0, 1));

        // And the agents really were there to be miscounted.
        Assert.Equal(3, value.ActivityByHour.Max(hour => hour.PeakAgents));
    }

    [Fact]
    public async Task Subagents_add_nothing_to_the_open_sessions_in_an_hour()
    {
        var value = await ValueOf(WithSpawnedAgents(), DashboardScope.Default);

        Assert.All(value.ActivityByHour, hour => Assert.InRange(hour.OpenSessions, 0, 1));
    }

    [Fact]
    public async Task Subagents_add_nothing_to_the_day_counts()
    {
        var value = await ValueOf(WithSpawnedAgents(), DashboardScope.Default);

        var today = value.ActivityByDay.Single(day => day.Day == DateOnly.FromDateTime(Now.UtcDateTime));

        Assert.Equal(1, today.Sessions);
    }

    [Fact]
    public async Task Subagents_add_nothing_to_the_breakdown()
    {
        var value = await ValueOf(WithSpawnedAgents(), DashboardScope.Default);

        var row = Assert.Single(value.Breakdown);

        Assert.Equal(1, row.Sessions);
        Assert.Equal(TimeSpan.FromHours(2), row.ActiveTime);
        Assert.Equal(TimeSpan.FromHours(1), row.Waiting);
    }

    [Fact]
    public async Task A_subagent_is_never_counted_as_a_session_without_activity()
    {
        var value = await ValueOf(WithSpawnedAgents(), DashboardScope.Default);

        Assert.Equal(0, value.WithoutActivity);
    }

    /// <summary>
    /// One session, and three agents it spawned running right through the stretch it
    /// produced in. The fixture the ten hard-constraint facts above are all asserted
    /// against, and the numbers are chosen so that folding the agents into the sessions
    /// moves every one of them: the count would read four, the agent-active time eight
    /// hours, and the peak and open counts four apiece.
    /// <para>
    /// Ten facts rather than one assertion over the whole record, deliberately. A single
    /// whole-insight comparison goes green the day somebody adds a member to the record,
    /// and the constraint these hold up is the one the feature was approved on.
    /// </para>
    /// </summary>
    private static SessionInsights WithSpawnedAgents()
    {
        var ran = (Now.AddHours(-3), Now.AddHours(-1));
        var waited = (Now.AddHours(-1), Now);

        return Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-1), "one"))
            },
            Activity(
                [RanAndWaited("one", Tower, "Claude", [ran], [waited])],
                [
                    Spawned("a1", "one", Tower, ran),
                    Spawned("a2", "one", Tower, ran),
                    Spawned("a3", "one", Tower, ran)
                ]));
    }

    private static async Task<AssistantSessionsInsight> ValueOf(ISessionInsights insights, DashboardScope scope)
    {
        var result = await insights.GetSessionsAsync(scope);

        Assert.True(result.HasValue);

        return result.Value!;
    }

    /// <summary>
    /// The one hour of the grid anything landed in. Written as a search rather than an
    /// index so a test that put its run in the wrong cell fails on the assertion it meant
    /// rather than on arithmetic in the test itself.
    /// </summary>
    private static ActivityHour Worked(AssistantSessionsInsight value) =>
        Assert.Single(value.ActivityByHour.Where(hour => hour.Active > TimeSpan.Zero));

    private static AssistantSessionRow Row(AssistantSessionsInsight value, string name) =>
        Assert.Single(value.Breakdown.Where(row => row.Name == name));

    /// <summary>
    /// A fixed two-hour zone rather than a real one, for the reason
    /// <c>LocalHourBucketsTests</c> gives: a real id differs on Linux and brings a
    /// daylight-saving rule that would make these assertions depend on the date.
    /// </summary>
    private static readonly TimeZoneInfo PlusTwo =
        TimeZoneInfo.CreateCustomTimeZone("test-plus-two", TimeSpan.FromHours(2), "+02", "+02");

    /// <summary>
    /// One factory so the second source and the zone are one edit rather than twenty-five.
    /// A quiet activity source by default, because most facts here are about the session
    /// half and a fixture that had to describe both would bury which half it was testing.
    /// </summary>
    private static SessionInsights Insights(
        IAssistantSessionSource source,
        IAssistantActivitySource? activity = null,
        TimeZoneInfo? zone = null,
        WorkingHours? week = null) =>
        new(
            source,
            activity ?? new StubAssistantActivitySource(),
            new FixedWorkingHours(week ?? WorkingHours.Default),
            new FixedClock(Now, zone));

    /// <summary>
    /// A working week that does not come off disk.
    /// <para>
    /// The default unless a fact is about the split, because the alternative — reading the
    /// store — would make every assertion about an in-hours count depend on what the
    /// person running the tests had set their own hours to.
    /// </para>
    /// </summary>
    private sealed class FixedWorkingHours(WorkingHours week) : IWorkingHoursSettings
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public WorkingHours Current => week;

        public string SettingsPath => "working-hours.json";

        public string? SetDay(DayOfWeek day, bool working, TimeOnly start, TimeOnly end) => null;

        public string? ResetToDefault() => null;
    }

    private static AssistantSessionReport Report(params AssistantSession[] sessions) =>
        new(sessions, [], false, 100);

    private static StubAssistantActivitySource Activity(params AssistantActivitySession[] sessions) =>
        new() { Report = new AssistantActivityReport(sessions, [], Now.AddDays(-7 * 12), TimeSpan.FromMinutes(5)) };

    /// <summary>A report carrying both lists. The positional builder above stays untouched
    /// and the agents arrive through the init property, exactly as they do out of the
    /// seam.</summary>
    private static StubAssistantActivitySource Activity(
        AssistantActivitySession[] sessions,
        AssistantActivitySubagent[] subagents) =>
        new()
        {
            Report = new AssistantActivityReport(sessions, [], Now.AddDays(-7 * 12), TimeSpan.FromMinutes(5))
            {
                Subagents = subagents
            }
        };

    /// <summary>One agent a session spawned, and when it was producing. Always Claude:
    /// Copilot spawns none, and a fixture that pretended otherwise would be describing a
    /// profile nobody has.</summary>
    private static AssistantActivitySubagent Spawned(
        string agentId,
        string sessionId,
        string machineId,
        params (DateTimeOffset From, DateTimeOffset To)[] active) =>
        new(agentId, sessionId, machineId, MachineName(machineId), "Claude", [.. active.Select(Interval)]);

    /// <summary>One session that produced, and when.</summary>
    private static AssistantActivitySession Ran(
        string id,
        string machineId,
        string assistant,
        params (DateTimeOffset From, DateTimeOffset To)[] active) =>
        new(id, machineId, MachineName(machineId), assistant, [.. active.Select(Interval)], []);

    /// <summary>One session that did both, which is what a real one does: a wait is the
    /// gap between two runs, so the two lists together are the session's whole record and
    /// neither alone shows the difference between producing and being on the go.</summary>
    private static AssistantActivitySession RanAndWaited(
        string id,
        string machineId,
        string assistant,
        (DateTimeOffset From, DateTimeOffset To)[] active,
        (DateTimeOffset From, DateTimeOffset To)[] waiting) =>
        new(
            id,
            machineId,
            MachineName(machineId),
            assistant,
            [.. active.Select(Interval)],
            [.. waiting.Select(Interval)]);

    /// <summary>One session that spent the given stretches waiting on a person.</summary>
    private static AssistantActivitySession Waited(
        string id,
        string machineId,
        string assistant,
        params (DateTimeOffset From, DateTimeOffset To)[] waiting) =>
        new(id, machineId, MachineName(machineId), assistant, [], [.. waiting.Select(Interval)]);

    private static AssistantActivityInterval Interval((DateTimeOffset From, DateTimeOffset To) span) =>
        new(span.From, span.To);

    private static AssistantSession Session(
        string machineId,
        string assistant,
        DateTimeOffset? startedAt,
        DateTimeOffset lastActivityAt,
        string id = "") =>
        Session(machineId, MachineName(machineId), assistant, startedAt, lastActivityAt, id);

    private static AssistantSession Session(
        string machineId,
        string machineName,
        string assistant,
        DateTimeOffset? startedAt,
        DateTimeOffset lastActivityAt,
        string id = "") =>
        new(machineId, machineName, assistant, startedAt, lastActivityAt) { Id = id };

    private static string MachineName(string machineId) => machineId == Tower ? "DEV-TOWER" : "DEV-LAPTOP";

    private sealed class StubAssistantSessionSource : IAssistantSessionSource
    {
        public InsightAvailability Availability { get; init; } = InsightAvailability.Available;

        public AssistantSessionReport Report { get; init; } = AssistantSessionReport.Empty;

        public Exception? Throw { get; init; }

        public int Calls { get; private set; }

        public int AvailabilityCalls { get; private set; }

        public Task<InsightAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
        {
            AvailabilityCalls++;

            return Throw is not null ? Task.FromException<InsightAvailability>(Throw) : Task.FromResult(Availability);
        }

        public Task<AssistantSessionReport> GetSessionsAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Report);
        }
    }

    /// <summary>
    /// The expensive half, stubbed. It records the horizons it was asked for, which is
    /// how the "one read serves every period" claim is asserted rather than described.
    /// </summary>
    private sealed class StubAssistantActivitySource : IAssistantActivitySource
    {
        public AssistantActivityReport Report { get; init; } = AssistantActivityReport.Empty;

        public Exception? Throw { get; init; }

        public int Calls { get; private set; }

        public List<DateTimeOffset> Horizons { get; } = [];

        public Task<AssistantActivityReport> GetActivityAsync(
            DateTimeOffset since,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Horizons.Add(since);

            return Throw is not null
                ? Task.FromException<AssistantActivityReport>(Throw)
                : Task.FromResult(Report);
        }
    }

    /// <summary>A clock that does not move, so a window is the same window on every
    /// machine and on every run.</summary>
    private sealed class FixedClock(DateTimeOffset now, TimeZoneInfo? zone = null) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        /// <summary>The grid is the only figure on this surface drawn in local hours, so
        /// it is the only one whose test has to say which local. Fixed rather than the
        /// machine's, because a grid asserted against whatever zone CI happens to run in
        /// is a grid asserted against nothing.</summary>
        public override TimeZoneInfo LocalTimeZone => zone ?? TimeZoneInfo.Utc;
    }
}
