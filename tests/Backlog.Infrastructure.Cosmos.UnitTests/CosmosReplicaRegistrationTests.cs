using Backlog.Infrastructure.Cosmos.Extensions;
using Backlog.Infrastructure.Cosmos.Sessions;
using Backlog.Infrastructure.Cosmos.Tasks;
using Backlog.Modules.Sync.Ports;

using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;

namespace Backlog.Infrastructure.Cosmos.UnitTests;

/// <summary>
/// What happens when nobody has configured Cosmos, which is a different question
/// in a development run and in a deployed service.
/// <para>
/// Registering nothing is exactly right locally: the module's in-memory
/// <c>TryAddSingleton</c>s stand, and the service runs end to end with no
/// emulator. It is exactly wrong deployed, where the same silence means a
/// service that lost its connection string starts healthy, serves every owner
/// out of process memory, and loses all of it on the next restart.
/// </para>
/// </summary>
public class CosmosReplicaRegistrationTests
{
    /// <summary>An account nothing ever connects to. The registration reads the
    /// endpoint and builds a client factory; no call is made here.</summary>
    private const string UnreachableAccount = "https://localhost:8081/";

    [Fact]
    public void A_development_run_with_no_cosmos_registers_nothing()
    {
        var builder = Builder(Environments.Development);

        builder.AddCosmosReplicas();

        Assert.DoesNotContain(builder.Services, service => service.ServiceType == typeof(ITaskReplica));
        Assert.DoesNotContain(builder.Services, service => service.ServiceType == typeof(ISessionReplica));
    }

    /// <summary>
    /// Inherited ADR 0018 in one line: bind, validate, fail fast. A deployed
    /// service with no Cosmos configured has lost something rather than declined
    /// something, and the only place that can be said out loud is startup —
    /// afterwards it looks exactly like a service that is working.
    /// </summary>
    [Fact]
    public void Anywhere_but_development_a_missing_configuration_stops_the_start()
    {
        var builder = Builder(Environments.Production);

        var refusal = Assert.Throws<InvalidOperationException>(builder.AddCosmosReplicas);

        Assert.Contains("Cosmos", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_configured_account_endpoint_is_enough_to_register_both_replicas()
    {
        var builder = Builder(Environments.Production);
        builder.Configuration["Sync:Cosmos:AccountEndpoint"] = UnreachableAccount;

        builder.AddCosmosReplicas();

        var tasks = Assert.Single(
            builder.Services,
            service => service.ServiceType == typeof(ITaskReplica));

        var sessions = Assert.Single(
            builder.Services,
            service => service.ServiceType == typeof(ISessionReplica));

        Assert.Equal(typeof(CosmosTaskReplica), tasks.ImplementationType);
        Assert.Equal(typeof(CosmosSessionReplica), sessions.ImplementationType);
    }

    /// <summary>
    /// One client for the account, however many containers hang off it.
    /// Registering it twice would build two connection pools and two background
    /// endpoint refreshes against the same account, and only one of them would
    /// ever be resolved — which is the failure a second registration method per
    /// replica would have introduced quietly.
    /// </summary>
    [Fact]
    public void The_cosmos_client_is_registered_once_for_both_replicas()
    {
        var builder = Builder(Environments.Production);
        builder.Configuration["Sync:Cosmos:AccountEndpoint"] = UnreachableAccount;

        builder.AddCosmosReplicas();

        Assert.Single(builder.Services, service => service.ServiceType == typeof(CosmosClient));
    }

    private static HostApplicationBuilder Builder(string environment) =>
        Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environment });
}
