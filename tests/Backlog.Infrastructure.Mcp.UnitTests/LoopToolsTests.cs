using Backlog.Modules.Sessions.Abstractions;
using Backlog.Tests;

using Microsoft.Extensions.Time.Testing;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// <c>check_in</c> (local ADR 0051 §3): a check-in is recorded per loop session, its answer
/// carries the command the person last set — none by default — and a dispatcher's answer
/// carries the session requests and answer deliveries waiting.
/// </summary>
public class LoopToolsTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Noon);
    private readonly InMemoryLoopRegister _register;
    private readonly InMemorySessionRequestStore _requests;
    private readonly HeldWaitingQuestions _questions = new();
    private readonly LoopTools _tools;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public LoopToolsTests()
    {
        _register = new InMemoryLoopRegister(_clock);
        _requests = new InMemorySessionRequestStore(_clock);
        _tools = new LoopTools(_register, _clock, _requests, _questions);
    }

    [Fact]
    public async Task A_first_check_in_is_recorded_and_carries_no_command()
    {
        var answer = await _tools.CheckInAsync("dispatcher", " manager-session ", "listening", 30, requests: 1, sessions: 2, cancellationToken: Ct);

        Assert.Equal("manager-session", answer.SessionId);
        Assert.Equal("dispatcher", answer.Kind);
        Assert.Null(answer.Command);
        Assert.Null(answer.CommandSetAt);
        Assert.Equal(Noon, answer.CheckedInAt);
        Assert.Equal(Noon + TimeSpan.FromMinutes(40), answer.StalledAfter);

        var stored = await _register.GetAsync("manager-session", Ct);
        Assert.NotNull(stored);
        Assert.Equal(LoopActivity.Listening, stored.CheckIn.Activity);
        Assert.Equal(new LoopHeldWork(1, 0, 2, 0), stored.CheckIn.Held);
    }

    /// <summary>The round trip: the person sets a command through the monitor a screen
    /// uses, and the loop's next check-in carries it until they set another.</summary>
    [Fact]
    public async Task The_command_the_person_sets_reaches_every_next_check_in()
    {
        var monitor = new LoopSessionMonitor(_register, clock: _clock);
        await _tools.CheckInAsync("dispatcher", "manager-session", "listening", 5, cancellationToken: Ct);

        _clock.Advance(TimeSpan.FromMinutes(1));
        await monitor.SetCommandAsync("manager-session", LoopCommand.Pause, Ct);
        _clock.Advance(TimeSpan.FromMinutes(1));

        var paused = await _tools.CheckInAsync("dispatcher", "manager-session", "listening", 5, cancellationToken: Ct);
        var stillPaused = await _tools.CheckInAsync("dispatcher", "manager-session", "listening", 5, cancellationToken: Ct);

        Assert.Equal("pause", paused.Command);
        Assert.Equal(Noon + TimeSpan.FromMinutes(1), paused.CommandSetAt);
        Assert.Equal("pause", stillPaused.Command);

        await monitor.SetCommandAsync("manager-session", LoopCommand.Resume, Ct);
        Assert.Equal("resume", (await _tools.CheckInAsync("dispatcher", "manager-session", "working", 5, cancellationToken: Ct)).Command);

        await monitor.SetCommandAsync("manager-session", LoopCommand.Stop, Ct);
        Assert.Equal("stop", (await _tools.CheckInAsync("dispatcher", "manager-session", "listening", 5, cancellationToken: Ct)).Command);
    }

    /// <summary>A dispatch wait moves only the time (local ADR 0051 §3); the next
    /// <c>check_in</c> records everything it says again.</summary>
    [Fact]
    public async Task A_check_in_after_a_wait_records_the_full_check_in_again()
    {
        await _tools.CheckInAsync("dispatcher", "manager-session", "holding", 5, requests: 1, cancellationToken: Ct);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _register.RecordWaitAsync("manager-session", _clock.GetUtcNow(), Ct);
        _clock.Advance(TimeSpan.FromMinutes(1));

        var answer = await _tools.CheckInAsync("dispatcher", "manager-session", "working", 15, sessions: 2, cancellationToken: Ct);

        Assert.Equal(Noon + TimeSpan.FromMinutes(2), answer.CheckedInAt);

        var stored = await _register.GetAsync("manager-session", Ct);
        Assert.NotNull(stored);
        Assert.Equal(LoopActivity.Working, stored.CheckIn.Activity);
        Assert.Equal(15, stored.CheckIn.HeartbeatMinutes);
        Assert.Equal(new LoopHeldWork(0, 0, 2, 0), stored.CheckIn.Held);
        Assert.Equal(Noon + TimeSpan.FromMinutes(2), stored.CheckIn.At);
        Assert.Null(stored.CheckIn.ReportedAt);
    }

    [Fact]
    public async Task A_command_is_kept_per_loop_session()
    {
        var monitor = new LoopSessionMonitor(_register, clock: _clock);
        await _tools.CheckInAsync("dispatcher", "manager-session", "listening", 5, cancellationToken: Ct);
        await _tools.CheckInAsync("brain-tender", "brain-session", "listening", 15, cancellationToken: Ct);

        await monitor.SetCommandAsync("brain-session", LoopCommand.Stop, Ct);

        Assert.Null((await _tools.CheckInAsync("dispatcher", "manager-session", "listening", 5, cancellationToken: Ct)).Command);
        Assert.Equal("stop", (await _tools.CheckInAsync("brain-tender", "brain-session", "working", 15, cancellationToken: Ct)).Command);
    }

    [Fact]
    public async Task A_command_on_a_session_that_never_checked_in_sets_nothing()
    {
        var monitor = new LoopSessionMonitor(_register, clock: _clock);

        Assert.Null(await monitor.SetCommandAsync("nobody", LoopCommand.Pause, Ct));
        Assert.Empty(await _register.ListAsync(Ct));
    }

    [Fact]
    public async Task A_dispatchers_answer_carries_the_requests_and_deliveries_waiting()
    {
        _requests.Add(SessionRequests.ForEntry(Guid.NewGuid(), "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon));
        var claimed = _requests.Add(SessionRequests.ForEntry(Guid.NewGuid(), "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon));
        await _requests.ClaimAsync(claimed.Id, "manager-1", Ct);
        _questions.AddPendingDelivery("asking-session", "Which branch?", "main", Noon);

        var answer = await _tools.CheckInAsync("dispatcher", "manager-session", "listening", 5, cancellationToken: Ct);

        Assert.Equal(new LoopWaitingPayload(SessionRequests: 1, Deliveries: 1), answer.Waiting);
    }

    [Fact]
    public async Task A_brain_tenders_answer_carries_no_queue()
    {
        _requests.Add(SessionRequests.ForEntry(Guid.NewGuid(), "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon));

        var answer = await _tools.CheckInAsync("brain-tender", "brain-session", "working", 15, notes: 3, cancellationToken: Ct);

        Assert.Equal("brain-tender", answer.Kind);
        Assert.Null(answer.Waiting);
    }

    // --- The loop's limits and the app's reading (local ADR 0051 §4) ----------------------

    [Fact]
    public async Task A_check_in_without_limits_holds_the_default_limits()
    {
        await _tools.CheckInAsync("dispatcher", "manager-session", "listening", 5, cancellationToken: Ct);

        Assert.Equal(CapacityLimits.Default, (await _register.GetAsync("manager-session", Ct))!.CheckIn.Limits);
    }

    [Fact]
    public async Task Each_limit_the_loop_sends_replaces_its_default_alone()
    {
        await _tools.CheckInAsync("dispatcher", "manager-session", "listening", 5, maxSessions: 2, minFreeMemoryGb: 8, cancellationToken: Ct);

        Assert.Equal(new CapacityLimits(2, 80, 80, 8), (await _register.GetAsync("manager-session", Ct))!.CheckIn.Limits);
    }

    [Theory]
    [InlineData(0, null, null, null)]
    [InlineData(null, 0.0, null, null)]
    [InlineData(null, 120.0, null, null)]
    [InlineData(null, null, -5.0, null)]
    [InlineData(null, null, null, -1.0)]
    public async Task A_check_in_with_a_limit_out_of_range_is_refused(int? maxSessions, double? maxUsage, double? maxCpu, double? minFree)
    {
        var refused = await Assert.ThrowsAsync<McpException>(() => _tools.CheckInAsync(
            "dispatcher", "manager-session", "listening", 5,
            maxSessions: maxSessions, maxUsagePercent: maxUsage, maxCpuPercent: maxCpu, minFreeMemoryGb: minFree,
            cancellationToken: Ct));

        Assert.Contains("loop.limits", refused.Message, StringComparison.Ordinal);
        Assert.Empty(await _register.ListAsync(Ct));
    }

    /// <summary>The scenario "Plan usage holds the queue": the answer says there is no
    /// room, names the limit reached, and gives every figure beside the loop's limits.</summary>
    [Fact]
    public async Task A_dispatchers_answer_carries_the_capacity_reading_against_its_limits()
    {
        var reader = new MeasuredCapacity(working: 1, usage: 84, cpu: null, free: 11.5);
        var tools = new LoopTools(_register, _clock, _requests, _questions, reader);

        var answer = await tools.CheckInAsync("dispatcher", "manager-session", "listening", 5, maxSessions: 2, maxUsagePercent: 70, cancellationToken: Ct);

        Assert.Equal(new CapacityLimits(2, 70, 80, 4), reader.AskedWith);
        Assert.Null(reader.AskedFor);

        var capacity = answer.Capacity;
        Assert.NotNull(capacity);
        Assert.False(capacity.HasRoom);
        Assert.Equal(["usage"], capacity.HeldBy);
        Assert.Equal(1, capacity.WorkingSessions);
        Assert.Equal(84, capacity.PlanUsagePercent);
        Assert.Null(capacity.CpuPercent);
        Assert.Equal(11.5, capacity.FreeMemoryGb);
        Assert.Equal(new LoopLimitsPayload(Sessions: 2, UsagePercent: 70, CpuPercent: 80, MinFreeGb: 4), capacity.Limits);
    }

    [Fact]
    public async Task Every_limit_reached_is_named_in_order_on_the_wire()
    {
        var tools = new LoopTools(_register, _clock, _requests, _questions, new MeasuredCapacity(working: 3, usage: 90, cpu: 95, free: 1));

        var answer = await tools.CheckInAsync("dispatcher", "manager-session", "listening", 5, cancellationToken: Ct);

        Assert.Equal(["sessions", "usage", "cpu", "memory"], answer.Capacity!.HeldBy);
    }

    /// <summary>The reading and the requests waiting are kept with the check-in, so the
    /// row reads Holding from what the app measured.</summary>
    [Fact]
    public async Task The_reading_and_the_work_waiting_are_stored_with_the_check_in()
    {
        _requests.Add(SessionRequests.ForEntry(Guid.NewGuid(), "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon));
        _requests.Add(SessionRequests.ForEntry(Guid.NewGuid(), "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon));
        var tools = new LoopTools(_register, _clock, _requests, _questions, new MeasuredCapacity(working: 1, usage: 84, cpu: 30, free: 12));

        await tools.CheckInAsync("dispatcher", "manager-session", "listening", 5, cancellationToken: Ct);

        var stored = (await _register.GetAsync("manager-session", Ct))!.CheckIn;
        Assert.Equal(new CapacityReading(1, 84, 30, 12, CapacityLimits.Default), stored.Reading);
        Assert.Equal(2, stored.WorkWaiting);
        Assert.Equal(LoopState.Holding, LoopSessions.StateAt(stored, LoopProcess.Alive, Noon));
    }

    /// <summary>A paused queue hands out no request (local ADR 0038 §3), so nothing it holds
    /// is work waiting: none on the wire, none stored, and a reached limit holds nothing back.
    /// A pending delivery still counts — deliveries flow while the queue is paused.</summary>
    [Fact]
    public async Task A_paused_queue_has_no_work_waiting()
    {
        _requests.Add(SessionRequests.ForEntry(Guid.NewGuid(), "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon));
        _requests.Add(SessionRequests.ForEntry(Guid.NewGuid(), "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon));
        await _requests.SetPausedAsync(true, Ct);
        _questions.AddPendingDelivery("asking-session", "Which branch?", "main", Noon);
        var tools = new LoopTools(_register, _clock, _requests, _questions, new MeasuredCapacity(working: 3, usage: 84, cpu: 30, free: 12));

        var answer = await tools.CheckInAsync("dispatcher", "manager-session", "listening", 5, cancellationToken: Ct);

        Assert.Equal(new LoopWaitingPayload(SessionRequests: 0, Deliveries: 1), answer.Waiting);
        var stored = (await _register.GetAsync("manager-session", Ct))!.CheckIn;
        Assert.Equal(0, stored.WorkWaiting);
        Assert.Equal(LoopState.Listening, LoopSessions.StateAt(stored, LoopProcess.Alive, Noon));
    }

    [Fact]
    public async Task A_brain_tenders_answer_carries_no_capacity_and_nothing_is_measured()
    {
        var reader = new MeasuredCapacity(working: 3, usage: 90, cpu: 95, free: 1);
        var tools = new LoopTools(_register, _clock, _requests, _questions, reader);

        var answer = await tools.CheckInAsync("brain-tender", "brain-session", "working", 15, maxSessions: 5, cancellationToken: Ct);

        Assert.Null(answer.Capacity);
        Assert.Null(reader.AskedWith);
        Assert.Null((await _register.GetAsync("brain-session", Ct))!.CheckIn.Reading);
    }

    [Fact]
    public async Task With_no_capacity_reader_the_answer_carries_no_capacity()
    {
        var answer = await _tools.CheckInAsync("dispatcher", "manager-session", "holding", 5, cancellationToken: Ct);

        Assert.Null(answer.Capacity);
        Assert.Null((await _register.GetAsync("manager-session", Ct))!.CheckIn.Reading);
    }

    /// <summary>A queue that cannot be read never costs the loop its check-in: it is recorded,
    /// the work waiting reads unknown, and the loop's own word decides Holding.</summary>
    [Theory]
    [InlineData("holding", LoopState.Holding)]
    [InlineData("listening", LoopState.Listening)]
    public async Task A_check_in_is_recorded_when_the_queue_cannot_be_read(string state, LoopState expected)
    {
        var tools = new LoopTools(_register, _clock, new UnreadableRequests(), new UnreadableQuestions(), new MeasuredCapacity(working: 3, usage: 84, cpu: 30, free: 12));

        var answer = await tools.CheckInAsync("dispatcher", "manager-session", state, 5, cancellationToken: Ct);

        Assert.Equal(new LoopWaitingPayload(SessionRequests: 0, Deliveries: 0), answer.Waiting);
        Assert.NotNull(answer.Capacity);

        var stored = (await _register.GetAsync("manager-session", Ct))!.CheckIn;
        Assert.Null(stored.WorkWaiting);
        Assert.NotNull(stored.Reading);
        Assert.Equal(expected, LoopSessions.StateAt(stored, LoopProcess.Alive, Noon));
    }

    [Fact]
    public async Task A_check_in_is_recorded_when_the_capacity_cannot_be_measured()
    {
        _requests.Add(SessionRequests.ForEntry(Guid.NewGuid(), "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon));
        var tools = new LoopTools(_register, _clock, _requests, _questions, new UnmeasurableCapacity());

        var answer = await tools.CheckInAsync("dispatcher", "manager-session", "holding", 5, cancellationToken: Ct);

        Assert.Null(answer.Capacity);
        Assert.Equal(1, answer.Waiting!.SessionRequests);

        var stored = (await _register.GetAsync("manager-session", Ct))!.CheckIn;
        Assert.Null(stored.Reading);
        Assert.Equal(1, stored.WorkWaiting);
        Assert.Equal(LoopState.Holding, LoopSessions.StateAt(stored, LoopProcess.Alive, Noon));
    }

    /// <summary>With no queue composed nothing says whether work waits, so a reading with
    /// no room does not turn a holding loop Listening.</summary>
    [Fact]
    public async Task With_no_queue_composed_the_work_waiting_is_unknown_and_the_loops_word_decides()
    {
        var tools = new LoopTools(_register, _clock, capacity: new MeasuredCapacity(working: 3, usage: 84, cpu: 30, free: 12));

        await tools.CheckInAsync("dispatcher", "manager-session", "holding", 5, cancellationToken: Ct);

        var stored = (await _register.GetAsync("manager-session", Ct))!.CheckIn;
        Assert.Null(stored.WorkWaiting);
        Assert.Equal(LoopState.Holding, LoopSessions.StateAt(stored, LoopProcess.Alive, Noon));
    }

    [Theory]
    [InlineData("manager", "listening", 5, "loop.kind")]
    [InlineData("dispatcher", "sleeping", 5, "loop.state")]
    [InlineData("dispatcher", "listening", 0, "loop.check_in")]
    [InlineData("dispatcher", "listening", 1441, "loop.check_in")]
    public async Task A_check_in_with_a_part_it_cannot_read_is_refused(string kind, string state, int heartbeat, string code)
    {
        var refused = await Assert.ThrowsAsync<McpException>(() => _tools.CheckInAsync(kind, "manager-session", state, heartbeat, cancellationToken: Ct));

        Assert.Contains(code, refused.Message, StringComparison.Ordinal);
        Assert.Empty(await _register.ListAsync(Ct));
    }

    [Fact]
    public async Task A_check_in_with_no_session_or_a_negative_count_is_refused()
    {
        await Assert.ThrowsAsync<McpException>(() => _tools.CheckInAsync("dispatcher", "  ", "listening", 5, cancellationToken: Ct));
        await Assert.ThrowsAsync<McpException>(() => _tools.CheckInAsync("dispatcher", "manager-session", "listening", 5, requests: -1, cancellationToken: Ct));

        Assert.Empty(await _register.ListAsync(Ct));
    }

    /// <summary>A capacity reader at the boundary — the machine's load and the week's
    /// telemetry — answering fixed figures and remembering what it was asked with.</summary>
    private sealed class MeasuredCapacity(int? working, double? usage, double? cpu, double? free) : ICapacityReader
    {
        public CapacityLimits? AskedWith { get; private set; }

        public string? AskedFor { get; private set; }

        public Task<CapacityReading> ReadAsync(CapacityLimits limits, string? dispatcher = null, CancellationToken cancellationToken = default)
        {
            AskedWith = limits;
            AskedFor = dispatcher;

            return Task.FromResult(new CapacityReading(working, usage, cpu, free, limits));
        }
    }

    /// <summary>A capacity reader whose store fails, breaking its own never-throws contract.</summary>
    private sealed class UnmeasurableCapacity : ICapacityReader
    {
        public Task<CapacityReading> ReadAsync(CapacityLimits limits, string? dispatcher = null, CancellationToken cancellationToken = default) =>
            throw new IOException("database is locked");
    }

    /// <summary>A session request queue whose database cannot be read.</summary>
    // Differs from InMemorySessionRequestStore: every read throws, as an unreadable database does.
    private sealed class UnreadableRequests : ISessionRequestStore
    {
        private static IOException Locked => new("database is locked");

        public Task<SessionRequest> RequestAsync(SessionRequest candidate, CancellationToken cancellationToken = default) => throw Locked;

        public Task<SessionRequest?> GetAsync(Guid id, CancellationToken cancellationToken = default) => throw Locked;

        public Task<IReadOnlyList<SessionRequest>> ListAsync(SessionRequestState? state = null, CancellationToken cancellationToken = default) => throw Locked;

        public Task<SessionRequest?> NextAsync(CancellationToken cancellationToken = default) => throw Locked;

        public Task<SessionRequestOutcome> ClaimAsync(Guid id, string dispatcher, CancellationToken cancellationToken = default) => throw Locked;

        public Task<SessionRequestOutcome> ReleaseAsync(Guid id, string dispatcher, CancellationToken cancellationToken = default) => throw Locked;

        public Task<SessionRequestOutcome> ReportStartedAsync(Guid id, string dispatcher, string sessionId, CancellationToken cancellationToken = default) => throw Locked;

        public Task<SessionRequestOutcome> CancelAsync(Guid id, CancellationToken cancellationToken = default) => throw Locked;

        public Task<SessionRequestOutcome> MoveAsync(Guid id, int toIndex, CancellationToken cancellationToken = default) => throw Locked;

        public Task<IReadOnlyList<SessionRequest>> LinkAsync(Guid entryId, IReadOnlyCollection<string> planTags, string sessionId, CancellationToken cancellationToken = default) => throw Locked;

        public Task<bool> IsPausedAsync(CancellationToken cancellationToken = default) => throw Locked;

        public Task SetPausedAsync(bool paused, CancellationToken cancellationToken = default) => throw Locked;
    }

    /// <summary>A question relay store whose database cannot be read.</summary>
    // Differs from HeldWaitingQuestions: every read throws, as an unreadable database does.
    private sealed class UnreadableQuestions : IWaitingQuestionStore
    {
        private static IOException Locked => new("database is locked");

        public Task<WaitingQuestion> RegisterAsync(WaitingQuestion candidate, CancellationToken cancellationToken = default) => throw Locked;

        public Task<WaitingQuestion?> GetAsync(Guid id, CancellationToken cancellationToken = default) => throw Locked;

        public Task<IReadOnlyList<WaitingQuestion>> ListAsync(bool openOnly = false, CancellationToken cancellationToken = default) => throw Locked;

        public Task<WaitingQuestionOutcome> ChangeAsync(Guid id, Func<WaitingQuestion, WaitingQuestionOutcome> transition, CancellationToken cancellationToken = default) => throw Locked;
    }
}
