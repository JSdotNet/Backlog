using Backlog.Modules.Sessions.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sessions;

/// <summary>
/// Reads the question relay into the session list
/// (<c>domain.invariants.md#invariant-an-open-waiting-question-makes-a-live-session-waiting</c>):
/// a live session that holds an open waiting question reads Waiting. A session that reads
/// Finished stays Finished and keeps its questions: the desktop app reaps an idle
/// session's process, and an idle session is exactly the one waiting on a prose question.
/// Its end — archived or deleted — closes them, where <see cref="SessionEndWatchingSource"/>
/// notices it.
/// <para>
/// The relay is resolved per reading, so a host that composed none — or one whose
/// relay cannot be read — gets the list as its sources answered it.
/// </para>
/// </summary>
internal sealed class WaitingSessionSource(
    IAgentSessionSource inner,
    Func<QuestionRelay?> relay,
    ILogger? logger = null) : IAgentSessionSource
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
        GetSessionsAsync(AgentSessionQuery.Newest, cancellationToken);

    public async Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default)
    {
        var catalog = await inner.GetSessionsAsync(query, cancellationToken).ConfigureAwait(false);

        if (relay() is not { } questions) return catalog;

        IReadOnlySet<string> waiting;

        try
        {
            waiting = await questions.WaitingSessionsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "The waiting questions could not be read; sessions read as their sources answered.");
            return catalog;
        }

        if (waiting.Count == 0) return catalog;

        var sessions = new List<AgentSession>(catalog.Sessions.Count);

        foreach (var session in catalog.Sessions)
        {
            var state = WaitingQuestions.StateWith(session.State, waiting.Contains(session.Id));
            sessions.Add(state == session.State ? session : session with { State = state });
        }

        return catalog with { Sessions = sessions };
    }
}
