using Backlog.Infrastructure.Sync.DevbookSubjects;
using Backlog.Infrastructure.Sync.Extensions;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.SharedKernel;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// What a host gets for opting into devbook subject replication, asserted the
/// way <see cref="AnnotationSyncRegistrationTests"/> asserts the annotation
/// feed: by building the provider with the validation the generic host turns on
/// in Development.
/// </summary>
public class DevbookSubjectSyncRegistrationTests : IDisposable
{
    private static readonly Uri SyncAddress = new("https://sync.example");

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "backlog-devbook-subject-sync-registration-tests", Guid.NewGuid().ToString("n"));

    private static ServiceProvider Build(IServiceCollection services) =>
        services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

    [Fact]
    public void The_pairing_surface_alone_brings_no_subject_sync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSyncClient(SyncAddress);

        using var provider = Build(services);

        Assert.Null(provider.GetService<DevbookSubjectSyncSession>());
        Assert.Null(provider.GetService<DevbookSubjectSyncWorker>());
    }

    [Fact]
    public void A_host_that_opts_in_composes_the_exchange_and_the_loop()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSingleton<IAppFeatureSettings>(new StubFeatureSettings(enabled: true));
        services.AddSingleton<IDevbookSubjectStateStore>(new InMemoryDevbookSubjectStateStore());
        services.AddSyncClient(SyncAddress);
        services.AddDevbookSubjectSyncStore(_root);
        services.AddDevbookSubjectSyncClient(SyncAddress);

        using var provider = Build(services);

        Assert.NotNull(provider.GetRequiredService<DevbookSubjectSyncSession>());
        Assert.Equal(
            Path.Combine(_root, "devbook-subject-sync-state.json"),
            provider.GetRequiredService<IDevbookSubjectSyncStateStore>().StorePath);

        using var worker = provider.GetRequiredService<DevbookSubjectSyncWorker>();
        Assert.NotNull(worker);
    }

    /// <summary>Opting in without the stores leaves the exchange and the loop
    /// registered and unconstructable — the shape the loop's resolve
    /// guards.</summary>
    [Fact]
    public void Opting_in_without_the_stores_leaves_the_exchange_unconstructable()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSingleton<IAppFeatureSettings>(new StubFeatureSettings(enabled: true));
        services.AddSyncClient(SyncAddress);
        services.AddDevbookSubjectSyncClient(SyncAddress);

        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetService<DevbookSubjectSyncSession>());
        Assert.Throws<InvalidOperationException>(() => provider.GetService<DevbookSubjectSyncWorker>());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
