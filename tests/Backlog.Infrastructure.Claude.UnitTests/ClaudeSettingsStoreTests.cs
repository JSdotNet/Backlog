using Backlog.Infrastructure.Claude;

namespace Backlog.Infrastructure.Claude.UnitTests;

public class ClaudeSettingsStoreTests
{
    [Fact]
    public void A_missing_settings_file_reads_as_unconfigured()
    {
        using var directory = new TemporaryDirectory();

        var store = new ClaudeSettingsStore(directory.File("claude.json"));

        Assert.False(store.Current.IsConfigured);
        Assert.Equal(ClaudeSettingsStore.DefaultApiVersion, store.Current.ApiVersion);
        Assert.Equal(ClaudeSettingsStore.DefaultApiEndpoint, store.Current.ApiEndpoint);
    }

    [Fact]
    public void A_saved_key_survives_a_restart()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.File("claude.json");

        new ClaudeSettingsStore(path).SetAdminApiKey("  sk-ant-admin01-example  ");

        var reopened = new ClaudeSettingsStore(path);

        Assert.Equal("sk-ant-admin01-example", reopened.Current.AdminApiKey);
        Assert.True(reopened.Current.LooksLikeAdminKey);
    }

    [Fact]
    public void A_custom_api_endpoint_is_normalized_and_survives_a_restart()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.File("claude.json");

        var store = new ClaudeSettingsStore(path);
        store.SetApiEndpoint(" https://claude.example.internal/v1/ ");

        var reopened = new ClaudeSettingsStore(path);

        Assert.Equal("https://claude.example.internal/v1", reopened.Current.ApiEndpoint);
    }

    [Fact]
    public void Setting_a_workspace_leaves_the_key_alone()
    {
        using var directory = new TemporaryDirectory();
        var store = new ClaudeSettingsStore(directory.File("claude.json"));

        store.SetAdminApiKey("sk-ant-admin01-example");
        store.SetWorkspaceId("wrkspc_01");

        Assert.Equal("sk-ant-admin01-example", store.Current.AdminApiKey);
        Assert.Equal("wrkspc_01", store.Current.WorkspaceId);
    }

    [Fact]
    public void Clearing_the_key_leaves_nothing_behind_on_disk()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.File("claude.json");

        var store = new ClaudeSettingsStore(path);
        store.SetAdminApiKey("sk-ant-admin01-example");
        store.ClearAdminApiKey();

        Assert.DoesNotContain("sk-ant-admin01-example", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.False(new ClaudeSettingsStore(path).Current.IsConfigured);
    }

    [Fact]
    public void A_corrupt_settings_file_never_stops_the_app_from_opening()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.File("claude.json");
        File.WriteAllText(path, "{ not json at all");

        var store = new ClaudeSettingsStore(path);

        Assert.False(store.Current.IsConfigured);
    }

    [Fact]
    public void Changing_the_settings_tells_whoever_is_listening()
    {
        using var directory = new TemporaryDirectory();
        var store = new ClaudeSettingsStore(directory.File("claude.json"));

        var changes = 0;
        store.Changed += () => changes++;

        store.SetAdminApiKey("sk-ant-admin01-example");

        Assert.Equal(1, changes);
    }
}
