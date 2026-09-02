using System.Globalization;

namespace Backlog.UI.Components.Diagrams.C4;

/// <summary>One drawn card, placed.</summary>
/// <param name="Alias">The identifier the rendered shape carries, so a click and a
/// Highlighter dim both resolve through the same thread the mermaid rendering used.</param>
public sealed record C4PlacedNode(
    C4Node Node,
    string Alias,
    double X,
    double Y,
    double Width,
    double Height,
    bool External,
    bool Drillable);

/// <summary>A boundary: the frame drawn around whatever the view opened up.</summary>
public sealed record C4PlacedBoundary(
    string Id,
    string Label,
    string Kind,
    double X,
    double Y,
    double Width,
    double Height);

/// <summary>One drawn edge: a path, and the label that sits on it.</summary>
/// <param name="Path">An SVG path, already a cubic curve. Curved rather than
/// straight because two straight lines between the same pair of columns land on top
/// of each other, and because it is what the diagrams this is modelled on look
/// like.</param>
public sealed record C4PlacedEdge(
    string FromAlias,
    string ToAlias,
    string Path,
    string? Label,
    string? Technology,
    int? Order,
    double LabelX,
    double LabelY);

/// <summary>Everything needed to draw one view, in one coordinate space.</summary>
public sealed record C4Diagram(
    double Width,
    double Height,
    IReadOnlyList<C4PlacedBoundary> Boundaries,
    IReadOnlyList<C4PlacedNode> Nodes,
    IReadOnlyList<C4PlacedEdge> Edges)
{
    public static C4Diagram Empty { get; } = new(0, 0, [], [], []);

    public bool HasContent => Nodes.Count > 0;
}

/// <summary>
/// Lays a C4 view out.
/// <para>
/// This exists because mermaid's C4 renderer cannot be made to look like the tool the
/// model is authored in. Its boxes are sized from their own text, it draws no icon, it
/// routes edges its own way and it writes every colour inline — so the picture was
/// always going to be a mermaid picture. Everything drawn from here is this product's
/// own: card size, the kind glyph, the ramp, dashed boundaries, curved edges.
/// </para>
/// <para>
/// It is deliberately a small algorithm rather than a general graph layout. A C4 view
/// is a handful of boxes with a strong intended reading — actors above, the subject in
/// the middle, what it depends on below, and whatever the view opened up drawn inside a
/// frame. Layering on the edge direction and gridding inside the frame produces that,
/// and a force-directed or Sugiyama pass would produce something less predictable for
/// no gain at this size.
/// </para>
/// <para>
/// Selection is not re-derived here. Which elements and which edges a view contains
/// comes from <see cref="C4MermaidWriter.VisibleElements"/> and
/// <see cref="C4MermaidWriter.VisibleRelationships"/>, so the two renderings always
/// draw the same view — and the mermaid one stays available as a fallback.
/// </para>
/// </summary>
public static class C4LayoutEngine
{
    private const double CardWidth = 196;
    private const double CardHeight = 84;
    private const double ColumnGap = 44;
    private const double RowGap = 92;
    private const double BoundaryPadding = 28;
    private const double BoundaryHeader = 26;
    private const double Margin = 28;

    /// <summary>How many cards sit in a row inside a boundary before it wraps. Four
    /// keeps a container view of a typical system about as wide as it is tall, which
    /// is the shape that fits a pane.</summary>
    private const int BoundaryColumns = 4;

    public static C4Diagram Build(C4Workspace workspace, C4View view)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(view);

        var nodes = C4Exploration.Nodes(workspace, view);
        if (nodes.Count == 0) return C4Diagram.Empty;

        var byId = nodes.ToDictionary(node => node.ElementId, StringComparer.OrdinalIgnoreCase);
        var elements = C4MermaidWriter.VisibleElements(workspace, view)
            .ToDictionary(element => element.Id, StringComparer.OrdinalIgnoreCase);

        // A cell is either one card or one boundary full of cards. Layering happens
        // over cells, so a system that was opened up moves as a whole.
        var cells = BuildCells(workspace, view, nodes, elements);
        var edges = C4MermaidWriter.VisibleRelationships(workspace, view);

        AssignRanks(cells, edges);
        Place(cells);

        var placedNodes = new List<C4PlacedNode>();
        var placedBoundaries = new List<C4PlacedBoundary>();

        foreach (var cell in cells)
        {
            if (cell.Boundary is not null)
            {
                placedBoundaries.Add(new C4PlacedBoundary(
                    cell.Boundary.Id,
                    cell.Boundary.Name,
                    KindLabel(cell.Boundary.Kind),
                    cell.X,
                    cell.Y,
                    cell.Width,
                    cell.Height));
            }

            foreach (var member in cell.Members)
            {
                placedNodes.Add(new C4PlacedNode(
                    member.Node,
                    member.Node.Alias,
                    cell.X + member.OffsetX,
                    cell.Y + member.OffsetY,
                    CardWidth,
                    CardHeight,
                    IsExternal(workspace, view, member.Node.ElementId),
                    member.Node.DrillViewKey is not null));
            }
        }

        var placedEdges = Route(edges, byId, placedNodes, workspace);

        var width = placedNodes.Count == 0 ? 0 : placedNodes.Max(node => node.X + node.Width) + Margin;
        var height = placedNodes.Count == 0 ? 0 : placedNodes.Max(node => node.Y + node.Height) + Margin;

        // A boundary may reach past its last card, and an edge label may sit outside
        // both, so the canvas is the union rather than the cards alone.
        foreach (var boundary in placedBoundaries)
        {
            width = Math.Max(width, boundary.X + boundary.Width + Margin);
            height = Math.Max(height, boundary.Y + boundary.Height + Margin);
        }

        return new C4Diagram(width, height, placedBoundaries, placedNodes, placedEdges);
    }

    // ---- cells -----------------------------------------------------------------

    private sealed class Member
    {
        public required C4Node Node { get; init; }
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
    }

    private sealed class Cell
    {
        public C4Element? Boundary { get; init; }
        public List<Member> Members { get; } = [];
        public double Width { get; set; }
        public double Height { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public int Rank { get; set; }
        public HashSet<string> Ids { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Groups the drawn cards into cells: one per boundary, plus one per card that is
    /// in no boundary.
    /// <para>
    /// A boundary is an element the view draws something <em>inside</em> — the same
    /// test the mermaid writer makes when it chooses between a frame and a card, so the
    /// two renderings never disagree about which is which.
    /// </para>
    /// </summary>
    private static List<Cell> BuildCells(
        C4Workspace workspace,
        C4View view,
        IReadOnlyList<C4Node> nodes,
        Dictionary<string, C4Element> elements)
    {
        var boundaries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var element in elements.Values)
        {
            if (element.ParentId is not null && elements.ContainsKey(element.ParentId)) boundaries.Add(element.ParentId);
        }

        var cells = new List<Cell>();
        var byBoundary = new Dictionary<string, Cell>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in nodes)
        {
            // A boundary is drawn as a frame, never as a card of its own.
            if (boundaries.Contains(node.ElementId)) continue;

            var element = elements.GetValueOrDefault(node.ElementId);
            var parent = element?.ParentId;

            if (parent is not null && boundaries.Contains(parent))
            {
                if (!byBoundary.TryGetValue(parent, out var cell))
                {
                    cell = new Cell { Boundary = workspace.Element(parent) };
                    byBoundary[parent] = cell;
                    cells.Add(cell);
                }

                cell.Members.Add(new Member { Node = node });
                cell.Ids.Add(node.ElementId);
                continue;
            }

            var own = new Cell();
            own.Members.Add(new Member { Node = node });
            own.Ids.Add(node.ElementId);
            cells.Add(own);
        }

        foreach (var cell in cells) Size(cell);
        return cells;
    }

    /// <summary>Sizes a cell and places its members inside it: a bare card is its own
    /// size, and a boundary wraps its cards into a grid with room for its label.</summary>
    private static void Size(Cell cell)
    {
        if (cell.Boundary is null)
        {
            cell.Width = CardWidth;
            cell.Height = CardHeight;
            cell.Members[0].OffsetX = 0;
            cell.Members[0].OffsetY = 0;
            return;
        }

        var columns = Math.Min(BoundaryColumns, cell.Members.Count);
        var rows = (int)Math.Ceiling(cell.Members.Count / (double)columns);

        for (var index = 0; index < cell.Members.Count; index++)
        {
            var column = index % columns;
            var row = index / columns;

            cell.Members[index].OffsetX = BoundaryPadding + column * (CardWidth + ColumnGap);
            cell.Members[index].OffsetY = BoundaryPadding + BoundaryHeader + row * (CardHeight + RowGap / 2);
        }

        cell.Width = BoundaryPadding * 2 + columns * CardWidth + (columns - 1) * ColumnGap;
        cell.Height = BoundaryPadding * 2 + BoundaryHeader + rows * CardHeight + (rows - 1) * (RowGap / 2);
    }

    // ---- ranking ---------------------------------------------------------------

    /// <summary>
    /// Puts each cell on a row.
    /// <para>
    /// A longest-path layering over the edge graph, which gives the reading a C4
    /// diagram is supposed to have: whoever starts things at the top, what they use
    /// under them, what that depends on under that. People are pinned to the top row
    /// even when nothing points at them, because an actor below the system it uses
    /// reads as though the system calls the person.
    /// </para>
    /// <para>
    /// Cycles are broken by simply not revisiting a cell. A cycle in a C4 model is
    /// ordinary — two containers that call each other — and the layering only has to
    /// be stable and readable, not canonical.
    /// </para>
    /// </summary>
    private static void AssignRanks(List<Cell> cells, IReadOnlyList<C4VisibleRelationship> edges)
    {
        var owner = new Dictionary<string, Cell>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in cells)
        {
            foreach (var id in cell.Ids) owner[id] = cell;
        }

        var outgoing = new Dictionary<Cell, List<Cell>>();
        var incoming = new Dictionary<Cell, int>();
        foreach (var cell in cells)
        {
            outgoing[cell] = [];
            incoming[cell] = 0;
        }

        foreach (var edge in edges)
        {
            if (!owner.TryGetValue(edge.FromId, out var from)) continue;
            if (!owner.TryGetValue(edge.ToId, out var to)) continue;
            if (ReferenceEquals(from, to)) continue;

            outgoing[from].Add(to);
            incoming[to]++;
        }

        // Sources first: a person, or anything nothing points at.
        var queue = new Queue<Cell>();
        foreach (var cell in cells)
        {
            var isPerson = cell.Boundary is null && cell.Members[0].Node.Kind == C4ElementKind.Person;
            if (isPerson || incoming[cell] == 0)
            {
                cell.Rank = 0;
                queue.Enqueue(cell);
            }
            else
            {
                cell.Rank = -1;
            }
        }

        // Nothing was a source, so the graph is one cycle. Seed it with the first
        // cell rather than leaving every rank at -1.
        if (queue.Count == 0 && cells.Count > 0)
        {
            cells[0].Rank = 0;
            queue.Enqueue(cells[0]);
        }

        var guard = 0;
        while (queue.Count > 0 && guard++ < cells.Count * 8)
        {
            var cell = queue.Dequeue();

            foreach (var next in outgoing[cell])
            {
                var wanted = cell.Rank + 1;
                if (next.Rank >= wanted) continue;

                next.Rank = wanted;
                queue.Enqueue(next);
            }
        }

        // Anything the walk never reached — an isolated cell in a cycle — goes on the
        // bottom row rather than at rank -1, which would place it above everything.
        var deepest = cells.Count == 0 ? 0 : cells.Max(cell => cell.Rank);
        foreach (var cell in cells)
        {
            if (cell.Rank < 0) cell.Rank = deepest + 1;
        }
    }

    // ---- placement -------------------------------------------------------------

    /// <summary>Places each row left to right in declaration order, and centres the
    /// rows on each other so the picture reads down a spine rather than off to one
    /// side.</summary>
    private static void Place(List<Cell> cells)
    {
        var rows = cells
            .GroupBy(cell => cell.Rank)
            .OrderBy(group => group.Key)
            .ToList();

        var widths = rows
            .Select(row => row.Sum(cell => cell.Width) + Math.Max(0, row.Count() - 1) * ColumnGap)
            .ToList();

        var widest = widths.Count == 0 ? 0 : widths.Max();
        var y = Margin;

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index].ToList();
            var x = Margin + (widest - widths[index]) / 2;
            var tallest = row.Max(cell => cell.Height);

            foreach (var cell in row)
            {
                cell.X = x;

                // Cards in a row sit on a shared centre line, so a boundary beside a
                // single card does not drag that card to the top of the row.
                cell.Y = y + (tallest - cell.Height) / 2;
                x += cell.Width + ColumnGap;
            }

            y += tallest + RowGap;
        }
    }

    // ---- edges -----------------------------------------------------------------

    /// <summary>
    /// Routes each edge as a cubic curve between the two cards' nearest sides.
    /// <para>
    /// Curved for two reasons. Two edges between the same pair of rows fall exactly on
    /// top of each other as straight lines, and a curve leaving a card vertically
    /// makes the direction readable where a diagonal does not.
    /// </para>
    /// </summary>
    private static List<C4PlacedEdge> Route(
        IReadOnlyList<C4VisibleRelationship> edges,
        Dictionary<string, C4Node> byId,
        List<C4PlacedNode> placed,
        C4Workspace workspace)
    {
        var positions = placed.ToDictionary(node => node.Node.ElementId, StringComparer.OrdinalIgnoreCase);
        var routed = new List<C4PlacedEdge>();

        foreach (var edge in edges)
        {
            if (!positions.TryGetValue(edge.FromId, out var from)) continue;
            if (!positions.TryGetValue(edge.ToId, out var to)) continue;

            var obstacles = placed
                .Where(node => !ReferenceEquals(node, from) && !ReferenceEquals(node, to))
                .ToList();

            var (x1, y1, x2, y2) = Anchors(from, to);

            // The control points pull straight out of the side the edge left, which is
            // what gives the curve its direction rather than a lazy diagonal bow.
            var vertical = Math.Abs(y2 - y1) >= Math.Abs(x2 - x1);
            var pull = vertical ? Math.Abs(y2 - y1) * 0.45 : Math.Abs(x2 - x1) * 0.45;

            var c1x = vertical ? x1 : x1 + Math.Sign(x2 - x1) * pull;
            var c1y = vertical ? y1 + Math.Sign(y2 - y1) * pull : y1;
            var c2x = vertical ? x2 : x2 - Math.Sign(x2 - x1) * pull;
            var c2y = vertical ? y2 - Math.Sign(y2 - y1) * pull : y2;

            // A line drawn through a card is worse than a longer line drawn around it.
            // Two cards in the same row with a third between them is the ordinary case
            // — the direct route crosses the middle card, and takes the edge's label
            // through it too — and the layered layout makes it common rather than rare.
            //
            // Each way round is tried and checked, rather than the first one being
            // taken on faith. A detour is only an improvement if it is clear itself,
            // and going over the top can just as easily land on something as going
            // straight through did.
            if (Crosses(x1, y1, c1x, c1y, c2x, c2y, x2, y2, obstacles))
            {
                var clear = false;

                foreach (var candidate in Detours(from, to, obstacles, vertical))
                {
                    if (Crosses(candidate.X1, candidate.Y1, candidate.C1X, candidate.C1Y, candidate.C2X, candidate.C2Y, candidate.X2, candidate.Y2, obstacles)) continue;

                    (x1, y1, c1x, c1y, c2x, c2y, x2, y2) =
                        (candidate.X1, candidate.Y1, candidate.C1X, candidate.C1Y, candidate.C2X, candidate.C2Y, candidate.X2, candidate.Y2);

                    clear = true;
                    break;
                }

                // No curve misses everything, so the edge is routed along the gaps
                // instead. That happens where the cards are in a grid — a component view
                // is one — and an edge has to pass a row, then a column, then another
                // row: no single curve has enough bends in it to do that.
                if (!clear)
                {
                    var around = C4Router.Around(from, to, obstacles);

                    if (around is not null)
                    {
                        var (aroundX, aroundY) = C4Router.Midpoint(around);

                        routed.Add(new C4PlacedEdge(
                            from.Alias,
                            to.Alias,
                            C4Router.Path(around),
                            edge.Description,
                            edge.Technology,
                            edge.Order,
                            aroundX,
                            aroundY));

                        continue;
                    }
                }
            }

            var path = string.Create(
                CultureInfo.InvariantCulture,
                $"M {x1:0.##} {y1:0.##} C {c1x:0.##} {c1y:0.##}, {c2x:0.##} {c2y:0.##}, {x2:0.##} {y2:0.##}");

            // The label sits on the curve rather than between the endpoints. On a
            // detour those are very different places, and the midpoint of a route that
            // arcs over a card is exactly on top of that card — which is the thing the
            // detour was drawn to avoid.
            var (labelX, labelY) = Midpoint(x1, y1, c1x, c1y, c2x, c2y, x2, y2);

            routed.Add(new C4PlacedEdge(
                from.Alias,
                to.Alias,
                path,
                edge.Description,
                edge.Technology,
                edge.Order,
                labelX,
                labelY));
        }

        return Spread(routed, placed);
    }

    /// <summary>
    /// Every way round the thing that was in the way, in the order worth trying.
    /// <para>
    /// Over the top first for a horizontal edge and out to the roomier side first for
    /// a vertical one, then the opposite, then wider versions of both. The caller
    /// takes the first that is actually clear — a detour that lands on a different
    /// card is not an improvement, and picking one without checking was how the
    /// landscape ended up with an edge through <c>Email / IMAP</c>.
    /// </para>
    /// <para>
    /// The endpoints stay on the cards they belong to throughout: a detour changes the
    /// path, never what it connects, so the arrowhead always lands where it should.
    /// </para>
    /// </summary>
    private static IEnumerable<EdgePath> Detours(
        C4PlacedNode from,
        C4PlacedNode to,
        List<C4PlacedNode> obstacles,
        bool vertical)
    {
        var fromCx = from.X + from.Width / 2;
        var toCx = to.X + to.Width / 2;
        var fromCy = from.Y + from.Height / 2;
        var toCy = to.Y + to.Height / 2;

        var left = Math.Min(fromCx, toCx);
        var right = Math.Max(fromCx, toCx);
        var top = Math.Min(fromCy, toCy);
        var bottom = Math.Max(fromCy, toCy);

        var across = obstacles.Where(node => node.X + node.Width > left && node.X < right).ToList();
        var between = obstacles.Where(node => node.Y + node.Height > top && node.Y < bottom).ToList();

        var ceiling = across.Select(node => node.Y).DefaultIfEmpty(Math.Min(from.Y, to.Y)).Min();
        var floor = across.Select(node => node.Y + node.Height).DefaultIfEmpty(Math.Max(from.Y + from.Height, to.Y + to.Height)).Max();
        var leftEdge = between.Select(node => node.X).DefaultIfEmpty(Math.Min(from.X, to.X)).Min();
        var rightEdge = between.Select(node => node.X + node.Width).DefaultIfEmpty(Math.Max(from.X + from.Width, to.X + to.Width)).Max();

        EdgePath Over(double clearance)
        {
            var apex = Math.Min(ceiling, Math.Min(from.Y, to.Y)) - clearance;
            return new EdgePath(fromCx, from.Y, fromCx, apex, toCx, apex, toCx, to.Y);
        }

        EdgePath Under(double clearance)
        {
            var apex = Math.Max(floor, Math.Max(from.Y + from.Height, to.Y + to.Height)) + clearance;
            return new EdgePath(fromCx, from.Y + from.Height, fromCx, apex, toCx, apex, toCx, to.Y + to.Height);
        }

        // Two ways to use a lane, because the obvious one does not hold it.
        //
        // Beside swings towards the lane straight away, which reads well when the lane
        // lies between the two cards. Through goes down the lane first and turns late.
        // A corridor between two cards in a row is about forty pixels wide and the edge
        // crossing it travels several hundred sideways, so a curve that starts turning
        // at the top has left the corridor long before it is past the row — which is
        // exactly how the line from the actor came to be drawn across Email / IMAP.
        EdgePath Beside(double lane)
        {
            var startY = toCy > fromCy ? from.Y + from.Height : from.Y;
            var endY = toCy > fromCy ? to.Y : to.Y + to.Height;
            return new EdgePath(fromCx, startY, lane, startY, lane, endY, toCx, endY);
        }

        // Runs at a given height, leaving and entering each card by whichever of its
        // edges faces that height — so a lane above the pair is joined top-to-top and
        // one below it bottom-to-bottom, and the line never doubles back through a card
        // to reach its own end.
        EdgePath Along(double lane)
        {
            var startY = lane > fromCy ? from.Y + from.Height : from.Y;
            var endY = lane > toCy ? to.Y + to.Height : to.Y;
            return new EdgePath(fromCx, startY, fromCx, lane, toCx, lane, toCx, endY);
        }

        EdgePath Through(double lane)
        {
            var startY = toCy > fromCy ? from.Y + from.Height : from.Y;
            var endY = toCy > fromCy ? to.Y : to.Y + to.Height;
            return new EdgePath(fromCx, startY, lane, endY, lane, endY, toCx, endY);
        }

        // Every edge is offered both families, in the order its own shape prefers.
        //
        // Which family suits an edge is a matter of degree, not a category: the one
        // from the actor down into the frame is a few pixels wider than it is tall, so
        // it counted as horizontal, was offered only the arcs — and every arc landed on
        // something, because it has a whole row to cross. What it needed was a corridor
        // between two cards in that row, which it was never shown. Offering both, in a
        // sensible order, costs one more pass and removes the cliff entirely.
        var overFirst = !vertical;

        var arcs = new List<EdgePath>();

        // The band between two rows before a clearance above everything. Two cards in
        // one row reach each other through the gap under that row; lifting the line
        // over the tallest card on the page instead sends its two ends straight down
        // through whatever happens to sit above them.
        foreach (var lane in Corridors(across.Select(node => (node.Y, node.Y + node.Height)), (from.Y + to.Y) / 2))
        {
            arcs.Add(Along(lane));
        }

        arcs.Add(Over(34));
        arcs.Add(Under(34));
        arcs.Add(Over(78));
        arcs.Add(Under(78));

        var goRight = Math.Abs(rightEdge - right) <= Math.Abs(left - leftEdge);
        var sides = new List<EdgePath>();

        // Through a gap before round the outside. A row of boxes is mostly gaps, and
        // crossing one costs a fraction of the distance going round seven cards does.
        foreach (var lane in Corridors(between.Select(node => (node.X, node.X + node.Width)), (fromCx + toCx) / 2))
        {
            sides.Add(Through(lane));
            sides.Add(Beside(lane));
        }

        sides.Add(Through(goRight ? rightEdge + 34 : leftEdge - 34));
        sides.Add(Beside(goRight ? rightEdge + 34 : leftEdge - 34));
        sides.Add(Through(goRight ? leftEdge - 34 : rightEdge + 34));
        sides.Add(Beside(goRight ? leftEdge - 34 : rightEdge + 34));
        sides.Add(Through(goRight ? rightEdge + 88 : leftEdge - 88));
        sides.Add(Beside(goRight ? rightEdge + 88 : leftEdge - 88));
        sides.Add(Through(goRight ? leftEdge - 88 : rightEdge + 88));
        sides.Add(Beside(goRight ? leftEdge - 88 : rightEdge + 88));

        foreach (var candidate in overFirst ? arcs.Concat(sides) : sides.Concat(arcs))
        {
            yield return candidate;
        }
    }

    /// <summary>
    /// The middles of the clear gaps between a set of spans, nearest first.
    /// <para>
    /// A row of boxes is mostly gaps, and a line crossing the row through one of them
    /// travels a fraction of the distance it would going round the outside — which on
    /// a seven-card row is most of the width of the picture. Ordered by how far each
    /// corridor is from where the line wanted to go, so the detour taken is the
    /// smallest one that works.
    /// </para>
    /// <para>
    /// Takes spans rather than cards because both axes need it. Sideways it finds the
    /// gaps between cards in a row; downwards it finds the band between two rows, which
    /// is where an edge joining two cards in the same row belongs — arcing clear of the
    /// tallest card on the page instead put the line through whatever sat directly
    /// above its own ends.
    /// </para>
    /// <para>
    /// Spans are merged before the gaps are read, so two cards that overlap on this
    /// axis — the whole of a row, seen sideways-on — count once rather than reporting
    /// the space between their leading edges as a corridor.
    /// </para>
    /// <para>
    /// A gap narrower than a card's own margin is skipped: threading a line through it
    /// would clear both boxes by the width of the line and read as touching them.
    /// </para>
    /// </summary>
    private static IEnumerable<double> Corridors(IEnumerable<(double Start, double End)> spans, double preferred)
    {
        const double narrowest = 30;

        var ordered = spans.OrderBy(span => span.Start).ToList();
        var lanes = new List<double>();
        var reach = double.NegativeInfinity;

        foreach (var span in ordered)
        {
            if (reach > double.NegativeInfinity && span.Start - reach >= narrowest)
            {
                lanes.Add((reach + span.Start) / 2);
            }

            reach = Math.Max(reach, span.End);
        }

        return lanes.OrderBy(lane => Math.Abs(lane - preferred));
    }

    /// <summary>One candidate path: its two ends and its two control points.</summary>
    private readonly record struct EdgePath(
        double X1, double Y1,
        double C1X, double C1Y,
        double C2X, double C2Y,
        double X2, double Y2);

    /// <summary>
    /// Whether a curve passes through any card that is not one of its own ends.
    /// <para>
    /// Sampled along the curve rather than solved. A cubic against a rectangle has a
    /// closed form and it is not worth writing: these are twenty-odd boxes on a page,
    /// the samples are a few dozen points, and a miss between two samples is a graze
    /// nobody would call a crossing.
    /// </para>
    /// </summary>
    private static bool Crosses(
        double x1, double y1,
        double c1x, double c1y,
        double c2x, double c2y,
        double x2, double y2,
        List<C4PlacedNode> obstacles)
    {
        if (obstacles.Count == 0) return false;

        const int samples = 28;
        const double inset = 4;

        for (var step = 1; step < samples; step++)
        {
            var t = step / (double)samples;
            var (px, py) = Point(t, x1, y1, c1x, c1y, c2x, c2y, x2, y2);

            foreach (var node in obstacles)
            {
                if (px > node.X + inset && px < node.X + node.Width - inset
                    && py > node.Y + inset && py < node.Y + node.Height - inset)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>A point on the cubic at <paramref name="t"/>.</summary>
    private static (double X, double Y) Point(
        double t,
        double x1, double y1,
        double c1x, double c1y,
        double c2x, double c2y,
        double x2, double y2)
    {
        var u = 1 - t;
        var a = u * u * u;
        var b = 3 * u * u * t;
        var c = 3 * u * t * t;
        var d = t * t * t;

        return (a * x1 + b * c1x + c * c2x + d * x2, a * y1 + b * c1y + c * c2y + d * y2);
    }

    /// <summary>The curve's own middle, which is where a label belongs — not the point
    /// halfway between the endpoints, which on a detour is inside the card the detour
    /// went around.</summary>
    private static (double X, double Y) Midpoint(
        double x1, double y1,
        double c1x, double c1y,
        double c2x, double c2y,
        double x2, double y2) =>
        Point(0.5, x1, y1, c1x, c1y, c2x, c2y, x2, y2);

    /// <summary>
    /// Where two labels would land on the same spot, nudges them apart.
    /// <para>
    /// The failure this avoids is the one the mermaid rendering had all over it: three
    /// edges through the middle of a diagram, three labels printed on top of each
    /// other, and none of them readable.
    /// </para>
    /// </summary>
    private static List<C4PlacedEdge> Spread(List<C4PlacedEdge> edges, List<C4PlacedNode> placed)
    {
        var spread = new List<C4PlacedEdge>(edges.Count);
        var used = new List<(double X, double Y)>();

        foreach (var edge in edges)
        {
            var x = edge.LabelX;
            var y = edge.LabelY;
            var step = 0;

            // Nudged clear of other labels *and* of any card the edge does not touch.
            // Only checking labels was not enough: a nudge of up to eight steps moves a
            // label the better part of two hundred pixels, which was quite far enough
            // to walk it off its own curve and onto somebody else's box.
            while (step < 8 && (Taken(used, x, y) || OnACard(placed, edge, x, y)))
            {
                step++;
                y += 22;
            }

            // Every position was occupied, so the original is put back: a label on its
            // own curve reads better than one parked eight steps away from the line it
            // belongs to.
            if (step == 8 && OnACard(placed, edge, x, y)) (x, y) = (edge.LabelX, edge.LabelY);

            used.Add((x, y));
            spread.Add(edge with { LabelX = x, LabelY = y });
        }

        return spread;

        static bool Taken(List<(double X, double Y)> used, double x, double y) =>
            used.Any(seen => Math.Abs(seen.X - x) < 120 && Math.Abs(seen.Y - y) < 22);

        static bool OnACard(List<C4PlacedNode> placed, C4PlacedEdge edge, double x, double y) =>
            placed.Any(node =>
                node.Alias != edge.FromAlias && node.Alias != edge.ToAlias
                && x > node.X && x < node.X + node.Width
                && y > node.Y && y < node.Y + node.Height);
    }

    /// <summary>The two points an edge runs between: the facing sides of the cards,
    /// so a line never starts inside the box it leaves.</summary>
    private static (double X1, double Y1, double X2, double Y2) Anchors(C4PlacedNode from, C4PlacedNode to)
    {
        var fromCx = from.X + from.Width / 2;
        var fromCy = from.Y + from.Height / 2;
        var toCx = to.X + to.Width / 2;
        var toCy = to.Y + to.Height / 2;

        if (Math.Abs(toCy - fromCy) >= Math.Abs(toCx - fromCx))
        {
            return toCy > fromCy
                ? (fromCx, from.Y + from.Height, toCx, to.Y)
                : (fromCx, from.Y, toCx, to.Y + to.Height);
        }

        return toCx > fromCx
            ? (from.X + from.Width, fromCy, to.X, toCy)
            : (from.X, fromCy, to.X + to.Width, toCy);
    }

    private static bool IsExternal(C4Workspace workspace, C4View view, string elementId)
    {
        if (view.Kind is C4ViewKind.SystemLandscape or C4ViewKind.Deployment) return false;
        if (view.ScopeId is null) return false;

        return !workspace.Ancestry(elementId)
            .Any(step => string.Equals(step.Id, view.ScopeId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>What goes in a card's second line, in square brackets, the way every
    /// C4 tool writes it.</summary>
    public static string KindLabel(C4ElementKind kind) => kind switch
    {
        C4ElementKind.Person => "Person",
        C4ElementKind.SoftwareSystem or C4ElementKind.SoftwareSystemInstance => "Software System",
        C4ElementKind.Container or C4ElementKind.ContainerInstance => "Container",
        C4ElementKind.Component => "Component",
        C4ElementKind.DeploymentNode => "Deployment Node",
        C4ElementKind.InfrastructureNode => "Infrastructure Node",
        _ => "Element"
    };
}
