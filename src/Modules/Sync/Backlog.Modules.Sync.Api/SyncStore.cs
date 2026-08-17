using System.Collections.Concurrent;

namespace Backlog.Modules.Sync.Api;

/// <summary>Capture pushed from a device into the sync layer.</summary>
public sealed record CaptureRequest(string Title, string Source);

/// <summary>An unsynced capture awaiting pickup by the desktop.</summary>
public sealed record InboxItem(Guid Id, string Title, string Source, DateTimeOffset CapturedAt);

/// <summary>
/// In-memory stand-in for the TTL-backed store behind the sync service. The sync
/// layer holds only transient state; canonical data stays on the desktop.
/// </summary>
public sealed class SyncStore
{
    private readonly ConcurrentDictionary<Guid, InboxItem> _items = new();

    public SyncStore()
    {
        Capture("Try Aspire resource commands", "seed");
        Capture("Draft mobile inbox triage flow", "seed");
    }

    public IReadOnlyCollection<InboxItem> All() =>
        _items.Values.OrderByDescending(i => i.CapturedAt).ToList();

    public InboxItem Capture(string title, string source)
    {
        var item = new InboxItem(Guid.NewGuid(), title, source, DateTimeOffset.UtcNow);
        _items[item.Id] = item;
        return item;
    }

    public bool Acknowledge(Guid id) => _items.TryRemove(id, out _);
}
