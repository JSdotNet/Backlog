namespace Backlog.Modules.Events.Abstractions;

/// <summary>
/// Where the Events map draws its tiles from (local ADR 0061 §1, §4, §6): the tile URL template
/// and the attribution the provider requires, or none while no map key is set, which the map
/// answers with its missing-key note. Drawing the map reads this and the stored points only;
/// no address is ever sent while it draws.
/// <para>
/// A port: <c>AddEventsModule()</c> registers one that has no source, as a desktop with no map
/// key; the host replaces it with the Azure Maps adapter. The template carries the key, because
/// an image request cannot carry a header (ADR 0061 §4), so it stays inside the WebView and is
/// never logged.
/// </para>
/// </summary>
public interface IEventMapTiles
{
    /// <summary>The tile source now, or null while no map key is set.</summary>
    Task<EventMapTilesDto?> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// A raster tile source as Leaflet reads it. <paramref name="UrlTemplate"/> holds Leaflet's
/// <c>{z}</c>, <c>{x}</c> and <c>{y}</c>; <paramref name="Attribution"/> is the text the map
/// shows at its bottom right, always visible; <paramref name="TileSize"/> is in pixels.
/// </summary>
public sealed record EventMapTilesDto(string UrlTemplate, string Attribution, int TileSize = 256, int MinZoom = 1, int MaxZoom = 19);
