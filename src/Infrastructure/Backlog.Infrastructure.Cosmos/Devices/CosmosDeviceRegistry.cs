using System.Net;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Backlog.Infrastructure.Cosmos.Devices;

/// <summary>
/// The device registry, in Cosmos DB (.arc42/adr/0005 §Identity).
/// <para>
/// One container, partitioned on <c>/id</c> rather than on the owner — the
/// reasoning is on <see cref="DeviceDocument"/>. The consequence for this class
/// is which of its four reads are point reads and which are queries:
/// <see cref="FindById"/>, the one on every token mint, reads a single document
/// by its own key; <see cref="CountByOwner"/> and <see cref="OwnerExists"/> ask
/// the whole container for one owner's documents, and are correspondingly
/// reserved for the Devices tab and for registration.
/// </para>
/// <para>
/// The isolation story is unchanged by this adapter, and worth restating
/// because the container is now partitioned in a way that makes the wrong
/// read easy: the service reaches Cosmos under one identity that can see every
/// partition, and what keeps an owner inside their own devices is that every
/// caller of the two owner-scoped reads passes the owner out of a verified
/// token. Nothing here re-checks that, and nothing here could — the registry is
/// what the token is minted from.
/// </para>
/// <para>
/// A registry rather than a replica, and the difference shows in what it does
/// not have: no change feed, no cursor, no tombstone, no TTL. A device is
/// written once and read many times; there is no offline copy that needs to
/// converge with it.
/// </para>
/// </summary>
internal sealed class CosmosDeviceRegistry : IDeviceRegistry
{
    private const string UnavailableMessage = "The device registry is not available yet. Try again shortly.";

    private readonly CosmosContainerHandle _container;

    public CosmosDeviceRegistry(IServiceProvider services, IOptions<CosmosOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _container = new CosmosContainerHandle(
            services,
            options.Value.DatabaseName,
            options.Value.DevicesContainerName,
            UnavailableMessage);
    }

    public async Task Add(Device device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        var document = DeviceDocumentFactory.From(device);

        try
        {
            // Create rather than upsert. A device id is a v7 GUID this service
            // minted a moment ago, so a collision is not a race between two
            // writers — it is a bug, and a 409 from the store is the right way
            // for it to surface rather than one registration silently
            // overwriting another's credential hash.
            await Container().CreateItemAsync(
                document,
                new PartitionKey(document.Id),
                cancellationToken: cancellationToken);
        }
        catch (CosmosException failure)
        {
            throw Fault(failure);
        }
    }

    public async Task<Device?> FindById(DeviceId id, CancellationToken cancellationToken = default)
    {
        var key = ReplicaDocumentSerialization.Key(id.Value);

        try
        {
            var response = await Container().ReadItemAsync<DeviceDocument>(
                key,
                new PartitionKey(key),
                cancellationToken: cancellationToken);

            return DeviceDocumentFactory.ToDevice(response.Resource);
        }
        catch (CosmosException failure) when (failure.StatusCode is HttpStatusCode.NotFound)
        {
            // Unknown device. The handler answers the same 401 it answers a
            // wrong credential with, so this null carries no information a
            // caller could not already get.
            return null;
        }
        catch (CosmosException failure)
        {
            throw Fault(failure);
        }
    }

    public Task<int> CountByOwner(OwnerId ownerId, CancellationToken cancellationToken = default) =>
        CountOwnedBy(ownerId, cancellationToken);

    public async Task<bool> OwnerExists(OwnerId ownerId, CancellationToken cancellationToken = default) =>
        await CountOwnedBy(ownerId, cancellationToken) > 0;

    /// <summary>
    /// The one query in this adapter, and it crosses partitions on purpose —
    /// see <see cref="DeviceDocument"/>. One query rather than a count and an
    /// exists apiece: the count is what both callers actually want, and a
    /// separate <c>EXISTS</c> would save nothing on a result set bounded by how
    /// many machines a person owns.
    /// </summary>
    private async Task<int> CountOwnedBy(OwnerId ownerId, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.ownerId = @owner")
            .WithParameter("@owner", ReplicaDocumentSerialization.Key(ownerId.Value));

        using var results = Container().GetItemQueryIterator<int>(query);

        try
        {
            var count = 0;

            while (results.HasMoreResults)
            {
                foreach (var partial in await results.ReadNextAsync(cancellationToken))
                {
                    // A cross-partition aggregate arrives already merged by the
                    // SDK; summing is for the shape of the API, not for the
                    // arithmetic.
                    count += partial;
                }
            }

            return count;
        }
        catch (CosmosException failure)
        {
            throw Fault(failure);
        }
    }

    private Container Container() => _container.Container();

    /// <summary>What a Cosmos failure becomes on the way out: a coded problem
    /// rather than an unclassified 500 (inherited ADR 0017). Two answers rather
    /// than the replica's three — nothing here can be too large.</summary>
    private static SyncReplicaException Fault(CosmosException failure) =>
        failure.StatusCode is HttpStatusCode.TooManyRequests
            ? new SyncReplicaException(
                SyncErrorCodes.ReplicaBusy,
                "The device registry is busy. Try again shortly.",
                failure)
            : new SyncReplicaException(SyncErrorCodes.ReplicaUnavailable, UnavailableMessage, failure);
}
