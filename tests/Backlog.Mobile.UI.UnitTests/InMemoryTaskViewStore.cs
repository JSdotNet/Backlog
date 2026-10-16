using Backlog.Mobile.UI.Tasks;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>A task view that forgets on dispose. <see cref="SqliteTaskViewStore"/>
/// has its own round-trip tests; the fold and the screen only need something that
/// keeps what it is given.</summary>
internal sealed class InMemoryTaskViewStore(string? cursor = null, params TaskViewRow[] rows) : ITaskViewStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, TaskViewRow> _rows = rows.ToDictionary(row => row.Id);
    private readonly List<(string? Cursor, MyDayPreferencesDocument? Preferences)> _saves = [];
    private string? _cursor = cursor;
    private MyDayPreferencesDocument? _preferences;

    /// <summary>The preferences a test starts the store with, as a phone restarted
    /// over a file that already keeps some.</summary>
    public MyDayPreferencesDocument? Preferences
    {
        init => _preferences = value;
    }

    /// <summary>Every save's cursor and preferences, in order — what a test reads to
    /// see the preferences were kept with the page that carried them.</summary>
    public IReadOnlyList<(string? Cursor, MyDayPreferencesDocument? Preferences)> Saves
    {
        get
        {
            lock (_lock) return [.. _saves];
        }
    }

    public IReadOnlyList<TaskViewRow> ReadRows()
    {
        lock (_lock) return [.. _rows.Values];
    }

    public string? ReadCursor()
    {
        lock (_lock) return _cursor;
    }

    public MyDayPreferencesDocument? ReadMyDayPreferences()
    {
        lock (_lock) return _preferences;
    }

    public Task SaveAsync(IReadOnlyList<TaskViewRow> rows, string? cursor = null, CancellationToken cancellationToken = default) =>
        SaveAsync(rows, cursor, preferences: null, cancellationToken);

    public Task SaveAsync(IReadOnlyList<TaskViewRow> rows, string? cursor, MyDayPreferencesDocument? preferences, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            foreach (var row in rows) _rows[row.Id] = row;
            if (cursor is not null) _cursor = cursor;
            if (preferences is not null) _preferences = preferences;
            _saves.Add((cursor, preferences));
        }

        return Task.CompletedTask;
    }

    public Task ResetCursorAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock) _cursor = null;
        return Task.CompletedTask;
    }
}
