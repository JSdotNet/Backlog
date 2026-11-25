namespace Backlog.UI.Components.Diagrams;

/// <summary>A place on the globe in degrees, the way a tile map is told one.
/// Longitude is not wrapped: a view panned past the antimeridian reports a centre
/// beyond 180, and a point is projected where Leaflet would draw it.</summary>
public readonly record struct MapCoordinate(double Latitude, double Longitude);

/// <summary>How a mark is drawn. Three shapes, so the tone is never the only cue
/// (<c>.devbook/design/color-scheme.md#events-map-marks</c>): a fill, a dashed edge,
/// and a grey ring. The words for what each means are the host's, in its legend.</summary>
public enum TileMapMarkTone
{
    /// <summary>A <c>color-primary</c> fill with the number in <c>color-text-inverse</c>.</summary>
    Filled,

    /// <summary>No fill inside a dashed <c>color-primary</c> edge.</summary>
    Dashed,

    /// <summary>No fill inside a solid <c>color-border-strong</c> ring.</summary>
    Muted
}

/// <summary>
/// One mark a <see cref="TileMap"/> draws as a pin over its tiles, and — when it
/// carries a <see cref="Label"/> and lies outside the view — as a pill on the map's
/// nearest edge.
/// </summary>
/// <param name="Id">The host's id, handed back when the mark is picked.</param>
/// <param name="Point">Where the mark stands.</param>
/// <param name="AccessibleName">The pin's and the pill's whole name. Every fact the
/// mark draws must be in it, because a pin is too small to say anything else.</param>
/// <param name="Tooltip">The short line shown while the pin is hovered or focused.</param>
/// <param name="Label">The few characters drawn inside the pin, a number usually.
/// Null draws an empty pin, and an unlabelled mark gets no edge pill.</param>
/// <param name="Tone">How the pin is drawn.</param>
public sealed record TileMapMark(
    string Id,
    MapCoordinate Point,
    string AccessibleName,
    string Tooltip,
    string? Label = null,
    TileMapMarkTone Tone = TileMapMarkTone.Filled);

/// <summary>How far in from each side of the map its own chrome reaches, in CSS pixels:
/// the zoom controls' column on the right, the legend and attribution along the bottom.
/// The tiles under it are covered, so nothing to pick is placed there.</summary>
public readonly record struct TileMapInsets(double Top, double Right, double Bottom, double Left);

/// <summary>What a tile map shows: its centre, its zoom, and its size in CSS
/// pixels, as Leaflet reports them — and how much of it the map's chrome covers,
/// which <see cref="TileMapProjection.IsInView"/> leaves out.</summary>
public sealed record TileMapViewport(MapCoordinate Center, double Zoom, double Width, double Height, TileMapInsets Reserved = default);

/// <summary>The stacking ranks a tile map writes on its numbered marks: the lowest number
/// highest, so a nearby pin 2 never covers pin 1. Capped below the selected and focused
/// layers in <c>components.css</c>, which sit above every rank.</summary>
internal static class TileMapRanks
{
    /// <summary>The highest rank written, the first numbered mark's; the stylesheet's
    /// selected layer is above it.</summary>
    public const int Highest = 9000;
}

/// <summary>A centre and a zoom: where a map opens when the host says so.</summary>
public sealed record TileMapView(MapCoordinate Center, double Zoom);

/// <summary>Which edge of the map an edge pill sits on.</summary>
public enum TileMapEdge
{
    Top,
    Right,
    Bottom,
    Left
}

/// <summary>A position on the map, in CSS pixels from its top-left corner.</summary>
public readonly record struct TileMapScreenPoint(double X, double Y);

/// <summary>Where an out-of-view mark's pill goes: the edge it sits on and its
/// position along it, already inside the map by the inset it was asked for.</summary>
public readonly record struct TileMapEdgePlacement(TileMapEdge Edge, TileMapScreenPoint Position);

/// <summary>The box a fit frames, in degrees.</summary>
public readonly record struct MapBounds(double South, double West, double North, double East);
