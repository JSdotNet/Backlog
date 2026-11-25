namespace Backlog.Infrastructure.AzureMaps.UnitTests;

/// <summary>
/// The Azure Maps key at rest (local ADR 0061 §4, local ADR 0030): entered in Settings, sealed
/// under the signed-in Windows user like the Foundry key, opened on read; the endpoint, the
/// api-versions and the tileset stay legible settings beside it.
/// </summary>
public sealed class AzureMapsSettingsStoreTests : IDisposable
{
    private readonly TemporaryDirectory _temp = new("backlog-azure-maps-settings");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void A_new_store_has_no_key_and_the_eu_endpoint()
    {
        var settings = new AzureMapsSettingsStore(NewSettingsPath()).Current;

        Assert.Null(settings.ApiKey);
        Assert.False(settings.IsConfigured);
        Assert.Equal("https://eu.atlas.microsoft.com", settings.Endpoint);
        Assert.Equal("2026-01-01", settings.GeocodingApiVersion);
        Assert.Equal("2024-04-01", settings.TileApiVersion);
        Assert.Equal("microsoft.base.road", settings.TilesetId);
    }

    [Fact]
    public void A_saved_key_is_trimmed_raises_changed_and_survives_a_restart()
    {
        var path = NewSettingsPath();
        var store = new AzureMapsSettingsStore(path);
        var changed = 0;
        store.Changed += () => changed++;

        var error = store.SetApiKey("  maps-key  ");

        Assert.Null(error);
        Assert.Equal(1, changed);
        Assert.Equal("maps-key", store.Current.ApiKey);
        Assert.True(store.Current.IsConfigured);
        Assert.Equal("maps-key", new AzureMapsSettingsStore(path).Current.ApiKey);
    }

    [Fact]
    public void Clearing_the_key_leaves_no_key_after_a_restart()
    {
        var path = NewSettingsPath();
        var store = new AzureMapsSettingsStore(path);
        Assert.Null(store.SetApiKey("maps-key"));

        Assert.Null(store.ClearApiKey());

        Assert.False(store.Current.IsConfigured);
        Assert.Null(new AzureMapsSettingsStore(path).Current.ApiKey);
    }

    [Fact]
    public void A_saved_key_is_not_in_the_file_and_the_rest_stays_legible()
    {
        if (!OperatingSystem.IsWindows()) return;

        var path = NewSettingsPath();
        Assert.Null(new AzureMapsSettingsStore(path).SetApiKey("maps-key-sealed"));

        var text = File.ReadAllText(path);
        Assert.DoesNotContain("maps-key-sealed", text, StringComparison.Ordinal);
        Assert.Contains("\"apiKey\": \"dpapi:", text, StringComparison.Ordinal);
        Assert.Contains("\"endpoint\": \"https://eu.atlas.microsoft.com\"", text, StringComparison.Ordinal);
        Assert.Equal("maps-key-sealed", new AzureMapsSettingsStore(path).Current.ApiKey);
    }

    [Fact]
    public void A_plaintext_key_is_read_and_sealed_on_first_read()
    {
        if (!OperatingSystem.IsWindows()) return;

        var path = NewSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            { "apiKey": "maps-key-plaintext", "endpoint": "https://eu.atlas.microsoft.com" }
            """);

        var store = new AzureMapsSettingsStore(path);

        Assert.Equal("maps-key-plaintext", store.Current.ApiKey);
        var text = File.ReadAllText(path);
        Assert.DoesNotContain("maps-key-plaintext", text, StringComparison.Ordinal);
        Assert.Contains("\"apiKey\": \"dpapi:", text, StringComparison.Ordinal);
        Assert.Equal("maps-key-plaintext", new AzureMapsSettingsStore(path).Current.ApiKey);
    }

    [Theory]
    [InlineData("dpapi:dGhpcyBpcyBiYXNlNjQgYnV0IG5vdCBhIERQQVBJIGVudmVsb3Bl")]
    [InlineData("dpapi:this is not base64 at all !!")]
    [InlineData("dpapi:")]
    public void A_key_this_user_cannot_open_reads_as_no_key_and_the_rest_still_loads(string stored)
    {
        if (!OperatingSystem.IsWindows()) return;

        var path = NewSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $$"""
            { "apiKey": "{{stored}}", "endpoint": "https://other.atlas.microsoft.com/", "tilesetId": "microsoft.base.darkgrey" }
            """);

        var settings = new AzureMapsSettingsStore(path).Current;

        Assert.Null(settings.ApiKey);
        Assert.False(settings.IsConfigured);
        Assert.Equal("https://other.atlas.microsoft.com", settings.Endpoint);
        Assert.Equal("microsoft.base.darkgrey", settings.TilesetId);
    }

    [Fact]
    public void A_file_that_is_not_json_reads_as_the_defaults()
    {
        var path = NewSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not json");

        var settings = new AzureMapsSettingsStore(path).Current;

        Assert.Null(settings.ApiKey);
        Assert.Equal("https://eu.atlas.microsoft.com", settings.Endpoint);
    }

    private string NewSettingsPath() => _temp.Combine(Guid.NewGuid().ToString("n"), "azure-maps.json");
}
