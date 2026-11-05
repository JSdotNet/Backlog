using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Remembers, per surface id, where that surface's last popped-out window stood, so the
/// next one opens there (local ADR 0043 §5), whether it was kept on top of other windows,
/// and the popped-out windows a person chose to remember, to reopen on the next start.
/// Nothing else reopens: a window is remembered only once its Remember toggle is pressed.
/// <para>
/// Its own file, <c>shell-windows.json</c>, beside <c>shell-navigation.json</c>: a
/// window being dragged writes far more often than anything the navigation file holds,
/// and the two are read by different owners — the shell's view state and the head's
/// windows.
/// </para>
/// <para>
/// Written off the caller's thread through <see cref="QueuedFileWrites"/>, the same way
/// <see cref="ShellNavigationStore"/> is: a move or a resize is raised on the UI thread,
/// many times a second while a window is dragged, and the writer coalesces them to the
/// newest.
/// </para>
/// </summary>
public sealed class ShellWindowStore
{
    /// <summary>The file's name, beside <c>shell-navigation.json</c>.</summary>
    public const string FileName = "shell-windows.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, ShellWindowBounds> _bounds;
    private readonly List<RememberedShellWindow> _remembered;
    private readonly HashSet<string> _onTop = new(StringComparer.Ordinal);

    /// <summary>The file in <see cref="WorkspaceSettingsStore.DefaultAppDataDirectory"/>,
    /// beside <see cref="ShellNavigationStore.FileName"/>: <c>Backlog.Debug</c> for a
    /// Debug build, <c>Backlog</c> for the installed app.</summary>
    public ShellWindowStore()
        : this(Path.Combine(WorkspaceSettingsStore.DefaultAppDataDirectory, FileName))
    {
    }

    /// <summary>Names the file separately from the per-user location, for a test or a
    /// session running beside another.</summary>
    public ShellWindowStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        (_bounds, _remembered) = Read();
    }

    /// <summary>Where the bounds are written.</summary>
    public string SettingsPath => _path;

    /// <summary>The last popped-out window of <paramref name="surfaceId"/>'s bounds, or
    /// null when none of its windows has been remembered.</summary>
    public ShellWindowBounds? Get(string surfaceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceId);

        lock (_gate)
        {
            return _bounds.GetValueOrDefault(surfaceId);
        }
    }

    /// <summary>Remembers <paramref name="bounds"/> for <paramref name="surfaceId"/> and
    /// returns before the file is written. Bounds with no size are not kept, and bounds
    /// equal to the ones kept write nothing.</summary>
    public void Set(string surfaceId, ShellWindowBounds bounds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceId);
        ArgumentNullException.ThrowIfNull(bounds);
        if (!bounds.HasArea) return;

        lock (_gate)
        {
            if (_bounds.TryGetValue(surfaceId, out var kept) && kept == bounds) return;

            _bounds[surfaceId] = bounds;

            // Queued under the gate, so the snapshots reach the writer in the order
            // the sets were made whichever threads make them.
            QueuedFileWrites.Enqueue(_path, Serialize());
        }
    }

    /// <summary>The windows remembered to reopen on the next start, in the order they were
    /// first remembered.</summary>
    public IReadOnlyList<RememberedShellWindow> Remembered
    {
        get
        {
            lock (_gate)
            {
                return [.. _remembered];
            }
        }
    }

    /// <summary>The remembered window <paramref name="key"/> names, or null when that
    /// window is not remembered.</summary>
    public RememberedShellWindow? GetRemembered(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (_gate)
        {
            return _remembered.Find(window => string.Equals(window.Key, key, StringComparison.Ordinal));
        }
    }

    /// <summary>Remembers <paramref name="window"/>, replacing in place the entry its key
    /// already has, and returns before the file is written. Bounds with no size are kept
    /// as none, and an entry equal to the one kept writes nothing.</summary>
    public void Remember(RememberedShellWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(window.Key);
        ArgumentException.ThrowIfNullOrWhiteSpace(window.SurfaceId);
        ArgumentNullException.ThrowIfNull(window.Query);

        // A copy of the query, so a caller that goes on changing its dictionary does
        // not change what is kept.
        var kept = window with
        {
            Query = new Dictionary<string, string>(window.Query, StringComparer.Ordinal),
            Bounds = window.Bounds is { HasArea: true } bounds ? bounds : null
        };

        lock (_gate)
        {
            var index = _remembered.FindIndex(entry => string.Equals(entry.Key, kept.Key, StringComparison.Ordinal));
            if (index >= 0)
            {
                if (Same(_remembered[index], kept)) return;

                _remembered[index] = kept;
            }
            else
            {
                _remembered.Add(kept);
            }

            QueuedFileWrites.Enqueue(_path, Serialize());
        }
    }

    /// <summary>Forgets the remembered window <paramref name="key"/> names, so it does
    /// not reopen. Nothing is written when it was not remembered.</summary>
    public void Forget(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (_gate)
        {
            if (_remembered.RemoveAll(window => string.Equals(window.Key, key, StringComparison.Ordinal)) == 0) return;

            QueuedFileWrites.Enqueue(_path, Serialize());
        }
    }

    /// <summary>Whether the last popped-out window of <paramref name="surfaceId"/> was
    /// kept on top of other windows, so the next one opens kept on top too.</summary>
    public bool IsOnTop(string surfaceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceId);

        lock (_gate)
        {
            return _onTop.Contains(surfaceId);
        }
    }

    /// <summary>Remembers whether <paramref name="surfaceId"/>'s windows are kept on top,
    /// apart from its bounds, and returns before the file is written. The same value as
    /// kept writes nothing.</summary>
    public void SetOnTop(string surfaceId, bool onTop)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceId);

        lock (_gate)
        {
            var changed = onTop ? _onTop.Add(surfaceId) : _onTop.Remove(surfaceId);
            if (!changed) return;

            QueuedFileWrites.Enqueue(_path, Serialize());
        }
    }

    /// <summary>Waits until every set so far is on disk: for the app closing, or a test
    /// reading the file itself.</summary>
    public void Flush() => QueuedFileWrites.WaitFor(_path);

    /// <summary>By value, query included: the record's own equality compares the query
    /// by reference.</summary>
    private static bool Same(RememberedShellWindow kept, RememberedShellWindow next) =>
        string.Equals(kept.SurfaceId, next.SurfaceId, StringComparison.Ordinal)
        && kept.Bounds == next.Bounds
        && kept.Query.Count == next.Query.Count
        && kept.Query.All(pair => next.Query.TryGetValue(pair.Key, out var value) && string.Equals(value, pair.Value, StringComparison.Ordinal));

    /// <summary>Taken under the gate.</summary>
    private string Serialize() =>
        JsonSerializer.Serialize(
            new ShellWindowsDto
            {
                OnTop = _onTop.Count == 0 ? null : [.. _onTop.Order(StringComparer.Ordinal)],
                Surfaces = _bounds
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(
                        pair => pair.Key,
                        pair => (WindowDto?)ToDto(pair.Value),
                        StringComparer.Ordinal),

                // Left out while no window is remembered, so the file reads as the
                // builds before remembered windows wrote it.
                Remembered = _remembered.Count == 0
                    ? null
                    : [.. _remembered.Select(window => (RememberedDto?)new RememberedDto
                    {
                        Key = window.Key,
                        SurfaceId = window.SurfaceId,
                        Query = window.Query.Count == 0
                            ? null
                            : window.Query
                                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                                .ToDictionary(pair => pair.Key, pair => (string?)pair.Value, StringComparer.Ordinal),
                        Bounds = window.Bounds is { } bounds ? ToDto(bounds) : null
                    })]
            },
            JsonOptions);

    private static WindowDto ToDto(ShellWindowBounds bounds) => new()
    {
        X = bounds.X,
        Y = bounds.Y,
        Width = bounds.Width,
        Height = bounds.Height,
        Maximized = bounds.Maximized
    };

    /// <summary>The bounds <paramref name="window"/> holds, or null for none or for
    /// bounds with no size.</summary>
    private static ShellWindowBounds? FromDto(WindowDto? window)
    {
        if (window is null) return null;

        var read = new ShellWindowBounds(window.X, window.Y, window.Width, window.Height, window.Maximized);
        return read.HasArea ? read : null;
    }

    private (Dictionary<string, ShellWindowBounds> Bounds, List<RememberedShellWindow> Remembered) Read()
    {
        // A store on a path another store in this process has just written reads
        // what that one wrote, not the file as it stood before.
        QueuedFileWrites.WaitFor(_path);

        var bounds = new Dictionary<string, ShellWindowBounds>(StringComparer.Ordinal);
        var remembered = new List<RememberedShellWindow>();
        try
        {
            if (!File.Exists(_path)) return (bounds, remembered);

            var dto = JsonSerializer.Deserialize<ShellWindowsDto>(File.ReadAllText(_path), JsonOptions);
            foreach (var (surfaceId, window) in dto?.Surfaces ?? [])
            {
                if (string.IsNullOrWhiteSpace(surfaceId) || FromDto(window) is not { } read) continue;

                bounds[surfaceId] = read;
            }

            foreach (var window in dto?.Remembered ?? [])
            {
                // An entry that names no window or no surface could never reopen;
                // a key given twice keeps its first entry.
                if (window is null
                    || string.IsNullOrWhiteSpace(window.Key)
                    || string.IsNullOrWhiteSpace(window.SurfaceId)
                    || remembered.Exists(entry => string.Equals(entry.Key, window.Key, StringComparison.Ordinal)))
                {
                    continue;
                }

                remembered.Add(new RememberedShellWindow(
                    window.Key,
                    window.SurfaceId,
                    // A value stored as null has no place in a window's address, and
                    // escaping it there would throw as the window reopens.
                    (window.Query ?? [])
                        .Where(pair => pair.Value is not null)
                        .ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.Ordinal),
                    FromDto(window.Bounds)));
            }

            foreach (var surfaceId in dto?.OnTop ?? [])
            {
                if (!string.IsNullOrWhiteSpace(surfaceId)) _onTop.Add(surfaceId);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt or unreachable file must never stop a window from opening —
            // it opens where the system puts a new one, and nothing extra reopens.
            bounds.Clear();
            remembered.Clear();
            _onTop.Clear();
        }

        return (bounds, remembered);
    }

    /// <summary>The file: <c>{ "surfaces": { "devbook": { "x": …, … } }, "onTop": [ "tasks" ],
    /// "remembered": [ { "key": …, "surfaceId": …, "query": { … }, "bounds": { "x": …, … } } ] }</c>.
    /// Every key is a stored name: never rename one without reading the old.</summary>
    private sealed class ShellWindowsDto
    {
        public Dictionary<string, WindowDto?>? Surfaces { get; init; }

        /// <summary>The surfaces whose windows are kept on top of other windows.
        /// Absent in a file written before the pin existed, and when none is.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string?[]? OnTop { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<RememberedDto?>? Remembered { get; init; }
    }

    private sealed class RememberedDto
    {
        public string? Key { get; init; }

        public string? SurfaceId { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string?>? Query { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public WindowDto? Bounds { get; init; }
    }

    private sealed class WindowDto
    {
        public int X { get; init; }

        public int Y { get; init; }

        public int Width { get; init; }

        public int Height { get; init; }

        public bool Maximized { get; init; }
    }
}
