using System.Text.Json;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Sqlite;
using Backlog.Modules.Knowledge.Abstractions;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// The workspace's own settings file: where the backlog lives, which GitHub
/// repository backs that folder, and which knowledge folders are configured for
/// it.
/// <para>
/// The setting itself is deliberately <em>not</em> stored in the backlog folder —
/// it is kept in a fixed per-user location, because a pointer that moves with
/// the thing it points at is no pointer at all. Move the store to a synced
/// folder and this app still knows where you sent it.
/// </para>
/// <para>
/// Three settings in one file rather than three files, because they are one
/// decision: this folder, backed by that repository, with these knowledge
/// folders. Only the folder itself is a module port — WorkspaceTaskStore
/// implements the module's store port over this one. The repository and the
/// folder list are named in an adapter type and a Second Brain type that no
/// abstractions project may see, and their only consumer is the desktop settings
/// screen, which takes this adapter directly the way it already takes the GitHub
/// and Claude ones.
/// </para>
/// </summary>
public sealed class WorkspaceSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _settingsPath;

    /// <summary>The per-user AppData folder name used when nothing overrides it.
    /// A Debug build names it differently from a Release install so a developer
    /// running the app — or the desktop web harness Aspire drives for the same
    /// purpose — always lands in a private, isolated workspace instead of quietly
    /// sharing whatever real backlog is configured on that machine.</summary>
#if DEBUG
    public const string DefaultAppDataFolderName = "Backlog.Debug";
#else
    public const string DefaultAppDataFolderName = "Backlog";
#endif

    public WorkspaceSettingsStore()
        : this(null)
    {
    }

    public WorkspaceSettingsStore(string? appDataDirectory)
        : this(
            string.IsNullOrWhiteSpace(appDataDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DefaultAppDataFolderName)
                : appDataDirectory,
            Path.Combine(
                string.IsNullOrWhiteSpace(appDataDirectory)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DefaultAppDataFolderName)
                    : appDataDirectory,
                "settings.json"))
    {
    }

    /// <summary>Names the settings file separately from the folder it describes.
    /// Public rather than internal because it is the only way to give a test — or
    /// a session running beside another — a workspace that does not fight over the
    /// real per-user file.</summary>
    public WorkspaceSettingsStore(string appData, string settingsPath)
    {
        Directory.CreateDirectory(appData);

        _settingsPath = settingsPath;
        DefaultRootDirectory = appData;

        var settings = ReadSettings();
        RootDirectory = settings?.RootDirectory ?? DefaultRootDirectory;
        RootRepository = settings?.RootRepository?.ToRepository();
        KnowledgeFolders = KnowledgeFolderSetting.Normalize(
            settings?.KnowledgeFolders?.Select(folder => folder.ToSetting()).OfType<KnowledgeFolderSetting>() ?? []);

        // The store owns the location, so it is the store that makes sure the
        // location is usable. This used to happen as a side effect of building a
        // repository here; doing it deliberately means a first run still lands in
        // a folder that exists, now that nothing else is constructed.
        try
        {
            EnsureStorageFolders(RootDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // A folder that cannot be prepared is reported when it is actually
            // used; refusing to construct the app over it would leave no way to
            // open Settings and point it somewhere else.
        }
    }

    /// <summary>Raised after the store moves, so open views can reload.</summary>
    public event Action? RootChanged;

    /// <summary>Where the backlog lives when nothing has been configured.</summary>
    public string DefaultRootDirectory { get; }

    /// <summary>Where the backlog lives right now. The repository adapter reads
    /// this per call rather than being handed a path once, so pointing the app at
    /// a different folder takes effect without restarting it — see
    /// <c>RootedFileBacklogRepository</c>.</summary>
    public string RootDirectory { get; private set; }

    /// <summary>Optional GitHub repository metadata for backing up the storage
    /// folder later. The folder remains the source of truth today.</summary>
    public GitHubRepositoryRef? RootRepository { get; private set; }

    /// <summary>Knowledge folders resolved against the storage root when no repository scope is active.</summary>
    public IReadOnlyList<KnowledgeFolderSetting> KnowledgeFolders { get; private set; }

    public bool IsDefaultRoot =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(RootDirectory),
            Path.TrimEndingDirectorySeparator(DefaultRootDirectory),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>The database file the tasks are kept in — shown on the settings
    /// page so it can be found in a file manager, and so it is obvious what to
    /// copy when somebody wants a backup.</summary>
    public string DatabasePath => SqliteTaskRepository.DatabasePathFor(RootDirectory);

    public string InboxDirectory => Path.Combine(RootDirectory, InboxFolderName);

    /// <summary>The folder the Inbox context will capture into. It is prepared
    /// here rather than by the task store, because the store is a database file
    /// now and has no folders of its own to make.</summary>
    private const string InboxFolderName = "_inbox";

    /// <summary>Makes a chosen root usable: the folder itself, and the inbox
    /// folder inside it. The task database creates itself on first use, so there
    /// is nothing to prepare for it here.</summary>
    private static void EnsureStorageFolders(string rootDir)
    {
        Directory.CreateDirectory(rootDir);
        Directory.CreateDirectory(Path.Combine(rootDir, InboxFolderName));
    }

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
            EnsureStorageFolders(full);
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

    public string? SetKnowledgeFolder(string key, bool enabled, string? path)
    {
        if (string.IsNullOrWhiteSpace(key)) return "Choose a knowledge folder before saving.";

        var folders = KnowledgeFolderSetting.Normalize(KnowledgeFolders).ToList();
        var index = folders.FindIndex(folder => string.Equals(folder.Key, key, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return $"Unknown knowledge folder '{key}'.";

        folders[index] = folders[index] with
        {
            Enabled = enabled,
            Path = string.IsNullOrWhiteSpace(path) ? null : path.Trim()
        };

        KnowledgeFolders = KnowledgeFolderSetting.Normalize(folders);
        var error = SaveSettings("Knowledge folders updated, but the choice couldn't be saved for next time.");
        if (error is null) RootChanged?.Invoke();
        return error;
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
                    },
                KnowledgeFolders = KnowledgeFolders.Select(StoreKnowledgeFolderSettings.From).ToList()
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

            EnsureStorageFolders(root);
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

        public List<StoreKnowledgeFolderSettings>? KnowledgeFolders { get; init; }
    }

    private sealed record StoreKnowledgeFolderSettings
    {
        public string? Key { get; init; }

        public bool Enabled { get; init; } = true;

        public string? Path { get; init; }

        /// <summary>The stored row as a setting, or null when its key names no
        /// knowledge folder — a typo, or a section since retired, as
        /// <c>.backlog</c> now is. Dropping it is what <see cref="KnowledgeFolderSetting.Normalize"/>
        /// would do anyway; saying so here is what keeps a stale file from
        /// stopping the app from opening.</summary>
        public KnowledgeFolderSetting? ToSetting()
        {
            var folder = KnowledgeFolderSetting.Defaults()
                .FirstOrDefault(defaultFolder => string.Equals(defaultFolder.Key, Key, StringComparison.OrdinalIgnoreCase));

            return folder is null
                ? null
                : folder with
                {
                    Enabled = Enabled,
                    Path = Path
                };
        }

        public static StoreKnowledgeFolderSettings From(KnowledgeFolderSetting folder) => new()
        {
            Key = folder.Key,
            Enabled = folder.Enabled,
            Path = folder.Path
        };
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
