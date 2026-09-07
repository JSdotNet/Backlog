using Backlog.Infrastructure.Cosmos.Extensions;
using Backlog.Infrastructure.Cosmos.Tasks;
using Backlog.Modules.Sync.Ports;

using Microsoft.Extensions.Hosting;

namespace Backlog.Infrastructure.Cosmos.UnitTests;

/// <summary>
/// What happens when nobody has configured Cosmos, which is a different question
/// in a development run and in a deployed service.
/// <para>
/// Registering nothing is exactly right locally: the module's in-memory
/// <c>TryAddSingleton</c> stands, and the service runs end to end with no
/// emulator. It is exactly wrong deployed, where the same silence means a
/// service that lost its connection string starts healthy, serves every owner
/// out of process memory, and loses all of it on the next restart.
/// </para>
/// </summary>
public class CosmosTaskReplicaRegistrationTests
{
    /// <summary>An account nothing ever connects to. The registration reads the
    /// endpoint and builds a client factory; no call is made here.</summary>
    private const string UnreachableAccount = "https://localhost:8081/";

    [Fact]
    public void A_development_run_with_no_cosmos_registers_nothing()
    {
        var builder = Builder(Environments.Development);

        builder.AddCosmosTaskReplica();

        Assert.DoesNotContain(builder.Services, service => service.ServiceType == typeof(ITaskReplica));
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

        var refusal = Assert.Throws<InvalidOperationException>(builder.AddCosmosTaskReplica);

        Assert.Contains("Cosmos", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_configured_account_endpoint_is_enough_to_register_the_replica()
    {
        var builder = Builder(Environments.Production);
        builder.Configuration["Sync:Cosmos:AccountEndpoint"] = UnreachableAccount;

        builder.AddCosmosTaskReplica();

        var registration = Assert.Single(
            builder.Services,
            service => service.ServiceType == typeof(ITaskReplica));

        Assert.Equal(typeof(CosmosTaskReplica), registration.ImplementationType);
    }

    private static HostApplicationBuilder Builder(string environment) =>
        Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environment });
}
