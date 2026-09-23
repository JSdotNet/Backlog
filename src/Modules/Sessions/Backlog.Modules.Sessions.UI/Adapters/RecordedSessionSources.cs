using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Modules.Sessions.UI.Adapters;

/// <summary>
/// This machine's session records, answered as sessions. A contributor to the composite
/// beside the local readers and the replicated source; the composite lets the local
/// reading win wherever there is one, so a record only answers for a session whose
/// files are gone.
/// <para>
/// Independent of sync by construction: it reads the local store and nothing else, and
/// a host without a store contributes nothing rather than failing.
/// </para>
/// </summary>
internal sealed class RecordedAgentSessionSource(IAgentSessionRecordStore? store) : IAgentSessionSource
{
    public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
        GetSessionsAsync(AgentSessionQuery.Newest, cancellationToken);

    public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (store is null) return Task.FromResult(AgentSessionCatalog.Empty);

        cancellationToken.ThrowIfCancellationRequested();

        var held = store.All().Select(record => record.Session).ToList();

        // The same two shapes the local reader answers: everything at or after a
        // horizon, or the newest per agent. A record is a finished session by
        // definition — nothing is left to show it running.
        var sessions = query.Horizon is { } horizon
            ? held.Where(session => session.LastActivityAt >= horizon).ToList()
            : [.. held
                .GroupBy(session => session.Kind)
                .SelectMany(group => group.OrderByDescending(session => session.LastActivityAt).Take(AgentSessionLimits.PerAgent))];

        return Task.FromResult(new AgentSessionCatalog(
            [.. sessions.Select(session => session with { State = AgentSessionState.Finished, Origin = AgentSessionOrigin.Recorded })],
            [],
            query.IsNewest ? held.Count : sessions.Count));
    }
}

/// <summary>
/// This machine's session records, answered as activity: the runs, waits and limit
/// hits its transcripts were last folded into, clipped to the horizon on the local
/// source's terms.
/// </summary>
internal sealed class RecordedAgentActivitySource(IAgentSessionRecordStore? store) : IAgentActivitySource
{
    public Task<AgentActivityLog> GetActivityAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        if (store is null) return Task.FromResult(new AgentActivityLog([], [], since, TimeSpan.Zero));

        cancellationToken.ThrowIfCancellationRequested();

        var sessions = store.All()
            .Select(record => record.Activity)
            .OfType<AgentSessionActivity>()
            .Select(activity => activity with
            {
                Runs = [.. activity.Runs.Where(run => run.EndedAt > since).Select(run => run.StartedAt >= since ? run : new AgentActivityRun(since, run.EndedAt))],
                Waits = [.. activity.Waits.Where(wait => wait.EndedAt > since).Select(wait => wait.StartedAt >= since ? wait : new AgentActivityWait(since, wait.EndedAt))],
                LimitHits = [.. activity.LimitHits.Where(hit => hit.At >= since)],
                Origin = AgentSessionOrigin.Recorded
            })
            .Where(activity => activity.Runs.Count > 0 || activity.Waits.Count > 0)
            .ToList();

        // No opinion on the threshold, like the replicated source: the fold that made
        // these ran when the record was amended.
        return Task.FromResult(new AgentActivityLog(sessions, [], since, TimeSpan.Zero));
    }
}

/// <summary>
/// Reads this machine's own sessions and amends their records. The local readers only —
/// never the composite, so a record is never amended from a replicated reading or from
/// itself.
/// </summary>
internal sealed class SessionRecordKeeper(
    IAgentSessionSource local,
    IAgentActivitySource? localActivity,
    IAgentSessionRecordStore? store,
    TimeProvider time,
    ISessionRecordPublisher? publisher) : ISessionRecordKeeper
{
    /// <summary>How far before the newest record an ordinary update starts reading, so
    /// a session that took a turn while the last update ran is read again.</summary>
    internal static readonly TimeSpan Overlap = TimeSpan.FromHours(1);

    public async Task<SessionRecordUpdate> UpdateAsync(bool everything, CancellationToken cancellationToken = default)
    {
        if (store is null) return SessionRecordUpdate.None;

        var horizon = everything ? DateTimeOffset.MinValue : Newest() is { } newest ? newest - Overlap : DateTimeOffset.MinValue;

        var catalog = await local.GetSessionsAsync(AgentSessionQuery.Since(horizon), cancellationToken).ConfigureAwait(false);
        var sessions = catalog.Sessions.Where(session => session.Origin == AgentSessionOrigin.Local).ToList();

        if (sessions.Count == 0) return await Published(SessionRecordUpdate.None, everything).ConfigureAwait(false);

        // From the earliest start among them, for the push's reason: a record carries
        // the session's whole activity, and a reading clipped at the horizon would amend
        // it with less than the transcript holds.
        var since = sessions.Min(session => session.StartedAt ?? DateTimeOffset.MinValue);
        var activity = new Dictionary<(AgentSessionKind, string), AgentSessionActivity>();

        if (localActivity is not null)
        {
            var log = await localActivity.GetActivityAsync(since, cancellationToken).ConfigureAwait(false);

            foreach (var record in log.Sessions.Where(record => record.Origin == AgentSessionOrigin.Local))
            {
                activity.TryAdd((record.Kind, record.Id), record);
            }
        }

        var now = time.GetUtcNow();

        var update = store.Save(
        [
            .. sessions.Select(session => new AgentSessionRecord(
                session,
                activity.TryGetValue((session.Kind, session.Id), out var found) ? found : null,
                now))
        ]);

        return await Published(update, everything).ConfigureAwait(false);
    }

    private DateTimeOffset? Newest()
    {
        var records = store!.All();

        return records.Count == 0 ? null : records.Max(record => record.Session.LastActivityAt);
    }

    private Task<SessionRecordUpdate> Published(SessionRecordUpdate update, bool everything) =>
        Task.FromResult(everything && publisher is not null ? update with { Published = publisher.RepublishAll() } : update);
}
