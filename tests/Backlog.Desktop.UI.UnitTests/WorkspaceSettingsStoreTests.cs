
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
    /// A settings file written while <c>.backlog</c> was still a knowledge-base
    /// section must keep opening the app. The row names a section that no longer
    /// exists, so it is dropped — silently, because there is nothing the reader
    /// could usefully do about a setting for a section they can no longer see.
    /// </summary>
    [Fact]
    public void A_retired_knowledge_folder_row_is_dropped_rather_than_read()
    {
        var appData = TempDir();
        var settingsPath = Path.Combine(appData, "settings.json");
        Directory.CreateDirectory(appData);
        File.WriteAllText(settingsPath, """
            {
              "knowledgeFolders": [
                { "key": ".backlog", "enabled": true, "path": "docs/.backlog" },
                { "key": ".domain", "enabled": false, "path": "docs/.domain" }
              ]
            }
            """);

        var store = new WorkspaceSettingsStore(appData, settingsPath);

        Assert.DoesNotContain(".backlog", store.KnowledgeFolders.Select(folder => folder.Key));
        var domain = store.KnowledgeFolders.Single(folder => folder.Key == ".domain");
        Assert.False(domain.Enabled);
        Assert.Equal("docs/.domain", domain.EffectivePath);
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
    private static Func<string, SyncedFolderMatch?> Inside(string syncedFolder, string providerName) =>
        root => root.StartsWith(syncedFolder, StringComparison.OrdinalIgnoreCase)
            ? new SyncedFolderMatch(providerName, syncedFolder)
            : null;
}
