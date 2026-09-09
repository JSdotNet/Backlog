namespace Backlog.Modules.Dashboard.Abstractions.Services;

/// <summary>One stretch, in the Dashboard's words. Half-open.</summary>
public sealed record AssistantActivityInterval(DateTimeOffset From, DateTimeOffset To);

/// <summary>
/// What one session was doing, reduced to what a utilisation figure is worked out from.
/// </summary>
/// <param name="Id">
/// The session's own identifier, matched against <see cref="AssistantSession.Id"/>. It
/// is carried for one figure and could not be computed without it: a session that left
/// no parsable activity record at all is a real session and has to be counted as one,
/// and the only way to know which sessions those are is to join the two lists by
/// identity.
/// </param>
/// <param name="MachineId">Matched against <see cref="DashboardScope.MachineId"/>.</param>
/// <param name="MachineName">Carried beside the id rather than looked up, for the
/// reason <see cref="AssistantSession.MachineName"/> gives.</param>
/// <param name="Assistant">Claude, Copilot — whatever the source calls the tool. A
/// string for the reason <see cref="AssistantSession.Assistant"/> is one: this module
/// does not own the list of assistants.</param>
/// <param name="Active">Stretches in which an agent was producing. Disjoint within one
/// session and freely overlapping across two, which is the measure rather than a defect
/// in it.</param>
/// <param name="Waiting">Stretches that ended in a person. Always empty for one of the
/// two assistants; see <see cref="IAssistantActivitySource"/>.</param>
public sealed record AssistantActivitySession(
    string Id,
    string MachineId,
    string MachineName,
    string Assistant,
    IReadOnlyList<AssistantActivityInterval> Active,
    IReadOnlyList<AssistantActivityInterval> Waiting);

/// <summary>
/// What one agent a session spawned was doing, reduced to what a concurrency figure is
/// worked out from.
/// </summary>
/// <param name="Id">The agent's own identifier. Carried because it is what makes two
/// overlapping stretches two agents rather than one.</param>
/// <param name="SessionId">The session that spawned it, matched against
/// <see cref="AssistantSession.Id"/>. Carried for the one sentence the surface owes a
/// reader: how many sessions a peak was spread across.</param>
/// <param name="MachineId">Matched against <see cref="DashboardScope.MachineId"/>, so
/// the machine filter drives these figures exactly as it drives the session ones.</param>
/// <param name="MachineName">Carried beside the id, for
/// <see cref="AssistantSession.MachineName"/>'s reason.</param>
/// <param name="Assistant">Claude on every installation there is. A string for
/// <see cref="AssistantActivitySession.Assistant"/>'s reason, and carried rather than
/// assumed so "this figure is Claude's alone" stays something the data says.</param>
/// <param name="Active">Stretches in which this agent was producing. Disjoint within one
/// agent; freely overlapping across two, and always overlapping with its own parent
/// session's — which is why this is a separate list rather than more intervals in the
/// session's own.</param>
public sealed record AssistantActivitySubagent(
    string Id,
    string SessionId,
    string MachineId,
    string MachineName,
    string Assistant,
    IReadOnlyList<AssistantActivityInterval> Active);

/// <summary>
/// Everything one activity read produced.
/// </summary>
/// <param name="Sessions">One entry per session with a parsable record. A session that
/// left none is absent rather than present-and-empty — the session list already says it
/// existed, and the difference is what the surface counts as unrecorded.</param>
/// <param name="Unreadable">The sources that could not be read, by name.</param>
/// <param name="Since">The horizon the source was asked for, so a surface reporting a
/// floor knows where the floor is.</param>
/// <param name="IdleAfter">The gap that ends a run, so the sentence on screen names
/// the source's number rather than a second copy of it.</param>
public sealed record AssistantActivityReport(
    IReadOnlyList<AssistantActivitySession> Sessions,
    IReadOnlyList<string> Unreadable,
    DateTimeOffset Since,
    TimeSpan IdleAfter)
{
    /// <summary>
    /// One entry per agent a session spawned with a parsable record. Empty rather than
    /// absent-per-session: a session that spawned nothing has nothing here and is
    /// unaffected everywhere else.
    /// <para>
    /// A list of its own and not more <see cref="Sessions"/>. Nothing that measures the
    /// sessions may see these, and the separation is the contract rather than a
    /// convenience — <see cref="AssistantActivitySubagent"/> says what folding them in
    /// would cost.
    /// </para>
    /// <para>An init property rather than a fifth parameter; the fixtures construct this
    /// positionally.</para>
    /// </summary>
    public IReadOnlyList<AssistantActivitySubagent> Subagents { get; init; } = [];

    public static AssistantActivityReport Empty { get; } =
        new([], [], DateTimeOffset.MinValue, TimeSpan.Zero);
}

/// <summary>
/// PORT — when the assistants on this installation were actually producing.
/// <para>
/// Separate from <see cref="IAssistantSessionSource"/> rather than a method on it, and
/// deliberately so: that port's own documentation turns on having <em>no window in the
/// signature</em>, because it is a read that already returns everything it will ever
/// return. This one is not that. Its cost scales with how far back it is asked to look
/// — hundreds of megabytes of transcript — so it takes a horizon, and putting a
/// windowed method on a port whose central claim is that it needs no window would
/// falsify the prose rather than extend it.
/// </para>
/// <para>
/// No availability question here. The folders are the same folders
/// <see cref="IAssistantSessionSource.GetAvailabilityAsync"/> already answers for, and
/// a second answer to one question is a second answer free to disagree.
/// </para>
/// <para>
/// <b>What the two assistants can and cannot contribute.</b> One of them marks a prompt
/// in what it writes down, so a gap that prompt ended is a gap the agent spent stopped;
/// the other's own user message lands roughly zero seconds after the event before it, so
/// there is nothing in that format that means "the agent stopped and nothing had answered
/// yet". That assistant therefore contributes
/// <see cref="AssistantActivitySession.Active"/> and never contributes
/// <see cref="AssistantActivitySession.Waiting"/>, and the surface says so rather than
/// presenting one assistant's figure under both names.
/// </para>
/// <para>
/// Nothing in this contract names a Sessions type. The adapter that answers it may see
/// both contexts; nothing in this module may.
/// </para>
/// </summary>
public interface IAssistantActivitySource
{
    /// <summary>Everything since <paramref name="since"/>. Intervals are clipped to it
    /// rather than dropped, so a session that began before the horizon reports the part
    /// of itself inside it.</summary>
    Task<AssistantActivityReport> GetActivityAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default);
}
