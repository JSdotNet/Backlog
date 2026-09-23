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

    /// <summary>
    /// How many activity intervals go in one push, summed over every record in it.
    /// <para>
    /// A second bound beside the count, because the count stopped bounding the
    /// weight the day a record could carry a thousand intervals: two hundred such
    /// records would be around 19 MB against a service body limit of 1 MB. Four
    /// thousand intervals is about 450 KB on the wire — under half the limit, so
    /// the base records beside them and the JSON around them have room — and a
    /// batch is flushed <em>before</em> the record that would take it over. A
    /// single record always fits alone: the mapping caps each list at
    /// <see cref="SessionRecordLimits.IntervalsPerList"/>, so no record weighs more
    /// than a thousand.
    /// </para>
    /// <para>
    /// Most batches never come near it. A session weighs a handful of intervals on
    /// an ordinary day, so the count cap is the one that splits a busy week and this
    /// one is for the machine that has been folding a long-running agent for a
    /// month.
    /// </para>
    /// </summary>
    private const int PushBatchIntervals = 4_000;

    private readonly SessionSyncClient _client;
    private readonly IAgentSessionSource _sessions;
    private readonly ISessionRepositoryAliases _aliases;
    private readonly ISessionSyncStateStore _state;
    private readonly IReplicatedSessionStore _replica;
    private readonly IDeviceCredentialStore _credentials;
    private readonly TimeProvider _time;
    private readonly SyncActivityLog? _activity;
    private readonly IAgentActivitySource? _agentActivity;

    /// <param name="activity">Where each record that moves is written down by
    /// name, or null on a head with nothing to show one in. A sent record is
    /// named by the local session's title, and a received one by the title it
    /// carries or, from a device that predates the field, by the machine it came
    /// from.</param>
    /// <param name="agentActivity">
    /// Where the runs and waits a record carries come from, or null on a head that
    /// composed no activity source — which then pushes null in both lists, the
    /// record every push carried before the two fields existed.
    /// <para>
    /// This is the merged port every screen reads, not the local reader alone, and
    /// that is acceptable only because the push filters what it answers on
    /// <see cref="AgentSessionActivity.Origin"/>: the merged port includes what
    /// other machines reported, and a record that arrived over the wire must not
    /// go back out under this machine's id. The filter is the same rule the
    /// session filter below enforces, applied to the second list.
    /// </para>
    /// </param>
    public SessionSyncSession(
        SessionSyncClient client,
        IAgentSessionSource sessions,
        ISessionRepositoryAliases aliases,
        ISessionSyncStateStore state,
        IReplicatedSessionStore replica,
        IDeviceCredentialStore credentials,
        TimeProvider time,
        SyncActivityLog? activity = null,
        IAgentActivitySource? agentActivity = null)
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
        _activity = activity;
        _agentActivity = agentActivity;
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
    /// The catalog is read <see cref="AgentSessionQuery.Since"/> the watermark rather
    /// than in the inventory's newest-per-agent shape. That shape is a cap, and a
    /// push selecting from a capped list ships at most a hundred sessions per agent
    /// however many moved — the hundred-and-first this machine ran since the last
    /// cycle would never leave it, and no other machine's count would be right. The
    /// horizon reading is everything past the watermark, and the strict filter below
    /// still decides the edge; the source's inclusive one is a superset of it.
    /// </para>
    /// <para>
    /// The activity read happens once per push, when something is pending, from a
    /// horizon that never clips a pending session — see <see cref="ActivityFor"/>
    /// for why the horizon has to be that early and what each cycle then costs.
    /// </para>
    /// </summary>
    public async Task<Result<SessionSyncSummary>> PushAsync(CancellationToken cancellationToken = default)
    {
        ReconcileIdentity();

        var watermark = _state.Current.PushWatermark;

        var catalog = await _sessions
            .GetSessionsAsync(AgentSessionQuery.Since(watermark), cancellationToken)
            .ConfigureAwait(false);

        var pending = catalog.Sessions
            .Where(session => IsOwn(session.Origin))
            .Where(session => session.LastActivityAt > watermark)
            .OrderBy(session => session.LastActivityAt)
            .ToList();

        var pushed = 0;

        if (pending.Count == 0) return Result.Success(new SessionSyncSummary(pushed, 0, 0, _time.GetUtcNow()));

        var activity = await ActivityFor(pending, cancellationToken).ConfigureAwait(false);

        // The one place a record is built, so "what leaves this machine" has one
        // place to be audited. See SessionRecordMapping.
        var outgoing = pending
            .Select(session => new Outgoing(
                session,
                SessionRecordMapping.ToRecord(
                    session,
                    _aliases,
                    activity.TryGetValue((session.Kind, session.Id), out var found) ? found : null)))
            .ToList();

        var sent = 0;

        foreach (var batch in Batches(outgoing))
        {
            var response = await _client
                .PushAsync([.. batch.Select(item => item.Record)], cancellationToken)
                .ConfigureAwait(false);

            if (response.IsFailure) return Result.Failure<SessionSyncSummary>(response.Error);

            pushed += response.Value.Accepted;
            sent += batch.Count;

            foreach (var item in batch)
            {
                _activity?.Record(SyncDirection.Sent, SyncItemKind.Session, item.Session.Id, item.Session.Title);
            }

            if (WatermarkAfter([.. batch.Select(item => item.Session)], final: sent >= outgoing.Count) is { } advanced)
            {
                _state.Save(_state.Current with { PushWatermark = advanced });
            }
        }

        return Result.Success(new SessionSyncSummary(pushed, 0, 0, _time.GetUtcNow()));
    }

    /// <summary>
    /// Whether this machine may push a session: one it read from its own files, or its
    /// own record of one whose files are gone. Never one that arrived over sync — that
    /// would go out again under this machine's token, attributed to this box.
    /// </summary>
    private static bool IsOwn(AgentSessionOrigin origin) =>
        origin is AgentSessionOrigin.Local or AgentSessionOrigin.Recorded;

    /// <summary>A session about to go and the record built for it, kept together
    /// so the log entry and the watermark read the session while the wire reads
    /// the record.</summary>
    private sealed record Outgoing(AgentSession Session, SessionRecord Record);

    /// <summary>
    /// This machine's own activity for the sessions about to go, keyed the way a
    /// session is identified — the agent and the id together, never the id alone
    /// (<c>.domain/sessions/naming.md#session-identity</c>). Empty on a head with
    /// no activity source.
    /// <para>
    /// <strong>Only local records, and this is where that is enforced for the
    /// second list.</strong> The port is the merged one, so it answers with what
    /// other machines reported as well; a replicated record matched to a local
    /// session by id would go out again under this machine's token, attributed to
    /// this box. The session filter above stops that for rows; this stops it for
    /// the intervals on them.
    /// </para>
    /// <para>
    /// <strong>The horizon must never clip a pending session, because a record
    /// carries the session's whole activity every time it goes.</strong> The
    /// replica keeps one record per session — a later push replaces the earlier
    /// one whole — and the reading device clips to its own window on arrival, so
    /// a record that went out with less than everything would overwrite one that
    /// had more. The failure is the quiet kind: a session with no recorded start
    /// pending alone in a cycle, measured from its own last activity, has every
    /// run ending before that instant clipped away and its transcript skipped on
    /// mtime, so it goes out with null in both lists and downgrades a record that
    /// carried intervals to "no record". So the horizon is the earliest recorded
    /// start among the pending sessions, and the beginning of time where any of
    /// them recorded none — the sources compare against the horizon and never do
    /// arithmetic on it, so <see cref="DateTimeOffset.MinValue"/> is a safe
    /// floor rather than an overflow waiting to happen.
    /// </para>
    /// <para>
    /// What that costs, honestly: a long-lived or start-less session pins the
    /// horizon far back for every cycle it stays pending, and the local source
    /// then probes every transcript whose mtime is inside that window — a
    /// <c>File.Exists</c> and a JSON read of the cache entry per unchanged file,
    /// a parse only for one that moved. That is the same cost the Dashboard already
    /// pays per refresh over its twelve-week horizon, and the alternative — a
    /// horizon that occasionally sends less than the whole record — is a wrong
    /// number on another machine's screen rather than a slower cycle on this one.
    /// </para>
    /// </summary>
    private async Task<Dictionary<(AgentSessionKind Kind, string Id), AgentSessionActivity>> ActivityFor(
        IReadOnlyList<AgentSession> pending,
        CancellationToken cancellationToken)
    {
        var index = new Dictionary<(AgentSessionKind, string), AgentSessionActivity>();

        if (_agentActivity is null) return index;

        var since = pending.Min(session => session.StartedAt ?? DateTimeOffset.MinValue);

        var log = await _agentActivity.GetActivityAsync(since, cancellationToken).ConfigureAwait(false);

        foreach (var record in log.Sessions)
        {
            if (!IsOwn(record.Origin)) continue;

            index.TryAdd((record.Kind, record.Id), record);
        }

        return index;
    }

    /// <summary>
    /// The outgoing records cut into pushes: at most <see cref="PushBatchSize"/>
    /// records and at most <see cref="PushBatchIntervals"/> intervals per push,
    /// whichever is reached first, in the order the records were given.
    /// <para>
    /// The weight check runs before a record is added, so a batch is flushed ahead
    /// of the record that would take it over rather than after — and a batch that
    /// is empty always takes the next record whatever it weighs, which is what
    /// keeps a single heavy record from being unsendable.
    /// </para>
    /// </summary>
    private static IEnumerable<IReadOnlyList<Outgoing>> Batches(IReadOnlyList<Outgoing> outgoing)
    {
        var batch = new List<Outgoing>();
        var weight = 0;

        foreach (var item in outgoing)
        {
            var cost = WeightOf(item.Record);

            if (batch.Count > 0 && (batch.Count == PushBatchSize || weight + cost > PushBatchIntervals))
            {
                yield return batch;

                batch = [];
                weight = 0;
            }

            batch.Add(item);
            weight += cost;
        }

        if (batch.Count > 0) yield return batch;
    }

    /// <summary>How many list items a record carries — both interval lists and its
    /// limit hits together. A hit is a few more fields than an interval and there
    /// are at most a fifth as many, so counting it as one keeps the estimate inside
    /// the margin the batch cap already leaves. The scalars beside them are bounded
    /// by the count cap.</summary>
    private static int WeightOf(SessionRecord record) =>
        (record.Runs?.Count ?? 0) + (record.Waits?.Count ?? 0) + (record.LimitHits?.Count ?? 0);

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
        ReconcileIdentity();

        // Once, for a device that dropped its own records before it kept them: the
        // feed from its start, because the cursor has already passed every one of
        // them. A record that arrives twice lands where it already is.
        if (!_state.Current.KeepsOwnRecords)
        {
            _state.Save(_state.Current with { PullCursor = null, KeepsOwnRecords = true });
        }

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

            // Every record is kept, this machine's own included. The service holds
            // them for a year and the transcript behind one is gone after a month,
            // so the record is the only thing left to answer for a session of this
            // machine's once the assistant has cleaned its files away. The sources
            // that read the held set give way to the local reader for a session it
            // can still read — see CompositeAgentSessionSource. Only the other
            // machines' records count as received.
            _replica.Save(page.Value.Sessions);

            var theirs = page.Value.Sessions
                .Where(entry => self is null || entry.MachineId != self)
                .ToList();

            applied += theirs.Count;

            foreach (var entry in theirs)
            {
                _activity?.Record(
                    SyncDirection.Received, SyncItemKind.Session, entry.Record.SessionId,
                    SessionTitle(entry.Record));
            }

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

    /// <summary>What a record from another machine is called in the log: its own
    /// title, or — from a device that predates the field — what the Sessions screen
    /// groups by: the machine, then the repository and branch where the record
    /// names them.</summary>
    private static string SessionTitle(SessionRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.Title)) return record.Title;

        var where = record.RepositoryAlias is null
            ? null
            : record.Branch is null ? record.RepositoryAlias : $"{record.RepositoryAlias} on {record.Branch}";

        return where is null
            ? $"{record.AgentKind} session on {record.MachineName}"
            : $"{record.AgentKind} session on {record.MachineName} · {where}";
    }

    /// <summary>
    /// Starts the progress over when it was recorded for a different identity —
    /// the same check, for the same reason, as <c>TaskSyncSession.ReconcileIdentity</c>.
    /// This is the half that showed first: a device that forgot its credential and
    /// registered again kept a watermark at the newest session it had ever pushed,
    /// so the new owner was sent nothing older than that, and the second machine
    /// pairing in saw none of the first one's sessions while the first saw all of
    /// the second's. A state with no identity recorded predates the check and is
    /// reset too.
    /// </summary>
    private void ReconcileIdentity()
    {
        if (_credentials.Current is not { } me) return;

        var state = _state.Current;
        if (state.OwnerId == me.OwnerId && state.DeviceId == me.DeviceId) return;

        _state.Save(new SessionSyncState(DateTimeOffset.MinValue, null, me.OwnerId, me.DeviceId));
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
