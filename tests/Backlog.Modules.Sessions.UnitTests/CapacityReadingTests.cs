namespace Backlog.Modules.Sessions.UnitTests;

/// <summary>
/// The Capacity Reading a loop checks before it starts another session (local ADR 0051 §4,
/// <c>.devbook/domain/sessions/domain.md#capacity-reading</c>): its dispatched sessions
/// working, plan usage, CPU and free memory, each against its limit, and the limits that
/// hold it back.
/// </summary>
public sealed class CapacityReadingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private const string Manager = "manager-1";

    private static SessionRequest Request(SessionRequestState state, string? sessionId, string? claimedBy = Manager) =>
        new(Guid.NewGuid(), SessionRequestKind.Entry, Guid.NewGuid(), null, "owner/repo", @"D:\repo", state, claimedBy, null, sessionId, Now.AddHours(-1));

    private static AgentSession Session(string desktopId, AgentSessionState state) =>
        new($"id-{desktopId}", AgentSessionKind.Claude, "env", "This PC", "A session", @"D:\repo", "owner/repo", "main", Now.AddHours(-1), Now, state, null, AgentSessionOrigin.Local)
        {
            DesktopSessionId = desktopId
        };

    private static ClaudeApiRequest ApiRequest(string id, long micros, string model = "claude-opus-5-5") =>
        new(id, "s", Now.AddHours(-1), model, "high", micros, 10, 100, 1000, 100, 500, "repl_main_thread", null, null, null);

    private static AgentLimitHit Hit(AgentLimitKind kind, DateTimeOffset at) => new(at, kind, null) { ResetsAt = at.AddDays(1) };

    private static CapacityReading Read(
        IReadOnlyList<SessionRequest>? requests,
        IReadOnlyList<AgentSession>? sessions,
        CapacityLimits? limits = null,
        string? dispatcher = null) =>
        CapacityReadings.Read(requests, sessions, null, null, null, limits ?? CapacityLimits.Default, dispatcher);

    private static CapacityReading Figures(int? working = 0, double? usage = null, double? cpu = null, double? free = null) =>
        new(working, usage, cpu, free, CapacityLimits.Default);

    [Fact]
    public void The_default_limits_are_three_sessions_80_percent_usage_80_percent_cpu_and_4_GB_free()
    {
        Assert.Equal(new CapacityLimits(3, 80, 80, 4), CapacityLimits.Default);
    }

    [Fact]
    public void Each_default_limit_can_be_overridden()
    {
        var limits = CapacityLimits.Default with { Sessions = 5, UsagePercent = 90, CpuPercent = 70, MinFreeGb = 2 };

        Assert.Equal(new CapacityLimits(5, 90, 70, 2), limits);
    }

    [Fact]
    public void Running_and_stalled_sessions_of_started_or_linked_requests_count_as_working()
    {
        var requests = new[]
        {
            Request(SessionRequestState.Started, "local_a"),
            Request(SessionRequestState.Linked, "LOCAL_B"),
            Request(SessionRequestState.Started, "local_c")
        };
        var sessions = new[]
        {
            Session("local_a", AgentSessionState.Running),
            Session("local_b", AgentSessionState.Stalled),
            Session("local_c", AgentSessionState.Running)
        };

        Assert.Equal(3, Read(requests, sessions).WorkingSessions);
    }

    [Fact]
    public void Three_dispatched_sessions_all_waiting_leave_room_at_a_limit_of_three()
    {
        var requests = new[]
        {
            Request(SessionRequestState.Started, "local_a"),
            Request(SessionRequestState.Linked, "local_b"),
            Request(SessionRequestState.Linked, "local_c")
        };
        var sessions = new[]
        {
            Session("local_a", AgentSessionState.Waiting),
            Session("local_b", AgentSessionState.Waiting),
            Session("local_c", AgentSessionState.Waiting)
        };

        var reading = Read(requests, sessions);

        Assert.Equal(0, reading.WorkingSessions);
        Assert.True(reading.HasRoom);
    }

    [Fact]
    public void Finished_unmatched_and_undispatched_sessions_do_not_count()
    {
        var requests = new[]
        {
            Request(SessionRequestState.Started, "local_done"),
            Request(SessionRequestState.Started, "local_gone"),
            Request(SessionRequestState.Requested, "local_queued"),
            Request(SessionRequestState.Claimed, "local_claimed"),
            Request(SessionRequestState.Cancelled, "local_cancelled"),
            Request(SessionRequestState.Started, null)
        };
        var sessions = new[]
        {
            Session("local_done", AgentSessionState.Finished),
            Session("local_queued", AgentSessionState.Running),
            Session("local_claimed", AgentSessionState.Running),
            Session("local_cancelled", AgentSessionState.Running),
            Session("local_by_hand", AgentSessionState.Running)
        };

        Assert.Equal(0, Read(requests, sessions).WorkingSessions);
    }

    [Fact]
    public void A_session_two_requests_name_counts_once()
    {
        var requests = new[] { Request(SessionRequestState.Linked, "local_a"), Request(SessionRequestState.Started, "local_a") };

        Assert.Equal(1, Read(requests, [Session("local_a", AgentSessionState.Running)]).WorkingSessions);
    }

    [Fact]
    public void Given_a_dispatcher_only_the_requests_it_claimed_count()
    {
        var requests = new[]
        {
            Request(SessionRequestState.Started, "local_mine"),
            Request(SessionRequestState.Started, "local_theirs", claimedBy: "manager-2")
        };
        var sessions = new[] { Session("local_mine", AgentSessionState.Running), Session("local_theirs", AgentSessionState.Running) };

        Assert.Equal(1, Read(requests, sessions, dispatcher: Manager).WorkingSessions);
        Assert.Equal(2, Read(requests, sessions).WorkingSessions);
    }

    [Fact]
    public void Without_requests_or_sessions_the_working_figure_is_unavailable()
    {
        Assert.Null(Read(null, [Session("local_a", AgentSessionState.Running)]).WorkingSessions);
        Assert.Null(Read([Request(SessionRequestState.Started, "local_a")], null).WorkingSessions);
    }

    [Fact]
    public void Plan_usage_is_the_higher_of_the_weekly_and_Fable_shares_of_their_limits()
    {
        // The all-models refusal sized its limit at $10 and Fable's at $2.50. This week:
        // $4 in all, $1.50 of it Fable — 40 % and 60 % of the two limits.
        var calibration = new[] { ApiRequest("old", 10_000_000) with { Timestamp = Now.AddDays(-20) } };
        var fableCalibration = new[] { ApiRequest("old-fable", 2_500_000, "claude-fable-1") with { Timestamp = Now.AddDays(-15) } };
        var hits = new[] { Hit(AgentLimitKind.Weekly, Now.AddDays(-19)), Hit(AgentLimitKind.WeeklyFable, Now.AddDays(-14)) };
        var week = new[] { ApiRequest("a", 2_500_000), ApiRequest("b", 1_500_000, "claude-fable-1") };
        IReadOnlyList<ClaudeApiRequest> all = [.. calibration, .. fableCalibration, .. week];
        var weekly = WeeklyUsage.Of(all, hits, Now);

        var reading = CapacityReadings.Read([], [], weekly, all, null, CapacityLimits.Default);

        Assert.Equal(60, reading.PlanUsagePercent!.Value, 6);
    }

    [Fact]
    public void Plan_usage_is_unavailable_until_a_weekly_refusal_sizes_a_limit()
    {
        IReadOnlyList<ClaudeApiRequest> week = [ApiRequest("a", 4_000_000)];
        var weekly = WeeklyUsage.Of(week, [], Now);

        Assert.Null(CapacityReadings.Read([], [], weekly, week, null, CapacityLimits.Default).PlanUsagePercent);
        Assert.Null(CapacityReadings.Read([], [], null, week, null, CapacityLimits.Default).PlanUsagePercent);
        Assert.Null(CapacityReadings.Read([], [], weekly, null, null, CapacityLimits.Default).PlanUsagePercent);
    }

    [Fact]
    public void The_machine_load_is_carried_and_without_one_cpu_and_memory_are_unavailable()
    {
        var loaded = CapacityReadings.Read([], [], null, null, new MachineLoad(42.5, 7.25), CapacityLimits.Default);
        var unloaded = CapacityReadings.Read([], [], null, null, null, CapacityLimits.Default);

        Assert.Equal((42.5, 7.25), (loaded.CpuPercent, loaded.FreeMemoryGb));
        Assert.Null(unloaded.CpuPercent);
        Assert.Null(unloaded.FreeMemoryGb);
    }

    [Fact]
    public void The_reading_carries_the_limits_it_was_read_against()
    {
        var limits = CapacityLimits.Default with { Sessions = 1 };

        Assert.Equal(limits, Read([], [], limits).Limits);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    public void Sessions_hold_at_the_limit(int working, bool held)
    {
        Assert.Equal(held, Figures(working: working).HeldBy().Contains(CapacityLimit.Sessions));
    }

    [Theory]
    [InlineData(79.9, false)]
    [InlineData(80.0, true)]
    public void Usage_holds_at_the_limit(double usage, bool held)
    {
        Assert.Equal(held, Figures(usage: usage).HeldBy().Contains(CapacityLimit.Usage));
    }

    [Theory]
    [InlineData(79.9, false)]
    [InlineData(80.0, true)]
    public void Cpu_holds_at_the_limit(double cpu, bool held)
    {
        Assert.Equal(held, Figures(cpu: cpu).HeldBy().Contains(CapacityLimit.Cpu));
    }

    [Theory]
    [InlineData(4.0, false)]
    [InlineData(3.99, true)]
    public void Memory_holds_below_the_minimum_free(double free, bool held)
    {
        Assert.Equal(held, Figures(free: free).HeldBy().Contains(CapacityLimit.Memory));
    }

    [Fact]
    public void An_unavailable_figure_holds_nothing_back()
    {
        var reading = Figures(working: null, usage: null, cpu: null, free: null);

        Assert.Empty(reading.HeldBy());
        Assert.True(reading.HasRoom);
    }

    [Fact]
    public void Every_limit_reached_is_named_in_order_sessions_usage_cpu_memory()
    {
        var reading = Figures(working: 3, usage: 95, cpu: 99, free: 1);

        Assert.Equal([CapacityLimit.Sessions, CapacityLimit.Usage, CapacityLimit.Cpu, CapacityLimit.Memory], reading.HeldBy());
        Assert.False(reading.HasRoom);
    }

    [Fact]
    public void A_reading_under_every_limit_has_room()
    {
        Assert.True(Figures(working: 2, usage: 50, cpu: 30, free: 16).HasRoom);
    }
}
