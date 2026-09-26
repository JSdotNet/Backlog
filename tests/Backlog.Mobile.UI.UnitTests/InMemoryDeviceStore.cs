using Backlog.Mobile.UI.Outbox;
using Backlog.Mobile.UI.Tasks;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>A device store that forgets on dispose. The SQLite store has its own
/// round-trip tests; the screens only need something that keeps what it is given.</summary>
internal sealed class InMemoryDeviceStore : IDeviceStore
{
    private readonly List<OutboxEntry> _outbox = [];
    private CachedInbox? _inbox;

    public InMemoryDeviceStore(CachedInbox? inbox = null) => _inbox = inbox;

    public IReadOnlyList<OutboxEntry> ReadOutbox()
    {
        lock (_outbox) return [.. _outbox.OrderBy(entry => entry.CreatedAt)];
    }

    public Task AddAsync(OutboxEntry entry, CancellationToken cancellationToken = default)
    {
        lock (_outbox) _outbox.Add(entry);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(OutboxEntry entry, CancellationToken cancellationToken = default)
    {
        lock (_outbox)
        {
            var index = _outbox.FindIndex(queued => queued.Id == entry.Id);
            if (index >= 0) _outbox[index] = entry;
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_outbox) _outbox.RemoveAll(entry => entry.Id == id);
        return Task.CompletedTask;
    }

    public CachedInbox? ReadInbox() => _inbox;

    public Task SaveInboxAsync(CachedInbox inbox, CancellationToken cancellationToken = default)
    {
        _inbox = inbox;
        return Task.CompletedTask;
    }
}

internal static class TestOutbox
{
    /// <summary>What <c>AddDeviceOutbox</c> registers, over a store in memory. The
    /// capture kind sends through whichever <see cref="Services.CloudSyncClient"/>
    /// the test registered.</summary>
    public static IServiceCollection AddTestDeviceOutbox(this IServiceCollection services, IDeviceStore? store = null, ITaskViewStore? taskView = null)
    {
        services.AddSingleton(store ?? new InMemoryDeviceStore());
        services.AddSingleton<IOutboxKind, CaptureOutboxKind>();
        services.AddSingleton<IOutboxKind, TaskOutboxKind>();
        services.AddSingleton<DeviceOutbox>();
        services.AddSingleton(taskView ?? new InMemoryTaskViewStore());
        services.AddSingleton<TaskViewProjection>();
        return services;
    }
}
