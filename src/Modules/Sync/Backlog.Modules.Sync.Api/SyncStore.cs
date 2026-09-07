using System.Collections.Concurrent;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Modules.Sync.Api;

/// <summary>
/// In-memory stand-in for the TTL-backed store behind the sync service. The
/// sync layer holds only transient state; canonical data stays on the desktop.
/// <para>
/// Every method takes an <see cref="OwnerId"/>, and that owner comes from the
/// caller's validated token and from nowhere else: the service is what scopes a
/// call to one person's captures. The store it eventually becomes does not
/// enforce that boundary — the service reaches Cosmos under a managed identity
/// that can read every partition, so the scoping is these parameters and the
/// endpoints that fill them in (.arc42/adr/0005 §Identity).
/// </para>
/// <para>
/// It starts empty. It used to seed two captures, which belonged to no owner
/// and so could not survive the scoping: a seeded item is either visible to
/// everybody or to nobody, and neither is a useful demo.
/// </para>
/// </summary>
public sealed class SyncStore
{
    private readonly ConcurrentDictionary<OwnerId, ConcurrentDictionary<Guid, InboxItem>> _byOwner = new();

    public IReadOnlyCollection<InboxItem> All(OwnerId owner) =>
        _byOwner.TryGetValue(owner, out var items)
            ? items.Values.OrderByDescending(i => i.CapturedAt).ToList()
            : [];

    public InboxItem Capture(OwnerId owner, string title, string source)
    {
        var item = new InboxItem(Guid.CreateVersion7(), title, source, DateTimeOffset.UtcNow);
        ItemsFor(owner)[item.Id] = item;
        return item;
    }

    /// <summary>
    /// Removes one capture, and answers false when this owner has no such item.
    /// An id belonging to somebody else is indistinguishable from an id that
    /// never existed, which is the point: the lookup starts from the owner, so
    /// there is no query that could find it.
    /// </summary>
    public bool Acknowledge(OwnerId owner, Guid id) =>
        _byOwner.TryGetValue(owner, out var items) && items.TryRemove(id, out _);

    /// <summary>The owner's partition, created on the way in. Only capture takes
    /// this route: reading and acknowledging go through
    /// <see cref="ConcurrentDictionary{TKey,TValue}.TryGetValue"/> instead, so
    /// that a token naming an owner who has never captured anything does not
    /// leave an empty partition behind for every one that asks.</summary>
    private ConcurrentDictionary<Guid, InboxItem> ItemsFor(OwnerId owner) =>
        _byOwner.GetOrAdd(owner, _ => new ConcurrentDictionary<Guid, InboxItem>());
}
