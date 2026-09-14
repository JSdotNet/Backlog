namespace Backlog.Infrastructure.Sync.UnitTests;

public sealed class SyncServiceSettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-sync-settings-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void A_fresh_store_has_no_service_url()
    {
        var store = new SyncServiceSettingsStore(NewSettingsPath());

        Assert.Null(store.Current.ServiceUrl);
        Assert.False(store.Current.IsConfigured);
    }

    [Fact]
    public void A_saved_url_survives_a_restart()
    {
        var path = NewSettingsPath();
        var store = new SyncServiceSettingsStore(path);

        var error = store.SetServiceUrl("https://sync.example.test/");

        Assert.Null(error);
        var restarted = new SyncServiceSettingsStore(path);
        Assert.Equal("https://sync.example.test", restarted.Current.ServiceUrl);
        Assert.True(restarted.Current.IsConfigured);
    }

    [Fact]
    public void Blank_clears_the_url()
    {
        var store = new SyncServiceSettingsStore(NewSettingsPath());
        store.SetServiceUrl("https://sync.example.test");

        var error = store.SetServiceUrl("   ");

        Assert.Null(error);
        Assert.Null(store.Current.ServiceUrl);
    }

    [Theory]
    [InlineData("sync.example.test")]
    [InlineData("ftp://sync.example.test")]
    [InlineData("not a url")]
    public void A_value_that_is_not_an_absolute_http_url_is_refused_and_not_saved(string value)
    {
        var store = new SyncServiceSettingsStore(NewSettingsPath());
        store.SetServiceUrl("https://kept.example.test");

        var error = store.SetServiceUrl(value);

        Assert.NotNull(error);
        Assert.Equal("https://kept.example.test", store.Current.ServiceUrl);
    }

    [Fact]
    public void Saving_raises_changed()
    {
        var store = new SyncServiceSettingsStore(NewSettingsPath());
        var raised = 0;
        store.Changed += () => raised++;

        store.SetServiceUrl("https://sync.example.test");

        Assert.Equal(1, raised);
    }

    private string NewSettingsPath() => Path.Combine(_root, Guid.NewGuid().ToString("N"), "sync-service.json");
}
