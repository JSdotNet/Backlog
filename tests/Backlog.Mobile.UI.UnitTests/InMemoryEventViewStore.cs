using Backlog.Mobile.UI.Events;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>An event view that forgets on dispose; the SQLite store has its own round-trip
/// test.</summary>
internal sealed class InMemoryEventViewStore : IEventViewStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, KnowledgeEventChangeRecord> _rows = [];
    private string? _cursor;

    public IReadOnlyList<KnowledgeEventChangeRecord> ReadRows()
    {
        lock (_lock) return [.. _rows.Values];
    }

    public string? ReadCursor()
    {
        lock (_lock) return _cursor;
    }

    public Task SaveAsync(IReadOnlyList<KnowledgeEventChangeRecord> rows, string? cursor = null, CancellationToken cancellationToken = default)
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
}
