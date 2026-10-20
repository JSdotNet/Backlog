namespace Backlog.UI.Components.Diagrams;

/// <summary>A card placed on the map: its centre in the map's own units.</summary>
public sealed record ContextMapCard(ContextMapContext Context, double X, double Y, int Lines);

/// <summary>A line placed on the map, from the edge of one card to the edge of the
/// other, in the map's own units.</summary>
public sealed record ContextMapStroke(ContextMapLine Line, double X1, double Y1, double X2, double Y2, bool Core);

/// <summary>
/// Where the Context map's Map tab draws each card and line (local ADR 0037):
/// every context on one ellipse, in the order that crosses the fewest lines. On a
/// convex ring two lines cross exactly when their ends interleave round it, so the
/// order alone decides the crossings; no line is routed.
/// </summary>
/// <param name="Width">The map's width in its own units, its SVG viewBox's. It
/// grows past <see cref="DefaultWidth"/>, the cards keeping their size, when that
/// many cards would touch on the default ring.</param>
/// <param name="Height">The map's height in the same units.</param>
/// <param name="CardWidth">A card's width.</param>
/// <param name="CardHeight">A card's height.</param>
/// <param name="Crossings">How many pairs of lines still cross.</param>
public sealed record ContextMapLayout(
    double Width,
    double Height,
    double CardWidth,
    double CardHeight,
    IReadOnlyList<ContextMapCard> Cards,
    IReadOnlyList<ContextMapStroke> Strokes,
    int Crossings)
{
    public const double DefaultWidth = 1000;
    public const double DefaultHeight = 640;
    public const double DefaultCardWidth = 156;
    public const double DefaultCardHeight = 72;

    /// <summary>The gap between a line's end and the card it meets, so an
    /// arrowhead's tip stops short of the card's edge.</summary>
    public const double Gap = 4;

    /// <summary>Lays <paramref name="map"/> out.</summary>
    public static ContextMapLayout Arrange(ContextMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        const double cardWidth = DefaultCardWidth, cardHeight = DefaultCardHeight;
        if (map.IsEmpty) return new ContextMapLayout(DefaultWidth, DefaultHeight, cardWidth, cardHeight, [], [], 0);

        var ids = map.Contexts.Select(c => c.Id).ToList();
        var pairs = map.Lines.Select(l => (ids.IndexOf(l.From), ids.IndexOf(l.To))).Where(p => p.Item1 >= 0 && p.Item2 >= 0).ToArray();
        var order = Order(ids.Count, pairs);

        // The ring grows, never the cards, until no two neighbouring cards touch.
        var (width, height) = (DefaultWidth, DefaultHeight);
        while (!Separated(order.Length, width, height, cardWidth, cardHeight)) (width, height) = (width * 1.05, height * 1.05);
        width = Math.Round(width);
        height = Math.Round(height);

        var centres = new (double X, double Y)[ids.Count];
        var ring = Ring(order.Length, width, height, cardWidth, cardHeight);
        for (var slot = 0; slot < order.Length; slot++) centres[order[slot]] = ring[slot];

        // Cards in ring order, so tabbing walks round the map.
        var cards = order
            .Select(i => new ContextMapCard(map.Contexts[i], centres[i].X, centres[i].Y, map.LinesTouching(map.Contexts[i].Id)))
            .ToList();

        var strokes = new List<ContextMapStroke>();
        foreach (var line in map.Lines)
        {
            var a = ids.IndexOf(line.From);
            var b = ids.IndexOf(line.To);
            if (a < 0 || b < 0) continue;
            var (x1, y1) = Edge(centres[a], centres[b], cardWidth, cardHeight);
            var (x2, y2) = Edge(centres[b], centres[a], cardWidth, cardHeight);
            strokes.Add(new ContextMapStroke(line, x1, y1, x2, y2, map.IsCoreLine(line)));
        }

        return new ContextMapLayout(width, height, cardWidth, cardHeight, cards, strokes, CountCrossings(SlotsOf(order), pairs));
    }

    /// <summary>The centre of each of <paramref name="count"/> slots round the
    /// ellipse inside a <paramref name="width"/> by <paramref name="height"/> map,
    /// the first at the top, clockwise.</summary>
    private static (double X, double Y)[] Ring(int count, double width, double height, double cardWidth, double cardHeight)
    {
        var cx = width / 2;
        var cy = height / 2;
        var rx = cx - cardWidth / 2 - 8;
        var ry = cy - cardHeight / 2 - 8;
        var ring = new (double X, double Y)[count];
        for (var slot = 0; slot < count; slot++)
        {
            var angle = -Math.PI / 2 + 2 * Math.PI * slot / count;
            ring[slot] = (Math.Round(cx + rx * Math.Cos(angle), 2), Math.Round(cy + ry * Math.Sin(angle), 2));
        }
        return ring;
    }

    /// <summary>The least room between two neighbouring cards, across or down, so
    /// the line between them shows its length and not only its heads.</summary>
    private const double MinimumGap = 32;

    /// <summary>Whether every two neighbouring slots keep their cards apart by
    /// <see cref="MinimumGap"/>, across or down.</summary>
    private static bool Separated(int count, double width, double height, double cardWidth, double cardHeight)
    {
        if (count < 2) return true;
        var ring = Ring(count, width, height, cardWidth, cardHeight);
        for (var slot = 0; slot < count; slot++)
        {
            var (a, b) = (ring[slot], ring[(slot + 1) % count]);
            if (Math.Abs(a.X - b.X) < cardWidth + MinimumGap && Math.Abs(a.Y - b.Y) < cardHeight + MinimumGap) return false;
        }
        return true;
    }

    /// <summary>Where the line from <paramref name="from"/>'s centre towards
    /// <paramref name="to"/>'s leaves <paramref name="from"/>'s card, a
    /// <see cref="Gap"/> beyond its edge.</summary>
    private static (double X, double Y) Edge((double X, double Y) from, (double X, double Y) to, double cardWidth, double cardHeight)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        if (dx == 0 && dy == 0) return from;

        var halfW = cardWidth / 2 + Gap;
        var halfH = cardHeight / 2 + Gap;
        var scale = Math.Min(dx == 0 ? double.MaxValue : halfW / Math.Abs(dx), dy == 0 ? double.MaxValue : halfH / Math.Abs(dy));
        return (Math.Round(from.X + dx * scale, 2), Math.Round(from.Y + dy * scale, 2));
    }

    /// <summary>The ring order — node index per slot — with the fewest crossings
    /// this search finds: from a few starting orders, swap two nodes or move one
    /// while that crosses fewer lines. Deterministic, so the same map always draws
    /// the same way.</summary>
    internal static int[] Order(int count, IReadOnlyList<(int A, int B)> pairs)
    {
        if (count <= 3) return [.. Enumerable.Range(0, count)];

        var degree = new int[count];
        foreach (var (a, b) in pairs) { degree[a]++; degree[b]++; }

        // The declared order, then a breadth-first walk from every context in turn:
        // a walk keeps neighbours together round the ring, and which context it
        // starts from changes which lines the search can untangle.
        var starts = new List<int[]> { Enumerable.Range(0, count).ToArray() };
        foreach (var root in Enumerable.Range(0, count).OrderByDescending(i => degree[i]).ThenBy(i => i))
        {
            starts.Add(Breadth(count, pairs, degree, root));
        }

        int[]? best = null;
        var bestCrossings = int.MaxValue;
        foreach (var start in starts)
        {
            var order = Improve(start, pairs);
            var crossings = CountCrossings(SlotsOf(order), pairs);
            if (crossings < bestCrossings)
            {
                best = order;
                bestCrossings = crossings;
            }
        }
        return best!;
    }

    private static int[] Breadth(int count, IReadOnlyList<(int A, int B)> pairs, int[] degree, int first)
    {
        var seen = new bool[count];
        var order = new List<int>(count);
        var queue = new Queue<int>();
        foreach (var root in Enumerable.Range(0, count).OrderByDescending(i => i == first).ThenByDescending(i => degree[i]).ThenBy(i => i))
        {
            if (seen[root]) continue;
            seen[root] = true;
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                order.Add(node);
                var next = pairs
                    .Where(p => p.A == node || p.B == node)
                    .Select(p => p.A == node ? p.B : p.A)
                    .Distinct()
                    .OrderByDescending(i => degree[i])
                    .ThenBy(i => i);
                foreach (var n in next)
                {
                    if (seen[n]) continue;
                    seen[n] = true;
                    queue.Enqueue(n);
                }
            }
        }
        return [.. order];
    }

    private static int[] Improve(int[] start, IReadOnlyList<(int A, int B)> pairs)
    {
        var order = (int[])start.Clone();
        var current = CountCrossings(SlotsOf(order), pairs);
        var improved = true;
        var rounds = 0;
        while (improved && current > 0 && rounds++ < 50)
        {
            improved = false;

            for (var i = 0; i < order.Length && current > 0; i++)
            {
                for (var j = i + 1; j < order.Length; j++)
                {
                    (order[i], order[j]) = (order[j], order[i]);
                    var crossings = CountCrossings(SlotsOf(order), pairs);
                    if (crossings < current)
                    {
                        current = crossings;
                        improved = true;
                    }
                    else
                    {
                        (order[i], order[j]) = (order[j], order[i]);
                    }
                }
            }

            for (var i = 0; i < order.Length && current > 0; i++)
            {
                for (var j = 0; j < order.Length; j++)
                {
                    if (i == j) continue;
                    var moved = Move(order, i, j);
                    var crossings = CountCrossings(SlotsOf(moved), pairs);
                    if (crossings < current)
                    {
                        order = moved;
                        current = crossings;
                        improved = true;
                    }
                }
            }
        }
        return order;
    }

    private static int[] Move(int[] order, int from, int to)
    {
        var list = order.ToList();
        var node = list[from];
        list.RemoveAt(from);
        list.Insert(to, node);
        return [.. list];
    }

    private static int[] SlotsOf(int[] order)
    {
        var slots = new int[order.Length];
        for (var slot = 0; slot < order.Length; slot++) slots[order[slot]] = slot;
        return slots;
    }

    /// <summary>How many pairs of lines cross when node <c>n</c> sits at
    /// <paramref name="slots"/>[n] round the ring. Lines sharing a card never cross.</summary>
    internal static int CountCrossings(int[] slots, IReadOnlyList<(int A, int B)> pairs)
    {
        var crossings = 0;
        for (var i = 0; i < pairs.Count; i++)
        {
            var (a, b) = (slots[pairs[i].A], slots[pairs[i].B]);
            if (a > b) (a, b) = (b, a);
            for (var j = i + 1; j < pairs.Count; j++)
            {
                var c = slots[pairs[j].A];
                var d = slots[pairs[j].B];
                if (c == a || c == b || d == a || d == b) continue;
                var cInside = c > a && c < b;
                var dInside = d > a && d < b;
                if (cInside != dInside) crossings++;
            }
        }
        return crossings;
    }
}
