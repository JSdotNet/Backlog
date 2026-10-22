namespace Backlog.UI.Components.Diagrams;

/// <summary>
/// Where an atlas renderer's camera is, as it reports it to
/// <see cref="GraphAtlas.OnCameraMoved"/>: the turn across, the tilt, and the
/// zoom, 1 being the layout's own fit.
/// </summary>
/// <param name="Yaw">The turn about the vertical axis, in radians.</param>
/// <param name="Pitch">The tilt up or down, in radians.</param>
/// <param name="Zoom">The zoom factor; 1 is the default view.</param>
public sealed record GraphAtlasCamera(double Yaw, double Pitch, double Zoom)
{
    /// <summary>The Lobes atlas's default view, which a new layout starts from —
    /// what the foot shows until the renderer's first report. The renderer keeps
    /// its own copy (<c>backlogLobesRender</c> in components.js) and reports it
    /// on its first frame, so a drift between the two lasts one frame.</summary>
    public static GraphAtlasCamera LobesDefault { get; } = new(-0.7, -0.2, 1);

    /// <summary>The zoom as a whole percentage, as the atlas's hint prints it.</summary>
    public int ZoomPercent => (int)Math.Round(Zoom * 100, MidpointRounding.AwayFromZero);
}
