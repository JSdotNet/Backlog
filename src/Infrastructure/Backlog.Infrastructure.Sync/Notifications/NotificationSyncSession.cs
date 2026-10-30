using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sync.Notifications;

/// <summary>What one exchange did, for a screen to show and a test to assert.</summary>
/// <param name="Pushed">Notifications the replica took.</param>
/// <param name="Accepted">Replies accepted as an answer.</param>
/// <param name="Rejected">Replies rejected with a reason.</param>
/// <param name="PolledReplies">Whether the replies were listed at all — only while a question
/// is open.</param>
/// <param name="At">When the exchange finished.</param>
public sealed record NotificationSyncSummary(int Pushed, int Accepted, int Rejected, bool PolledReplies, DateTimeOffset At);

/// <summary>
/// One exchange of the owner instance with the notification replica (local ADR 0039,
/// <c>.devbook/arc42/06-runtime-view.md#notification-reply</c>):
/// <list type="number">
/// <item>push the notifications published here, and the ones that settled here, since the last
/// push;</item>
/// <item>while any question is open, list the replies addressed to this instance and hand each,
/// in the order the replica took them, to <see cref="INotificationReplies.AcceptReplyAsync"/> —
/// the owner's one gate, so the first valid reply wins and every later one is rejected with its
/// reason;</item>
/// <item>delete each reply once processed, and push the notifications the replies settled or
/// added a rejection to.</item>
/// </list>
/// <para>
/// The owner checks the account again before it accepts: a reply the replica stamped with
/// another owner is rejected as <c>reply.foreign_owner</c> — the service's partition already
/// keeps one out, and this is the second check ADR 0039 §2 asks for.
/// </para>
/// <para>
/// Processing is idempotent, so a reply whose delete failed is safe to see again: one that is
/// already the answer, or already rejected, is only deleted.
/// </para>
/// </summary>
public sealed class NotificationSyncSession
{
    /// <summary>How many notifications go in one push — half the service's cap, since one
    /// may weigh 64 KB.</summary>
    internal const int PushBatchSize = 50;

    /// <summary>How long a settled notification is remembered after its last push — the
    /// replica's own retention, after which it is gone there too.</summary>
    internal static readonly TimeSpan SettledRetention = TimeSpan.FromDays(30);

    /// <summary>How often an open notification is pushed again although nothing changed, so
    /// its replica copy's backstop expiry (90 days) never catches up with a question still
    /// waiting on a running desktop.</summary>
    internal static readonly TimeSpan OpenRefresh = TimeSpan.FromDays(7);

    /// <summary>How often an open question is pushed again although nothing changed, so a
    /// viewer reading its stamp can tell a desktop that is running from one that is asleep
    /// (<c>.devbook/domain/notifications/features.md#answering-from-another-device</c>): the
    /// phone says "delivers on wake" once the owner's newest stamp is older than
    /// <see cref="PresenceWindow"/>.</summary>
    public static readonly TimeSpan PresenceRefresh = TimeSpan.FromMinutes(10);

    /// <summary>How old the newest stamp of an owner's open questions may be before a viewer
    /// reads the owner as offline: one <see cref="PresenceRefresh"/>, a cycle of the worker
    /// that pushes it, and a margin for a slow pull.</summary>
    public static readonly TimeSpan PresenceWindow = TimeSpan.FromMinutes(15);

    /// <summary>The most rejections one notification carries. More is a viewer replying in a
    /// loop; the oldest drop off.</summary>
    internal const int MaxRejectionsPerNotification = 20;

    private readonly NotificationSyncClient _client;
    private readonly INotificationReplication _notifications;
    private readonly INotificationReplies _replies;
    private readonly INotificationSyncStateStore _state;
    private readonly IDeviceCredentialStore _credentials;
    private readonly IDeviceIdentitySource _identity;
    private readonly TimeProvider _time;
    private readonly INotifications? _owner;
    private readonly ILogger _log;

    /// <summary>Why a notification the replica holds under another owner instance is closed
    /// here: the same id published on two desktops — a question both read from the same source —
    /// belongs to the desktop that put it on the replica first, and this one stays a viewer of it.</summary>
    public const string OwnedElsewhereReason = "Owned by another desktop";

    public NotificationSyncSession(
        NotificationSyncClient client,
        INotificationReplication notifications,
        INotificationReplies replies,
        INotificationSyncStateStore state,
        IDeviceCredentialStore credentials,
        IDeviceIdentitySource identity,
        TimeProvider time,
        ILogger<NotificationSyncSession>? log = null,
        INotifications? owner = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(replies);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(time);

        _client = client;
        _notifications = notifications;
        _replies = replies;
        _state = state;
        _credentials = credentials;
        _identity = identity;
        _time = time;
        _owner = owner;
        _log = log ?? NullLogger<NotificationSyncSession>.Instance;
    }

    /// <summary>Push, then — only while a question is open — process the replies and push what
    /// they changed. Nothing open and nothing new means no request at all.</summary>
    public Task<Result<NotificationSyncSummary>> SyncAsync(CancellationToken cancellationToken = default) =>
        SyncAsync(pollReplies: true, cancellationToken);

    /// <summary>
    /// The same exchange, with the reply listing left to the caller. <paramref name="pollReplies"/>
    /// false is the scheduled tick while the desktop's nudge socket is open (local ADR 0039 §4):
    /// a reply then announces itself, so the tick only pushes, and the sync service is not woken
    /// every two minutes to say there is nothing. The cycle that settles a question still lists
    /// the replies once, whatever this says.
    /// </summary>
    public async Task<Result<NotificationSyncSummary>> SyncAsync(bool pollReplies, CancellationToken cancellationToken = default)
    {
        if (_credentials.Current is not { } me)
        {
            return Result.Failure<NotificationSyncSummary>(Error.Conflict(
                "sync.not_paired", "This device is not paired, so it has no notifications to carry."));
        }

        ReconcileIdentity(me);

        var open = await _notifications.ListOpenAsync(cancellationToken).ConfigureAwait(false);
        MarkLocalChanges(open);

        var pushed = await PushDirtyAsync(cancellationToken).ConfigureAwait(false);
        if (pushed.IsFailure) return Result.Failure<NotificationSyncSummary>(pushed.Error);

        // Replies are listed while a question is open, and once more in the cycle that pushes a
        // notification as settled: a reply that reached the service before the question closed
        // here is then rejected as closed rather than left waiting for the backstop expiry.
        if ((!pollReplies || !open.Any(notification => notification.Kind == NotificationKind.Question)) && !pushed.Value.Settled)
        {
            Prune();
            return Result.Success(new NotificationSyncSummary(pushed.Value.Pushed, 0, 0, PolledReplies: false, _time.GetUtcNow()));
        }

        var processed = await ProcessRepliesAsync(me, cancellationToken).ConfigureAwait(false);
        if (processed.IsFailure) return Result.Failure<NotificationSyncSummary>(processed.Error);

        var pushedAfter = await PushDirtyAsync(cancellationToken).ConfigureAwait(false);
        if (pushedAfter.IsFailure) return Result.Failure<NotificationSyncSummary>(pushedAfter.Error);

        Prune();

        return Result.Success(new NotificationSyncSummary(
            pushed.Value.Pushed + pushedAfter.Value.Pushed,
            processed.Value.Accepted,
            processed.Value.Rejected,
            PolledReplies: true,
            _time.GetUtcNow()));
    }

    /// <summary>Follows a new pairing. Under the same owner the instance keeps its progress and
    /// pushes everything it tracks again under the new device; under another owner it starts
    /// over, since nothing it pushed is that owner's.</summary>
    private void ReconcileIdentity(DeviceCredential me)
    {
        var state = _state.Current;
        if (state.OwnerId == me.OwnerId && state.DeviceId == me.DeviceId) return;

        if (state.OwnerId == me.OwnerId)
        {
            // Paired again under the same owner: the notifications are still this
            // instance's, so keep what was decided about their replies and push every one
            // again under the new device, which then speaks for the instance.
            _state.Save(state with
            {
                DeviceId = me.DeviceId,
                Tracked = state.Tracked.ToDictionary(entry => entry.Key, entry => entry.Value with { Dirty = true }),
            });
            return;
        }

        _state.Save(NotificationSyncState.Nowhere with { OwnerId = me.OwnerId, DeviceId = me.DeviceId });
    }

    /// <summary>A notification open here that was never pushed is new; one pushed open that is
    /// no longer open settled here; one pushed open a week ago is due to refresh its replica
    /// copy's expiry; an open question pushed more than <see cref="PresenceRefresh"/> ago is
    /// due to say this desktop is still running. All four go on the next push.</summary>
    private void MarkLocalChanges(IReadOnlyList<NotificationSnapshot> open)
    {
        var tracked = new Dictionary<Guid, TrackedNotification>(_state.Current.Tracked);
        var openIds = open.Select(notification => notification.Id).ToHashSet();
        var questionIds = open.Where(notification => notification.Kind == NotificationKind.Question).Select(notification => notification.Id).ToHashSet();
        var changed = false;

        foreach (var id in openIds.Where(id => !tracked.ContainsKey(id)))
        {
            tracked[id] = new TrackedNotification(PushedState: null, Dirty: true, RejectedReplies: []);
            changed = true;
        }

        var refreshBefore = _time.GetUtcNow() - OpenRefresh;
        var presenceBefore = _time.GetUtcNow() - PresenceRefresh;

        foreach (var (id, entry) in tracked.ToList())
        {
            if (entry.Dirty || entry.PushedState != NotificationStates.Open) continue;

            if (!openIds.Contains(id)
                || entry.PushedAt is not { } pushedAt
                || pushedAt < refreshBefore
                || (questionIds.Contains(id) && !entry.Withheld && pushedAt < presenceBefore))
            {
                tracked[id] = entry with { Dirty = true };
                changed = true;
            }
        }

        if (changed) _state.Save(_state.Current with { Tracked = tracked });
    }

    /// <summary>Pushes every dirty notification as it stands now, in batches, and marks each
    /// batch pushed once the replica took it. One the service would refuse is left out and
    /// named in a warning rather than sent to have its batch refused whole; one the replica holds
    /// under another owner instance is named in a warning and not offered again. Answers how many
    /// the replica took, and whether any of them went out settled.</summary>
    private async Task<Result<(int Pushed, bool Settled)>> PushDirtyAsync(CancellationToken cancellationToken)
    {
        var dirty = _state.Current.Tracked.Where(entry => entry.Value.Dirty).Select(entry => entry.Key).ToList();
        var pushed = 0;
        var settled = false;

        for (var start = 0; start < dirty.Count; start += PushBatchSize)
        {
            var batch = new List<(NotificationChange Change, NotificationSnapshot Snapshot)>();

            foreach (var id in dirty.Skip(start).Take(PushBatchSize))
            {
                var snapshot = await _notifications.GetAsync(id, cancellationToken).ConfigureAwait(false);
                var entry = _state.Current.Tracked[id];

                if (snapshot is null)
                {
                    Forget(id);
                    continue;
                }

                var change = NotificationPayloads.ToChange(snapshot, entry.RejectedReplies, StampAfter(entry.PushedAt), _credentials.Current?.DeviceName);

                if (NotificationRecordLimits.WhyRefused(change) is { } reason)
                {
                    // Never the body or the title: only the id and the rule.
                    _log.LogWarning(
                        "Leaving notification {NotificationId} out of the push, because the sync service would refuse it. {Reason}",
                        id,
                        reason);

                    Update(id, entry with { Dirty = false, PushedState = change.State, SettledAt = snapshot.SettledAt, PushedAt = change.UpdatedAt, Withheld = true });
                    continue;
                }

                batch.Add((change, snapshot));
            }

            if (batch.Count == 0) continue;

            var response = await _client.PushAsync([.. batch.Select(item => item.Change)], cancellationToken).ConfigureAwait(false);
            if (response.IsFailure) return Result.Failure<(int, bool)>(response.Error);

            pushed += response.Value.Accepted;

            var notOwned = (response.Value.NotOwned ?? []).ToHashSet();

            foreach (var id in notOwned)
            {
                // An id the replica holds under another owner instance: a collision nothing
                // here can mend, so it is named once and not offered on every cycle.
                _log.LogWarning(
                    "The sync service holds notification {NotificationId} under another owner instance, so it was not written.",
                    id);
            }

            foreach (var (change, snapshot) in batch)
            {
                settled |= change.State != NotificationStates.Open;

                Update(change.Id, _state.Current.Tracked[change.Id] with
                {
                    Dirty = false,
                    PushedState = change.State,
                    SettledAt = snapshot.SettledAt,
                    PushedAt = change.UpdatedAt,
                    Withheld = notOwned.Contains(change.Id),
                });
            }

            await CloseOwnedElsewhereAsync(notOwned, cancellationToken).ConfigureAwait(false);
        }

        return (pushed, settled);
    }

    /// <summary>
    /// Closes the local copy of each open notification the replica holds under another owner
    /// instance, and marks it pushed as closed so it is not offered again. A source that names its
    /// notifications by a derived id — one spec-manager question read on two signed-in desktops —
    /// publishes the same id on both, and the replica's one-writer rule keeps the first; the second
    /// desktop then stops asking the person, rather than taking an answer the owner never sees.
    /// </summary>
    private async Task CloseOwnedElsewhereAsync(IReadOnlySet<Guid> notOwned, CancellationToken cancellationToken)
    {
        if (_owner is null) return;

        foreach (var id in notOwned)
        {
            var closed = await _owner.CloseAsync(id, OwnedElsewhereReason, cancellationToken).ConfigureAwait(false);
            if (closed.IsFailure) continue;

            if (_state.Current.Tracked.TryGetValue(id, out var entry))
            {
                Update(id, entry with { Dirty = false, PushedState = NotificationStates.Closed, SettledAt = _time.GetUtcNow() });
            }
        }
    }

    /// <summary>Now, or just after <paramref name="previous"/> when the clock reads earlier:
    /// the replica drops a change older than the one it holds, so a stamp never goes back.</summary>
    private DateTimeOffset StampAfter(DateTimeOffset? previous)
    {
        var now = _time.GetUtcNow();

        return previous is { } last && now <= last ? last.AddMilliseconds(1) : now;
    }

    /// <summary>Processes the replies addressed to this instance, oldest first, and deletes
    /// each once processed.</summary>
    private async Task<Result<(int Accepted, int Rejected)>> ProcessRepliesAsync(DeviceCredential me, CancellationToken cancellationToken)
    {
        var listed = await _client.ListRepliesAsync(_identity.Current.Id, NotificationSyncClient.DefaultMaxReplies, cancellationToken).ConfigureAwait(false);
        if (listed.IsFailure) return Result.Failure<(int, int)>(listed.Error);

        _state.Save(_state.Current with { LastReplyPoll = _time.GetUtcNow() });

        var accepted = 0;
        var rejected = 0;

        // The order the service took them in; its stamp is in seconds, so two replies in the
        // same second are put in the order the people made them.
        foreach (var record in listed.Value.Replies.OrderBy(record => record.ServerTimestamp).ThenBy(record => record.Reply.RepliedAt))
        {
            var reply = record.Reply;
            var snapshot = await _notifications.GetAsync(reply.NotificationId, cancellationToken).ConfigureAwait(false);

            if (!AlreadyProcessed(record, snapshot))
            {
                var outcome = record.OwnerId != me.OwnerId
                    ? Result.Failure<NotificationAnswer>(NotificationsErrors.ReplyForeignOwner())
                    : await _replies.AcceptReplyAsync(ToReply(record, me), cancellationToken).ConfigureAwait(false);

                if (outcome.IsSuccess)
                {
                    accepted++;
                    MarkDirty(reply.NotificationId);
                }
                else
                {
                    rejected++;

                    // Logged by id and code only: an open answer is the person's own text.
                    _log.LogInformation(
                        "Rejected reply {ReplyId} to notification {NotificationId}: {Code}.",
                        reply.Id,
                        reply.NotificationId,
                        outcome.Error.Code);

                    // A reply to a notification this instance does not hold has nowhere to
                    // say so; it is only deleted.
                    if (snapshot is not null) Reject(record, outcome.Error);
                }
            }

            var deleted = await _client.DeleteReplyAsync(reply.Id, cancellationToken).ConfigureAwait(false);
            if (deleted.IsFailure && deleted.Error.Code != SyncErrorCodes.NotificationReplyNotFound)
            {
                // Stop here: the reply is still listed next time, and processing it again
                // only deletes it.
                return Result.Failure<(int, int)>(deleted.Error);
            }
        }

        return (accepted, rejected);
    }

    /// <summary>Whether this reply was handled on an earlier run whose delete did not land: it
    /// is the answer already, or it is already rejected.</summary>
    private bool AlreadyProcessed(NotificationReplyRecord record, NotificationSnapshot? snapshot) =>
        snapshot?.Answer?.ReplyId == record.Reply.Id
        || (_state.Current.Tracked.TryGetValue(record.Reply.NotificationId, out var entry)
            && entry.RejectedReplies.Any(rejection => rejection.ReplyId == record.Reply.Id));

    /// <summary>The reply as the owner's gate takes it. The device it came from is the sync
    /// device the service stamped — except this instance's own, which is named by its
    /// installation identity, so an owner-only question still takes a reply made here.</summary>
    private NotificationReply ToReply(NotificationReplyRecord record, DeviceCredential me) =>
        new(
            record.Reply.Id,
            record.Reply.NotificationId,
            record.Reply.OwnerInstanceId,
            record.FromDeviceId == me.DeviceId ? _identity.Current.Id : record.FromDeviceId,
            record.Reply.ChoiceIds ?? [],
            record.Reply.Text,
            record.Reply.RepliedAt,
            record.Reply.Note);

    private void Reject(NotificationReplyRecord record, Error error)
    {
        var id = record.Reply.NotificationId;
        var entry = _state.Current.Tracked.TryGetValue(id, out var held)
            ? held
            : new TrackedNotification(PushedState: null, Dirty: true, RejectedReplies: []);

        var rejection = new NotificationReplyRejection(
            record.Reply.Id,
            record.FromDeviceId,
            NotificationReplyReasons.For(error.Code),
            error.Code,
            error.Message,
            _time.GetUtcNow());

        Update(id, entry with
        {
            Dirty = true,
            RejectedReplies = [.. entry.RejectedReplies.Append(rejection).TakeLast(MaxRejectionsPerNotification)],
        });
    }

    private void MarkDirty(Guid id)
    {
        var entry = _state.Current.Tracked.TryGetValue(id, out var held)
            ? held
            : new TrackedNotification(PushedState: null, Dirty: true, RejectedReplies: []);

        Update(id, entry with { Dirty = true });
    }

    private void Update(Guid id, TrackedNotification entry) =>
        _state.Save(_state.Current with { Tracked = new Dictionary<Guid, TrackedNotification>(_state.Current.Tracked) { [id] = entry } });

    private void Forget(Guid id)
    {
        var tracked = new Dictionary<Guid, TrackedNotification>(_state.Current.Tracked);
        if (tracked.Remove(id)) _state.Save(_state.Current with { Tracked = tracked });
    }

    /// <summary>Forgets settled notifications the replica has expired by now.</summary>
    private void Prune()
    {
        var cutoff = _time.GetUtcNow() - SettledRetention;
        var stale = _state.Current.Tracked
            .Where(entry => !entry.Value.Dirty
                && entry.Value.PushedState is not null and not NotificationStates.Open
                && entry.Value.SettledAt is { } settled
                && settled < cutoff)
            .Select(entry => entry.Key)
            .ToList();

        if (stale.Count == 0) return;

        var tracked = new Dictionary<Guid, TrackedNotification>(_state.Current.Tracked);
        foreach (var id in stale) tracked.Remove(id);
        _state.Save(_state.Current with { Tracked = tracked });
    }
}
