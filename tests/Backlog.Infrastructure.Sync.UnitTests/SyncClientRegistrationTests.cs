using Backlog.Infrastructure.Sync.Extensions;
using Backlog.Infrastructure.Sync.Sessions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks;
using Backlog.SharedKernel;
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

        // The background loop comes with replication and not with pairing. A
        // head with no local task database has nothing for it to exchange, and a
        // timer that woke up every five minutes to find that out would be a
        // timer on a phone's battery.
        Assert.Null(provider.GetService<TaskSyncWorker>());
    }

    /// <summary>
    /// The desktop shape: a repository, a sync-state store and a feature store of
    /// the host's choosing, and the opt-in call on top of the pairing surface.
    /// <para>
    /// The feature store is on the list because the loop reads it. Opting into
    /// replication now means opting into something that asks whether the person
    /// wants it, so a head with no <c>IAppFeatureSettings</c> fails validation
    /// here rather than at its own startup — which is what this whole file is
    /// for.
    /// </para>
    /// </summary>
    [Fact]
    public void A_host_that_opts_in_composes_the_session_and_the_loop()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSingleton<IAppFeatureSettings>(new StubFeatureSettings(enabled: true));
        services.AddSingleton<ITaskRepository>(new InMemoryTaskStore());
        services.AddSingleton<ITaskSyncStateStore>(new InMemoryTaskSyncStateStore());
        services.AddSyncClient(SyncAddress);
        services.AddTaskSyncClient(SyncAddress);

        using var provider = Build(services);

        Assert.NotNull(provider.GetRequiredService<TaskSyncSession>());
        Assert.NotNull(provider.GetRequiredService<DevicePairingClient>());

        using var worker = provider.GetRequiredService<TaskSyncWorker>();
        Assert.NotNull(worker);
    }

    /// <summary>
    /// Opting in without choosing a sync-state store leaves both the session and
    /// the loop registered and unconstructable — the case Settings'
    /// <c>ResolveTaskSync</c> guard exists for. It is pinned here so that guard
    /// keeps describing something real: the container throws rather than
    /// answering null.
    /// <para>
    /// The worker is asserted as well as the session because the worker is what
    /// the screen now asks for, and it only shares the session's fate by taking
    /// the same store. If it ever stopped taking one, the screen would start
    /// getting an answer on a head that cannot replicate.
    /// </para>
    /// </summary>
    [Fact]
    public void Opting_in_without_a_sync_state_store_leaves_the_session_unconstructable()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSingleton<IAppFeatureSettings>(new StubFeatureSettings(enabled: true));
        services.AddSingleton<ITaskRepository>(new InMemoryTaskStore());
        services.AddSyncClient(SyncAddress);
        services.AddTaskSyncClient(SyncAddress);

        // No ValidateOnBuild here: the point is what a host that skipped its
        // store sees at resolution, which is the shape the screen guards.
        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetService<TaskSyncSession>());
        Assert.Throws<InvalidOperationException>(() => provider.GetService<TaskSyncWorker>());
    }

    // --- Session replication ---------------------------------------------------

    /// <summary>
    /// Session replication is opt-in on its own terms, and this is what that has to
    /// mean: a host that composed task sync but not session sync carries none of it.
    /// The two are separate calls because the heads differ — a head can have a task
    /// database and no session readers, and the mobile heads have neither.
    /// </summary>
    [Fact]
    public void Task_replication_alone_brings_no_session_replication()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSingleton<IAppFeatureSettings>(new StubFeatureSettings(enabled: true));
        services.AddSingleton<ITaskRepository>(new InMemoryTaskStore());
        services.AddSingleton<ITaskSyncStateStore>(new InMemoryTaskSyncStateStore());
        services.AddSyncClient(SyncAddress);
        services.AddTaskSyncClient(SyncAddress);

        using var provider = Build(services);

        Assert.Null(provider.GetService<SessionSyncSession>());
        Assert.Null(provider.GetService<SessionSyncClient>());
        Assert.Null(provider.GetService<SessionSyncWorker>());
    }

    /// <summary>
    /// The desktop shape for sessions: the two stores, a feature store, a credential
    /// store and a session source, plus the opt-in call. Built with the validation
    /// the generic host applies in Development, because the failure this covers is a
    /// registration whose constructor no host service can satisfy — which only
    /// <see cref="ServiceProviderOptions.ValidateOnBuild"/> says out loud.
    /// </summary>
    [Fact]
    public void A_host_that_opts_into_session_sync_composes_the_exchange_and_the_loop()
    {
        using var provider = Build(SessionHost());

        Assert.NotNull(provider.GetRequiredService<SessionSyncSession>());

        using var worker = provider.GetRequiredService<SessionSyncWorker>();
        Assert.NotNull(worker);
    }

    /// <summary>
    /// Opting in without choosing the two stores leaves the exchange and the loop
    /// registered and unconstructable, which is the same shape task sync has and the
    /// same shape a settings screen has to guard. Pinned so that guard keeps
    /// describing something real: the container throws rather than answering null.
    /// </summary>
    [Fact]
    public void Opting_in_without_the_session_stores_leaves_the_exchange_unconstructable()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSingleton<IAppFeatureSettings>(new StubFeatureSettings(enabled: true));
        services.AddSingleton<IAgentSessionSource>(new StubAgentSessionSource());
        services.AddSyncClient(SyncAddress);
        services.AddSessionSyncClient(SyncAddress);

        // No ValidateOnBuild here: the point is what a host that skipped its stores
        // sees at resolution.
        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetService<SessionSyncSession>());
        Assert.Throws<InvalidOperationException>(() => provider.GetService<SessionSyncWorker>());
    }

    /// <summary>
    /// The alias directory is deliberately optional, and this is the host that
    /// proves it: no <c>IRepositoryDirectory</c> anywhere, and the exchange still
    /// composes. A head that has not composed Tasks' adapters is a head where no
    /// repository has an alias yet, which is an arrangement rather than a fault —
    /// the recorded <c>owner/name</c> travels instead.
    /// </summary>
    [Fact]
    public void Session_sync_composes_without_a_repository_directory()
    {
        using var provider = Build(SessionHost());

        Assert.NotNull(provider.GetRequiredService<ISessionRepositoryAliases>());
        Assert.Null(provider.GetRequiredService<ISessionRepositoryAliases>().AliasFor("jsdotnet/backlog"));
    }

    /// <summary>
    /// The replicated source is registered under a key rather than as the plain
    /// <see cref="IAgentSessionSource"/>, so it contributes to the merged source
    /// instead of replacing whatever the host registered. Two unkeyed registrations
    /// would make the last line win, and half the list would vanish with nothing
    /// failing to say so.
    /// </summary>
    [Fact]
    public void The_replicated_source_contributes_rather_than_replacing()
    {
        using var provider = Build(SessionHost());

        // The host's own registration is what an unkeyed ask still answers with.
        Assert.IsType<StubAgentSessionSource>(provider.GetRequiredService<IAgentSessionSource>());

        // And the replicated one is there beside it, for a composite to collect.
        var keyed = provider.GetKeyedServices<IAgentSessionSource>(KeyedService.AnyKey).ToList();
        Assert.Contains(keyed, source => source is ReplicatedAgentSessionSource);
    }

    /// <summary>The composition a desktop head makes, with the two session stores in
    /// memory so nothing here writes a file.</summary>
    private static ServiceCollection SessionHost()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSingleton<IAppFeatureSettings>(new StubFeatureSettings(enabled: true));
        services.AddSingleton<IAgentSessionSource>(new StubAgentSessionSource());
        services.AddSingleton<ISessionSyncStateStore>(new InMemorySessionSyncStateStore());
        services.AddSingleton<IReplicatedSessionStore>(new InMemoryReplicatedSessionStore());
        services.AddSyncClient(SyncAddress);
        services.AddSessionSyncClient(SyncAddress);

        return services;
    }
}
