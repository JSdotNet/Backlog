using System.Text.Json;

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
                KnowledgeFeatures.KnowledgeSections,
                KnowledgeFeatures.RepositoryKnowledge,
                KnowledgeFeatures.ArchifyDiagrams,
                DevPcFeatures.SystemTools,
                SessionFeatures.Sessions,
                DashboardFeatures.Dashboard,

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

    private static string NewSettingsPath() =>
        Path.Combine(Path.GetTempPath(), "backlog-feature-tests", Guid.NewGuid().ToString("n"), "features.json");

    private static void DeleteSettingsDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory is null) return;

        try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
    }
}
