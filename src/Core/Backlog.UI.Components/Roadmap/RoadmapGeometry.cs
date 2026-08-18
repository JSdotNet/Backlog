using System.Globalization;

namespace Backlog.UI.Components.Roadmap;

/// <summary>
/// Where everything goes, in rem.
/// <para>
/// One measuring system for the whole chart, and rem rather than pixels or
/// percentages so the timeline grows with the reader's font size instead of
/// staying the size someone else's browser was. The links overlay is an SVG
/// whose viewBox is stated in these same units, which is why an arrow lands on
/// a bar's edge exactly rather than nearly: both are the same arithmetic.
/// </para>
/// <para>
/// This type renders nothing and holds no state. It is here so the positioning
/// can be tested as arithmetic — a date in, a measurement out — rather than by
/// reading numbers back out of a rendered stylesheet.
/// </para>
/// </summary>
/// <param name="Window">The stretch of time being drawn.</param>
/// <param name="QuarterWidthRem">How wide one quarter is. The zoom control, in
/// effect: everything horizontal is derived from it.</param>
/// <param name="RowHeightRem">How tall one row is. The default is the 44px an
/// interactive target is required to be, because a bar's hit area is the row it
/// sits in — a shorter row would mean a bar nobody with unsteady hands can
/// reliably hit.</param>
/// <param name="BarHeightRem">How tall a bar is inside its row. The difference
/// between the two is the gutter the dependency arrows are routed through.</param>
public sealed record RoadmapGeometry(
    RoadmapWindow Window,
    double QuarterWidthRem = 16,
    double RowHeightRem = 2.75,
    double BarHeightRem = 1.5)
{
    /// <summary>How narrow a bar is allowed to get.
    /// <para>
    /// Not the 44px an interactive target is required to be: a bar's width is a
    /// duration a reader measures by eye, and padding a three-day item out to
    /// forty-four pixels would draw it as a fortnight. The target size is met
    /// instead by an invisible hit area around the bar, which costs the reader
    /// nothing and tells them nothing untrue.
    /// </para></summary>
    public const double MinBarWidthRem = 0.5;

    /// <summary>How far an arrow stands off the bar it leaves or meets, so a
    /// link between two touching bars is still a line rather than a corner.</summary>
    public const double LinkGapRem = 0.5;

    public double DayWidthRem => QuarterWidthRem / RoadmapWindow.NominalQuarterDays;

    public double WeekWidthRem => DayWidthRem * 7;

    public double TrackWidthRem => Window.TotalDays * DayWidthRem;

    /// <summary>How far in from the track's left edge a date falls.</summary>
    public double XFor(DateOnly date) => (date.DayNumber - Window.Start.DayNumber) * DayWidthRem;

    /// <summary>How wide a span is, counting both end days.</summary>
    public double WidthFor(DateOnly start, DateOnly end) =>
        Math.Max(MinBarWidthRem, (end.DayNumber - start.DayNumber + 1) * DayWidthRem);

    public double RowTop(int rowIndex) => rowIndex * RowHeightRem;

    public double RowCenter(int rowIndex) => rowIndex * RowHeightRem + RowHeightRem / 2;

    /// <summary>Where a bar's top edge sits inside its row, centred.</summary>
    public double BarTop(int rowIndex) => RowTop(rowIndex) + (RowHeightRem - BarHeightRem) / 2;

    public double TrackHeightRem(int rowCount) => rowCount * RowHeightRem;

    /// <summary>
    /// How many whole weeks a horizontal drag of <paramref name="deltaRem"/>
    /// amounts to. The browser reports pixels; this is the only place they stop
    /// being pixels.
    /// </summary>
    public int WeekStepsFor(double deltaRem) =>
        (int)Math.Round(deltaRem / WeekWidthRem, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The dependency arrow between two points, as an SVG path.
    /// <para>
    /// Two shapes, and which one is used says something. When the target starts
    /// after the source ends there is room for a plain elbow, and the arrow runs
    /// forward. When it does not — the dependent work is already under way, or
    /// starts first — the arrow has to double back, and the detour through the
    /// gutter is the reader's cue that the plan contains that contradiction.
    /// Drawing both as a straight line would hide it.
    /// </para>
    /// </summary>
    /// <param name="startX">Right edge of the thing depended on.</param>
    /// <param name="startY">Its vertical centre.</param>
    /// <param name="endX">Left edge of the thing waiting.</param>
    /// <param name="endY">Its vertical centre.</param>
    public string LinkPath(double startX, double startY, double endX, double endY)
    {
        var head = endX - LinkGapRem;

        if (head - startX >= LinkGapRem)
        {
            var corner = Math.Max(startX + LinkGapRem, head);

            return $"M {N(startX)} {N(startY)} H {N(corner)} V {N(endY)} H {N(endX)}";
        }

        // Doubling back. The channel is the gutter between rows when the two are
        // on different rows, and just below the bar when they share one — a
        // return line drawn through the bar itself would read as a strikethrough.
        var channel = Math.Abs(endY - startY) > RowHeightRem / 2
            ? (startY + endY) / 2
            : startY + BarHeightRem / 2 + (RowHeightRem - BarHeightRem) / 4;

        var out_ = startX + LinkGapRem;
        var back = endX - LinkGapRem;

        return $"M {N(startX)} {N(startY)} H {N(out_)} V {N(channel)} H {N(back)} V {N(endY)} H {N(endX)}";
    }

    /// <summary>
    /// A number as CSS and SVG will accept it.
    /// <para>
    /// Invariant, always. A machine set to a locale that writes decimals with a
    /// comma would otherwise emit <c>left: 12,5rem</c>, which is not a length —
    /// the whole chart would collapse to the left edge, on that machine only,
    /// and nowhere else.
    /// </para>
    /// </summary>
    public static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>One dependency arrow, resolved to where its two ends are drawn.</summary>
/// <param name="FromId">The thing depended on.</param>
/// <param name="ToId">The thing waiting.</param>
/// <param name="Path">The SVG path, in the geometry's rem units.</param>
/// <param name="Label">What the arrow means, for anything that cannot see it.</param>
public sealed record RoadmapArrow(string FromId, string ToId, string Path, string Label);
