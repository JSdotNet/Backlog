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

            var restarted = new AppFeatureSettingsStore(path);

            Assert.False(restarted.IsEnabled(AppFeatureSettingsStore.GitHubIntegration));
            Assert.False(restarted.IsEnabled(AppFeatureSettingsStore.CopilotCli));
            Assert.True(restarted.IsEnabled(AppFeatureSettingsStore.Backlog));
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
                    "retired-feature",
                    AppFeatureSettingsStore.Updates
                }
            }));

            var store = new AppFeatureSettingsStore(path);

            Assert.True(store.IsEnabled(AppFeatureSettingsStore.Backlog));
            Assert.False(store.IsEnabled(AppFeatureSettingsStore.Updates));
            Assert.Equal([AppFeatureSettingsStore.Updates], store.Current.DisabledFeatures);
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
