namespace Backlog.Modules.Roadmap.UI;

/// <summary>
/// The line a lane's pace slider runs along, in story points a week.
/// <para>
/// From the pace the lane measured over the last two weeks to the pace the reader
/// typed in the roadmap's heading, the lower of the two first: a lane that has been
/// dormant measures very little, and the reader's own figure is what they want to
/// reach back towards, so those two are the ends that mean something. When the lane
/// measured nothing, or measured exactly the typed pace, there is no line between
/// them and it runs from half the typed pace to double it.
/// </para>
/// <para>
/// Stepped a whole point at a time, so a pace set with the slider is always a whole
/// number. Both ends are moved outward onto a whole point so the thumb can rest on
/// them, and the line is always long enough to reach the pace in use. It never starts
/// below one point — a pace of zero has no length to draw — so a lane in use at less
/// than a point a week is drawn at the start of the line.
/// </para>
/// </summary>
internal readonly record struct PaceSliderRange(decimal Min, decimal Max, decimal Step)
{
    /// <summary>A whole point a week: the one figure the slider sets.</summary>
    public const decimal WholePoint = 1m;

    public static PaceSliderRange Of(decimal? twoWeek, decimal typed, decimal current)
    {
        var low = typed / 2m;
        var high = typed * 2m;

        if (twoWeek is { } measured && measured != typed)
        {
            low = Math.Min(measured, typed);
            high = Math.Max(measured, typed);
        }

        low = Math.Min(low, current);
        high = Math.Max(high, current);

        var min = Math.Max(WholePoint, Math.Floor(low));
        var max = Math.Ceiling(high);

        // One step at least, so there is a line to move along.
        if (max <= min) max = min + WholePoint;

        return new PaceSliderRange(min, max, WholePoint);
    }
}
