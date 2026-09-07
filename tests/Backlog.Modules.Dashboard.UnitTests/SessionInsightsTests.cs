using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.Services;

namespace Backlog.Modules.Dashboard.UnitTests;

/// <summary>
/// The sessions part: one local read, scoped by window and by machine in this module
/// rather than by the source.
/// <para>
/// The arithmetic is what these facts are about. Every figure here understates rather
/// than invents — a session that ran past the edge of the window is clipped to it, a
/// session whose start was never recorded contributes no duration at all, and both of
/// those are said out loud rather than smoothed over.
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
        var insights = Insights(new StubAssistantSessionSource
        {
            Report = Report(Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-1)))
        });

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(1, value.Sessions);
        Assert.Equal(TimeSpan.FromHours(2), value.ActiveTime);
        Assert.Equal(Now.AddHours(-1), value.LastActivityAt);
        Assert.Equal(0, value.WithoutStart);
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

    /// <summary>
    /// A session that began before the window and ran into it counts, but only for the
    /// part of it that is inside: a quarter's figure that included time from the
    /// quarter before would make two readings of the same window disagree.
    /// </summary>
    [Fact]
    public async Task A_session_that_began_before_the_window_is_clipped_to_it()
    {
        var scope = new DashboardScope(Period: DashboardPeriod.FourWeeks);
        var (from, _) = scope.Window(Now);

        var insights = Insights(new StubAssistantSessionSource
        {
            Report = Report(Session(Tower, "Claude", from.AddHours(-5), from.AddHours(3)))
        });

        var value = await ValueOf(insights, scope);

        Assert.Equal(1, value.Sessions);
        Assert.Equal(TimeSpan.FromHours(3), value.ActiveTime);
    }

    /// <summary>
    /// A session with no recorded start is real and is counted; what it cannot do is
    /// contribute a duration. Reporting it as a zero-length session would be inventing
    /// a fact, and leaving it out entirely would understate how many there were — so it
    /// counts, adds nothing, and is named.
    /// </summary>
    [Fact]
    public async Task A_session_whose_start_was_never_recorded_counts_but_adds_no_time()
    {
        var insights = Insights(new StubAssistantSessionSource
        {
            Report = Report(
                Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-1)),
                Session(Tower, "Copilot", startedAt: null, Now.AddHours(-2)))
        });

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(2, value.Sessions);
        Assert.Equal(TimeSpan.FromHours(2), value.ActiveTime);
        Assert.Equal(1, value.WithoutStart);
    }

    [Fact]
    public async Task Focusing_a_machine_narrows_the_figures_to_that_machine()
    {
        var insights = Insights(new StubAssistantSessionSource
        {
            Report = Report(
                Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-1)),
                Session(Laptop, "Copilot", Now.AddHours(-9), Now.AddHours(-8)))
        });

        var value = await ValueOf(insights, DashboardScope.Default with { MachineId = Laptop });

        Assert.Equal(1, value.Sessions);
        Assert.Equal(TimeSpan.FromHours(1), value.ActiveTime);
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
    /// The arithmetic the tile's permanent footnote is about. Two agents running through
    /// the same hour is two hours of work with the assistants, not one hour of wall
    /// clock — the figure is deliberately capable of exceeding the window it sits under,
    /// which is why the screen says so rather than leaving the reader to find out.
    /// </summary>
    [Fact]
    public async Task Sessions_that_overlap_are_summed_rather_than_merged()
    {
        var insights = Insights(new StubAssistantSessionSource
        {
            Report = Report(
                Session(Tower, "Claude", Now.AddHours(-3), Now.AddHours(-2)),
                Session(Tower, "Copilot", Now.AddHours(-3), Now.AddHours(-2)))
        });

        var value = await ValueOf(insights, DashboardScope.Default);

        Assert.Equal(TimeSpan.FromHours(2), value.ActiveTime);
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

        var insights = Insights(new StubAssistantSessionSource
        {
            Report = Report(Session(Tower, "Claude", from.AddHours(-1), from))
        });

        var value = await ValueOf(insights, scope);

        Assert.Equal(1, value.Sessions);

        // In the window and contributing nothing to it: every minute it ran was before
        // the window opened.
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
    /// Activity after the window closed is clipped to the close, the same way activity
    /// before it opened is clipped to the open. Otherwise a session still running would
    /// keep adding time the reader did not ask to see.
    /// </summary>
    [Fact]
    public async Task Activity_past_the_end_of_the_window_is_clipped_to_it()
    {
        var scope = new DashboardScope(Period: DashboardPeriod.FourWeeks);
        var (_, to) = scope.Window(Now);

        var insights = Insights(new StubAssistantSessionSource
        {
            Report = Report(Session(Tower, "Claude", to.AddHours(-2), to.AddHours(3)))
        });

        var value = await ValueOf(insights, scope);

        Assert.Equal(1, value.Sessions);
        Assert.Equal(TimeSpan.FromHours(2), value.ActiveTime);
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

    private static async Task<AssistantSessionsInsight> ValueOf(ISessionInsights insights, DashboardScope scope)
    {
        var result = await insights.GetSessionsAsync(scope);

        Assert.True(result.HasValue);

        return result.Value!;
    }

    private static SessionInsights Insights(IAssistantSessionSource source) => new(source, new FixedClock(Now));

    private static AssistantSessionReport Report(params AssistantSession[] sessions) =>
        new(sessions, [], false, 100);

    private static AssistantSession Session(
        string machineId,
        string assistant,
        DateTimeOffset? startedAt,
        DateTimeOffset lastActivityAt) =>
        Session(machineId, MachineName(machineId), assistant, startedAt, lastActivityAt);

    private static AssistantSession Session(
        string machineId,
        string machineName,
        string assistant,
        DateTimeOffset? startedAt,
        DateTimeOffset lastActivityAt) =>
        new(machineId, machineName, assistant, startedAt, lastActivityAt);

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

    /// <summary>A clock that does not move, so a window is the same window on every
    /// machine and on every run.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
