using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The actual hours a roadmap head shows: the union of every agent's active stretches,
/// cut at local midnight and summed per date (ADR 0019, verification 18 to 22).
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

    [Fact]
    public async Task Overlapping_sessions_count_once()
    {
        var source = new StubActivity(Log(
            Session("one", Run(At(2026, 10, 1, 9), At(2026, 10, 1, 11))),
            Session("two", Run(At(2026, 10, 1, 10), At(2026, 10, 1, 12)))));

        var hours = await Read(source, new DateOnly(2026, 9, 28), Today);

        Assert.Equal(TimeSpan.FromHours(3), Assert.Single(hours).Value);
    }

    [Fact]
    public async Task A_stretch_across_midnight_counts_toward_each_date_for_its_own_part()
    {
        var source = new StubActivity(Log(
            Session("late", Run(At(2026, 9, 30, 23), At(2026, 10, 1, 1).AddMinutes(30)))));

        var hours = await Read(source, new DateOnly(2026, 9, 28), Today);

        Assert.Equal(TimeSpan.FromHours(1), hours[new DateOnly(2026, 9, 30)]);
        Assert.Equal(TimeSpan.FromHours(1.5), hours[new DateOnly(2026, 10, 1)]);
    }

    [Fact]
    public async Task A_date_after_today_is_never_answered()
    {
        // A source that reports a stretch it cannot have seen yet, so the clip is what
        // keeps it off the axis rather than the source's good behaviour.
        var source = new StubActivity(Log(
            Session("ahead", Run(At(2026, 10, 5, 9), At(2026, 10, 5, 11)))));

        var hours = await Read(source, new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 11));

        Assert.Empty(hours);
    }

    [Fact]
    public async Task A_range_entirely_after_today_reads_nothing()
    {
        var source = new StubActivity(Log(Session("any", Run(At(2026, 10, 1, 9), At(2026, 10, 1, 10)))));

        var hours = await Read(source, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 11));

        Assert.Empty(hours);
        Assert.Empty(source.Asked);
    }

    [Fact]
    public async Task Today_counts_up_to_now()
    {
        var source = new StubActivity(Log(
            Session("running", Run(At(2026, 10, 3, 12), At(2026, 10, 3, 17)))));

        var hours = await Read(source, Today, Today);

        Assert.Equal(TimeSpan.FromHours(2), hours[Today]);
    }

    [Fact]
    public async Task A_wait_is_not_active_time()
    {
        var source = new StubActivity(Log(
            new AgentSessionActivity(
                "waiting",
                AgentSessionKind.Claude,
                "tower",
                "DEV-TOWER",
                [Run(At(2026, 10, 2, 9), At(2026, 10, 2, 10))],
                [new AgentActivityWait(At(2026, 10, 2, 10), At(2026, 10, 2, 13))])));

        var hours = await Read(source, new DateOnly(2026, 9, 28), Today);

        Assert.Equal(TimeSpan.FromHours(1), hours[new DateOnly(2026, 10, 2)]);
    }

    [Fact]
    public async Task No_activity_answers_an_empty_map_rather_than_no_answer()
    {
        var hours = await Read(new StubActivity(Log()), new DateOnly(2026, 9, 28), Today);

        Assert.NotNull(hours);
        Assert.Empty(hours);
    }

    [Fact]
    public async Task A_session_replicated_from_a_paired_device_counts_and_overlaps_once()
    {
        var source = new StubActivity(Log(
            Session("here", Run(At(2026, 10, 2, 9), At(2026, 10, 2, 11))),
            Session("laptop", Run(At(2026, 10, 2, 10), At(2026, 10, 2, 13))) with
            {
                Origin = AgentSessionOrigin.Replicated
            }));

        var hours = await Read(source, new DateOnly(2026, 9, 28), Today);

        Assert.Equal(TimeSpan.FromHours(4), hours[new DateOnly(2026, 10, 2)]);
    }

    [Fact]
    public async Task A_subagent_counts_where_its_session_was_silent_and_once_where_both_ran()
    {
        // The parent goes quiet while the agent it spawned works; the spawned agent's
        // stretch is what says the time was worked.
        var log = Log(Session("parent", Run(At(2026, 10, 2, 9), At(2026, 10, 2, 10)))) with
        {
            Subagents =
            [
                new SubagentActivity(
                    "agent-1",
                    "parent",
                    AgentSessionKind.Claude,
                    "tower",
                    "DEV-TOWER",
                    [Run(At(2026, 10, 2, 9).AddMinutes(30), At(2026, 10, 2, 12))])
            ]
        };

        var hours = await Read(new StubActivity(log), new DateOnly(2026, 9, 28), Today);

        Assert.Equal(TimeSpan.FromHours(3), hours[new DateOnly(2026, 10, 2)]);
    }

    [Fact]
    public async Task The_source_is_asked_from_local_midnight_of_the_first_date()
    {
        var source = new StubActivity(Log());

        await Read(source, new DateOnly(2026, 9, 28), Today);

        Assert.Equal(At(2026, 9, 28, 0), Assert.Single(source.Asked));
    }

    [Fact]
    public async Task Time_before_the_first_date_is_not_counted()
    {
        var source = new StubActivity(Log(
            Session("early", Run(At(2026, 9, 27, 23), At(2026, 9, 28, 1)))));

        var hours = await Read(source, new DateOnly(2026, 9, 28), Today);

        Assert.Equal(TimeSpan.FromHours(1), Assert.Single(hours).Value);
    }

    [Fact]
    public async Task The_day_the_clocks_go_back_has_twenty_five_hours()
    {
        // 25 October 2026: Amsterdam leaves summer time at 03:00 and repeats an hour.
        var time = Clock(At(2026, 10, 27, 12));
        var source = new StubActivity(Log(
            Session("all-day", Run(At(2026, 10, 25, 0), At(2026, 10, 26, 0)))));

        var hours = await new RoadmapActualHours(source, time)
            .ReadAsync(new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 26), TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromHours(25), Assert.Single(hours!).Value);
    }

    [Fact]
    public async Task A_host_without_session_activity_cannot_state_the_hours()
    {
        var hours = await new RoadmapActualHours(null, Clock(Now))
            .ReadAsync(new DateOnly(2026, 9, 28), Today, TestContext.Current.CancellationToken);

        Assert.Null(hours);
    }

    [Fact]
    public async Task With_the_sessions_area_switched_off_the_hours_are_not_read()
    {
        var source = new StubActivity(Log(Session("any", Run(At(2026, 10, 1, 9), At(2026, 10, 1, 10)))));

        var hours = await new RoadmapActualHours(source, Clock(Now), new Features(sessions: false))
            .ReadAsync(new DateOnly(2026, 9, 28), Today, TestContext.Current.CancellationToken);

        Assert.Null(hours);
        Assert.Empty(source.Asked);
    }

    [Fact]
    public async Task With_the_sessions_area_on_the_hours_are_read()
    {
        var source = new StubActivity(Log(Session("any", Run(At(2026, 10, 1, 9), At(2026, 10, 1, 10)))));

        var hours = await new RoadmapActualHours(source, Clock(Now), new Features(sessions: true))
            .ReadAsync(new DateOnly(2026, 9, 28), Today, TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromHours(1), Assert.Single(hours!).Value);
    }

    [Fact]
    public async Task A_failed_read_surfaces_to_the_caller()
    {
        var source = new StubActivity(Log()) { Failure = new IOException("transcript locked") };

        await Assert.ThrowsAsync<IOException>(() => Read(source, new DateOnly(2026, 9, 28), Today));
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

    private static AgentActivityRun Run(DateTimeOffset from, DateTimeOffset to) => new(from, to);

    private static AgentSessionActivity Session(string id, params AgentActivityRun[] runs) =>
        new(id, AgentSessionKind.Claude, "tower", "DEV-TOWER", runs, []);

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
