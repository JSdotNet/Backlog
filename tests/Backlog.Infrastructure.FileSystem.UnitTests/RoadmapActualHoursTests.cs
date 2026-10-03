using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The actual hours a roadmap head shows: the person's working stretches, built per
/// session from their own turns, merged across sessions, cut at local midnight and
/// summed per date (ADR 0019 §6, verification 19 to 30).
/// <para>
/// Every test runs on a named zone rather than whatever zone CI is in, for the reason
/// the dashboard's local-hour grid takes its zone as a parameter: a split at midnight
/// asserted against an unknown zone is asserted against nothing.
/// </para>
/// </summary>
public sealed class RoadmapActualHoursTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    /// <summary>Saturday 3 October 2026, 14:00 in Amsterdam (summer time).</summary>
    private static readonly DateTimeOffset Now = At(2026, 10, 3, 14);

    private static readonly DateOnly Today = new(2026, 10, 3);

    private static readonly DateOnly Thursday = new(2026, 10, 1);

    private static readonly DateOnly Monday = new(2026, 9, 28);

    [Fact]
    public async Task Turns_twenty_minutes_apart_join_and_the_gap_between_them_counts()
    {
        // 10:00 prompt, answered until 10:05; 10:20 prompt, answered until 10:30.
        var source = new StubActivity(Log(
            Session(
                "joined",
                turns: [On(Thursday, 10), On(Thursday, 10, 20)],
                Run(On(Thursday, 10), On(Thursday, 10, 5)),
                Run(On(Thursday, 10, 20), On(Thursday, 10, 30)))));

        var hours = await Read(source, Monday, Today);

        Assert.Equal(TimeSpan.FromMinutes(30), hours[Thursday]);
    }

    [Fact]
    public async Task Turns_forty_minutes_apart_stay_apart_and_the_gap_counts_nothing()
    {
        // 10:00 prompt, answered until 10:05; 10:40 prompt, answered until 10:45.
        var source = new StubActivity(Log(
            Session(
                "apart",
                turns: [On(Thursday, 10), On(Thursday, 10, 40)],
                Run(On(Thursday, 10), On(Thursday, 10, 5)),
                Run(On(Thursday, 10, 40), On(Thursday, 10, 45)))));

        var hours = await Read(source, Monday, Today);

        Assert.Equal(TimeSpan.FromMinutes(10), hours[Thursday]);
    }

    [Fact]
    public async Task A_stretch_ends_when_the_reply_to_its_last_turn_ends()
    {
        var source = new StubActivity(Log(
            Session("answered", turns: [On(Thursday, 11)], Run(On(Thursday, 11), On(Thursday, 11, 45)))));

        var hours = await Read(source, Monday, Today);

        Assert.Equal(TimeSpan.FromMinutes(45), hours[Thursday]);
    }

    [Fact]
    public async Task Overnight_subagents_with_no_human_turn_behind_them_add_nothing()
    {
        // The person started an orchestration at 22:00 and left it; the agents it spawned
        // worked until six in the morning, and the session answered their hand-backs.
        var log = Log(Session(
            "orchestrator",
            turns: [On(Thursday, 22)],
            Run(On(Thursday, 22), On(Thursday, 22, 10)),
            Run(On(Thursday, 23), At(2026, 10, 2, 6)))) with
        {
            Subagents =
            [
                new SubagentActivity(
                    "agent-1",
                    "orchestrator",
                    AgentSessionKind.Claude,
                    "tower",
                    "DEV-TOWER",
                    [Run(On(Thursday, 22, 10), At(2026, 10, 2, 6))])
            ]
        };

        var hours = await Read(new StubActivity(log), Monday, Today);

        Assert.Equal(TimeSpan.FromMinutes(10), hours[Thursday]);
        Assert.False(hours.ContainsKey(new DateOnly(2026, 10, 2)));
    }

    [Fact]
    public async Task Overlapping_stretches_of_two_sessions_count_once()
    {
        var source = new StubActivity(Log(
            Session("one", turns: [On(Thursday, 9)], Run(On(Thursday, 9), On(Thursday, 11))),
            Session("two", turns: [On(Thursday, 10)], Run(On(Thursday, 10), On(Thursday, 12)))));

        var hours = await Read(source, Monday, Today);

        Assert.Equal(TimeSpan.FromHours(3), Assert.Single(hours).Value);
    }

    [Fact]
    public async Task A_task_notification_neither_starts_nor_extends_a_stretch()
    {
        // The fold never marks a notification as a human turn, so the run it caused at
        // 10:20 has no turn behind it.
        var source = new StubActivity(Log(
            Session(
                "notified",
                turns: [On(Thursday, 10)],
                Run(On(Thursday, 10), On(Thursday, 10, 5)),
                Run(On(Thursday, 10, 20), On(Thursday, 10, 40)))));

        var hours = await Read(source, Monday, Today);

        Assert.Equal(TimeSpan.FromMinutes(5), hours[Thursday]);
    }

    [Fact]
    public async Task An_answer_to_the_agents_question_extends_the_stretch()
    {
        // Prompted at 10:00; the agent asked at 10:10; the person answered at 10:25 and
        // the agent worked until 10:35.
        var source = new StubActivity(Log(
            Session(
                "asked",
                turns: [On(Thursday, 10), On(Thursday, 10, 25)],
                Run(On(Thursday, 10), On(Thursday, 10, 10)),
                Run(On(Thursday, 10, 25), On(Thursday, 10, 35)))));

        var hours = await Read(source, Monday, Today);

        Assert.Equal(TimeSpan.FromMinutes(35), hours[Thursday]);
    }

    [Fact]
    public async Task A_stretch_across_midnight_counts_toward_each_date_for_its_own_part()
    {
        var source = new StubActivity(Log(
            Session("late", turns: [At(2026, 9, 30, 23)], Run(At(2026, 9, 30, 23), At(2026, 10, 1, 1)))));

        var hours = await Read(source, Monday, Today);

        Assert.Equal(TimeSpan.FromHours(1), hours[new DateOnly(2026, 9, 30)]);
        Assert.Equal(TimeSpan.FromHours(1), hours[Thursday]);
    }

    [Fact]
    public async Task A_session_replicated_from_a_paired_device_counts_and_overlaps_once()
    {
        var source = new StubActivity(Log(
            Session("here", turns: [On(Thursday, 9)], Run(On(Thursday, 9), On(Thursday, 11))),
            Session("laptop", turns: [On(Thursday, 10)], Run(On(Thursday, 10), On(Thursday, 13))) with
            {
                Origin = AgentSessionOrigin.Replicated
            }));

        var hours = await Read(source, Monday, Today);

        Assert.Equal(TimeSpan.FromHours(4), hours[Thursday]);
    }

    [Fact]
    public async Task Today_counts_up_to_now()
    {
        // A source that reports a reply running past now, so the clip is what keeps
        // today honest rather than the source's good behaviour.
        var source = new StubActivity(Log(
            Session("running", turns: [On(Today, 12)], Run(On(Today, 12), On(Today, 17)))));

        var hours = await Read(source, Today, Today);

        Assert.Equal(TimeSpan.FromHours(2), hours[Today]);
    }

    [Fact]
    public async Task An_open_stretch_counts_up_to_now()
    {
        // The reply's last event was two minutes ago, inside the gap that ends a run, so
        // the agent may still be answering and the person is still at it.
        var source = new StubActivity(Log(
            Session("open", turns: [On(Today, 13, 30)], Run(On(Today, 13, 30), On(Today, 13, 58)))));

        var hours = await Read(source, Today, Today);

        Assert.Equal(TimeSpan.FromMinutes(30), hours[Today]);
    }

    [Fact]
    public async Task A_copilot_session_contributes_no_hours()
    {
        // Copilot's stream cannot say who made a turn, so its record carries none.
        var source = new StubActivity(Log(
            new AgentSessionActivity(
                "copilot",
                AgentSessionKind.Copilot,
                "tower",
                "DEV-TOWER",
                [Run(On(Thursday, 9), On(Thursday, 12))],
                [])));

        var hours = await Read(source, Monday, Today);

        Assert.Empty(hours);
    }

    [Fact]
    public async Task A_wait_is_not_worked_time_after_the_stretch_has_ended()
    {
        // The agent stopped at 10:00 and nobody prompted it until 13:00: the stretch of
        // the 09:00 prompt ended with its reply, and the three hours count nothing.
        var source = new StubActivity(Log(
            new AgentSessionActivity(
                "waiting",
                AgentSessionKind.Claude,
                "tower",
                "DEV-TOWER",
                [Run(On(Thursday, 9), On(Thursday, 10))],
                [new AgentActivityWait(On(Thursday, 10), On(Thursday, 13))])
            {
                HumanTurns = [On(Thursday, 9)]
            }));

        var hours = await Read(source, Monday, Today);

        Assert.Equal(TimeSpan.FromHours(1), hours[Thursday]);
    }

    [Fact]
    public async Task No_activity_answers_an_empty_map_rather_than_no_answer()
    {
        var hours = await Read(new StubActivity(Log()), Monday, Today);

        Assert.NotNull(hours);
        Assert.Empty(hours);
    }

    [Fact]
    public async Task A_date_after_today_is_never_answered()
    {
        var source = new StubActivity(Log(
            Session("ahead", turns: [At(2026, 10, 5, 9)], Run(At(2026, 10, 5, 9), At(2026, 10, 5, 11)))));

        var hours = await Read(source, Monday, new DateOnly(2026, 10, 11));

        Assert.Empty(hours);
    }

    [Fact]
    public async Task A_range_entirely_after_today_reads_nothing()
    {
        var source = new StubActivity(Log(Session("any", turns: [On(Thursday, 9)], Run(On(Thursday, 9), On(Thursday, 10)))));

        var hours = await Read(source, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 11));

        Assert.Empty(hours);
        Assert.Empty(source.Asked);
    }

    [Fact]
    public async Task The_source_is_asked_from_a_day_before_the_first_date_so_a_stretch_begun_before_it_is_whole()
    {
        var source = new StubActivity(Log());

        await Read(source, Monday, Today);

        Assert.Equal(At(2026, 9, 27, 0), Assert.Single(source.Asked));
    }

    [Fact]
    public async Task Time_before_the_first_date_is_not_counted_and_a_stretch_begun_then_still_joins()
    {
        // Prompted at 23:50 on Sunday and again at 00:10 on Monday: one stretch, of which
        // only Monday's part is asked for. Without the 23:50 turn the Monday part would
        // start at 00:10.
        var source = new StubActivity(Log(
            Session(
                "early",
                turns: [At(2026, 9, 27, 23).AddMinutes(50), At(2026, 9, 28, 0).AddMinutes(10)],
                Run(At(2026, 9, 27, 23).AddMinutes(50), At(2026, 9, 27, 23).AddMinutes(55)),
                Run(At(2026, 9, 28, 0).AddMinutes(10), At(2026, 9, 28, 0).AddMinutes(30)))));

        var hours = await Read(source, Monday, Today);

        Assert.Equal(TimeSpan.FromMinutes(30), Assert.Single(hours).Value);
    }

    [Fact]
    public async Task The_day_the_clocks_go_back_has_twenty_five_hours()
    {
        // 25 October 2026: Amsterdam leaves summer time at 03:00 and repeats an hour.
        var time = Clock(At(2026, 10, 27, 12));
        var source = new StubActivity(Log(
            Session("all-day", turns: [At(2026, 10, 25, 0)], Run(At(2026, 10, 25, 0), At(2026, 10, 26, 0)))));

        var hours = await new RoadmapActualHours(source, time)
            .ReadAsync(new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 26), TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromHours(25), Assert.Single(hours!).Value);
    }

    [Fact]
    public async Task A_host_without_session_activity_cannot_state_the_hours()
    {
        var hours = await new RoadmapActualHours(null, Clock(Now))
            .ReadAsync(Monday, Today, TestContext.Current.CancellationToken);

        Assert.Null(hours);
    }

    [Fact]
    public async Task With_the_sessions_area_switched_off_the_hours_are_not_read()
    {
        var source = new StubActivity(Log(Session("any", turns: [On(Thursday, 9)], Run(On(Thursday, 9), On(Thursday, 10)))));

        var hours = await new RoadmapActualHours(source, Clock(Now), new Features(sessions: false))
            .ReadAsync(Monday, Today, TestContext.Current.CancellationToken);

        Assert.Null(hours);
        Assert.Empty(source.Asked);
    }

    [Fact]
    public async Task With_the_sessions_area_on_the_hours_are_read()
    {
        var source = new StubActivity(Log(Session("any", turns: [On(Thursday, 9)], Run(On(Thursday, 9), On(Thursday, 10)))));

        var hours = await new RoadmapActualHours(source, Clock(Now), new Features(sessions: true))
            .ReadAsync(Monday, Today, TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromHours(1), Assert.Single(hours!).Value);
    }

    [Fact]
    public async Task A_failed_read_surfaces_to_the_caller()
    {
        var source = new StubActivity(Log()) { Failure = new IOException("transcript locked") };

        await Assert.ThrowsAsync<IOException>(() => Read(source, Monday, Today));
    }

    private static async Task<IReadOnlyDictionary<DateOnly, TimeSpan>> Read(
        IAgentActivitySource source,
        DateOnly from,
        DateOnly through)
    {
        var hours = await new RoadmapActualHours(source, Clock(Now))
            .ReadAsync(from, through, TestContext.Current.CancellationToken);

        Assert.NotNull(hours);
        return hours;
    }

    private static FakeTimeProvider Clock(DateTimeOffset now)
    {
        var time = new FakeTimeProvider(now);
        time.SetLocalTimeZone(Amsterdam);
        return time;
    }

    /// <summary>An Amsterdam wall-clock hour as the instant it names.</summary>
    private static DateTimeOffset At(int year, int month, int day, int hour)
    {
        var local = new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Amsterdam.GetUtcOffset(local));
    }

    /// <summary>An Amsterdam wall-clock time on a date as the instant it names.</summary>
    private static DateTimeOffset On(DateOnly date, int hour, int minute = 0) =>
        At(date.Year, date.Month, date.Day, hour).AddMinutes(minute);

    private static AgentActivityRun Run(DateTimeOffset from, DateTimeOffset to) => new(from, to);

    private static AgentSessionActivity Session(string id, DateTimeOffset[] turns, params AgentActivityRun[] runs) =>
        new(id, AgentSessionKind.Claude, "tower", "DEV-TOWER", runs, []) { HumanTurns = turns };

    private static AgentActivityLog Log(params AgentSessionActivity[] sessions) =>
        new(sessions, [], DateTimeOffset.MinValue, TimeSpan.FromMinutes(5));

    private sealed class StubActivity(AgentActivityLog log) : IAgentActivitySource
    {
        public List<DateTimeOffset> Asked { get; } = [];

        public Exception? Failure { get; init; }

        public Task<AgentActivityLog> GetActivityAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
        {
            Asked.Add(since);
            return Failure is null ? Task.FromResult(log) : Task.FromException<AgentActivityLog>(Failure);
        }
    }

    private sealed class Features(bool sessions) : IAppFeatureSettings
    {
        public event Action? Changed { add { } remove { } }

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => "features.json";

        public bool IsEnabled(string key) =>
            key == SessionFeatures.Sessions ? sessions : throw new ArgumentException("Unknown feature " + key, nameof(key));

        public string? SetEnabled(string key, bool enabled) => null;
    }
}
