using global::Backlog.UI.Components.Diagrams;

namespace Backlog.Modules.Events.UI;

/// <summary>
/// The Events map's view as the person left it: shown or hidden, its height against the list,
/// and where it was panned and zoomed. One per window, for the session only — nothing stores it
/// (<c>.devbook/domain/events/features.md#events-map</c>), so the pane opened again, after
/// Settings say, finds the map as it was.
/// </summary>
public sealed class EventsMapView
{
    /// <summary>The map's height when the pane first opens, in rem.</summary>
    public const double DefaultHeightRem = 16;

    /// <summary>Whether the map shows above the list.</summary>
    public bool Shown { get; set; } = true;

    /// <summary>The map's height, in rem.</summary>
    public double HeightRem { get; set; } = DefaultHeightRem;

    /// <summary>Where the map was last panned and zoomed to, or null before it settled once.</summary>
    public TileMapView? View { get; set; }
}
