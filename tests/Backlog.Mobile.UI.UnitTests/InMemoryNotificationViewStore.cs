using Backlog.Mobile.UI.Notifications;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>A notification view that forgets on dispose; the SQLite store has its own
/// round-trip test.</summary>
internal sealed class InMemoryNotificationViewStore : INotificationViewStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, NotificationChangeRecord> _rows = [];
    private readonly List<NotificationReplyPost> _sent = [];
    private string? _cursor;

    public IReadOnlyList<NotificationChangeRecord> ReadRows()
    {
        lock (_lock) return [.. _rows.Values];
    }

    public string? ReadCursor()
    {
        lock (_lock) return _cursor;
    }

    public Task SaveAsync(IReadOnlyList<NotificationChangeRecord> rows, string? cursor = null, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            foreach (var row in rows) _rows[row.Change.Id] = row;
            if (cursor is not null) _cursor = cursor;
        }

        return Task.CompletedTask;
    }

    public Task ResetCursorAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock) _cursor = null;
        return Task.CompletedTask;
    }

    public IReadOnlyList<NotificationReplyPost> ReadSentReplies()
    {
        lock (_lock) return [.. _sent];
    }

    public Task AddSentReplyAsync(NotificationReplyPost reply, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_sent.All(held => held.Id != reply.Id)) _sent.Add(reply);
        }

        return Task.CompletedTask;
    }
}
