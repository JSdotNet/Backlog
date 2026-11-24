using System.Net;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Backlog.Infrastructure.Cosmos.AssertionReplays;

/// <summary>
/// Spent device assertion ids, in Cosmos DB (local ADR 0058 §2), so that every instance
/// of the service — a restart, or a second revision during a deploy — refuses an id
/// another one already took. The in-memory cache stays the Development stand-in.
/// <para>
/// Spending is one create of the id's document. A 201 is the first spend; a 409 is a
/// replay, or a lapsed id Cosmos has not reaped yet, both refused, the latter in the safe
/// direction. Any other failure, the account not answering included, goes out as the
/// coded fault, so the token exchange answers 503 and never mints on an assertion it
/// could not check — a timeout that may have written the document as well: the device
/// signs a new assertion for its retry.
/// </para>
/// </summary>
internal sealed class CosmosAssertionReplayCache : IAssertionReplayCache
{
    private const string UnavailableMessage = "The assertion replay store is not available yet. Try again shortly.";

    private readonly CosmosContainerHandle _container;
    private readonly TimeProvider _clock;

    public CosmosAssertionReplayCache(IServiceProvider services, IOptions<CosmosOptions> options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        _clock = clock;
        _container = new CosmosContainerHandle(
            services,
            options.Value.DatabaseName,
            options.Value.AssertionReplaysContainerName,
            UnavailableMessage);
    }

    public async Task<bool> TryAdd(DeviceId deviceId, string assertionId, DateTimeOffset acceptableUntil, CancellationToken cancellationToken = default)
    {
        var document = AssertionReplayDocumentFactory.From(deviceId, assertionId, acceptableUntil, _clock.GetUtcNow());

        try
        {
            var container = await _container.ContainerAsync(cancellationToken).ConfigureAwait(false);
            await container.CreateItemAsync(
                document,
                new PartitionKey(document.Id),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (CosmosException failure) when (failure.StatusCode is HttpStatusCode.Conflict)
        {
            return false;
        }
        catch (CosmosException failure)
        {
            throw Fault(failure);
        }
    }

    /// <summary>What a Cosmos failure becomes on the way out: a coded problem rather than
    /// an unclassified 500 (inherited ADR 0017).</summary>
    private static SyncReplicaException Fault(CosmosException failure) =>
        failure.StatusCode is HttpStatusCode.TooManyRequests
            ? new SyncReplicaException(
                SyncErrorCodes.ReplicaBusy,
                "The assertion replay store is busy. Try again shortly.",
                failure)
            : new SyncReplicaException(SyncErrorCodes.ReplicaUnavailable, UnavailableMessage, failure);
}
