using Backlog.Modules.Inbox.Abstractions.Services;
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
public sealed record TaskSyncSummary(int Pushed, int Pulled, int Applied, int Skipped, DateTimeOffset At)
{
    /// <summary>Tasks offered in a push the replica did not take, because it
    /// already held a later version. Never healthy either: the task stamps an
    /// edit past the copy it edits, so a refusal means two devices really did
    /// edit the same task, and this device's edit is the one that lost.</summary>
    public int Refused { get; init; }
}

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

    /// <summary>The kind token a capture document carries. The same literal
    /// <see cref="TaskReplicaMerge"/> reads and the service writes, duplicated
    /// for the reason given there.</summary>
    private const string CaptureType = "capture";

    private readonly TaskSyncClient _client;
    private readonly TaskReplicaMerge _merge;
    private readonly ITaskRepository _tasks;
    private readonly ITaskSyncStateStore _state;
    private readonly IDeviceCredentialStore _credentials;
    private readonly TimeProvider _time;
    private readonly IInboxCaptureOutbox? _outbox;
    private readonly SyncActivityLog? _activity;

    /// <param name="credentials">Whose device this is. Read before every push
    /// and pull to check the progress in <paramref name="state"/> belongs to the
    /// same owner and device — see <see cref="ReconcileIdentity"/>.</param>
    /// <param name="outbox">The Inbox's acknowledgements waiting to leave this
    /// machine, or null on a head that has no inbox store. Optional by
    /// construction, so the mobile head composes exactly as it did.</param>
    /// <param name="activity">Where each document that leaves is written down
    /// by name, or null on a head with nothing to show one in. What arrives is
    /// recorded by the merge, which is the one that knows whether it was
    /// kept.</param>
    public TaskSyncSession(
        TaskSyncClient client,
        TaskReplicaMerge merge,
        ITaskRepository tasks,
        ITaskSyncStateStore state,
        IDeviceCredentialStore credentials,
        TimeProvider time,
        IInboxCaptureOutbox? outbox = null,
        SyncActivityLog? activity = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(merge);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(time);

        _client = client;
        _merge = merge;
        _tasks = tasks;
        _state = state;
        _credentials = credentials;
        _time = time;
        _outbox = outbox;
        _activity = activity;
    }

    /// <summary>
    /// Starts the progress over when it was recorded for a different identity.
    /// <para>
    /// The watermark says what one owner's replica has accepted from this
    /// device, and the cursor is signed for one owner. Neither survives the
    /// device becoming somebody else — which is what forgetting the credential and
    /// registering or pairing again does — yet the file that holds them is not
    /// tied to the credential and used to sit untouched through it. The result
    /// was a device that joined a new owner, re-sent only what it had edited
    /// since, and left the new replica without everything it had pushed to the old
    /// one: a second machine pairing in saw a fraction of the first one's tasks
    /// and none of its sessions, with nothing anywhere to say why.
    /// </para>
    /// <para>
    /// The cursor half healed itself — the service refuses a cursor signed for
    /// another owner and the pull starts over — which is exactly why the push half
    /// went unnoticed. Both are reset here so the two cannot disagree again, and a
    /// state with no identity recorded at all is reset too: it predates this
    /// check, and a watermark of unknown provenance is the gap, not a saving.
    /// </para>
    /// <para>
    /// Called at the top of both halves rather than once in
    /// <see cref="SyncAsync"/>, because a caller may run either alone and the
    /// invariant is the state's, not the exchange's.
    /// </para>
    /// </summary>
    private void ReconcileIdentity()
    {
        if (_credentials.Current is not { } me) return;

        var state = _state.Current;
        if (state.OwnerId == me.OwnerId && state.DeviceId == me.DeviceId) return;

        _state.Save(new TaskSyncState(DateTimeOffset.MinValue, null, me.OwnerId, me.DeviceId));
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
    /// <para>
    /// <b>After the tasks, the inbox's acknowledgements.</b> A capture this
    /// desktop routed or archived is told to the replica as a tombstone of the
    /// capture document, through this same push and counted in
    /// <see cref="TaskSyncSummary.Pushed"/>. Its own outbox rather than the task
    /// watermark, because a capture is not a row in the task store and never
    /// was; the flag on the inbox item is cleared only once the replica has
    /// accepted the batch it went in, so an acknowledgement decided offline
    /// waits for the first push that succeeds and a failed one leaves it — and
    /// every one after it — exactly where it was.
    /// </para>
    /// </summary>
    public async Task<Result<TaskSyncSummary>> PushAsync(CancellationToken cancellationToken = default)
    {
        ReconcileIdentity();

        var watermark = _state.Current.PushWatermark;

        var pending = (await _tasks.ListChangedSinceAsync(watermark, cancellationToken).ConfigureAwait(false))
            .ToList();

        var pushed = 0;
        var refused = 0;

        for (var start = 0; start < pending.Count; start += PushBatchSize)
        {
            var batch = pending.GetRange(start, Math.Min(PushBatchSize, pending.Count - start));
            var changes = batch.Select(TaskReplicaMerge.ToChange).ToList();

            var response = await _client.PushAsync(changes, cancellationToken).ConfigureAwait(false);
            if (response.IsFailure) return Result.Failure<TaskSyncSummary>(response.Error);

            // A 200 with fewer accepted than sent is the replica keeping a later
            // version of the rest. The response does not say which, and the
            // watermark still moves past them — offered again they would be
            // refused again — so the count is where the loss is said.
            var refusedInBatch = Math.Max(0, batch.Count - response.Value.Accepted);
            pushed += response.Value.Accepted;
            refused += refusedInBatch;

            // After the service answered and not before: the log says what left,
            // and a batch the replica rejected outright never did. Nor did a
            // batch it took nothing from — the echo of what this device pulled
            // last time, which sits above the watermark like an edit and which
            // the replica answers with a count of zero. A person completing a
            // task on the other machine saw it listed here as sent back to them
            // a moment after it arrived, and read that as this machine
            // overwriting their work. A batch it took in part is logged with the
            // shortfall on every line, because the response cannot say which of
            // them stayed behind; the all-or-nothing echo is the case that
            // happens on every cycle, and it is listed nowhere.
            if (response.Value.Accepted > 0)
            {
                foreach (var task in batch)
                {
                    _activity?.Record(
                        SyncDirection.Sent, SyncItemKind.Task, task.Id.ToString("D"), task.Title,
                        SentNote(task.DeletedAt is null ? null : "deleted", refusedInBatch, batch.Count));
                }
            }

            if (WatermarkAfter(batch, final: start + batch.Count >= pending.Count) is { } advanced)
            {
                _state.Save(_state.Current with { PushWatermark = advanced });
            }
        }

        if (_outbox is not null)
        {
            var acknowledgements = (await _outbox.ListPendingAsync(cancellationToken).ConfigureAwait(false)).ToList();

            // The same batches the tasks go in, for the same reason and one
            // more: the service refuses a push over its limit outright, so an
            // outbox that grew past it offline would otherwise send one
            // request that can never be accepted, on every tick, for ever.
            // Each batch is marked sent as it lands, so a failure part way
            // keeps what got through and leaves the rest exactly as it was.
            for (var start = 0; start < acknowledgements.Count; start += PushBatchSize)
            {
                var batch = acknowledgements.GetRange(start, Math.Min(PushBatchSize, acknowledgements.Count - start));
                var tombstones = batch
                    .Select(ack => new TaskChange(ack.CaptureId, ack.AcknowledgedAt, ack.AcknowledgedAt, CapturePayload(ack)))
                    .ToList();

                var response = await _client.PushAsync(tombstones, cancellationToken).ConfigureAwait(false);
                if (response.IsFailure) return Result.Failure<TaskSyncSummary>(response.Error);

                pushed += response.Value.Accepted;

                foreach (var ack in batch)
                {
                    _activity?.Record(
                        SyncDirection.Sent, SyncItemKind.Capture, ack.CaptureId.ToString("D"), ack.Title, "acknowledged");
                }

                await _outbox
                    .MarkSentAsync([.. batch.Select(ack => ack.CaptureId)], cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return Result.Success(new TaskSyncSummary(pushed, 0, 0, 0, _time.GetUtcNow()) { Refused = refused });
    }

    /// <summary>The note a sent task's log line carries: what was already known
    /// about it, and — when the replica took the batch only in part — how much
    /// of the batch it did not take, since the response cannot say which.</summary>
    private static string? SentNote(string? note, int refusedInBatch, int batchCount)
    {
        if (refusedInBatch == 0) return note;

        var shortfall = $"{refusedInBatch} of {batchCount} in this batch refused as stale";
        return note is null ? shortfall : $"{note}; {shortfall}";
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
    /// .devbook/arc42/adr/0005 section Consequences asks to be loud about, and quietly
    /// starting over is exactly the quiet.
    /// </para>
    /// </summary>
    public async Task<Result<TaskSyncSummary>> PullAsync(CancellationToken cancellationToken = default)
    {
        ReconcileIdentity();

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
                .ApplyAsync(page.Value.Tasks, cancellationToken)
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
    /// <para>
    /// The order is safe because the replica refuses a stale push
    /// (<c>TaskChangePrecedence</c>): a device holding an older copy of a task
    /// the other machine has since edited sends it, is refused, and takes the
    /// newer document on the pull that follows. Before the replica compared
    /// stamps this order let the stale copy land on top of the newer one, which
    /// is what pulling first would have prevented; the refusal makes the order a
    /// matter of what the summary reads rather than of what survives.
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

    /// <summary>
    /// The tombstone's payload: the capture as the service wrote it, as far as
    /// this desktop can reconstruct one. The replica is whole-document
    /// last-write-wins, so the tombstone has to carry a whole document, and the
    /// three literals are the service's own — <c>capture</c>, <c>draft</c>,
    /// <c>medium</c>. <c>SourceInboxId</c> says which end acknowledged, which
    /// nothing reads yet and a person looking at the document may.
    /// </summary>
    private static TaskPayload CapturePayload(InboxCaptureAckDto ack) => new(
        ack.Title,
        ContentMd: string.Empty,
        CaptureType,
        Status: "draft",
        Priority: "medium",
        Order: 0,
        Area: null,
        ack.CapturedAt,
        SourceInboxId: "desktop",
        RecurrenceSourceId: null,
        DueOn: null,
        RemindAt: null,
        Recurrence: null,
        InMyDayOn: null,
        View: null,
        Effort: null,
        ImportPlanId: null,
        ImportItemId: null,
        AttachmentPath: null,
        Tags: [],
        RepoIds: [],
        DependsOn: [],
        SubItems: [],
        UsageEvents: [],
        ProjectionRefs: []);

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
