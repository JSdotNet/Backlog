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
}
