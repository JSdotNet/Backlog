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

    /// <summary>
    /// The stuck-loading race, at this seam. The first read of a profile parses every
    /// transcript and takes long enough for a reader to move the machine filter while
    /// it runs. The part cancels its first fetch and asks again; the read is one shared
    /// entry, so if it ran under the first fetch's token the second would inherit a
    /// cancellation it never asked for, swallow it as its own, and leave the part on
    /// Loading with nothing left to wake it.
    /// </summary>
    [Fact]
    public async Task Moving_a_filter_while_the_first_read_is_in_flight_still_answers_the_second_scope()
    {
        var gate = new TaskCompletionSource();
        var source = new StubAssistantSessionSource
        {
            Report = Report(Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-1), "one")),
            Gate = gate
        };
        var insights = Insights(source);

        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();

        var everyMachine = insights.GetSessionsAsync(DashboardScope.Default, first.Token);
        var oneMachine = insights.GetSessionsAsync(DashboardScope.Default with { MachineId = Tower }, second.Token);

        await first.CancelAsync();
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => everyMachine);

        gate.SetResult();
        var result = await oneMachine;

        Assert.True(result.HasValue, result.Availability.Reason);
        Assert.Equal(1, result.Value!.Sessions);
        Assert.Equal(1, source.Calls);
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
        var focused = await ValueOf(insights, DashboardScope.Default with { Repositories = RepositoryFocus.Of("backlog") });

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

    /// <summary>
    /// The mean is over the sessions that carry a count and nothing else. A Copilot
    /// session records no prompt count, and averaging its null in as zero would report a
    /// figure half the size of the one the counted transcripts actually support — which
    /// is why the denominator travels beside the mean.
    /// </summary>
    [Fact]
    public async Task Prompts_per_session_averages_the_counted_sessions_and_skips_the_rest()
    {
        var insights = Insights(new StubAssistantSessionSource
        {
            Report = Report(
                Session(Tower, "Claude", Now.AddHours(-4), Now.AddHours(-3)) with { Prompts = 12 },
                Session(Tower, "Claude", Now.AddHours(-2), Now.AddHours(-1)) with { Prompts = 3 },
                Session(Tower, "Copilot", Now.AddHours(-2), Now.AddHours(-1)))
        });

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(3, value.Sessions);
        Assert.Equal(2, value.SessionsWithPrompts);
        Assert.Equal(7.5m, value.PromptsPerSession);
    }

    /// <summary>
    /// Nothing to average is null, not zero. Zero would say a person opened sessions and
    /// never spoke in them; the only thing a window of uncounted sessions supports is
    /// that there was nothing to count from.
    /// </summary>
    [Fact]
    public async Task Prompts_per_session_is_absent_when_no_session_carries_a_count()
    {
        var insights = Insights(new StubAssistantSessionSource
        {
            Report = Report(Session(Tower, "Copilot", Now.AddHours(-2), Now.AddHours(-1)))
        });

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(1, value.Sessions);
        Assert.Equal(0, value.SessionsWithPrompts);
        Assert.Null(value.PromptsPerSession);
    }

    /// <summary>
    /// Bucketed on the same instant the sessions series is, so the two charts share an
    /// axis and a column in one is the same sessions as the column beside it in the
    /// other. A week with no counted session is a zero point, so the axis keeps its
    /// shape; a week with an uncounted session in it is still a zero, because that
    /// session contributes no prompts and no denominator.
    /// </summary>
    [Fact]
    public async Task Prompts_per_session_is_averaged_within_the_week_a_session_last_moved()
    {
        var scope = DashboardScope.Default;

        var insights = Insights(new StubAssistantSessionSource
        {
            Report = Report(
                // Two counted in the week before Now's: 10 and 4 average to 7.
                Session(Tower, "Claude", Now.AddDays(-8), Now.AddDays(-8)) with { Prompts = 10 },
                Session(Tower, "Claude", Now.AddDays(-7), Now.AddDays(-7)) with { Prompts = 4 },
                // Only Copilot two weeks back: a zero, not a gap.
                Session(Tower, "Copilot", Now.AddDays(-15), Now.AddDays(-15)),
                // One counted in Now's own week.
                Session(Tower, "Claude", Now.AddHours(-2), Now.AddHours(-1)) with { Prompts = 5 })
        });

        var value = await ValueOf(insights, scope);

        Assert.Equal(scope.Weeks + 1, value.PromptsPerSessionPerWeek.Count);
        Assert.Equal([0m, 7m, 5m], value.PromptsPerSessionPerWeek.TakeLast(3).Select(point => point.Value));
        Assert.Equal(
            value.SessionsPerWeek.Select(point => point.Label),
            value.PromptsPerSessionPerWeek.Select(point => point.Label));
    }

    /// <summary>The machine filter narrows the mean the way it narrows every other
    /// figure on the part: a session on the other machine is not in the denominator.</summary>
    [Fact]
    public async Task Prompts_per_session_follows_the_machine_filter()
    {
        var insights = Insights(new StubAssistantSessionSource
        {
            Report = Report(
                Session(Tower, "Claude", Now.AddHours(-2), Now.AddHours(-1)) with { Prompts = 2 },
                Session(Laptop, "Claude", Now.AddHours(-2), Now.AddHours(-1)) with { Prompts = 8 })
        });

        var everywhere = await ValueOf(insights, DashboardScope.Default);
        var focused = await ValueOf(insights, DashboardScope.Default with { MachineId = Laptop });

        Assert.Equal(5m, everywhere.PromptsPerSession);
        Assert.Equal(8m, focused.PromptsPerSession);
        Assert.Equal(1, focused.SessionsWithPrompts);
        Assert.Equal(8m, focused.PromptsPerSessionPerWeek[^1].Value);
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

        // The Sunday is the last row of the week before Now's, so it is on that week's
        // grid rather than the latest one.
        var day = value.Grids.SelectMany(grid => grid.Days).Single(entry => entry.Day == new DateOnly(2026, 8, 16));

        Assert.Equal(0, day.SessionsInWorkingHours);
        Assert.Equal(1, day.SessionsOutsideWorkingHours);

        Assert.All(
            value.Grids.SelectMany(grid => grid.Hours).Where(hour => hour.Day == new DateOnly(2026, 8, 16)),
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

    // --- The four weekly series read out of the sweeps ------------------------------

    /// <summary>
    /// The agent-hours land in the week they were worked, and the columns add up to the
    /// tile. Two hours in the ISO week before Now's and one in Now's own, so a series
    /// that bucketed on the session instead of on the hours would read [0, 3] here.
    /// </summary>
    [Fact]
    public async Task Active_time_is_bucketed_into_the_week_it_was_worked_and_sums_to_the_tile()
    {
        var earlier = (Now.AddDays(-8), Now.AddDays(-8).AddHours(2));
        var recent = (Now.AddHours(-2), Now.AddHours(-1));

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(
                    Session(Tower, "Claude", earlier.Item1, earlier.Item2, "one"),
                    Session(Tower, "Claude", recent.Item1, recent.Item2, "two"))
            },
            Activity(Ran("one", Tower, "Claude", earlier), Ran("two", Tower, "Claude", recent)));

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal([2m, 1m], value.ActiveTimePerWeek.TakeLast(2).Select(point => point.Value));
        Assert.Equal(["W33", "W34"], value.ActiveTimePerWeek.TakeLast(2).Select(point => point.Label));

        // The same buckets as the sessions series, so the two charts share an axis.
        Assert.Equal(value.SessionsPerWeek.Select(point => point.Label), value.ActiveTimePerWeek.Select(point => point.Label));

        // And the columns are the tile, cut by week.
        Assert.Equal((decimal)value.ActiveTime.TotalHours, value.ActiveTimePerWeek.Sum(point => point.Value));
    }

    /// <summary>
    /// <b>The difference from the sessions series, and the sentence the part owes.</b> A
    /// session is one mark in the week it last moved; its hours are wherever they were.
    /// One run across the Sunday–Monday boundary is one session in W34 and an hour of
    /// work in each of W33 and W34.
    /// </summary>
    [Fact]
    public async Task A_run_across_a_week_boundary_puts_its_hours_in_both_weeks_and_its_session_in_one()
    {
        // 2026-08-16 is the Sunday W33 ends on; the run straddles midnight into W34.
        var boundary = new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.Zero);
        var ran = (boundary.AddHours(-1), boundary.AddHours(1));

        var insights = Insights(
            new StubAssistantSessionSource { Report = Report(Session(Tower, "Claude", ran.Item1, ran.Item2, "one")) },
            Activity(Ran("one", Tower, "Claude", ran)));

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal([0m, 1m], value.SessionsPerWeek.TakeLast(2).Select(point => point.Value));
        Assert.Equal([1m, 1m], value.ActiveTimePerWeek.TakeLast(2).Select(point => point.Value));
    }

    /// <summary>
    /// Waiting is cut the same way and adds up to its own tile. A wait is the gap between
    /// two runs, so a session that ran, waited a week and ran again puts the whole gap in
    /// the weeks it spanned rather than in the week the prompt finally arrived.
    /// </summary>
    [Fact]
    public async Task Waiting_is_bucketed_into_the_week_it_was_waited_and_sums_to_the_tile()
    {
        var waited = (Now.AddDays(-8), Now.AddDays(-8).AddHours(3));
        var recent = (Now.AddHours(-2), Now.AddHours(-1));

        var insights = Insights(
            new StubAssistantSessionSource { Report = Report(Session(Tower, "Claude", waited.Item1, recent.Item2, "one")) },
            Activity(RanAndWaited("one", Tower, "Claude", [recent], [waited])));

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal([3m, 0m], value.WaitingPerWeek.TakeLast(2).Select(point => point.Value));
        Assert.Equal((decimal)value.Waiting.TotalHours, value.WaitingPerWeek.Sum(point => point.Value));
    }

    /// <summary>
    /// The peaks are a maximum per week rather than a sum: two sessions running together
    /// last week and one alone this week is [2, 1], and a week nobody worked is a zero
    /// point rather than a gap, on the sessions series' rule.
    /// </summary>
    [Fact]
    public async Task The_most_sessions_at_once_is_read_per_week_as_a_peak_not_a_sum()
    {
        var together = (Now.AddDays(-8), Now.AddDays(-8).AddHours(1));
        var alone = (Now.AddHours(-2), Now.AddHours(-1));

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(
                    Session(Tower, "Claude", together.Item1, together.Item2, "one"),
                    Session(Tower, "Claude", together.Item1, together.Item2, "two"),
                    Session(Tower, "Claude", alone.Item1, alone.Item2, "three"))
            },
            Activity(
                Ran("one", Tower, "Claude", together),
                Ran("two", Tower, "Claude", together),
                Ran("three", Tower, "Claude", alone)));

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal([2m, 1m], value.MostSessionsAtOncePerWeek.TakeLast(2).Select(point => point.Value));
        Assert.Equal(0m, value.MostSessionsAtOncePerWeek[0].Value);

        // And the tile is the highest column, never more and never less.
        Assert.Equal((decimal)value.MostSessionsAtOnce!.Peak, value.MostSessionsAtOncePerWeek.Max(point => point.Value));
    }

    /// <summary>The agents' peak per week comes off the agents' own sweep, so a week with
    /// three sessions and no spawned agent reads zero here and three on the series above.</summary>
    [Fact]
    public async Task The_most_agents_at_once_is_read_per_week_from_the_agents_sweep()
    {
        var value = await ValueOf(WithSpawnedAgents(), DashboardScope.Default);

        Assert.Equal(3m, value.MostAgentsAtOncePerWeek[^1].Value);
        Assert.Equal(1m, value.MostSessionsAtOncePerWeek[^1].Value);
        Assert.Equal((decimal)value.MostAgentsAtOnce!.Peak, value.MostAgentsAtOncePerWeek.Max(point => point.Value));

        // Empty when there is no axis, on the sessions series' rule.
        Assert.Equal(value.SessionsPerWeek.Count, value.MostAgentsAtOncePerWeek.Count);
    }

    /// <summary>
    /// The weeks are cut on the local clock, the way the grid is and the way a reset is
    /// read off a usage screen. An hour worked at 22:30 UTC on a Sunday is that Sunday's
    /// week on a UTC clock and the next week's two hours east, where it is already
    /// Monday — and the columns and the grid move together, which is the point of one
    /// cut for both.
    /// </summary>
    [Fact]
    public async Task The_week_columns_are_cut_on_the_local_clock_like_the_grid()
    {
        // 22:30–23:30 UTC on Sunday 16 August: local Monday 00:30–01:30 two hours east.
        var sunday = new DateTimeOffset(2026, 8, 16, 22, 30, 0, TimeSpan.Zero);
        var ran = (sunday, sunday.AddHours(1));

        var sessions = new StubAssistantSessionSource { Report = Report(Session(Tower, "Claude", ran.Item1, ran.Item2, "one")) };

        var utc = await ValueOf(Insights(sessions, Activity(Ran("one", Tower, "Claude", ran))), DashboardScope.Default);
        var east = await ValueOf(Insights(sessions, Activity(Ran("one", Tower, "Claude", ran)), PlusTwo), DashboardScope.Default);

        Assert.Equal([1m, 0m], utc.ActiveTimePerWeek.TakeLast(2).Select(point => point.Value));
        Assert.Equal([0m, 1m], east.ActiveTimePerWeek.TakeLast(2).Select(point => point.Value));

        // And under the calendar fallback the columns still say which ISO week they are.
        Assert.Equal(WeekSource.Calendar, east.Week.Source);
        Assert.Equal("W34", east.ActiveTimePerWeek[^1].Label);
    }

    // --- The same series cut by repository ------------------------------------------

    /// <summary>
    /// A row per band: a configured repository by its alias, the unconfigured ones
    /// folded into one, and one for the sessions that recorded none — which is every
    /// Claude session, and the row a reader meets first. The rows' hours add up to the
    /// total column for column; dropping the unrecorded row would turn a chart of all
    /// sessions into a chart of Copilot's.
    /// </summary>
    [Fact]
    public async Task Hours_are_cut_by_repository_band_with_the_configured_alias_as_the_name()
    {
        var ran = (Now.AddHours(-3), Now.AddHours(-1));

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(
                    Session(Tower, "Copilot", ran.Item1, ran.Item2, "one") with { Repository = "acme/backlog" },
                    Session(Tower, "Copilot", ran.Item1, ran.Item2, "two") with { Repository = "acme/unknown" },
                    Session(Tower, "Claude", ran.Item1, ran.Item2, "three"))
            },
            Activity(
                Ran("one", Tower, "Copilot", ran),
                Ran("two", Tower, "Copilot", (ran.Item1, ran.Item1.AddHours(1))),
                Ran("three", Tower, "Claude", ran)));

        var value = await ValueOf(insights, DashboardScope.Default);

        // Producing time descending, then name. The alias, not owner/name, so the row
        // wears the word the header's chips do; the folded rows are named for the fact.
        Assert.Equal(
            ["No repository recorded", "backlog", "Other repositories"],
            value.ByRepository.Select(row => row.Name));
        Assert.Equal(
            [RepositoryBandKind.Unrecorded, RepositoryBandKind.Configured, RepositoryBandKind.Other],
            value.ByRepository.Select(row => row.Kind));

        Assert.Equal(2m, value.ByRepository[0].ActiveTimePerWeek[^1].Value);
        Assert.Equal(2m, value.ByRepository[1].ActiveTimePerWeek[^1].Value);
        Assert.Equal(1m, value.ByRepository[2].ActiveTimePerWeek[^1].Value);

        // Same buckets as the totals, and the rows add up to them.
        Assert.All(value.ByRepository, row => Assert.Equal(value.SessionsPerWeek.Count, row.ActiveTimePerWeek.Count));
        Assert.Equal(
            value.ActiveTimePerWeek.Select(point => point.Value),
            Enumerable.Range(0, value.ActiveTimePerWeek.Count)
                .Select(week => value.ByRepository.Sum(row => row.ActiveTimePerWeek[week].Value)));
    }

    /// <summary>The sessions count and the prompt mean are cut per row on the same terms
    /// as the totals — the week the session last moved, the mean over counted sessions
    /// only — so the rows' session counts add up to the sessions series above.</summary>
    [Fact]
    public async Task Sessions_and_prompts_are_cut_per_repository_row_on_the_totals_terms()
    {
        var ran = (Now.AddHours(-3), Now.AddHours(-1));

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(
                    Session(Tower, "Copilot", ran.Item1, ran.Item2, "one") with { Repository = "acme/backlog" },
                    Session(Tower, "Copilot", ran.Item1, ran.Item2, "two") with { Repository = "acme/backlog" },
                    Session(Tower, "Claude", ran.Item1, ran.Item2, "three") with { Prompts = 6 },
                    Session(Tower, "Claude", ran.Item1, ran.Item2, "four") with { Prompts = 10 })
            },
            Activity(Ran("one", Tower, "Copilot", ran), Ran("three", Tower, "Claude", ran)));

        var value = await ValueOf(insights, DashboardScope.Default);

        var backlog = Assert.Single(value.ByRepository.Where(row => row.Name == "backlog"));
        var unrecorded = Assert.Single(value.ByRepository.Where(row => row.Kind == RepositoryBandKind.Unrecorded));

        Assert.Equal(2m, backlog.SessionsPerWeek[^1].Value);
        Assert.Equal(2m, unrecorded.SessionsPerWeek[^1].Value);
        Assert.Equal(value.SessionsPerWeek[^1].Value, value.ByRepository.Sum(row => row.SessionsPerWeek[^1].Value));

        // Copilot counts no prompts, so its row is zero — "nothing to count" drawn as
        // the totals draw it — and Claude's row is the mean over its two.
        Assert.Equal(0m, backlog.PromptsPerSessionPerWeek[^1].Value);
        Assert.Equal(8m, unrecorded.PromptsPerSessionPerWeek[^1].Value);
    }

    /// <summary>
    /// <b>The one thing on this insight that follows the repository scope.</b> With
    /// repositories in focus only their rows are built and the folded rows are out —
    /// the Sessions list's treatment of a session it cannot place — while the totals
    /// stay whole, because narrowing them would hide Claude's half.
    /// </summary>
    [Fact]
    public async Task The_repository_rows_follow_the_scope_and_the_totals_do_not()
    {
        var ran = (Now.AddHours(-3), Now.AddHours(-1));

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(
                    Session(Tower, "Copilot", ran.Item1, ran.Item2, "one") with { Repository = "acme/backlog" },
                    Session(Tower, "Copilot", ran.Item1, ran.Item2, "two") with { Repository = "acme/other" },
                    Session(Tower, "Copilot", ran.Item1, ran.Item2, "three") with { Repository = "acme/unknown" },
                    Session(Tower, "Claude", ran.Item1, ran.Item2, "four"))
            },
            Activity(
                Ran("one", Tower, "Copilot", ran),
                Ran("two", Tower, "Copilot", ran),
                Ran("three", Tower, "Copilot", ran),
                Ran("four", Tower, "Claude", ran)));

        var focused = await ValueOf(insights, new DashboardScope(Repositories: RepositoryFocus.Of("backlog")));

        Assert.Equal(["backlog"], focused.ByRepository.Select(row => row.Name));

        // The totals above are every session still.
        Assert.Equal(4m, focused.SessionsPerWeek[^1].Value);
        Assert.Equal(8m, focused.ActiveTimePerWeek[^1].Value);
    }

    /// <summary>
    /// A row's peak is the most at once in that row, and the rows' peaks do not add up
    /// to the total: two repositories with one session each running together is a
    /// total peak of two and a peak of one in each row.
    /// </summary>
    [Fact]
    public async Task A_repository_row_peak_is_its_own_and_the_rows_do_not_sum_to_the_total_peak()
    {
        var ran = (Now.AddHours(-3), Now.AddHours(-1));

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(
                    Session(Tower, "Copilot", ran.Item1, ran.Item2, "one") with { Repository = "acme/backlog" },
                    Session(Tower, "Copilot", ran.Item1, ran.Item2, "two") with { Repository = "acme/other" })
            },
            Activity(Ran("one", Tower, "Copilot", ran), Ran("two", Tower, "Copilot", ran)));

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(2m, value.MostSessionsAtOncePerWeek[^1].Value);
        Assert.All(value.ByRepository, row => Assert.Equal(1m, row.MostSessionsAtOncePerWeek[^1].Value));
    }

    /// <summary>A spawned agent is in the row of the session that spawned it, and the
    /// wait too — the whole record of a session sits in one row.</summary>
    [Fact]
    public async Task Agents_and_waits_follow_their_session_into_its_repository_row()
    {
        var ran = (Now.AddHours(-3), Now.AddHours(-2));
        var waited = (Now.AddHours(-2), Now.AddHours(-1));

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(Session(Tower, "Copilot", ran.Item1, waited.Item2, "one") with { Repository = "acme/backlog" })
            },
            Activity(
                [RanAndWaited("one", Tower, "Copilot", [ran], [waited])],
                [Spawned("a1", "one", Tower, ran), Spawned("a2", "one", Tower, ran)]));

        var value = await ValueOf(insights, DashboardScope.Default);

        var row = Assert.Single(value.ByRepository);

        Assert.Equal("backlog", row.Name);
        Assert.Equal(1m, row.WaitingPerWeek[^1].Value);
        Assert.Equal(2m, row.MostAgentsAtOncePerWeek[^1].Value);
    }

    /// <summary>A repository with sessions in the horizon and none in the window is not
    /// a row: a legend entry for a band of nothing tells a reader nothing.</summary>
    [Fact]
    public async Task A_repository_with_nothing_in_the_window_has_no_row()
    {
        var older = (Now.AddDays(-42), Now.AddDays(-42).AddHours(1));
        var recent = (Now.AddHours(-2), Now.AddHours(-1));

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(
                    Session(Tower, "Copilot", older.Item1, older.Item2, "old") with { Repository = "acme/other" },
                    Session(Tower, "Copilot", recent.Item1, recent.Item2, "new") with { Repository = "acme/backlog" })
            },
            Activity(Ran("old", Tower, "Copilot", older), Ran("new", Tower, "Copilot", recent)));

        var four = await ValueOf(insights, new DashboardScope(Period: DashboardPeriod.FourWeeks));
        var twelve = await ValueOf(insights, new DashboardScope(Period: DashboardPeriod.TwelveWeeks));

        Assert.Equal(["backlog"], four.ByRepository.Select(row => row.Name));
        Assert.Equal(["backlog", "other"], twelve.ByRepository.Select(row => row.Name).Order(StringComparer.Ordinal));
    }

    // --- The usage week -------------------------------------------------------------

    /// <summary>
    /// A configured reset cuts the weeks: with the reset at Monday 14:00 local, Now's
    /// week began Monday 17 August 14:00 UTC (the test clock is UTC), a run on Monday
    /// morning is the week before, and the columns are labelled by the day the week
    /// began rather than by ISO number.
    /// </summary>
    [Fact]
    public async Task A_configured_reset_cuts_the_weeks_and_names_them_by_their_first_day()
    {
        // Monday 17 August, 13:00–13:30 UTC: an hour before this week's reset.
        var before = new DateTimeOffset(2026, 8, 17, 13, 0, 0, TimeSpan.Zero);
        var after = new DateTimeOffset(2026, 8, 17, 15, 0, 0, TimeSpan.Zero);

        var insights = Insights(
            new StubAssistantSessionSource
            {
                Report = Report(
                    Session(Tower, "Claude", before, before.AddMinutes(30), "one"),
                    Session(Tower, "Claude", after, after.AddHours(1), "two"))
            },
            Activity(Ran("one", Tower, "Claude", (before, before.AddMinutes(30))), Ran("two", Tower, "Claude", (after, after.AddHours(1)))),
            reset: new FixedUsageReset(new UsageWeekReset(DayOfWeek.Monday, new TimeOnly(14, 0))));

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(WeekSource.Configured, value.Week.Source);
        Assert.Equal("Monday 14:00", value.Week.ResetAt);
        Assert.Equal(["10 Aug", "17 Aug"], value.ActiveTimePerWeek.TakeLast(2).Select(point => point.Label));
        Assert.Equal([0.5m, 1m], value.ActiveTimePerWeek.TakeLast(2).Select(point => point.Value));
        Assert.Equal([1m, 1m], value.SessionsPerWeek.TakeLast(2).Select(point => point.Value));
    }

    /// <summary>
    /// With nothing configured, the last weekly refusal's reset is the anchor — any
    /// reset instant cuts the same weeks, so one recorded in July anchors this week too.
    /// A refusal for the five-hour window is not a weekly reset and anchors nothing.
    /// </summary>
    [Fact]
    public async Task The_last_weekly_refusal_anchors_the_weeks_when_nothing_is_configured()
    {
        var ran = (Now.AddHours(-3), Now.AddHours(-1));

        // A weekly reset at Monday 21:00 UTC, four weeks back, and a five-hour refusal
        // since then whose reset is not a week boundary.
        var weeklyReset = new DateTimeOffset(2026, 7, 27, 21, 0, 0, TimeSpan.Zero);

        var insights = Insights(
            new StubAssistantSessionSource { Report = Report(Session(Tower, "Claude", ran.Item1, ran.Item2, "one")) },
            Activity(
                [Ran("one", Tower, "Claude", ran)],
                [
                    Refused("old", AssistantLimitKind.SevenDay, weeklyReset.AddDays(-1), weeklyReset),
                    Refused("one", AssistantLimitKind.FiveHour, Now.AddHours(-2), Now.AddHours(1))
                ]));

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(WeekSource.Detected, value.Week.Source);
        Assert.Equal("Monday 21:00", value.Week.ResetAt);

        // Now is Wednesday 19 August 09:00: the week began Monday 17 August 21:00.
        Assert.Equal("17 Aug", value.ActiveTimePerWeek[^1].Label);
        Assert.Equal(new DateTimeOffset(2026, 8, 17, 21, 0, 0, TimeSpan.Zero), value.Grids[^1].StartsAt);
    }

    /// <summary>A configured reset outranks a detected one: the records on a machine may
    /// be from another plan or another month, and the person said otherwise.</summary>
    [Fact]
    public async Task A_configured_reset_outranks_a_detected_one()
    {
        var ran = (Now.AddHours(-3), Now.AddHours(-1));
        var weeklyReset = new DateTimeOffset(2026, 7, 27, 21, 0, 0, TimeSpan.Zero);

        var insights = Insights(
            new StubAssistantSessionSource { Report = Report(Session(Tower, "Claude", ran.Item1, ran.Item2, "one")) },
            Activity([Ran("one", Tower, "Claude", ran)], [Refused("old", AssistantLimitKind.SevenDay, weeklyReset.AddDays(-1), weeklyReset)]),
            reset: new FixedUsageReset(new UsageWeekReset(DayOfWeek.Monday, new TimeOnly(14, 0))));

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(WeekSource.Configured, value.Week.Source);
        Assert.Equal(new DateTimeOffset(2026, 8, 17, 14, 0, 0, TimeSpan.Zero), value.Grids[^1].StartsAt);
    }

    /// <summary>
    /// A grid row is a calendar day, cut at the reset: with the reset at Monday 14:00
    /// the first row is Monday from 14:00 and the last is the next Monday up to 13:00 —
    /// eight rows, 168 hours between them, and the hours outside the week simply not
    /// there. One grid per column, on the same keys, so a column a reader picks names a
    /// grid.
    /// </summary>
    [Fact]
    public async Task A_grid_is_calendar_days_cut_at_the_reset_hour()
    {
        // Tuesday 18 August 02:00–03:00 UTC: on Tuesday's row, inside the week.
        var ran = (new DateTimeOffset(2026, 8, 18, 2, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 18, 3, 0, 0, TimeSpan.Zero));

        var insights = Insights(
            new StubAssistantSessionSource { Report = Report(Session(Tower, "Claude", ran.Item1, ran.Item2, "one")) },
            Activity(Ran("one", Tower, "Claude", ran)),
            reset: new FixedUsageReset(new UsageWeekReset(DayOfWeek.Monday, new TimeOnly(14, 0))));

        var value = await ValueOf(insights, DashboardScope.Default);

        var grid = value.Grids[^1];

        Assert.Equal(value.ActiveTimePerWeek.Count, value.Grids.Count);
        Assert.Equal(value.ActiveTimePerWeek.Select(point => point.Label), value.Grids.Select(one => one.Label));
        Assert.Equal(168, grid.Hours.Count);

        // Eight calendar rows: Monday 17 from 14:00 to Monday 24 up to 13:00.
        Assert.Equal(8, grid.Days.Count);
        Assert.Equal(Enumerable.Range(14, 10), grid.Hours.Where(hour => hour.Day == new DateOnly(2026, 8, 17)).Select(hour => hour.Hour));
        Assert.Equal(Enumerable.Range(0, 14), grid.Hours.Where(hour => hour.Day == new DateOnly(2026, 8, 24)).Select(hour => hour.Hour));
        Assert.Equal(24, grid.Hours.Count(hour => hour.Day == new DateOnly(2026, 8, 18)));

        // The run at 02:00 Tuesday is on Tuesday's row, and Tuesday's row counts it.
        var worked = Assert.Single(grid.Hours.Where(hour => hour.Active > TimeSpan.Zero));
        Assert.Equal((new DateOnly(2026, 8, 18), 2), (worked.Day, worked.Hour));
        Assert.Equal(1, grid.Days.Single(day => day.Day == new DateOnly(2026, 8, 18)).Sessions);

        // The latest grid is what the older properties still answer with.
        Assert.Equal(grid.Hours, value.ActivityByHour);
    }

    /// <summary>Under the calendar fallback the reset hour is midnight and a week is
    /// seven full days.</summary>
    [Fact]
    public async Task Under_the_calendar_fallback_a_grid_is_seven_full_days()
    {
        var ran = (Now.AddHours(-3), Now.AddHours(-1));

        var insights = Insights(
            new StubAssistantSessionSource { Report = Report(Session(Tower, "Claude", ran.Item1, ran.Item2, "one")) },
            Activity(Ran("one", Tower, "Claude", ran)));

        var grid = (await ValueOf(insights, DashboardScope.Default)).Grids[^1];

        Assert.Equal(7, grid.Days.Count);
        Assert.Equal(new DateOnly(2026, 8, 17), grid.Days[0].Day);
        Assert.All(grid.Days, day => Assert.Equal(24, grid.Hours.Count(hour => hour.Day == day.Day)));
    }

    /// <summary>A five-hour refusal is marked on the cell it happened in, on the row it
    /// falls in; a refusal in another week is on that week's grid.</summary>
    [Fact]
    public async Task A_refusal_is_marked_on_the_cell_of_the_week_it_happened_in()
    {
        var ran = (Now.AddHours(-3), Now.AddHours(-1));
        var refusedAt = new DateTimeOffset(2026, 8, 18, 16, 30, 0, TimeSpan.Zero);
        var lastWeek = new DateTimeOffset(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);

        var insights = Insights(
            new StubAssistantSessionSource { Report = Report(Session(Tower, "Claude", ran.Item1, ran.Item2, "one")) },
            Activity(
                [Ran("one", Tower, "Claude", ran)],
                [
                    Refused("one", AssistantLimitKind.FiveHour, refusedAt, refusedAt.AddHours(2)),
                    Refused("one", AssistantLimitKind.FiveHour, lastWeek, lastWeek.AddHours(2))
                ]),
            reset: new FixedUsageReset(new UsageWeekReset(DayOfWeek.Monday, new TimeOnly(14, 0))));

        var value = await ValueOf(insights, DashboardScope.Default);

        var mark = Assert.Single(value.Grids[^1].Limits);

        // Tuesday 16:30 is on Tuesday's row, hour 16.
        Assert.Equal((new DateOnly(2026, 8, 18), 16, AssistantLimitKind.FiveHour), (mark.Day, mark.Hour, mark.Kind));
        Assert.Equal(refusedAt, mark.At);

        // The wall reached to the hour the window reset: 18:30 is hour 18.
        Assert.Equal((new DateOnly(2026, 8, 18), 18), (mark.UntilDay, mark.UntilHour));

        Assert.Single(value.Grids[^2].Limits);
    }

    /// <summary>A wall that would reach past the week stops at the week's last hour, and
    /// a weekly refusal has no reach at all — its reset is the next week's start.</summary>
    [Fact]
    public async Task A_walls_reach_stops_at_the_week_and_a_weekly_refusal_has_none()
    {
        var ran = (Now.AddHours(-3), Now.AddHours(-1));

        // Sunday 23 August 22:30 UTC, resetting Monday 02:00 — past the calendar week's
        // end at Monday 00:00 — and a weekly refusal an hour earlier.
        var lateSunday = new DateTimeOffset(2026, 8, 23, 22, 30, 0, TimeSpan.Zero);

        var insights = Insights(
            new StubAssistantSessionSource { Report = Report(Session(Tower, "Claude", ran.Item1, ran.Item2, "one")) },
            Activity(
                [Ran("one", Tower, "Claude", ran)],
                [
                    Refused("one", AssistantLimitKind.FiveHour, lateSunday, lateSunday.AddHours(3.5)),
                    Refused("one", AssistantLimitKind.SevenDay, lateSunday.AddHours(-1), new DateTimeOffset(2026, 8, 24, 21, 0, 0, TimeSpan.Zero))
                ]),
            // Pinned to Monday midnight, so the weekly refusal above does not move the
            // week's end to its own reset and the five-hour reset genuinely falls past it.
            reset: new FixedUsageReset(new UsageWeekReset(DayOfWeek.Monday, new TimeOnly(0, 0))));

        var value = await ValueOf(insights, DashboardScope.Default);

        var marks = value.Grids[^1].Limits;

        var fiveHour = Assert.Single(marks.Where(mark => mark.Kind == AssistantLimitKind.FiveHour));
        Assert.Equal((new DateOnly(2026, 8, 23), 23), (fiveHour.UntilDay, fiveHour.UntilHour));

        var weekly = Assert.Single(marks.Where(mark => mark.Kind == AssistantLimitKind.SevenDay));
        Assert.Null(weekly.UntilDay);
    }

    /// <summary>The refusals are counted for the tile, by kind, over the window.</summary>
    [Fact]
    public async Task Refusals_are_counted_by_kind_over_the_window()
    {
        var ran = (Now.AddHours(-3), Now.AddHours(-1));

        var insights = Insights(
            new StubAssistantSessionSource { Report = Report(Session(Tower, "Claude", ran.Item1, ran.Item2, "one")) },
            Activity(
                [Ran("one", Tower, "Claude", ran)],
                [
                    Refused("one", AssistantLimitKind.FiveHour, Now.AddHours(-2), Now),
                    Refused("one", AssistantLimitKind.FiveHour, Now.AddDays(-10), Now.AddDays(-10).AddHours(2)),
                    Refused("one", AssistantLimitKind.SevenDay, Now.AddDays(-9), Now),
                    Refused("one", AssistantLimitKind.FiveHour, Now.AddDays(-40), Now.AddDays(-40).AddHours(2))
                ]));

        var four = await ValueOf(insights, new DashboardScope(Period: DashboardPeriod.FourWeeks));
        var twelve = await ValueOf(insights, new DashboardScope(Period: DashboardPeriod.TwelveWeeks));

        Assert.Equal(new LimitHitCounts(2, 1), four.LimitHits);
        Assert.Equal(new LimitHitCounts(3, 1), twelve.LimitHits);
        Assert.Equal(4, twelve.LimitHits.Total);
    }

    /// <summary>The refusals follow the machine filter, the way every grid does.</summary>
    [Fact]
    public async Task A_refusal_on_another_machine_is_not_marked_when_one_machine_is_focused()
    {
        var ran = (Now.AddHours(-3), Now.AddHours(-1));

        var insights = Insights(
            new StubAssistantSessionSource { Report = Report(Session(Tower, "Claude", ran.Item1, ran.Item2, "one")) },
            Activity(
                [Ran("one", Tower, "Claude", ran)],
                [new AssistantLimitHit(Now.AddHours(-2), AssistantLimitKind.FiveHour, Now, "elsewhere", Laptop)]));

        var everywhere = await ValueOf(insights, DashboardScope.Default);
        var focused = await ValueOf(insights, new DashboardScope(MachineId: Tower));

        Assert.Single(everywhere.Grids[^1].Limits);
        Assert.Empty(focused.Grids[^1].Limits);
    }

    /// <summary>Following the period control, exactly as the tiles they cut do: four
    /// weeks is five columns and twelve is thirteen, and the older run is only on the
    /// wider axis.</summary>
    [Fact]
    public async Task The_weekly_series_follow_the_period_control()
    {
        var older = (Now.AddDays(-42), Now.AddDays(-42).AddHours(1));

        var insights = Insights(
            new StubAssistantSessionSource { Report = Report(Session(Tower, "Claude", older.Item1, older.Item2, "old")) },
            Activity(Ran("old", Tower, "Claude", older)));

        var four = await ValueOf(insights, new DashboardScope(Period: DashboardPeriod.FourWeeks));
        var twelve = await ValueOf(insights, new DashboardScope(Period: DashboardPeriod.TwelveWeeks));

        Assert.Equal(5, four.ActiveTimePerWeek.Count);
        Assert.Equal(13, twelve.ActiveTimePerWeek.Count);
        Assert.Equal(0m, four.ActiveTimePerWeek.Sum(point => point.Value));
        Assert.Equal(1m, twelve.ActiveTimePerWeek.Sum(point => point.Value));
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
        WorkingHours? week = null,
        IUsageResetSettings? reset = null) =>
        new(
            source,
            activity ?? new StubAssistantActivitySource(),
            new FixedWorkingHours(week ?? WorkingHours.Default),
            new StubRepositoryDirectory(),
            reset ?? new FixedUsageReset(null),
            new FixedClock(Now, zone));

    /// <summary>A reset that does not come off disk: null for the calendar fallback the
    /// weekly facts are asserted under, or the one a fact about the usage week hands in.</summary>
    private sealed class FixedUsageReset(UsageWeekReset? reset) : IUsageResetSettings
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public UsageWeekReset? Current => reset;

        public string SettingsPath => "usage-reset.json";

        public string? Set(DayOfWeek day, TimeOnly time) => null;

        public string? Clear() => null;
    }

    /// <summary>Two configured repositories, so a recorded one can be known by its
    /// alias, unknown, or absent — the three bands the repository rows are cut into.</summary>
    private sealed class StubRepositoryDirectory : IRepositoryDirectory
    {
        public IReadOnlyList<DashboardRepository> Repositories { get; } =
        [
            new("backlog", "acme/backlog"),
            new("other", "acme/other")
        ];
    }

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

    /// <summary>A report carrying the allowance refusals as well, on the same terms.</summary>
    private static StubAssistantActivitySource Activity(
        AssistantActivitySession[] sessions,
        AssistantLimitHit[] limits) =>
        new()
        {
            Report = new AssistantActivityReport(sessions, [], Now.AddDays(-7 * 12), TimeSpan.FromMinutes(5))
            {
                Limits = limits
            }
        };

    /// <summary>One refusal, placed on a session on the tower.</summary>
    private static AssistantLimitHit Refused(
        string sessionId,
        AssistantLimitKind kind,
        DateTimeOffset at,
        DateTimeOffset resetsAt) =>
        new(at, kind, resetsAt, sessionId, Tower);

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

        /// <summary>When set, the report is held back until this completes — and the
        /// wait honours the token, the way the real readers do between transcripts.</summary>
        public TaskCompletionSource? Gate { get; init; }

        public async Task<AssistantSessionReport> GetSessionsAsync(CancellationToken cancellationToken = default)
        {
            Calls++;

            if (Gate is not null)
            {
                await Gate.Task.WaitAsync(cancellationToken);
            }

            return Report;
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
