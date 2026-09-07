using Backlog.Infrastructure.Sync.Extensions;
using Backlog.Modules.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// What a host gets for calling each half of the composition, asserted the way
/// a host finds out: by building the provider with the validation the generic
/// host turns on in Development.
/// <para>
/// These build a provider rather than reading the descriptors back, because the
/// failure this covers is not a missing registration — it is a registration
/// whose constructor no host service can satisfy, and only
/// <see cref="ServiceProviderOptions.ValidateOnBuild"/> says so. A mobile head
/// has no <see cref="ITaskRepository"/> and never will; when the task-sync
/// services were registered for every caller, both mobile hosts died inside
/// <c>Build()</c> with a resolution failure no unit test could see.
/// </para>
/// </summary>
public class SyncClientRegistrationTests
{
    private static readonly Uri SyncAddress = new("https://sync.example");

    /// <summary>The validation the generic host applies in Development, which is
    /// where the crash this covers happened.</summary>
    private static ServiceProvider Build(IServiceCollection services) =>
        services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

    /// <summary>
    /// The mobile shape: a credential store and nothing else of Tasks. Both
    /// mobile heads compose exactly this, so the pairing surface has to stand on
    /// its own.
    /// </summary>
    [Fact]
    public void A_host_with_no_task_repository_composes_the_pairing_surface()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSyncClient(SyncAddress);

        using var provider = Build(services);

        Assert.NotNull(provider.GetRequiredService<DevicePairingClient>());
        Assert.NotNull(provider.GetRequiredService<SyncTokenProvider>());
    }

    /// <summary>
    /// Task replication is opt-in, and this is what "opt-in" has to mean: a host
    /// that did not ask for it does not carry it. Asserted as an absence rather
    /// than through the test above, because a registration that happens to be
    /// satisfiable in some other host would slip past a validation check.
    /// </summary>
    [Fact]
    public void The_pairing_surface_alone_brings_no_task_sync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSyncClient(SyncAddress);

        using var provider = Build(services);

        Assert.Null(provider.GetService<TaskSyncSession>());
        Assert.Null(provider.GetService<TaskReplicaMerge>());
        Assert.Null(provider.GetService<TaskSyncClient>());
    }

    /// <summary>
    /// The desktop shape: a repository and a sync-state store of the host's
    /// choosing, and the opt-in call on top of the pairing surface.
    /// </summary>
    [Fact]
    public void A_host_that_opts_in_composes_the_session()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSingleton<ITaskRepository>(new InMemoryTaskStore());
        services.AddSingleton<ITaskSyncStateStore>(new InMemoryTaskSyncStateStore());
        services.AddSyncClient(SyncAddress);
        services.AddTaskSyncClient(SyncAddress);

        using var provider = Build(services);

        Assert.NotNull(provider.GetRequiredService<TaskSyncSession>());
        Assert.NotNull(provider.GetRequiredService<DevicePairingClient>());
    }

    /// <summary>
    /// Opting in without choosing a sync-state store leaves the session
    /// registered and unconstructable — the case Settings' <c>ResolveTaskSync</c>
    /// guard exists for. It is pinned here so that guard keeps describing
    /// something real: the container throws rather than answering null.
    /// </summary>
    [Fact]
    public void Opting_in_without_a_sync_state_store_leaves_the_session_unconstructable()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSingleton<ITaskRepository>(new InMemoryTaskStore());
        services.AddSyncClient(SyncAddress);
        services.AddTaskSyncClient(SyncAddress);

        // No ValidateOnBuild here: the point is what a host that skipped its
        // store sees at resolution, which is the shape the screen guards.
        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetService<TaskSyncSession>());
    }
}
