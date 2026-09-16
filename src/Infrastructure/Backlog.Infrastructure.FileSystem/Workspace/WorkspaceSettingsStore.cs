using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Sqlite;
using Backlog.Modules.DevPc.Abstractions;
using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// The workspace's own settings file: where the backlog lives, which GitHub
/// repository backs it up and on what schedule, and where branch snapshots are
/// cached when somebody has moved them.
/// <para>
/// The setting itself is deliberately <em>not</em> stored in the backlog folder —
/// it is kept in a fixed per-user location, because a pointer that moves with
/// the thing it points at is no pointer at all. Move the store to a synced
/// folder and this app still knows where you sent it.
/// </para>
/// <para>
/// One file rather than several, because they are one decision: this folder,
/// backed up to that repository, on this schedule. Only the folder itself is a
/// module port — WorkspaceTaskStore implements the module's store port over
/// this one. The repository is named in an adapter type that no abstractions
/// project may see, and its consumers are the desktop settings screen, which
/// takes this adapter directly the way it already takes the GitHub and Claude
/// ones, and <see cref="BackupWorker"/>, which does the backing up.
/// </para>
/// <para>
/// The storage folder used to carry a devbook of its own — a configured set of
/// knowledge folders read when no repository was scoped. It no longer does: a
/// devbook belongs to a repository, and the repository rows on the Repositories
/// tab are where its folders are configured. A settings file written while that
/// was still true still opens; the rows it carried are ignored and the next
/// save drops them.
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
        BackupSchedule = settings?.BackupSchedule?.ToSchedule() ?? BackupSchedule.Off;

        // The legacy name is consulted only when the current one is absent, so a
        // file written before the context was renamed reads as the same choice
        // and the next save carries it under the current name only.
        _devbookCacheOverride = Clean(settings?.DevbookCacheDirectory) ?? Clean(settings?.KnowledgeCacheDirectory);

        ActivityCacheDirectory = Path.Combine(appData, ActivityCacheFolderName);
        SessionActivityCacheDirectory = Path.Combine(appData, SessionActivityCacheFolderName);
        SpendCacheDirectory = Path.Combine(appData, SpendCacheFolderName);

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

    /// <summary>The GitHub repository the backlog is backed up to, or null while
    /// nobody has named one. Naming it is not enough on its own: nothing is
    /// uploaded until <see cref="BackupSchedule"/> says when, or somebody presses
    /// the button. The folder remains the source of truth — a backup is a copy
    /// that leaves, never one that comes back on its own.</summary>
    public GitHubRepositoryRef? RootRepository { get; private set; }

    /// <summary>When the backlog is backed up to <see cref="RootRepository"/>.
    /// <see cref="BackupSchedule.Off"/> until somebody chooses otherwise, and
    /// kept even while no repository is named so a repository added later
    /// starts on the schedule that was already set.</summary>
    public BackupSchedule BackupSchedule { get; private set; }

    /// <summary>Raised when the backup repository or its schedule changes, for
    /// the worker that has to re-arm its timer. Separate from
    /// <see cref="RootChanged"/> because a backup setting moving is not a reason
    /// for every open view to reload the backlog.</summary>
    public event Action? BackupChanged;

    /// <summary>The folder somebody pointed branch snapshots at, or null while
    /// they go to <see cref="DefaultDevbookCacheDirectory"/>.</summary>
    private string? _devbookCacheOverride;

    /// <summary>The folder branch snapshots are kept in when nothing overrides
    /// it: <c>devbook-cache</c> under the storage folder, so that a backlog
    /// moved to another disk takes its default cache location along and the
    /// disk that holds the backlog is the disk that holds what was fetched to
    /// read beside it. Recomputed from <see cref="RootDirectory"/> rather than
    /// stored, which is what makes it follow a move.</summary>
    public string DefaultDevbookCacheDirectory => Path.Combine(RootDirectory, DevbookCacheFolderName);

    /// <summary>
    /// Where the devbook fetched from a repository branch is cached.
    /// <para>
    /// Under the storage folder by default, and configurable, because a machine
    /// with a small system drive and a large one for work is an ordinary
    /// machine, and a repository tree per registered repository is the kind of
    /// thing people want somewhere they chose. A snapshot is a disposable copy
    /// of a commit that can always be fetched again, so nothing here is backed
    /// up: <see cref="BackupWorker"/> takes the database and only the database,
    /// and a move carries <see cref="OwnedRootFolders"/> and leaves this folder
    /// to refill in the new place.
    /// </para>
    /// </summary>
    public string DevbookCacheDirectory => _devbookCacheOverride ?? DefaultDevbookCacheDirectory;

    /// <summary>Whether snapshots are still going to the folder under the
    /// storage folder. The settings screen shows the field empty when they
    /// are, so the placeholder does the explaining rather than a path somebody
    /// never typed.</summary>
    public bool IsDefaultDevbookCacheDirectory => _devbookCacheOverride is null;

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
    /// Not configurable, unlike <see cref="DevbookCacheDirectory"/>, and not
    /// under the root either. That one is a setting because a repository tree
    /// per registered repository is large enough that somebody with a small
    /// system drive needs a say; this is kilobytes, and a second path field on
    /// the settings screen would cost more attention than it saves disk.
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

    /// <summary>
    /// Where the settled part of the two assistants' spend reports is kept: Claude
    /// Code days more than a couple of days old, Copilot months more than a few
    /// days over.
    /// <para>
    /// Beside the per-user settings and never under the backlog root, for the reason
    /// <see cref="ActivityCacheDirectory"/> gives — and with one more thing at stake
    /// than there: what is in here is money, per model, per day, and a folder of it
    /// carried into a synced drive is a copy of a billing report somebody did not
    /// ask to have copied.
    /// </para>
    /// <para>
    /// A third folder rather than a corner of either of the other two, so that each
    /// cache is one deletion: forgetting a repository's pull requests must not take
    /// a year of spend with it, and clearing the spend must not touch the
    /// transcripts. Not configurable, for the reason the activity cache is not — it
    /// is kilobytes.
    /// </para>
    /// </summary>
    public string SpendCacheDirectory { get; }

    /// <summary>The default cache folder's name under the storage folder. It
    /// used to sit beside the per-user settings instead, under this name or the
    /// older <c>knowledge-cache</c>; a machine that still has one of those has a
    /// folder of snapshots nothing reads any more, which refills in the new
    /// place on the next fetch and can be deleted by hand.</summary>
    internal const string DevbookCacheFolderName = "devbook-cache";

    private const string ActivityCacheFolderName = "activity-cache";

    private const string SessionActivityCacheFolderName = "session-activity-cache";

    private const string SpendCacheFolderName = "spend-cache";

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
        var saveError = SaveSettings("Repository configured, but the choice couldn't be saved for next time.");
        BackupChanged?.Invoke();
        return saveError;
    }

    public string? ClearRepository()
    {
        if (RootRepository is null) return null;

        RootRepository = null;
        var error = SaveSettings("Repository cleared, but the choice couldn't be saved for next time.");
        BackupChanged?.Invoke();
        return error;
    }

    /// <summary>Changes when the backlog is backed up. Saved even while no
    /// repository is named, and announced either way, so the worker sees a
    /// schedule that was set before the repository as soon as both are there.
    /// The in-memory value moves even when the write fails, the way every
    /// setter here behaves: the person chose it, and the message says only
    /// that it will not survive a restart.</summary>
    public string? SetBackupSchedule(BackupSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (schedule == BackupSchedule) return null;

        BackupSchedule = schedule;
        var error = SaveSettings("Backup schedule changed, but the choice couldn't be saved for next time.");
        BackupChanged?.Invoke();
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

            _devbookCacheOverride = null;
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

        // Typing the default path is choosing the default, not overriding it
        // with a path that happens to match: recorded as no override, so the
        // field shows empty again and a later move of the root takes the cache
        // default along instead of pinning it to the folder the root used to be.
        _devbookCacheOverride = string.Equals(
            Path.TrimEndingDirectorySeparator(full),
            Path.TrimEndingDirectorySeparator(DefaultDevbookCacheDirectory),
            StringComparison.OrdinalIgnoreCase)
            ? null
            : full;

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
                // Written even while off, so the time and day somebody set
                // before switching the schedule off are still there when they
                // switch it back on.
                BackupSchedule = StoreBackupScheduleSettings.From(BackupSchedule),

                // Written as null while it is the default, so a workspace nobody
                // has moved the cache in keeps producing the file it always did.
                DevbookCacheDirectory = _devbookCacheOverride
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

        /// <summary>When the backlog is backed up, or null in a file written
        /// before there was a schedule to write. Absent reads as off.</summary>
        public StoreBackupScheduleSettings? BackupSchedule { get; init; }

        /// <summary>Where branch snapshots are cached, or null for the default
        /// folder under the storage folder. Absent reads as the default, which
        /// is what every settings file written before branch loading existed
        /// says.</summary>
        public string? DevbookCacheDirectory { get; init; }

        /// <summary>FROZEN LEGACY FIELD: the name <see cref="DevbookCacheDirectory"/>
        /// was written under while the context was called Knowledge. Read when
        /// the current name is absent, never assigned on save, and omitted when
        /// null so the file written back carries only the current name.
        /// <para>
        /// The storage folder's own devbook rows — <c>devbookFolders</c>, and
        /// <c>knowledgeFolders</c> before that — are not declared here at all:
        /// the deserializer ignores what it has no property for, which is how a
        /// file that still carries them opens as if it never did.
        /// </para></summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? KnowledgeCacheDirectory { get; init; }
    }

    private sealed record StoreBackupScheduleSettings
    {
        public string? Cadence { get; init; }

        /// <summary>Local time of day as <c>HH:mm</c>, the way the working-hours
        /// file writes its times.</summary>
        public string? At { get; init; }

        public string? Day { get; init; }

        /// <summary>The stored row as a schedule, or null when it does not read
        /// as one — a hand-edited file is a file like any other here, and a row
        /// that cannot be read means off rather than a failure to open.</summary>
        public BackupSchedule? ToSchedule()
        {
            if (!Enum.TryParse<BackupCadence>(Cadence, ignoreCase: true, out var cadence)) return null;

            var at = TimeOnly.TryParseExact(At, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : FileSystem.BackupSchedule.Off.At;

            var day = Enum.TryParse<DayOfWeek>(Day, ignoreCase: true, out var parsedDay)
                ? parsedDay
                : FileSystem.BackupSchedule.Off.Day;

            return new BackupSchedule(cadence, at, day);
        }

        public static StoreBackupScheduleSettings From(BackupSchedule schedule) => new()
        {
            Cadence = schedule.Cadence.ToString(),
            At = schedule.At.ToString("HH:mm", CultureInfo.InvariantCulture),
            Day = schedule.Day.ToString()
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
