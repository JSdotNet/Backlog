using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backlog.Infrastructure.Cosmos.Notifications;

/// <summary>
/// The notification replica, in Cosmos DB (local ADR 0039): notifications in the
/// <c>notifications</c> container and the replies to them in <c>notificationReplies</c>, both
/// partitioned on <c>/ownerId</c>.
/// <para>
/// The notification side is <see cref="Annotations.CosmosAnnotationReplica"/>'s arrangement,
/// kept as a copy: whole documents in and out, read-compare-write against the etag, the
/// stream change feed's 304 as the only drained signal. One rule is its own: a notification
/// already held is written again only by a push naming the same owner instance, and the device
/// that pushed it last speaks for that instance. The reply side is a mailbox — point writes, a
/// query for the replies addressed to one instance, and deletes.
/// </para>
/// </summary>
internal sealed class CosmosNotificationReplica : INotificationReplica
{
    private const string UnavailableMessage = "The notification replica is not available yet. Try again shortly.";

    private const int WriteAttempts = 3;

    private readonly CosmosContainerHandle _notifications;
    private readonly CosmosContainerHandle _replies;
    private readonly CosmosOptions _options;
    private readonly ILogger<CosmosNotificationReplica> _log;

    public CosmosNotificationReplica(IServiceProvider services, IOptions<CosmosOptions> options, ILogger<CosmosNotificationReplica> log)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _log = log;
        _notifications = new CosmosContainerHandle(services, _options.DatabaseName, _options.NotificationsContainerName, UnavailableMessage);
        _replies = new CosmosContainerHandle(services, _options.DatabaseName, _options.NotificationRepliesContainerName, UnavailableMessage);
    }

    public async Task<NotificationUpsertResult> Upsert(OwnerScope scope, IReadOnlyList<NotificationChange> changes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        if (changes.Count == 0) return new NotificationUpsertResult(0, [], []);

        var container = await _notifications.ContainerAsync(cancellationToken).ConfigureAwait(false);
        var partition = new PartitionKey(ReplicaDocumentSerialization.Key(scope.OwnerId.Value));
        var notOwned = new ConcurrentQueue<Guid>();
        var moved = new ConcurrentDictionary<NotificationChange, bool>(ReferenceEqualityComparer.Instance);

        var accepted = await ReplicaWrites.WriteAllAsync(
            changes,
            change => ReplicaDocumentSerialization.Key(change.Id),
            async (change, token) =>
            {
                var outcome = await WriteAsync(container, partition, scope, change, token).ConfigureAwait(false);
                if (outcome is WriteOutcome.NotOwned) notOwned.Enqueue(change.Id);
                if (outcome is WriteOutcome.Transition) moved[change] = true;
                return outcome is WriteOutcome.Written or WriteOutcome.Transition;
            },
            cancellationToken).ConfigureAwait(false);

        // In the push's own order, whatever order the parallel writes finished in.
        return new NotificationUpsertResult(accepted, [.. notOwned], [.. changes.Where(moved.ContainsKey)]);
    }

    internal enum WriteOutcome
    {
        /// <summary>Written, and new here or in another state than the one held: a phone is
        /// pushed (ADR 0039 §4).</summary>
        Transition,

        /// <summary>Written, in the state already held — the owner's presence refresh.</summary>
        Written,
        Stale,
        NotOwned
    }

    private async Task<WriteOutcome> WriteAsync(Container container, PartitionKey partition, OwnerScope scope, NotificationChange change, CancellationToken cancellationToken)
    {
        var id = ReplicaDocumentSerialization.Key(change.Id);

        for (var attempt = 1; attempt <= WriteAttempts; attempt++)
        {
            NotificationDocument? held = null;
            string? etag = null;

            try
            {
                var read = await container.ReadItemAsync<NotificationDocument>(id, partition, cancellationToken: cancellationToken).ConfigureAwait(false);
                held = read.Resource;
                etag = read.ETag;
            }
            catch (CosmosException failure) when (failure.StatusCode is HttpStatusCode.NotFound)
            {
                // Nothing held: a notification published since the last push.
            }
            catch (CosmosException failure)
            {
                throw Fault(failure);
            }

            var decision = Decide(held, change);
            if (decision is WriteOutcome.NotOwned or WriteOutcome.Stale) return decision;

            var document = NotificationDocumentFactory.From(scope, change, _options);

            try
            {
                if (held is null)
                {
                    await container.CreateItemAsync(document, partition, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await container.ReplaceItemAsync(document, id, partition, new ItemRequestOptions { IfMatchEtag = etag }, cancellationToken).ConfigureAwait(false);
                }

                return decision;
            }
            catch (CosmosException failure) when (failure.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
            {
                _log.LogDebug(
                    "Notification {NotificationId} changed under a push from device {DeviceId}; re-reading (attempt {Attempt} of {Attempts}).",
                    change.Id, scope.DeviceId.Value, attempt, WriteAttempts);
            }
            catch (CosmosException failure)
            {
                throw Fault(failure);
            }
        }

        throw new SyncReplicaException(SyncErrorCodes.ReplicaBusy, "The notification replica is busy. Try again shortly.");
    }

    /// <summary>
    /// What writing <paramref name="change"/> over <paramref name="held"/> — the document read
    /// just before, or null when there is none — would be: refused as another owner instance's
    /// or as older than the one held, or written, and then a transition when it is new here or
    /// moves the state held (ADR 0039 §4: only those reach a phone).
    /// </summary>
    internal static WriteOutcome Decide(NotificationDocument? held, NotificationChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (held is null) return WriteOutcome.Transition;
        if (!string.Equals(held.OwnerInstanceId, ReplicaDocumentSerialization.Key(change.OwnerInstanceId), StringComparison.Ordinal)) return WriteOutcome.NotOwned;
        if (change.UpdatedAt < held.UpdatedAt) return WriteOutcome.Stale;

        return string.Equals(held.State, change.State, StringComparison.Ordinal) ? WriteOutcome.Written : WriteOutcome.Transition;
    }

    public async Task<NotificationReplicaPage> ReadChanges(OwnerId owner, NotificationReplicaCursor? cursor, int maxItems, CancellationToken cancellationToken = default)
    {
        if (cursor is { } resume && resume.Owner != owner)
        {
            throw new InvalidOperationException(
                "The cursor belongs to another owner. Verify it against the caller before it gets here.");
        }

        var key = ReplicaDocumentSerialization.Key(owner.Value);

        var startFrom = cursor is { } from
            ? ChangeFeedStartFrom.ContinuationToken(from.Continuation)
            : ChangeFeedStartFrom.Beginning(FeedRange.FromPartitionKey(new PartitionKey(key)));

        var container = await _notifications.ContainerAsync(cancellationToken).ConfigureAwait(false);
        using var iterator = container.GetChangeFeedStreamIterator(
            startFrom,
            ChangeFeedMode.Incremental,
            new ChangeFeedRequestOptions { PageSizeHint = maxItems });

        ResponseMessage response;
        try
        {
            response = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (CosmosException failure)
        {
            throw failure.StatusCode is HttpStatusCode.BadRequest ? ReplicaFailures.Expired(failure) : ReplicaFailures.Unavailable(UnavailableMessage, failure);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.NotModified) return Page([], owner, response.ContinuationToken, drained: true);
            if (response.StatusCode is HttpStatusCode.BadRequest) throw ReplicaFailures.Expired(null);
            if (!response.IsSuccessStatusCode) throw new SyncReplicaException(SyncErrorCodes.ReplicaUnavailable, UnavailableMessage);

            var documents = JsonSerializer.Deserialize<ChangeFeedPage>(response.Content, ReplicaDocumentSerialization.Options)?.Documents ?? [];

            // Belt against the SDK changing what a continuation means: a page carrying
            // another partition's documents is discarded whole.
            if (documents.Any(document => !string.Equals(document.OwnerId, key, StringComparison.Ordinal)))
            {
                _log.LogError(
                    "A notification change feed page for owner {OwnerId} contained documents from another partition. Discarding it.",
                    key);

                return Page([], owner, continuation: null, drained: true);
            }

            return Page(documents, owner, response.ContinuationToken, drained: false);
        }
    }

    internal static NotificationReplicaPage Page(IReadOnlyList<NotificationDocument> documents, OwnerId owner, string? continuation, bool drained) =>
        new(
            [.. documents.Select(NotificationDocumentFactory.ToRecord).OfType<NotificationChangeRecord>()],
            new NotificationReplicaCursor(owner, continuation ?? string.Empty),
            !drained);

    public async Task<NotificationReplyStoreOutcome> StoreReply(OwnerScope scope, NotificationReplyPost reply, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reply);

        var partition = new PartitionKey(ReplicaDocumentSerialization.Key(scope.OwnerId.Value));

        // The notification is looked for in the caller's own partition only. A device
        // under another account finds nothing there, which is the service's half of
        // reply.foreign_owner (ADR 0039 §2).
        NotificationDocument notification;
        var notifications = await _notifications.ContainerAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            notification = (await notifications.ReadItemAsync<NotificationDocument>(
                ReplicaDocumentSerialization.Key(reply.NotificationId), partition, cancellationToken: cancellationToken).ConfigureAwait(false)).Resource;
        }
        catch (CosmosException failure) when (failure.StatusCode is HttpStatusCode.NotFound)
        {
            return NotificationReplyStoreOutcome.NotificationNotFound;
        }
        catch (CosmosException failure)
        {
            throw Fault(failure);
        }

        if (!string.Equals(notification.OwnerInstanceId, ReplicaDocumentSerialization.Key(reply.OwnerInstanceId), StringComparison.Ordinal))
        {
            return NotificationReplyStoreOutcome.NotificationNotFound;
        }

        var replies = await _replies.ContainerAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await replies.CreateItemAsync(
                NotificationDocumentFactory.From(scope, reply, _options),
                partition,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return NotificationReplyStoreOutcome.Stored;
        }
        catch (CosmosException failure) when (failure.StatusCode is HttpStatusCode.Conflict)
        {
            // Written once and never edited: a second post of the same id is the
            // phone's outbox sending it again, not a change.
            return NotificationReplyStoreOutcome.AlreadyStored;
        }
        catch (CosmosException failure)
        {
            throw Fault(failure);
        }
    }

    public async Task<IReadOnlyList<NotificationReplyRecord>> ListReplies(OwnerScope scope, Guid ownerInstanceId, int maxItems, CancellationToken cancellationToken = default)
    {
        if (!await SpeaksForAsync(scope, ownerInstanceId, cancellationToken).ConfigureAwait(false)) return [];

        var container = await _replies.ContainerAsync(cancellationToken).ConfigureAwait(false);

        // One ORDER BY path, so the default index serves it; replies stamped in the same
        // second are put in order by the owner, which breaks the tie on repliedAt.
        var query = new QueryDefinition("SELECT TOP @max * FROM c WHERE c.ownerInstanceId = @instance ORDER BY c._ts")
            .WithParameter("@max", maxItems)
            .WithParameter("@instance", ReplicaDocumentSerialization.Key(ownerInstanceId));

        var found = new List<NotificationReplyRecord>();

        try
        {
            using var iterator = container.GetItemQueryIterator<NotificationReplyDocument>(
                query,
                requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(ReplicaDocumentSerialization.Key(scope.OwnerId.Value)) });

            while (iterator.HasMoreResults && found.Count < maxItems)
            {
                foreach (var document in await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (NotificationDocumentFactory.ToRecord(document) is { } record) found.Add(record);
                }
            }
        }
        catch (CosmosException failure)
        {
            throw Fault(failure);
        }

        return found;
    }

    public async Task<bool> DeleteReply(OwnerScope scope, Guid replyId, CancellationToken cancellationToken = default)
    {
        var container = await _replies.ContainerAsync(cancellationToken).ConfigureAwait(false);
        var partition = new PartitionKey(ReplicaDocumentSerialization.Key(scope.OwnerId.Value));
        var id = ReplicaDocumentSerialization.Key(replyId);

        try
        {
            var read = await container.ReadItemAsync<NotificationReplyDocument>(id, partition, cancellationToken: cancellationToken).ConfigureAwait(false);

            // Only the device that speaks for the instance the reply is addressed to removes it.
            if (!Guid.TryParse(read.Resource.OwnerInstanceId, out var instance)
                || !await SpeaksForAsync(scope, instance, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            await container.DeleteItemAsync<NotificationReplyDocument>(
                id, partition, new ItemRequestOptions { IfMatchEtag = read.ETag }, cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (CosmosException failure) when (failure.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.PreconditionFailed)
        {
            // Gone already, or replaced between the read and the delete — a reply is never
            // replaced, so the second is a delete that raced another.
            return false;
        }
        catch (CosmosException failure)
        {
            throw Fault(failure);
        }
    }

    /// <summary>Whether the caller's device last pushed a notification of the instance — the
    /// device that speaks for it. One count over the owner's own partition.</summary>
    private async Task<bool> SpeaksForAsync(OwnerScope scope, Guid ownerInstanceId, CancellationToken cancellationToken)
    {
        var container = await _notifications.ContainerAsync(cancellationToken).ConfigureAwait(false);
        var query = new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.ownerInstanceId = @instance AND c.deviceId = @device")
            .WithParameter("@instance", ReplicaDocumentSerialization.Key(ownerInstanceId))
            .WithParameter("@device", ReplicaDocumentSerialization.Key(scope.DeviceId.Value));

        try
        {
            using var iterator = container.GetItemQueryIterator<int>(
                query,
                requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(ReplicaDocumentSerialization.Key(scope.OwnerId.Value)) });

            var count = 0;
            while (iterator.HasMoreResults)
            {
                foreach (var page in await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false)) count += page;
            }

            return count > 0;
        }
        catch (CosmosException failure)
        {
            throw Fault(failure);
        }
    }

    internal static SyncReplicaException Fault(CosmosException failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return failure.StatusCode switch
        {
            HttpStatusCode.RequestEntityTooLarge => new SyncReplicaException(
                SyncErrorCodes.NotificationInvalid,
                "That notification is too large for the replica to store.",
                failure),
            HttpStatusCode.TooManyRequests => new SyncReplicaException(
                SyncErrorCodes.ReplicaBusy,
                "The notification replica is busy. Try again shortly.",
                failure),
            _ => ReplicaFailures.Unavailable(UnavailableMessage, failure),
        };
    }

    private sealed record ChangeFeedPage(
        [property: JsonPropertyName("Documents")] IReadOnlyList<NotificationDocument> Documents);
}
