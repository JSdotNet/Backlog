namespace Backlog.UI.Components.Diagrams;

/// <summary>
/// Where a point stands on a <see cref="TileMap"/>, worked out in C# so the pins,
/// the edge pills and a host's "is it in view" all have one answer that a test can
/// read without a browser.
///
/// <para>The projection is Leaflet's own: spherical Web Mercator (EPSG:3857) on
/// 256-pixel tiles, latitude clamped at Leaflet's 85.0511287798°, longitude not
/// wrapped. A pin placed here therefore lands on the pixel Leaflet's
/// <c>latLngToContainerPoint</c> would give for the viewport it reported.</para>
/// </summary>
public static class TileMapProjection
{
    /// <summary>Leaflet's tile size, which fixes the scale of zoom 0.</summary>
    public const double TileSize = 256;

    /// <summary>Leaflet's <c>SphericalMercator.MAX_LATITUDE</c>: past it the
    /// projection runs to infinity.</summary>
    public const double MaxLatitude = 85.0511287798;

    /// <summary>The point's position on the map, in CSS pixels from its top-left
    /// corner. Off the map is a position outside <c>0..Width</c> or
    /// <c>0..Height</c>, never a refusal.</summary>
    public static TileMapScreenPoint Project(TileMapViewport viewport, MapCoordinate point)
    {
        ArgumentNullException.ThrowIfNull(viewport);

        var scale = TileSize * Math.Pow(2, viewport.Zoom);
        var (pointX, pointY) = World(point, scale);
        var (centerX, centerY) = World(viewport.Center, scale);

        return new TileMapScreenPoint(
            pointX - centerX + (viewport.Width / 2),
            pointY - centerY + (viewport.Height / 2));
    }

    /// <summary>Whether the point is on the map as drawn, its edges included.
    /// A viewport with no size yet holds nothing.</summary>
    public static bool IsInView(TileMapViewport viewport, MapCoordinate point)
    {
        ArgumentNullException.ThrowIfNull(viewport);

        if (viewport.Width <= 0 || viewport.Height <= 0) return false;

        var position = Project(viewport, point);
        return position.X >= 0 && position.X <= viewport.Width
            && position.Y >= 0 && position.Y <= viewport.Height;
    }

    /// <summary>
    /// Where the pill for an out-of-view point goes: on the ray from the map's
    /// centre towards the point, where it crosses the map's edge drawn
    /// <paramref name="inset"/> pixels inside, so the whole pill stays on the map.
    /// The edge is the one that ray crosses, which is the nearest edge in the
    /// point's direction — a point due north is on the top edge however far away.
    /// Null for a point in view, and for a viewport too small to hold the inset.
    /// </summary>
    public static TileMapEdgePlacement? EdgePlacement(TileMapViewport viewport, MapCoordinate point, double inset = 0)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentOutOfRangeException.ThrowIfNegative(inset);

        if (IsInView(viewport, point)) return null;

        var halfWidth = (viewport.Width / 2) - inset;
        var halfHeight = (viewport.Height / 2) - inset;
        if (halfWidth <= 0 || halfHeight <= 0) return null;

        var position = Project(viewport, point);
        var dx = position.X - (viewport.Width / 2);
        var dy = position.Y - (viewport.Height / 2);

        var alongX = dx == 0 ? double.PositiveInfinity : halfWidth / Math.Abs(dx);
        var alongY = dy == 0 ? double.PositiveInfinity : halfHeight / Math.Abs(dy);
        var along = Math.Min(alongX, alongY);

        var edge = alongX < alongY
            ? (dx < 0 ? TileMapEdge.Left : TileMapEdge.Right)
            : (dy < 0 ? TileMapEdge.Top : TileMapEdge.Bottom);

        return new TileMapEdgePlacement(
            edge,
            new TileMapScreenPoint((viewport.Width / 2) + (dx * along), (viewport.Height / 2) + (dy * along)));
    }

    /// <summary>The smallest box holding every point, or null for none. A box
    /// across the antimeridian is not looked for: the points are framed the long
    /// way round, the way Leaflet's own <c>fitBounds</c> would.</summary>
    public static MapBounds? Bounds(IEnumerable<MapCoordinate> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        MapBounds? bounds = null;
        foreach (var point in points)
        {
            bounds = bounds is { } box
                ? new MapBounds(
                    Math.Min(box.South, point.Latitude),
                    Math.Min(box.West, point.Longitude),
                    Math.Max(box.North, point.Latitude),
                    Math.Max(box.East, point.Longitude))
                : new MapBounds(point.Latitude, point.Longitude, point.Latitude, point.Longitude);
        }

        return bounds;
    }

    private static (double X, double Y) World(MapCoordinate point, double scale)
    {
        var latitude = Math.Clamp(point.Latitude, -MaxLatitude, MaxLatitude) * Math.PI / 180;
        var x = (point.Longitude + 180) / 360 * scale;
        var y = (0.5 - (Math.Log(Math.Tan((Math.PI / 4) + (latitude / 2))) / (2 * Math.PI))) * scale;
        return (x, y);
    }
}
