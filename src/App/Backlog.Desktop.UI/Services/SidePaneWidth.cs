namespace Backlog.Desktop.UI.Services;

/// <summary>
/// The side pane is resized by dragging its edge, and the pointer drag lives in
/// JavaScript. The bounds and the keyboard equivalent live here so both routes
/// settle on the same clamped value.
/// </summary>
public static class SidePaneWidth
{
    public const double MinRem = 24;
    public const double MaxRem = 54;
    public const double DefaultRem = 36;

    private const double StepRem = 2;

    public static double Clamp(double widthRem) => Math.Clamp(widthRem, MinRem, MaxRem);

    /// <summary>
    /// Applies a separator key press. Left widens the pane because the separator
    /// moves left; Home and End map to the reported aria bounds, not to the
    /// visual ones.
    /// </summary>
    public static double Adjust(double widthRem, string? key) => Clamp(key switch
    {
        "ArrowLeft" => widthRem + StepRem,
        "ArrowRight" => widthRem - StepRem,
        "PageUp" => widthRem + (StepRem * 3),
        "PageDown" => widthRem - (StepRem * 3),
        "Home" => MinRem,
        "End" => MaxRem,
        _ => widthRem
    });
}
