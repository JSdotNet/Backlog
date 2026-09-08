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

namespace Backlog.Infrastructure.Cosmos.Sessions;

/// <summary>
/// The session replica, in Cosmos DB (.arc42/adr/0005 §Session records).
/// <para>
/// One container, partitioned on <c>/ownerId</c>, whole records in and whole
/// records out. There is no query at all here — not even the one exception
/// <see cref="Tasks.CosmosTaskReplica"/> makes for the inbox — because nothing
/// asks the replica a question about session records: the reading device answers
/// its fleet view from its own session log, and the replica only carries the
/// evidence there.
/// </para>
/// <para>
/// The container is resolved lazily, for the reasons the task replica beside it
/// spells out at length: the AppHost deliberately does not wait on the emulator,
/// so the sync service starts long before Cosmos answers and a client that asks
/// too early gets 503 <c>sync.replica_unavailable</c> rather than a hang or a
/// 500. Nothing is created here either — the database and both containers come
/// from the AppHost locally and from <c>infra/sync/main.bicep</c> deployed, and
/// this container in particular must not be created by a service that does not
/// know its indexing policy or its twelve-month <c>defaultTtl</c>.
/// </para>
/// </summary>
internal sealed class CosmosSessionReplica : ISessionReplica
{
    private readonly Lazy<Container> _container;
    private readonly CosmosOptions _options;
    private readonly ILogger<CosmosSessionReplica> _log;

    public CosmosSessionReplica(
        IServiceProvider services,
        IOptions<CosmosOptions> options,
        ILogger<CosmosSessionReplica> log)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _log = log;

        // Lazy, and PublicationOnly rather than the default
        // ExecutionAndPublication, for the reason CosmosTaskReplica records: the
        // default caches the exception as well as the value, so a service that
        // started a second before its store would answer 503 for the life of the
        // process — worse than failing to start, because Container Apps restarts
        // a container that died and never one that is answering.
        _container = new Lazy<Container>(() =>
        {
            // Activity.Current is cleared for the length of the construction for
            // the same reason it is there: the SDK starts a background endpoint
            // refresh when a client is built, a timer captures the
            // ExecutionContext it was started on, and the refresh five minutes
            // later would otherwise report itself as a child of whichever push
            // happened to build the client — making that one span measure 300
            // seconds. Only the first caller of either replica pays this; the
            // client itself is a singleton both of them resolve.
            var ambient = Activity.Current;
            Activity.Current = null;

            try
            {
                return services
                    .GetRequiredService<CosmosClient>()
                    .GetContainer(_options.DatabaseName, _options.SessionsContainerName);
            }
            finally
            {
                // The caller is mid-request and the rest of it belongs on the
                // trace it arrived on.
                Activity.Current = ambient;
            }
        }, LazyThreadSafetyMode.PublicationOnly);
    }

    public async Task<int> Append(
        OwnerScope scope,
        IReadOnlyList<SessionRecord> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        if (records.Count == 0)
        {
            return 0;
        }

        var container = Container();
        var partition = new PartitionKey(ReplicaDocumentSerialization.Key(scope.OwnerId.Value));
        var accepted = 0;

        // One request per record rather than a transactional batch. The batch is
        // capped at a hundred operations and is all-or-nothing, and a push from a
        // machine that has been offline all day is neither: a single unlucky
        // record must not send the rest back.
        //
        // An upsert rather than a create, and that is what append-only means for
        // a single-writer feed: a still-running session reports later evidence
        // under the same key, replacing this machine's own record for it and
        // moving it to the end of the feed. The key begins with this machine's
        // device id, so the write cannot reach another machine's record for the
        // same session even if two machines somehow issued the same session id.
        foreach (var record in records)
        {
            try
            {
                await container.UpsertItemAsync(
                    SessionDocumentFactory.From(scope, record),
                    partition,
                    cancellationToken: cancellationToken);

                accepted++;
            }
            catch (CosmosException failure)
            {
                // Every CosmosException, not only the ones that mean "not there
                // yet". Anything left unclassified would be a 500 with no code on
                // it (inherited ADR 0017), and the batch carrying that record
                // would fail on every run for ever.
                throw Fault(failure);
            }
        }

        return accepted;
    }

    public async Task<SessionReplicaPage> ReadChanges(
        OwnerId owner,
        SessionReplicaCursor? cursor,
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

        var key = ReplicaDocumentSerialization.Key(owner.Value);

        // Beginning rather than Now when there is no cursor: a device that has
        // just paired has to receive the session history the owner already has,
        // and the model has no separate bootstrap path.
        var startFrom = cursor is { } from
            ? ChangeFeedStartFrom.ContinuationToken(from.Continuation)
            : ChangeFeedStartFrom.Beginning(FeedRange.FromPartitionKey(new PartitionKey(key)));

        // The stream iterator, not GetChangeFeedIterator<T>. A change feed's
        // HasMoreResults is permanently true, so the typed iterator reports a
        // drained feed only as an empty page and cannot say whether it is caught
        // up or merely between batches. The stream form hands back the
        // ResponseMessage, whose 304 is the documented "nothing since your
        // cursor" signal — and on this feed that is the ordinary answer, because
        // a fleet nobody is working on writes nothing for hours.
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
            // quiet. The endpoint answers 200 with an empty list, the new cursor,
            // and hasMore false.
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
                    "The session replica is not available yet. Try again shortly.");
            }

            var documents = Read(response.Content);

            // Belt against the SDK changing what a continuation token means. A
            // resumed feed is constrained by the range embedded in the token and
            // not by anything this call passes, so if that ever stops being one
            // owner's partition, the whole page is discarded rather than half of
            // somebody else's session history being handed out — and the
            // continuation goes with it, because it names the range those
            // documents came from.
            if (documents.Any(document => !string.Equals(document.OwnerId, key, StringComparison.Ordinal)))
            {
                _log.LogError(
                    "A session change feed page for owner {OwnerId} contained documents from another partition. Discarding it.",
                    key);

                return Discarded(owner);
            }

            return Page(documents, owner, response.ContinuationToken, drained: false);
        }
    }

    private static IReadOnlyList<SessionDocument> Read(Stream content)
    {
        var page = JsonSerializer.Deserialize<ChangeFeedPage>(content, ReplicaDocumentSerialization.Options);

        return page?.Documents ?? [];
    }

    /// <summary>
    /// Builds the page, including the cursor a drained feed still gets.
    /// <para>
    /// <paramref name="drained"/> is the sole authority for <c>HasMore</c>, and
    /// the page size deliberately says nothing. <c>PageSizeHint</c> is a hint:
    /// Cosmos returns fewer items as a response nears its size limit, so a short
    /// page and a caught-up feed cannot be told apart by counting — and a device
    /// catching up on a fleet's session history would stop at the first short one
    /// and report a finished sync while it was nowhere near caught up.
    /// </para>
    /// </summary>
    internal static SessionReplicaPage Page(
        IReadOnlyList<SessionDocument> documents,
        OwnerId owner,
        string? continuation,
        bool drained)
    {
        List<SessionRecordEntry> entries =
        [
            .. documents.Select(SessionDocumentFactory.ToEntry).OfType<SessionRecordEntry>(),
        ];

        return new SessionReplicaPage(
            entries,
            new SessionReplicaCursor(owner, continuation ?? string.Empty),
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
    internal static SessionReplicaPage Discarded(OwnerId owner) =>
        Page([], owner, continuation: null, drained: true);

    /// <summary>
    /// What a Cosmos failure becomes on the way out: a coded problem a client can
    /// branch on, never an unclassified 500.
    /// <para>
    /// Three answers, because they ask three different things of the caller. A
    /// record the store will not take is the caller's to fix and no retry helps;
    /// throttling that outlived the SDK's own retries is worth coming back for;
    /// everything else is the store not being reachable, which is the ordinary
    /// state of the first minutes of a local run.
    /// </para>
    /// <para>
    /// The first of those should be unreachable — every field of a record is
    /// bounded in <c>SyncRequestLimits</c> well below Cosmos's two-megabyte
    /// ceiling, and the batch cap bounds the count. It is answered precisely
    /// anyway rather than folded into the third, because folding it would tell a
    /// caller to come back and retry a record that will be refused identically
    /// every time until somebody notices.
    /// </para>
    /// </summary>
    internal static SyncReplicaException Fault(CosmosException failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return failure.StatusCode switch
        {
            HttpStatusCode.RequestEntityTooLarge => new SyncReplicaException(
                SyncErrorCodes.SessionTooLarge,
                "That session record is too large for the replica to store.",
                failure),

            HttpStatusCode.TooManyRequests => new SyncReplicaException(
                SyncErrorCodes.ReplicaBusy,
                "The session replica is busy. Try again shortly.",
                failure),

            _ => Unavailable(failure),
        };
    }

    private static SyncReplicaException Unavailable(CosmosException failure) => new(
        SyncErrorCodes.ReplicaUnavailable,
        "The session replica is not available yet. Try again shortly.",
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
                "The session replica is not available yet. Try again shortly.",
                failure);
        }
    }

    /// <summary>The envelope a change feed page arrives in. Only the documents
    /// are read; the <c>_rid</c> and <c>_count</c> beside them say nothing this
    /// adapter cannot see for itself.</summary>
    private sealed record ChangeFeedPage(
        [property: JsonPropertyName("Documents")] IReadOnlyList<SessionDocument> Documents);
}
