namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// The side pane is resized by dragging its edge, and the pointer drag lives in
/// JavaScript. The bounds and the keyboard equivalent live here so both routes
/// settle on the same clamped value.
/// </summary>
public static class SidePaneWidth
{
    public const double MinRem = 24;

    /// <summary>
    /// A safety rail, not the real ceiling: the layout caps the pane at the
    /// window width less <c>--workspace-min-width</c>, so on a wide screen the
    /// knowledge pane can be the wider of the two panes.
    /// </summary>
    public const double MaxRem = 200;

    public const double DefaultRem = 36;

    private const double StepRem = 2;

    private static double EffectiveMax(double maxRem) => Math.Clamp(maxRem, MinRem, MaxRem);

    public static double Clamp(double widthRem, double maxRem = MaxRem) =>
        Math.Clamp(widthRem, MinRem, EffectiveMax(maxRem));

    /// <summary>
    /// Applies a separator key press. Left widens the pane because the separator
    /// moves left; Home and End map to the reported aria bounds, not to the
    /// visual ones.
    /// </summary>
    public static double Adjust(double widthRem, string? key, double maxRem = MaxRem) => Clamp(key switch
    {
        "ArrowLeft" => widthRem + StepRem,
        "ArrowRight" => widthRem - StepRem,
        "PageUp" => widthRem + (StepRem * 3),
        "PageDown" => widthRem - (StepRem * 3),
        "Home" => MinRem,
        "End" => EffectiveMax(maxRem),
        _ => widthRem
    }, maxRem);
}
