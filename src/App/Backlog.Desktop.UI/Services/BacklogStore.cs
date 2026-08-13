using System.Text.Json;
using Backlog.Modules.Backlog;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.GitHub;

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

    public BacklogStore()
        : this(null)
    {
    }

    public BacklogStore(string? appDataDirectory)
        : this(
            string.IsNullOrWhiteSpace(appDataDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Backlog")
                : appDataDirectory,
            Path.Combine(
                string.IsNullOrWhiteSpace(appDataDirectory)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Backlog")
                    : appDataDirectory,
                "settings.json"))
    {
    }

    internal BacklogStore(string appData, string settingsPath)
    {
        Directory.CreateDirectory(appData);

        _settingsPath = settingsPath;
        DefaultRootDirectory = appData;

        var settings = ReadSettings();
        RootDirectory = settings?.RootDirectory ?? DefaultRootDirectory;
        RootRepository = settings?.RootRepository?.ToRepository();
        Repository = new FileBacklogRepository(RootDirectory);
    }

    /// <summary>Raised after the store moves, so open views can reload.</summary>
    public event Action? RootChanged;

    /// <summary>Where the backlog lives when nothing has been configured.</summary>
    public string DefaultRootDirectory { get; }

    public string RootDirectory { get; private set; }

    public IBacklogRepository Repository { get; private set; }

    /// <summary>Optional GitHub repository metadata for backing up the storage
    /// folder later. The folder remains the source of truth today.</summary>
    public GitHubRepositoryRef? RootRepository { get; private set; }

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

        var saveError = SaveSettings("Moved, but the choice couldn't be saved for next time.");
        if (saveError is not null)
        {
            // The move itself succeeded; only remembering it failed. Say so
            // rather than silently reverting to the old folder next launch.
            RootChanged?.Invoke();
            return saveError;
        }

        RootChanged?.Invoke();
        return null;
    }

    /// <summary>Returns the app to its default per-user folder.</summary>
    public string? ResetToDefault() => TryUseRoot(DefaultRootDirectory);

    public string? TrySetRepository(string? repoText)
    {
        var repository = GitHubRepositoryRef.TryParse(repoText, out var error);
        if (repository is null)
        {
            return error ?? "Enter a GitHub repository as owner/repo, or clear it.";
        }

        RootRepository = repository;
        return SaveSettings("Repository configured, but the choice couldn't be saved for next time.");
    }

    public string? ClearRepository()
    {
        if (RootRepository is null) return null;

        RootRepository = null;
        return SaveSettings("Repository cleared, but the choice couldn't be saved for next time.");
    }

    private string? SaveSettings(string saveFailureMessage)
    {
        try
        {
            var settings = new StoreSettings
            {
                RootDirectory = RootDirectory,
                RootRepository = RootRepository is null
                    ? null
                    : new StoreRepositorySettings
                    {
                        Alias = RootRepository.Alias,
                        Owner = RootRepository.Owner,
                        Name = RootRepository.Name
                    }
            };
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return saveFailureMessage;
        }
    }

    private StoreSettings? ReadSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return null;

            var settings = JsonSerializer.Deserialize<StoreSettings>(File.ReadAllText(_settingsPath), JsonOptions);
            if (settings is null) return null;

            var root = settings.RootDirectory;
            if (string.IsNullOrWhiteSpace(root)) return settings with { RootDirectory = null };

            Directory.CreateDirectory(Path.Combine(root, "entries"));
            return settings with { RootDirectory = root };
        }
        catch (Exception)
        {
            // A corrupt or unreachable setting must never stop the app from
            // opening — fall back to the default folder.
            return null;
        }
    }

    private sealed record StoreSettings
    {
        public string? RootDirectory { get; init; }

        public StoreRepositorySettings? RootRepository { get; init; }
    }

    private sealed record StoreRepositorySettings
    {
        public string? Alias { get; init; }

        public string? Owner { get; init; }

        public string? Name { get; init; }

        public GitHubRepositoryRef? ToRepository()
        {
            if (string.IsNullOrWhiteSpace(Owner) || string.IsNullOrWhiteSpace(Name))
            {
                return null;
            }

            var alias = string.IsNullOrWhiteSpace(Alias) ? Name : Alias;
            return new GitHubRepositoryRef(GitHubRepositoryRef.NormalizeAlias(alias), Owner, Name);
        }
    }
}
