namespace Backlog.Infrastructure.Sessions.UnitTests;

/// <summary>
/// The usage week — placed by the latest weekly-limit refusal's reset time, else the
/// trailing seven days — and the weekly limits calibrated from the cost reached at the
/// most recent refusal of each kind, with Fable's limit counting Fable's cost only.
/// </summary>
public sealed class WeeklyUsageTests
{
    private const string Owner = "session-owner";

    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private static AgentLimitHit Hit(AgentLimitKind kind, DateTimeOffset at, DateTimeOffset? resetsAt = null) =>
        new(at, kind, null) { ResetsAt = resetsAt };

    private static ClaudeApiRequest Request(
        string id,
        DateTimeOffset at,
        long? micros,
        string model = "claude-opus-5-5",
        string session = Owner) =>
        new(id, session, at, model, "high", micros, 10, 100, 1000, 100, 500, "repl_main_thread", null, null, null);

    [Fact]
    public void Without_a_weekly_refusal_the_week_is_the_trailing_seven_days()
    {
        var week = UsageWeek.Of([Hit(AgentLimitKind.FiveHour, Now.AddHours(-1), Now.AddHours(2))], Now);

        Assert.Equal(new UsageWeek(Now.AddDays(-7), Now, false), week);
        Assert.True(week.Contains(Now));
        Assert.False(week.Contains(Now.AddDays(-7).AddTicks(-1)));
    }

    [Fact]
    public void The_week_ends_at_the_reset_the_latest_weekly_refusal_carried()
    {
        var reset = Now.AddDays(2);
        var hits = new[]
        {
            Hit(AgentLimitKind.Weekly, Now.AddDays(-9), Now.AddDays(-5)),
            Hit(AgentLimitKind.WeeklyFable, Now.AddHours(-3), reset)
        };

        var week = UsageWeek.Of(hits, Now);

        Assert.Equal(new UsageWeek(reset.AddDays(-7), reset, true), week);
        Assert.False(week.Contains(reset));
    }

    [Fact]
    public void An_all_models_refusal_places_the_week_before_a_later_Fable_one()
    {
        var hits = new[]
        {
            Hit(AgentLimitKind.Weekly, Now.AddDays(-2), Now.AddDays(1)),
            Hit(AgentLimitKind.WeeklyFable, Now.AddHours(-1), Now.AddDays(3))
        };

        Assert.Equal(Now.AddDays(1), UsageWeek.Of(hits, Now).End);
        Assert.Equal(Now.AddDays(3), UsageWeek.Of([hits[1]], Now).End);
    }

    [Fact]
    public void A_share_counts_nothing_recorded_after_the_total_was_read()
    {
        var reset = Now.AddDays(2);
        var requests = new[] { Request("a", Now.AddHours(-1), 10_000_000), Request("b", Now.AddHours(-2), 10_000_000, session: "another") };
        var usage = WeeklyUsage.Of(requests, [Hit(AgentLimitKind.Weekly, Now.AddDays(-1), reset)], Now);

        var share = usage.ShareOfSession(Owner, [.. requests, Request("later", Now.AddHours(1), 80_000_000)]);

        Assert.Equal(0.5, share!.OfWeek, 6);
    }

    [Fact]
    public void A_reset_that_has_passed_rolls_forward_a_week_at_a_time()
    {
        var reset = Now.AddDays(-10);

        var week = UsageWeek.Of([Hit(AgentLimitKind.Weekly, reset.AddDays(-1), reset)], Now);

        Assert.Equal(new UsageWeek(Now.AddDays(-3), Now.AddDays(4), true), week);
    }

    [Fact]
    public void A_reset_exactly_now_starts_the_next_week()
    {
        var week = UsageWeek.Of([Hit(AgentLimitKind.Weekly, Now.AddDays(-1), Now)], Now);

        Assert.Equal(new UsageWeek(Now, Now.AddDays(7), true), week);
    }

    [Fact]
    public void A_refusal_without_a_reset_does_not_place_the_week()
    {
        var week = UsageWeek.Of([Hit(AgentLimitKind.Weekly, Now.AddHours(-1))], Now);

        Assert.False(week.FromReset);
    }

    [Fact]
    public void The_weekly_limit_is_the_cost_reached_at_the_most_recent_refusal_inside_its_week()
    {
        var reset = Now.AddDays(1);
        var hitAt = Now.AddHours(-2);
        var requests = new[]
        {
            Request("before-week", reset.AddDays(-7).AddMinutes(-1), 9_000_000),
            Request("a", reset.AddDays(-6), 30_000_000),
            Request("b", hitAt.AddMinutes(-5), 20_000_000, model: "claude-fable-1"),
            Request("b", hitAt.AddMinutes(-5), 20_000_000, model: "claude-fable-1"),
            Request("after-hit", hitAt.AddMinutes(1), 7_000_000)
        };
        var hits = new[]
        {
            Hit(AgentLimitKind.Weekly, Now.AddDays(-12), Now.AddDays(-8)),
            Hit(AgentLimitKind.Weekly, hitAt, reset)
        };

        var limit = WeeklyLimit.Calibrate(AgentLimitKind.Weekly, hits, requests);

        Assert.Equal(new WeeklyLimit(AgentLimitKind.Weekly, hitAt, 50_000_000), limit);
    }

    [Fact]
    public void Fables_limit_counts_Fable_cost_only()
    {
        var hitAt = Now.AddHours(-1);
        var requests = new[]
        {
            Request("opus", hitAt.AddHours(-1), 40_000_000),
            Request("fable", hitAt.AddHours(-2), 15_000_000, model: "claude-fable-1"),
            Request("fable-1m", hitAt.AddHours(-3), 5_000_000, model: "fable[1m]")
        };

        var limit = WeeklyLimit.Calibrate(AgentLimitKind.WeeklyFable, [Hit(AgentLimitKind.WeeklyFable, hitAt)], requests);

        Assert.Equal(new WeeklyLimit(AgentLimitKind.WeeklyFable, hitAt, 20_000_000), limit);
    }

    [Fact]
    public void No_refusal_or_no_reported_cost_before_it_sizes_no_limit()
    {
        var requests = new[] { Request("a", Now.AddHours(-1), null) };

        Assert.Null(WeeklyLimit.Calibrate(AgentLimitKind.Weekly, [], requests));
        Assert.Null(WeeklyLimit.Calibrate(AgentLimitKind.Weekly, [Hit(AgentLimitKind.Weekly, Now)], requests));
        Assert.Null(WeeklyLimit.Calibrate(AgentLimitKind.Weekly, [Hit(AgentLimitKind.WeeklyFable, Now)], [Request("b", Now.AddHours(-1), 1_000)]));
        Assert.Null(WeeklyLimit.Calibrate(AgentLimitKind.FiveHour, [Hit(AgentLimitKind.FiveHour, Now)], [Request("b", Now.AddHours(-1), 1_000)]));
    }

    [Fact]
    public void A_session_shows_its_share_of_the_weeks_reported_cost()
    {
        var requests = new[]
        {
            Request("mine", Now.AddDays(-1), 25_000_000),
            Request("mine-last-week", Now.AddDays(-8), 100_000_000),
            Request("theirs", Now.AddDays(-2), 75_000_000, session: "another")
        };

        var usage = WeeklyUsage.Of(requests, [], Now);
        var share = usage.ShareOfSession(Owner, requests);

        Assert.Equal(100_000_000, usage.TotalUsdMicros);
        Assert.Equal(new UsageShare(0.25, null, null), share);
    }

    [Fact]
    public void Once_a_weekly_refusal_is_recorded_the_share_carries_a_percentage_of_the_limit()
    {
        var reset = Now.AddDays(3);
        var hitAt = Now.AddDays(-1);
        var requests = new[]
        {
            Request("earlier", hitAt.AddHours(-1), 200_000_000, session: "another"),
            Request("mine", Now.AddHours(-1), 20_000_000),
            Request("mine-fable", Now.AddHours(-4), 10_000_000, model: "claude-fable-1")
        };
        var hits = new[]
        {
            Hit(AgentLimitKind.Weekly, hitAt, reset),
            Hit(AgentLimitKind.WeeklyFable, Now.AddHours(-3), reset)
        };

        var usage = WeeklyUsage.Of(requests, hits, Now);
        var share = usage.ShareOfSession(Owner, requests)!;

        Assert.Equal(200_000_000, usage.Limit!.UsdMicros);
        Assert.Equal(10_000_000, usage.FableLimit!.UsdMicros);
        Assert.Equal(30 / 230d, share.OfWeek, 6);
        Assert.Equal(30 / 200d, share.OfLimit!.Value, 6);
        Assert.Equal(1d, share.FableOfLimit!.Value, 6);
    }

    [Fact]
    public void Nothing_reported_inside_the_week_is_no_share()
    {
        var requests = new[] { Request("old", Now.AddDays(-9), 5_000_000), Request("unpriced", Now.AddHours(-1), null) };

        var usage = WeeklyUsage.Of(requests, [], Now);

        Assert.Null(usage.ShareOfSession(Owner, requests));
        Assert.Null(usage.ShareOfSession(null, requests));
    }

    [Fact]
    public void A_run_shows_the_share_of_the_requests_its_stages_claim()
    {
        var start = Now.AddHours(-2);
        var stage = new DeliveryRunStage("Implement", "done", 600_000, 1)
        {
            StartedAt = start,
            CompletedAt = start.AddMinutes(10),
            Execution = """{"mode":"inline","model":"claude-opus-5-5"}"""
        };
        var run = new DeliveryRun(
            "run-1", "backlog", "wt-1a2b3c4d", "wt", "machine", "Machine", "flow-code", "A run", "done", null, [],
            start, start.AddMinutes(10), [stage], null, null, [], [])
        {
            SessionIds = [Owner]
        };
        var requests = new[]
        {
            Request("in-run", start.AddMinutes(5), 10_000_000),
            Request("after-run", start.AddMinutes(30), 30_000_000),
            Request("theirs", start.AddMinutes(5), 60_000_000, session: "another")
        };

        var share = WeeklyUsage.Of(requests, [], Now).ShareOfRun(run, requests);

        Assert.Equal(new UsageShare(0.1, null, null), share);
    }

    [Theory]
    [InlineData(0, "0%")]
    [InlineData(0.0004, "< 0.1%")]
    [InlineData(0.042, "4.2%")]
    [InlineData(0.05, "5%")]
    [InlineData(0.123, "12%")]
    [InlineData(1.5, "150%")]
    public void A_share_reads_as_a_percentage(double fraction, string expected) =>
        Assert.Equal(expected, UsageShare.Percent(fraction));
}
