using Backlog.Infrastructure.FileSystem.Dashboard;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The second of the two places in the product that may see both the Sessions context
/// and the Dashboard's — the expensive one. What is worth asserting about it is the same
/// thing as about its sibling: that it is a mapping and not a second opinion. Every
/// judgement it passes on — what an assistant is called, which gap ended a run, what
/// could not be read — is the other context's, so a change there reaches the dashboard
/// rather than being contradicted here.
/// </summary>
public class AgentActivityAssistantActivitySourceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Horizon = Noon.AddDays(-7 * 12);

    /// <summary>
    /// The Sessions context calls it an Environment and the Dashboard calls it a
    /// Machine; the rename happens here, at the seam, rather than either context giving
    /// up its own vocabulary. The id and the name split exactly as they do for a session
    /// row, so the two lists can be filtered by the same control.
    /// </summary>
    [Fact]
    public async Task An_environment_becomes_a_machine_at_the_seam()
    {
        var report = await Source(Activity("claude-1", AgentSessionKind.Claude)).GetActivityAsync(Horizon);

        var session = Assert.Single(report.Sessions);

        Assert.Equal("tower", session.MachineId);
        Assert.Equal("DEV-TOWER", session.MachineName);

        // And the session's own id crosses unchanged: it is the key the Dashboard joins
        // its two lists on, so an id rewritten here would be a join that matches nothing.
        Assert.Equal("claude-1", session.Id);
    }

    /// <summary>
    /// The label comes from the Sessions context rather than from a switch here, so a
    /// third assistant appears on the dashboard the day it appears in the session list.
    /// Asserted against that context's own function rather than against a literal, or
    /// the test would be a second copy of the very thing it is checking is not copied.
    /// </summary>
    [Theory]
    [InlineData(AgentSessionKind.Claude)]
    [InlineData(AgentSessionKind.Copilot)]
    public async Task An_assistant_is_named_by_the_context_that_owns_the_name(AgentSessionKind kind)
    {
        var report = await Source(Activity("one", kind)).GetActivityAsync(Horizon);

        var session = Assert.Single(report.Sessions);

        Assert.Equal(AgentSessionGroups.Label(kind), session.Assistant);
    }

    /// <summary>
    /// The intervals themselves are carried and never re-measured. Anything this class
    /// did to them — merging, rounding, capping a long wait — would be a second opinion
    /// about a figure the other context already formed, and the two would drift.
    /// </summary>
    [Fact]
    public async Task Runs_and_waits_cross_the_seam_unchanged()
    {
        var activity = new AgentSessionActivity(
            "claude-1",
            AgentSessionKind.Claude,
            "tower",
            "DEV-TOWER",
            [new AgentActivityRun(Noon.AddHours(-3), Noon.AddHours(-2)), new AgentActivityRun(Noon.AddHours(-1), Noon)],
            [new AgentActivityWait(Noon.AddHours(-2), Noon.AddHours(-1))]);

        var session = Assert.Single((await Source(activity).GetActivityAsync(Horizon)).Sessions);

        Assert.Equal(
            [(Noon.AddHours(-3), Noon.AddHours(-2)), (Noon.AddHours(-1), Noon)],
            session.Active.Select(interval => (interval.From, interval.To)));

        Assert.Equal(
            [(Noon.AddHours(-2), Noon.AddHours(-1))],
            session.Waiting.Select(interval => (interval.From, interval.To)));
    }

    /// <summary>
    /// The gap that ended a run is a judgement the parser made, and the sentence on
    /// screen names it. Restating the number here would be a second copy free to drift
    /// from the one the figures were actually folded at, which would leave the surface
    /// explaining a figure with someone else's rule.
    /// </summary>
    [Fact]
    public async Task The_threshold_the_source_folded_at_crosses_the_seam()
    {
        var log = new AgentActivityLog([], [], Horizon, TimeSpan.FromMinutes(5));

        var report = await new AgentActivityAssistantActivitySource(new StubAgentActivitySource(log))
            .GetActivityAsync(Horizon);

        Assert.Equal(TimeSpan.FromMinutes(5), report.IdleAfter);

        // And the horizon the read was answered for, so a surface reporting a floor
        // knows where the floor is.
        Assert.Equal(Horizon, report.Since);
    }

    /// <summary>
    /// An agent whose folder could not be read arrives by name, so the part can say
    /// which half of the picture is missing rather than presenting the other half as the
    /// whole.
    /// </summary>
    [Fact]
    public async Task An_unreadable_agent_is_named_on_the_other_side_too()
    {
        var log = new AgentActivityLog([], ["Copilot"], Horizon, TimeSpan.FromMinutes(5));

        var report = await new AgentActivityAssistantActivitySource(new StubAgentActivitySource(log))
            .GetActivityAsync(Horizon);

        Assert.Equal(["Copilot"], report.Unreadable);
    }

    /// <summary>
    /// The horizon is the caller's and travels through untouched. This adapter has no
    /// opinion about how far back to look — that is the cost decision the Dashboard made
    /// and states.
    /// </summary>
    [Fact]
    public async Task The_horizon_the_dashboard_asked_for_is_the_one_the_context_is_asked_for()
    {
        var source = new StubAgentActivitySource(AgentActivityLog.Empty);

        _ = await new AgentActivityAssistantActivitySource(source).GetActivityAsync(Horizon);

        Assert.Equal(Horizon, Assert.Single(source.Asked));
    }

    /// <summary>
    /// The agents a session spawned cross as a list of their own, and the separation is
    /// the contract rather than a convenience: the Dashboard's session figures are all
    /// measured over <c>Sessions</c>, so an agent that arrived in there would not skew one
    /// of them but redefine every one.
    /// </summary>
    [Fact]
    public async Task Subagents_cross_the_seam_as_their_own_list_and_never_as_sessions()
    {
        var report = await Source(
                [Activity("claude-1", AgentSessionKind.Claude)],
                [Subagent("a16156d26373fd0e8", "claude-1")])
            .GetActivityAsync(Horizon);

        var session = Assert.Single(report.Sessions);

        Assert.Equal("claude-1", session.Id);

        var agent = Assert.Single(report.Subagents);

        Assert.Equal("a16156d26373fd0e8", agent.Id);

        // And its stretches cross unchanged, for the reason the sessions' do: anything
        // this class did to them would be a second opinion about a figure the other
        // context already formed.
        Assert.Equal(
            [(Noon.AddHours(-3), Noon.AddHours(-2))],
            agent.Active.Select(interval => (interval.From, interval.To)));
    }

    /// <summary>
    /// The label comes from the Sessions context here exactly as it does for a session
    /// row, so "this figure is Claude's alone" stays something the data says rather than
    /// something a switch in this file decided. Asserted against that context's own
    /// function, or the test would be a second copy of the thing it is checking is not
    /// copied.
    /// </summary>
    [Theory]
    [InlineData(AgentSessionKind.Claude)]
    [InlineData(AgentSessionKind.Copilot)]
    public async Task A_subagents_assistant_is_named_by_the_sessions_context_rather_than_here(
        AgentSessionKind kind)
    {
        var report = await Source(
                [Activity("one", kind)],
                [Subagent("a1", "one", kind)])
            .GetActivityAsync(Horizon);

        Assert.Equal(AgentSessionGroups.Label(kind), Assert.Single(report.Subagents).Assistant);
    }

    /// <summary>
    /// Both identifiers cross untouched, and each is load-bearing. The machine id is what
    /// makes the machine filter drive these figures the way it drives the session ones;
    /// the session id is what lets the surface say how many sessions a peak was spread
    /// across. The Environment→Machine rename happens here as it does for a session row.
    /// </summary>
    [Fact]
    public async Task A_subagents_machine_id_and_session_id_cross_unchanged()
    {
        var report = await Source(
                [Activity("claude-1", AgentSessionKind.Claude)],
                [Subagent("a1", "claude-1")])
            .GetActivityAsync(Horizon);

        var agent = Assert.Single(report.Subagents);

        Assert.Equal("tower", agent.MachineId);
        Assert.Equal("DEV-TOWER", agent.MachineName);
        Assert.Equal("claude-1", agent.SessionId);
    }

    /// <summary>Most profiles have sessions that spawned nothing, and a log with no agents
    /// maps to a report with none rather than to anything the surface has to explain.</summary>
    [Fact]
    public async Task A_log_with_no_subagents_maps_to_a_report_with_none()
    {
        var report = await Source(Activity("claude-1", AgentSessionKind.Claude)).GetActivityAsync(Horizon);

        Assert.Single(report.Sessions);
        Assert.Empty(report.Subagents);
    }

    private static AgentActivityAssistantActivitySource Source(params AgentSessionActivity[] sessions) =>
        new(new StubAgentActivitySource(new AgentActivityLog(sessions, [], Horizon, TimeSpan.FromMinutes(5))));

    /// <summary>A log carrying both lists, for the facts that are about the second one.
    /// The positional builder below stays untouched and the agents arrive through the init
    /// property, exactly as they do out of the source.</summary>
    private static AgentActivityAssistantActivitySource Source(
        AgentSessionActivity[] sessions,
        SubagentActivity[] subagents) =>
        new(new StubAgentActivitySource(
            new AgentActivityLog(sessions, [], Horizon, TimeSpan.FromMinutes(5)) { Subagents = subagents }));

    private static SubagentActivity Subagent(
        string id,
        string sessionId,
        AgentSessionKind kind = AgentSessionKind.Claude) =>
        new(
            Id: id,
            SessionId: sessionId,
            Kind: kind,
            EnvironmentId: "tower",
            Environment: "DEV-TOWER",
            Runs: [new AgentActivityRun(Noon.AddHours(-3), Noon.AddHours(-2))]);

    private static AgentSessionActivity Activity(string id, AgentSessionKind kind) =>
        new(
            Id: id,
            Kind: kind,
            EnvironmentId: "tower",
            Environment: "DEV-TOWER",
            Runs: [new AgentActivityRun(Noon.AddHours(-2), Noon.AddHours(-1))],
            Waits: []);

    private sealed class StubAgentActivitySource(AgentActivityLog log) : IAgentActivitySource
    {
        public List<DateTimeOffset> Asked { get; } = [];

        public Task<AgentActivityLog> GetActivityAsync(
            DateTimeOffset since,
            CancellationToken cancellationToken = default)
        {
            Asked.Add(since);

            return Task.FromResult(log);
        }
    }
}
