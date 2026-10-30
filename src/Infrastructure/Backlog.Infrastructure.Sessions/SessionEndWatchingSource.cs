using System.Collections.Concurrent;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sessions;

/// <summary>
/// The session list as every consumer reads it, watched for a session that ends, which
/// closes its notifications with <see cref="SessionNotificationSubjects.SessionEndedReason"/>.
/// A session ends here when the last reading had it open in the Claude desktop app
/// (<see cref="AgentSession.Archived"/> false) and this one finds it:
/// <list type="bullet">
/// <item><b>archived</b> — <see cref="AgentSession.Archived"/> true; or</item>
/// <item><b>deleted</b> — read off this machine with no desktop record at all, in two
/// readings in a row that could read every folder. One reading is not enough: a desktop
/// record being rewritten reads as no record for a moment, and closing a question for
/// good over that would be closing it on a running session.</item>
/// </list>
/// <para>
/// A session is never called ended for being missing from a reading: a capped or horizon
/// reading leaves sessions out on purpose. And only a session this process saw open can
/// end here; one archived while the app was closed, or before anything read it, is the
/// startup probe's to catch. The desktop app's archive flag is local to this machine and
/// never syncs, so the owner instance — the desktop that published — is the one that
/// reads it.
/// </para>
/// </summary>
internal sealed class SessionEndWatchingSource(
    IAgentSessionSource inner,
    Func<INotifications?> notifications,
    ILogger? logger = null,
    Func<QuestionRelay?>? relay = null) : IAgentSessionSource
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    /// <summary>The sessions the last reading found open, by agent and id; true once a
    /// reading has found one of them without its desktop record.</summary>
    private readonly ConcurrentDictionary<(AgentSessionKind Kind, string Id), bool> _open = new();

    public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
        GetSessionsAsync(AgentSessionQuery.Newest, cancellationToken);

    public async Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default)
    {
        var catalog = await inner.GetSessionsAsync(query, cancellationToken).ConfigureAwait(false);
        var complete = catalog.Unreadable.Count == 0;

        foreach (var session in catalog.Sessions)
        {
            var key = (session.Kind, session.Id);

            switch (session.Archived)
            {
                case false:
                    _open[key] = false;
                    continue;

                case true when _open.TryRemove(key, out _):
                    await CloseAsync(session.Id, cancellationToken).ConfigureAwait(false);
                    continue;

                case null when complete && session.Origin is AgentSessionOrigin.Local && _open.TryGetValue(key, out var missedOnce):
                    if (!missedOnce)
                    {
                        _open[key] = true;
                    }
                    else if (_open.TryRemove(key, out _))
                    {
                        await CloseAsync(session.Id, cancellationToken).ConfigureAwait(false);
                    }

                    continue;
            }
        }

        return catalog;
    }

    private async Task CloseAsync(string id, CancellationToken cancellationToken)
    {
        // The session's end closes its waiting questions too (local ADR 0041), before the
        // subject's notifications, so each closes as session ended rather than unexplained.
        if (relay?.Invoke() is { } questions)
        {
            try
            {
                await questions.CloseForSessionAsync(id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "Closing the waiting questions of session {Session} failed.", id);
            }
        }

        await SessionSubjectEnds.CloseAsync(
            notifications,
            SessionSubjectEnds.Session(id),
            SessionNotificationSubjects.SessionEndedReason,
            _logger,
            cancellationToken).ConfigureAwait(false);
    }
}
