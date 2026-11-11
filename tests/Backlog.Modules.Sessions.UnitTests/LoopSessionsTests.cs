using Backlog.Modules.Sessions.Abstractions;
using Backlog.Tests;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Sessions.UnitTests;

/// <summary>
/// The Loop State (local ADR 0051 §3, <c>.devbook/domain/sessions/domain.md#loop-state</c>):
/// Listening or Holding while a loop checks in, Stalled past its heartbeat plus 10 minutes
/// with its process alive, Not running with no process or its session archived — and the
/// monitor a screen reads them through.
/// </summary>
public class LoopSessionsTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LoopCheckIn CheckIn(
        LoopActivity activity = LoopActivity.Listening,
        int heartbeat = 30,
        LoopKind kind = LoopKind.Dispatcher,
        string sessionId = "manager-session") =>
        LoopSessions.CheckIn(sessionId, kind, activity, heartbeat, LoopHeldWork.None, Noon);

    /// <summary>The scenario "A Manager waits for requests": a check-in 40 seconds ago.</summary>
    [Fact]
    public void A_loop_that_checked_in_within_two_minutes_reads_Listening()
    {
        Assert.Equal(LoopState.Listening, LoopSessions.StateAt(CheckIn(), LoopProcess.Alive, Noon + TimeSpan.FromSeconds(40)));
    }

    [Fact]
    public void A_dispatcher_that_says_it_holds_reads_Holding()
    {
        Assert.Equal(LoopState.Holding, LoopSessions.StateAt(CheckIn(LoopActivity.Holding), LoopProcess.Alive, Noon + TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void A_working_loop_reads_Listening()
    {
        Assert.Equal(LoopState.Listening, LoopSessions.StateAt(CheckIn(LoopActivity.Working), LoopProcess.Alive, Noon));
    }

    [Fact]
    public void The_Brain_tender_has_no_queue_so_it_never_reads_Holding()
    {
        var checkIn = CheckIn(LoopActivity.Holding, kind: LoopKind.BrainTender);

        Assert.Equal(LoopState.Listening, LoopSessions.StateAt(checkIn, LoopProcess.Alive, Noon));
    }

    /// <summary>Between 2 minutes and the heartbeat plus 10 minutes a loop keeps the state
    /// it last read.</summary>
    [Theory]
    [InlineData(LoopActivity.Listening, LoopState.Listening)]
    [InlineData(LoopActivity.Holding, LoopState.Holding)]
    public void A_loop_keeps_its_last_state_until_its_heartbeat_plus_ten_minutes(LoopActivity activity, LoopState expected)
    {
        var checkIn = CheckIn(activity, heartbeat: 30);

        Assert.Equal(expected, LoopSessions.StateAt(checkIn, LoopProcess.Alive, Noon + TimeSpan.FromMinutes(3)));
        Assert.Equal(expected, LoopSessions.StateAt(checkIn, LoopProcess.Alive, Noon + TimeSpan.FromMinutes(40)));
    }

    /// <summary>The scenario "A hung loop": a 30-minute heartbeat, 41 minutes quiet, a process.</summary>
    [Fact]
    public void A_live_loop_quiet_past_its_heartbeat_plus_ten_minutes_reads_Stalled()
    {
        var checkIn = CheckIn(heartbeat: 30);

        Assert.Equal(Noon + TimeSpan.FromMinutes(40), LoopSessions.StalledAfter(checkIn));
        Assert.Equal(LoopState.Stalled, LoopSessions.StateAt(checkIn, LoopProcess.Alive, Noon + TimeSpan.FromMinutes(41)));
    }

    [Fact]
    public void A_loop_the_session_list_does_not_hold_reads_by_its_check_ins_alone()
    {
        var checkIn = CheckIn(heartbeat: 5);

        Assert.Equal(LoopState.Listening, LoopSessions.StateAt(checkIn, LoopProcess.Unknown, Noon + TimeSpan.FromMinutes(10)));
        Assert.Equal(LoopState.Stalled, LoopSessions.StateAt(checkIn, LoopProcess.Unknown, Noon + TimeSpan.FromMinutes(16)));
    }

    [Theory]
    [InlineData(LoopProcess.Gone)]
    [InlineData(LoopProcess.Archived)]
    public void A_loop_with_no_process_or_an_archived_session_reads_Not_running(LoopProcess process)
    {
        Assert.Equal(LoopState.NotRunning, LoopSessions.StateAt(CheckIn(), process, Noon));
        Assert.Equal(LoopState.NotRunning, LoopSessions.StateAt(CheckIn(), process, Noon + TimeSpan.FromHours(2)));
    }

    [Theory]
    [InlineData(AgentSessionState.Running, null, LoopProcess.Alive)]
    [InlineData(AgentSessionState.Waiting, false, LoopProcess.Alive)]
    [InlineData(AgentSessionState.Stalled, false, LoopProcess.Alive)]
    [InlineData(AgentSessionState.Finished, false, LoopProcess.Gone)]
    [InlineData(AgentSessionState.Running, true, LoopProcess.Archived)]
    public void A_sessions_state_and_archived_flag_read_as_its_process(AgentSessionState state, bool? archived, LoopProcess expected)
    {
        Assert.Equal(expected, LoopSessions.ProcessOf(Session("manager-session", state, archived)));
    }

    [Fact]
    public void No_session_reads_as_an_unknown_process()
    {
        Assert.Equal(LoopProcess.Unknown, LoopSessions.ProcessOf(null));
    }

    [Theory]
    [InlineData("", 5)]
    [InlineData("manager-session", 0)]
    [InlineData("manager-session", LoopSessions.MaxHeartbeatMinutes + 1)]
    public void A_check_in_needs_a_session_and_a_heartbeat_between_a_minute_and_a_day(string sessionId, int heartbeat)
    {
        Assert.NotNull(LoopSessions.Refusal(sessionId, heartbeat, LoopHeldWork.None));
        Assert.Throws<ArgumentException>(() => LoopSessions.CheckIn(sessionId, LoopKind.Dispatcher, LoopActivity.Listening, heartbeat, LoopHeldWork.None, Noon));
    }

    [Fact]
    public void A_new_check_in_keeps_the_command_and_setting_none_clears_it()
    {
        var first = LoopSessions.Record(null, CheckIn());
        var paused = LoopSessions.Command(first, LoopCommand.Pause, Noon);
        var later = LoopSessions.Record(paused, CheckIn(LoopActivity.Working) with { At = Noon + TimeSpan.FromMinutes(5) });

        Assert.Equal(LoopCommand.None, first.Command);
        Assert.Equal(LoopCommand.Pause, later.Command);
        Assert.Equal(Noon, later.CommandSetAt);
        Assert.Equal(LoopActivity.Working, later.CheckIn.Activity);

        var cleared = LoopSessions.Command(later, LoopCommand.None, Noon + TimeSpan.FromMinutes(6));
        Assert.Equal(LoopCommand.None, cleared.Command);
        Assert.Null(cleared.CommandSetAt);
    }

    [Fact]
    public async Task The_monitor_reads_each_loop_against_the_session_list()
    {
        var clock = new FakeTimeProvider(Noon);
        var register = new InMemoryLoopRegister(clock);
        await register.CheckInAsync(CheckIn(heartbeat: 5, sessionId: "live"), Ct);
        await register.CheckInAsync(CheckIn(heartbeat: 5, sessionId: "hung"), Ct);
        await register.CheckInAsync(CheckIn(heartbeat: 5, sessionId: "archived"), Ct);
        clock.Advance(TimeSpan.FromMinutes(16));
        await register.CheckInAsync(CheckIn(heartbeat: 5, sessionId: "live") with { At = clock.GetUtcNow() }, Ct);

        var monitor = new LoopSessionMonitor(
            register,
            new ListedSessions(
                Session("live", AgentSessionState.Running, false),
                Session("hung", AgentSessionState.Running, false),
                Session("archived", AgentSessionState.Finished, true)),
            clock);

        var read = (await monitor.ReadAsync(Ct)).ToDictionary(reading => reading.Loop.SessionId, reading => reading.State);

        Assert.Equal(LoopState.Listening, read["live"]);
        Assert.Equal(LoopState.Stalled, read["hung"]);
        Assert.Equal(LoopState.NotRunning, read["archived"]);
    }

    private static AgentSession Session(string id, AgentSessionState state, bool? archived) =>
        new(
            id,
            AgentSessionKind.Claude,
            EnvironmentId: "this-machine",
            Environment: "This machine",
            Title: "📡 SESSION DISPATCHER",
            WorkingFolder: @"D:\Repos\Backlog",
            Repository: null,
            Branch: "main",
            StartedAt: Noon - TimeSpan.FromHours(1),
            LastActivityAt: Noon,
            state,
            TurnCount: 3,
            AgentSessionOrigin.Local)
        {
            Archived = archived
        };

    private sealed class ListedSessions(params AgentSession[] sessions) : IAgentSessionSource
    {
        private readonly AgentSessionCatalog _catalog = new(sessions, [], sessions.Length);

        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) => Task.FromResult(_catalog);

        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default) => Task.FromResult(_catalog);
    }
}
