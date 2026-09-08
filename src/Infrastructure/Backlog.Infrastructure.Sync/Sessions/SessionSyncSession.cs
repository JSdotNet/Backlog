using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.Sync.Sessions;

/// <summary>What one session exchange did, for a screen to show and a test to
/// assert.
/// <para>
/// <paramref name="Pulled"/> counts what came back and <paramref name="Applied"/>
/// what was kept, and the two differ on purpose: this device's own records travel
/// the feed like everybody else's and are dropped on arrival, so "pulled 40,
/// applied 0" is what a single-device owner sees every cycle and is entirely
/// healthy. There is no <c>Skipped</c> counterpart to
/// <c>TaskSyncSummary.Skipped</c> here — a session record has no document this
/// build could fail to read, because the service refuses an invalid record for the
/// whole batch at the edge rather than storing one.
/// </para></summary>
public sealed record SessionSyncSummary(int Pushed, int Pulled, int Applied, DateTimeOffset At);

/// <summary>
/// One exchange over the session replica: push what this machine has seen, pull
/// what the other environments reported.
/// <para>
/// The two halves are separate methods as well as a combined one, for the reason
/// <see cref="TaskSyncSession"/> keeps them apart: they fail separately, and a
/// person may want only one of them — pushing works while a cursor is being
/// reissued, and pulling works on a machine that has run no agent today.
/// </para>
/// <para>
/// <strong>Only local records are pushed, and that is a rule about who may write
/// rather than a tidiness.</strong> .arc42/adr/0005 §Session records makes session
/// records single-writer: a session ran on one machine, only that machine holds
/// the evidence, and the service accepts a record only from the machine it names.
/// This device reads its sessions through the same merged port every screen does,
/// so the filter on <see cref="AgentSessionOrigin.Local"/> is what stops it
/// re-publishing another machine's records under its own id — which would attach
/// somebody else's work to this box and, because the watermark would then advance
/// on the replicated stamps, do it again on every cycle for ever.
/// </para>
/// </summary>
public sealed class SessionSyncSession
{
    /// <summary>
    /// How many records go in one push.
    /// <para>
    /// The service caps a batch at 500 and answers
    /// <see cref="SyncErrorCodes.SessionBatchTooLarge"/> above it. This is the same
    /// 200 <c>TaskSyncSession</c> batches at and for the same argument: large
    /// enough that a machine returning from a week away sends a handful of
    /// requests rather than hundreds, small enough that a failure loses one
    /// batch's worth of progress rather than the run's, and well below the cap so
    /// that reaching the cap means a client that stopped batching rather than a
    /// busy week.
    /// </para>
    /// </summary>
    private const int PushBatchSize = 200;

    private readonly SessionSyncClient _client;
    private readonly IAgentSessionSource _sessions;
    private readonly ISessionRepositoryAliases _aliases;
    private readonly ISessionSyncStateStore _state;
    private readonly IReplicatedSessionStore _replica;
    private readonly IDeviceCredentialStore _credentials;
    private readonly TimeProvider _time;

    public SessionSyncSession(
        SessionSyncClient client,
        IAgentSessionSource sessions,
        ISessionRepositoryAliases aliases,
        ISessionSyncStateStore state,
        IReplicatedSessionStore replica,
        IDeviceCredentialStore credentials,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(aliases);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(replica);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(time);

        _client = client;
        _sessions = sessions;
        _aliases = aliases;
        _state = state;
        _replica = replica;
        _credentials = credentials;
        _time = time;
    }

    /// <summary>
    /// Sends every local session that has moved since the watermark.
    /// <para>
    /// The watermark advances to the highest <c>LastActivityAt</c> that was
    /// actually accepted, and never to <see cref="TimeProvider.GetUtcNow"/>. A
    /// session that takes a turn while a batch is in flight carries a stamp
    /// between the two, and a watermark set to now would step over it: that
    /// reading would never be selected again, and every other machine would go on
    /// showing the session as it was before the turn, with nothing failing to say
    /// so.
    /// </para>
    /// <para>
    /// It advances per batch rather than at the end, so a run that dies on the
    /// third batch of five keeps the first two. Resending is free: the replica
    /// keys a session document on the machine id, the agent and the session id, so
    /// a record sent twice lands on the document it already wrote.
    /// </para>
    /// <para>
    /// <strong>And never into the middle of a stamp.</strong> The selection is
    /// strictly greater than the watermark, and <c>LastActivityAt</c> is not
    /// unique — several sessions can fall back on the same file timestamp, and a
    /// machine that has just been restored gets a whole folder of them. A batch
    /// boundary inside a shared stamp therefore has to stop short of it, or every
    /// session still carrying that stamp is excluded from every future run.
    /// </para>
    /// <para>
    /// The whole catalog is read rather than a changed-since query, because there
    /// is no such query to ask: <see cref="IAgentSessionSource"/> reads two agents'
    /// folders and answers with what it found. The filter is therefore here, and
    /// the source's own per-agent cap bounds the work regardless of how long this
    /// device has been away.
    /// </para>
    /// </summary>
    public async Task<Result<SessionSyncSummary>> PushAsync(CancellationToken cancellationToken = default)
    {
        var watermark = _state.Current.PushWatermark;

        var catalog = await _sessions.GetSessionsAsync(cancellationToken).ConfigureAwait(false);

        var pending = catalog.Sessions
            .Where(session => session.Origin == AgentSessionOrigin.Local)
            .Where(session => session.LastActivityAt > watermark)
            .OrderBy(session => session.LastActivityAt)
            .ToList();

        var pushed = 0;

        for (var start = 0; start < pending.Count; start += PushBatchSize)
        {
            var batch = pending.GetRange(start, Math.Min(PushBatchSize, pending.Count - start));

            // The one place a record is built, so "what leaves this machine" has
            // one place to be audited. See SessionRecordMapping.
            var records = batch
                .Select(session => SessionRecordMapping.ToRecord(session, _aliases))
                .ToList();

            var response = await _client.PushAsync(records, cancellationToken).ConfigureAwait(false);
            if (response.IsFailure) return Result.Failure<SessionSyncSummary>(response.Error);

            pushed += response.Value.Accepted;

            if (WatermarkAfter(batch, final: start + batch.Count >= pending.Count) is { } advanced)
            {
                _state.Save(_state.Current with { PushWatermark = advanced });
            }
        }

        return Result.Success(new SessionSyncSummary(pushed, 0, 0, _time.GetUtcNow()));
    }

    /// <summary>
    /// Reads the owner's session feed to its end, keeping each page as it arrives.
    /// <para>
    /// It loops until the service says <c>HasMore</c> is false and
    /// <strong>never infers "caught up" from a short page</strong>. The contract is
    /// explicit that a page size is a hint to the store and not a promise, and the
    /// feed's position advances whether or not a page had anything in it — so an
    /// empty page still hands back a cursor worth keeping, and a page of three
    /// where a hundred were asked for says nothing at all about what is behind it.
    /// </para>
    /// <para>
    /// The cursor is saved after every successful page, so a pull interrupted half
    /// way resumes rather than restarts. The cost of saving eagerly is a page
    /// replayed when a run dies between keeping and saving, and a replayed page
    /// writes the same records over themselves. The cost of saving once at the end
    /// would be a device that never finishes catching up.
    /// </para>
    /// <para>
    /// <strong>This device's own records are dropped.</strong> They travel the feed
    /// like every other machine's, and keeping them would put a second copy of
    /// every local session in the pane — one read off disk and one that had been
    /// round trip, differing in exactly the field that says which
    /// (<see cref="AgentSessionOrigin"/>). They are dropped on the machine id the
    /// service stamped rather than on anything this build could have set, which is
    /// what makes the test unfakeable.
    /// </para>
    /// <para>
    /// A cursor the service will not resume from is recovered from once, exactly as
    /// <see cref="TaskSyncSession.PullAsync"/> recovers: the cursor is forgotten on
    /// disk before the retry and the feed is read from the beginning, which is what
    /// a freshly paired device does anyway. Once and not in a loop, because a
    /// service answering "expired" to a pull that carried no cursor is saying
    /// something starting over cannot fix. <see cref="SyncErrorCodes.SyncCursorNotYours"/>
    /// is deliberately not on the list — a correctly-signed cursor for another
    /// owner's feed is the one event .arc42/adr/0005 §Consequences asks to be loud
    /// about.
    /// </para>
    /// </summary>
    public async Task<Result<SessionSyncSummary>> PullAsync(CancellationToken cancellationToken = default)
    {
        var cursor = _state.Current.PullCursor;
        var self = _credentials.Current?.DeviceId;
        var pulled = 0;
        var applied = 0;
        var startedOver = false;

        while (true)
        {
            var page = await _client
                .PullAsync(cursor, SessionSyncClient.DefaultMaxItems, cancellationToken)
                .ConfigureAwait(false);

            if (page.IsFailure)
            {
                if (startedOver || cursor is null || !Retired(page.Error.Code))
                {
                    return Result.Failure<SessionSyncSummary>(page.Error);
                }

                startedOver = true;
                cursor = null;
                _state.Save(_state.Current with { PullCursor = null });

                // The counts go with the cursor. What was kept before the restart
                // is about to arrive again, and a summary that added the two would
                // tell the person twice as much had arrived as did.
                pulled = 0;
                applied = 0;

                continue;
            }

            pulled += page.Value.Sessions.Count;

            var theirs = page.Value.Sessions
                .Where(entry => self is null || entry.MachineId != self)
                .ToList();

            _replica.Save(theirs);
            applied += theirs.Count;

            cursor = page.Value.Since;
            _state.Save(_state.Current with { PullCursor = cursor });

            if (!page.Value.HasMore) break;
        }

        return Result.Success(new SessionSyncSummary(0, pulled, applied, _time.GetUtcNow()));
    }

    /// <summary>
    /// Push, then pull.
    /// <para>
    /// In that order because the pull is what tells this device it is up to date,
    /// and a pull that ran first would say so while this machine's own sessions
    /// were still unsent. A push that fails stops the exchange rather than being
    /// followed by a pull: the failure is almost always the service being
    /// unreachable, and a second call to say the same thing is a second thing for
    /// a person to read.
    /// </para>
    /// </summary>
    public async Task<Result<SessionSyncSummary>> SyncAsync(CancellationToken cancellationToken = default)
    {
        var push = await PushAsync(cancellationToken).ConfigureAwait(false);
        if (push.IsFailure) return push;

        var pull = await PullAsync(cancellationToken).ConfigureAwait(false);
        if (pull.IsFailure) return pull;

        return Result.Success(new SessionSyncSummary(
            push.Value.Pushed,
            pull.Value.Pulled,
            pull.Value.Applied,
            _time.GetUtcNow()));
    }

    /// <summary>The two answers that mean "that cursor is no longer one you can
    /// resume from", which the device recovers from by forgetting it. Neither says
    /// anything about the owner's records, so starting over loses nothing but the
    /// position — and a record that arrives twice lands on the row it already
    /// wrote.</summary>
    private static bool Retired(string code) =>
        code is SyncErrorCodes.SyncCursorExpired or SyncErrorCodes.SyncCursorMalformed;

    /// <summary>
    /// How far the watermark may move once a batch has been accepted, or null when
    /// it may not move at all.
    /// <para>
    /// <c>TaskSyncSession.WatermarkAfter</c>'s reasoning over this exchange's own
    /// stamp. The last stamp in the batch on the final batch, because everything
    /// selected has then been sent. Anywhere else the highest stamp strictly below
    /// the batch's last, because a session sharing that last stamp may still be
    /// waiting in the next batch and the selection would never offer it again.
    /// </para>
    /// <para>
    /// Null when a whole batch shares one stamp: there is nowhere safe to move to,
    /// so the batch is simply sent again next run. Free, because a resent record
    /// lands on the document it already wrote, and the alternative is losing the
    /// sessions that share it.
    /// </para>
    /// </summary>
    private static DateTimeOffset? WatermarkAfter(IReadOnlyList<AgentSession> batch, bool final)
    {
        if (final) return batch[^1].LastActivityAt;

        var boundary = batch[^1].LastActivityAt;

        return batch
            .Where(session => session.LastActivityAt < boundary)
            .Select(session => (DateTimeOffset?)session.LastActivityAt)
            .Max();
    }
}
