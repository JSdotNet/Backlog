using System.Text.Json;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Remembers what the shell was showing, so it reopens there instead of always
/// defaulting to the workspace panes.
/// <para>
/// Its own file beside the app's other per-user settings, for the same reason
/// <see cref="BacklogRefreshSettingsStore"/> has one: what is currently on
/// screen changes far more often than the workspace pointer <c>settings.json</c>
/// holds.
/// </para>
/// <para>
/// Holds the surface and the pane names as plain strings rather than the
/// shell's own enums, which are internal to the desktop UI project and not
/// visible from here — the shell is the one that knows what the names mean
/// and is the only reader of them.
/// </para>
/// <para>
/// One file for both, not two, because a fresh shell instance restores them
/// together: which takeover was open and which panes were showing underneath
/// it are both "what the reader was looking at", read back in the same
/// <c>OnInitializedAsync</c>.
/// </para>
/// </summary>
public sealed class ShellNavigationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly string[] Empty = [];

    private readonly string _path;

    public ShellNavigationStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Backlog",
                "shell-navigation.json"))
    {
    }

    /// <summary>Names the settings file separately from the per-user location.
    /// Public rather than internal because it is the only way to give a test —
    /// or a session running beside another — a store that does not fight over
    /// the real per-user file.</summary>
    public ShellNavigationStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var dto = Read();
        LastSurface = dto?.LastSurface;
        LastEnabledPanes = dto?.LastEnabledPanes ?? Empty;
        LastPinnedPanes = dto?.LastPinnedPanes ?? Empty;
    }

    /// <summary>Raised after anything remembered here changes, so nothing has
    /// to poll the file to notice.</summary>
    public event Action? Changed;

    /// <summary>The surface that was last set, or null when nothing has been
    /// remembered yet — first launch included.</summary>
    public string? LastSurface { get; private set; }

    /// <summary>The panes that were enabled when last set. Empty means nothing
    /// has been remembered yet, not that every pane was closed — the shell
    /// never allows that state to begin with.</summary>
    public IReadOnlyList<string> LastEnabledPanes { get; private set; }

    /// <summary>The subset of <see cref="LastEnabledPanes"/> that were pinned.</summary>
    public IReadOnlyList<string> LastPinnedPanes { get; private set; }

    /// <summary>Where the choices are written.</summary>
    public string SettingsPath => _path;

    public void SetLastSurface(string? surface)
    {
        if (surface == LastSurface) return;

        LastSurface = surface;
        Save();
    }

    public void SetLastPanes(IReadOnlyList<string> enabled, IReadOnlyList<string> pinned)
    {
        if (enabled.SequenceEqual(LastEnabledPanes) && pinned.SequenceEqual(LastPinnedPanes)) return;

        LastEnabledPanes = [.. enabled];
        LastPinnedPanes = [.. pinned];
        Save();
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(new ShellNavigationDto
            {
                LastSurface = LastSurface,
                LastEnabledPanes = [.. LastEnabledPanes],
                LastPinnedPanes = [.. LastPinnedPanes]
            }, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing this write only costs the next launch its restore; what
            // is on screen right now is unaffected.
        }

        Changed?.Invoke();
    }

    private ShellNavigationDto? Read()
    {
        try
        {
            if (!File.Exists(_path)) return null;

            return JsonSerializer.Deserialize<ShellNavigationDto>(File.ReadAllText(_path), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt or unreachable file must never stop the app from
            // opening — fall back to nothing remembered.
            return null;
        }
    }

    private sealed class ShellNavigationDto
    {
        public string? LastSurface { get; init; }

        public string[]? LastEnabledPanes { get; init; }

        public string[]? LastPinnedPanes { get; init; }
    }
}
