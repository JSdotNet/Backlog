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

        // One request per document rather than a transactional batch, for the
        // reason the task replica gives: a single unlucky document must not
        // send the rest of the batch back.
        foreach (var change in changes)
        {
            try
            {
                await container.UpsertItemAsync(
                    AnnotationDocumentFactory.From(scope, change, _options),
                    partition,
                    cancellationToken: cancellationToken);

                accepted++;
            }
            catch (CosmosException failure)
            {
                throw Fault(failure);
            }
        }

        return accepted;
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
