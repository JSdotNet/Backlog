using Backlog.Modules.Events.Abstractions.Services;
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

        var state = _state.Current;
        if (state.OwnerId == me.OwnerId && state.DeviceId == me.DeviceId) return;

        _state.Save(new KnowledgeEventSyncState(DateTimeOffset.MinValue, null, me.OwnerId, me.DeviceId));
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
                if (response.IsFailure) return Result.Failure<KnowledgeEventSyncSummary>(response.Error);

                pushed += response.Value.Accepted;
                refused += Math.Max(0, changes.Count - response.Value.Accepted);
            }

            if (ReplicaRoutes.WatermarkAfter(batch, final: start + batch.Count >= pending.Count, change => change.UpdatedAt) is { } advanced)
            {
                _state.Save(_state.Current with { PushWatermark = advanced });
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

            if (page.IsFailure)
            {
                if (startedOver || cursor is null || !ReplicaRoutes.Retired(page.Error.Code))
                {
                    return Result.Failure<KnowledgeEventSyncSummary>(page.Error);
                }

                startedOver = true;
                cursor = null;
                _state.Save(_state.Current with { PullCursor = null });
                pulled = 0;
                applied = 0;

                continue;
            }

            pulled += page.Value.Events.Count;
            applied += (await _merge.ApplyAsync(page.Value.Events, cancellationToken).ConfigureAwait(false)).Applied;

            cursor = page.Value.Since;
            _state.Save(_state.Current with { PullCursor = cursor });

            if (!page.Value.HasMore) break;
        }

        return Result.Success(new KnowledgeEventSyncSummary(0, pulled, applied, _time.GetUtcNow()));
    }

    /// <summary>Push, then pull; a failed push stops the exchange. A bare 404 from either half is
    /// a service without the route and ends the exchange as a success that says so.</summary>
    public async Task<Result<KnowledgeEventSyncSummary>> SyncAsync(CancellationToken cancellationToken = default)
    {
        var push = await PushAsync(cancellationToken).ConfigureAwait(false);
        if (push.IsFailure) return NotYetOr(push.Error);

        var pull = await PullAsync(cancellationToken).ConfigureAwait(false);
        if (pull.IsFailure) return NotYetOr(pull.Error);

        return Result.Success(new KnowledgeEventSyncSummary(
            push.Value.Pushed,
            pull.Value.Pulled,
            pull.Value.Applied,
            _time.GetUtcNow())
        {
            Refused = push.Value.Refused,
            Skipped = push.Value.Skipped,
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
