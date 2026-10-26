namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The Cohesion graph laid out: the <see cref="AiModel"/> turned into node boxes
/// and orthogonal edge polylines, the way the design's Cohesion board places
/// them. Pure geometry — no UI reads it but the view that draws it.
/// <para>
/// The seven stages are columns, the pull request sits in the gutter between
/// Verify and Integrate, and the three layers are bands below the path. A node
/// sits where the adoption view puts its card (<c>devbook-ai.md</c>, "The loop
/// picture"): at the first stage its <c>stage</c> lists, else in the band of its
/// first <c>layer</c> — there in the column nearest the usages it is linked to. Usages are ordered within a column to
/// shorten their edges.
/// </para>
/// <para>
/// Every node sits on one grid, so the gutters between columns and between rows
/// are free of nodes: an edge leaves a node's side, runs through those gutters
/// and enters the other node's side. Parallel runs in one gutter get distinct
/// offsets, and no segment crosses a node's box.
/// </para>
/// </summary>
/// <param name="Width">The whole graph's width.</param>
/// <param name="Height">The whole graph's height, the last band included.</param>
/// <param name="LaneHeight">The height of the path's seven lanes.</param>
/// <param name="PullRequestX">Where the pull request's marker stands: the middle
/// of the gutter between Verify and Integrate.</param>
/// <param name="Columns">The seven stages' lanes, in path order.</param>
/// <param name="Bands">The three layers' bands, top to bottom.</param>
/// <param name="Nodes">One box per placed usage, in the model's reading order.</param>
/// <param name="Edges">The <c>feeds</c> edges, then the <c>related</c> ones.</param>
public sealed record AiCohesionLayout(
    double Width,
    double Height,
    double LaneHeight,
    double PullRequestX,
    IReadOnlyList<AiCohesionColumn> Columns,
    IReadOnlyList<AiCohesionBand> Bands,
    IReadOnlyList<AiCohesionNode> Nodes,
    IReadOnlyList<AiCohesionEdge> Edges)
{
    public const double NodeWidth = 196;
    public const double NodeHeight = 66;
    public const double ColumnGap = 30;
    public const double RowGap = 22;
    public const double Margin = 40;
    public const double LaneHeaderHeight = 46;
    public const double PathTop = 100;
    public const double LanePadding = 10;
    public const double BandGap = 12;
    public const double BandHeaderHeight = 26;

    /// <summary>The largest distance between two runs sharing a gutter.</summary>
    public const double TrackSpacing = 8;

    /// <summary>The largest distance between two edges leaving one side of a node.</summary>
    public const double PortSpacing = 12;

    /// <summary>The distance between two straight edges joining the same two nodes.</summary>
    public const double StraightSpacing = 16;

    private const double TrackInset = 3;
    private const int OrderingSweeps = 4;
    private const double PortInset = 10;
    private const string OperateStage = "operate";
    private const string Candidate = "candidate";

    public static double ColumnX(int column) => Margin + column * (NodeWidth + ColumnGap);

    public static AiCohesionLayout From(AiModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.Stages.Count == 0) throw new ArgumentException("The model has no stages to lay out.", nameof(model));
        return new Builder(model).Build();
    }

    private sealed class Builder(AiModel model)
    {
        private readonly int columns = model.Stages.Count;
        private readonly Dictionary<AiModelUsage, Slot> slots = new(ReferenceEqualityComparer.Instance);
        private readonly List<double> rowTops = [];

        /// <summary>Per row, where the free gap above it ends: the row's top, or
        /// for a band's first row the band's top, so no run crosses its label.</summary>
        private readonly List<double> gapEnds = [];
        private readonly List<AiCohesionBand> bands = [];
        private double laneHeight;
        private double height;

        public AiCohesionLayout Build()
        {
            var links = Links();
            var neighbours = Neighbours(links);

            PlaceOnThePath();
            PlaceInTheBands(neighbours);
            OrderWithinColumns(neighbours);
            Measure();

            var nodes = model.Usages
                .Where(slots.ContainsKey)
                .Select(usage =>
                {
                    var slot = slots[usage];
                    return new AiCohesionNode(usage, slot.Column, slot.Layer, slot.Row, ColumnX(slot.Column), rowTops[slot.Row], NodeWidth, NodeHeight);
                })
                .ToList();
            var byUsage = nodes.ToDictionary(node => node.Usage, (IEqualityComparer<AiModelUsage>)ReferenceEqualityComparer.Instance);

            var edges = new Router(this, byUsage, columns).Route(links);

            var stageColumns = model.Stages
                .Select((stage, index) => new AiCohesionColumn(stage, ColumnX(index) - ColumnGap / 2, NodeWidth + ColumnGap, stage.BeforePullRequest))
                .ToList();
            var width = 2 * Margin + columns * NodeWidth + (columns - 1) * ColumnGap;
            var pullRequestX = (ColumnX(AiModel.StagesBeforePullRequest - 1) + NodeWidth + ColumnX(AiModel.StagesBeforePullRequest)) / 2;

            return new AiCohesionLayout(width, height, laneHeight, pullRequestX, stageColumns, bands, nodes, edges);
        }

        public double RowTop(int row) => rowTops[row];

        /// <summary>The channel above row <paramref name="channel"/> — the gutter
        /// between it and the row before — as the y range free of nodes.</summary>
        public (double Low, double High) Channel(int channel)
        {
            // A route only ever takes the channel above a row, never the one
            // below the last: channel is always a row index.
            if (channel == 0) return (LaneHeaderHeight, gapEnds[0]);
            return (rowTops[channel - 1] + NodeHeight, gapEnds[channel]);
        }

        private List<Link> Links()
        {
            var placed = model.Usages.Where(IsPlaced).ToHashSet(ReferenceEqualityComparer.Instance);
            var links = new List<Link>();

            foreach (var feed in model.Feeds)
            {
                if (feed.To is not { } to || ReferenceEquals(feed.From, to) || !placed.Contains(feed.From) || !placed.Contains(to)) continue;

                var dashed = IsCandidate(feed.From) || IsCandidate(to);
                var openLoop = IsCandidate(feed.From) && feed.From.Stages.Contains(OperateStage, StringComparer.Ordinal);
                links.Add(new Link(openLoop ? AiCohesionEdgeKind.OpenLoop : AiCohesionEdgeKind.Feed, feed.From, to, feed.Label, dashed));
            }

            foreach (var support in model.Supports)
            {
                if (support.To is not { } to || ReferenceEquals(support.From, to) || !placed.Contains(support.From) || !placed.Contains(to)) continue;
                links.Add(new Link(AiCohesionEdgeKind.Support, support.From, to, null, false));
            }

            return links;
        }

        /// <summary>Whether a usage gets a box: the same test both placements
        /// make — a stage puts it on the path, a layer alone in a band.</summary>
        private static bool IsPlaced(AiModelUsage usage) => usage.Stages.Count > 0 || usage.Layers.Count > 0;

        private static bool IsCandidate(AiModelUsage usage) =>
            string.Equals(usage.Status, Candidate, StringComparison.OrdinalIgnoreCase);

        private static Dictionary<AiModelUsage, List<AiModelUsage>> Neighbours(IEnumerable<Link> links)
        {
            var map = new Dictionary<AiModelUsage, List<AiModelUsage>>(ReferenceEqualityComparer.Instance);
            foreach (var link in links)
            {
                Add(link.From, link.To);
                Add(link.To, link.From);
            }

            return map;

            void Add(AiModelUsage from, AiModelUsage to)
            {
                if (!map.TryGetValue(from, out var list)) map[from] = list = [];
                list.Add(to);
            }
        }

        private void PlaceOnThePath()
        {
            var keys = model.Stages.Select(stage => stage.Key).ToList();
            var rows = new int[columns];
            foreach (var usage in model.Usages.Where(usage => IsPlaced(usage) && usage.Stages.Count > 0))
            {
                var column = keys.IndexOf(usage.Stages[0]);
                if (column < 0) continue;
                slots[usage] = new Slot(column, null, rows[column]++);
            }
        }

        private int PathRows => Math.Max(1, slots.Values.Where(slot => slot.Layer is null).Select(slot => slot.Row + 1).DefaultIfEmpty(0).Max());

        /// <summary>A layer-only usage takes the free column nearest the mean
        /// column of the usages it links to — linked ones first — and a band grows
        /// a row only when all seven are taken.</summary>
        private void PlaceInTheBands(Dictionary<AiModelUsage, List<AiModelUsage>> neighbours)
        {
            var rowBase = PathRows;
            foreach (var layer in model.Layers)
            {
                var members = model.Usages
                    .Where(usage => IsPlaced(usage) && usage.Stages.Count == 0 && FirstLayer(usage) == layer.Key)
                    .ToList();

                var taken = new List<bool[]> { new bool[columns] };
                var pending = members.ToList();
                while (pending.Count > 0)
                {
                    var next = pending
                        .Select((usage, order) => (usage, order, wanted: Wanted(usage)))
                        .OrderBy(entry => entry.wanted is null ? 1 : 0)
                        .ThenBy(entry => entry.order)
                        .First();
                    pending.Remove(next.usage);

                    var (row, column) = FreeCell(taken, next.wanted ?? 0);
                    taken[row][column] = true;
                    slots[next.usage] = new Slot(column, layer.Key, rowBase + row);
                }

                rowBase += taken.Count;
                bands.Add(new AiCohesionBand(layer, 0, 0, taken.Count));
            }

            double? Wanted(AiModelUsage usage)
            {
                var placed = neighbours.GetValueOrDefault(usage, [])
                    .Where(slots.ContainsKey)
                    .Select(other => (double)slots[other].Column)
                    .ToList();
                return placed.Count == 0 ? null : placed.Average();
            }
        }

        private static string? FirstLayer(AiModelUsage usage) => usage.Layers.Count > 0 ? usage.Layers[0] : null;

        private (int Row, int Column) FreeCell(List<bool[]> taken, double wanted)
        {
            for (var row = 0; ; row++)
            {
                if (row == taken.Count) taken.Add(new bool[columns]);
                var free = Enumerable.Range(0, columns).Where(column => !taken[row][column]).ToList();
                if (free.Count == 0) continue;
                return (row, free.OrderBy(column => Math.Abs(column - wanted)).ThenBy(column => column).First());
            }
        }

        /// <summary>The barycentre heuristic: a few sweeps that sort each path
        /// column by the mean row of what its usages link to. A usage linked to
        /// nothing is sorted by its current row instead, so it stays among the
        /// rows around it.</summary>
        private void OrderWithinColumns(Dictionary<AiModelUsage, List<AiModelUsage>> neighbours)
        {
            for (var sweep = 0; sweep < OrderingSweeps; sweep++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var members = slots
                        .Where(pair => pair.Value.Layer is null && pair.Value.Column == column)
                        .OrderBy(pair => pair.Value.Row)
                        .Select(pair => pair.Key)
                        .ToList();

                    var ordered = members
                        .Select((usage, index) => (usage, index, key: Barycentre(usage) ?? index))
                        .OrderBy(entry => entry.key)
                        .ThenBy(entry => entry.index)
                        .Select(entry => entry.usage)
                        .ToList();

                    for (var row = 0; row < ordered.Count; row++)
                    {
                        slots[ordered[row]] = slots[ordered[row]] with { Row = row };
                    }
                }
            }

            double? Barycentre(AiModelUsage usage)
            {
                var rows = neighbours.GetValueOrDefault(usage, [])
                    .Where(slots.ContainsKey)
                    .Select(other => (double)slots[other].Row)
                    .ToList();
                return rows.Count == 0 ? null : rows.Average();
            }
        }

        private void Measure()
        {
            var pathRows = PathRows;
            for (var row = 0; row < pathRows; row++)
            {
                rowTops.Add(PathTop + row * (NodeHeight + RowGap));
                gapEnds.Add(rowTops[^1]);
            }

            laneHeight = rowTops[^1] + NodeHeight + LanePadding;

            var top = laneHeight;
            for (var index = 0; index < bands.Count; index++)
            {
                var band = bands[index];
                top += BandGap;
                for (var row = 0; row < band.Rows; row++)
                {
                    rowTops.Add(top + BandHeaderHeight + row * (NodeHeight + RowGap));
                    gapEnds.Add(row == 0 ? top : rowTops[^1]);
                }

                var bandHeight = BandHeaderHeight + band.Rows * (NodeHeight + RowGap) - RowGap + LanePadding;
                bands[index] = band with { Y = top, Height = bandHeight };
                top += bandHeight;
            }

            height = top;
        }

        private readonly record struct Slot(int Column, string? Layer, int Row);
    }

    private sealed record Link(AiCohesionEdgeKind Kind, AiModelUsage From, AiModelUsage To, string? Label, bool Dashed);

    /// <summary>Routes every link through the gutters. Ports, then channel
    /// tracks, then gutter tracks: each step's offsets are known before the next
    /// one needs them.</summary>
    private sealed class Router(Builder grid, Dictionary<AiModelUsage, AiCohesionNode> nodes, int columns)
    {
        public List<AiCohesionEdge> Route(List<Link> links)
        {
            var occupied = nodes.Values.ToDictionary(node => (node.Column, node.Row));
            var plans = links.Select((link, index) => Plan(index, link, occupied)).ToList();

            AssignStraightOffsets(plans);
            AssignPorts(plans);
            AssignChannelTracks(plans);
            AssignGutterTracks(plans);

            return [.. plans.Select(Edge)];
        }

        private Plan Plan(int index, Link link, Dictionary<(int, int), AiCohesionNode> occupied)
        {
            var a = nodes[link.From];
            var b = nodes[link.To];
            var plan = new Plan(index, link, a, b);

            if (a.Column == b.Column && Clear(a, b, occupied))
            {
                plan.Shape = Shape.Straight;
                return plan;
            }

            if (a.Column == b.Column)
            {
                // Blocked within its own column: out and back in on the right.
                plan.Shape = Shape.Jog;
                plan.FromSide = plan.ToSide = Side.Right;
                plan.FromGutter = plan.ToGutter = a.Column + 1;
                return plan;
            }

            var rightward = b.Column > a.Column;
            plan.FromSide = rightward ? Side.Right : Side.Left;
            plan.ToSide = rightward ? Side.Left : Side.Right;
            plan.FromGutter = rightward ? a.Column + 1 : a.Column;
            plan.ToGutter = rightward ? b.Column : b.Column + 1;

            if (plan.FromGutter == plan.ToGutter)
            {
                // Neighbouring columns: one run in the gutter between them.
                plan.Shape = Shape.Jog;
                return plan;
            }

            // Further apart — even along one clear row, where a straight run could
            // meet a stub of another node's port — out through the channel beside
            // the source's row.
            plan.Shape = Shape.Channel;
            plan.Channel = b.Row > a.Row ? a.Row + 1 : a.Row;
            return plan;
        }

        /// <summary>Whether no node stands between two in one column.</summary>
        private static bool Clear(AiCohesionNode a, AiCohesionNode b, Dictionary<(int, int), AiCohesionNode> occupied)
        {
            var (low, high) = (Math.Min(a.Row, b.Row), Math.Max(a.Row, b.Row));
            return Enumerable.Range(low + 1, Math.Max(0, high - low - 1)).All(row => !occupied.ContainsKey((a.Column, row)));
        }

        /// <summary>Straight edges joining the same two nodes stand side by side
        /// about the column's centre.</summary>
        private static void AssignStraightOffsets(List<Plan> plans)
        {
            foreach (var group in plans.Where(plan => plan.Shape == Shape.Straight).GroupBy(plan => PairKey(plan.A, plan.B)))
            {
                var members = group.ToList();
                for (var i = 0; i < members.Count; i++)
                {
                    members[i].StraightX = members[i].A.X + NodeWidth / 2 + Spread(i, members.Count, StraightSpacing, NodeWidth - 2 * PortInset);
                }
            }
        }

        private static (AiModelUsage, AiModelUsage) PairKey(AiCohesionNode a, AiCohesionNode b) =>
            a.Row <= b.Row ? (a.Usage, b.Usage) : (b.Usage, a.Usage);

        /// <summary>Every edge end on a side facing one gutter, in one row, takes
        /// its own height there — the ends on both sides of that gutter together,
        /// so that no two stubs in a gutter share a line. An edge joining the two
        /// sides of one gutter takes one height for both ends.</summary>
        private void AssignPorts(List<Plan> plans)
        {
            var groups = new Dictionary<(int Gutter, int Row), List<(Plan Plan, bool AtFrom)>>();
            foreach (var plan in plans.Where(plan => plan.Shape != Shape.Straight))
            {
                Add((plan.FromGutter, plan.A.Row), plan, atFrom: true);
                var toKey = (plan.ToGutter, plan.B.Row);
                if (toKey != (plan.FromGutter, plan.A.Row)) Add(toKey, plan, atFrom: false);
                else plan.SharedPort = true;
            }

            foreach (var ((_, row), ends) in groups)
            {
                var ordered = ends
                    .OrderBy(end => Toward(end.Plan, end.AtFrom))
                    .ThenBy(end => end.Plan.Index)
                    .ToList();
                var centre = grid.RowTop(row) + NodeHeight / 2;
                for (var i = 0; i < ordered.Count; i++)
                {
                    var y = centre + Spread(i, ordered.Count, PortSpacing, NodeHeight - 2 * PortInset);
                    var (plan, atFrom) = ordered[i];
                    if (atFrom) plan.FromY = y;
                    if (!atFrom || plan.SharedPort) plan.ToY = y;
                }
            }

            void Add((int, int) key, Plan plan, bool atFrom)
            {
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = [];
                list.Add((plan, atFrom));
            }
        }

        /// <summary>The height an edge end heads for once it leaves its side, so
        /// the ends in a group stack without crossing.</summary>
        private double Toward(Plan plan, bool atFrom)
        {
            if (plan.Shape == Shape.Channel) return Mid(grid.Channel(plan.Channel));
            var other = atFrom ? plan.B : plan.A;
            return other.Y + NodeHeight / 2;
        }

        /// <summary>Runs along one channel whose spans overlap take distinct heights.</summary>
        private void AssignChannelTracks(List<Plan> plans)
        {
            foreach (var group in plans.Where(plan => plan.Shape == Shape.Channel).GroupBy(plan => plan.Channel))
            {
                var spans = group
                    .Select(plan => (plan, low: Math.Min(GutterRange(plan.FromGutter).Low, GutterRange(plan.ToGutter).Low),
                        high: Math.Max(GutterRange(plan.FromGutter).High, GutterRange(plan.ToGutter).High)))
                    .ToList();
                var tracks = Tracks(spans.Select(span => (span.low, span.high)).ToList());
                var count = tracks.Max() + 1;
                var (low, high) = grid.Channel(group.Key);
                for (var i = 0; i < spans.Count; i++)
                {
                    spans[i].plan.ChannelY = Mid((low, high)) + Spread(tracks[i], count, TrackSpacing, high - low - 2 * TrackInset);
                }
            }
        }

        /// <summary>Vertical runs in one gutter whose spans overlap take distinct offsets.</summary>
        private void AssignGutterTracks(List<Plan> plans)
        {
            var runs = new List<(Plan Plan, bool First, int Gutter, double Low, double High)>();
            foreach (var plan in plans.Where(plan => plan.Shape != Shape.Straight))
            {
                if (plan.Shape == Shape.Jog && plan.FromY == plan.ToY)
                {
                    plan.FromX = Mid(GutterRange(plan.FromGutter));
                }
                else if (plan.Shape == Shape.Jog)
                {
                    runs.Add((plan, true, plan.FromGutter, Math.Min(plan.FromY, plan.ToY), Math.Max(plan.FromY, plan.ToY)));
                }
                else
                {
                    runs.Add((plan, true, plan.FromGutter, Math.Min(plan.FromY, plan.ChannelY), Math.Max(plan.FromY, plan.ChannelY)));
                    runs.Add((plan, false, plan.ToGutter, Math.Min(plan.ChannelY, plan.ToY), Math.Max(plan.ChannelY, plan.ToY)));
                }
            }

            foreach (var group in runs.GroupBy(run => run.Gutter))
            {
                var members = group.ToList();
                var tracks = Tracks(members.Select(run => (run.Low, run.High)).ToList());
                var count = tracks.Max() + 1;
                var (low, high) = GutterRange(group.Key);
                for (var i = 0; i < members.Count; i++)
                {
                    var x = Mid((low, high)) + Spread(tracks[i], count, TrackSpacing, high - low - 2 * TrackInset);
                    if (members[i].First) members[i].Plan.FromX = x;
                    else members[i].Plan.ToX = x;
                }
            }
        }

        /// <summary>Greedy interval colouring: each span takes the lowest track
        /// no overlapping span holds. Touching spans count as overlapping.</summary>
        private static int[] Tracks(List<(double Low, double High)> spans)
        {
            var result = new int[spans.Count];
            var ends = new List<double>();
            foreach (var index in Enumerable.Range(0, spans.Count).OrderBy(i => spans[i].Low).ThenBy(i => i))
            {
                var track = ends.FindIndex(end => end < spans[index].Low);
                if (track < 0)
                {
                    ends.Add(spans[index].High);
                    track = ends.Count - 1;
                }
                else
                {
                    ends[track] = spans[index].High;
                }

                result[index] = track;
            }

            return result;
        }

        /// <summary>The offset of item <paramref name="index"/> of
        /// <paramref name="count"/> centred on zero, at most
        /// <paramref name="spacing"/> apart and all within <paramref name="room"/>.</summary>
        private static double Spread(int index, int count, double spacing, double room)
        {
            if (count <= 1) return 0;
            var step = Math.Min(spacing, room / (count - 1));
            return (index - (count - 1) / 2.0) * step;
        }

        private static double Mid((double Low, double High) range) => (range.Low + range.High) / 2;

        /// <summary>Gutter <paramref name="gutter"/> lies left of column
        /// <paramref name="gutter"/>; the first and last are the margins.</summary>
        private (double Low, double High) GutterRange(int gutter)
        {
            var low = gutter == 0 ? Margin - ColumnGap : ColumnX(gutter - 1) + NodeWidth;
            var high = gutter >= columns ? ColumnX(columns - 1) + NodeWidth + ColumnGap : ColumnX(gutter);
            return (low, high);
        }

        private static AiCohesionEdge Edge(Plan plan)
        {
            var (a, b) = (plan.A, plan.B);
            List<AiCohesionPoint> points;
            if (plan.Shape == Shape.Straight)
            {
                var down = b.Row > a.Row;
                points = [new(plan.StraightX, down ? a.Bottom : a.Y), new(plan.StraightX, down ? b.Y : b.Bottom)];
            }
            else
            {
                var fromX = plan.FromSide == Side.Right ? a.Right : a.X;
                var toX = plan.ToSide == Side.Right ? b.Right : b.X;
                points = plan.Shape == Shape.Jog
                    ? [new(fromX, plan.FromY), new(plan.FromX, plan.FromY), new(plan.FromX, plan.ToY), new(toX, plan.ToY)]
                    : [new(fromX, plan.FromY), new(plan.FromX, plan.FromY), new(plan.FromX, plan.ChannelY),
                        new(plan.ToX, plan.ChannelY), new(plan.ToX, plan.ToY), new(toX, plan.ToY)];
            }

            points = Simplify(points);
            var link = plan.Link;
            return new AiCohesionEdge(link.Kind, link.From, link.To, link.Label, link.Dashed, points, LabelPoint(points));
        }

        /// <summary>Drops repeated points and the middle of three in a line.</summary>
        private static List<AiCohesionPoint> Simplify(List<AiCohesionPoint> points)
        {
            var distinct = new List<AiCohesionPoint>();
            foreach (var point in points)
            {
                if (distinct.Count == 0 || distinct[^1] != point) distinct.Add(point);
            }

            var result = new List<AiCohesionPoint>();
            foreach (var point in distinct)
            {
                if (result.Count >= 2)
                {
                    var (p, q) = (result[^2], result[^1]);
                    if ((p.X == q.X && q.X == point.X) || (p.Y == q.Y && q.Y == point.Y)) result.RemoveAt(result.Count - 1);
                }

                result.Add(point);
            }

            return result;
        }

        /// <summary>The middle of the longest segment: where the label reads best.</summary>
        private static AiCohesionPoint LabelPoint(List<AiCohesionPoint> points)
        {
            var best = (Length: -1.0, Point: points[0]);
            for (var i = 1; i < points.Count; i++)
            {
                var length = Math.Abs(points[i].X - points[i - 1].X) + Math.Abs(points[i].Y - points[i - 1].Y);
                if (length > best.Length) best = (length, new AiCohesionPoint((points[i].X + points[i - 1].X) / 2, (points[i].Y + points[i - 1].Y) / 2));
            }

            return best.Point;
        }
    }

    private enum Shape { Straight, Jog, Channel }

    private enum Side { Left, Right }

    private sealed class Plan(int index, Link link, AiCohesionNode a, AiCohesionNode b)
    {
        public int Index { get; } = index;
        public Link Link { get; } = link;
        public AiCohesionNode A { get; } = a;
        public AiCohesionNode B { get; } = b;
        public Shape Shape { get; set; }
        public Side FromSide { get; set; }
        public Side ToSide { get; set; }
        public int FromGutter { get; set; }
        public int ToGutter { get; set; }
        public int Channel { get; set; }
        public bool SharedPort { get; set; }
        public double StraightX { get; set; }
        public double FromY { get; set; }
        public double ToY { get; set; }
        public double ChannelY { get; set; }
        public double FromX { get; set; }
        public double ToX { get; set; }
    }
}

/// <summary>What an edge says.</summary>
public enum AiCohesionEdgeKind
{
    /// <summary>A <c>feeds</c> hand-off.</summary>
    Feed,

    /// <summary>A <c>feeds</c> hand-off out of a candidate in production — at
    /// the Operate stage — back into the loop: the loop that is still open.</summary>
    OpenLoop,

    /// <summary>A <c>related</c> link: one usage supporting or steering another.</summary>
    Support
}

public readonly record struct AiCohesionPoint(double X, double Y);

/// <param name="Stage">The stage the lane stands for.</param>
/// <param name="X">The lane's left edge: half a gutter left of its nodes.</param>
/// <param name="Width">The lane's width: a node and a gutter.</param>
/// <param name="BeforePullRequest">Whether the lane sits before the pull request.</param>
public sealed record AiCohesionColumn(AiModelStage Stage, double X, double Width, bool BeforePullRequest);

/// <param name="Layer">The layer the band stands for.</param>
/// <param name="Y">The band's top; its label sits in its first
/// <see cref="AiCohesionLayout.BandHeaderHeight"/>.</param>
/// <param name="Height">The band's height.</param>
/// <param name="Rows">How many rows of nodes it holds: one, unless more than
/// seven usages share it.</param>
public sealed record AiCohesionBand(AiModelLayer Layer, double Y, double Height, int Rows);

/// <param name="Usage">The usage the box stands for.</param>
/// <param name="Column">The stage column it sits in, from zero.</param>
/// <param name="Layer">The layer whose band holds it, or null on the path.</param>
/// <param name="Row">Its row on the whole grid: the path's rows, then each band's.</param>
public sealed record AiCohesionNode(AiModelUsage Usage, int Column, string? Layer, int Row, double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;
}

/// <param name="Kind">A feed, the open loop, or a support edge.</param>
/// <param name="From">The usage the edge leaves.</param>
/// <param name="To">The usage its arrow points at.</param>
/// <param name="Label">The <c>feeds</c> label, or null for a support edge.</param>
/// <param name="IsDashed">For a feed: whether either end is a candidate — it
/// would hand on, it does not yet. A support edge's dash is its kind's.</param>
/// <param name="Points">The orthogonal polyline, from the edge of
/// <paramref name="From"/>'s box to the edge of <paramref name="To"/>'s.</param>
/// <param name="LabelAt">Where the label sits: the middle of the longest segment.</param>
public sealed record AiCohesionEdge(
    AiCohesionEdgeKind Kind,
    AiModelUsage From,
    AiModelUsage To,
    string? Label,
    bool IsDashed,
    IReadOnlyList<AiCohesionPoint> Points,
    AiCohesionPoint LabelAt);
