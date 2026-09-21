using Backlog.Infrastructure.Sync.Annotations;
using Backlog.Infrastructure.Sync.Extensions;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.SharedKernel;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// What a host gets for opting into annotation replication, asserted the way
/// <see cref="SyncClientRegistrationTests"/> asserts the other two: by building
/// the provider with the validation the generic host turns on in Development.
/// </summary>
public class AnnotationSyncRegistrationTests : IDisposable
{
    private static readonly Uri SyncAddress = new("https://sync.example");

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "backlog-annotation-sync-registration-tests", Guid.NewGuid().ToString("n"));

    private static ServiceProvider Build(IServiceCollection services) =>
        services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

    [Fact]
    public void The_pairing_surface_alone_brings_no_annotation_sync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSyncClient(SyncAddress);

        using var provider = Build(services);

        Assert.Null(provider.GetService<AnnotationSyncSession>());
        Assert.Null(provider.GetService<AnnotationSyncClient>());
        Assert.Null(provider.GetService<AnnotationSyncWorker>());
    }

    [Fact]
    public void A_host_that_opts_in_composes_the_exchange_and_the_loop()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSingleton<IAppFeatureSettings>(new StubFeatureSettings(enabled: true));
        services.AddSingleton<IDevbookAnnotationStore>(new InMemoryDevbookAnnotationStore());
        services.AddSyncClient(SyncAddress);
        services.AddAnnotationSyncStore(_root);
        services.AddAnnotationSyncClient(SyncAddress);

        using var provider = Build(services);

        Assert.NotNull(provider.GetRequiredService<AnnotationSyncSession>());
        Assert.Equal(Path.Combine(_root, "annotation-sync-state.json"), provider.GetRequiredService<IAnnotationSyncStateStore>().StorePath);

        using var worker = provider.GetRequiredService<AnnotationSyncWorker>();
        Assert.NotNull(worker);
    }

    /// <summary>Opting in without the store or the annotation store leaves the
    /// exchange and the loop registered and unconstructable — the shape the
    /// other two exchanges have, and the shape the loop's resolve guards.</summary>
    [Fact]
    public void Opting_in_without_the_stores_leaves_the_exchange_unconstructable()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSingleton<IAppFeatureSettings>(new StubFeatureSettings(enabled: true));
        services.AddSyncClient(SyncAddress);
        services.AddAnnotationSyncClient(SyncAddress);

        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetService<AnnotationSyncSession>());
        Assert.Throws<InvalidOperationException>(() => provider.GetService<AnnotationSyncWorker>());
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
