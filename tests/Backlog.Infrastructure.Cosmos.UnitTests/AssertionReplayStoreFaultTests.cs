using System.Net;
using Backlog.Infrastructure.Cosmos.AssertionReplays;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Cosmos.UnitTests;

/// <summary>
/// The shared replay store (local ADR 0058 §2): an assertion id is spent once per device
/// across every instance of the service, because the store's own create-or-conflict is
/// what decides; and when the store cannot answer, the mint is refused rather than taken
/// unchecked (inherited ADR 0015). The container is <see cref="ScriptedContainer"/>.
/// </summary>
public class AssertionReplayStoreFaultTests
{
    private static readonly DeviceId Device = new(Guid.Parse("3b2e2c9f-3a3d-4b5c-8d4e-6f708192a3b4"));
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_id_is_taken_once_and_a_second_spend_is_a_replay()
    {
        var store = Store(new ScriptedContainer());

        Assert.True(await store.TryAdd(Device, "jti-1", Now.AddMinutes(4), Cancellation));
        Assert.False(await store.TryAdd(Device, "jti-1", Now.AddMinutes(4), Cancellation));
    }

    /// <summary>Two instances of the service, one container: the second instance sees
    /// the id the first spent, which the in-memory store could not.</summary>
    [Fact]
    public async Task An_id_spent_on_one_instance_is_a_replay_on_another()
    {
        var container = new ScriptedContainer();

        Assert.True(await Store(container).TryAdd(Device, "jti-1", Now.AddMinutes(4), Cancellation));
        Assert.False(await Store(container).TryAdd(Device, "jti-1", Now.AddMinutes(4), Cancellation));
    }

    [Fact]
    public async Task The_same_id_from_another_device_is_its_own()
    {
        var store = Store(new ScriptedContainer());

        Assert.True(await store.TryAdd(Device, "jti-1", Now.AddMinutes(4), Cancellation));
        Assert.True(await store.TryAdd(DeviceId.New(), "jti-1", Now.AddMinutes(4), Cancellation));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, false, SyncErrorCodes.ReplicaUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout, false, SyncErrorCodes.ReplicaUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout, true, SyncErrorCodes.ReplicaUnavailable)]
    [InlineData(HttpStatusCode.Forbidden, false, SyncErrorCodes.ReplicaUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, false, SyncErrorCodes.ReplicaBusy)]
    public async Task A_store_that_cannot_answer_refuses_the_spend_with_its_coded_fault(HttpStatusCode status, bool applied, string code)
    {
        var container = new ScriptedContainer
        {
            Script = (operation, _) => operation == "create" ? new ScriptedFault(status, applied) : null,
        };

        var fault = await Assert.ThrowsAsync<SyncReplicaException>(
            () => Store(container).TryAdd(Device, "jti-1", Now.AddMinutes(4), Cancellation));

        Assert.Equal(code, fault.Code);
    }

    [Fact]
    public async Task An_unreachable_account_refuses_the_spend()
    {
        var store = new CosmosAssertionReplayCache(
            Services(new UnreachableCosmosClient()),
            Options.Create(new CosmosOptions()),
            new FakeTimeProvider(Now));

        var fault = await Assert.ThrowsAsync<SyncReplicaException>(
            () => store.TryAdd(Device, "jti-1", Now.AddMinutes(4), Cancellation));

        Assert.Equal(SyncErrorCodes.ReplicaUnavailable, fault.Code);
    }

    private static CosmosAssertionReplayCache Store(ScriptedContainer container) =>
        new(Services(new ScriptedCosmosClient(container)), Options.Create(new CosmosOptions()), new FakeTimeProvider(Now));

    private static ServiceProvider Services(CosmosClient client)
    {
        var services = new ServiceCollection();
        services.AddSingleton(client);
        services.AddSingleton(Options.Create(new CosmosOptions()));
        services.AddSingleton<ILogger<CosmosAccountGate>>(NullLogger<CosmosAccountGate>.Instance);
        services.AddSingleton<CosmosAccountGate>();
        return services.BuildServiceProvider();
    }

    /// <summary>An account that never answers its first read.</summary>
    private sealed class UnreachableCosmosClient : CosmosClient
    {
        public override Task<AccountProperties> ReadAccountAsync() =>
            Task.FromException<AccountProperties>(new HttpRequestException("No account here."));

        public override Container GetContainer(string databaseId, string containerId) => new ScriptedContainer();
    }
}
