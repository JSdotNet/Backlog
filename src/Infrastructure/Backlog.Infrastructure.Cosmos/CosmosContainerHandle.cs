using System.Diagnostics;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Ports;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.Cosmos;

/// <summary>
/// One container, resolved on first use — the one thing every adapter in this
/// project does the same way, said once.
/// <para>
/// Lazy rather than resolved in the constructor: the adapters are singletons
/// built while the emulator may still be starting — the AppHost deliberately
/// does not wait on it, it is a 2.5 GB image with a cold start measured in
/// minutes — and a failure to reach Cosmos must surface as a 503 on the call
/// that needed it rather than as a service that would not start. Nothing is
/// created here either: the database and every container come from the AppHost
/// locally and from <c>infra/sync/main.bicep</c> deployed, and a service that
/// could create its own containers would create them without the TTLs and
/// indexing policies that file sets.
/// </para>
/// <para>
/// PublicationOnly, and that mode is doing real work. The default
/// ExecutionAndPublication caches the exception as well as the value: once the
/// factory throws, every later <c>.Value</c> rethrows that same instance and the
/// factory never runs again, so a service that started a second before its
/// store would answer 503 for the life of the process. That is worse than
/// failing to start, because Container Apps restarts a container that died and
/// never one that is answering. PublicationOnly runs the factory again for the
/// next caller; the cost is that two callers racing may both build one, and the
/// loser's is discarded.
/// </para>
/// <para>
/// Lazy has a consequence, and this is it. The client singleton is built inside
/// whichever request needed Cosmos first, and the SDK starts a background
/// endpoint refresh when it is constructed. A timer captures the
/// ExecutionContext it was started on and <see cref="Activity.Current"/>
/// travels in that context, so the refresh five minutes later reports itself
/// as a child of that one push: the <c>sync.push_tasks</c> trace then measures
/// 300 seconds and every latency percentile read off it is a fiction. Clearing
/// <see cref="Activity.Current"/> for the length of the construction is what
/// unparents it. Not <see cref="ExecutionContext.SuppressFlow"/>, which stops
/// the whole context flowing and would take the cancellation and logging
/// scopes with it, and not eager construction, which would trade this for a
/// service that cannot start before its emulator. Only the first caller of any
/// adapter pays this; the client itself is a singleton all of them resolve.
/// </para>
/// </summary>
internal sealed class CosmosContainerHandle
{
    private readonly Lazy<Container> _container;
    private readonly string _unavailableMessage;

    /// <param name="services">Where the <see cref="CosmosClient"/> singleton is
    /// resolved from, on first use rather than now.</param>
    /// <param name="databaseName">The database, from <see cref="CosmosOptions"/>.</param>
    /// <param name="containerName">The container, from <see cref="CosmosOptions"/>.</param>
    /// <param name="unavailableMessage">What a caller is told when the container
    /// cannot be resolved — safe to show a person, and naming which store it
    /// was so the message on a 503 says something.</param>
    public CosmosContainerHandle(
        IServiceProvider services,
        string databaseName,
        string containerName,
        string unavailableMessage)
    {
        ArgumentNullException.ThrowIfNull(services);

        _unavailableMessage = unavailableMessage;
        _container = new Lazy<Container>(() =>
        {
            var ambient = Activity.Current;
            Activity.Current = null;

            try
            {
                return services
                    .GetRequiredService<CosmosClient>()
                    .GetContainer(databaseName, containerName);
            }
            finally
            {
                // The caller is mid-request and the rest of it belongs on the
                // trace it arrived on.
                Activity.Current = ambient;
            }
        }, LazyThreadSafetyMode.PublicationOnly);
    }

    /// <summary>The container, resolved once. A failure to build it is the
    /// store not being reachable, which is the ordinary state of the first
    /// minutes of a local run, so it goes out as the coded 503 rather than as
    /// whatever the SDK threw.</summary>
    public Container Container()
    {
        try
        {
            return _container.Value;
        }
        catch (Exception failure) when (failure is not SyncReplicaException)
        {
            throw new SyncReplicaException(SyncErrorCodes.ReplicaUnavailable, _unavailableMessage, failure);
        }
    }
}
