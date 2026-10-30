using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sync.DevbookSubjects;

/// <summary>What one exchange did, for a screen to show and a test to assert.
/// <paramref name="Pulled"/> counts what came back and <paramref name="Applied"/>
/// what was written; a device's own echo arrives and changes nothing.</summary>
public sealed record DevbookSubjectSyncSummary(int Pushed, int Pulled, int Applied, DateTimeOffset At)
{
    /// <summary>
    /// States offered in a push the replica did not take, because it already held
    /// that version or a later one.
    /// <para>
    /// Not a count of lost work. Most of it is a healthy echo: a state pulled from
    /// another device is stamped past this device's push watermark, so the next
    /// push offers it back, and the replica refuses it as the version it already
    /// holds — on every ordinary two-device cycle. Only the rest is two devices
    /// having set one subject, the later stamp kept on both.
    /// </para>
    /// </summary>
    public int Refused { get; init; }

    /// <summary>States left on this machine because the service would refuse
    /// them, each named in a warning.</summary>
    public int Skipped { get; init; }

    /// <summary>The sync service answered that it has no subject route: it is
    /// older than this client, and nothing was exchanged. Not a failure — the
    /// deployed service is updated by hand and lags — and nothing moved, so the
    /// next cycle after it is updated sends everything.</summary>
    public bool NotYetServed { get; init; }
}

/// <summary>
/// One exchange with the devbook subject replica: push what this device's
/// subject state store changed, pull what other devices changed.
/// <see cref="Annotations.AnnotationSyncSession"/>'s loop over the subject feed —
/// the watermark rules, the per-page cursor save, the once-only recovery from a
/// retired cursor, the push filter by the service's own rule — kept as a copy for
/// the reason that one is: the feeds fail separately and a shared loop would give
/// them one error to report.
/// <para>
/// One thing more than its model: a sync service that predates subject state
/// answers a bare 404, with no problem code, for the route, and
/// <see cref="SyncAsync"/> reads that — and only that — as "not yet" (<see cref="DevbookSubjectSyncSummary.NotYetServed"/>) rather than a
/// failure, so a desktop ahead of the deployed service neither reports an error
/// every five minutes nor moves its progress.
/// </para>
/// </summary>
public sealed class DevbookSubjectSyncSession
{
    /// <summary>How many changes go in one push — the annotation batch size, well
    /// below the service's cap.</summary>
    private const int PushBatchSize = 200;

    /// <summary>The code <c>SyncHttp</c> gives a 404 whose body carries none —
    /// what an older service answers for a route it does not map.</summary>
    private const string UnmappedRouteCode = "sync.http_404";

    private readonly DevbookSubjectSyncClient _client;
    private readonly DevbookSubjectReplicaMerge _merge;
    private readonly IDevbookSubjectStateStore _store;
    private readonly IDevbookSubjectSyncStateStore _state;
    private readonly IDeviceCredentialStore _credentials;
    private readonly TimeProvider _time;
    private readonly ILogger _log;

    /// <param name="credentials">Whose device this is, checked against the
    /// recorded progress before either half runs.</param>
    /// <param name="log">Where a state left out of a push is named.</param>
    public DevbookSubjectSyncSession(
        DevbookSubjectSyncClient client,
        DevbookSubjectReplicaMerge merge,
        IDevbookSubjectStateStore store,
        IDevbookSubjectSyncStateStore state,
        IDeviceCredentialStore credentials,
        TimeProvider time,
        ILogger<DevbookSubjectSyncSession>? log = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(merge);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(time);

        _client = client;
        _merge = merge;
        _store = store;
        _state = state;
        _credentials = credentials;
        _time = time;
        _log = log ?? NullLogger<DevbookSubjectSyncSession>.Instance;
    }

    /// <summary>Starts the progress over when it was recorded for a different
    /// identity — the check <c>TaskSyncSession.ReconcileIdentity</c>
    /// explains.</summary>
    private void ReconcileIdentity()
    {
        if (_credentials.Current is not { } me) return;

        var state = _state.Current;
        if (state.OwnerId == me.OwnerId && state.DeviceId == me.DeviceId) return;

        _state.Save(new DevbookSubjectSyncState(DateTimeOffset.MinValue, null, me.OwnerId, me.DeviceId));
    }

    /// <summary>
    /// Sends everything this device has changed since the watermark, in batches,
    /// advancing the watermark per accepted batch and never into the middle of a
    /// shared stamp. A state the service would refuse is left out of its batch
    /// and named in a warning, and the watermark moves past it: none of its
    /// faults mends without a new state, and setting one stamps it past the
    /// watermark again.
    /// </summary>
    public async Task<Result<DevbookSubjectSyncSummary>> PushAsync(CancellationToken cancellationToken = default)
    {
        ReconcileIdentity();

        var pending = _store.ListChangedSince(_state.Current.PushWatermark);
        var pushed = 0;
        var refused = 0;
        var skipped = 0;

        for (var start = 0; start < pending.Count; start += PushBatchSize)
        {
            var batch = pending.Skip(start).Take(PushBatchSize).ToList();
            var changes = new List<DevbookSubjectChange>(batch.Count);

            foreach (var subject in batch)
            {
                var change = DevbookSubjectReplicaMerge.ToChange(subject);

                if (DevbookSubjectRecordLimits.WhyRefused(change) is { } reason)
                {
                    _log.LogWarning(
                        "Leaving devbook subject {SubjectId} ({SubjectKey}) out of the push, because the sync service "
                        + "would refuse it and every state sent beside it. {Reason}",
                        subject.Id,
                        subject.Subject.CanonicalKey,
                        reason);

                    skipped++;
                    continue;
                }

                changes.Add(change);
            }

            if (changes.Count > 0)
            {
                var response = await _client.PushAsync(changes, cancellationToken).ConfigureAwait(false);
                if (response.IsFailure) return Result.Failure<DevbookSubjectSyncSummary>(response.Error);

                pushed += response.Value.Accepted;
                refused += Math.Max(0, changes.Count - response.Value.Accepted);
            }

            if (ReplicaRoutes.WatermarkAfter(batch, final: start + batch.Count >= pending.Count, subject => subject.UpdatedAt) is { } advanced)
            {
                _state.Save(_state.Current with { PushWatermark = advanced });
            }
        }

        return Result.Success(new DevbookSubjectSyncSummary(pushed, 0, 0, _time.GetUtcNow()) { Refused = refused, Skipped = skipped });
    }

    /// <summary>
    /// Reads the owner's feed to its end, applying each page and saving the
    /// cursor after each, until the service says there is no more. A cursor the
    /// service will not resume from is forgotten once and the feed read from the
    /// beginning, as <see cref="TaskSyncSession.PullAsync"/> does.
    /// </summary>
    public async Task<Result<DevbookSubjectSyncSummary>> PullAsync(CancellationToken cancellationToken = default)
    {
        ReconcileIdentity();

        var cursor = _state.Current.PullCursor;
        var pulled = 0;
        var applied = 0;
        var startedOver = false;

        while (true)
        {
            var page = await _client
                .PullAsync(cursor, DevbookSubjectSyncClient.DefaultMaxItems, cancellationToken)
                .ConfigureAwait(false);

            if (page.IsFailure)
            {
                if (startedOver || cursor is null || !ReplicaRoutes.Retired(page.Error.Code))
                {
                    return Result.Failure<DevbookSubjectSyncSummary>(page.Error);
                }

                startedOver = true;
                cursor = null;
                _state.Save(_state.Current with { PullCursor = null });
                pulled = 0;
                applied = 0;

                continue;
            }

            pulled += page.Value.Subjects.Count;
            applied += _merge.Apply(page.Value.Subjects).Applied;

            cursor = page.Value.Since;
            _state.Save(_state.Current with { PullCursor = cursor });

            if (!page.Value.HasMore) break;
        }

        return Result.Success(new DevbookSubjectSyncSummary(0, pulled, applied, _time.GetUtcNow()));
    }

    /// <summary>Push, then pull; a failed push stops the exchange. A bare 404
    /// from either half is a service without the route — see the class remarks —
    /// and ends the exchange as a success that says so.</summary>
    public async Task<Result<DevbookSubjectSyncSummary>> SyncAsync(CancellationToken cancellationToken = default)
    {
        var push = await PushAsync(cancellationToken).ConfigureAwait(false);
        if (push.IsFailure) return NotYetOr(push.Error);

        var pull = await PullAsync(cancellationToken).ConfigureAwait(false);
        if (pull.IsFailure) return NotYetOr(pull.Error);

        return Result.Success(new DevbookSubjectSyncSummary(
            push.Value.Pushed,
            pull.Value.Pulled,
            pull.Value.Applied,
            _time.GetUtcNow())
        {
            Refused = push.Value.Refused,
            Skipped = push.Value.Skipped,
        });
    }

    /// <summary>
    /// A failure, unless it is the bare 404 a service without the route answers:
    /// no problem code, so <c>SyncHttp</c> named it <see cref="UnmappedRouteCode"/>.
    /// A 404 that carries a code is the service saying something specific about
    /// this request, and fails as it would on any other feed — read as "not yet",
    /// it would leave the feed never syncing with nothing shown.
    /// </summary>
    private Result<DevbookSubjectSyncSummary> NotYetOr(Error error)
    {
        if (error.Type != ErrorType.NotFound || !string.Equals(error.Code, UnmappedRouteCode, StringComparison.Ordinal))
        {
            return Result.Failure<DevbookSubjectSyncSummary>(error);
        }

        _log.LogDebug("The sync service has no devbook subject route yet ({Code}); nothing was exchanged.", error.Code);

        return Result.Success(new DevbookSubjectSyncSummary(0, 0, 0, _time.GetUtcNow()) { NotYetServed = true });
    }
}
