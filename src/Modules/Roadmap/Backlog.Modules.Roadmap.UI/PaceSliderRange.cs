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
/// Both ends are moved outward onto a step so the thumb can rest on them, and the
/// line is always long enough to hold the pace in use, so the thumb is never drawn
/// somewhere the lane is not. The ends never go below one step — a pace of zero has
/// no length to draw — unless the pace in use is itself lower, which then is the end.
/// </para>
/// </summary>
internal readonly record struct PaceSliderRange(decimal Min, decimal Max, decimal Step)
{
    /// <summary>A quarter of a point a week: fine enough to place a lane by, coarse
    /// enough to land on with a thumb.</summary>
    public const decimal QuarterPoint = 0.25m;

    public static PaceSliderRange Of(decimal? twoWeek, decimal typed, decimal current)
    {
        var low = typed / 2m;
        var high = typed * 2m;

        if (twoWeek is { } measured && measured != typed)
        {
            low = Math.Min(measured, typed);
            high = Math.Max(measured, typed);
        }

        high = Math.Max(high, current);

        // The floor only applies to the ends the lane and the reader chose. A pace in
        // use below it — the 8-week stretch of a quiet lane can measure an eighth of a
        // point — still has to be on the line, so it is the end itself then.
        var min = Math.Max(QuarterPoint, Math.Floor(low / QuarterPoint) * QuarterPoint);
        if (current < min) min = current;
        var max = Math.Ceiling(high / QuarterPoint) * QuarterPoint;

        // One step at least, so there is a line to move along.
        if (max <= min) max = min + QuarterPoint;

        return new PaceSliderRange(min, max, QuarterPoint);
    }
}
