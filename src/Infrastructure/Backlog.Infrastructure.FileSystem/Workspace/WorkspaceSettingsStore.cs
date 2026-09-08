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

    private readonly Func<string, SyncedFolderMatch?> _detectSyncedFolder;

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
        : this(appData, settingsPath, new SyncedFolderDetector().Detect)
    {
    }

    /// <summary>Names the sync-folder probe as well, for the same reason as
    /// above: whether the machine running a test has OneDrive signed in is not
    /// something a test may depend on. Production takes
    /// <see cref="SyncedFolderDetector"/>, which reads this machine.</summary>
    public WorkspaceSettingsStore(
        string appData,
        string settingsPath,
        Func<string, SyncedFolderMatch?> detectSyncedFolder)
    {
        Directory.CreateDirectory(appData);

        _detectSyncedFolder = detectSyncedFolder;
        _settingsPath = settingsPath;
        DefaultRootDirectory = appData;

        var settings = ReadSettings();
        RootDirectory = settings?.RootDirectory ?? DefaultRootDirectory;
        RootRepository = settings?.RootRepository?.ToRepository();
        KnowledgeFolders = KnowledgeFolderSetting.Normalize(
            settings?.KnowledgeFolders?.Select(folder => folder.ToSetting()).OfType<KnowledgeFolderSetting>() ?? []);

        DefaultKnowledgeCacheDirectory = Path.Combine(appData, KnowledgeCacheFolderName);
        KnowledgeCacheDirectory = Clean(settings?.KnowledgeCacheDirectory) ?? DefaultKnowledgeCacheDirectory;

        ActivityCacheDirectory = Path.Combine(appData, ActivityCacheFolderName);

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

        // Construction is what "at startup" means for a store both app heads
        // register as a singleton, so the root a returning user already has is
        // looked at here rather than only when they next change it.
        RefreshSyncedRoot();
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

    /// <summary>
    /// The file-sync provider's folder <see cref="RootDirectory"/> turned out to
    /// sit inside, or null when it sits outside every one this app recognises.
    /// <para>
    /// Read by the Storage settings screen, which is the only place that can act
    /// on it: the app warns and the root stays the user's choice. R9 in
    /// <c>.arc42/11-risks-and-technical-debt.md</c> is the loss this exists for
    /// and local ADR 0005's <c>### The database filename</c> is where the check
    /// was preferred to a per-device database name. Detection is heuristic, so
    /// null means "nothing recognised" rather than "not synced" — see
    /// <see cref="SyncedFolderDetector"/>.
    /// </para>
    /// </summary>
    public SyncedFolderMatch? SyncedRoot { get; private set; }

    /// <summary>Optional GitHub repository metadata for backing up the storage
    /// folder later. The folder remains the source of truth today.</summary>
    public GitHubRepositoryRef? RootRepository { get; private set; }

    /// <summary>Knowledge folders resolved against the storage root when no repository scope is active.</summary>
    public IReadOnlyList<KnowledgeFolderSetting> KnowledgeFolders { get; private set; }

    /// <summary>The folder branch snapshots are kept in when nothing overrides
    /// it: one beside the per-user settings, never inside the backlog.</summary>
    public string DefaultKnowledgeCacheDirectory { get; }

    /// <summary>
    /// Where knowledge fetched from a repository branch is cached.
    /// <para>
    /// Configurable, and beside the per-user settings by default rather than
    /// inside the backlog folder, because a snapshot is neither the workspace's
    /// content nor anything anybody should back up: it is a disposable copy of a
    /// commit that can always be fetched again. Somebody who keeps their backlog
    /// on a synced drive should not find every registered repository's tree
    /// syncing with it.
    /// </para>
    /// <para>
    /// It is a setting rather than a constant because a machine with a small
    /// system drive and a large one for work is an ordinary machine, and a
    /// repository tree per registered repository is the kind of thing people
    /// want somewhere they chose.
    /// </para>
    /// </summary>
    public string KnowledgeCacheDirectory { get; private set; }

    /// <summary>Whether snapshots are still going to the folder beside the
    /// per-user settings. The settings screen shows the field empty when they
    /// are, so the placeholder does the explaining rather than a path somebody
    /// never typed.</summary>
    public bool IsDefaultKnowledgeCacheDirectory =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(KnowledgeCacheDirectory),
            Path.TrimEndingDirectorySeparator(DefaultKnowledgeCacheDirectory),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Where the dashboard's merged-pull-request detail is kept.
    /// <para>
    /// Beside the per-user settings, and deliberately <em>not</em> under the
    /// backlog root. <c>FileTaskSyncStateStore</c> spells out why at length: the
    /// syncing of that root is the hazard ADR 0005 exists to remove, and a folder
    /// of thousands of tiny per-pull-request files landing in somebody's synced
    /// drive is exactly the shape of thing that made it a hazard. Nothing in here
    /// is workspace content either — it is a copy of GitHub's own answers about
    /// commits that cannot change, and can always be fetched again.
    /// </para>
    /// <para>
    /// Not configurable, unlike <see cref="KnowledgeCacheDirectory"/>. That one is
    /// a setting because a repository tree per registered repository is large
    /// enough that somebody with a small system drive needs a say; this is
    /// kilobytes, and a second path field on the settings screen would cost more
    /// attention than it saves disk.
    /// </para>
    /// </summary>
    public string ActivityCacheDirectory { get; }

    private const string KnowledgeCacheFolderName = "knowledge-cache";

    private const string ActivityCacheFolderName = "activity-cache";

    private static string? Clean(string? path) => string.IsNullOrWhiteSpace(path) ? null : path.Trim();

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
        RefreshSyncedRoot();

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

    /// <summary>Asks the probe about the root the store is now pointing at.
    /// <para>
    /// Catches everything, and deliberately: a warning nobody asked for must
    /// never be the reason the app cannot open Settings, or cannot move the
    /// backlog off the very folder it would have warned about. The detector fails
    /// open on its own account; this is the guarantee it holds even when the
    /// probe is somebody else's.
    /// </para></summary>
    private void RefreshSyncedRoot()
    {
        try
        {
            SyncedRoot = _detectSyncedFolder(RootDirectory);
        }
        catch (Exception)
        {
            SyncedRoot = null;
        }
    }

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

    /// <summary>
    /// Points branch snapshots at a different folder, or — with a blank path —
    /// back at the default one.
    /// <para>
    /// Nothing is moved and nothing is deleted. The old folder's snapshots are
    /// left where they are and the new folder simply starts empty, refilling on
    /// the next fetch. Moving them would be a long file copy behind a settings
    /// field, and deleting them would be this app throwing away a folder
    /// somebody may have pointed at something else entirely.
    /// </para>
    /// </summary>
    public string? SetKnowledgeCacheDirectory(string? path)
    {
        var trimmed = Clean(path);

        if (trimmed is null)
        {
            if (IsDefaultKnowledgeCacheDirectory) return null;

            KnowledgeCacheDirectory = DefaultKnowledgeCacheDirectory;
            var reset = SaveSettings("Snapshot folder reset, but the choice couldn't be saved for next time.");
            if (reset is null) RootChanged?.Invoke();
            return reset;
        }

        // The same demand TryUseRoot makes, for the same reason: a relative path
        // would resolve against whatever the working directory happens to be and
        // put somebody's snapshots somewhere they never named.
        if (!Path.IsPathRooted(trimmed)) return "Use a full path, such as D:\\Backlog\\knowledge-cache.";

        string full;
        try
        {
            full = Path.GetFullPath(trimmed);
        }
        catch (Exception)
        {
            return "That doesn't look like a valid folder path.";
        }

        if (!Path.IsPathFullyQualified(full)) return "Use a full path, such as D:\\Backlog\\knowledge-cache.";

        try
        {
            Directory.CreateDirectory(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return $"Couldn't use that folder: {ex.Message}";
        }

        if (string.Equals(
            Path.TrimEndingDirectorySeparator(full),
            Path.TrimEndingDirectorySeparator(KnowledgeCacheDirectory),
            StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        KnowledgeCacheDirectory = full;

        var error = SaveSettings("Snapshot folder changed, but the choice couldn't be saved for next time.");
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
                KnowledgeFolders = KnowledgeFolders.Select(StoreKnowledgeFolderSettings.From).ToList(),

                // Written as null while it is the default, so a workspace nobody
                // has moved the cache in keeps producing the file it always did.
                KnowledgeCacheDirectory = IsDefaultKnowledgeCacheDirectory ? null : KnowledgeCacheDirectory
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

        /// <summary>Where branch snapshots are cached, or null for the default
        /// folder beside this file. Absent reads as the default, which is what
        /// every settings file written before branch loading existed says.</summary>
        public string? KnowledgeCacheDirectory { get; init; }
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
