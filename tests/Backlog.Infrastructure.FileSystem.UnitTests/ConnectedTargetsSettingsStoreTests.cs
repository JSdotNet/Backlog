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
