using System.Net;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Backlog.Infrastructure.Cosmos.PairingCodes;

/// <summary>
/// The pairing-code store, in Cosmos DB (.devbook/arc42/adr/0005 §Identity).
/// <para>
/// One container, partitioned on the code hash, every operation a point
/// operation on one document. What this adapter owes the handler above it is
/// one guarantee the in-memory store states in its own terms: <see cref="TryBurn"/>
/// answers true to exactly one caller per code. Here that is an etag
/// precondition on the replace that marks the code redeemed — the store's own
/// compare-and-swap, and the only mechanism that holds across two instances of
/// the service, which a Container App scaling from zero to one and back can
/// briefly have.
/// </para>
/// <para>
/// Short-lived storage, and the reason there is a <c>ttl</c> on every
/// document: a code lives ten minutes and is read at most a handful of times,
/// so once Cosmos has reaped it nothing here ever needs it back. The port says
/// as much, and the handler's own expiry check is what actually turns a stale
/// code away — the TTL is housekeeping, not enforcement.
/// </para>
/// </summary>
internal sealed class CosmosPairingCodeStore : IPairingCodeStore
{
    private const string UnavailableMessage = "The pairing-code store is not available yet. Try again shortly.";

    private readonly CosmosContainerHandle _container;
    private readonly TimeProvider _clock;

    public CosmosPairingCodeStore(IServiceProvider services, IOptions<CosmosOptions> options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        _clock = clock;
        _container = new CosmosContainerHandle(
            services,
            options.Value.DatabaseName,
            options.Value.PairingCodesContainerName,
            UnavailableMessage);
    }

    public async Task Add(PairingCode code, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);

        // The TTL is relative to this write, so the clock is read here and not
        // when the code was minted a few microseconds earlier — the difference
        // is nothing, and the factory taking a `now` is what lets the rule be
        // pinned without a store.
        var document = PairingCodeDocumentFactory.From(code, _clock.GetUtcNow());

        try
        {
            // Upsert, matching the in-memory store: a hash collision between
            // two live codes is a one-in-10^11 event over a ten-minute window,
            // and if it ever happened the newer code winning is the same answer
            // the dictionary gives.
            await Container().UpsertItemAsync(
                document,
                new PartitionKey(document.Id),
                cancellationToken: cancellationToken);
        }
        catch (CosmosException failure)
        {
            throw Fault(failure);
        }
    }

    public async Task<PairingCode?> FindByHash(string codeHash, CancellationToken cancellationToken = default)
    {
        var document = await Read(codeHash, cancellationToken);

        return document is null ? null : PairingCodeDocumentFactory.ToPairingCode(document);
    }

    /// <summary>
    /// Read, then replace on the condition that nothing has changed since. Two
    /// devices racing on one code both read <c>redeemed: false</c> under the
    /// same etag; the first replace moves the etag and the second fails its
    /// precondition with a 412, which is the false the handler turns into
    /// "already used". A code that is gone — reaped, or never there — is false
    /// for the same reason: nobody burning it is this caller.
    /// </summary>
    public async Task<bool> TryBurn(string codeHash, CancellationToken cancellationToken = default)
    {
        var document = await Read(codeHash, cancellationToken);

        if (document is null || document.Redeemed || string.IsNullOrEmpty(document.ETag))
        {
            return false;
        }

        document.Redeemed = true;

        // The replace restarts the document's TTL clock, so the ttl is reset
        // here rather than carried over: a burned code is kept for the grace
        // alone, long enough for a replay to be told "already used" rather
        // than "not found", and not a full window longer than that.
        document.Ttl = PairingCodeDocumentFactory.ReapGraceSeconds;

        try
        {
            await Container().ReplaceItemAsync(
                document,
                document.Id,
                new PartitionKey(document.Id),
                new ItemRequestOptions { IfMatchEtag = document.ETag },
                cancellationToken);

            return true;
        }
        catch (CosmosException failure) when (failure.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (CosmosException failure)
        {
            throw Fault(failure);
        }
    }

    private async Task<PairingCodeDocument?> Read(string codeHash, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Container().ReadItemAsync<PairingCodeDocument>(
                codeHash,
                new PartitionKey(codeHash),
                cancellationToken: cancellationToken);

            return response.Resource;
        }
        catch (CosmosException failure) when (failure.StatusCode is HttpStatusCode.NotFound)
        {
            // A typo and a reaped code alike. The handler answers both as "not
            // one this service issued", and deliberately cannot tell them apart.
            return null;
        }
        catch (CosmosException failure)
        {
            throw Fault(failure);
        }
    }

    private Container Container() => _container.Container();

    /// <summary>What a Cosmos failure becomes on the way out: a coded problem
    /// rather than an unclassified 500 (inherited ADR 0017).</summary>
    private static SyncReplicaException Fault(CosmosException failure) =>
        failure.StatusCode is HttpStatusCode.TooManyRequests
            ? new SyncReplicaException(
                SyncErrorCodes.ReplicaBusy,
                "The pairing-code store is busy. Try again shortly.",
                failure)
            : new SyncReplicaException(SyncErrorCodes.ReplicaUnavailable, UnavailableMessage, failure);
}
