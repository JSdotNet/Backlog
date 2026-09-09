namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>
/// One stretch in which an agent was producing: a run of transcript events with no
/// gap in it longer than <see cref="AgentActivityLog.IdleAfter"/>.
/// <para>
/// Half-open. Runs from one session never overlap each other; runs from two sessions
/// freely do, and that is the measure rather than a defect in it.
/// </para>
/// </summary>
public sealed record AgentActivityRun(DateTimeOffset StartedAt, DateTimeOffset EndedAt);

/// <summary>
/// One stretch in which the agent had stopped and nothing had prompted it yet. A gap
/// that ended in a prompt; a gap that ended in anything else is idle or abandoned time
/// and is not one of these.
/// <para>
/// A prompt, not a person, and the distinction is the whole of what this record can
/// honestly claim. Most prompts that end a gap are an SDK or a skill sending the next
/// message rather than somebody typing — 1,623 of 1,728 on the machine this was measured
/// on. A surface that renders these as time a reader kept an agent waiting would be
/// naming a cause this type never established.
/// </para>
/// </summary>
public sealed record AgentActivityWait(DateTimeOffset StartedAt, DateTimeOffset EndedAt);

/// <summary>
/// What one session was doing, as its own transcript records it.
/// </summary>
/// <param name="Id">The agent's own identifier, so an activity record and a session
/// row are the same session by identity.</param>
/// <param name="Kind">Which assistant.</param>
/// <param name="EnvironmentId">ADR 0005's machine id, stamped by the source.</param>
/// <param name="Environment">What that machine is called.</param>
/// <param name="Runs">Ordered, disjoint, ascending.</param>
/// <param name="Waits">
/// Ordered, disjoint, ascending. Empty for every Copilot session, and that is a fact
/// about Copilot rather than about the session — see <see cref="IAgentActivitySource"/>.
/// </param>
public sealed record AgentSessionActivity(
    string Id,
    AgentSessionKind Kind,
    string EnvironmentId,
    string Environment,
    IReadOnlyList<AgentActivityRun> Runs,
    IReadOnlyList<AgentActivityWait> Waits);

/// <summary>
/// What one agent a session spawned was doing, as its own sidechain transcript
/// records it.
/// <para>
/// Beside <see cref="AgentSessionActivity"/> and never one of them. That collection is
/// the session set every figure on the sessions surface is measured over — the active
/// sweep, the waiting sweep, the open sweep, the breakdown, the day counts and the
/// count of sessions with no record. A subagent listed there would not skew one figure;
/// it would silently redefine all eight.
/// </para>
/// <para>
/// No waits, and that is a measurement rather than an omission. A sidechain's user turns
/// are tool results: 135,896 turns across every subagent transcript on the machine this
/// was built against carry no promptSource at all, against sdk 768 / typed 41 /
/// system 37 on the parent side. So the fold can produce no wait here, ever, and an
/// always-empty list would be a claim with nothing behind it. Copilot's empty waits are
/// a fact about Copilot; these would be a fact about nothing.
/// </para>
/// </summary>
/// <param name="Id">The agent id, as the transcript's own filename spells it.</param>
/// <param name="SessionId">The session that spawned it — the folder this transcript is
/// filed under, read off the path rather than out of the file.</param>
/// <param name="Kind">Which assistant spawned it. Claude on every profile anybody has,
/// and carried rather than assumed so the seam names the assistant through
/// AgentSessionGroups.Label instead of keeping a second copy of that decision.</param>
/// <param name="EnvironmentId">ADR 0005's machine id, stamped by the source.</param>
/// <param name="Environment">What that machine is called.</param>
/// <param name="Runs">Ordered, disjoint, ascending. Runs from one subagent never
/// overlap each other; runs from two freely do, and from a subagent and its own parent
/// session they always do — that is the measure rather than a defect in it.</param>
public sealed record SubagentActivity(
    string Id,
    string SessionId,
    AgentSessionKind Kind,
    string EnvironmentId,
    string Environment,
    IReadOnlyList<AgentActivityRun> Runs);

/// <summary>
/// Everything one activity read produced.
/// </summary>
/// <param name="Sessions">One entry per session that had a parsable record inside the
/// horizon. A session with no record at all is absent rather than present-and-empty:
/// the session list already says it existed.</param>
/// <param name="Unreadable">The agents whose folder could not be read, by name.</param>
/// <param name="Since">The horizon this read was asked for, so a surface reporting a
/// floor knows where the floor is.</param>
/// <param name="IdleAfter">
/// The gap that ends a run. Carried rather than known, on
/// <see cref="AgentSessionCatalog.Discovered"/>'s precedent: the threshold is a
/// judgement this source made and a surface that hard-coded the same number would be a
/// second copy of it free to drift. The sentence on screen names it.
/// </param>
public sealed record AgentActivityLog(
    IReadOnlyList<AgentSessionActivity> Sessions,
    IReadOnlyList<string> Unreadable,
    DateTimeOffset Since,
    TimeSpan IdleAfter)
{
    /// <summary>
    /// One entry per agent a session spawned that had a parsable record inside the
    /// horizon. Empty for Copilot, which spawns none.
    /// <para>
    /// An init property rather than a fifth parameter, on
    /// <c>AssistantSessionsInsight.SessionsPerWeek</c>'s precedent: five fixture
    /// builders construct the records on this path positionally.
    /// </para>
    /// </summary>
    public IReadOnlyList<SubagentActivity> Subagents { get; init; } = [];

    public static AgentActivityLog Empty { get; } = new([], [], DateTimeOffset.MinValue, TimeSpan.Zero);
}

/// <summary>
/// Where the agent-activity record on a machine comes from.
/// <para>
/// A second port rather than a second method on <see cref="IAgentSessionSource"/>,
/// and that is the whole point of it. That one stats files; this one reads their
/// bodies — 309 MB and 120,000 lines for a week of Claude on this machine. The
/// session list must never pay that, and a boolean on one method would make "cheap"
/// and "expensive" the same contract with a flag every implementer had to honour.
/// Two ports means the pane cannot accidentally ask.
/// </para>
/// <para>
/// Bounded by time, not by count. <see cref="AgentSessionLimits.PerAgent"/> deliberately
/// does not apply: a capped list of rows is a list a reader scrolls, while a capped
/// duration is a wrong number, and the horizon is a bound the caller chose and can
/// state.
/// </para>
/// <para>
/// <b>What the two agents can and cannot contribute.</b> Claude marks a prompt with a
/// <c>promptSource</c> field, so a gap it ends is a gap the agent spent stopped. Copilot's
/// <c>user.message</c> fires roughly zero seconds after the event before it — measured
/// over 919 of them — so it cannot mark that boundary at all. Copilot therefore
/// contributes runs and never contributes waits, and the surface says so rather than
/// presenting a Claude-only figure as both agents'.
/// <para>
/// <c>promptSource</c> says a prompt arrived, not who sent it: <c>sdk</c> outnumbers
/// <c>typed</c> better than thirty to one here. A wait is therefore the agent's idleness
/// and not a person's delay, which is what the tile over these is named for.
/// </para>
/// </para>
/// </summary>
public interface IAgentActivitySource
{
    /// <summary>Everything since <paramref name="since"/>. Intervals are clipped to
    /// it rather than dropped, so a session that began before the horizon reports the
    /// part of itself inside it.</summary>
    Task<AgentActivityLog> GetActivityAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default);
}
