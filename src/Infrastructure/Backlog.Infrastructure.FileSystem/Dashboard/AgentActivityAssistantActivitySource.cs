using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Infrastructure.FileSystem.Dashboard;

/// <summary>
/// The Dashboard's assistant-activity facts, answered out of the Sessions context's own
/// activity port.
/// </summary>
/// <remarks>
/// <para>
/// A cross-context join, and it lives here for the reason
/// <see cref="AgentSessionAssistantSessionSource"/> lives here: a screen renders one
/// context and asks that context's module, so the Dashboard's pane may not reference
/// Sessions' published surface. Nothing in either module can see this class; each still
/// asks only its own port.
/// </para>
/// <para>
/// It is a mapping and nothing else, and everything it might have decided is deferred to
/// the context that owns the decision. <see cref="AgentSessionGroups.Label(AgentSessionKind)"/>
/// decides what an assistant is called; the threshold that ended a run is carried across
/// rather than restated; the Environment→Machine rename happens here, at the seam, rather
/// than either context giving up its own vocabulary. A second copy of any of those here
/// would be a second definition free to drift from the first.
/// </para>
/// <para>
/// No availability method, unlike its sibling, because this port has none. The folders
/// are the same folders the session source already answers for, and a second answer to
/// one question is a second answer free to disagree.
/// </para>
/// </remarks>
public sealed class AgentActivityAssistantActivitySource(IAgentActivitySource activity) : IAssistantActivitySource
{
    public async Task<AssistantActivityReport> GetActivityAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        var log = await activity.GetActivityAsync(since, cancellationToken).ConfigureAwait(false);

        return new AssistantActivityReport(
            [.. log.Sessions.Select(Map)],
            log.Unreadable,
            log.Since,
            log.IdleAfter);
    }

    /// <summary>
    /// One session's activity, in the Dashboard's words. The session's own id crosses
    /// unchanged: it is the key the Dashboard joins its two lists on, and an id rewritten
    /// at the seam would be a join that silently matches nothing.
    /// </summary>
    private static AssistantActivitySession Map(AgentSessionActivity session) =>
        new(
            Id: session.Id,
            MachineId: session.EnvironmentId,
            MachineName: session.Environment,
            Assistant: AgentSessionGroups.Label(session.Kind),
            Active: [.. session.Runs.Select(run => new AssistantActivityInterval(run.StartedAt, run.EndedAt))],
            Waiting: [.. session.Waits.Select(wait => new AssistantActivityInterval(wait.StartedAt, wait.EndedAt))]);
}
