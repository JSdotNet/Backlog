using System.Globalization;
using System.Text;

namespace Backlog.UI.Components.Diagrams.C4;

/// <summary>
/// Finds a way between two cards that goes round the others rather than through them.
/// <para>
/// The curved detours in <see cref="C4LayoutEngine"/> handle the ordinary case — a card
/// or two in the way, cleared by an arc over the top or a bow out to the side — and they
/// read better than anything with corners in it. What they cannot do is weave. A cubic
/// has one bend in it, so an edge that has to pass a row, then a column, then another
/// row has no curve available to it that misses everything, and the layered layout puts
/// components in a grid where exactly that is normal rather than exotic.
/// </para>
/// <para>
/// So this is the last resort, and it gives up the curve to buy the clearance: a path
/// along the gaps between the cards, turning only where it must. The gaps are already
/// there — a grid of cards is mostly the space around them — and the corridor between
/// two rows crosses the corridor between two columns at a point a line can turn on. That
/// grid of crossings is small, a dozen or so each way, so the route is a shortest path
/// over a few hundred points: nothing to compute, and it gives the shortest way round
/// with the fewest corners rather than the first one that happens to work.
/// </para>
/// </summary>
internal static class C4Router
{
    /// <summary>How far a route keeps away from a card it is passing.</summary>
    private const double Clearance = 8;

    /// <summary>The lane outside everything, for a route that has to go right round.</summary>
    private const double Outside = 34;

    /// <summary>What a corner costs, in pixels of route. High enough that a longer path
    /// with one bend beats a shorter one with three: a line with fewer corners is easier
    /// to follow, and the point of routing at all is to be read.</summary>
    private const double BendCost = 90;

    /// <summary>Narrower than this and a lane is not worth threading — the line would
    /// clear both cards by less than its own width and read as touching them.</summary>
    private const double Narrowest = 24;

    /// <summary>
    /// The corners of a route from one card to another, or <c>null</c> when they are
    /// walled in and no clear route exists. The first and last points sit on the two
    /// cards' own edges, so the arrowhead still lands where it should.
    /// </summary>
    public static IReadOnlyList<(double X, double Y)>? Around(
        C4PlacedNode from,
        C4PlacedNode to,
        IReadOnlyList<C4PlacedNode> obstacles)
    {
        if (obstacles.Count == 0) return null;

        // Everything in the way is fattened by the clearance, so a route that merely
        // avoids the rectangles still keeps a visible margin. The two cards being joined
        // are blocked at their true size instead: a route has to touch them — that is
        // where it starts and ends — but must not cut across them on the way.
        var blocked = obstacles
            .Select(node => (
                Left: node.X - Clearance,
                Top: node.Y - Clearance,
                Right: node.X + node.Width + Clearance,
                Bottom: node.Y + node.Height + Clearance))
            .Concat(
            [
                (from.X, from.Y, from.X + from.Width, from.Y + from.Height),
                (to.X, to.Y, to.X + to.Width, to.Y + to.Height)
            ])
            .ToList();

        var all = obstacles.Append(from).Append(to).ToList();

        var xs = Lanes(all.Select(node => (node.X, node.X + node.Width)))
            .Concat([all.Min(node => node.X) - Outside, all.Max(node => node.X + node.Width) + Outside])
            .Concat([from.X, from.X + from.Width, to.X, to.X + to.Width])
            .Concat([from.X + from.Width / 2, to.X + to.Width / 2])
            .Distinct()
            .OrderBy(lane => lane)
            .ToList();

        var ys = Lanes(all.Select(node => (node.Y, node.Y + node.Height)))
            .Concat([all.Min(node => node.Y) - Outside, all.Max(node => node.Y + node.Height) + Outside])
            .Concat([from.Y, from.Y + from.Height, to.Y, to.Y + to.Height])
            .Concat([from.Y + from.Height / 2, to.Y + to.Height / 2])
            .Distinct()
            .OrderBy(lane => lane)
            .ToList();

        var points = new List<(double X, double Y)>();
        var index = new Dictionary<(double, double), int>();

        foreach (var x in xs)
        {
            foreach (var y in ys)
            {
                if (Inside(blocked, x, y)) continue;

                index[(x, y)] = points.Count;
                points.Add((x, y));
            }
        }

        var starts = Anchors(from).Where(index.ContainsKey).Select(point => index[point]).ToList();
        var ends = Anchors(to).Where(index.ContainsKey).Select(point => index[point]).ToHashSet();

        if (starts.Count == 0 || ends.Count == 0) return null;

        var links = Links(points, index, blocked, xs, ys);
        var route = Shortest(points, links, starts, ends);

        return route is null ? null : Straighten(route);
    }

    /// <summary>The middles of the clear gaps between a set of spans.</summary>
    private static IEnumerable<double> Lanes(IEnumerable<(double Start, double End)> spans)
    {
        var reach = double.NegativeInfinity;

        foreach (var span in spans.OrderBy(span => span.Start))
        {
            if (reach > double.NegativeInfinity && span.Start - reach >= Narrowest)
            {
                yield return (reach + span.Start) / 2;
            }

            reach = Math.Max(reach, span.End);
        }
    }

    /// <summary>The middle of each of a card's four sides, which is where an edge may
    /// join it.</summary>
    private static IEnumerable<(double X, double Y)> Anchors(C4PlacedNode node)
    {
        var cx = node.X + node.Width / 2;
        var cy = node.Y + node.Height / 2;

        yield return (cx, node.Y);
        yield return (cx, node.Y + node.Height);
        yield return (node.X, cy);
        yield return (node.X + node.Width, cy);
    }

    /// <summary>
    /// Joins each grid point to its neighbour along each axis, where the space between
    /// them is clear. Neighbours only: a longer straight run is the same thing as the
    /// short hops that make it up, and costs no extra corners to travel.
    /// </summary>
    private static List<List<(int To, double Cost, bool Vertical)>> Links(
        List<(double X, double Y)> points,
        Dictionary<(double, double), int> index,
        List<(double Left, double Top, double Right, double Bottom)> blocked,
        List<double> xs,
        List<double> ys)
    {
        var links = points.Select(_ => new List<(int, double, bool)>()).ToList();

        void Join(int a, int b, bool vertical)
        {
            var (ax, ay) = points[a];
            var (bx, by) = points[b];
            if (!Clear(blocked, ax, ay, bx, by)) return;

            var cost = Math.Abs(bx - ax) + Math.Abs(by - ay);
            links[a].Add((b, cost, vertical));
            links[b].Add((a, cost, vertical));
        }

        foreach (var x in xs)
        {
            var column = ys.Where(y => index.ContainsKey((x, y))).ToList();
            for (var i = 1; i < column.Count; i++) Join(index[(x, column[i - 1])], index[(x, column[i])], true);
        }

        foreach (var y in ys)
        {
            var row = xs.Where(x => index.ContainsKey((x, y))).ToList();
            for (var i = 1; i < row.Count; i++) Join(index[(row[i - 1], y)], index[(row[i], y)], false);
        }

        return links;
    }

    /// <summary>
    /// Cheapest route from any starting anchor to any finishing one, counting corners as
    /// well as distance. The state carries which way the route was travelling when it
    /// arrived, because that is what decides whether the next step is a turn.
    /// </summary>
    private static List<(double X, double Y)>? Shortest(
        List<(double X, double Y)> points,
        List<List<(int To, double Cost, bool Vertical)>> links,
        List<int> starts,
        HashSet<int> ends)
    {
        // Two states per point — arrived going across, arrived going down — so that a
        // turn can be charged for. Index 0 is horizontal, 1 is vertical.
        var best = new double[points.Count, 2];
        var cameFrom = new int[points.Count, 2];
        var cameAxis = new int[points.Count, 2];

        for (var i = 0; i < points.Count; i++)
        {
            best[i, 0] = best[i, 1] = double.PositiveInfinity;
            cameFrom[i, 0] = cameFrom[i, 1] = -1;
        }

        var queue = new PriorityQueue<(int Node, int Axis), double>();

        foreach (var start in starts)
        {
            // Either heading is free to begin with: the first step out of a card is not
            // a turn, whichever way it goes.
            best[start, 0] = best[start, 1] = 0;
            queue.Enqueue((start, 0), 0);
            queue.Enqueue((start, 1), 0);
        }

        while (queue.TryDequeue(out var state, out var cost))
        {
            var (node, axis) = state;
            if (cost > best[node, axis]) continue;

            // Reached the far card, and by an actual route rather than by being one of
            // the starting anchors: an edge from a card to itself is not a route.
            if (ends.Contains(node) && cameFrom[node, axis] >= 0)
            {
                return Walk(points, cameFrom, cameAxis, node, axis);
            }

            foreach (var (next, step, vertical) in links[node])
            {
                var nextAxis = vertical ? 1 : 0;
                var total = cost + step + (nextAxis == axis ? 0 : BendCost);

                if (total >= best[next, nextAxis]) continue;

                best[next, nextAxis] = total;
                cameFrom[next, nextAxis] = node;
                cameAxis[next, nextAxis] = axis;
                queue.Enqueue((next, nextAxis), total);
            }
        }

        return null;
    }

    private static List<(double X, double Y)> Walk(
        List<(double X, double Y)> points,
        int[,] cameFrom,
        int[,] cameAxis,
        int node,
        int axis)
    {
        var route = new List<(double X, double Y)>();

        while (node >= 0)
        {
            route.Add(points[node]);

            var previous = cameFrom[node, axis];
            if (previous < 0) break;

            axis = cameAxis[node, axis];
            node = previous;
        }

        route.Reverse();
        return route;
    }

    /// <summary>Drops the points that are not corners, so the path is written as the few
    /// segments it actually has rather than every grid line it crossed.</summary>
    private static List<(double X, double Y)> Straighten(List<(double X, double Y)> route)
    {
        var kept = new List<(double X, double Y)> { route[0] };

        for (var i = 1; i < route.Count - 1; i++)
        {
            var before = kept[^1];
            var here = route[i];
            var after = route[i + 1];

            var straight = (Same(before.X, here.X) && Same(here.X, after.X))
                || (Same(before.Y, here.Y) && Same(here.Y, after.Y));

            if (!straight) kept.Add(here);
        }

        kept.Add(route[^1]);
        return kept;
    }

    /// <summary>
    /// The route as an SVG path, with its corners rounded off. A right angle drawn sharp
    /// looks like a mistake next to the curves every other edge is drawn with; a small
    /// radius is enough to make it read as the same kind of line.
    /// </summary>
    public static string Path(IReadOnlyList<(double X, double Y)> route)
    {
        const double radius = 14;

        var path = new StringBuilder();
        path.Append(CultureInfo.InvariantCulture, $"M {route[0].X:0.##} {route[0].Y:0.##}");

        for (var i = 1; i < route.Count - 1; i++)
        {
            var before = route[i - 1];
            var corner = route[i];
            var after = route[i + 1];

            var entry = Towards(corner, before, Math.Min(radius, Length(before, corner) / 2));
            var exit = Towards(corner, after, Math.Min(radius, Length(corner, after) / 2));

            path.Append(CultureInfo.InvariantCulture, $" L {entry.X:0.##} {entry.Y:0.##}");
            path.Append(CultureInfo.InvariantCulture, $" Q {corner.X:0.##} {corner.Y:0.##}, {exit.X:0.##} {exit.Y:0.##}");
        }

        path.Append(CultureInfo.InvariantCulture, $" L {route[^1].X:0.##} {route[^1].Y:0.##}");
        return path.ToString();
    }

    /// <summary>The point on the route's own line, halfway along it, where its label goes.</summary>
    public static (double X, double Y) Midpoint(IReadOnlyList<(double X, double Y)> route)
    {
        var total = 0.0;
        for (var i = 1; i < route.Count; i++) total += Length(route[i - 1], route[i]);

        var travelled = 0.0;
        for (var i = 1; i < route.Count; i++)
        {
            var step = Length(route[i - 1], route[i]);
            if (travelled + step >= total / 2) return Towards(route[i - 1], route[i], total / 2 - travelled);

            travelled += step;
        }

        return route[^1];
    }

    private static bool Same(double a, double b) => Math.Abs(a - b) < 0.01;

    private static bool Inside(List<(double Left, double Top, double Right, double Bottom)> blocked, double x, double y) =>
        blocked.Exists(rect => x > rect.Left && x < rect.Right && y > rect.Top && y < rect.Bottom);

    /// <summary>Whether an axis-aligned segment stays out of everything in the way.</summary>
    private static bool Clear(
        List<(double Left, double Top, double Right, double Bottom)> blocked,
        double ax, double ay, double bx, double by)
    {
        var left = Math.Min(ax, bx);
        var right = Math.Max(ax, bx);
        var top = Math.Min(ay, by);
        var bottom = Math.Max(ay, by);

        return !blocked.Exists(rect =>
            right > rect.Left && left < rect.Right && bottom > rect.Top && top < rect.Bottom);
    }

    private static double Length((double X, double Y) a, (double X, double Y) b) =>
        Math.Abs(b.X - a.X) + Math.Abs(b.Y - a.Y);

    private static (double X, double Y) Towards((double X, double Y) from, (double X, double Y) to, double distance)
    {
        var span = Length(from, to);
        if (span <= 0) return from;

        var share = distance / span;
        return (from.X + (to.X - from.X) * share, from.Y + (to.Y - from.Y) * share);
    }
}
