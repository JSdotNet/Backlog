using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Tasks;

/// <summary>
/// The state of the sessions a task links to, answered out of the Sessions context's
/// own port.
/// <para>
/// A cross-context join, and it lives here for the reason the Dashboard's and the
/// Roadmap's next door do: the backlog's pane may not reference Sessions' published
/// surface, so an adapter that may see both answers Tasks' port. It is a mapping and
/// nothing else — whether a session is stalled is the Sessions context's call, made
/// against its own threshold, and is passed on unchanged.
/// </para>
/// <para>
/// It reads the newest-per-agent inventory, the reading the Sessions pane itself
/// shows. A live session is always in it; a finished one old enough to have dropped
/// out of it is absent, and its badge draws without a state, as every badge did
/// before this existed.
/// </para>
/// </summary>
public sealed class LinkedSessionStates(IAgentSessionSource sessions) : ILinkedSessionStates
{
    public async Task<IReadOnlyDictionary<string, LinkedSessionState>> StatesOfAsync(
        IReadOnlyCollection<string> sessionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);

        var answer = new Dictionary<string, LinkedSessionState>(StringComparer.OrdinalIgnoreCase);
        if (sessionIds.Count == 0) return answer;

        AgentSessionCatalog catalog;
        try
        {
            catalog = await sessions.GetSessionsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Unreadable is not a state: the badges stay as they were drawn.
            return answer;
        }

        var held = new Dictionary<string, AgentSessionState>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in catalog.Sessions)
        {
            held.TryAdd(session.Id, session.State);
        }

        foreach (var id in sessionIds)
        {
            if (held.TryGetValue(id, out var state)) answer[id] = Map(state);
        }

        return answer;
    }

    private static LinkedSessionState Map(AgentSessionState state) => state switch
    {
        AgentSessionState.Stalled => LinkedSessionState.Stalled,
        AgentSessionState.Finished => LinkedSessionState.Finished,
        _ => LinkedSessionState.Running
    };
}
