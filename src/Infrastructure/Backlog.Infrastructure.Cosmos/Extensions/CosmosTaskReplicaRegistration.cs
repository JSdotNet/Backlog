using Backlog.Infrastructure.Cosmos.Tasks;
using Backlog.Modules.Sync.Ports;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Backlog.Infrastructure.Cosmos.Extensions;

/// <summary>
/// Wires the Cosmos-backed task replica into a host — or does nothing at all,
/// deliberately.
/// </summary>
public static class CosmosTaskReplicaRegistration
{
    /// <summary>
    /// Where the account endpoint may be named instead of by a connection
    /// string. A deployed service is given a connection string by its host; a
    /// developer pointing at their own account has an endpoint and a managed
    /// identity and no string to paste.
    /// </summary>
    private const string AccountEndpointKey = $"{CosmosOptions.SectionName}:AccountEndpoint";

    /// <summary>
    /// Registers the Cosmos client and the replica that reads it.
    /// <para>
    /// <strong>In Development it no-ops when Cosmos is not configured.</strong>
    /// That is the point of the guard rather than a convenience: with no
    /// connection string and no account endpoint, the call returns having
    /// registered nothing, the module's
    /// <c>TryAddSingleton&lt;ITaskReplica, InMemoryTaskReplica&gt;</c> stands,
    /// and the sync service runs end to end with no emulator. Every endpoint
    /// test gets that, and so does a bare <c>dotnet run</c> of the service.
    /// </para>
    /// <para>
    /// <strong>Anywhere else the same silence is a defect, so it throws.</strong>
    /// A deployed service that lost its connection string would otherwise start
    /// healthy and serve every owner out of process memory, losing all of it on
    /// the next restart — and nothing about it would look wrong until somebody
    /// noticed their tasks had stopped travelling. Inherited ADR 0018: bind,
    /// validate, fail fast. Startup is the only place this can be said, because
    /// afterwards a memory-backed replica behaves exactly like a working one.
    /// </para>
    /// <para>
    /// Call it before <c>AddSyncModule()</c>. Both registrations are for the same
    /// port, and the module's is a <c>TryAdd</c>, so whichever runs first wins.
    /// </para>
    /// </summary>
    public static TBuilder AddCosmosTaskReplica<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<CosmosOptions>()
            .Bind(builder.Configuration.GetSection(CosmosOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var connectionString = builder.Configuration.GetConnectionString(CosmosOptions.ConnectionName);
        var accountEndpoint = builder.Configuration[AccountEndpointKey];

        if (string.IsNullOrWhiteSpace(connectionString) && string.IsNullOrWhiteSpace(accountEndpoint))
        {
            if (!builder.Environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    $"Cosmos is not configured. Set the '{CosmosOptions.ConnectionName}' connection string or "
                    + $"'{AccountEndpointKey}'. Only a Development run may fall back to the in-memory replica, "
                    + "which loses every owner's tasks when the process ends.");
            }

            return builder;
        }

        builder.AddAzureCosmosClient(
            CosmosOptions.ConnectionName,
            settings =>
            {
                if (string.IsNullOrWhiteSpace(connectionString) && Uri.TryCreate(accountEndpoint, UriKind.Absolute, out var endpoint))
                {
                    // No key and no string: the deployed service reaches Cosmos
                    // with a managed identity, and the endpoint is the whole of
                    // what it needs to be told.
                    settings.AccountEndpoint = endpoint;
                }
            },
            options =>
            {
                // Mandatory, and the single easiest thing here to leave out. The
                // v3 SDK serialises with Newtonsoft by default, which ignores
                // every [JsonPropertyName] on TaskDocument — including the one
                // that makes OwnerId serialise as the /ownerId partition key.
                // Omitting this line does not fail: it silently writes a
                // container with no partitioning that matches the one the bicep
                // declares.
                options.UseSystemTextJsonSerializerWithOptions = TaskDocumentSerialization.Options;

                // Resilience per inherited ADR 0015. The standard HTTP resilience
                // handler does not reach this dependency — CosmosClient owns its
                // own transport and never goes through IHttpClientFactory — so
                // the timeout and the retry policy have to be stated on the
                // client itself or there is none.
                options.RequestTimeout = TimeSpan.FromSeconds(10);
                options.MaxRetryAttemptsOnRateLimitedRequests = 3;
                options.MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(10);

                // Gateway rather than Direct: it is one HTTPS port outbound,
                // which is what a container app and a developer behind a
                // corporate proxy can both actually make, and the extra hop costs
                // a few milliseconds on a workload that syncs a person's tasks.
                options.ConnectionMode = ConnectionMode.Gateway;
            });

        // Add, not TryAdd: this is the concrete registration, and the in-memory
        // one is what steps aside for it. Which is also why the call order in
        // the summary above is not a style note — a TryAdd that ran first would
        // leave the service talking to a dictionary while Cosmos sat there.
        builder.Services.AddSingleton<ITaskReplica, CosmosTaskReplica>();

        return builder;
    }
}
