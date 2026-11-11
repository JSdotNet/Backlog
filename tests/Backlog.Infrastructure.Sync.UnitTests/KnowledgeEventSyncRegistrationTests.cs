using Backlog.Infrastructure.Sync.Extensions;
using Backlog.Infrastructure.Sync.KnowledgeEvents;
using Backlog.Modules.Events.Abstractions.Services;
using Backlog.SharedKernel;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// What a host gets for opting into knowledge event replication, asserted the way
/// <see cref="DevbookSubjectSyncRegistrationTests"/> asserts the subject feed: by building the
/// provider with the validation the generic host turns on in Development.
/// </summary>
public class KnowledgeEventSyncRegistrationTests : IDisposable
{
    private static readonly Uri SyncAddress = new("https://sync.example");

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "backlog-knowledge-event-sync-registration-tests", Guid.NewGuid().ToString("n"));

    private static ServiceProvider Build(IServiceCollection services) =>
        services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

    [Fact]
    public void The_pairing_surface_alone_brings_no_knowledge_event_sync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSyncClient(SyncAddress);

        using var provider = Build(services);

        Assert.Null(provider.GetService<KnowledgeEventSyncSession>());
        Assert.Null(provider.GetService<KnowledgeEventSyncWorker>());
    }

    [Fact]
    public void A_host_that_opts_in_composes_the_exchange_and_the_loop()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSingleton<IAppFeatureSettings>(new StubFeatureSettings(enabled: true));
        services.AddSingleton<IKnowledgeEventReplication>(new InMemoryKnowledgeEventReplication());
        services.AddSyncClient(SyncAddress);
        services.AddKnowledgeEventSyncStore(_root);
        services.AddKnowledgeEventSyncClient(SyncAddress);

        using var provider = Build(services);

        Assert.NotNull(provider.GetRequiredService<KnowledgeEventSyncSession>());
        Assert.Equal(
            Path.Combine(_root, "knowledge-event-sync-state.json"),
            provider.GetRequiredService<IKnowledgeEventSyncStateStore>().StorePath);

        using var worker = provider.GetRequiredService<KnowledgeEventSyncWorker>();
        Assert.NotNull(worker);
    }

    /// <summary>Opting in on a head without Events leaves the exchange registered and
    /// unconstructable — the shape the loop's resolve guards.</summary>
    [Fact]
    public void Opting_in_without_events_leaves_the_exchange_unconstructable()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSingleton<IAppFeatureSettings>(new StubFeatureSettings(enabled: true));
        services.AddSyncClient(SyncAddress);
        services.AddKnowledgeEventSyncStore(_root);
        services.AddKnowledgeEventSyncClient(SyncAddress);

        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetService<KnowledgeEventSyncSession>());
    }

    [Fact]
    public void The_progress_file_round_trips_and_a_file_that_cannot_be_read_is_nowhere()
    {
        var path = Path.Combine(_root, "knowledge-event-sync-state.json");
        var stamp = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var owner = Guid.NewGuid();

        new FileKnowledgeEventSyncStateStore(path).Save(new KnowledgeEventSyncState(stamp, "cursor-1", owner, Guid.NewGuid()));

        var reread = new FileKnowledgeEventSyncStateStore(path).Current;
        Assert.Equal(stamp, reread.PushWatermark);
        Assert.Equal("cursor-1", reread.PullCursor);
        Assert.Equal(owner, reread.OwnerId);

        File.WriteAllText(path, "{not json");
        Assert.Equal(DateTimeOffset.MinValue, new FileKnowledgeEventSyncStateStore(path).Current.PushWatermark);
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
