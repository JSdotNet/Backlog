using System.Collections.Concurrent;
using System.Globalization;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;

namespace Backlog.Modules.Sync.Adapters;

/// <summary>
/// In-memory stand-in that the Cosmos-backed notification replica replaces — the status and
/// shape of <see cref="InMemoryAnnotationReplica"/>: one partition per owner, so another
/// owner's notification is unreachable by construction, and a per-owner sequence number
/// standing in for Cosmos's <c>_ts</c>. Nothing expires here; retention is the Cosmos TTL's.
/// </summary>
public sealed class InMemoryNotificationReplica : INotificationReplica
{
    private readonly ConcurrentDictionary<OwnerId, OwnerPartition> _byOwner = new();

    public Task<NotificationUpsertResult> Upsert(OwnerScope scope, IReadOnlyList<NotificationChange> changes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var partition = Partition(scope.OwnerId);
        var accepted = 0;
        var notOwned = new List<Guid>();

        foreach (var change in changes)
        {
            switch (partition.Write(change, scope.DeviceId.Value))
            {
                case WriteOutcome.Written:
                    accepted++;
                    break;
                case WriteOutcome.NotOwned:
                    notOwned.Add(change.Id);
                    break;
            }
        }

        return Task.FromResult(new NotificationUpsertResult(accepted, notOwned));
    }

    public Task<NotificationReplicaPage> ReadChanges(OwnerId owner, NotificationReplicaCursor? cursor, int maxItems, CancellationToken cancellationToken = default)
    {
        // The handler verifies a cursor against the caller first; one for another owner here
        // is a bug, and an empty page would hide it.
        if (cursor is { } resume && resume.Owner != owner)
        {
            throw new InvalidOperationException(
                "The cursor belongs to another owner. Verify it against the caller before it gets here.");
        }

        if (!_byOwner.TryGetValue(owner, out var partition))
        {
            return Task.FromResult(new NotificationReplicaPage([], new NotificationReplicaCursor(owner, "0"), HasMore: false));
        }

        var after = cursor is { } from && long.TryParse(from.Continuation, CultureInfo.InvariantCulture, out var position) ? position : 0;
        return Task.FromResult(partition.Read(owner, after, maxItems));
    }

    public Task<NotificationReplyStoreOutcome> StoreReply(OwnerScope scope, NotificationReplyPost reply, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reply);

        return Task.FromResult(Partition(scope.OwnerId).StoreReply(reply, scope));
    }

    public Task<IReadOnlyList<NotificationReplyRecord>> ListReplies(OwnerScope scope, Guid ownerInstanceId, int maxItems, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byOwner.TryGetValue(scope.OwnerId, out var partition)
            ? partition.ListReplies(scope.DeviceId.Value, ownerInstanceId, maxItems)
            : (IReadOnlyList<NotificationReplyRecord>)[]);

    public Task<bool> DeleteReply(OwnerScope scope, Guid replyId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byOwner.TryGetValue(scope.OwnerId, out var partition) && partition.DeleteReply(scope.DeviceId.Value, replyId));

    private OwnerPartition Partition(OwnerId owner) => _byOwner.GetOrAdd(owner, _ => new OwnerPartition());

    private enum WriteOutcome
    {
        Written,
        Stale,
        NotOwned
    }

    /// <summary>One owner's notifications, their feed, and the replies to them. One lock over
    /// all three, so a reply is checked against the notification as it stands.</summary>
    private sealed class OwnerPartition
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<Guid, StoredNotification> _notifications = [];
        private readonly Dictionary<Guid, StoredReply> _replies = [];
        private long _sequence;

        internal WriteOutcome Write(NotificationChange change, Guid deviceId)
        {
            lock (_gate)
            {
                if (_notifications.TryGetValue(change.Id, out var held))
                {
                    if (held.Change.OwnerInstanceId != change.OwnerInstanceId) return WriteOutcome.NotOwned;
                    if (change.UpdatedAt < held.Change.UpdatedAt) return WriteOutcome.Stale;
                }

                _notifications[change.Id] = new StoredNotification(change, deviceId, ++_sequence);
                return WriteOutcome.Written;
            }
        }

        internal NotificationReplicaPage Read(OwnerId owner, long after, int maxItems)
        {
            lock (_gate)
            {
                var page = _notifications.Values
                    .Where(stored => stored.Sequence > after)
                    .OrderBy(stored => stored.Sequence)
                    .Take(maxItems)
                    .ToList();

                var position = page.Count > 0 ? page[^1].Sequence : _sequence;

                return new NotificationReplicaPage(
                    [.. page.Select(stored => new NotificationChangeRecord(stored.Change, stored.WriterDeviceId, stored.Sequence))],
                    new NotificationReplicaCursor(owner, position.ToString(CultureInfo.InvariantCulture)),
                    page.Count == maxItems);
            }
        }

        internal NotificationReplyStoreOutcome StoreReply(NotificationReplyPost reply, OwnerScope scope)
        {
            lock (_gate)
            {
                if (!_notifications.TryGetValue(reply.NotificationId, out var notification)
                    || notification.Change.OwnerInstanceId != reply.OwnerInstanceId)
                {
                    return NotificationReplyStoreOutcome.NotificationNotFound;
                }

                if (_replies.ContainsKey(reply.Id)) return NotificationReplyStoreOutcome.AlreadyStored;

                _replies[reply.Id] = new StoredReply(
                    new NotificationReplyRecord(reply, scope.OwnerId.Value, scope.DeviceId.Value, ++_sequence));

                return NotificationReplyStoreOutcome.Stored;
            }
        }

        internal IReadOnlyList<NotificationReplyRecord> ListReplies(Guid deviceId, Guid ownerInstanceId, int maxItems)
        {
            lock (_gate)
            {
                if (!SpeaksFor(deviceId, ownerInstanceId)) return [];

                return
                [
                    .. _replies.Values
                        .Where(stored => stored.Record.Reply.OwnerInstanceId == ownerInstanceId)
                        .OrderBy(stored => stored.Record.ServerTimestamp)
                        .Take(maxItems)
                        .Select(stored => stored.Record),
                ];
            }
        }

        internal bool DeleteReply(Guid deviceId, Guid replyId)
        {
            lock (_gate)
            {
                return _replies.TryGetValue(replyId, out var stored)
                    && SpeaksFor(deviceId, stored.Record.Reply.OwnerInstanceId)
                    && _replies.Remove(replyId);
            }
        }

        /// <summary>Whether <paramref name="deviceId"/> last wrote a notification of the
        /// instance: the device that speaks for it.</summary>
        private bool SpeaksFor(Guid deviceId, Guid ownerInstanceId) =>
            _notifications.Values.Any(stored => stored.WriterDeviceId == deviceId && stored.Change.OwnerInstanceId == ownerInstanceId);
    }

    private sealed record StoredNotification(NotificationChange Change, Guid WriterDeviceId, long Sequence);

    private sealed record StoredReply(NotificationReplyRecord Record);
}
