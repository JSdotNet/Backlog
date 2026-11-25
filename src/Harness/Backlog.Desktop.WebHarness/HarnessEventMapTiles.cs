using System.Globalization;
using System.Text;
using Backlog.Modules.Events.Abstractions;

namespace Backlog.Desktop.WebHarness;

/// <summary>
/// The Events map's tiles from this harness's own origin: a stand-in tile for every z/x/y, so
/// the map draws, pans and zooms with no Azure Maps key and no network (local ADR 0061). The
/// harness registers it beside <see cref="HarnessAddressGeocoder"/>, under the same switch, and
/// serves <see cref="RoutePattern"/>.
/// <para>
/// A grid labelled with its own z/x/y, not a map, so nobody mistakes it for one. The pins still
/// sit where their stored points are, so their places relative to each other read true, and a
/// pan shows the labels move.
/// </para>
/// </summary>
public sealed class HarnessEventMapTiles : IEventMapTiles
{
    /// <summary>The route the tiles are served on.</summary>
    public const string RoutePattern = "/harness/map-tiles/{z:int}/{x:int}/{y:int}.svg";

    /// <summary>The template the map reads, Leaflet's placeholders in the route above.</summary>
    public const string UrlTemplate = "/harness/map-tiles/{z}/{x}/{y}.svg";

    /// <summary>What the map's attribution says while it draws these tiles.</summary>
    public const string Attribution = "Harness stand-in tiles: no map data";

    /// <summary>The tiles' content type.</summary>
    public const string ContentType = "image/svg+xml";

    private static readonly EventMapTilesDto Source = new(UrlTemplate, Attribution, TileSize: 256, MinZoom: 2, MaxZoom: 18);

    public Task<EventMapTilesDto?> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<EventMapTilesDto?>(Source);

    /// <summary>The tile at <paramref name="z"/>/<paramref name="x"/>/<paramref name="y"/>.</summary>
    public static byte[] Tile(int z, int x, int y)
    {
        var label = string.Create(CultureInfo.InvariantCulture, $"{z}/{x}/{y}");

        return Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"256\" height=\"256\" viewBox=\"0 0 256 256\">"
            + "<rect width=\"256\" height=\"256\" fill=\"#2a2a2e\"/>"
            + "<path d=\"M0 64H256M0 128H256M0 192H256M64 0V256M128 0V256M192 0V256\" stroke=\"#3a3a3f\" stroke-width=\"1\"/>"
            + "<path d=\"M0 0.5H256M0.5 0V256\" stroke=\"#55555b\" stroke-width=\"1\"/>"
            + $"<text x=\"8\" y=\"20\" fill=\"#77777f\" font-family=\"sans-serif\" font-size=\"12\">{label}</text>"
            + "</svg>");
    }
}
