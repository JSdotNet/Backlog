using System.Text.Json;

using Backlog.Modules.Sync.Abstractions;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class AppFeatureSettingsStoreTests
{
    /// <summary>
    /// The Tasks feature key is still the string "backlog".
    /// <para>
    /// The constant was renamed with the bounded context, but the value is a persisted
    /// key: it is written into features.json as a member of the disabled/enabled sets.
    /// Renaming the string would orphan whatever the reader had already toggled — their
    /// settings would not error, they would just stop applying.
    /// </para>
    /// </summary>
    [Fact]
    public void The_tasks_feature_key_keeps_the_value_it_was_stored_under()
    {
        Assert.Equal("backlog", TasksFeatures.Tasks);
    }

    [Fact]
    public void Tasks_cannot_be_disabled()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new AppFeatureSettingsStore(AppFeatures.All, path);

            var message = store.SetEnabled(TasksFeatures.Tasks, enabled: false);

            Assert.True(store.IsEnabled(TasksFeatures.Tasks));
            Assert.Contains("always available", message);
            Assert.Empty(store.Current.DisabledFeatures);
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
            store.SetEnabled(TasksFeatures.AdditionalRepositories, enabled: false);

            var restarted = new AppFeatureSettingsStore(AppFeatures.All, path);

            Assert.False(restarted.IsEnabled(TasksFeatures.GitHubIntegration));
            Assert.False(restarted.IsEnabled(AppFeatureKeys.CopilotCli));
            Assert.False(restarted.IsEnabled(TasksFeatures.AdditionalRepositories));
            Assert.True(restarted.IsEnabled(TasksFeatures.Tasks));
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
                TasksFeatures.Tasks,
                AppFeatures.InboxPane,
                RoadmapFeatures.Roadmap,
                DevbookFeatures.DevbookSections,
                DevbookFeatures.RepositoryDevbook,
                DevbookFeatures.ArchifyDiagrams,
                DevbookFeatures.C4Diagrams,
                DevbookFeatures.Search,
                DevbookFeatures.SemanticSearch,
                DevPcFeatures.SystemTools,
                SessionFeatures.Sessions,
                DashboardFeatures.Dashboard,
                SyncFeatures.Sync,

                // Cross-cutting
                TasksFeatures.AdditionalRepositories,
                TasksFeatures.GitHubIntegration,
                AppFeatures.FeedbackReporting,
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
    public void Unknown_and_always_enabled_keys_are_ignored_when_loaded()
    {
        var path = NewSettingsPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                disabledFeatures = new[]
                {
                    TasksFeatures.Tasks,
                    TasksFeatures.GitHubIntegration,
                    "retired-feature",
                    "updates",
                    "repositories"
                }
            }));

            var store = new AppFeatureSettingsStore(AppFeatures.All, path);

            Assert.True(store.IsEnabled(TasksFeatures.Tasks));
            Assert.Equal([TasksFeatures.GitHubIntegration], store.Current.DisabledFeatures);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>
    /// A feature whose key had to change still remembers what the reader chose
    /// under the old one. Sync used to be three opt-in switches - device pairing,
    /// task sync, session sync - and a file written while any of them was on
    /// must come up with Sync on, or the rename would silently switch off the
    /// very thing the person had asked for. The catalog names the former keys;
    /// the store knows nothing about Sync itself.
    /// </summary>
    [Theory]
    [InlineData("device-pairing")]
    [InlineData("task-sync")]
    [InlineData("session-sync")]
    public void A_feature_stays_on_when_it_was_switched_on_under_a_former_key(string formerKey)
    {
        var path = NewSettingsPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { enabledFeatures = new[] { formerKey } }));

            var store = new AppFeatureSettingsStore(AppFeatures.All, path);

            Assert.True(store.IsEnabled(SyncFeatures.Sync));
            Assert.Equal([SyncFeatures.Sync], store.Current.EnabledFeatures);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>The former key is a way in, not a second name: once the choice
    /// has been read it is kept under the current key and the old one is not
    /// written back, and switching the feature off afterwards clears it for good
    /// rather than leaving the old key behind to switch it on again at the next
    /// start.</summary>
    [Fact]
    public void A_former_key_is_rewritten_under_the_current_one_on_the_next_save()
    {
        var path = NewSettingsPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { enabledFeatures = new[] { "task-sync" } }));

            var store = new AppFeatureSettingsStore(AppFeatures.All, path);
            store.SetEnabled(SyncFeatures.Sync, enabled: false);

            var restarted = new AppFeatureSettingsStore(AppFeatures.All, path);

            Assert.False(restarted.IsEnabled(SyncFeatures.Sync));
            Assert.DoesNotContain("task-sync", File.ReadAllText(path));
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>An opt-out feature reads its former key from the disabled set, the
    /// same way round.</summary>
    [Fact]
    public void An_opt_out_feature_stays_off_when_it_was_switched_off_under_a_former_key()
    {
        var path = NewSettingsPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { disabledFeatures = new[] { "old-key" } }));

            IReadOnlyList<AppFeatureDefinition> catalog =
            [
                new("new-key", "Renamed", "An opt-out feature that changed its key.", FormerKeys: ["old-key"])
            ];

            var store = new AppFeatureSettingsStore(catalog, path);

            Assert.False(store.IsEnabled("new-key"));
            Assert.Equal(["new-key"], store.Current.DisabledFeatures);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    /// <summary>
    /// The Devbook context was called Knowledge, and four of its feature keys
    /// carried that name into the reader's settings file. A file written before
    /// the rename must read as the same choices — the pane somebody switched off
    /// stays off, the search they switched on stays on — and the next save must
    /// carry only the current keys.
    /// </summary>
    [Fact]
    public void The_devbook_features_read_the_keys_they_were_stored_under_before_the_rename()
    {
        var path = NewSettingsPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                disabledFeatures = new[] { "repository-knowledge", "knowledge-sections" },
                enabledFeatures = new[] { "knowledge-search", "knowledge-semantic-search" }
            }));

            var store = new AppFeatureSettingsStore(AppFeatures.All, path);

            Assert.False(store.IsEnabled(DevbookFeatures.RepositoryDevbook));
            Assert.False(store.IsEnabled(DevbookFeatures.DevbookSections));
            Assert.True(store.IsEnabled(DevbookFeatures.Search));
            Assert.True(store.IsEnabled(DevbookFeatures.SemanticSearch));

            store.SetEnabled(DevbookFeatures.RepositoryDevbook, enabled: false);

            var saved = File.ReadAllText(path);
            Assert.Contains("repository-devbook", saved);
            Assert.Contains("devbook-sections", saved);
            Assert.Contains("devbook-search", saved);
            Assert.Contains("devbook-semantic-search", saved);
            Assert.DoesNotContain("knowledge", saved);
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
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
