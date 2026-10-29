using System.Text.Json;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Remembers, per surface id, where that surface's last popped-out window stood, so the
/// next one opens there (local ADR 0040 §5). Only the bounds: no window reopens at
/// start, and nothing here says which windows were open.
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

    public ShellWindowStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Backlog",
                FileName))
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

        _bounds = Read();
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

    /// <summary>Waits until every set so far is on disk: for the app closing, or a test
    /// reading the file itself.</summary>
    public void Flush() => QueuedFileWrites.WaitFor(_path);

    /// <summary>Taken under the gate.</summary>
    private string Serialize() =>
        JsonSerializer.Serialize(
            new ShellWindowsDto
            {
                Surfaces = _bounds
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(
                        pair => pair.Key,
                        pair => (WindowDto?)new WindowDto
                        {
                            X = pair.Value.X,
                            Y = pair.Value.Y,
                            Width = pair.Value.Width,
                            Height = pair.Value.Height,
                            Maximized = pair.Value.Maximized
                        },
                        StringComparer.Ordinal)
            },
            JsonOptions);

    private Dictionary<string, ShellWindowBounds> Read()
    {
        // A store on a path another store in this process has just written reads
        // what that one wrote, not the file as it stood before.
        QueuedFileWrites.WaitFor(_path);

        var bounds = new Dictionary<string, ShellWindowBounds>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(_path)) return bounds;

            var dto = JsonSerializer.Deserialize<ShellWindowsDto>(File.ReadAllText(_path), JsonOptions);
            foreach (var (surfaceId, window) in dto?.Surfaces ?? [])
            {
                if (string.IsNullOrWhiteSpace(surfaceId) || window is null) continue;

                var read = new ShellWindowBounds(window.X, window.Y, window.Width, window.Height, window.Maximized);
                if (read.HasArea) bounds[surfaceId] = read;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt or unreachable file must never stop a window from opening —
            // it opens where the system puts a new one.
            bounds.Clear();
        }

        return bounds;
    }

    /// <summary>The file: <c>{ "surfaces": { "devbook": { "x": …, … } } }</c>. Every key
    /// is a stored name: never rename one without reading the old.</summary>
    private sealed class ShellWindowsDto
    {
        public Dictionary<string, WindowDto?>? Surfaces { get; init; }
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
