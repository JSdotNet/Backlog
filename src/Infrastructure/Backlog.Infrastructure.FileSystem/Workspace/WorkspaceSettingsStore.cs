using System.Text.Json;
using System.Text.Json.Serialization;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Sqlite;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.DevPc.Abstractions;
using Microsoft.Data.Sqlite;

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
/// folder list are named in an adapter type and a Devbook type that no
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
        // The legacy names are consulted only when the current one is absent, so a
        // file written before the context was renamed reads as the same choices
        // and the next save carries them under the current names only.
        DevbookFolders = DevbookFolderSetting.Normalize(
            (settings?.DevbookFolders ?? settings?.KnowledgeFolders)?.Select(folder => folder.ToSetting()).OfType<DevbookFolderSetting>() ?? []);

        DefaultDevbookCacheDirectory = ResolveDefaultDevbookCacheDirectory(appData);
        DevbookCacheDirectory = Clean(settings?.DevbookCacheDirectory) ?? Clean(settings?.KnowledgeCacheDirectory) ?? DefaultDevbookCacheDirectory;

        ActivityCacheDirectory = Path.Combine(appData, ActivityCacheFolderName);
        SessionActivityCacheDirectory = Path.Combine(appData, SessionActivityCacheFolderName);

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

    /// <summary>Devbook folders resolved against the storage root when no repository scope is active.</summary>
    public IReadOnlyList<DevbookFolderSetting> DevbookFolders { get; private set; }

    /// <summary>The folder branch snapshots are kept in when nothing overrides
    /// it: one beside the per-user settings, never inside the backlog.</summary>
    public string DefaultDevbookCacheDirectory { get; }

    /// <summary>
    /// Where the devbook fetched from a repository branch is cached.
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
    public string DevbookCacheDirectory { get; private set; }

    /// <summary>Whether snapshots are still going to the folder beside the
    /// per-user settings. The settings screen shows the field empty when they
    /// are, so the placeholder does the explaining rather than a path somebody
    /// never typed.</summary>
    public bool IsDefaultDevbookCacheDirectory =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(DevbookCacheDirectory),
            Path.TrimEndingDirectorySeparator(DefaultDevbookCacheDirectory),
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
    /// Not configurable, unlike <see cref="DevbookCacheDirectory"/>. That one is
    /// a setting because a repository tree per registered repository is large
    /// enough that somebody with a small system drive needs a say; this is
    /// kilobytes, and a second path field on the settings screen would cost more
    /// attention than it saves disk.
    /// </para>
    /// </summary>
    public string ActivityCacheDirectory { get; }

    /// <summary>
    /// Where the parsed activity of an agent transcript is kept.
    /// <para>
    /// Beside the per-user settings and never under the backlog root, for the reason
    /// <see cref="ActivityCacheDirectory"/> gives and one more that is specific to this
    /// folder: what is cached here is a parse of files that only exist on <em>this</em>
    /// machine, keyed on those files' own paths and write times. Synced to another
    /// device it would be a folder of entries nothing can ever match — ADR 0005's
    /// hazard in its purest form, since the content is not merely disposable but
    /// meaningless anywhere else.
    /// </para>
    /// <para>
    /// Separate from <see cref="ActivityCacheDirectory"/> rather than shared with it.
    /// The two hold unrelated things — GitHub's answers about pull requests, and this
    /// machine's own transcripts — and one folder would make "forget the activity
    /// cache" an action that threw away whichever of the two the person did not mean.
    /// </para>
    /// </summary>
    public string SessionActivityCacheDirectory { get; }

    private const string DevbookCacheFolderName = "devbook-cache";

    private const string ActivityCacheFolderName = "activity-cache";

    private const string SessionActivityCacheFolderName = "session-activity-cache";

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

    /// <summary>The folders in the root that are the app's, beside the
    /// database: the inbox folder, the shared repository registry, and the
    /// tools catalog when it was put here. This list is what a move carries
    /// and the whole of what it carries. The root used to hold one markdown
    /// file per entry, and a root that old still has that person's own
    /// notes, folders and images beside the database; copying the folder
    /// wholesale took all of that to the new place too, which is not what
    /// "move the backlog" means to the person pressing the button. A folder
    /// the app wrote that is not named here is one it no longer reads —
    /// <c>_backlog</c>, <c>_roadmap</c> — and stays behind for the same reason.</summary>
    internal static readonly string[] OwnedRootFolders =
    [
        InboxFolderName,
        GitHubSettingsStore.RegistryFolderName,
        DevToolConfigurationPaths.ToolFolderName,
    ];

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
    /// into a settings field is an ordinary thing to do, not an exception.
    /// <para>
    /// Only the pointer moves: whatever backlog the new folder already holds is
    /// what the app reads next, and the one in the old folder stays where it
    /// is. That is the right thing for opening a backlog that already lives
    /// somewhere else, and the wrong thing for taking this one along — which
    /// is what <see cref="TryMoveRoot"/> does.
    /// </para></summary>
    public string? TryUseRoot(string? path)
    {
        var error = ResolveRoot(path, out var full);
        if (error is not null) return error;

        return IsCurrentRoot(full) ? null : PointAt(full);
    }

    /// <summary>Takes the backlog along to a different folder: copies the
    /// database and the app's own folders beside it there first, then points
    /// the app at it. The folders too, because the root holds more than the
    /// database — see <see cref="OwnedRootFolders"/> — and each of those is
    /// somebody's data that would otherwise be left behind for the same
    /// reason the database used to be. Only those: whatever else the person
    /// keeps in the folder is theirs and stays where they put it.
    /// <para>
    /// The old folder is left as it was rather than emptied. Somebody moving
    /// off a synced folder is exactly the person whose old copy should not be
    /// removed by the click that was meant to rescue it — and on Windows the
    /// pooled connections the repositories keep on the old database would
    /// refuse the delete anyway. The settings screen says so in its status.
    /// </para>
    /// <para>
    /// A folder that already holds a backlog is never written over. Which of
    /// two databases to keep is not a decision to make from a settings field;
    /// the error points at <see cref="TryUseRoot"/> as the way to open that
    /// one instead.
    /// </para></summary>
    public RootMove TryMoveRoot(string? path)
    {
        var error = ResolveRoot(path, out var full);
        if (error is not null) return RootMove.Failed(error);

        if (IsCurrentRoot(full)) return RootMove.Pointed;

        var sourceDatabase = DatabasePath;
        var targetDatabase = SqliteTaskRepository.DatabasePathFor(full);

        // Nothing written yet means nothing to carry: the move is a plain
        // repoint, and whatever the folder holds is what the app reads next.
        if (!File.Exists(sourceDatabase))
        {
            var pointError = PointAt(full);
            return RootMove.Pointed with { Error = pointError };
        }

        if (File.Exists(targetDatabase))
        {
            return RootMove.Failed(
                "That folder already holds a backlog, so nothing was moved. "
                + "Move its backlog.db aside first, or switch without moving to use that backlog instead.");
        }

        try
        {
            SqliteDatabaseFile.CopyTo(sourceDatabase, targetDatabase);
            CopyOwnedRootFolders(RootDirectory, full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or SqliteException)
        {
            return RootMove.Failed($"Couldn't move the backlog: {ex.Message}");
        }

        var saveError = PointAt(full);
        // A save failure here is the same one PointAt reports: the move happened
        // and only remembering it did not, so the answer is the success with
        // that message attached rather than a failure that undoes nothing.
        return RootMove.Copied with { Error = saveError };
    }

    /// <summary>Returns the app to its default per-user folder.</summary>
    public string? ResetToDefault() => TryUseRoot(DefaultRootDirectory);

    /// <summary>Turns what somebody typed into the folder it names, or says why
    /// it cannot be one. Makes the folder usable on the way, so a path that
    /// resolves is a path the app can actually be pointed at.</summary>
    private static string? ResolveRoot(string? path, out string full)
    {
        full = string.Empty;

        if (string.IsNullOrWhiteSpace(path)) return "Enter a folder path.";

        var trimmed = path.Trim();

        // Resolving a relative path against whatever the working directory
        // happens to be would quietly put someone's backlog somewhere they
        // never named. Ask for the whole path instead.
        if (!Path.IsPathRooted(trimmed)) return "Use a full path, such as D:\\Notes\\Backlog.";

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

        return null;
    }

    private bool IsCurrentRoot(string full) =>
        string.Equals(Path.TrimEndingDirectorySeparator(full),
            Path.TrimEndingDirectorySeparator(RootDirectory),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>The repoint itself, once the folder is known to be usable and
    /// whatever had to be copied into it is there.</summary>
    private string? PointAt(string full)
    {
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

    /// <summary>Copies the app's own folders — <see cref="OwnedRootFolders"/>,
    /// each one whole — from one root into another, and nothing else. The
    /// database is not among them: it goes through the backup API, and a raw
    /// copy of the journal SQLite keeps beside it would sit next to a database
    /// that no longer matches it. Only files the destination does not already
    /// have: a folder that exists but holds no database is still somebody's
    /// folder, and nothing in it is replaced. Empty folders come too, so the
    /// inbox folder is prepared in the new root the way it was in the old one.
    /// <para>
    /// Internal rather than private because the packaged app's first start
    /// after this convention has the same copy to make — from the folder its
    /// installer used to redirect writes into — and should make it the same way.
    /// </para></summary>
    internal static void CopyOwnedRootFolders(string source, string destination)
    {
        foreach (var name in OwnedRootFolders)
        {
            var folder = Path.Combine(source, name);
            if (!Directory.Exists(folder)) continue;

            CopyFolderContents(folder, Path.Combine(destination, name));
        }
    }

    private static void CopyFolderContents(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var folder in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, folder)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            if (File.Exists(target)) continue;

            File.Copy(file, target);
        }
    }

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

    public string? SetDevbookFolder(string key, bool enabled, string? path)
    {
        if (string.IsNullOrWhiteSpace(key)) return "Choose a knowledge folder before saving.";

        var folders = DevbookFolderSetting.Normalize(DevbookFolders).ToList();
        var index = folders.FindIndex(folder => string.Equals(folder.Key, key, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return $"Unknown knowledge folder '{key}'.";

        folders[index] = folders[index] with
        {
            Enabled = enabled,
            Path = string.IsNullOrWhiteSpace(path) ? null : path.Trim()
        };

        DevbookFolders = DevbookFolderSetting.Normalize(folders);
        var error = SaveSettings("Devbook folders updated, but the choice couldn't be saved for next time.");
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
    public string? SetDevbookCacheDirectory(string? path)
    {
        var trimmed = Clean(path);

        if (trimmed is null)
        {
            if (IsDefaultDevbookCacheDirectory) return null;

            DevbookCacheDirectory = DefaultDevbookCacheDirectory;
            var reset = SaveSettings("Snapshot folder reset, but the choice couldn't be saved for next time.");
            if (reset is null) RootChanged?.Invoke();
            return reset;
        }

        // The same demand TryUseRoot makes, for the same reason: a relative path
        // would resolve against whatever the working directory happens to be and
        // put somebody's snapshots somewhere they never named.
        if (!Path.IsPathRooted(trimmed)) return "Use a full path, such as D:\\Backlog\\devbook-cache.";

        string full;
        try
        {
            full = Path.GetFullPath(trimmed);
        }
        catch (Exception)
        {
            return "That doesn't look like a valid folder path.";
        }

        if (!Path.IsPathFullyQualified(full)) return "Use a full path, such as D:\\Backlog\\devbook-cache.";

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
            Path.TrimEndingDirectorySeparator(DevbookCacheDirectory),
            StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        DevbookCacheDirectory = full;

        var error = SaveSettings("Snapshot folder changed, but the choice couldn't be saved for next time.");
        if (error is null) RootChanged?.Invoke();
        return error;
    }

    /// <summary>
    /// The folder branch snapshots go to when nobody has chosen one. A
    /// <c>knowledge-cache</c> left over from before the rename is not looked at:
    /// snapshots are disposable and are fetched again into the new folder.
    /// </summary>
    private static string ResolveDefaultDevbookCacheDirectory(string appData) =>
        Path.Combine(appData, DevbookCacheFolderName);

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
                DevbookFolders = DevbookFolders.Select(StoreDevbookFolderSettings.From).ToList(),

                // Written as null while it is the default, so a workspace nobody
                // has moved the cache in keeps producing the file it always did.
                DevbookCacheDirectory = IsDefaultDevbookCacheDirectory ? null : DevbookCacheDirectory
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

        public List<StoreDevbookFolderSettings>? DevbookFolders { get; init; }

        /// <summary>Where branch snapshots are cached, or null for the default
        /// folder beside this file. Absent reads as the default, which is what
        /// every settings file written before branch loading existed says.</summary>
        public string? DevbookCacheDirectory { get; init; }

        /// <summary>FROZEN LEGACY FIELDS: the names <see cref="DevbookFolders"/>
        /// and <see cref="DevbookCacheDirectory"/> were written under while the
        /// context was called Knowledge. Read when the current name is absent,
        /// never assigned on save, and omitted when null so the file written back
        /// carries only the current names.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<StoreDevbookFolderSettings>? KnowledgeFolders { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? KnowledgeCacheDirectory { get; init; }
    }

    private sealed record StoreDevbookFolderSettings
    {
        public string? Key { get; init; }

        public bool Enabled { get; init; } = true;

        public string? Path { get; init; }

        /// <summary>The stored row as a setting, or null when its key names no
        /// knowledge folder — a typo, or a section since retired, as
        /// <c>.backlog</c> now is. Dropping it is what <see cref="DevbookFolderSetting.Normalize"/>
        /// would do anyway; saying so here is what keeps a stale file from
        /// stopping the app from opening.</summary>
        public DevbookFolderSetting? ToSetting()
        {
            var folder = DevbookFolderSetting.Defaults()
                .FirstOrDefault(defaultFolder => string.Equals(defaultFolder.Key, Key, StringComparison.OrdinalIgnoreCase));

            return folder is null
                ? null
                : folder with
                {
                    Enabled = Enabled,
                    Path = Path
                };
        }

        public static StoreDevbookFolderSettings From(DevbookFolderSetting folder) => new()
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
