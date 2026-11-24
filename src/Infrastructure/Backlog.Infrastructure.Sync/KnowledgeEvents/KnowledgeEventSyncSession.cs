using Backlog.Modules.Events.Abstractions.DataTransferObjects;
using Backlog.Modules.Events.Abstractions.Services;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sync.KnowledgeEvents;

/// <summary>What one exchange did, for a screen to show and a test to assert.
/// <paramref name="Pulled"/> counts what came back and <paramref name="Applied"/> what was
/// written; this device's own echo arrives and changes nothing.</summary>
public sealed record KnowledgeEventSyncSummary(int Pushed, int Pulled, int Applied, DateTimeOffset At)
{
    /// <summary>Changes offered in a push the replica did not take, because it already held
    /// that version or a later one — mostly the healthy echo of a copy pulled from another
    /// desktop, as <see cref="DevbookSubjects.DevbookSubjectSyncSummary.Refused"/> explains.</summary>
    public int Refused { get; init; }

    /// <summary>Events or series left on this machine because the service would refuse them,
    /// each named in a warning.</summary>
    public int Skipped { get; init; }

    /// <summary>The sync service answered that it has no knowledge event route: it is older
    /// than this client, and nothing was exchanged. Not a failure — the deployed service is
    /// updated by hand and lags — and nothing moved, so the next cycle after it is updated sends
    /// everything.</summary>
    public bool NotYetServed { get; init; }

    /// <summary>Whether this exchange sent this desktop's <c>events</c> switch (local ADR 0050
    /// §6).</summary>
    public bool AreaSent { get; init; }
}

/// <summary>
/// One exchange with the knowledge event replica (local ADR 0050 §2): push what this device's
/// events and series changed, pull what other desktops changed.
/// <see cref="DevbookSubjects.DevbookSubjectSyncSession"/>'s loop over the knowledge event feed
/// — the watermark rules, the per-page cursor save, the once-only recovery from a retired
/// cursor, the push filter by the service's own rule, and a bare 404 read as "not yet served" —
/// kept as a copy for the reason that one is: the feeds fail separately.
/// <para>
/// Events and series share one watermark: both are read since it, merged into one list in stamp
/// order, and pushed together, so a series goes out beside the edition that names it.
/// </para>
/// </summary>
public sealed class KnowledgeEventSyncSession
{
    /// <summary>How many changes go in one push — the service's own cap
    /// (<c>SyncRequestLimits.MaximumPushKnowledgeEvents</c>), because an event document is far
    /// heavier than a subject state.</summary>
    internal const int PushBatchSize = 50;

    /// <summary>The code <c>SyncHttp</c> gives a 404 whose body carries none — what an older
    /// service answers for a route it does not map.</summary>
    private const string UnmappedRouteCode = "sync.http_404";

    private readonly KnowledgeEventSyncClient _client;
    private readonly KnowledgeEventReplicaMerge _merge;
    private readonly IKnowledgeEventReplication _replication;
    private readonly IKnowledgeEventSyncStateStore _state;
    private readonly IDeviceCredentialStore _credentials;
    private readonly TimeProvider _time;
    private readonly ILogger _log;

    /// <param name="credentials">Whose device this is, checked against the recorded progress
    /// before either half runs.</param>
    /// <param name="log">Where a change left out of a push is named.</param>
    public KnowledgeEventSyncSession(
        KnowledgeEventSyncClient client,
        KnowledgeEventReplicaMerge merge,
        IKnowledgeEventReplication replication,
        IKnowledgeEventSyncStateStore state,
        IDeviceCredentialStore credentials,
        TimeProvider time,
        ILogger<KnowledgeEventSyncSession>? log = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(merge);
        ArgumentNullException.ThrowIfNull(replication);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(time);

        _client = client;
        _merge = merge;
        _replication = replication;
        _state = state;
        _credentials = credentials;
        _time = time;
        _log = log ?? NullLogger<KnowledgeEventSyncSession>.Instance;
    }

    /// <summary>Starts the progress over when it was recorded for a different identity — the
    /// check <c>TaskSyncSession.ReconcileIdentity</c> explains.</summary>
    private void ReconcileIdentity()
    {
        if (_credentials.Current is not { } me) return;

        // The switch is this desktop's, whoever it is paired as: a move not yet sent goes to the new owner.
        _state.Update(state => state.OwnerId == me.OwnerId && state.DeviceId == me.DeviceId
            ? state
            : new KnowledgeEventSyncState(DateTimeOffset.MinValue, null, me.OwnerId, me.DeviceId, state.Area));
    }

    /// <summary>
    /// Sends this desktop's <c>events</c> switch when it moved and the replica does not have it
    /// yet (local ADR 0050 §6), alone in its request, so a service that refuses the kind refuses
    /// nothing else. Answers whether it sent one. The switch is marked sent only while it still
    /// stands as sent: a move made during the request is sent on the next exchange.
    /// </summary>
    public async Task<Result<bool>> PushAreaAsync(CancellationToken cancellationToken = default)
    {
        ReconcileIdentity();

        if (_state.Current.Area is not { Sent: false } area) return Result.Success(false);

        var change = KnowledgeEventDocuments.ToChange(new EventsAreaReplicaDto(EventsAreaReplicaDto.WellKnownId, area.On, area.At));

        var response = await _client.PushAsync([change], cancellationToken).ConfigureAwait(false);
        if (response is not Ok<PushKnowledgeEventsResponse>(var answer)) return (Error)response.Value!;

        // Not taken: the replica holds a later switch, by another desktop's clock. That one stands.
        if (answer.Accepted == 0)
        {
            _log.LogInformation("The events switch was not taken: the replica holds a later one from another desktop.");
        }

        _state.Update(state => state.Area == area ? state with { Area = area with { Sent = true } } : state);

        return Result.Success(true);
    }

    /// <summary>
    /// Sends every event and series changed since the watermark, in batches, advancing the
    /// watermark per accepted batch and never into the middle of a shared stamp. A change the
    /// service would refuse is left out of its batch and named in a warning, and the watermark
    /// moves past it: none of its faults mends without a new save, and a save stamps it past
    /// the watermark again.
    /// </summary>
    public async Task<Result<KnowledgeEventSyncSummary>> PushAsync(CancellationToken cancellationToken = default)
    {
        ReconcileIdentity();

        var watermark = _state.Current.PushWatermark;
        var events = await _replication.ListEventsChangedSinceAsync(watermark, cancellationToken).ConfigureAwait(false);
        var series = await _replication.ListSeriesChangedSinceAsync(watermark, cancellationToken).ConfigureAwait(false);

        var pending = events
            .Select(KnowledgeEventDocuments.ToChange)
            .Concat(series.Select(KnowledgeEventDocuments.ToChange))
            .OrderBy(change => change.UpdatedAt)
            .ThenBy(change => change.Id)
            .ToList();

        var pushed = 0;
        var refused = 0;
        var skipped = 0;

        for (var start = 0; start < pending.Count; start += PushBatchSize)
        {
            var batch = pending.Skip(start).Take(PushBatchSize).ToList();
            var changes = new List<KnowledgeEventChange>(batch.Count);

            foreach (var change in batch)
            {
                if (KnowledgeEventRecordLimits.WhyRefused(change) is { } reason)
                {
                    _log.LogWarning(
                        "Leaving {Kind} {Id} out of the push, because the sync service would refuse it and every change sent beside it. {Reason}",
                        change.Kind,
                        change.Id,
                        reason);

                    skipped++;
                    continue;
                }

                changes.Add(change);
            }

            if (changes.Count > 0)
            {
                var response = await _client.PushAsync(changes, cancellationToken).ConfigureAwait(false);
                if (response is not Ok<PushKnowledgeEventsResponse>(var answer)) return (Error)response.Value!;

                pushed += answer.Accepted;
                refused += Math.Max(0, changes.Count - answer.Accepted);
            }

            if (ReplicaRoutes.WatermarkAfter(batch, final: start + batch.Count >= pending.Count, change => change.UpdatedAt) is { } advanced)
            {
                _state.Update(state => state with { PushWatermark = advanced });
            }
        }

        return Result.Success(new KnowledgeEventSyncSummary(pushed, 0, 0, _time.GetUtcNow()) { Refused = refused, Skipped = skipped });
    }

    /// <summary>
    /// Reads the owner's feed to its end, applying each page and saving the cursor after each,
    /// until the service says there is no more. A cursor the service will not resume from is
    /// forgotten once and the feed read from the beginning.
    /// </summary>
    public async Task<Result<KnowledgeEventSyncSummary>> PullAsync(CancellationToken cancellationToken = default)
    {
        ReconcileIdentity();

        var cursor = _state.Current.PullCursor;
        var pulled = 0;
        var applied = 0;
        var startedOver = false;

        while (true)
        {
            var page = await _client
                .PullAsync(cursor, KnowledgeEventSyncClient.DefaultMaxItems, cancellationToken)
                .ConfigureAwait(false);

            if (page is not Ok<PullKnowledgeEventsResponse>(var feed))
            {
                var error = (Error)page.Value!;
                if (startedOver || cursor is null || !ReplicaRoutes.Retired(error.Code))
                {
                    return error;
                }

                startedOver = true;
                cursor = null;
                _state.Update(state => state with { PullCursor = null });
                pulled = 0;
                applied = 0;

                continue;
            }

            pulled += feed.Events.Count;
            applied += (await _merge.ApplyAsync(feed.Events, cancellationToken).ConfigureAwait(false)).Applied;

            cursor = feed.Since;
            _state.Update(state => state with { PullCursor = cursor });

            if (!feed.HasMore) break;
        }

        return Result.Success(new KnowledgeEventSyncSummary(0, pulled, applied, _time.GetUtcNow()));
    }

    /// <summary>
    /// The exchange of a desktop whose area is off: the switch alone. It fails as
    /// <see cref="SyncAsync"/> does, except where the service is older than the switch — a bare
    /// 404, or the kind refused — which ends as a success that says it is not yet served, so the
    /// worker shows nothing wrong while the deployed service catches up and the switch stays
    /// unsent.
    /// </summary>
    public async Task<Result<KnowledgeEventSyncSummary>> WithdrawAsync(CancellationToken cancellationToken = default)
    {
        var sent = await PushAreaAsync(cancellationToken).ConfigureAwait(false);

        if (sent is not Ok<bool>(var areaSent))
        {
            var error = (Error)sent.Value!;
            if (string.Equals(error.Code, SyncErrorCodes.KnowledgeEventInvalid, StringComparison.Ordinal))
            {
                _log.LogInformation("The sync service refused the events switch ({Code}); it goes again next exchange.", error.Code);
                return Result.Success(new KnowledgeEventSyncSummary(0, 0, 0, _time.GetUtcNow()) { NotYetServed = true });
            }

            return NotYetOr(error);
        }

        return Result.Success(new KnowledgeEventSyncSummary(0, 0, 0, _time.GetUtcNow()) { AreaSent = areaSent });
    }

    /// <summary>Push, then the switch, then pull; a failed push stops the exchange. A bare 404
    /// from either half is a service without the route and ends the exchange as a success that
    /// says so. The switch failing stops nothing: it stays unsent, is named in the log, and goes
    /// again next exchange — a service deployed before the switch's kind refuses it.</summary>
    public async Task<Result<KnowledgeEventSyncSummary>> SyncAsync(CancellationToken cancellationToken = default)
    {
        var push = await PushAsync(cancellationToken).ConfigureAwait(false);
        if (push is not Ok<KnowledgeEventSyncSummary>(var pushSummary)) return NotYetOr((Error)push.Value!);

        var area = await PushAreaAsync(cancellationToken).ConfigureAwait(false);
        if (area is Error areaError)
        {
            _log.LogInformation(
                "The events switch was not sent and goes again next exchange: {Code} {Message}", areaError.Code, areaError.Message);
        }

        var pull = await PullAsync(cancellationToken).ConfigureAwait(false);
        if (pull is not Ok<KnowledgeEventSyncSummary>(var pullSummary)) return NotYetOr((Error)pull.Value!);

        return Result.Success(new KnowledgeEventSyncSummary(
            pushSummary.Pushed,
            pullSummary.Pulled,
            pullSummary.Applied,
            _time.GetUtcNow())
        {
            Refused = pushSummary.Refused,
            Skipped = pushSummary.Skipped,
            AreaSent = area is Ok<bool>(true),
        });
    }

    /// <summary>A failure, unless it is the bare 404 a service without the route answers. A 404
    /// that carries a code is the service saying something specific about this request, and
    /// fails as it would on any other feed.</summary>
    private Result<KnowledgeEventSyncSummary> NotYetOr(Error error)
    {
        if (error.Type != ErrorType.NotFound || !string.Equals(error.Code, UnmappedRouteCode, StringComparison.Ordinal))
        {
            return Result.Failure<KnowledgeEventSyncSummary>(error);
        }

        _log.LogDebug("The sync service has no knowledge event route yet ({Code}); nothing was exchanged.", error.Code);

        return Result.Success(new KnowledgeEventSyncSummary(0, 0, 0, _time.GetUtcNow()) { NotYetServed = true });
    }
}
