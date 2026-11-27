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

    /// <summary>How wide a day is on a plain, all-quarters window. A
    /// <see cref="RoadmapWindow.IsGraduated"/> window has no single answer; ask
    /// <see cref="WeekWidthAt"/> there.</summary>
    public double DayWidthRem => QuarterWidthRem / RoadmapWindow.NominalQuarterDays;

    public double WeekWidthRem => DayWidthRem * 7;

    public double TrackWidthRem => XFor(Window.End.AddDays(1));

    /// <summary>
    /// How far in from the track's left edge a date falls.
    /// <para>
    /// On a graduated window each column has a day width of its own, so this walks
    /// the columns. A date outside the window carries on at the width of the nearest
    /// end, which is what a bar hanging off either edge needs to be measured by.
    /// </para>
    /// </summary>
    public double XFor(DateOnly date)
    {
        if (!Window.IsGraduated) return (date.DayNumber - Window.Start.DayNumber) * DayWidthRem;

        var x = 0.0;
        var columns = Window.Columns;

        if (date < columns[0].Start) return (date.DayNumber - columns[0].Start.DayNumber) * DayWidthIn(columns[0]);

        foreach (var column in columns)
        {
            var dayWidth = DayWidthIn(column);

            if (date <= column.End) return x + (date.DayNumber - column.Start.DayNumber) * dayWidth;

            x += column.TotalDays * dayWidth;
        }

        var tail = columns[^1];

        return x + (date.DayNumber - tail.End.DayNumber - 1) * DayWidthIn(tail);
    }

    /// <summary>
    /// The day drawn at a distance from the track's left edge — <see cref="XFor"/>
    /// read the other way, for a pointer that landed somewhere between the bars.
    /// Past either end of the window it carries on at the nearest end's width, as
    /// <see cref="XFor"/> does.
    /// </summary>
    public DateOnly DateAt(double xRem)
    {
        if (!Window.IsGraduated) return Window.Start.AddDays((int)Math.Floor(xRem / DayWidthRem));

        var columns = Window.Columns;

        if (xRem < 0) return columns[0].Start.AddDays((int)Math.Floor(xRem / DayWidthIn(columns[0])));

        var x = 0.0;

        foreach (var column in columns)
        {
            var dayWidth = DayWidthIn(column);
            var width = column.TotalDays * dayWidth;

            if (xRem < x + width) return column.Start.AddDays(Math.Min(column.TotalDays - 1, (int)Math.Floor((xRem - x) / dayWidth)));

            x += width;
        }

        var tail = columns[^1];

        return tail.End.AddDays(1 + (int)Math.Floor((xRem - x) / DayWidthIn(tail)));
    }

    /// <summary>How wide a span is, counting both end days.</summary>
    public double WidthFor(DateOnly start, DateOnly end) =>
        Math.Max(MinBarWidthRem, XFor(end.AddDays(1)) - XFor(start));

    /// <summary>How wide the week starting on a date is drawn — what a pointer
    /// drag from that point counts one week step in.</summary>
    public double WeekWidthAt(DateOnly date) => XFor(date.AddDays(7)) - XFor(date);

    /// <summary>
    /// How wide one whole column of a scale is drawn on a graduated window.
    /// <para>
    /// Every whole week is the same width, every whole month and every whole quarter
    /// too, whatever its number of days — a ruler whose February is narrower than
    /// its March reads as uneven, not as precise. Each tier is compressed against the
    /// one nearer today: a day of this week is an eighth of <see cref="QuarterWidthRem"/>,
    /// a week three sixteenths, a month five sixteenths and a quarter half, so there
    /// is room to read a day where the work in flight is, a week where weeks matter,
    /// and a year out still fits. All four follow <see cref="QuarterWidthRem"/>, which
    /// stays the one zoom control.
    /// </para>
    /// </summary>
    public double ColumnWidthRem(RoadmapColumnScale scale) => scale switch
    {
        RoadmapColumnScale.Day => QuarterWidthRem / 8,
        RoadmapColumnScale.Week => QuarterWidthRem * 3 / 16,
        RoadmapColumnScale.Month => QuarterWidthRem * 5 / 16,
        _ => QuarterWidthRem / 2
    };

    /// <summary>How wide one day is inside a column: its scale's width shared over
    /// the days of the whole week, month or quarter. A column clipped to part of
    /// its period is that share of the width, so its days match its neighbours'.</summary>
    public double DayWidthIn(RoadmapColumn column) => ColumnWidthRem(column.Scale) / column.NominalDays;

    public double RowTop(int rowIndex) => rowIndex * RowHeightRem;

    public double RowCenter(int rowIndex) => rowIndex * RowHeightRem + RowHeightRem / 2;

    public double TrackHeightRem(int rowCount) => rowCount * RowHeightRem;

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
    public string LinkPath(double startX, double startY, double endX, double endY) =>
        Plain(new RoadmapLinkEnds(startX, startY, endX, endY));

    /// <summary>How far apart two arrows sharing a corridor are drawn: their elbow
    /// columns, or the channels two doubling-back arrows return along. Enough to read
    /// as two lines at the heavier traced stroke, small enough that the bend still sits
    /// where the plain path puts it.</summary>
    public const double CorridorStepRem = 0.2;

    /// <summary>
    /// Every arrow's path, with arrows that would share a corridor drawn apart.
    /// <para>
    /// Two elbows whose vertical runs overlap in one column draw one line where there
    /// are two dependencies, and two detours whose return runs overlap along one
    /// channel do the same — a reader tracing either cannot tell where one ends and the
    /// other begins. So an arrow whose run would lie on an earlier arrow's is moved to
    /// the nearest free place: an elbow's column steps back toward its source, and
    /// forward a step when the source leaves no room; a detour between two rows steps
    /// alternately down and up inside the gutter, and one that returns under its own
    /// row steps down only, because up is the bar. Runs that share a column or a
    /// channel without overlapping are not in each other's way and stay put, and so
    /// does an arrow alone: each keeps exactly the path
    /// <see cref="LinkPath(double, double, double, double)"/> draws, so a plan with
    /// nothing shared looks as it always did.
    /// </para>
    /// </summary>
    /// <param name="links">The arrows' ends, in the order they are drawn: the order
    /// decides which of two arrows in each other's way keeps the plain path.</param>
    public IReadOnlyList<string> LinkPaths(IReadOnlyList<RoadmapLinkEnds> links)
    {
        ArgumentNullException.ThrowIfNull(links);

        // The runs already placed: where each lies, and the span it covers along it.
        var columns = new List<(double At, double From, double To)>();
        var channels = new List<(double At, double From, double To)>();
        var paths = new string[links.Count];

        for (var index = 0; index < links.Count; index++)
        {
            var link = links[index];

            if (IsElbow(link))
            {
                var (from, to) = Span(link.StartY, link.EndY);
                var x = Free(ColumnCandidates(link), columns, from, to);
                columns.Add((x, from, to));
                paths[index] = Elbow(link, x);
            }
            else
            {
                var (from, to) = Span(link.EndX - LinkGapRem, link.StartX + LinkGapRem);
                var y = Free(ChannelCandidates(link), channels, from, to);
                channels.Add((y, from, to));
                paths[index] = Detour(link, y);
            }
        }

        return paths;
    }

    private static (double From, double To) Span(double a, double b) => (Math.Min(a, b), Math.Max(a, b));

    /// <summary>The first candidate place no placed run that overlaps this span already
    /// holds; the plain place when every one is held, since a run must go somewhere.
    /// Overlap is strict, so two runs that only meet at an end — two arrows arriving at
    /// one bar from above and below — are not in each other's way.</summary>
    private static double Free(IEnumerable<double> candidates, List<(double At, double From, double To)> placed, double from, double to)
    {
        var list = candidates.ToList();

        foreach (var candidate in list)
        {
            var taken = placed.Any(run =>
                Math.Abs(run.At - candidate) < CorridorStepRem / 2
                && run.From < to && from < run.To);

            if (!taken) return candidate;
        }

        return list[0];
    }

    /// <summary>Where an elbow may turn, plain column first: back toward the source a
    /// step at a time while it stays clear of the source's stand-off, then forward
    /// while it stays clear of the target's — so lanes never pile up on a clamp.</summary>
    private static IEnumerable<double> ColumnCandidates(RoadmapLinkEnds link)
    {
        var plain = ElbowColumn(link);
        yield return plain;

        var least = link.StartX + LinkGapRem / 2;
        for (var x = plain - CorridorStepRem; x >= least; x -= CorridorStepRem) yield return x;

        var most = link.EndX - LinkGapRem / 2;
        for (var x = plain + CorridorStepRem; x <= most; x += CorridorStepRem) yield return x;
    }

    /// <summary>Where a detour may return, plain channel first. Between two rows it
    /// alternates below and above, inside the gutter. Under its own row it steps down
    /// only, and stops short of the next row's bar: above the plain channel is the bar
    /// the arrow left.</summary>
    private IEnumerable<double> ChannelCandidates(RoadmapLinkEnds link)
    {
        var plain = Channel(link);
        yield return plain;

        if (SharesRow(link))
        {
            var floor = link.StartY + RowHeightRem - BarHeightRem / 2;
            for (var y = plain + CorridorStepRem; y < floor; y += CorridorStepRem) yield return y;
            yield break;
        }

        var room = (RowHeightRem - BarHeightRem) / 2;
        for (var step = 1; step * CorridorStepRem <= room - CorridorStepRem / 2; step++)
        {
            yield return plain + step * CorridorStepRem;
            yield return plain - step * CorridorStepRem;
        }
    }

    private static bool IsElbow(RoadmapLinkEnds link) => link.EndX - LinkGapRem - link.StartX >= LinkGapRem;

    private bool SharesRow(RoadmapLinkEnds link) => Math.Abs(link.EndY - link.StartY) <= RowHeightRem / 2;

    private static double ElbowColumn(RoadmapLinkEnds link) =>
        Math.Max(link.StartX + LinkGapRem, link.EndX - LinkGapRem);

    // The channel is the gutter between rows when the two are on different rows, and
    // just below the bar when they share one — a return line drawn through the bar
    // itself would read as a strikethrough.
    private double Channel(RoadmapLinkEnds link) =>
        SharesRow(link)
            ? link.StartY + BarHeightRem / 2 + (RowHeightRem - BarHeightRem) / 4
            : (link.StartY + link.EndY) / 2;

    /// <summary>One arrow's plain path.</summary>
    private string Plain(RoadmapLinkEnds link) =>
        IsElbow(link) ? Elbow(link, ElbowColumn(link)) : Detour(link, Channel(link));

    private static string Elbow(RoadmapLinkEnds link, double corner) =>
        $"M {N(link.StartX)} {N(link.StartY)} H {N(corner)} V {N(link.EndY)} H {N(link.EndX)}";

    private static string Detour(RoadmapLinkEnds link, double channel) =>
        $"M {N(link.StartX)} {N(link.StartY)} H {N(link.StartX + LinkGapRem)} V {N(channel)} H {N(link.EndX - LinkGapRem)} V {N(link.EndY)} H {N(link.EndX)}";

    /// <summary>
    /// Everything a dependency chain through <paramref name="id"/> holds: every thing it
    /// waits on, however far back, every thing that waits on it, however far on, and the
    /// links on those paths. Both walks keep a visited set, so a plan whose links loop
    /// still ends. A link between two things upstream is on a path to
    /// <paramref name="id"/> and is in the chain; a link from something upstream to
    /// something downstream that bypasses it is not.
    /// </summary>
    /// <param name="id">The thing traced. Always in <see cref="RoadmapChain.Nodes"/>.</param>
    /// <param name="links">The links that can be drawn.</param>
    public static RoadmapChain Chain(string id, IEnumerable<RoadmapLink> links)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(links);

        var list = links.ToList();
        var nodes = new HashSet<string>(StringComparer.Ordinal) { id };
        var traced = new HashSet<RoadmapLink>();

        Walk(upstream: true);
        Walk(upstream: false);

        return new RoadmapChain(nodes, traced);

        void Walk(bool upstream)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { id };
            var frontier = new Queue<string>([id]);

            while (frontier.TryDequeue(out var current))
            {
                foreach (var link in list)
                {
                    var (near, far) = upstream ? (link.ToId, link.FromId) : (link.FromId, link.ToId);
                    if (!string.Equals(near, current, StringComparison.Ordinal)) continue;

                    traced.Add(link);
                    nodes.Add(far);
                    if (visited.Add(far)) frontier.Enqueue(far);
                }
            }
        }
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

/// <summary>Where an arrow leaves and meets, in the geometry's rem: the right edge
/// and centre of the thing depended on, the left edge and centre of the thing
/// waiting.</summary>
public readonly record struct RoadmapLinkEnds(double StartX, double StartY, double EndX, double EndY);

/// <summary>A dependency chain (<see cref="RoadmapGeometry.Chain"/>): the things on
/// it, the traced one included, and the links between them.</summary>
public sealed record RoadmapChain(IReadOnlySet<string> Nodes, IReadOnlySet<RoadmapLink> Links)
{
    /// <summary>Whether there is anything to trace: at least one link.</summary>
    public bool HasLinks => Links.Count > 0;
}
