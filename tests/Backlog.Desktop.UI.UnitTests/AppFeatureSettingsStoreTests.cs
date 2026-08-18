using System.Text.Json;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class AppFeatureSettingsStoreTests
{
    [Fact]
    public void Backlog_cannot_be_disabled()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new AppFeatureSettingsStore(AppFeatures.All, path);

            var message = store.SetEnabled(BacklogFeatures.Backlog, enabled: false);

            Assert.True(store.IsEnabled(BacklogFeatures.Backlog));
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
            store.SetEnabled(BacklogFeatures.GitHubIntegration, enabled: false);
            store.SetEnabled(AppFeatureKeys.CopilotCli, enabled: false);
            store.SetEnabled(BacklogFeatures.AdditionalRepositories, enabled: false);

            var restarted = new AppFeatureSettingsStore(AppFeatures.All, path);

            Assert.False(restarted.IsEnabled(BacklogFeatures.GitHubIntegration));
            Assert.False(restarted.IsEnabled(AppFeatureKeys.CopilotCli));
            Assert.False(restarted.IsEnabled(BacklogFeatures.AdditionalRepositories));
            Assert.True(restarted.IsEnabled(BacklogFeatures.Backlog));
        }
        finally
        {
            DeleteSettingsDirectory(path);
        }
    }

    [Fact]
    public void Features_are_listed_in_settings_order()
    {
        Assert.Equal(
            [
                BacklogFeatures.Backlog,
                AppFeatures.InboxPane,
                KnowledgeFeatures.KnowledgeSections,
                KnowledgeFeatures.RepositoryKnowledge,
                BacklogFeatures.AdditionalRepositories,
                DevPcFeatures.SystemTools,
                BacklogFeatures.GitHubIntegration,
                AppFeatures.FeedbackReporting,
                AppFeatureKeys.CopilotCli,
                AppFeatures.AiAssistant,
                AppFeatures.UsageMetrics
            ],
            AppFeatures.All.Select(feature => feature.Key));
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
                    BacklogFeatures.Backlog,
                    BacklogFeatures.GitHubIntegration,
                    "retired-feature",
                    "updates",
                    "repositories"
                }
            }));

            var store = new AppFeatureSettingsStore(AppFeatures.All, path);

            Assert.True(store.IsEnabled(BacklogFeatures.Backlog));
            Assert.Equal([BacklogFeatures.GitHubIntegration], store.Current.DisabledFeatures);
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
