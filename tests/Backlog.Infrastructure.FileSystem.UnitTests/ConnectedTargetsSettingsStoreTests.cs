using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The connected repositories and products, on disk: every setting and the sync's
/// progress survive a restart, a target is one entry however its name is cased,
/// and a file somebody edited badly opens as nothing connected rather than not at
/// all. Real temp directories, for the reason
/// <see cref="CaptureSourcesSettingsStoreTests"/> gives.
/// </summary>
public sealed class ConnectedTargetsSettingsStoreTests : IDisposable
{
    private static readonly DateTimeOffset Synced = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "connected-targets-settings-store-tests-" + Guid.NewGuid().ToString("N"));

    public ConnectedTargetsSettingsStoreTests() => Directory.CreateDirectory(_root);

    private string SettingsFile => Path.Combine(_root, "connected-targets.json");

    private ConnectedTargetsSettingsStore Store() => new(SettingsFile);

    [Fact]
    public void An_untouched_store_has_nothing_connected()
    {
        Assert.Empty(Store().List());
    }

    [Fact]
    public void Every_setting_and_the_progress_survive_a_restart()
    {
        var target = new ConnectedTarget("github", "JSdotNet/Backlog")
        {
            SkipUntouchedOlderThan = TimeSpan.FromDays(90),
            TitleFollowsSource = false,
            SyncInterval = TimeSpan.FromMinutes(30),
            PromoteArchivesOriginal = false,
            CompleteAtSource = true,
            LastSyncedAt = Synced,
            IgnoreUntouchedBefore = Synced.AddDays(-90),
        };

        Assert.Null(Store().Save(target));

        Assert.Equal(target, Assert.Single(Store().List()));
    }

    [Fact]
    public void Saving_the_same_target_differently_cased_replaces_it()
    {
        var store = Store();
        store.Save(new ConnectedTarget("github", "JSdotNet/Backlog"));
        store.Save(new ConnectedTarget("github", "jsdotnet/backlog", Enabled: false));

        var target = Assert.Single(Store().List());
        Assert.False(target.Enabled);
        Assert.NotNull(Store().Get("github", "JSDOTNET/BACKLOG"));
    }

    [Fact]
    public void A_removed_target_stays_removed()
    {
        var store = Store();
        store.Save(new ConnectedTarget("github", "JSdotNet/Backlog"));
        store.Save(new ConnectedTarget("github", "JSdotNet/Docs"));

        Assert.Null(store.Remove("github", "JSdotNet/Backlog"));
        Assert.Null(store.Remove("github", "JSdotNet/Backlog"));

        Assert.Equal(["JSdotNet/Docs"], Store().List().Select(target => target.Target));
    }

    /// <summary>The file is replaced whole or not at all, through a temporary file
    /// beside it: a write cut short leaves the previous file, never a truncated
    /// one that would open as nothing connected. So a write that cannot complete —
    /// here the temporary file's name is taken by a folder — is reported, by a
    /// removal as by a save, and the file on disk keeps what it had.</summary>
    [Fact]
    public void A_write_that_cannot_complete_is_reported_and_leaves_the_file_as_it_was()
    {
        var store = Store();
        Assert.Null(store.Save(new ConnectedTarget("github", "JSdotNet/Backlog")));
        Directory.CreateDirectory(SettingsFile + ".tmp");

        Assert.NotNull(store.Remove("github", "JSdotNet/Backlog"));
        Assert.NotNull(store.Save(new ConnectedTarget("github", "JSdotNet/Docs")));

        Directory.Delete(SettingsFile + ".tmp");
        Assert.Equal(["JSdotNet/Backlog"], Store().List().Select(target => target.Target));
    }

    [Fact]
    public void A_save_leaves_no_temporary_file_behind()
    {
        Assert.Null(Store().Save(new ConnectedTarget("github", "JSdotNet/Backlog")));

        Assert.Equal([SettingsFile], Directory.GetFileSystemEntries(_root));
    }

    /// <summary>An update is applied to what the store holds at that moment, under
    /// its lock, so a sync recording its progress cannot write back a setting the
    /// person changed while it ran.</summary>
    [Fact]
    public void An_update_changes_the_stored_target_and_persists()
    {
        var store = Store();
        store.Save(new ConnectedTarget("github", "JSdotNet/Backlog") { TitleFollowsSource = false });

        Assert.Null(store.Update("github", "jsdotnet/backlog", target => target with { LastSyncedAt = Synced }));

        var reopened = Assert.Single(Store().List());
        Assert.Equal(Synced, reopened.LastSyncedAt);
        Assert.False(reopened.TitleFollowsSource);
    }

    [Fact]
    public void An_update_of_a_target_that_is_not_connected_adds_nothing()
    {
        var store = Store();

        Assert.Null(store.Update("github", "JSdotNet/Backlog", target => target with { LastSyncedAt = Synced }));

        Assert.Empty(store.List());
    }

    [Fact]
    public void A_hand_edited_line_with_no_interval_takes_the_default_and_one_with_no_target_is_dropped()
    {
        File.WriteAllText(SettingsFile, """
            { "targets": [
                { "connectorId": "github", "target": "JSdotNet/Backlog" },
                { "connectorId": "github", "target": "" }
            ] }
            """);

        var target = Assert.Single(Store().List());

        Assert.True(target.Enabled);
        Assert.True(target.TitleFollowsSource);
        Assert.Equal(ConnectedTarget.DefaultSyncInterval, target.SyncInterval);
    }

    /// <summary>A file written before targets could complete at the source has no
    /// such key, and reads as the setting's default: off.</summary>
    [Fact]
    public void A_file_written_before_complete_at_source_existed_completes_nothing_at_the_source()
    {
        File.WriteAllText(SettingsFile, """
            { "targets": [
                { "connectorId": "github", "target": "JSdotNet/Backlog", "enabled": true, "titleFollowsSource": true }
            ] }
            """);

        Assert.False(Assert.Single(Store().List()).CompleteAtSource);
    }

    /// <summary>Why the last sync failed survives a restart, so the card still says
    /// it the next time the settings open.</summary>
    [Fact]
    public void The_last_sync_failure_survives_a_restart()
    {
        var target = new ConnectedTarget("spec-manager", "backlog")
        {
            LastSyncedAt = Synced,
            LastSyncError = "spec-manager has no product named backlog.",
            LastSyncFailedAt = Synced.AddMinutes(15),
        };

        Assert.Null(Store().Save(target));

        Assert.Equal(target, Assert.Single(Store().List()));
    }

    /// <summary>A file written before failures were kept has no such keys, and
    /// reads as a target with none.</summary>
    [Fact]
    public void A_file_written_before_sync_failures_were_kept_reads_as_none()
    {
        File.WriteAllText(SettingsFile, """
            { "targets": [
                { "connectorId": "github", "target": "JSdotNet/Backlog", "lastSyncedAt": "2026-10-05T09:00:00+00:00" }
            ] }
            """);

        var target = Assert.Single(Store().List());

        Assert.Equal(Synced, target.LastSyncedAt);
        Assert.Null(target.LastSyncError);
        Assert.Null(target.LastSyncFailedAt);
    }

    [Fact]
    public void The_last_sync_failure_is_written_under_its_own_keys()
    {
        Store().Save(new ConnectedTarget("github", "JSdotNet/Backlog") { LastSyncError = "Nope.", LastSyncFailedAt = Synced });

        var json = File.ReadAllText(SettingsFile);

        Assert.Contains("\"lastSyncError\": \"Nope.\"", json, StringComparison.Ordinal);
        Assert.Contains("\"lastSyncFailedAt\"", json, StringComparison.Ordinal);
    }

    /// <summary>The repository a target's tasks are filed under and its status map
    /// survive a restart, the map still read without regard to case.</summary>
    [Fact]
    public void The_repository_and_the_status_map_survive_a_restart()
    {
        var target = new ConnectedTarget("spec-manager", "backlog")
        {
            RepositoryId = "JSdotNet/Backlog",
            StatusMap = new Dictionary<string, EntryStatus>
            {
                ["Verfijnd"] = EntryStatus.Ready,
                ["Review"] = EntryStatus.InProgress,
            },
        };

        Assert.Null(Store().Save(target));

        var reopened = Assert.Single(Store().List());
        Assert.Equal(target, reopened);
        Assert.Equal("JSdotNet/Backlog", reopened.RepositoryId);
        Assert.Equal(EntryStatus.Ready, reopened.MappedStatusOf("verfijnd"));
    }

    /// <summary>The map is written as the source's status names, each to a status's
    /// own name, so the file reads as what it means and survives the enum being
    /// reordered.</summary>
    [Fact]
    public void The_status_map_is_written_as_names()
    {
        Store().Save(new ConnectedTarget("spec-manager", "backlog")
        {
            StatusMap = new Dictionary<string, EntryStatus> { ["Verfijnd"] = EntryStatus.Ready },
        });

        var json = File.ReadAllText(SettingsFile);

        Assert.Contains("\"statusMap\": {", json, StringComparison.Ordinal);
        Assert.Contains("\"Verfijnd\": \"Ready\"", json, StringComparison.Ordinal);
    }

    /// <summary>A hand-edited map with a status this build has no name for drops
    /// that line rather than the target; a blank repository reads as none.</summary>
    [Fact]
    public void A_hand_edited_map_line_naming_no_status_is_dropped_and_a_blank_repository_is_none()
    {
        File.WriteAllText(SettingsFile, """
            { "targets": [
                { "connectorId": "spec-manager", "target": "backlog", "repositoryId": " ",
                  "statusMap": { "Verfijnd": "ready", "Review": "Reviewing" } }
            ] }
            """);

        var target = Assert.Single(Store().List());

        Assert.Null(target.RepositoryId);
        Assert.Equal(EntryStatus.Ready, target.MappedStatusOf("Verfijnd"));
        Assert.Null(target.MappedStatusOf("Review"));
    }

    /// <summary>A file written before targets had a repository or a status map has
    /// neither key, and reads as the connector's default and the fixed map.</summary>
    [Fact]
    public void A_file_written_before_the_repository_and_the_status_map_existed_reads_as_neither()
    {
        File.WriteAllText(SettingsFile, """
            { "targets": [
                { "connectorId": "github", "target": "JSdotNet/Backlog", "enabled": true, "completeAtSource": true }
            ] }
            """);

        var target = Assert.Single(Store().List());

        Assert.Null(target.RepositoryId);
        Assert.Empty(target.StatusMap);
        Assert.True(target.CompleteAtSource);
    }

    /// <summary>A save of the same map, read back as another instance, changes
    /// nothing: a store tells it by the target's equality.</summary>
    [Fact]
    public void Saving_an_equal_status_map_changes_nothing()
    {
        var store = Store();
        store.Save(new ConnectedTarget("spec-manager", "backlog")
        {
            StatusMap = new Dictionary<string, EntryStatus> { ["Verfijnd"] = EntryStatus.Ready },
        });
        var changes = 0;
        store.Changed += () => changes++;

        store.Save(new ConnectedTarget("spec-manager", "backlog")
        {
            StatusMap = new Dictionary<string, EntryStatus> { ["verfijnd"] = EntryStatus.Ready },
        });

        Assert.Equal(0, changes);
    }

    [Fact]
    public void A_corrupt_file_opens_as_nothing_connected()
    {
        File.WriteAllText(SettingsFile, "{ not json");

        Assert.Empty(Store().List());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
