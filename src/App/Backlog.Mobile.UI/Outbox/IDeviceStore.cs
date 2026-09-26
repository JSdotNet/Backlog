using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Outbox;

/// <summary>
/// What the phone keeps on itself: the outbox, and the last inbox it was able
/// to pull. One store and one file, because both answer the same question — what
/// to show when the service cannot be reached.
/// </summary>
public interface IDeviceStore
{
    /// <summary>Every entry, oldest first — the order they are sent in.</summary>
    IReadOnlyList<OutboxEntry> ReadOutbox();

    Task AddAsync(OutboxEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Writes the entry's attempts and last error.</summary>
    Task UpdateAsync(OutboxEntry entry, CancellationToken cancellationToken = default);

    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The last inbox the service answered with, and when — or null
    /// when it never has on this device.</summary>
    CachedInbox? ReadInbox();

    Task SaveInboxAsync(CachedInbox inbox, CancellationToken cancellationToken = default);
}

/// <summary>An inbox as it was pulled, and when.</summary>
public sealed record CachedInbox(IReadOnlyList<InboxItem> Items, DateTimeOffset PulledAt);
