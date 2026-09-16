using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Sync.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.Sync.Annotations;

/// <summary>What one exchange did, for a screen to show and a test to assert.
/// <paramref name="Pulled"/> counts what came back and <paramref name="Applied"/>
/// what was written; a device's own echo arrives and changes nothing, so the
/// two differ on a healthy cycle.</summary>
public sealed record AnnotationSyncSummary(int Pushed, int Pulled, int Applied, DateTimeOffset At);

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
    private readonly TimeProvider _time;

    public AnnotationSyncSession(
        AnnotationSyncClient client,
        AnnotationReplicaMerge merge,
        IDevbookAnnotationStore store,
        IAnnotationSyncStateStore state,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(merge);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(time);

        _client = client;
        _merge = merge;
        _store = store;
        _state = state;
        _time = time;
    }

    /// <summary>
    /// Sends everything this device has changed since the watermark, in
    /// batches, advancing the watermark per accepted batch and never into the
    /// middle of a shared stamp — <see cref="TaskSyncSession.PushAsync"/>'s
    /// rules, for its reasons. Drafts are not in the selection: the store
    /// leaves them out of <see cref="IDevbookAnnotationStore.ListChangedSince"/>.
    /// </summary>
    public async Task<Result<AnnotationSyncSummary>> PushAsync(CancellationToken cancellationToken = default)
    {
        var watermark = _state.Current.PushWatermark;
        var pending = _store.ListChangedSince(watermark);
        var pushed = 0;

        for (var start = 0; start < pending.Count; start += PushBatchSize)
        {
            var batch = pending.Skip(start).Take(PushBatchSize).ToList();
            var changes = batch.Select(AnnotationReplicaMerge.ToChange).ToList();

            var response = await _client.PushAsync(changes, cancellationToken).ConfigureAwait(false);
            if (response.IsFailure) return Result.Failure<AnnotationSyncSummary>(response.Error);

            pushed += response.Value.Accepted;

            if (WatermarkAfter(batch, final: start + batch.Count >= pending.Count) is { } advanced)
            {
                _state.Save(_state.Current with { PushWatermark = advanced });
            }
        }

        return Result.Success(new AnnotationSyncSummary(pushed, 0, 0, _time.GetUtcNow()));
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
                if (startedOver || cursor is null || !Retired(page.Error.Code))
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
            applied += _merge.Apply(page.Value.Annotations, _state.Current.PushWatermark).Applied;

            cursor = page.Value.Since;
            _state.Save(_state.Current with { PullCursor = cursor });

            if (!page.Value.HasMore) break;
        }

        return Result.Success(new AnnotationSyncSummary(0, pulled, applied, _time.GetUtcNow()));
    }

    /// <summary>Push, then pull; a failed push stops the exchange.</summary>
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
            _time.GetUtcNow()));
    }

    private static bool Retired(string code) =>
        code is SyncErrorCodes.SyncCursorExpired or SyncErrorCodes.SyncCursorMalformed;

    /// <summary>How far the watermark may move once a batch has been accepted,
    /// or null when it may not move at all — the last stamp on the final batch,
    /// otherwise the highest stamp strictly below the batch's last, so an
    /// annotation sharing that stamp in the next batch is still selected.</summary>
    private static DateTimeOffset? WatermarkAfter(IReadOnlyList<DevbookAnnotation> batch, bool final)
    {
        if (final) return batch[^1].UpdatedAt;

        var boundary = batch[^1].UpdatedAt;

        return batch
            .Where(annotation => annotation.UpdatedAt < boundary)
            .Select(annotation => (DateTimeOffset?)annotation.UpdatedAt)
            .Max();
    }
}
