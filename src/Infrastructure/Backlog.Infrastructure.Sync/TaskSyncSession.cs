using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.Sync;

/// <summary>What one exchange did, for a screen to show and a test to assert.
/// <paramref name="Pulled"/> counts what came back and <paramref name="Applied"/>
/// what was written, and the two differ on purpose: a device's own echo and a
/// replayed page both arrive and change nothing, so "pulled 40, applied 0" is a
/// healthy answer rather than a broken one.
/// <para>
/// <paramref name="Skipped"/> is the one number here that is never healthy:
/// documents this build could not read. They are left in the replica and the
/// exchange carries on, so the count is the only thing that says it happened.
/// </para></summary>
public sealed record TaskSyncSummary(int Pushed, int Pulled, int Applied, int Skipped, DateTimeOffset At);

/// <summary>
/// One exchange with the replica: push what changed here, pull what changed
/// elsewhere.
/// <para>
/// The two halves are separate methods as well as a combined one because they
/// fail separately and a person may want only one of them — pushing works while
/// a cursor is being reissued, and pulling works on a device that has nothing of
/// its own to send.
/// </para>
/// </summary>
public sealed class TaskSyncSession
{
    /// <summary>How many changes go in one push. Large enough that a device
    /// returning from a week away sends a handful of requests rather than
    /// hundreds, small enough that a failure loses one batch's worth of progress
    /// rather than the run's.</summary>
    private const int PushBatchSize = 200;

    private readonly TaskSyncClient _client;
    private readonly TaskReplicaMerge _merge;
    private readonly ITaskRepository _tasks;
    private readonly ITaskSyncStateStore _state;
    private readonly TimeProvider _time;

    public TaskSyncSession(
        TaskSyncClient client,
        TaskReplicaMerge merge,
        ITaskRepository tasks,
        ITaskSyncStateStore state,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(merge);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(time);

        _client = client;
        _merge = merge;
        _tasks = tasks;
        _state = state;
        _time = time;
    }

    /// <summary>
    /// Sends everything this machine has changed since the watermark.
    /// <para>
    /// The watermark advances to the highest <c>UpdatedAt</c> that was actually
    /// accepted, and never to <see cref="TimeProvider.GetUtcNow"/>. A task saved
    /// while a batch was in flight carries a stamp between the two, and a
    /// watermark set to now would step over it: the edit would never be selected
    /// again and would stay on this machine for good, with nothing failing to say
    /// so.
    /// </para>
    /// <para>
    /// It advances per batch rather than at the end, so a run that dies on the
    /// third batch of five keeps the first two — the next run resends one batch's
    /// worth at most, and resending is free under a whole-document upsert.
    /// </para>
    /// <para>
    /// <b>And never into the middle of a stamp.</b> <c>UpdatedAt</c> is not
    /// unique — <c>TaskItem</c>'s constructor stamps it from <c>createdAt</c>, so
    /// anything that builds several tasks from one clock reading gives them all
    /// the same one — and the selection is strictly greater than the watermark.
    /// A batch boundary that falls inside a shared stamp therefore has to stop
    /// short of it: advancing onto it would exclude every task still carrying it
    /// from every future run, permanently and with nothing failing to say so.
    /// </para>
    /// <para>
    /// The selection is <see cref="ITaskRepository.ListChangedSinceAsync"/> and
    /// not <see cref="ITaskRepository.ListAsync"/>, because a tombstone has to be
    /// in it: <c>ListAsync</c> hides a deleted task from every screen by design,
    /// and a push built on it would leave a deletion on this machine forever
    /// while the other device kept the task. That read already applies the
    /// watermark and the ordering this loop depends on, so there is nothing to
    /// filter or sort here.
    /// </para>
    /// </summary>
    public async Task<Result<TaskSyncSummary>> PushAsync(CancellationToken cancellationToken = default)
    {
        var watermark = _state.Current.PushWatermark;

        var pending = (await _tasks.ListChangedSinceAsync(watermark, cancellationToken).ConfigureAwait(false))
            .ToList();

        var pushed = 0;

        for (var start = 0; start < pending.Count; start += PushBatchSize)
        {
            var batch = pending.GetRange(start, Math.Min(PushBatchSize, pending.Count - start));
            var changes = batch.Select(TaskReplicaMerge.ToChange).ToList();

            var response = await _client.PushAsync(changes, cancellationToken).ConfigureAwait(false);
            if (response.IsFailure) return Result.Failure<TaskSyncSummary>(response.Error);

            pushed += response.Value.Accepted;

            if (WatermarkAfter(batch, final: start + batch.Count >= pending.Count) is { } advanced)
            {
                _state.Save(_state.Current with { PushWatermark = advanced });
            }
        }

        return Result.Success(new TaskSyncSummary(pushed, 0, 0, 0, _time.GetUtcNow()));
    }

    /// <summary>
    /// Reads the owner's change feed to its end, applying each page as it
    /// arrives.
    /// <para>
    /// The cursor is saved after every successful page, so a pull interrupted
    /// half way resumes rather than restarts. The cost of saving too eagerly is a
    /// page replayed when a run dies between applying and saving, and a replayed
    /// page writes nothing — see <see cref="TaskReplicaMerge"/>. The cost of
    /// saving once at the end would be a device that never finishes catching up
    /// because it starts from the beginning every time.
    /// </para>
    /// <para>
    /// It loops until the service says <c>HasMore</c> is false rather than until
    /// a page comes back empty: the feed's position advances whether or not a
    /// page had anything in it, and an empty page still hands back a cursor worth
    /// keeping. <c>HasMore</c> is the store's own "you are caught up" signal and
    /// not a guess from the page size, so a short page is not the end of the feed
    /// and this loop must not read one as such.
    /// </para>
    /// <para>
    /// <b>A cursor the service will not resume from is recovered from once.</b>
    /// A container recreated by a redeploy, or the local emulator restarting on a
    /// fresh volume, retires every continuation ever minted against it; a device
    /// that kept sending its stored one would get a 400 on every pull it ever
    /// made again, and the only way out would be deleting the state file by hand.
    /// So the cursor is forgotten — on disk before the retry, so a run that dies
    /// in between does not send it again either — and the feed is read from the
    /// beginning, which is what a freshly paired device does anyway.
    /// </para>
    /// <para>
    /// Once, and not in a loop: a service answering "expired" to a pull that
    /// carried no cursor is saying something starting over cannot fix.
    /// <see cref="SyncErrorCodes.SyncCursorNotYours"/> is deliberately not on the
    /// list — a correctly-signed cursor for another owner's feed is the one event
    /// .arc42/adr/0005 section Consequences asks to be loud about, and quietly
    /// starting over is exactly the quiet.
    /// </para>
    /// </summary>
    public async Task<Result<TaskSyncSummary>> PullAsync(CancellationToken cancellationToken = default)
    {
        var cursor = _state.Current.PullCursor;
        var pulled = 0;
        var applied = 0;
        var skipped = 0;
        var startedOver = false;

        while (true)
        {
            var page = await _client
                .PullAsync(cursor, TaskSyncClient.DefaultMaxItems, cancellationToken)
                .ConfigureAwait(false);

            if (page.IsFailure)
            {
                if (startedOver || cursor is null || !Retired(page.Error.Code))
                {
                    return Result.Failure<TaskSyncSummary>(page.Error);
                }

                startedOver = true;
                cursor = null;
                _state.Save(_state.Current with { PullCursor = null });

                // The counts go with the cursor. What was applied before the
                // restart is about to arrive again, and a summary that added the
                // two would tell the person their backlog moved twice.
                pulled = 0;
                applied = 0;
                skipped = 0;

                continue;
            }

            pulled += page.Value.Tasks.Count;
            var merged = await _merge
                .ApplyAsync(page.Value.Tasks, _state.Current.PushWatermark, cancellationToken)
                .ConfigureAwait(false);

            applied += merged.Applied;
            skipped += merged.Skipped;

            cursor = page.Value.Since;
            _state.Save(_state.Current with { PullCursor = cursor });

            if (!page.Value.HasMore) break;
        }

        return Result.Success(new TaskSyncSummary(0, pulled, applied, skipped, _time.GetUtcNow()));
    }

    /// <summary>
    /// Push, then pull.
    /// <para>
    /// In that order because the pull is what tells this device it is up to date,
    /// and a pull that ran first would say so while local work was still
    /// unsent. A push that fails stops the exchange rather than being followed by
    /// a pull: the failure is almost always the service being unreachable, and a
    /// second call to say the same thing is a second thing for a person to read.
    /// </para>
    /// </summary>
    public async Task<Result<TaskSyncSummary>> SyncAsync(CancellationToken cancellationToken = default)
    {
        var push = await PushAsync(cancellationToken).ConfigureAwait(false);
        if (push.IsFailure) return push;

        var pull = await PullAsync(cancellationToken).ConfigureAwait(false);
        if (pull.IsFailure) return pull;

        return Result.Success(new TaskSyncSummary(
            push.Value.Pushed,
            pull.Value.Pulled,
            pull.Value.Applied,
            pull.Value.Skipped,
            _time.GetUtcNow()));
    }

    /// <summary>The two answers that mean "that cursor is no longer one you can
    /// resume from", which the device recovers from by forgetting it. Neither
    /// says anything about the owner's documents, so starting over loses nothing
    /// but the position.</summary>
    private static bool Retired(string code) =>
        code is SyncErrorCodes.SyncCursorExpired or SyncErrorCodes.SyncCursorMalformed;

    /// <summary>
    /// How far the watermark may move once a batch has been accepted, or null
    /// when it may not move at all.
    /// <para>
    /// The last stamp in the batch on the final batch, because everything that
    /// was selected has then been sent — including a whole backlog imported from
    /// one clock reading, which would otherwise be re-pushed on every sync for
    /// ever. Anywhere else it is the highest stamp strictly below the batch's
    /// last, because a task sharing that last stamp may still be waiting in the
    /// next batch and the selection would never offer it again.
    /// </para>
    /// <para>
    /// Null when a whole batch shares one stamp: there is nowhere safe to move
    /// to, so the batch is simply sent again next run. Free under a
    /// whole-document upsert, and the alternative is losing the tasks that share
    /// it.
    /// </para>
    /// </summary>
    private static DateTimeOffset? WatermarkAfter(IReadOnlyList<TaskItem> batch, bool final)
    {
        if (final) return batch[^1].UpdatedAt;

        var boundary = batch[^1].UpdatedAt;

        return batch
            .Where(task => task.UpdatedAt < boundary)
            .Select(task => (DateTimeOffset?)task.UpdatedAt)
            .Max();
    }
}
