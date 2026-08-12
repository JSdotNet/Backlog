using System.Text.Json;
using Backlog.Desktop.UI.Services;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class AppFeatureSettingsStoreTests
{
    [Fact]
    public void Backlog_cannot_be_disabled()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new AppFeatureSettingsStore(path);

            var message = store.SetEnabled(AppFeatureSettingsStore.Backlog, enabled: false);

            Assert.True(store.IsEnabled(AppFeatureSettingsStore.Backlog));
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
            var store = new AppFeatureSettingsStore(path);
            store.SetEnabled(AppFeatureSettingsStore.GitHubIntegration, enabled: false);
            store.SetEnabled(AppFeatureSettingsStore.CopilotCli, enabled: false);
            store.SetEnabled(AppFeatureSettingsStore.AdditionalRepositories, enabled: false);

            var restarted = new AppFeatureSettingsStore(path);

            Assert.False(restarted.IsEnabled(AppFeatureSettingsStore.GitHubIntegration));
            Assert.False(restarted.IsEnabled(AppFeatureSettingsStore.CopilotCli));
            Assert.False(restarted.IsEnabled(AppFeatureSettingsStore.AdditionalRepositories));
            Assert.True(restarted.IsEnabled(AppFeatureSettingsStore.Backlog));
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
                AppFeatureSettingsStore.Backlog,
                AppFeatureSettingsStore.KnowledgeSections,
                AppFeatureSettingsStore.RepositoryKnowledge,
                AppFeatureSettingsStore.AdditionalRepositories,
                AppFeatureSettingsStore.SystemTools,
                AppFeatureSettingsStore.GitHubIntegration,
                AppFeatureSettingsStore.FeedbackReporting,
                AppFeatureSettingsStore.CopilotCli,
                AppFeatureSettingsStore.AiAssistant,
                AppFeatureSettingsStore.UsageMetrics
            ],
            AppFeatureSettingsStore.Features.Select(feature => feature.Key));
    }

    [Fact]
    public void Usage_metrics_stays_off_until_it_is_asked_for()
    {
        var path = NewSettingsPath();

        try
        {
            var store = new AppFeatureSettingsStore(path);

            Assert.False(store.IsEnabled(AppFeatureSettingsStore.UsageMetrics));

            store.SetEnabled(AppFeatureSettingsStore.UsageMetrics, enabled: true);

            Assert.True(new AppFeatureSettingsStore(path).IsEnabled(AppFeatureSettingsStore.UsageMetrics));
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

            Assert.False(new AppFeatureSettingsStore(path).IsEnabled(AppFeatureSettingsStore.UsageMetrics));
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
            var store = new AppFeatureSettingsStore(path);
            store.SetEnabled(AppFeatureSettingsStore.UsageMetrics, enabled: true);
            store.SetEnabled(AppFeatureSettingsStore.UsageMetrics, enabled: false);

            var restarted = new AppFeatureSettingsStore(path);

            Assert.False(restarted.IsEnabled(AppFeatureSettingsStore.UsageMetrics));
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
                    AppFeatureSettingsStore.Backlog,
                    AppFeatureSettingsStore.GitHubIntegration,
                    "retired-feature",
                    "updates",
                    "repositories"
                }
            }));

            var store = new AppFeatureSettingsStore(path);

            Assert.True(store.IsEnabled(AppFeatureSettingsStore.Backlog));
            Assert.Equal([AppFeatureSettingsStore.GitHubIntegration], store.Current.DisabledFeatures);
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
