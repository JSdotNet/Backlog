using System.Text.Json;
using Backlog.Modules.Backlog;
using Backlog.Infrastructure.FileSystem;

namespace Backlog.Desktop.UI.Services;

/// <summary>
/// Owns where the backlog actually lives on disk, and hands out a repository
/// pointed at it.
/// <para>
/// The setting itself is deliberately <em>not</em> stored in the backlog folder —
/// it is kept in a fixed per-user location, because a pointer that moves with
/// the thing it points at is no pointer at all. Move the store to a synced
/// folder and this app still knows where you sent it.
/// </para>
/// </summary>
public sealed class BacklogStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _settingsPath;

    public BacklogStore() : this(null)
    {
    }

    public BacklogStore(string? appDataDirectory)
    {
        var appData = string.IsNullOrWhiteSpace(appDataDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Backlog")
            : appDataDirectory;
        Directory.CreateDirectory(appData);

        _settingsPath = Path.Combine(appData, "settings.json");
        DefaultRootDirectory = appData;

        RootDirectory = ReadSavedRoot() ?? DefaultRootDirectory;
        Repository = new FileBacklogRepository(RootDirectory);
    }

    /// <summary>Raised after the store moves, so open views can reload.</summary>
    public event Action? RootChanged;

    /// <summary>Where the backlog lives when nothing has been configured.</summary>
    public string DefaultRootDirectory { get; }

    public string RootDirectory { get; private set; }

    public IBacklogRepository Repository { get; private set; }

    public bool IsDefaultRoot =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(RootDirectory),
            Path.TrimEndingDirectorySeparator(DefaultRootDirectory),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Where the entry markdown files themselves are written — shown on
    /// the settings page so the folder can be found in a file manager.</summary>
    public string EntriesDirectory => Path.Combine(RootDirectory, "entries");

    /// <summary>Points the app at a different folder. Returns an error message
    /// when the folder cannot be used, rather than throwing — a bad path typed
    /// into a settings field is an ordinary thing to do, not an exception.</summary>
    public string? TryUseRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Enter a folder path.";

        var trimmed = path.Trim();

        // Resolving a relative path against whatever the working directory
        // happens to be would quietly put someone's backlog somewhere they
        // never named. Ask for the whole path instead.
        if (!Path.IsPathRooted(trimmed)) return "Use a full path, such as D:\\Notes\\Backlog.";

        string full;
        try
        {
            full = Path.GetFullPath(trimmed);
        }
        catch (Exception)
        {
            return "That doesn't look like a valid folder path.";
        }

        if (!Path.IsPathFullyQualified(full)) return "Use a full path, such as D:\\Notes\\Backlog.";

        try
        {
            Directory.CreateDirectory(Path.Combine(full, "entries"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return $"Couldn't use that folder: {ex.Message}";
        }

        if (string.Equals(Path.TrimEndingDirectorySeparator(full),
                Path.TrimEndingDirectorySeparator(RootDirectory),
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        RootDirectory = full;
        Repository = new FileBacklogRepository(full);

        try
        {
            var json = JsonSerializer.Serialize(new StoreSettings { RootDirectory = full }, JsonOptions);
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The move itself succeeded; only remembering it failed. Say so
            // rather than silently reverting to the old folder next launch.
            RootChanged?.Invoke();
            return "Moved, but the choice couldn't be saved for next time.";
        }

        RootChanged?.Invoke();
        return null;
    }

    /// <summary>Returns the app to its default per-user folder.</summary>
    public string? ResetToDefault() => TryUseRoot(DefaultRootDirectory);

    private string? ReadSavedRoot()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return null;

            var settings = JsonSerializer.Deserialize<StoreSettings>(File.ReadAllText(_settingsPath), JsonOptions);
            var root = settings?.RootDirectory;
            if (string.IsNullOrWhiteSpace(root)) return null;

            Directory.CreateDirectory(Path.Combine(root, "entries"));
            return root;
        }
        catch (Exception)
        {
            // A corrupt or unreachable setting must never stop the app from
            // opening — fall back to the default folder.
            return null;
        }
    }

    private sealed class StoreSettings
    {
        public string? RootDirectory { get; set; }
    }
}
