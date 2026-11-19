using System.Net;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Backlog.Infrastructure.Cosmos.ConnectorCredentials;

/// <summary>
/// The remote connector's refresh tokens in Cosmos DB (local ADR 0058 §8). A token lives
/// thirty days and the service scales to zero, so memory would disconnect the connector on
/// every cold start.
/// <para>
/// One document per token in the <c>connectorCredentials</c> container, addressed by its
/// hash, and every operation a point operation. What this adapter owes the handler is the
/// guarantee the in-memory store gives in its own terms: <see cref="TryRotate"/> answers
/// true to exactly one caller per token. Here that is a replace conditioned on the etag of
/// the read that found the token live — the store's own compare-and-swap, as
/// <c>CosmosPairingCodeStore.TryBurn</c> does, and the only mechanism that holds across two
/// instances of the service.
/// </para>
/// <para>
/// <strong>A family is revoked by one marker document, not by a write per token.</strong>
/// <see cref="RevokeFamily"/> writes <c>family:&lt;family id&gt;</c> and nothing else. A
/// token read with <see cref="FindByHash"/> is revoked when its family's marker exists,
/// whatever its own document says; a rotation and an add check the marker before their
/// write and again after it. So a replacement inserted while a revoke lands — the race a
/// per-token revoke could lose, the patches never seeing a token written after their query —
/// is refused by the rotation that wrote it, and reads as revoked even if that refusal never
/// ran.
/// </para>
/// </summary>
internal sealed class CosmosConnectorRefreshTokenStore : IConnectorRefreshTokenStore
{
    private readonly CosmosContainerHandle _container;
    private readonly TimeProvider _clock;

    public CosmosConnectorRefreshTokenStore(IServiceProvider services, IOptions<CosmosOptions> options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        _clock = clock;
        _container = new CosmosContainerHandle(
            services,
            options.Value.DatabaseName,
            options.Value.ConnectorCredentialsContainerName,
            ConnectorCredentialFaults.UnavailableMessage);
    }

    /// <summary>Writes the token — as revoked when its family already is — and checks the
    /// marker once more afterwards, so a revoke landing during the write still reaches it.
    /// The second check is belt and braces: <see cref="FindByHash"/> reads the marker too, so
    /// a fault on it keeps the write rather than failing an exchange whose code has burned.</summary>
    public async Task Add(ConnectorRefreshToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        var revoked = await IsRevoked(token.FamilyId, cancellationToken).ConfigureAwait(false);
        var document = ConnectorCredentialDocumentFactory.From(token with { Revoked = token.Revoked || revoked }, _clock.GetUtcNow());

        try
        {
            // Upsert, matching the in-memory store: a 256-bit token's hash colliding with a
            // live one is not an event worth a branch.
            var container = await _container.ContainerAsync(cancellationToken).ConfigureAwait(false);
            await container.UpsertItemAsync(
                document,
                new PartitionKey(document.Id),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (CosmosException failure)
        {
            throw ConnectorCredentialFaults.From(failure);
        }

        if (!document.Revoked && await RevokedSinceWrite(token.FamilyId, cancellationToken).ConfigureAwait(false))
        {
            await MarkRevoked(document, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<ConnectorRefreshToken?> FindByHash(string tokenHash, CancellationToken cancellationToken = default)
    {
        var document = await Read(tokenHash, cancellationToken).ConfigureAwait(false);

        if (document is null || !Guid.TryParse(document.FamilyId, out var familyId))
        {
            return null;
        }

        var familyRevoked = await IsRevoked(familyId, cancellationToken).ConfigureAwait(false);

        return ConnectorCredentialDocumentFactory.ToRefreshToken(document, familyRevoked);
    }

    /// <summary>
    /// Read the presented token and its family's marker; write the replacement; replace the
    /// presented token on the etag of that read; then read the marker again. The caller wins
    /// only when the token was live, the swap held, and the family was not revoked on either
    /// side of it. Whenever it loses, the replacement — never handed out — is deleted.
    /// <para>
    /// Once the swap may have committed, a store fault no longer fails the call on its own:
    /// a swap that failed without saying whether it applied is settled by reading the
    /// presented token back, and a fault on the marker read after it keeps the win, because
    /// <see cref="FindByHash"/> checks the marker anyway. Throwing after a committed swap
    /// would leave the client nothing but a retry that reads as a replay.
    /// </para>
    /// </summary>
    public async Task<bool> TryRotate(string presentedHash, ConnectorRefreshToken replacement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        var presented = await Read(presentedHash, cancellationToken).ConfigureAwait(false);
        var now = _clock.GetUtcNow();

        if (presented is null
            || string.IsNullOrEmpty(presented.ETag)
            || !Guid.TryParse(presented.FamilyId, out var familyId)
            || await IsRevoked(familyId, cancellationToken).ConfigureAwait(false)
            || !ConnectorCredentialDocumentFactory.Rotate(presented, replacement.TokenHash, now))
        {
            return false;
        }

        var successor = ConnectorCredentialDocumentFactory.From(replacement, now);

        try
        {
            var container = await _container.ContainerAsync(cancellationToken).ConfigureAwait(false);

            // Create, not upsert: the replacement is new, and a document already at its
            // address is not one this rotation may overwrite.
            await container.CreateItemAsync(
                successor,
                new PartitionKey(successor.Id),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (CosmosException failure) when (failure.StatusCode is HttpStatusCode.Conflict)
        {
            return false;
        }
        catch (CosmosException failure)
        {
            throw ConnectorCredentialFaults.From(failure);
        }

        Exception? ambiguous = null;

        try
        {
            var container = await _container.ContainerAsync(cancellationToken).ConfigureAwait(false);
            await container.ReplaceItemAsync(
                presented,
                presented.Id,
                new PartitionKey(presented.Id),
                new ItemRequestOptions { IfMatchEtag = presented.ETag },
                cancellationToken).ConfigureAwait(false);
        }
        catch (CosmosException failure) when (failure.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound)
        {
            // Another refresh got there first; nothing is stored when the caller loses.
            await Discard(successor, cancellationToken).ConfigureAwait(false);
            return false;
        }
        catch (CosmosException failure)
        {
            ambiguous = failure;
        }
        catch (OperationCanceledException failure) when (!cancellationToken.IsCancellationRequested)
        {
            // The SDK's own request timeout, not the caller giving up.
            ambiguous = failure;
        }

        if (ambiguous is not null)
        {
            // A timeout or a 503 does not say whether the replace applied. If it did, the
            // presented token is spent, and throwing now would leave the client a retry that
            // reads as a replay and revokes the family — so ask the store what happened.
            switch (await SwapOutcome(presentedHash, replacement.TokenHash, cancellationToken).ConfigureAwait(false))
            {
                case Swap.Landed:
                    break;

                case Swap.LostToAnother:
                    await Discard(successor, cancellationToken).ConfigureAwait(false);
                    return false;

                case Swap.NotLanded:
                    await Discard(successor, cancellationToken).ConfigureAwait(false);
                    throw ConnectorCredentialFaults.From(ambiguous);

                default:
                    // Still unknown. The successor stays: nobody holds its plaintext, so it
                    // is harmless if the swap never landed, and its ttl reaps it.
                    throw ConnectorCredentialFaults.From(ambiguous);
            }
        }

        // The swap held, but a revoke may have landed while the replacement was being
        // written. The marker is written before anything else, so reading it now sees it.
        if (await RevokedSinceWrite(familyId, cancellationToken).ConfigureAwait(false))
        {
            await Discard(successor, cancellationToken).ConfigureAwait(false);
            return false;
        }

        return true;
    }

    /// <summary>Writes the family's revocation marker — one upsert, idempotent, and the
    /// whole of the revoke. Every token of the family reads as revoked from then on.</summary>
    public async Task RevokeFamily(Guid familyId, CancellationToken cancellationToken = default)
    {
        var marker = ConnectorCredentialDocumentFactory.FamilyRevocation(familyId, _clock.GetUtcNow());

        try
        {
            var container = await _container.ContainerAsync(cancellationToken).ConfigureAwait(false);
            await container.UpsertItemAsync(
                marker,
                new PartitionKey(marker.Id),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (CosmosException failure)
        {
            throw ConnectorCredentialFaults.From(failure);
        }
    }

    private async Task<bool> IsRevoked(Guid familyId, CancellationToken cancellationToken)
    {
        var id = ConnectorCredentialDocumentFactory.FamilyRevocationId(familyId);

        try
        {
            var container = await _container.ContainerAsync(cancellationToken).ConfigureAwait(false);
            await container.ReadItemAsync<ConnectorFamilyRevocationDocument>(
                id,
                new PartitionKey(id),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (CosmosException failure) when (failure.StatusCode is HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (CosmosException failure)
        {
            throw ConnectorCredentialFaults.From(failure);
        }
    }

    /// <summary>Records on the token's own document that it was written into a revoked
    /// family. A field set, not a replace, so nothing else on the document is overwritten.
    /// The marker already decides; a failure here changes nothing a reader sees.</summary>
    private async Task MarkRevoked(ConnectorRefreshTokenDocument document, CancellationToken cancellationToken)
    {
        try
        {
            var container = await _container.ContainerAsync(cancellationToken).ConfigureAwait(false);
            await container.PatchItemAsync<ConnectorRefreshTokenDocument>(
                document.Id,
                new PartitionKey(document.Id),
                [PatchOperation.Set("/revoked", true)],
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (CosmosException)
        {
            // See the summary: the family marker is what every reader checks.
        }
        catch (SyncReplicaException)
        {
            // The account went away between the two calls; the marker still decides.
        }
    }

    /// <summary>Deletes a replacement whose rotation lost. A failure here leaves a token
    /// nobody holds the plaintext of, which expires with its TTL; it is not worth failing
    /// the refresh for, so it is swallowed.</summary>
    private async Task Discard(ConnectorRefreshTokenDocument successor, CancellationToken cancellationToken)
    {
        try
        {
            var container = await _container.ContainerAsync(cancellationToken).ConfigureAwait(false);
            await container.DeleteItemAsync<ConnectorRefreshTokenDocument>(
                successor.Id,
                new PartitionKey(successor.Id),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (CosmosException)
        {
            // See the summary: an orphan nobody can present, reaped by its ttl.
        }
        catch (SyncReplicaException)
        {
            // The account went away between the two calls; the same orphan, the same answer.
        }
    }

    private async Task<ConnectorRefreshTokenDocument?> Read(string tokenHash, CancellationToken cancellationToken)
    {
        var id = ConnectorCredentialDocumentFactory.RefreshTokenDocumentId(tokenHash);

        try
        {
            var container = await _container.ContainerAsync(cancellationToken).ConfigureAwait(false);
            var response = await container.ReadItemAsync<ConnectorRefreshTokenDocument>(
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

    /// <summary>
    /// The marker read after a write that has already committed. A fault here keeps the
    /// write — the caller is not told it failed — because failing now would strand a client
    /// whose code burned or whose token rotated, and its retry would read as a replay.
    /// Nothing is lost by it: <see cref="FindByHash"/> reads the marker on every lookup, so a
    /// token of a family revoked meanwhile dies there whatever this read could not see.
    /// </summary>
    private async Task<bool> RevokedSinceWrite(Guid familyId, CancellationToken cancellationToken)
    {
        try
        {
            return await IsRevoked(familyId, cancellationToken).ConfigureAwait(false);
        }
        catch (SyncReplicaException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>What a swap that failed ambiguously did, read back from the presented token:
    /// pointing at <paramref name="successorHash"/> means it landed, at another hash that a
    /// concurrent refresh won, at nothing that it did not apply.</summary>
    private async Task<Swap> SwapOutcome(string presentedHash, string successorHash, CancellationToken cancellationToken)
    {
        ConnectorRefreshTokenDocument? presented;

        try
        {
            presented = await Read(presentedHash, cancellationToken).ConfigureAwait(false);
        }
        catch (SyncReplicaException)
        {
            return Swap.Unknown;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Swap.Unknown;
        }

        if (presented is not null && string.Equals(presented.ReplacedByHash, successorHash, StringComparison.Ordinal))
        {
            return Swap.Landed;
        }

        // Gone, revoked, or rotated by somebody else: this caller lost, whatever the fault was.
        if (presented is null || presented.Revoked || !string.IsNullOrEmpty(presented.ReplacedByHash))
        {
            return Swap.LostToAnother;
        }

        return Swap.NotLanded;
    }

    private enum Swap
    {
        Unknown,
        Landed,
        LostToAnother,
        NotLanded,
    }
}
