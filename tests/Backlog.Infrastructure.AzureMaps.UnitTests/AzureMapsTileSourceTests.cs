namespace Backlog.Infrastructure.AzureMaps.UnitTests;

/// <summary>
/// Where the Events map's tiles come from (local ADR 0061 §1, §4, §6): with no key, none, so the
/// map shows its missing-key note and requests nothing; with a key, Render's Get Map Tile on the
/// EU endpoint as 256-pixel raster road tiles, the key in the URL because an image request cannot
/// carry a header, and the attribution the provider requires.
/// </summary>
public sealed class AzureMapsTileSourceTests : IDisposable
{
    private readonly TemporaryDirectory _temp = new("backlog-azure-maps-tiles");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task With_no_key_there_is_no_tile_source()
    {
        var tiles = await new AzureMapsTileSource(Store()).GetAsync(TestContext.Current.CancellationToken);

        Assert.Null(tiles);
    }

    [Fact]
    public async Task With_a_key_the_tiles_are_eu_raster_road_tiles_carrying_the_key()
    {
        var store = Store();
        Assert.Null(store.SetApiKey("maps key/+="));

        var tiles = await new AzureMapsTileSource(store).GetAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(tiles);
        Assert.Equal(
            "https://eu.atlas.microsoft.com/map/tile?api-version=2024-04-01&tilesetId=microsoft.base.road"
            + "&zoom={z}&x={x}&y={y}&tileSize=256&subscription-key=maps%20key%2F%2B%3D",
            tiles.UrlTemplate);
        Assert.Equal(256, tiles.TileSize);
        Assert.Equal(AzureMapsTileSource.Attribution, tiles.Attribution);
        Assert.Contains("Microsoft", tiles.Attribution, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_key_saved_or_cleared_later_is_read_on_the_next_ask()
    {
        var store = Store();
        var source = new AzureMapsTileSource(store);

        Assert.Null(store.SetApiKey("maps-key"));
        Assert.NotNull(await source.GetAsync(TestContext.Current.CancellationToken));

        Assert.Null(store.ClearApiKey());
        Assert.Null(await source.GetAsync(TestContext.Current.CancellationToken));
    }

    private AzureMapsSettingsStore Store() =>
        new(_temp.Combine(Guid.NewGuid().ToString("n"), "azure-maps.json"));
}
