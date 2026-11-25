using Backlog.Modules.Events.Abstractions;

namespace Backlog.Infrastructure.AzureMaps;

/// <summary>
/// The Events map's tiles from Azure Maps Render's Get Map Tile (local ADR 0061 §1, §4, §6):
/// 256-pixel raster road tiles from the endpoint the settings name, the EU one by default. With no
/// key there is no source, so the map shows its missing-key note and requests no tile.
/// <para>
/// The key rides in the template, because the WebView's image request cannot carry a header. The
/// template stays inside the person's own WebView: nothing here logs it, and no caller may.
/// </para>
/// </summary>
public sealed class AzureMapsTileSource(AzureMapsSettingsStore settings) : IEventMapTiles
{
    /// <summary>The copyright line the map shows at its bottom right, always visible (ADR 0061
    /// §6). Fixed rather than asked of Get Map Attribution per view, which would need the view the
    /// port does not carry.</summary>
    public const string Attribution = "© Microsoft Azure Maps and its data providers";

    private const int TileSize = 256;

    public Task<EventMapTilesDto?> GetAsync(CancellationToken cancellationToken = default)
    {
        var current = settings.Current;
        if (!current.IsConfigured) return Task.FromResult<EventMapTilesDto?>(null);

        var template =
            $"{current.Endpoint}/map/tile?api-version={Uri.EscapeDataString(current.TileApiVersion)}"
            + $"&tilesetId={Uri.EscapeDataString(current.TilesetId)}"
            + $"&zoom={{z}}&x={{x}}&y={{y}}&tileSize={TileSize}"
            + $"&subscription-key={Uri.EscapeDataString(current.ApiKey!)}";

        return Task.FromResult<EventMapTilesDto?>(new EventMapTilesDto(template, Attribution, TileSize));
    }
}
