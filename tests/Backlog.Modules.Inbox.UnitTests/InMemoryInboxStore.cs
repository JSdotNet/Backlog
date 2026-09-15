using Backlog.Modules.Inbox;
using Backlog.Modules.Inbox.DomainModels;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// Both repository ports over two dictionaries, the way the real adapter
/// answers both over one file. Shared by every test in this project that goes
/// near a handler, so there is one answer to "what does the store do".
/// <para>
/// It records every save so a test can assert that a refused command wrote
/// nothing — which is half of what "the item is not touched" means.
/// </para>
/// </summary>
internal sealed class InMemoryInboxStore : IInboxItemRepository, IInboxOrganizerRepository
{
    public Dictionary<Guid, InboxItem> Items { get; } = [];

    public Dictionary<Guid, InboxList> Lists { get; } = [];

    public Dictionary<Guid, InboxGroup> Groups { get; } = [];

    /// <summary>Every item save, in order.</summary>
    public List<Guid> ItemWrites { get; } = [];

    public void Seed(InboxItem item) => Items[item.Id] = item;

    public void Seed(InboxList list) => Lists[list.Id] = list;

    public void Seed(InboxGroup group) => Groups[group.Id] = group;

    public Task SaveAsync(InboxItem item, CancellationToken cancellationToken = default)
    {
        ItemWrites.Add(item.Id);
        Items[item.Id] = item;
        return Task.CompletedTask;
    }

    public Task<InboxItem?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.GetValueOrDefault(id));

    /// <summary>Newest capture first, every status — the order and the scope the
    /// real store promises.</summary>
    public Task<IReadOnlyList<InboxItem>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InboxItem>>([.. Items.Values.OrderByDescending(item => item.CapturedAt)]);

    public Task<IReadOnlyList<InboxItem>> ListPendingReplicaAckAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InboxItem>>([.. Items.Values.Where(item => item.ReplicaAckPending)]);

    public Task<IReadOnlyList<InboxList>> ListListsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InboxList>>(
            [.. Lists.Values.OrderBy(list => list.Order).ThenBy(list => list.Name, StringComparer.OrdinalIgnoreCase)]);

    public Task<IReadOnlyList<InboxGroup>> ListGroupsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InboxGroup>>(
            [.. Groups.Values.OrderBy(group => group.Order).ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)]);

    public Task SaveListAsync(InboxList list, CancellationToken cancellationToken = default)
    {
        Lists[list.Id] = list;
        return Task.CompletedTask;
    }

    public Task DeleteListAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Lists.Remove(id);
        return Task.CompletedTask;
    }

    public Task SaveGroupAsync(InboxGroup group, CancellationToken cancellationToken = default)
    {
        Groups[group.Id] = group;
        return Task.CompletedTask;
    }

    public Task DeleteGroupAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Groups.Remove(id);
        return Task.CompletedTask;
    }
}
