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

namespace Backlog.Infrastructure.Cosmos.DevbookSubjects;

/// <summary>
/// The devbook subject state replica, in Cosmos DB — its own container beside
/// <c>annotations</c>.
/// <para>
/// <see cref="Annotations.CosmosAnnotationReplica"/>'s arrangement, kept as a
/// copy rather than shared through a base type, as each replica before it is:
/// one container partitioned on <c>/ownerId</c>, whole documents in and out,
/// the container resolved lazily so the service starts before the emulator
/// answers, the stream iterator's 304 as the only signal that a feed is
/// drained, and a read-compare-write against the read's etag so an echo never
/// replaces a later state. Nothing here reads a state's fields.
/// </para>
/// </summary>
internal sealed class CosmosDevbookSubjectReplica : IDevbookSubjectReplica
{
    private const string UnavailableMessage = "The devbook subject replica is not available yet. Try again shortly.";

    /// <summary>How often one document is re-read and re-tried after another
    /// writer got in between the read and the write.</summary>
    private const int WriteAttempts = 3;

    private readonly CosmosContainerHandle _container;
    private readonly ILogger<CosmosDevbookSubjectReplica> _log;

    public CosmosDevbookSubjectReplica(
        IServiceProvider services,
        IOptions<CosmosOptions> options,
        ILogger<CosmosDevbookSubjectReplica> log)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        _log = log;

        _container = new CosmosContainerHandle(
            services,
            options.Value.DatabaseName,
            options.Value.DevbookSubjectsContainerName,
            UnavailableMessage);
    }

    public async Task<int> Upsert(
        OwnerScope scope,
        IReadOnlyList<DevbookSubjectChange> changes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        if (changes.Count == 0)
        {
            return 0;
        }

        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);
        var partition = new PartitionKey(ReplicaDocumentSerialization.Key(scope.OwnerId.Value));

        // One request per document, never two writes to one, for the reasons the
        // task replica gives.
        return await ReplicaWrites.WriteAllAsync(
            changes,
            change => ReplicaDocumentSerialization.Key(change.Id),
            (change, token) => WriteIfLaterAsync(container, partition, scope, change, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes one change unless the document held is already at least that
    /// version, and says whether it did — read, compare, then write against the
    /// read's etag, never a blind upsert, for the reason
    /// <c>CosmosAnnotationReplica.WriteIfLaterAsync</c> records. A version that
    /// landed in between fails the precondition, and the document is read and
    /// compared again. A document this service cannot read back is written
    /// over.
    /// </summary>
    private async Task<bool> WriteIfLaterAsync(
        Container container,
        PartitionKey partition,
        OwnerScope scope,
        DevbookSubjectChange change,
        CancellationToken cancellationToken)
    {
        var id = ReplicaDocumentSerialization.Key(change.Id);

        for (var attempt = 1; attempt <= WriteAttempts; attempt++)
        {
            DevbookSubjectDocument? held = null;
            string? etag = null;

            try
            {
                var read = await container.ReadItemAsync<DevbookSubjectDocument>(id, partition, cancellationToken: cancellationToken).ConfigureAwait(false);
                held = read.Resource;
                etag = read.ETag;
            }
            catch (CosmosException failure) when (failure.StatusCode is HttpStatusCode.NotFound)
            {
                // Nothing held: the first state this subject has had.
            }
            catch (CosmosException failure)
            {
                throw Fault(failure);
            }

            if (held is not null
                && DevbookSubjectDocumentFactory.ToRecord(held) is { } stored
                && !DevbookSubjectChangePrecedence.Supersedes(change, stored.Change))
            {
                return false;
            }

            var document = DevbookSubjectDocumentFactory.From(scope, change);

            try
            {
                if (held is null)
                {
                    await container.CreateItemAsync(document, partition, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await container.ReplaceItemAsync(
                        document,
                        id,
                        partition,
                        new ItemRequestOptions { IfMatchEtag = etag },
                        cancellationToken).ConfigureAwait(false);
                }

                return true;
            }
            catch (CosmosException failure) when (failure.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
            {
                _log.LogDebug(
                    "Devbook subject {SubjectId} changed under a push from device {DeviceId}; re-reading (attempt {Attempt} of {Attempts}).",
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
            "The devbook subject replica is busy. Try again shortly.");
    }

    public async Task<DevbookSubjectReplicaPage> ReadChanges(
        OwnerId owner,
        DevbookSubjectReplicaCursor? cursor,
        int maxItems,
        CancellationToken cancellationToken = default)
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

        // The stream iterator, because only the ResponseMessage carries the 304
        // that says "nothing since your cursor".
        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);
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
            if (response.StatusCode is HttpStatusCode.NotModified)
            {
                return Page([], owner, response.ContinuationToken, drained: true);
            }

            if (response.StatusCode is HttpStatusCode.BadRequest)
            {
                throw ReplicaFailures.Expired(null);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new SyncReplicaException(SyncErrorCodes.ReplicaUnavailable, UnavailableMessage);
            }

            var documents = Read(response.Content);

            // A page carrying another partition's documents is discarded whole,
            // continuation included, rather than half of somebody else's data
            // being handed out.
            if (documents.Any(document => !string.Equals(document.OwnerId, key, StringComparison.Ordinal)))
            {
                _log.LogError(
                    "A devbook subject change feed page for owner {OwnerId} contained documents from another partition. Discarding it.",
                    key);

                return Page([], owner, continuation: null, drained: true);
            }

            return Page(documents, owner, response.ContinuationToken, drained: false);
        }
    }

    private static IReadOnlyList<DevbookSubjectDocument> Read(Stream content)
    {
        var page = JsonSerializer.Deserialize<ChangeFeedPage>(content, ReplicaDocumentSerialization.Options);

        return page?.Documents ?? [];
    }

    /// <summary>Builds the page, including the cursor a drained feed still gets;
    /// <paramref name="drained"/> is the sole authority for <c>HasMore</c>.</summary>
    private static DevbookSubjectReplicaPage Page(
        IReadOnlyList<DevbookSubjectDocument> documents,
        OwnerId owner,
        string? continuation,
        bool drained)
    {
        List<DevbookSubjectChangeRecord> changes =
        [
            .. documents.Select(DevbookSubjectDocumentFactory.ToRecord).OfType<DevbookSubjectChangeRecord>(),
        ];

        return new DevbookSubjectReplicaPage(
            changes,
            new DevbookSubjectReplicaCursor(owner, continuation ?? string.Empty),
            !drained);
    }

    /// <summary>What a Cosmos failure becomes on the way out: a coded problem a
    /// client can branch on, never an unclassified 500.</summary>
    private static SyncReplicaException Fault(CosmosException failure) => failure.StatusCode switch
    {
        HttpStatusCode.RequestEntityTooLarge => new SyncReplicaException(
            SyncErrorCodes.DevbookSubjectTooLarge,
            "That devbook subject state is too large for the replica to store.",
            failure),

        HttpStatusCode.TooManyRequests => new SyncReplicaException(
            SyncErrorCodes.ReplicaBusy,
            "The devbook subject replica is busy. Try again shortly.",
            failure),

        _ => ReplicaFailures.Unavailable(UnavailableMessage, failure),
    };

    private ValueTask<Container> ContainerAsync(CancellationToken cancellationToken) =>
        _container.ContainerAsync(cancellationToken);

    private sealed record ChangeFeedPage(
        [property: JsonPropertyName("Documents")] IReadOnlyList<DevbookSubjectDocument> Documents);
}
