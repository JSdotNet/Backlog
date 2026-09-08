using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Infrastructure.FileSystem.Dashboard;

/// <summary>
/// The Dashboard's assistant-session facts, answered out of the Sessions context's own
/// port.
/// </summary>
/// <remarks>
/// <para>
/// A cross-context join, and it lives here for the reason
/// <c>ModuleBoundaryTests.A_module_ui_asks_only_its_own_modules_published_surface</c>
/// gives in its own failure message: a screen renders one context and asks that
/// context's module, so the Dashboard's pane may not reference Sessions' published
/// surface — "put the join in an adapter that answers both modules' ports instead".
/// This is that adapter, on the same precedent as the Roadmap ones next door. Nothing
/// in either module can see it; each still asks only its own port.
/// </para>
/// <para>
/// It is a mapping and nothing else. Every judgement it passes on is deferred to the
/// context that owns it — <see cref="AgentSessionGroups.Label(AgentSessionKind)"/>
/// decides what an assistant is called and <see cref="AgentSessionLimits.PerAgent"/>
/// decides how many sessions a read stops at — because a second copy of either here is a
/// second definition free to drift from the first. What the Dashboard does with the
/// figures is the Dashboard's; what a session <em>is</em> stays Sessions'.
/// </para>
/// <para>
/// Availability is always available. The failure this port could report is "the source
/// cannot answer at all", and a local read of two folders always can: an agent that was
/// never installed is a folder that is not there, which the catalog already reports as
/// unreadable and the part already says out loud. Reporting a missing folder as an
/// unavailable source would replace a part that says which half of the picture is
/// missing with one that shows nothing.
/// </para>
/// </remarks>
public sealed class AgentSessionAssistantSessionSource(IAgentSessionSource sessions) : IAssistantSessionSource
{
    public Task<InsightAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(InsightAvailability.Available);

    public async Task<AssistantSessionReport> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await sessions.GetSessionsAsync(cancellationToken).ConfigureAwait(false);

        return new AssistantSessionReport(
            [.. catalog.Sessions.Select(Map)],
            catalog.Unreadable,
            catalog.Capped,
            AgentSessionLimits.PerAgent);
    }

    /// <summary>
    /// One session, in the Dashboard's words. The Sessions context calls it an
    /// Environment and the Dashboard calls it a Machine; the rename happens here, at
    /// the seam, rather than either context giving up its own vocabulary.
    /// </summary>
    private static AssistantSession Map(AgentSession session) =>
        new(
            MachineId: session.EnvironmentId,
            MachineName: session.Environment,
            Assistant: AgentSessionGroups.Label(session.Kind),
            StartedAt: session.StartedAt,
            LastActivityAt: session.LastActivityAt)
        {
            // The agent's own identifier, carried across unchanged. It is what lets the
            // Dashboard tell a session that left an activity record from one that left
            // none, and an id rewritten here would be a join that silently matches
            // nothing rather than one that fails.
            Id = session.Id
        };
}
