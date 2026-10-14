using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sync.Annotations;

/// <summary>What one exchange did, for a screen to show and a test to assert.
/// <paramref name="Pulled"/> counts what came back and <paramref name="Applied"/>
/// what was written; a device's own echo arrives and changes nothing, so the
/// two differ on a healthy cycle.</summary>
public sealed record AnnotationSyncSummary(int Pushed, int Pulled, int Applied, DateTimeOffset At)
{
    /// <summary>Documents offered in a push the replica did not take, because
    /// it already held a later version. Never healthy: the local store stamps a
    /// change past the copy it changes, so a refusal means two devices really
    /// did edit the same remark, and the loser's edit is the one here.</summary>
    public int Refused { get; init; }

    /// <summary>Remarks left on this machine because the service would refuse
    /// them — filed under no repository, or a field past its bound — rather
    /// than sent to have the batch they were in refused whole. Each is named in
    /// a warning; none is offered again until an edit restamps it.</summary>
    public int Skipped { get; init; }
}

/// <summary>
/// One exchange with the annotation replica: push what this device's store
/// changed, pull what other devices changed. <see cref="TaskSyncSession"/>'s
/// loop over the third feed — the watermark rules, the per-page cursor save,
/// the once-only recovery from a retired cursor — kept as a copy for the
/// reason the session exchange is: the two fail separately and are switched
/// separately, and a shared loop would give them one error to report.
/// </summary>
public sealed class AnnotationSyncSession
{
    /// <summary>How many changes go in one push — the task batch size, and well
    /// below the service's cap for the same reasons.</summary>
    private const int PushBatchSize = 200;

    private readonly AnnotationSyncClient _client;
    private readonly AnnotationReplicaMerge _merge;
    private readonly IDevbookAnnotationStore _store;
    private readonly IAnnotationSyncStateStore _state;
    private readonly IDeviceCredentialStore _credentials;
    private readonly TimeProvider _time;
    private readonly ILogger _log;

    /// <param name="credentials">Whose device this is, checked against the
    /// recorded progress before either half runs — see <see cref="ReconcileIdentity"/>.</param>
    /// <param name="log">Where a remark left out of a push is named. Optional,
    /// as it is on <see cref="TaskReplicaMerge"/>.</param>
    public AnnotationSyncSession(
        AnnotationSyncClient client,
        AnnotationReplicaMerge merge,
        IDevbookAnnotationStore store,
        IAnnotationSyncStateStore state,
        IDeviceCredentialStore credentials,
        TimeProvider time,
        ILogger<AnnotationSyncSession>? log = null)
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
        _log = log ?? NullLogger<AnnotationSyncSession>.Instance;
    }

    /// <summary>Starts the progress over when it was recorded for a different
    /// identity — the check <c>TaskSyncSession.ReconcileIdentity</c> explains,
    /// applied to the third feed so a device that registers again does not carry
    /// the old owner's annotation watermark into the new one.</summary>
    private void ReconcileIdentity()
    {
        if (_credentials.Current is not { } me) return;

        var state = _state.Current;
        if (state.OwnerId == me.OwnerId && state.DeviceId == me.DeviceId) return;

        _state.Save(new AnnotationSyncState(DateTimeOffset.MinValue, null, me.OwnerId, me.DeviceId));
    }

    /// <summary>
    /// Sends everything this device has changed since the watermark, in
    /// batches, advancing the watermark per accepted batch and never into the
    /// middle of a shared stamp — <see cref="TaskSyncSession.PushAsync"/>'s
    /// rules, for its reasons. Drafts are not in the selection: the store
    /// leaves them out of <see cref="IDevbookAnnotationStore.ListChangedSince"/>.
    /// <para>
    /// A remark the service would refuse is left out of its batch and named in
    /// a warning, because the service refuses a batch whole and would otherwise
    /// refuse every remark beside it, on every cycle. The watermark moves past
    /// it all the same: none of its faults mends without an edit, and an edit
    /// stamps it past the watermark again. A batch with nothing left to send is
    /// not sent.
    /// </para>
    /// </summary>
    public async Task<Result<AnnotationSyncSummary>> PushAsync(CancellationToken cancellationToken = default)
    {
        ReconcileIdentity();

        var watermark = _state.Current.PushWatermark;
        var pending = _store.ListChangedSince(watermark);
        var pushed = 0;
        var refused = 0;
        var skipped = 0;

        for (var start = 0; start < pending.Count; start += PushBatchSize)
        {
            var batch = pending.Skip(start).Take(PushBatchSize).ToList();
            var changes = new List<AnnotationChange>(batch.Count);

            foreach (var annotation in batch)
            {
                var change = AnnotationReplicaMerge.ToChange(annotation);

                // The service's own rule, over what would cross the wire.
                if (AnnotationRecordLimits.WhyRefused(change) is { } reason)
                {
                    // Never the body: it is the person's own text.
                    _log.LogWarning(
                        "Leaving annotation {AnnotationId} on {ChapterPath} out of the push, because the sync "
                        + "service would refuse it and every remark sent beside it. {Reason}",
                        annotation.Id,
                        annotation.ChapterPath,
                        reason);

                    skipped++;
                    continue;
                }

                changes.Add(change);
            }

            if (changes.Count > 0)
            {
                var response = await _client.PushAsync(changes, cancellationToken).ConfigureAwait(false);
                if (response.IsFailure) return Result.Failure<AnnotationSyncSummary>(response.Error);

                // A 200 with fewer accepted than sent is the replica keeping a later
                // version of the rest. The watermark still moves past them — offering
                // them again would be refused again — so the count is the one place
                // the loss is said.
                pushed += response.Value.Accepted;
                refused += Math.Max(0, changes.Count - response.Value.Accepted);
            }

            if (ReplicaRoutes.WatermarkAfter(batch, final: start + batch.Count >= pending.Count, annotation => annotation.UpdatedAt) is { } advanced)
            {
                _state.Save(_state.Current with { PushWatermark = advanced });
            }
        }

        return Result.Success(new AnnotationSyncSummary(pushed, 0, 0, _time.GetUtcNow()) { Refused = refused, Skipped = skipped });
    }

    /// <summary>
    /// Reads the owner's feed to its end, applying each page as it arrives and
    /// saving the cursor after each, until the service says there is no more.
    /// A cursor the service will not resume from is forgotten once and the feed
    /// read from the beginning — see <see cref="TaskSyncSession.PullAsync"/>
    /// for why once, and why <see cref="SyncErrorCodes.SyncCursorNotYours"/> is
    /// not on that list.
    /// </summary>
    public async Task<Result<AnnotationSyncSummary>> PullAsync(CancellationToken cancellationToken = default)
    {
        ReconcileIdentity();

        var cursor = _state.Current.PullCursor;
        var pulled = 0;
        var applied = 0;
        var startedOver = false;

        while (true)
        {
            var page = await _client
                .PullAsync(cursor, AnnotationSyncClient.DefaultMaxItems, cancellationToken)
                .ConfigureAwait(false);

            if (page.IsFailure)
            {
                if (startedOver || cursor is null || !ReplicaRoutes.Retired(page.Error.Code))
                {
                    return Result.Failure<AnnotationSyncSummary>(page.Error);
                }

                startedOver = true;
                cursor = null;
                _state.Save(_state.Current with { PullCursor = null });
                pulled = 0;
                applied = 0;

                continue;
            }

            pulled += page.Value.Annotations.Count;
            applied += _merge.Apply(page.Value.Annotations).Applied;

            cursor = page.Value.Since;
            _state.Save(_state.Current with { PullCursor = cursor });

            if (!page.Value.HasMore) break;
        }

        return Result.Success(new AnnotationSyncSummary(0, pulled, applied, _time.GetUtcNow()));
    }

    /// <summary>Push, then pull; a failed push stops the exchange. A remark the
    /// push left out is not a failed push.</summary>
    public async Task<Result<AnnotationSyncSummary>> SyncAsync(CancellationToken cancellationToken = default)
    {
        var push = await PushAsync(cancellationToken).ConfigureAwait(false);
        if (push.IsFailure) return push;

        var pull = await PullAsync(cancellationToken).ConfigureAwait(false);
        if (pull.IsFailure) return pull;

        return Result.Success(new AnnotationSyncSummary(
            push.Value.Pushed,
            pull.Value.Pulled,
            pull.Value.Applied,
            _time.GetUtcNow())
        {
            Refused = push.Value.Refused,
            Skipped = push.Value.Skipped,
        });
    }
}
