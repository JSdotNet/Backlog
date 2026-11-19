using System.Text.Json;

using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.Sync.Abstractions;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class AppFeatureSettingsStoreTests
{
    /// <summary>
    /// A choice stored under <c>"pull-requests"</c> before the key moved from Sessions to
    /// Repository Management (local ADR 0042) still decides the pull requests pane after
    /// it: the string is the persisted value, and the move kept it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_pull_requests_choice_stored_before_the_key_moved_still_applies(bool enabled)
    {
        var path = NewSettingsPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                disabledFeatures = enabled ? Array.Empty<string>() : ["pull-requests"],
                enabledFeatures = enabled ? new[] { "pull-requests" } : []
            }));

            var store = new AppFeatureSettingsStore(AppFeatures.All, path);

            Assert.Equal(enabled, store.IsEnabled(RepositoryManagementFeatures.PullRequests));
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    [Fact]
    public void Disabled_features_survive_a_restart()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new AppFeatureSettingsStore(AppFeatures.All, path);
            store.SetEnabled(TasksFeatures.GitHubIntegration, enabled: false);
            store.SetEnabled(AppFeatureKeys.CopilotCli, enabled: false);

            var restarted = new AppFeatureSettingsStore(AppFeatures.All, path);

            Assert.False(restarted.IsEnabled(TasksFeatures.GitHubIntegration));
            Assert.False(restarted.IsEnabled(AppFeatureKeys.CopilotCli));
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>The settings screen draws two headings, so the catalog is ordered
    /// to match: every product area first, then everything cross-cutting. The
    /// screen partitions this list rather than holding an order of its own, which
    /// is what keeps "what you see" and "what the store was handed" the same
    /// sequence.</summary>
    [Fact]
    public void Features_are_listed_in_settings_order()
    {
        Assert.Equal(
            [
                // Product areas
                AppFeatures.InboxPane,
                Backlog.Modules.Progress.Abstractions.ProgressFeatures.WeeklyReview,
                DevbookFeatures.LobesAtlas,
                DevbookFeatures.Search,
                DevbookFeatures.SemanticSearch,
                DevbookFeatures.Editing,
                DevbookFeatures.Status,
                DevbookFeatures.DomainView,
                DevPcFeatures.SystemTools,
                SessionFeatures.Sessions,
                RepositoryManagementFeatures.PullRequests,
                SessionFeatures.SessionRequests,
                SyncFeatures.Sync,
                Backlog.Modules.Notifications.Abstractions.NotificationsFeatures.Notifications,
                Backlog.Modules.Events.Abstractions.EventsFeatures.Events,

                // Cross-cutting
                TasksFeatures.GitHubIntegration,
                AppFeatureKeys.CopilotCli,
                AppFeatures.AiAssistant,
                AppFeatures.UsageMetrics
            ],
            AppFeatures.All.Select(feature => feature.Key));
    }

    /// <summary>The sections are derived from the catalog, and this is the half
    /// of that worth pinning: a feature cannot be shown under a heading and left
    /// out of the list, or listed under both.</summary>
    [Fact]
    public void Every_feature_appears_under_exactly_one_heading()
    {
        var listed = AppFeatures.Sections.SelectMany(section => section.Features).ToList();

        Assert.Equal(AppFeatures.All, listed);
        Assert.Equal(AppFeatures.All.Count, listed.DistinctBy(feature => feature.Key).Count());
        Assert.All(AppFeatures.Sections, section => Assert.All(section.Features, feature =>
            Assert.Equal(section.Group, feature.Group)));
    }

    /// <summary>Maturity and the default switch are two decisions, and graduating
    /// a feature is meant to be one of them at a time. Sync moved one step, from
    /// not usable to worth trying, and stays opt-in. Pinned so that the next
    /// graduation is a deliberate edit here too, not a side effect of tidying the
    /// catalog.</summary>
    [Theory]
    [InlineData(SyncFeatures.Sync, AppFeatureStatus.Beta, false)]
    public void Graduated_features_keep_their_maturity_and_default_apart(string key, AppFeatureStatus status, bool enabledByDefault)
    {
        var feature = Assert.Single(AppFeatures.All, feature => feature.Key == key);

        Assert.Equal(status, feature.Status);
        Assert.Equal(enabledByDefault, feature.EnabledByDefault);
    }

    /// <summary>The viewer's Edit control and its status display each sit behind
    /// a switch of their own while their removal is weighed. Both are finished, so
    /// both are Released; both start off, so a person keeps them only by asking.
    /// The keys are persisted values and are pinned as literals.</summary>
    [Theory]
    [InlineData(DevbookFeatures.Editing, "devbook-editing")]
    [InlineData(DevbookFeatures.Status, "devbook-status")]
    public void The_devbook_viewer_switches_are_released_product_areas_that_start_off(string key, string persisted)
    {
        var feature = Assert.Single(AppFeatures.All, feature => feature.Key == key);

        Assert.Equal(persisted, key);
        Assert.Equal(AppFeatureStatus.Released, feature.Status);
        Assert.False(feature.EnabledByDefault);
        Assert.Equal(AppFeatureGroup.Domain, feature.Group);
    }

    /// <summary>Events is a product area still being built (local ADR 0050): marked Dev and
    /// off until a person asks for it, so nothing of it shows by default. The key is a
    /// persisted value and is pinned as a literal.</summary>
    [Fact]
    public void Events_is_a_dev_product_area_that_starts_off()
    {
        var feature = Assert.Single(AppFeatures.All, feature => feature.Key == Backlog.Modules.Events.Abstractions.EventsFeatures.Events);

        Assert.Equal("events", feature.Key);
        Assert.Equal("Events", feature.Name);
        Assert.Equal(AppFeatureStatus.Dev, feature.Status);
        Assert.False(feature.EnabledByDefault);
        Assert.Equal(AppFeatureGroup.Domain, feature.Group);
    }

    [Theory]
    [InlineData(DevbookFeatures.Editing)]
    [InlineData(DevbookFeatures.Status)]
    public void A_devbook_viewer_switch_stays_off_until_it_is_asked_for(string key)
    {
        var path = NewSettingsPath();

        try
        {
            var store = new AppFeatureSettingsStore(AppFeatures.All, path);

            Assert.False(store.IsEnabled(key));

            store.SetEnabled(key, enabled: true);

            Assert.True(new AppFeatureSettingsStore(AppFeatures.All, path).IsEnabled(key));
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    [Fact]
    public void Usage_metrics_stays_off_until_it_is_asked_for()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new AppFeatureSettingsStore(AppFeatures.All, path);

            Assert.False(store.IsEnabled(AppFeatures.UsageMetrics));

            store.SetEnabled(AppFeatures.UsageMetrics, enabled: true);

            Assert.True(new AppFeatureSettingsStore(AppFeatures.All, path).IsEnabled(AppFeatures.UsageMetrics));
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    [Fact]
    public void Inbox_pane_stays_off_until_it_is_asked_for()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new AppFeatureSettingsStore(AppFeatures.All, path);

            Assert.False(store.IsEnabled(AppFeatures.InboxPane));

            store.SetEnabled(AppFeatures.InboxPane, enabled: true);

            Assert.True(new AppFeatureSettingsStore(AppFeatures.All, path).IsEnabled(AppFeatures.InboxPane));
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    [Fact]
    public void An_opt_in_feature_stays_off_for_settings_written_before_it_existed()
    {
        var path = NewSettingsPath();

        try
        {
            // A file saved by an older build knows nothing about usage metrics;
            // silence must not read as consent.
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { disabledFeatures = Array.Empty<string>() }));

            Assert.False(new AppFeatureSettingsStore(AppFeatures.All, path).IsEnabled(AppFeatures.UsageMetrics));
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    [Fact]
    public void Switching_an_opt_in_feature_back_off_forgets_it_again()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new AppFeatureSettingsStore(AppFeatures.All, path);
            store.SetEnabled(AppFeatures.UsageMetrics, enabled: true);
            store.SetEnabled(AppFeatures.UsageMetrics, enabled: false);

            var restarted = new AppFeatureSettingsStore(AppFeatures.All, path);

            Assert.False(restarted.IsEnabled(AppFeatures.UsageMetrics));
            Assert.Empty(restarted.Current.EnabledFeatures);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    [Fact]
    public void Unknown_keys_are_ignored_when_loaded()
    {
        var path = NewSettingsPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                disabledFeatures = new[]
                {
                    TasksFeatures.GitHubIntegration,
                    "retired-feature",
                    "updates",
                    "repositories"
                }
            }));

            var store = new AppFeatureSettingsStore(AppFeatures.All, path);

            Assert.Equal([TasksFeatures.GitHubIntegration], store.Current.DisabledFeatures);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>
    /// The C4 switches are retired: the architecture surface is always on, the only
    /// reading of the C4 model (local ADR 0046). So is <c>"archify-diagrams"</c>, with
    /// Archify itself (local ADR 0057). A file written while any of them was on still
    /// loads, the choices beside them keep, and the next save carries no retired key.
    /// </summary>
    [Fact]
    public void Stored_c4_switch_choices_are_dropped_on_the_next_save()
    {
        var path = NewSettingsPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                enabledFeatures = new[] { "c4-diagrams", "c4-zoom", "archify-diagrams", DevbookFeatures.LobesAtlas }
            }));

            var store = new AppFeatureSettingsStore(AppFeatures.All, path);

            Assert.Equal([DevbookFeatures.LobesAtlas], store.Current.EnabledFeatures);

            store.SetEnabled(AppFeatureKeys.CopilotCli, enabled: false);

            var saved = File.ReadAllText(path);
            Assert.Contains(DevbookFeatures.LobesAtlas, saved);
            Assert.DoesNotContain("archify-diagrams", saved);
            Assert.DoesNotContain("c4-diagrams", saved);
            Assert.DoesNotContain("c4-zoom", saved);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>
    /// The always-on switches are retired: the roadmap, the Devbook, the dashboard,
    /// additional repositories, feedback reporting and the copy button are permanent,
    /// and the backlog never was switchable. A file that switched any of them off
    /// still loads, the choice beside them keeps, and the next save carries none of
    /// the retired keys.
    /// </summary>
    [Fact]
    public void Stored_choices_for_retired_switches_are_dropped_on_the_next_save()
    {
        var path = NewSettingsPath();
        string[] retired =
            ["roadmap", "repository-devbook", "dashboard", "additional-repositories", "feedback-reporting", "copy-entries", "backlog"];

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                disabledFeatures = retired.Append(TasksFeatures.GitHubIntegration).ToArray()
            }));

            var store = new AppFeatureSettingsStore(AppFeatures.All, path);

            Assert.Equal([TasksFeatures.GitHubIntegration], store.Current.DisabledFeatures);

            store.SetEnabled(AppFeatureKeys.CopilotCli, enabled: false);

            var saved = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path))
                .GetProperty("disabledFeatures").EnumerateArray().Select(key => key.GetString()).ToList();
            Assert.Equal([AppFeatureKeys.CopilotCli, TasksFeatures.GitHubIntegration], saved);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>
    /// The feature flags include Sync. A Debug head that read the installed app's
    /// file would come up with Sync on because the installed app has it on, so
    /// the default path sits under the same isolated folder as the workspace.
    /// Read-only: the store creates the folder and reads the file, and writes
    /// nothing until a flag is set.
    /// </summary>
    [Fact]
    public void The_default_file_sits_under_the_default_app_data_folder()
    {
        var store = new AppFeatureSettingsStore(AppFeatures.All);

        Assert.Equal(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                WorkspaceSettingsStore.DefaultAppDataFolderName,
                "features.json"),
            store.SettingsPath);
    }

    private static string NewSettingsPath() =>
        Path.Combine(Path.GetTempPath(), "backlog-feature-tests", Guid.NewGuid().ToString("n"), "features.json");

    private static void DeleteSettingsDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory is null) return;

        try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
    }
}
