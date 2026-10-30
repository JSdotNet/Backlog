namespace Backlog.UI.Components.Diagrams;

/// <summary>A card placed on the map: its centre in the map's own units.</summary>
public sealed record ContextMapCard(ContextMapContext Context, double X, double Y, int Lines);

/// <summary>A line placed on the map, from the edge of one card to the edge of the
/// other, in the map's own units.</summary>
public sealed record ContextMapStroke(ContextMapLine Line, double X1, double Y1, double X2, double Y2, bool Core);

/// <summary>
/// Where the Context map's Map tab draws each card and line (local ADR 0044):
/// every context on one ellipse, in the order that crosses the fewest lines. On a
/// convex ring two lines cross exactly when their ends interleave round it, so the
/// order alone decides the crossings; no line is routed. A card a line would run
/// beneath steps out from the ring until the line passes inside it.
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
    public const double DefaultCardWidth = 200;
    public const double DefaultCardHeight = 56;

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
        var orders = Orders(ids.Count, pairs);

        // The ring grows, never the cards, until no two neighbouring cards touch.
        var (width, height) = (DefaultWidth, DefaultHeight);
        while (!Separated(ids.Count, width, height, cardWidth, cardHeight)) (width, height) = (width * 1.02, height * 1.02);

        // Of the orders crossing fewest lines, the one that runs fewest lines
        // beneath a card it does not join; a card a line still runs beneath then
        // steps out from the ring's centre until the line passes inside it.
        // Turning or mirroring an order round the ellipse keeps its crossings but
        // not its geometry, so each is tried, and the smallest map wins a tie.
        var (w, h) = (Math.Round(width), Math.Round(height));
        return orders
            .SelectMany(Turns)
            .Select(order => Place(map, ids, order, w, h, cardWidth, cardHeight, pairs))
            .OrderBy(candidate => Hidden(candidate).Count)
            .ThenBy(candidate => candidate.Width * candidate.Height)
            .First();

        static IEnumerable<int[]> Turns(int[] order)
        {
            foreach (var ring in new[] { order, order.Reverse().ToArray() })
            {
                for (var turn = 0; turn < ring.Length; turn++) yield return [.. ring[turn..], .. ring[..turn]];
            }
        }
    }

    /// <summary>How far a card may step out from the ring, a step at a time, to
    /// clear a line from under it; past that the map keeps the line.</summary>
    private const double NudgeStep = 8;

    private const int NudgeSteps = 10;

    /// <summary>How far a line may run beneath a card it does not join before it
    /// reads as hidden there rather than as clipping the card's corner.</summary>
    private const double HiddenLength = 6;

    /// <summary>The margin kept round the cards at the map's edge.</summary>
    private const double Margin = 8;

    private static ContextMapLayout Place(
        ContextMap map,
        List<string> ids,
        int[] order,
        double width,
        double height,
        double cardWidth,
        double cardHeight,
        IReadOnlyList<(int A, int B)> pairs)
    {
        var centres = new (double X, double Y)[ids.Count];
        var ring = Ring(order.Length, width, height, cardWidth, cardHeight);
        for (var slot = 0; slot < order.Length; slot++) centres[order[slot]] = ring[slot];

        for (var step = 0; step < NudgeSteps; step++)
        {
            var covering = Hidden(PlaceStrokes(map, ids, centres, cardWidth, cardHeight), PlaceCards(map, order, centres), cardWidth, cardHeight)
                .Select(h => ids.IndexOf(h.Card.Context.Id))
                .Distinct()
                .ToList();
            if (covering.Count == 0) break;
            foreach (var i in covering)
            {
                var (dx, dy) = (centres[i].X - width / 2, centres[i].Y - height / 2);
                var length = Math.Sqrt(dx * dx + dy * dy);
                if (length == 0) continue;
                centres[i] = (Math.Round(centres[i].X + NudgeStep * dx / length, 2), Math.Round(centres[i].Y + NudgeStep * dy / length, 2));
            }
        }

        // A card stepped out past the edge widens the map rather than leaving it.
        var left = centres.Min(c => c.X) - cardWidth / 2 - Margin;
        var top = centres.Min(c => c.Y) - cardHeight / 2 - Margin;
        var (shiftX, shiftY) = (Math.Max(0, -left), Math.Max(0, -top));
        if (shiftX > 0 || shiftY > 0)
        {
            for (var i = 0; i < centres.Length; i++) centres[i] = (Math.Round(centres[i].X + shiftX, 2), Math.Round(centres[i].Y + shiftY, 2));
        }
        width = Math.Max(Math.Ceiling(width + shiftX), Math.Ceiling(centres.Max(c => c.X) + cardWidth / 2 + Margin));
        height = Math.Max(Math.Ceiling(height + shiftY), Math.Ceiling(centres.Max(c => c.Y) + cardHeight / 2 + Margin));

        return new ContextMapLayout(
            width, height, cardWidth, cardHeight,
            PlaceCards(map, order, centres), PlaceStrokes(map, ids, centres, cardWidth, cardHeight),
            CountCrossings(SlotsOf(order), pairs));
    }

    // Cards in ring order, so tabbing walks round the map.
    private static List<ContextMapCard> PlaceCards(ContextMap map, int[] order, (double X, double Y)[] centres) =>
    [
        .. order.Select(i => new ContextMapCard(map.Contexts[i], centres[i].X, centres[i].Y, map.LinesTouching(map.Contexts[i].Id)))
    ];

    private static List<ContextMapStroke> PlaceStrokes(ContextMap map, List<string> ids, (double X, double Y)[] centres, double cardWidth, double cardHeight)
    {
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
        return strokes;
    }

    /// <summary>Every line of <paramref name="layout"/> that runs beneath a card it
    /// does not join for more than <see cref="HiddenLength"/>, with that card.</summary>
    public static IReadOnlyList<(ContextMapStroke Stroke, ContextMapCard Card)> Hidden(ContextMapLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return Hidden(layout.Strokes, layout.Cards, layout.CardWidth, layout.CardHeight);
    }

    private static List<(ContextMapStroke Stroke, ContextMapCard Card)> Hidden(
        IReadOnlyList<ContextMapStroke> strokes,
        IReadOnlyList<ContextMapCard> cards,
        double cardWidth,
        double cardHeight)
    {
        var hidden = new List<(ContextMapStroke, ContextMapCard)>();
        var (halfW, halfH) = (cardWidth / 2, cardHeight / 2);
        foreach (var stroke in strokes)
        {
            foreach (var card in cards)
            {
                if (stroke.Line.Touches(card.Context.Id)) continue;
                if (Inside(stroke, card.X - halfW, card.Y - halfH, card.X + halfW, card.Y + halfH) > HiddenLength) hidden.Add((stroke, card));
            }
        }
        return hidden;
    }

    /// <summary>How long a stretch of the stroke runs inside the rectangle:
    /// Liang–Barsky clipping, which keeps the part of the segment inside it.</summary>
    private static double Inside(ContextMapStroke stroke, double left, double top, double right, double bottom)
    {
        var (dx, dy) = (stroke.X2 - stroke.X1, stroke.Y2 - stroke.Y1);
        double t0 = 0, t1 = 1;
        foreach (var (p, q) in new[] { (-dx, stroke.X1 - left), (dx, right - stroke.X1), (-dy, stroke.Y1 - top), (dy, bottom - stroke.Y1) })
        {
            if (p == 0)
            {
                if (q < 0) return 0;
                continue;
            }
            var t = q / p;
            if (p < 0) t0 = Math.Max(t0, t);
            else t1 = Math.Min(t1, t);
            if (t0 > t1) return 0;
        }
        return (t1 - t0) * Math.Sqrt(dx * dx + dy * dy);
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
    private const double MinimumGap = 24;

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
    internal static int[] Order(int count, IReadOnlyList<(int A, int B)> pairs) => Orders(count, pairs)[0];

    /// <summary>Every distinct ring order the search ends on with the fewest
    /// crossings, in the order found, so a tie can be broken by how the map draws.</summary>
    internal static IReadOnlyList<int[]> Orders(int count, IReadOnlyList<(int A, int B)> pairs)
    {
        if (count <= 3) return [[.. Enumerable.Range(0, count)]];

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

        var best = new List<int[]>();
        var bestCrossings = int.MaxValue;
        foreach (var start in starts)
        {
            var order = Improve(start, pairs);
            var crossings = CountCrossings(SlotsOf(order), pairs);
            if (crossings < bestCrossings)
            {
                best.Clear();
                bestCrossings = crossings;
            }
            if (crossings == bestCrossings && !best.Any(b => b.SequenceEqual(order))) best.Add(order);
        }
        return best;
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
