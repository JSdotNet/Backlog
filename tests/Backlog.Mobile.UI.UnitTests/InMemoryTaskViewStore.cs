using Backlog.Mobile.UI.Tasks;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>A task view that forgets on dispose. <see cref="SqliteTaskViewStore"/>
/// has its own round-trip tests; the fold and the screen only need something that
/// keeps what it is given.</summary>
internal sealed class InMemoryTaskViewStore(string? cursor = null, params TaskViewRow[] rows) : ITaskViewStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, TaskViewRow> _rows = rows.ToDictionary(row => row.Id);
    private string? _cursor = cursor;

    public IReadOnlyList<TaskViewRow> ReadRows()
    {
        lock (_lock) return [.. _rows.Values];
    }

    public string? ReadCursor()
    {
        lock (_lock) return _cursor;
    }

    public Task SaveAsync(IReadOnlyList<TaskViewRow> rows, string? cursor = null, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            foreach (var row in rows) _rows[row.Id] = row;
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
