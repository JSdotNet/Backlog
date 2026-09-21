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

namespace Backlog.Infrastructure.Cosmos.Annotations;

/// <summary>
/// The Devbook annotation replica, in Cosmos DB — the third replica container
/// beside <c>tasks</c> and <c>sessions</c>.
/// <para>
/// <see cref="Tasks.CosmosTaskReplica"/>'s change-feed arrangement, kept as a
/// copy rather than shared through a base type, the way the session replica
/// already is: one container partitioned on <c>/ownerId</c>, whole documents in
/// and whole documents out, the container resolved lazily so the service starts
/// before the emulator answers, and the stream iterator's 304 as the only
/// signal that a feed is drained. Nothing here reads an annotation's fields;
/// the desktop's own store is the system of record and this is a relay.
/// </para>
/// </summary>
internal sealed class CosmosAnnotationReplica : IAnnotationReplica
{
    private const string UnavailableMessage = "The annotation replica is not available yet. Try again shortly.";

    private readonly CosmosContainerHandle _container;
    private readonly CosmosOptions _options;
    private readonly ILogger<CosmosAnnotationReplica> _log;

    public CosmosAnnotationReplica(
        IServiceProvider services,
        IOptions<CosmosOptions> options,
        ILogger<CosmosAnnotationReplica> log)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _log = log;

        _container = new CosmosContainerHandle(
            services,
            _options.DatabaseName,
            _options.AnnotationsContainerName,
            UnavailableMessage);
    }

    public async Task<int> Upsert(
        OwnerScope scope,
        IReadOnlyList<AnnotationChange> changes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        if (changes.Count == 0)
        {
            return 0;
        }

        var container = Container();
        var partition = new PartitionKey(ReplicaDocumentSerialization.Key(scope.OwnerId.Value));
        var accepted = 0;

        // One document at a time rather than a transactional batch, for the
        // reason the task replica gives: a single unlucky document must not
        // send the rest of the batch back.
        foreach (var change in changes)
        {
            if (await WriteIfLaterAsync(container, partition, scope, change, cancellationToken))
            {
                accepted++;
            }
        }

        return accepted;
    }

    /// <summary>How often one document is re-read and re-tried after another
    /// writer got in between the read and the write. Two of one person's
    /// desktops pushing the same remark in the same instant is the only way to
    /// need a second attempt at all.</summary>
    private const int WriteAttempts = 3;

    /// <summary>
    /// Writes one change unless the document held is already at least that
    /// version, and says whether it did.
    /// <para>
    /// Read, compare, then write against the etag the read returned — never a
    /// blind upsert. A blind upsert let a device's echo of a version it had
    /// pulled overwrite the tombstone or the edit another device had pushed
    /// since; <see cref="AnnotationChangePrecedence"/> carries that story and
    /// the rule. The etag closes the gap between the read and the write: a
    /// version that landed in between fails the precondition, and the document
    /// is read again and compared again rather than either copy winning by
    /// timing. A document that is not there yet is created rather than
    /// upserted for the same reason — a create that finds one is the same
    /// race, and is retried the same way.
    /// </para>
    /// <para>
    /// A document this service cannot read back — no payload, an id that is not
    /// a GUID — is written over: it is not one this service wrote, and holding
    /// a push back for it would leave that id stuck for ever.
    /// </para>
    /// </summary>
    private async Task<bool> WriteIfLaterAsync(
        Container container,
        PartitionKey partition,
        OwnerScope scope,
        AnnotationChange change,
        CancellationToken cancellationToken)
    {
        var id = ReplicaDocumentSerialization.Key(change.Id);

        for (var attempt = 1; attempt <= WriteAttempts; attempt++)
        {
            AnnotationDocument? held = null;
            string? etag = null;

            try
            {
                var read = await container.ReadItemAsync<AnnotationDocument>(id, partition, cancellationToken: cancellationToken);
                held = read.Resource;
                etag = read.ETag;
            }
            catch (CosmosException failure) when (failure.StatusCode is HttpStatusCode.NotFound)
            {
                // Nothing held: the ordinary case for a new remark.
            }
            catch (CosmosException failure)
            {
                throw Fault(failure);
            }

            if (held is not null
                && AnnotationDocumentFactory.ToRecord(held) is { } stored
                && !AnnotationChangePrecedence.Supersedes(change, stored.Change))
            {
                return false;
            }

            var document = AnnotationDocumentFactory.From(scope, change, _options);

            try
            {
                if (held is null)
                {
                    await container.CreateItemAsync(document, partition, cancellationToken: cancellationToken);
                }
                else
                {
                    await container.ReplaceItemAsync(
                        document,
                        id,
                        partition,
                        new ItemRequestOptions { IfMatchEtag = etag },
                        cancellationToken);
                }

                return true;
            }
            catch (CosmosException failure) when (failure.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
            {
                // Another writer landed between the read and the write. Go
                // round again: the next read sees their version and the
                // comparison decides, which is the whole point of not having
                // let timing decide here.
                _log.LogDebug(
                    "Annotation {AnnotationId} changed under a push from device {DeviceId}; re-reading (attempt {Attempt} of {Attempts}).",
                    change.Id, scope.DeviceId.Value, attempt, WriteAttempts);
            }
            catch (CosmosException failure)
            {
                throw Fault(failure);
            }
        }

        // Contended on every attempt. Busy rather than a silent drop, so the
        // device's watermark stays put and it sends the batch again next run.
        throw new SyncReplicaException(
            SyncErrorCodes.ReplicaBusy,
            "The annotation replica is busy. Try again shortly.");
    }

    public async Task<AnnotationReplicaPage> ReadChanges(
        OwnerId owner,
        AnnotationReplicaCursor? cursor,
        int maxItems,
        CancellationToken cancellationToken = default)
    {
        // The caller verifies a cursor against the token's owner before this
        // point; a mismatch here is a bug, and it throws rather than answering
        // an empty page that would look exactly like a drained feed.
        if (cursor is { } resume && resume.Owner != owner)
        {
            throw new InvalidOperationException(
                "The cursor belongs to another owner. Verify it against the caller before it gets here.");
        }

        var key = ReplicaDocumentSerialization.Key(owner.Value);

        var startFrom = cursor is { } from
            ? ChangeFeedStartFrom.ContinuationToken(from.Continuation)
            : ChangeFeedStartFrom.Beginning(FeedRange.FromPartitionKey(new PartitionKey(key)));

        // The stream iterator, not GetChangeFeedIterator<T>, because only the
        // ResponseMessage carries the 304 that says "nothing since your cursor".
        using var iterator = Container().GetChangeFeedStreamIterator(
            startFrom,
            ChangeFeedMode.Incremental,
            new ChangeFeedRequestOptions { PageSizeHint = maxItems });

        ResponseMessage response;
        try
        {
            response = await iterator.ReadNextAsync(cancellationToken);
        }
        catch (CosmosException failure)
        {
            throw failure.StatusCode is HttpStatusCode.BadRequest ? Expired(failure) : Unavailable(failure);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.NotModified)
            {
                return Page([], owner, response.ContinuationToken, drained: true);
            }

            if (response.StatusCode is HttpStatusCode.BadRequest)
            {
                throw Expired(null);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new SyncReplicaException(
                    SyncErrorCodes.ReplicaUnavailable,
                    UnavailableMessage);
            }

            var documents = Read(response.Content);

            // Belt against the SDK changing what a continuation token means: a
            // page carrying another partition's documents is discarded whole,
            // continuation included, rather than half of somebody else's data
            // being handed out.
            if (documents.Any(document => !string.Equals(document.OwnerId, key, StringComparison.Ordinal)))
            {
                _log.LogError(
                    "An annotation change feed page for owner {OwnerId} contained documents from another partition. Discarding it.",
                    key);

                return Discarded(owner);
            }

            return Page(documents, owner, response.ContinuationToken, drained: false);
        }
    }

    private static IReadOnlyList<AnnotationDocument> Read(Stream content)
    {
        var page = JsonSerializer.Deserialize<ChangeFeedPage>(content, ReplicaDocumentSerialization.Options);

        return page?.Documents ?? [];
    }

    /// <summary>Builds the page, including the cursor a drained feed still gets.
    /// <paramref name="drained"/> is the sole authority for <c>HasMore</c>; the
    /// page size deliberately says nothing, for the reason
    /// <see cref="Tasks.CosmosTaskReplica.Page"/> gives.</summary>
    internal static AnnotationReplicaPage Page(
        IReadOnlyList<AnnotationDocument> documents,
        OwnerId owner,
        string? continuation,
        bool drained)
    {
        List<AnnotationChangeRecord> changes =
        [
            .. documents.Select(AnnotationDocumentFactory.ToRecord).OfType<AnnotationChangeRecord>(),
        ];

        return new AnnotationReplicaPage(
            changes,
            new AnnotationReplicaCursor(owner, continuation ?? string.Empty),
            !drained);
    }

    /// <summary>The page handed back when a feed range answered with somebody
    /// else's documents: nothing, and no continuation, so the next pull starts
    /// from the beginning rather than being pinned to a range it can never
    /// read.</summary>
    internal static AnnotationReplicaPage Discarded(OwnerId owner) =>
        Page([], owner, continuation: null, drained: true);

    /// <summary>What a Cosmos failure becomes on the way out: a coded problem a
    /// client can branch on, never an unclassified 500.</summary>
    internal static SyncReplicaException Fault(CosmosException failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return failure.StatusCode switch
        {
            HttpStatusCode.RequestEntityTooLarge => new SyncReplicaException(
                SyncErrorCodes.AnnotationTooLarge,
                "That annotation is too large for the replica to store. Shorten it and try again.",
                failure),

            HttpStatusCode.TooManyRequests => new SyncReplicaException(
                SyncErrorCodes.ReplicaBusy,
                "The annotation replica is busy. Try again shortly.",
                failure),

            _ => Unavailable(failure),
        };
    }

    private static SyncReplicaException Unavailable(CosmosException failure) => new(
        SyncErrorCodes.ReplicaUnavailable,
        UnavailableMessage,
        failure);

    private static SyncReplicaException Expired(CosmosException? failure) => new(
        SyncErrorCodes.SyncCursorExpired,
        "That cursor is too old to resume from. Pull again without one.",
        failure);

    /// <summary>The container, resolved once. Internal so the activity the
    /// client is constructed under can be asserted without reaching Cosmos.</summary>
    internal Container Container() => _container.Container();

    private sealed record ChangeFeedPage(
        [property: JsonPropertyName("Documents")] IReadOnlyList<AnnotationDocument> Documents);
}
