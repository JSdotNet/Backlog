using Backlog.Infrastructure.Sqlite;
using Backlog.Modules.Tasks.DomainModels;
using Microsoft.Data.Sqlite;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The store decides where a person's backlog lives. Getting it wrong loses
/// their work, so the failure paths matter more than the happy one.
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class WorkspaceSettingsStoreTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    private string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "backlog-store-tests", Guid.NewGuid().ToString("n"));
        _tempDirs.Add(path);
        return path;
    }

    private WorkspaceSettingsStore Store()
    {
        var appData = TempDir();
        return new WorkspaceSettingsStore(appData, Path.Combine(appData, "settings.json"));
    }

    public void Dispose()
    {
        // The repositories the move tests open keep pooled handles on their
        // databases, and a pooled handle is a folder Windows will not delete.
        SqliteConnection.ClearAllPools();

        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Starts_in_a_usable_folder()
    {
        var store = Store();

        Assert.False(string.IsNullOrWhiteSpace(store.RootDirectory));
        Assert.True(Directory.Exists(store.RootDirectory));
        Assert.True(Directory.Exists(store.InboxDirectory));
    }

    [Fact]
    public void Tasks_live_in_a_database_under_the_root()
    {
        var store = Store();
        var target = TempDir();

        Assert.Null(store.TryUseRoot(target));

        Assert.Equal(Path.Combine(target, "backlog.db"), store.DatabasePath);
    }

    [Fact]
    public void Moving_creates_the_folder_that_was_asked_for()
    {
        var store = Store();
        var target = TempDir();

        Assert.False(Directory.Exists(target));
        Assert.Null(store.TryUseRoot(target));

        Assert.True(Directory.Exists(target));
        Assert.True(Directory.Exists(Path.Combine(target, "_inbox")));
        Assert.Equal(target, store.RootDirectory);
    }

    [Fact]
    public void Moving_points_the_store_at_the_new_folder()
    {
        var store = Store();
        var target = TempDir();

        Assert.Null(store.TryUseRoot(target));

        // Which repository reads that folder is not the store's business — it
        // owns the pointer, and RootedSqliteTaskRepository follows it.
        Assert.Equal(target, store.RootDirectory);
    }

    [Fact]
    public void Moving_announces_itself_so_open_views_can_reload()
    {
        var store = Store();
        var announced = 0;
        store.RootChanged += () => announced++;

        Assert.Null(store.TryUseRoot(TempDir()));

        Assert.Equal(1, announced);
    }

    [Fact]
    public void Re_selecting_the_folder_it_is_already_in_changes_nothing()
    {
        var store = Store();
        var target = TempDir();
        Assert.Null(store.TryUseRoot(target));

        var announced = 0;
        store.RootChanged += () => announced++;

        Assert.Null(store.TryUseRoot(target));

        Assert.Equal(0, announced);
        Assert.Equal(target, store.RootDirectory);
    }

    [Fact]
    public void Trailing_separators_and_casing_are_the_same_folder()
    {
        var store = Store();
        var target = TempDir();
        Assert.Null(store.TryUseRoot(target));

        var announced = 0;
        store.RootChanged += () => announced++;

        Assert.Null(store.TryUseRoot(target.ToUpperInvariant() + Path.DirectorySeparatorChar));

        Assert.Equal(0, announced);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_empty_path_is_answered_not_thrown(string? path)
    {
        var store = Store();
        var before = store.RootDirectory;

        var error = store.TryUseRoot(path);

        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(before, store.RootDirectory);
    }

    [Fact]
    public void A_path_that_cannot_be_a_folder_is_answered_not_thrown()
    {
        var store = Store();
        var before = store.RootDirectory;

        var error = store.TryUseRoot("\0not a path\0");

        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(before, store.RootDirectory);
    }

    [Fact]
    public void A_relative_path_is_refused_rather_than_quietly_resolved()
    {
        var store = Store();
        var before = store.RootDirectory;

        var error = store.TryUseRoot("notes\\backlog");

        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(before, store.RootDirectory);
    }

    [Fact]
    public void A_rejected_path_leaves_the_working_folder_alone()
    {
        var store = Store();
        var before = store.RootDirectory;

        store.TryUseRoot("   ");

        Assert.Equal(before, store.RootDirectory);
    }

    [Fact]
    public void The_default_folder_knows_it_is_the_default()
    {
        var store = Store();
        Assert.Null(store.ResetToDefault());

        Assert.True(store.IsDefaultRoot);
        Assert.Equal(store.DefaultRootDirectory, store.RootDirectory);
    }

    /// <summary>
    /// The parameterless store is what both MauiProgram.cs and the desktop web
    /// harness register through DI. A Debug build must never resolve that to the
    /// same per-user folder a Release install uses, or a local dev/test run would
    /// read and write whatever real backlog is configured on that machine.
    /// </summary>
    [Fact]
    public void Debug_builds_default_to_an_isolated_appdata_folder()
    {
#if DEBUG
        Assert.Equal("Backlog.Debug", WorkspaceSettingsStore.DefaultAppDataFolderName);
#else
        Assert.Equal("Backlog", WorkspaceSettingsStore.DefaultAppDataFolderName);
#endif
    }

    [Fact]
    public void A_chosen_folder_is_not_the_default()
    {
        var store = Store();

        Assert.Null(store.TryUseRoot(TempDir()));

        Assert.False(store.IsDefaultRoot);
    }

    [Fact]
    public void The_choice_survives_a_restart()
    {
        var appData = TempDir();
        var settingsPath = Path.Combine(appData, "settings.json");
        var store = new WorkspaceSettingsStore(appData, settingsPath);
        var target = TempDir();

        Assert.Null(store.TryUseRoot(target));

        var reopened = new WorkspaceSettingsStore(appData, settingsPath);
        Assert.Equal(target, reopened.RootDirectory);
    }

    [Fact]
    public void The_setting_is_not_kept_inside_the_folder_it_points_at()
    {
        var store = Store();
        var target = TempDir();

        Assert.Null(store.TryUseRoot(target));

        Assert.False(File.Exists(Path.Combine(target, "settings.json")));
    }

    [Fact]
    public void Storage_repository_metadata_survives_a_restart()
    {
        var appData = TempDir();
        var settingsPath = Path.Combine(appData, "settings.json");
        var store = new WorkspaceSettingsStore(appData, settingsPath);

        Assert.Null(store.TrySetRepository("JSdotNet/Backlog"));

        var reopened = new WorkspaceSettingsStore(appData, settingsPath);
        Assert.NotNull(reopened.RootRepository);
        Assert.Equal("JSdotNet/Backlog", reopened.RootRepository!.FullName);
    }

    [Fact]
    public void Storage_repository_metadata_can_be_cleared()
    {
        var appData = TempDir();
        var settingsPath = Path.Combine(appData, "settings.json");
        var store = new WorkspaceSettingsStore(appData, settingsPath);
        Assert.Null(store.TrySetRepository("JSdotNet/Backlog"));

        Assert.Null(store.ClearRepository());

        Assert.Null(store.RootRepository);
        Assert.Null(new WorkspaceSettingsStore(appData, settingsPath).RootRepository);
    }

    /// <summary>
    /// The storage folder used to carry a devbook of its own, configured as rows
    /// in this file. A file that still has them — under either name the rows
    /// were ever written under — must keep opening the app, and the next save
    /// must drop them: there is nothing left that reads them.
    /// </summary>
    [Fact]
    public void Storage_devbook_rows_from_an_older_file_are_ignored_and_dropped_on_save()
    {
        var appData = TempDir();
        var settingsPath = Path.Combine(appData, "settings.json");
        Directory.CreateDirectory(appData);
        File.WriteAllText(settingsPath, """
            {
              "knowledgeFolders": [ { "key": ".domain", "enabled": false, "path": null } ],
              "devbookFolders": [
                { "key": ".backlog", "enabled": true, "path": "docs/.backlog" },
                { "key": ".domain", "enabled": false, "path": "docs/.domain" }
              ]
            }
            """);

        var store = new WorkspaceSettingsStore(appData, settingsPath);
        Assert.Null(store.TrySetRepository("JSdotNet/Notes"));

        var saved = File.ReadAllText(settingsPath);
        Assert.DoesNotContain("devbookFolders", saved);
        Assert.DoesNotContain("knowledgeFolders", saved);
        Assert.Contains("\"rootRepository\"", saved);
    }

    /// <summary>
    /// The JSON key for the cache folder is the property name, so renaming the
    /// context renamed it. A settings file written before the rename must read
    /// as the same choice, and the next save must carry only the current key.
    /// </summary>
    [Fact]
    public void A_settings_file_written_under_the_knowledge_name_reads_the_same_cache_choice_and_is_rewritten()
    {
        var appData = TempDir();
        var settingsPath = Path.Combine(appData, "settings.json");
        var chosenCache = Path.Combine(TempDir(), "snapshots");
        Directory.CreateDirectory(appData);
        File.WriteAllText(settingsPath, $$"""
            {
              "knowledgeCacheDirectory": {{System.Text.Json.JsonSerializer.Serialize(chosenCache)}}
            }
            """);

        var store = new WorkspaceSettingsStore(appData, settingsPath);

        Assert.Equal(chosenCache, store.DevbookCacheDirectory);
        Assert.False(store.IsDefaultDevbookCacheDirectory);

        Assert.Null(store.TrySetRepository("JSdotNet/Notes"));

        var saved = File.ReadAllText(settingsPath);
        Assert.Contains("\"devbookCacheDirectory\"", saved);
        Assert.DoesNotContain("knowledgeCacheDirectory", saved);

        var reopened = new WorkspaceSettingsStore(appData, settingsPath);
        Assert.Equal(chosenCache, reopened.DevbookCacheDirectory);
    }

    /// <summary>Snapshots go under the storage folder until somebody says
    /// otherwise, and the settings screen shows the field empty while they do.</summary>
    [Fact]
    public void The_default_cache_folder_sits_under_the_storage_folder()
    {
        var store = Store();

        Assert.Equal(Path.Combine(store.RootDirectory, "devbook-cache"), store.DefaultDevbookCacheDirectory);
        Assert.Equal(store.DefaultDevbookCacheDirectory, store.DevbookCacheDirectory);
        Assert.True(store.IsDefaultDevbookCacheDirectory);
    }

    /// <summary>
    /// The default follows the folder: move the backlog and the default cache
    /// location moves with it, without the old one being copied — a snapshot
    /// refills. A folder somebody chose stays exactly where they put it.
    /// </summary>
    [Fact]
    public void Moving_the_backlog_moves_the_default_cache_location_and_leaves_a_chosen_one_alone()
    {
        var store = Store();
        var target = Path.Combine(TempDir(), "moved");

        Assert.Null(store.TryUseRoot(target));

        Assert.Equal(Path.Combine(Path.GetFullPath(target), "devbook-cache"), store.DevbookCacheDirectory);
        Assert.True(store.IsDefaultDevbookCacheDirectory);

        var chosen = Path.Combine(TempDir(), "snapshots");
        Assert.Null(store.SetDevbookCacheDirectory(chosen));
        Assert.Null(store.TryUseRoot(Path.Combine(TempDir(), "moved-again")));

        Assert.Equal(Path.GetFullPath(chosen), store.DevbookCacheDirectory);
        Assert.False(store.IsDefaultDevbookCacheDirectory);
    }

    /// <summary>Typing the default path is choosing the default rather than
    /// pinning it: the field shows empty again, and a later move takes the
    /// default along.</summary>
    [Fact]
    public void Typing_the_default_cache_path_reads_as_no_override()
    {
        var store = Store();
        Assert.Null(store.SetDevbookCacheDirectory(Path.Combine(TempDir(), "elsewhere")));
        Assert.False(store.IsDefaultDevbookCacheDirectory);

        Assert.Null(store.SetDevbookCacheDirectory(store.DefaultDevbookCacheDirectory));

        Assert.True(store.IsDefaultDevbookCacheDirectory);
    }

    // --- The backup schedule ------------------------------------------------

    /// <summary>Off until chosen, and remembered — cadence, time and day — across
    /// a restart. Written even while off, so the time somebody set before
    /// switching the schedule off is still there when they switch it back on.</summary>
    [Fact]
    public void The_backup_schedule_survives_a_restart()
    {
        var appData = TempDir();
        var settingsPath = Path.Combine(appData, "settings.json");

        var store = new WorkspaceSettingsStore(appData, settingsPath);
        Assert.Equal(BackupSchedule.Off, store.BackupSchedule);

        var chosen = new BackupSchedule(BackupCadence.Weekly, new TimeOnly(7, 30), DayOfWeek.Sunday);
        Assert.Null(store.SetBackupSchedule(chosen));
        Assert.Null(store.SetBackupSchedule(chosen with { Cadence = BackupCadence.Off }));

        var reopened = new WorkspaceSettingsStore(appData, settingsPath);
        Assert.Equal(chosen with { Cadence = BackupCadence.Off }, reopened.BackupSchedule);
    }

    /// <summary>The worker re-arms on the announcement, so the repository and
    /// the schedule both announce — and clearing the repository announces too,
    /// because that is a timer that has to stop.</summary>
    [Fact]
    public void Backup_settings_announce_every_change()
    {
        var store = Store();
        var raised = 0;
        store.BackupChanged += () => raised++;

        Assert.Null(store.TrySetRepository("JSdotNet/Notes"));
        Assert.Null(store.SetBackupSchedule(BackupSchedule.Off with { Cadence = BackupCadence.Daily }));
        Assert.Null(store.SetBackupSchedule(BackupSchedule.Off with { Cadence = BackupCadence.Daily }));
        Assert.Null(store.ClearRepository());

        Assert.Equal(3, raised);
    }

    /// <summary>A hand-edited schedule that does not read as one means off, not
    /// a settings file the app refuses to open.</summary>
    [Fact]
    public void An_unreadable_backup_schedule_reads_as_off()
    {
        var appData = TempDir();
        var settingsPath = Path.Combine(appData, "settings.json");
        Directory.CreateDirectory(appData);
        File.WriteAllText(settingsPath, """
            { "backupSchedule": { "cadence": "fortnightly", "at": "noon", "day": "Someday" } }
            """);

        var store = new WorkspaceSettingsStore(appData, settingsPath);

        Assert.Equal(BackupSchedule.Off, store.BackupSchedule);
    }

    /// <summary>
    /// The half of R9 the corrected copy does not reach. The instruction that put
    /// somebody's backlog on OneDrive is gone from the Storage screen, but a root
    /// already inside a synced folder stays there until somebody moves it — so the
    /// store looks, at construction, which is what "at startup" means for an app
    /// that registers it as a singleton. Local ADR 0005's
    /// <c>### The database filename</c> is where that was decided.
    /// <para>
    /// The probe is a seam for the same reason the VS Code launcher's is: whether
    /// OneDrive is signed in on the build agent is not something a test may
    /// depend on. What the heuristic itself recognises is asserted against a fake
    /// machine in <c>SyncedFolderDetectorTests</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void A_synced_root_is_reported_without_anybody_asking()
    {
        var appData = TempDir();
        var probed = new List<string>();

        var store = new WorkspaceSettingsStore(
            appData,
            Path.Combine(appData, "settings.json"),
            root =>
            {
                probed.Add(root);
                return new SyncedFolderMatch("OneDrive", root);
            });

        Assert.Equal("OneDrive", store.SyncedRoot?.ProviderName);

        // The subject is the root folder and nothing else. ADR 0005 prefers the
        // root precisely because everything under it carries the same hazard, so
        // a check on backlog.db would have answered for one file of several.
        Assert.Equal(store.RootDirectory, Assert.Single(probed));
        Assert.DoesNotContain(store.DatabasePath, probed);
    }

    /// <summary>The probe is stubbed to find nothing rather than left as the real
    /// detector, because over a temporary folder the real one would make this an
    /// assertion about this machine's %TEMP%: a profile relocated onto OneDrive
    /// — the very machine R9 describes — would turn it red for the one reason
    /// that is not a defect.</summary>
    [Fact]
    public void A_plain_folder_is_reported_as_nothing_at_all()
    {
        var appData = TempDir();

        var store = new WorkspaceSettingsStore(appData, Path.Combine(appData, "settings.json"), _ => null);

        Assert.Null(store.SyncedRoot);
    }

    [Fact]
    public void The_synced_root_is_looked_at_again_after_every_move()
    {
        var appData = TempDir();
        var synced = TempDir();
        var store = new WorkspaceSettingsStore(
            appData, Path.Combine(appData, "settings.json"), Inside(synced, "Dropbox"));

        Assert.Null(store.SyncedRoot);

        Assert.Null(store.TryUseRoot(Path.Combine(synced, "backlog")));
        Assert.Equal("Dropbox", store.SyncedRoot?.ProviderName);

        // And moving back out of it clears the warning rather than leaving the
        // screen warning about a folder the app no longer uses.
        Assert.Null(store.TryUseRoot(TempDir()));
        Assert.Null(store.SyncedRoot);
    }

    [Fact]
    public void Resetting_to_the_default_folder_looks_again_too()
    {
        var appData = TempDir();
        var synced = TempDir();
        var store = new WorkspaceSettingsStore(
            appData, Path.Combine(appData, "settings.json"), Inside(synced, "Google Drive"));
        Assert.Null(store.TryUseRoot(Path.Combine(synced, "backlog")));
        Assert.NotNull(store.SyncedRoot);

        Assert.Null(store.ResetToDefault());

        Assert.Null(store.SyncedRoot);
    }

    /// <summary>Warn, do not prevent. The root stays the user's choice, so a
    /// synced one is accepted, saved and reopened like any other.</summary>
    [Fact]
    public void A_synced_root_is_still_accepted_and_remembered()
    {
        var appData = TempDir();
        var settingsPath = Path.Combine(appData, "settings.json");
        var synced = TempDir();
        var target = Path.Combine(synced, "backlog");
        var store = new WorkspaceSettingsStore(appData, settingsPath, Inside(synced, "OneDrive"));

        Assert.Null(store.TryUseRoot(target));

        Assert.Equal(target, store.RootDirectory);
        Assert.True(Directory.Exists(target));
        Assert.Equal(
            target,
            new WorkspaceSettingsStore(appData, settingsPath, Inside(synced, "OneDrive")).RootDirectory);
    }

    /// <summary>Fail open. A probe that cannot answer leaves the app exactly
    /// where a machine with no provider on it does — no warning, and nothing
    /// thrown out of the constructor or out of a move.</summary>
    [Fact]
    public void A_probe_that_throws_is_no_warning_rather_than_a_broken_app()
    {
        var appData = TempDir();
        var store = new WorkspaceSettingsStore(
            appData,
            Path.Combine(appData, "settings.json"),
            _ => throw new IOException("the disk went away"));

        Assert.Null(store.SyncedRoot);

        Assert.Null(store.TryUseRoot(TempDir()));
        Assert.Null(store.SyncedRoot);
    }

    /// <summary>A fake provider that claims one folder and nothing else.</summary>
    /// <summary>
    /// The loss this exists for: somebody whose root was on OneDrive pressed
    /// "Use the default folder" and watched every task disappear, because the
    /// pointer moved and the database did not. Moving carries it.
    /// </summary>
    [Fact]
    public async Task Moving_carries_the_backlog_along()
    {
        var store = Store();
        var target = TempDir();
        var task = new TaskItem("Came along", string.Empty, EntryType.Task);
        await new SqliteTaskRepository(store.RootDirectory).SaveAsync(task, TestContext.Current.CancellationToken);

        var move = store.TryMoveRoot(target);

        Assert.Null(move.Error);
        Assert.True(move.Moved);
        Assert.True(move.CopiedData);
        Assert.Equal(target, store.RootDirectory);

        var arrived = await new SqliteTaskRepository(target).GetAsync(task.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(arrived);
        Assert.Equal("Came along", arrived.Title);
    }

    /// <summary>
    /// Copied, not moved, and asserted rather than assumed: the person leaving a
    /// synced folder is the last one whose old copy should vanish under the
    /// click that rescued it. The screen tells them it is still there.
    /// </summary>
    [Fact]
    public async Task Moving_leaves_the_old_folder_as_it_was()
    {
        var store = Store();
        var previous = store.RootDirectory;
        var task = new TaskItem("Still here too", string.Empty, EntryType.Task);
        await new SqliteTaskRepository(previous).SaveAsync(task, TestContext.Current.CancellationToken);

        Assert.Null(store.TryMoveRoot(TempDir()).Error);

        Assert.True(File.Exists(Path.Combine(previous, "backlog.db")));
        Assert.NotNull(await new SqliteTaskRepository(previous).GetAsync(task.Id, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The database runs in WAL mode and the repositories keep pooled handles
    /// on it, so the newest write can sit in the journal rather than the file.
    /// The pooled connection this test keeps alive is what makes that so; the
    /// copy has to read through SQLite rather than copy the file to see it.
    /// </summary>
    [Fact]
    public async Task Moving_carries_writes_the_journal_still_holds()
    {
        var store = Store();
        var repository = new SqliteTaskRepository(store.RootDirectory);
        var first = new TaskItem("First", string.Empty, EntryType.Task);
        await repository.SaveAsync(first, TestContext.Current.CancellationToken);
        var latest = new TaskItem("Latest", string.Empty, EntryType.Task);
        await repository.SaveAsync(latest, TestContext.Current.CancellationToken);
        var target = TempDir();

        Assert.Null(store.TryMoveRoot(target).Error);

        var titles = (await new SqliteTaskRepository(target).ListAsync(TestContext.Current.CancellationToken))
            .Select(item => item.Title)
            .ToList();
        Assert.Contains("First", titles);
        Assert.Contains("Latest", titles);
    }

    /// <summary>
    /// The root holds more than the database: the inbox folder, the shared
    /// repository registry under <c>config/</c>, a tools catalog when it was put
    /// here. Each is somebody's data and comes along; the database's own files
    /// do not come as raw copies, because the backup already carried the
    /// database and a stale journal beside it would be read as part of it.
    /// </summary>
    [Fact]
    public async Task Moving_carries_the_apps_own_folders_beside_the_database_along()
    {
        var store = Store();
        var target = TempDir();
        await new SqliteTaskRepository(store.RootDirectory).SaveAsync(new TaskItem("So there is a database", string.Empty, EntryType.Task), TestContext.Current.CancellationToken);
        File.WriteAllText(Path.Combine(store.InboxDirectory, "capture.md"), "captured");
        Directory.CreateDirectory(Path.Combine(store.RootDirectory, "config"));
        File.WriteAllText(Path.Combine(store.RootDirectory, "config", "repos.json"), "[]");
        Directory.CreateDirectory(Path.Combine(store.RootDirectory, ".tools", "MY-PC"));
        File.WriteAllText(Path.Combine(store.RootDirectory, ".tools", "ai-tools.json"), "{}");
        File.WriteAllText(Path.Combine(store.RootDirectory, ".tools", "MY-PC", "ai-tools.json"), "{}");

        Assert.Null(store.TryMoveRoot(target).Error);

        Assert.Equal("captured", File.ReadAllText(Path.Combine(target, "_inbox", "capture.md")));
        Assert.Equal("[]", File.ReadAllText(Path.Combine(target, "config", "repos.json")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(target, ".tools", "ai-tools.json")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(target, ".tools", "MY-PC", "ai-tools.json")));

        // The pooled connection the save above left open keeps the journal and
        // its index beside the source database; neither is a file to copy raw.
        Assert.False(File.Exists(Path.Combine(target, "backlog.db-wal")));
        Assert.False(File.Exists(Path.Combine(target, "backlog.db-shm")));
    }

    /// <summary>
    /// And nothing else. The default root was the markdown store's folder once,
    /// and somebody who has used the app that long still has their own notes,
    /// images and folders beside the database — and the retired
    /// <c>_backlog</c> and <c>_roadmap</c> folders the app itself left there.
    /// The move used to take the whole folder, which put all of that in the
    /// new place too; "move the backlog" means the backlog.
    /// </summary>
    [Fact]
    public async Task Moving_leaves_what_the_person_kept_beside_the_backlog_where_it_was()
    {
        var store = Store();
        var target = TempDir();
        await new SqliteTaskRepository(store.RootDirectory).SaveAsync(new TaskItem("So there is a database", string.Empty, EntryType.Task), TestContext.Current.CancellationToken);
        File.WriteAllText(Path.Combine(store.RootDirectory, "Cost.md"), "# my notes");
        File.WriteAllText(Path.Combine(store.RootDirectory, "image.png"), "png");
        Directory.CreateDirectory(Path.Combine(store.RootDirectory, "Documents"));
        File.WriteAllText(Path.Combine(store.RootDirectory, "Documents", "letter.docx"), "docx");
        Directory.CreateDirectory(Path.Combine(store.RootDirectory, "_backlog"));
        File.WriteAllText(Path.Combine(store.RootDirectory, "_backlog", "entry.md"), "# old");
        Directory.CreateDirectory(Path.Combine(store.RootDirectory, "_roadmap"));
        File.WriteAllText(Path.Combine(store.RootDirectory, "_roadmap", "plan.json"), "{}");

        var move = store.TryMoveRoot(target);

        Assert.Null(move.Error);
        Assert.True(move.CopiedData);
        Assert.False(File.Exists(Path.Combine(target, "Cost.md")));
        Assert.False(File.Exists(Path.Combine(target, "image.png")));
        Assert.False(Directory.Exists(Path.Combine(target, "Documents")));
        Assert.False(Directory.Exists(Path.Combine(target, "_backlog")));
        Assert.False(Directory.Exists(Path.Combine(target, "_roadmap")));
    }

    /// <summary>
    /// Which of two backlogs to keep is not a decision a settings field gets to
    /// make by overwriting one of them. The refusal names the way to open the
    /// other one instead.
    /// </summary>
    [Fact]
    public async Task Moving_never_writes_over_a_backlog_already_in_the_folder()
    {
        var store = Store();
        var previous = store.RootDirectory;
        var target = TempDir();
        await new SqliteTaskRepository(previous).SaveAsync(new TaskItem("Mine", string.Empty, EntryType.Task), TestContext.Current.CancellationToken);
        var theirs = new TaskItem("Theirs", string.Empty, EntryType.Task);
        await new SqliteTaskRepository(target).SaveAsync(theirs, TestContext.Current.CancellationToken);

        var move = store.TryMoveRoot(target);

        Assert.False(move.Moved);
        Assert.NotNull(move.Error);
        Assert.Contains("already holds a backlog", move.Error, StringComparison.Ordinal);
        Assert.Contains("switch without moving", move.Error, StringComparison.Ordinal);
        Assert.Equal(previous, store.RootDirectory);

        var untouched = Assert.Single(await new SqliteTaskRepository(target).ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Theirs", untouched.Title);
    }

    /// <summary>A first run that has written nothing has nothing to carry, and
    /// the answer says so rather than claiming a copy that never happened.</summary>
    [Fact]
    public void Moving_with_nothing_written_yet_only_points()
    {
        var store = Store();
        var target = TempDir();

        var move = store.TryMoveRoot(target);

        Assert.Null(move.Error);
        Assert.True(move.Moved);
        Assert.False(move.CopiedData);
        Assert.Equal(target, store.RootDirectory);
        Assert.False(File.Exists(Path.Combine(target, "backlog.db")));
    }

    [Fact]
    public void Moving_to_the_folder_it_is_already_in_changes_nothing()
    {
        var store = Store();
        var announced = 0;
        store.RootChanged += () => announced++;

        var move = store.TryMoveRoot(store.RootDirectory);

        Assert.Null(move.Error);
        Assert.False(move.CopiedData);
        Assert.Equal(0, announced);
    }

    [Fact]
    public void Moving_is_refused_on_the_same_terms_as_pointing()
    {
        var store = Store();
        var previous = store.RootDirectory;

        var move = store.TryMoveRoot("relative\\folder");

        Assert.False(move.Moved);
        Assert.Equal(store.TryUseRoot("relative\\folder"), move.Error);
        Assert.Equal(previous, store.RootDirectory);
    }

    /// <summary>Pointing is still pointing: the way to open a backlog that
    /// already lives somewhere else has to keep copying nothing.</summary>
    [Fact]
    public async Task Pointing_still_carries_nothing()
    {
        var store = Store();
        var target = TempDir();
        await new SqliteTaskRepository(store.RootDirectory).SaveAsync(new TaskItem("Stays", string.Empty, EntryType.Task), TestContext.Current.CancellationToken);

        Assert.Null(store.TryUseRoot(target));

        Assert.False(File.Exists(Path.Combine(target, "backlog.db")));
    }

    private static Func<string, SyncedFolderMatch?> Inside(string syncedFolder, string providerName) =>
        root => root.StartsWith(syncedFolder, StringComparison.OrdinalIgnoreCase)
            ? new SyncedFolderMatch(providerName, syncedFolder)
            : null;
}
