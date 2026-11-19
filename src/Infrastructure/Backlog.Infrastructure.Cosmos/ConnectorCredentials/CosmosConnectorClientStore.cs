using System.Net;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backlog.Infrastructure.Cosmos.ConnectorCredentials;

/// <summary>
/// The remote connector's client — its secret hash, times and generation — in Cosmos DB
/// (local ADR 0058 §8). It has to survive a restart: the service scales to zero, and a lost
/// hash disconnects the connector until the person makes a new secret.
/// <para>
/// One document per client id in the <c>connectorCredentials</c> container, every
/// operation a point operation, and both writes conditioned on the etag of a read.
/// <see cref="Save"/> stores the next generation only over the one before it, so two
/// devices replacing the secret at once cannot both win with different hashes: the loser
/// gets a retryable fault and reads again. <see cref="StampLastUsed"/> writes only while the
/// generation it was given is still current, so a stamp racing a revoke fails its
/// precondition instead of writing the revoked hash back.
/// </para>
/// </summary>
internal sealed class CosmosConnectorClientStore : IConnectorClientStore
{
    /// <summary>How many times a write re-reads after losing its precondition. The race it
    /// loses is almost always a stamp of the same moment, so one more read settles it; a
    /// stamp is a convenience, and giving up after a few is never wrong.</summary>
    private const int WriteAttempts = 3;

    private readonly CosmosContainerHandle _container;
    private readonly ILogger<CosmosConnectorClientStore> _log;

    public CosmosConnectorClientStore(
        IServiceProvider services,
        IOptions<CosmosOptions> options,
        ILogger<CosmosConnectorClientStore> log)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);

        _log = log;
        _container = new CosmosContainerHandle(
            services,
            options.Value.DatabaseName,
            options.Value.ConnectorCredentialsContainerName,
            ConnectorCredentialFaults.UnavailableMessage);
    }

    public async Task<ConnectorClient?> Find(string clientId, CancellationToken cancellationToken = default)
    {
        var document = await Read(clientId, cancellationToken).ConfigureAwait(false);

        return document is null ? null : ConnectorCredentialDocumentFactory.ToClient(document);
    }

    /// <summary>
    /// Stores <paramref name="client"/> when it is the generation after the stored one — or
    /// the first, when nothing is stored — and otherwise throws a retryable
    /// <see cref="SyncReplicaException"/>. Every caller computes the generation from a read
    /// of its own, so a stored generation other than the one before means another write got
    /// in between, and storing over it would let the losing secret's tokens keep refreshing.
    /// </summary>
    public async Task Save(ConnectorClient client, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        var document = ConnectorCredentialDocumentFactory.From(client);

        for (var attempt = 0; attempt < WriteAttempts; attempt++)
        {
            var current = await Read(client.ClientId, cancellationToken).ConfigureAwait(false);

            if (current is not null && current.Generation != client.Generation - 1)
            {
                throw ConnectorCredentialFaults.Conflict(null);
            }

            try
            {
                var container = await _container.ContainerAsync(cancellationToken).ConfigureAwait(false);

                if (current is null)
                {
                    await container.CreateItemAsync(
                        document,
                        new PartitionKey(document.Id),
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await container.ReplaceItemAsync(
                        document,
                        document.Id,
                        new PartitionKey(document.Id),
                        new ItemRequestOptions { IfMatchEtag = current.ETag },
                        cancellationToken).ConfigureAwait(false);
                }

                return;
            }
            catch (CosmosException failure) when (failure.StatusCode
                is HttpStatusCode.Conflict
                or HttpStatusCode.PreconditionFailed
                or HttpStatusCode.NotFound)
            {
                // Somebody wrote in between — usually a stamp: read again, and the
                // generation check decides.
            }
            catch (CosmosException failure)
            {
                throw ConnectorCredentialFaults.From(failure);
            }
        }

        throw ConnectorCredentialFaults.Conflict(null);
    }

    /// <summary>
    /// Best effort: the stamp runs after a code has burned or a refresh token has rotated,
    /// so a store fault here must not fail the grant — the client would never receive the
    /// token it was owed, and its retry would read as a replay. A fault is logged and
    /// swallowed; only the caller's own cancellation goes out.
    /// </summary>
    public async Task StampLastUsed(string clientId, int generation, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        try
        {
            await Stamp(clientId, generation, at, cancellationToken).ConfigureAwait(false);
        }
        catch (SyncReplicaException failure)
        {
            LogStampSkipped(failure.Code);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The SDK's own request timeout, not the caller giving up.
            LogStampSkipped("timeout");
        }
    }

    private async Task Stamp(string clientId, int generation, DateTimeOffset at, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < WriteAttempts; attempt++)
        {
            var document = await Read(clientId, cancellationToken).ConfigureAwait(false);

            if (document is null
                || string.IsNullOrEmpty(document.ETag)
                || !ConnectorCredentialDocumentFactory.Stamp(document, generation, at))
            {
                return;
            }

            try
            {
                var container = await _container.ContainerAsync(cancellationToken).ConfigureAwait(false);
                await container.ReplaceItemAsync(
                    document,
                    document.Id,
                    new PartitionKey(document.Id),
                    new ItemRequestOptions { IfMatchEtag = document.ETag },
                    cancellationToken).ConfigureAwait(false);

                return;
            }
            catch (CosmosException failure) when (failure.StatusCode is HttpStatusCode.PreconditionFailed)
            {
                // Somebody wrote in between: read again, and the generation check decides.
            }
            catch (CosmosException failure) when (failure.StatusCode is HttpStatusCode.NotFound)
            {
                return;
            }
            catch (CosmosException failure)
            {
                throw ConnectorCredentialFaults.From(failure);
            }
        }
    }

    // No client id, generation or hash: the code says what kind of fault it was, and that
    // is all a reader of the log needs to know the grant went ahead without the stamp.
    private void LogStampSkipped(string reason) =>
        _log.LogWarning("The connector client's last use was not recorded ({Reason}); the grant went ahead.", reason);

    private async Task<ConnectorClientDocument?> Read(string clientId, CancellationToken cancellationToken)
    {
        var id = ConnectorCredentialDocumentFactory.ClientDocumentId(clientId);

        try
        {
            var container = await _container.ContainerAsync(cancellationToken).ConfigureAwait(false);
            var response = await container.ReadItemAsync<ConnectorClientDocument>(
                id,
                new PartitionKey(id),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return response.Resource;
        }
        catch (CosmosException failure) when (failure.StatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (CosmosException failure)
        {
            throw ConnectorCredentialFaults.From(failure);
        }
    }
}
