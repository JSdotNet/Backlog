using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backlog.Infrastructure.Cosmos.Tasks;

/// <summary>
/// The task replica, in Cosmos DB (.arc42/adr/0005).
/// <para>
/// One container, partitioned on <c>/ownerId</c>, whole documents in and whole
/// documents out. Nothing here reads a task's fields except the inbox
/// projection, and there is no query a screen could be answered from: the
/// desktop's SQLite database is the system of record and this is a relay
/// between one person's devices.
/// </para>
/// <para>
/// The container is resolved lazily. The AppHost deliberately does not wait on
/// the emulator — it is a 2.5 GB image with a cold start measured in minutes —
/// so the sync service starts long before Cosmos answers, and a client that asks
/// too early gets 503 <c>sync.replica_unavailable</c> rather than a hang or a
/// 500. Nothing is created here either: the database and both containers come
/// from the AppHost locally and from <c>infra/sync/main.bicep</c> deployed, and a
/// service that could create its own containers would create them without the
/// TTL and indexing policy that file sets.
/// </para>
/// </summary>
internal sealed class CosmosTaskReplica : ITaskReplica
{
    private readonly Lazy<Container> _container;
    private readonly CosmosOptions _options;
    private readonly ILogger<CosmosTaskReplica> _log;

    public CosmosTaskReplica(
        IServiceProvider services,
        IOptions<CosmosOptions> options,
        ILogger<CosmosTaskReplica> log)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _log = log;

        // Lazy rather than resolved in the constructor: this adapter is a
        // singleton built while the emulator may still be starting, and a
        // failure to reach Cosmos must surface as a 503 on the call that needed
        // it rather than as a service that would not start.
        //
        // PublicationOnly, and that mode is doing real work here. The default
        // ExecutionAndPublication caches the exception as well as the value:
        // once the factory throws, every later .Value rethrows that same
        // instance and the factory never runs again, so a service that started a
        // second before its store would answer 503 for the life of the process.
        // That is worse than failing to start, because Container Apps restarts a
        // container that died and never one that is answering. PublicationOnly
        // runs the factory again for the next caller; the cost is that two
        // callers racing may both build one, and the loser's is discarded.
        _container = new Lazy<Container>(() =>
        {
            // Lazy has a consequence, and this is it. The client singleton is
            // built here, inside whichever request needed Cosmos first, and the
            // SDK starts a background endpoint refresh when it is constructed.
            // A timer captures the ExecutionContext it was started on and
            // Activity.Current travels in that context, so the refresh five
            // minutes later reports itself as a child of that one push: the
            // sync.push_tasks trace then measures 300 seconds and every latency
            // percentile read off it is a fiction.
            //
            // Clearing Activity.Current for the length of the construction is
            // what unparents it. Not ExecutionContext.SuppressFlow, which stops
            // the whole context flowing and would take the cancellation and
            // logging scopes with it, and not eager construction, which would
            // trade this for a service that cannot start before its emulator.
            var ambient = Activity.Current;
            Activity.Current = null;

            try
            {
                return services
                    .GetRequiredService<CosmosClient>()
                    .GetContainer(_options.DatabaseName, _options.TasksContainerName);
            }
            finally
            {
                // The caller is mid-request and the rest of it belongs on the
                // trace it arrived on.
                Activity.Current = ambient;
            }
        }, LazyThreadSafetyMode.PublicationOnly);
    }

    public async Task<int> Upsert(
        OwnerScope scope,
        IReadOnlyList<TaskChange> changes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        if (changes.Count == 0)
        {
            return 0;
        }

        var container = Container();
        var partition = new PartitionKey(TaskDocumentSerialization.Key(scope.OwnerId.Value));
        var accepted = 0;

        // One request per document rather than a transactional batch. The batch
        // is capped at a hundred operations and is all-or-nothing, and a push
        // from a device that has been offline for a week is neither: a single
        // unlucky document must not send the other four hundred back.
        foreach (var change in changes)
        {
            try
            {
                await container.UpsertItemAsync(
                    TaskDocumentFactory.From(scope, change, _options),
                    partition,
                    cancellationToken: cancellationToken);

                accepted++;
            }
            catch (CosmosException failure)
            {
                // Every CosmosException, not only the ones that mean "not there
                // yet". A 413 or a 429 that outlived the SDK's retries would
                // otherwise leave this method as an unclassified 500 with no
                // code on it (inherited ADR 0017) — and the batch carrying that
                // document would fail on every run for ever, so the push
                // watermark would never advance past it again.
                throw Fault(failure);
            }
        }

        return accepted;
    }

    public async Task<TaskReplicaPage> ReadChanges(
        OwnerId owner,
        TaskReplicaCursor? cursor,
        int maxItems,
        CancellationToken cancellationToken = default)
    {
        // The caller is what verifies a cursor against the token's owner, and it
        // has to have done so before this point. A mismatch here is a bug rather
        // than a request somebody made, so it throws: returning an empty page
        // would look exactly like a drained feed and hide it.
        if (cursor is { } resume && resume.Owner != owner)
        {
            throw new InvalidOperationException(
                "The cursor belongs to another owner. Verify it against the caller before it gets here.");
        }

        var key = TaskDocumentSerialization.Key(owner.Value);

        // Beginning rather than Now when there is no cursor: a device that has
        // just paired has to receive the tasks the owner already has, and the
        // model has no separate bootstrap path.
        var startFrom = cursor is { } from
            ? ChangeFeedStartFrom.ContinuationToken(from.Continuation)
            : ChangeFeedStartFrom.Beginning(FeedRange.FromPartitionKey(new PartitionKey(key)));

        // The stream iterator, not GetChangeFeedIterator<T>. A change feed's
        // HasMoreResults is permanently true, so the typed iterator reports a
        // drained feed only as an empty page and cannot say whether it is caught
        // up or merely between batches. The stream form hands back the
        // ResponseMessage, whose 304 is the documented "nothing since your
        // cursor" signal.
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
            // The 304 is not propagated. It carries no body and therefore no
            // cursor, and the feed's position advances whether or not anything
            // was there — so a client handed a 304 would keep its old cursor and
            // rescan from it on every poll for as long as the replica stays
            // quiet. The endpoint answers 200 with an empty list, the new
            // cursor, and hasMore false.
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
                    "The task replica is not available yet. Try again shortly.");
            }

            var documents = Read(response.Content);

            // Belt against the SDK changing what a continuation token means. A
            // resumed feed is constrained by the range embedded in the token and
            // not by anything this call passes, so if that ever stops being one
            // owner's partition, the whole page is discarded rather than half of
            // somebody else's data being handed out — and the continuation goes
            // with it, because it names the range those documents came from.
            if (documents.Any(document => !string.Equals(document.OwnerId, key, StringComparison.Ordinal)))
            {
                _log.LogError(
                    "A change feed page for owner {OwnerId} contained documents from another partition. Discarding it.",
                    key);

                return Discarded(owner);
            }

            return Page(documents, owner, response.ContinuationToken, drained: false);
        }
    }

    /// <summary>
    /// The owner's live captures, newest first. The one query in this adapter,
    /// and the exception <see cref="ITaskReplica"/> documents: the inbox has no
    /// local counterpart to disagree with, so answering it from here does not
    /// give the replica a second opinion about a screen.
    /// </summary>
    public async Task<IReadOnlyList<TaskChangeRecord>> ListCaptures(
        OwnerId owner,
        CancellationToken cancellationToken = default)
    {
        var key = TaskDocumentSerialization.Key(owner.Value);

        // Nulls are not written, so "not tombstoned" and "still a capture" are
        // both questions about whether the property is there at all.
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.ownerId = @owner AND NOT IS_DEFINED(c.deletedAt) "
            + "AND IS_DEFINED(c.task.sourceInboxId) ORDER BY c.task.createdAt DESC")
            .WithParameter("@owner", key);

        // The partition key on the request, as well as the predicate on the
        // owner: the first keeps the query inside one partition, the second
        // keeps it right if the first is ever forgotten.
        using var results = Container().GetItemQueryIterator<TaskDocument>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(key) });

        var captures = new List<TaskChangeRecord>();

        try
        {
            while (results.HasMoreResults)
            {
                foreach (var document in await results.ReadNextAsync(cancellationToken))
                {
                    if (string.Equals(document.OwnerId, key, StringComparison.Ordinal)
                        && TaskDocumentFactory.ToRecord(document) is { } record)
                    {
                        captures.Add(record);
                    }
                }
            }
        }
        catch (CosmosException failure)
        {
            throw Unavailable(failure);
        }

        return captures;
    }

    public async Task<TaskChangeRecord?> Find(
        OwnerId owner,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var key = TaskDocumentSerialization.Key(owner.Value);

        try
        {
            var response = await Container().ReadItemAsync<TaskDocument>(
                TaskDocumentSerialization.Key(id),
                new PartitionKey(key),
                cancellationToken: cancellationToken);

            // A read is already confined to the partition it named, so this can
            // only fail if the document says something other than what its own
            // partition key does. Checked anyway, because the alternative is
            // handing back a document the caller will then write.
            return string.Equals(response.Resource.OwnerId, key, StringComparison.Ordinal)
                ? TaskDocumentFactory.ToRecord(response.Resource)
                : null;
        }
        catch (CosmosException failure) when (failure.StatusCode is HttpStatusCode.NotFound)
        {
            // An id belonging to somebody else lands here too: the read named
            // this owner's partition, so it was never reachable.
            return null;
        }
        catch (CosmosException failure)
        {
            throw Unavailable(failure);
        }
    }

    private static IReadOnlyList<TaskDocument> Read(Stream content)
    {
        var page = JsonSerializer.Deserialize<ChangeFeedPage>(content, TaskDocumentSerialization.Options);

        return page?.Documents ?? [];
    }

    /// <summary>
    /// Builds the page, including the cursor a drained feed still gets.
    /// <para>
    /// <paramref name="drained"/> is the sole authority for <c>HasMore</c>, and
    /// the page size deliberately says nothing. <c>PageSizeHint</c> is a hint:
    /// Cosmos returns fewer items as a response nears its size limit, so a short
    /// page and a caught-up feed cannot be told apart by counting — and a device
    /// bootstrapping a large backlog would stop at the first short one and report
    /// a finished sync while it was nowhere near caught up. The 304 in
    /// <see cref="ReadChanges"/> is the store's own "nothing since your cursor",
    /// and it is the only thing that ends the client's loop.
    /// </para>
    /// </summary>
    internal static TaskReplicaPage Page(
        IReadOnlyList<TaskDocument> documents,
        OwnerId owner,
        string? continuation,
        bool drained)
    {
        List<TaskChangeRecord> changes =
        [
            .. documents.Select(TaskDocumentFactory.ToRecord).OfType<TaskChangeRecord>(),
        ];

        return new TaskReplicaPage(
            changes,
            new TaskReplicaCursor(owner, continuation ?? string.Empty),
            !drained);
    }

    /// <summary>
    /// The page handed back when a feed range answered with somebody else's
    /// documents: nothing, and <b>no continuation</b>.
    /// <para>
    /// The token belongs to the range those documents came from, so signing it
    /// for this caller would pin them to a range they can never read — silently,
    /// and for as long as they keep sending it back. An empty continuation means
    /// the next pull starts from the beginning, which is the safe direction to
    /// fail in.
    /// </para>
    /// </summary>
    internal static TaskReplicaPage Discarded(OwnerId owner) =>
        Page([], owner, continuation: null, drained: true);

    /// <summary>
    /// What a Cosmos failure becomes on the way out: a coded problem a client can
    /// branch on, never an unclassified 500.
    /// <para>
    /// Three answers, because they ask three different things of the caller. A
    /// document the store will not take is the caller's to shrink and no retry
    /// helps; throttling that outlived the SDK's own retries is worth coming back
    /// for; everything else is the store not being reachable, which is the
    /// ordinary state of the first minutes of a local run.
    /// </para>
    /// </summary>
    internal static SyncReplicaException Fault(CosmosException failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return failure.StatusCode switch
        {
            HttpStatusCode.RequestEntityTooLarge => new SyncReplicaException(
                SyncErrorCodes.TaskTooLarge,
                "That task is too large for the replica to store. Trim its content and try again.",
                failure),

            HttpStatusCode.TooManyRequests => new SyncReplicaException(
                SyncErrorCodes.ReplicaBusy,
                "The task replica is busy. Try again shortly.",
                failure),

            _ => Unavailable(failure),
        };
    }

    private static SyncReplicaException Unavailable(CosmosException failure) => new(
        SyncErrorCodes.ReplicaUnavailable,
        "The task replica is not available yet. Try again shortly.",
        failure);

    /// <summary>A continuation Cosmos will not resume from. It is the client's to
    /// recover from — drop the cursor and pull from the beginning — so it is a
    /// 400 with its own code rather than an error the service can retry.</summary>
    private static SyncReplicaException Expired(CosmosException? failure) => new(
        SyncErrorCodes.SyncCursorExpired,
        "That cursor is too old to resume from. Pull again without one.",
        failure);

    /// <summary>The container, resolved once. Internal rather than private so
    /// that the activity the client is constructed under can be asserted without
    /// a call that would then try to reach Cosmos.</summary>
    internal Container Container()
    {
        try
        {
            return _container.Value;
        }
        catch (Exception failure) when (failure is not SyncReplicaException)
        {
            throw new SyncReplicaException(
                SyncErrorCodes.ReplicaUnavailable,
                "The task replica is not available yet. Try again shortly.",
                failure);
        }
    }

    /// <summary>The envelope a change feed page arrives in. Only the documents
    /// are read; the <c>_rid</c> and <c>_count</c> beside them say nothing this
    /// adapter cannot see for itself.</summary>
    private sealed record ChangeFeedPage(
        [property: JsonPropertyName("Documents")] IReadOnlyList<TaskDocument> Documents);
}
