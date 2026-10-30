using System.Text.RegularExpressions;

namespace Backlog.UI.Components.Devbook;

/// <summary>The outline a flowchart node is drawn with.</summary>
public enum DomainFlowchartShape
{
    /// <summary><c>A["…"]</c>, or a bare id: the lifecycle's card.</summary>
    Box,

    /// <summary><c>A("…")</c>, <c>A(["…"])</c>, <c>A(("…"))</c>: a card with round ends.</summary>
    Round,

    /// <summary><c>A{"…"}</c>, <c>A{{"…"}}</c>: a question, a card with pointed ends.</summary>
    Decision
}

/// <summary>How a flowchart link is drawn.</summary>
public enum DomainFlowchartLine
{
    /// <summary><c>--&gt;</c>, <c>---</c>.</summary>
    Solid,

    /// <summary><c>-.-&gt;</c>, <c>-.-</c>: dashed, as the lifecycle's ways back are.</summary>
    Dotted,

    /// <summary><c>==&gt;</c>, <c>===</c>.</summary>
    Thick
}

/// <summary>A node of a flowchart.</summary>
/// <param name="Label">Its text, a <c>&lt;br/&gt;</c> read as a line break; its id when it has none.</param>
public sealed record DomainFlowchartNode(string Id, string Label, DomainFlowchartShape Shape);

/// <summary>A link of a flowchart.</summary>
/// <param name="Arrow">Whether it ends in an arrowhead (<c>--&gt;</c>) or not (<c>---</c>).</param>
public sealed record DomainFlowchartLink(string From, string To, string Label, DomainFlowchartLine Line, bool Arrow);

/// <summary>A <c>flowchart</c> (or <c>graph</c>) the Flow page draws itself.</summary>
/// <param name="Across">True for <c>LR</c>, ranks left to right; false for
/// <c>TD</c>/<c>TB</c>, ranks top to bottom.</param>
/// <param name="Nodes">In the order the source first names them.</param>
/// <param name="Links">In source order.</param>
public sealed record DomainFlowchartDiagram(bool Across, IReadOnlyList<DomainFlowchartNode> Nodes, IReadOnlyList<DomainFlowchartLink> Links) : DomainFlowFigure
{
    public DomainFlowchartNode Node(string id) => Nodes.First(node => node.Id == id);
}

/// <summary>A node's card as drawn: where it sits, its wrapped lines, and its rank.</summary>
public sealed record DomainFlowchartBox(DomainFlowchartNode Node, double X, double Y, double Width, double Height, IReadOnlyList<string> Lines, int Rank)
{
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
}

/// <summary>A link as drawn: its path, and where its label's lines sit.</summary>
/// <param name="Back">Whether it runs against the ranks — a loop back — and so
/// round the outside of the drawing.</param>
/// <param name="LabelAnchor">The SVG <c>text-anchor</c> of the label.</param>
public sealed record DomainFlowchartEdge(DomainFlowchartLink Link, string Path, IReadOnlyList<string> LabelLines, double LabelX, double LabelY, string LabelAnchor, bool Back);

/// <summary>A flowchart laid out.</summary>
public sealed record DomainFlowchartLayout(double Width, double Height, IReadOnlyList<DomainFlowchartBox> Boxes, IReadOnlyList<DomainFlowchartEdge> Edges);

/// <summary>
/// Reads a flow chapter's <c>flowchart</c> and lays it out in ranks, in the
/// lifecycle's cards and lines (local ADR 0044, the Flow page): each node a rank
/// past every node that links to it, every line running between the ranks with a
/// rounded turn, and a link back to an earlier rank round the outside. Pure, so
/// the storybook draws the same chart from a sample string.
/// <para>
/// A chart this cannot hold — subgraphs, <c>&amp;</c> groups, a link from a node to
/// itself, a shape other than a card, a round card or a question — reads as
/// <see langword="null"/>, and its fence keeps the Mermaid rendering.
/// </para>
/// </summary>
public static partial class DomainFlowcharts
{
    private const double Margin = 16;
    private const double PadX = 14;
    private const double PadY = 9;
    private const double LineHeight = 18;
    private const double TextCharWidth = 7.4;
    private const double LabelCharWidth = 6.6;
    private const double LabelLineHeight = 15;
    private const double MinWidth = 96;
    private const double Chamfer = 14;
    private const double CrossGap = 28;
    private const double DummyWidth = 12;
    private const double TurnBase = 14;
    private const double LaneGap = 10;
    private const double FinalBase = 22;
    private const double PortInset = 14;
    private const int MaxNodeChars = 30;
    private const int MaxLabelChars = 28;

    /// <summary>Reads a Mermaid source as a flowchart, or <see langword="null"/>
    /// when it is not one this can draw.</summary>
    public static DomainFlowchartDiagram? Parse(string? source)
    {
        var lines = (source ?? string.Empty).Replace("\r\n", "\n").Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("%%", StringComparison.Ordinal))
            .ToList();
        if (lines.Count == 0) return null;
        var header = Header().Match(lines[0]);
        if (!header.Success) return null;

        var nodes = new Dictionary<string, DomainFlowchartNode>(StringComparer.Ordinal);
        var order = new List<string>();
        var links = new List<DomainFlowchartLink>();
        foreach (var raw in lines.Skip(1))
        {
            var line = raw.TrimEnd(';').TrimEnd();
            var word = line.Split(' ', 2)[0];
            if (word is "classDef" or "class" or "style" or "linkStyle") continue;
            if (word is "subgraph" or "end" or "click" or "direction") return null;

            var at = 0;
            if (ReadNode(line, ref at) is not { } from) return null;
            Declare(from);
            while (true)
            {
                SkipSpaces(line, ref at);
                if (at >= line.Length) break;
                var link = Link().Match(line, at);
                if (!link.Success || link.Index != at) return null;
                at += link.Length;
                SkipSpaces(line, ref at);
                if (ReadNode(line, ref at) is not { } to) return null;
                Declare(to);
                if (to.Id == from.Id) return null;
                links.Add(Linked(from.Id, to.Id, link));
                from = to;
            }
        }

        if (links.Count == 0) return null;
        return new DomainFlowchartDiagram(header.Groups[2].Value == "LR", [.. order.Select(id => nodes[id])], links);

        void Declare((string Id, string? Label, DomainFlowchartShape Shape) node)
        {
            if (!nodes.ContainsKey(node.Id)) order.Add(node.Id);
            if (node.Label is not null) nodes[node.Id] = new DomainFlowchartNode(node.Id, node.Label, node.Shape);
            else nodes.TryAdd(node.Id, new DomainFlowchartNode(node.Id, node.Id, DomainFlowchartShape.Box));
        }
    }

    private static DomainFlowchartLink Linked(string from, string to, Match link)
    {
        var op = link.Groups["op"].Success ? link.Groups["op"].Value : link.Groups["close"].Value;
        var label = link.Groups["pipe"].Success ? link.Groups["pipe"].Value
            : link.Groups["text"].Success ? link.Groups["text"].Value
            : string.Empty;
        var line = op.Contains('.') ? DomainFlowchartLine.Dotted
            : op.Contains('=') ? DomainFlowchartLine.Thick
            : DomainFlowchartLine.Solid;
        return new DomainFlowchartLink(from, to, Text(label), line, op.EndsWith('>'));
    }

    /// <summary>A node at <paramref name="at"/>: its id, and its label and shape
    /// when it declares them there.</summary>
    private static (string Id, string? Label, DomainFlowchartShape Shape)? ReadNode(string line, ref int at)
    {
        var id = NodeId().Match(line, at);
        if (!id.Success || id.Index != at) return null;
        at += id.Length;

        (string Open, string Close, DomainFlowchartShape Shape)[] shapes =
        [
            ("([", "])", DomainFlowchartShape.Round),
            ("((", "))", DomainFlowchartShape.Round),
            ("{{", "}}", DomainFlowchartShape.Decision),
            ("[", "]", DomainFlowchartShape.Box),
            ("(", ")", DomainFlowchartShape.Round),
            ("{", "}", DomainFlowchartShape.Decision)
        ];
        string? label = null;
        var shape = DomainFlowchartShape.Box;
        foreach (var (open, close, kind) in shapes)
        {
            if (string.CompareOrdinal(line, at, open, 0, open.Length) != 0) continue;
            var start = at + open.Length;
            int end;
            string text;
            if (start < line.Length && line[start] == '"')
            {
                var quote = line.IndexOf('"', start + 1);
                if (quote < 0 || string.CompareOrdinal(line, quote + 1, close, 0, close.Length) != 0) return null;
                text = line[(start + 1)..quote];
                end = quote + 1;
            }
            else
            {
                end = line.IndexOf(close, start, StringComparison.Ordinal);
                if (end < 0) return null;
                text = line[start..end];
                if (text.IndexOfAny(['[', '(', '{', '"']) >= 0) return null; // a shape this does not draw
            }
            label = Text(text);
            shape = kind;
            at = end + close.Length;
            break;
        }

        var style = ClassSuffix().Match(line, at);
        if (style.Success && style.Index == at) at += style.Length;
        return (id.Value, label, shape);
    }

    private static void SkipSpaces(string line, ref int at)
    {
        while (at < line.Length && char.IsWhiteSpace(line[at])) at++;
    }

    private static string Text(string raw) =>
        LineBreak().Replace(raw.Trim().Trim('"'), "\n").Replace("#quot;", "\"", StringComparison.Ordinal).Trim();

    /// <summary>
    /// Lays the chart out in ranks: a walk from the nodes nothing links to marks
    /// the links back; every other link runs from a rank to a later one, a node
    /// taking the rank after the furthest node linking to it. A link that skips
    /// ranks runs through a slot in each rank it passes, so it never crosses a card.
    /// Within a rank the nodes sit in the order that keeps the lines straightest,
    /// each as near the middle of the nodes linking to it as its neighbours allow.
    /// </summary>
    public static DomainFlowchartLayout Layout(DomainFlowchartDiagram chart)
    {
        var across = chart.Across;
        var count = chart.Nodes.Count;
        var index = chart.Nodes.Select((node, i) => (node.Id, i)).ToDictionary(pair => pair.Id, pair => pair.i, StringComparer.Ordinal);
        var links = chart.Links.Select(link => (From: index[link.From], To: index[link.To])).ToList();

        // Links back: a link to a node still on the walk.
        var outgoing = Enumerable.Range(0, count).Select(_ => new List<int>()).ToArray();
        for (var i = 0; i < links.Count; i++) outgoing[links[i].From].Add(i);
        var back = new HashSet<int>();
        var walk = new int[count];
        var hasIncoming = new bool[count];
        foreach (var (_, to) in links) hasIncoming[to] = true;
        foreach (var root in Enumerable.Range(0, count).OrderBy(i => hasIncoming[i] ? 1 : 0).ThenBy(i => i))
        {
            if (walk[root] == 0) Walk(root);
        }

        // Ranks: one past the furthest node linking forward to it.
        var rank = new int[count];
        var indegree = new int[count];
        for (var i = 0; i < links.Count; i++) if (!back.Contains(i)) indegree[links[i].To]++;
        var queue = new Queue<int>(Enumerable.Range(0, count).Where(i => indegree[i] == 0));
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            foreach (var i in outgoing[node].Where(i => !back.Contains(i)))
            {
                var to = links[i].To;
                rank[to] = Math.Max(rank[to], rank[node] + 1);
                if (--indegree[to] == 0) queue.Enqueue(to);
            }
        }
        var ranks = rank.DefaultIfEmpty(0).Max() + 1;

        // Sizes, and the slots: each node, and each rank a forward link skips.
        var lines = chart.Nodes.Select(node => DomainFlows.Wrap(node.Label, MaxNodeChars)).ToArray();
        var widths = new double[count];
        var heights = new double[count];
        for (var i = 0; i < count; i++)
        {
            var text = lines[i].Max(line => line.Length) * TextCharWidth + 2 * PadX
                + (chart.Nodes[i].Shape == DomainFlowchartShape.Decision ? 2 * Chamfer - 8 : 0)
                + (chart.Nodes[i].Shape == DomainFlowchartShape.Round ? 8 : 0);
            widths[i] = Math.Max(MinWidth, text);
            heights[i] = lines[i].Count * LineHeight + 2 * PadY;
        }
        var labels = chart.Links.Select(link => link.Label.Length == 0 ? (IReadOnlyList<string>)[] : DomainFlows.Wrap(link.Label, MaxLabelChars)).ToArray();
        double LabelWidth(int link) => labels[link].Count == 0 ? 0 : labels[link].Max(line => line.Length) * LabelCharWidth;

        var slotRank = new List<int>(rank);
        var slotCross = new List<double>();
        var slotMain = new List<double>();
        for (var i = 0; i < count; i++)
        {
            slotCross.Add(across ? heights[i] : widths[i]);
            slotMain.Add(across ? widths[i] : heights[i]);
        }
        var chains = new List<int>[links.Count];
        for (var i = 0; i < links.Count; i++)
        {
            if (back.Contains(i)) continue;
            var chain = new List<int> { links[i].From };
            for (var r = rank[links[i].From] + 1; r < rank[links[i].To]; r++)
            {
                chain.Add(slotRank.Count);
                slotRank.Add(r);
                slotCross.Add(DummyWidth);
                slotMain.Add(0);
            }
            chain.Add(links[i].To);
            chains[i] = chain;
        }
        var slots = slotRank.Count;

        // A label sits on the stretch of its line at the end that has fewer
        // lines meeting it, so the labels of a fan never stack on one side: by
        // the target where a node fans out, by the source where lines gather.
        var fanOut = new int[count];
        var fanIn = new int[count];
        for (var i = 0; i < links.Count; i++)
        {
            if (back.Contains(i)) continue;
            fanOut[links[i].From]++;
            fanIn[links[i].To]++;
        }
        var nearSource = Enumerable.Range(0, links.Count).Select(i => fanOut[links[i].From] < fanIn[links[i].To]).ToArray();

        // Down the page the label stands beside that stretch, as the row's labels
        // stand beside their lines, so the node's slot leaves room for it on
        // either side of its middle.
        var room = slotCross.ToArray();
        if (!across)
        {
            for (var i = 0; i < links.Count; i++)
            {
                if (back.Contains(i) || labels[i].Count == 0) continue;
                var at = nearSource[i] ? links[i].From : links[i].To;
                room[at] = Math.Max(room[at], slotCross[at] + 2 * (LabelWidth(i) + 10));
            }
        }

        var before = Enumerable.Range(0, slots).Select(_ => new List<int>()).ToArray();
        var after = Enumerable.Range(0, slots).Select(_ => new List<int>()).ToArray();
        foreach (var chain in chains.Where(chain => chain is not null))
        {
            for (var k = 0; k + 1 < chain.Count; k++)
            {
                after[chain[k]].Add(chain[k + 1]);
                before[chain[k + 1]].Add(chain[k]);
            }
        }

        // Order within each rank: source order, then sweeps toward the middle of
        // each slot's neighbours.
        var layers = Enumerable.Range(0, ranks).Select(r => Enumerable.Range(0, slots).Where(s => slotRank[s] == r).ToList()).ToArray();
        var place = new double[slots];
        void Number()
        {
            foreach (var layer in layers) for (var k = 0; k < layer.Count; k++) place[layer[k]] = k;
        }
        Number();
        for (var sweep = 0; sweep < 4; sweep++)
        {
            for (var r = 1; r < ranks; r++) Sort(layers[r], before);
            for (var r = ranks - 2; r >= 0; r--) Sort(layers[r], after);
        }

        // Across the ranks: each slot as near the middle of what feeds it as the
        // slots beside it allow; the first rank packed and centred.
        var cross = new double[slots];
        double Separation(int a, int b) => (room[a] + room[b]) / 2 + (slotMain[a] == 0 || slotMain[b] == 0 ? CrossGap / 2 : CrossGap);
        var widest = layers.Select(layer => layer.Sum(s => room[s]) + Math.Max(0, layer.Count - 1) * CrossGap).DefaultIfEmpty(0).Max();
        for (var r = 0; r < ranks; r++)
        {
            var layer = layers[r];
            var span = layer.Sum(s => room[s]) + Math.Max(0, layer.Count - 1) * CrossGap;
            var x = (widest - span) / 2;
            var packed = new double[layer.Count];
            for (var k = 0; k < layer.Count; k++)
            {
                packed[k] = x + room[layer[k]] / 2;
                x += room[layer[k]] + CrossGap;
            }
            if (r == 0)
            {
                for (var k = 0; k < layer.Count; k++) cross[layer[k]] = packed[k];
                continue;
            }

            var wanted = layer.Select((s, k) => before[s].Count > 0 ? before[s].Average(p => cross[p]) : packed[k]).ToArray();
            var at = new double[layer.Count];
            for (var k = 0; k < layer.Count; k++)
            {
                at[k] = k == 0 ? wanted[k] : Math.Max(wanted[k], at[k - 1] + Separation(layer[k - 1], layer[k]));
            }
            var shift = Enumerable.Range(0, layer.Count).Average(k => wanted[k] - at[k]);
            for (var k = 0; k < layer.Count; k++) cross[layer[k]] = at[k] + shift;
        }
        var low = Enumerable.Range(0, slots).Select(s => cross[s] - room[s] / 2).DefaultIfEmpty(0).Min();
        for (var s = 0; s < slots; s++) cross[s] += Margin - low;
        var crossEnd = Enumerable.Range(0, slots).Select(s => cross[s] + room[s] / 2).DefaultIfEmpty(Margin).Max();

        // Ports: the ends of a node's lines spread along its side, in the order
        // of the slots at their other ends.
        var exitPort = new Dictionary<(int Link, int Slot), double>();
        var entryPort = new Dictionary<(int Link, int Slot), double>();
        var routeOut = Enumerable.Range(0, count).Select(_ => new List<(int Link, double Toward)>()).ToArray();
        var routeIn = Enumerable.Range(0, count).Select(_ => new List<(int Link, double Toward)>()).ToArray();
        for (var i = 0; i < links.Count; i++)
        {
            var (from, to) = links[i];
            if (back.Contains(i))
            {
                routeOut[from].Add((i, double.MaxValue));
                routeIn[to].Add((i, double.MaxValue));
                continue;
            }
            routeOut[from].Add((i, cross[chains[i][1]]));
            routeIn[to].Add((i, cross[chains[i][^2]]));
        }
        for (var node = 0; node < count; node++)
        {
            Spread(node, routeOut[node], exitPort);
            Spread(node, routeIn[node], entryPort);
        }

        // Runs: every stretch of a line across the space between two ranks — and
        // the space before the first rank and after the last, for the links back —
        // each in a lane of its own unless it shares an end.
        var runs = new List<Run>();
        var farLane = new Dictionary<int, int>();
        for (var i = 0; i < links.Count; i++)
        {
            var (from, to) = links[i];
            if (back.Contains(i))
            {
                farLane[i] = farLane.Count;
                var far = crossEnd + 20 + farLane[i] * LaneGap;
                runs.Add(new Run(i, rank[from], exitPort[(i, from)], far, from, -1 - i));
                runs.Add(new Run(i, rank[to] - 1, far, entryPort[(i, to)], -1 - i, to));
                continue;
            }
            var chain = chains[i];
            for (var k = 0; k + 1 < chain.Count; k++)
            {
                var a = chain[k];
                var b = chain[k + 1];
                var ca = k == 0 ? exitPort[(i, a)] : cross[a];
                var cb = k + 1 == chain.Count - 1 ? entryPort[(i, b)] : cross[b];
                // A jog of a few pixels reads as a wobble: run straight instead,
                // moving whichever end is a port.
                if (Math.Abs(ca - cb) < 8)
                {
                    if (k + 1 == chain.Count - 1) cb = ca;
                    else if (k == 0) ca = cb;
                }
                runs.Add(new Run(i, slotRank[a], ca, cb, a, b));
            }
        }
        var lanes = new Dictionary<int, int>();
        foreach (var gap in runs.GroupBy(run => run.Gap))
        {
            var taken = new List<List<Run>>();
            foreach (var run in gap.OrderBy(run => run.Low).ThenBy(run => run.High))
            {
                var lane = 0;
                while (lane < taken.Count && taken[lane].Any(other => Clash(run, other))) lane++;
                if (lane == taken.Count) taken.Add([]);
                taken[lane].Add(run);
                run.Lane = lane;
            }
            lanes[gap.Key] = taken.Count;
        }

        // Along the ranks: each rank as thick as its largest node, each gap as
        // wide as its lanes and the labels on its first and last stretches.
        double Region(int gap, bool first, double least)
        {
            var labelled = Enumerable.Range(0, links.Count)
                .Where(i => !back.Contains(i) && labels[i].Count > 0 && nearSource[i] == first
                    && (first ? rank[links[i].From] == gap : rank[links[i].To] == gap + 1))
                .ToList();
            if (labelled.Count == 0) return least;
            return across
                ? Math.Max(least, labelled.Max(LabelWidth) + 24)
                : least + labelled.Max(i => labels[i].Count) * LabelLineHeight + 4;
        }
        bool Outer(int gap) => gap < 0 || gap >= ranks - 1;
        double Lead(int gap) => Outer(gap) ? TurnBase : Region(gap, true, TurnBase);
        double GapSize(int gap) => lanes.TryGetValue(gap, out var used)
            ? Lead(gap) + (used - 1) * LaneGap + (Outer(gap) ? 14 : Region(gap, false, FinalBase))
            : Outer(gap) ? 0 : Lead(gap) + Region(gap, false, FinalBase);

        var rankStart = new double[ranks];
        var thick = new double[ranks];
        var gapStart = new Dictionary<int, double>();
        var main = Margin;
        gapStart[-1] = main;
        main += GapSize(-1);
        for (var r = 0; r < ranks; r++)
        {
            rankStart[r] = main;
            thick[r] = Enumerable.Range(0, slots).Where(s => slotRank[s] == r).Select(s => slotMain[s]).DefaultIfEmpty(0).Max();
            main += thick[r];
            gapStart[r] = main;
            main += GapSize(r);
        }
        var mainEnd = main + Margin;

        double MainStart(int slot) => rankStart[slotRank[slot]] + (thick[slotRank[slot]] - slotMain[slot]) / 2;
        double MainEnd(int slot) => slotMain[slot] == 0 ? rankStart[slotRank[slot]] + thick[slotRank[slot]] : MainStart(slot) + slotMain[slot];
        double Turn(Run run) => gapStart[run.Gap] + Lead(run.Gap) + run.Lane * LaneGap;

        (double X, double Y) P(double crossAt, double mainAt) => across ? (mainAt, crossAt) : (crossAt, mainAt);

        var boxes = new List<DomainFlowchartBox>();
        for (var i = 0; i < count; i++)
        {
            var (x, y) = P(cross[i] - (across ? heights[i] : widths[i]) / 2, MainStart(i));
            boxes.Add(new DomainFlowchartBox(chart.Nodes[i], x, y, widths[i], heights[i], lines[i], rank[i]));
        }

        var edges = new List<DomainFlowchartEdge>();
        var labelEnd = crossEnd;
        for (var i = 0; i < links.Count; i++)
        {
            var mine = runs.Where(run => run.Link == i).ToList();
            var points = new List<(double X, double Y)>();
            if (back.Contains(i))
            {
                var (outRun, inRun) = (mine[0], mine[1]);
                var far = outRun.CrossB;
                var exit = MainEnd(links[i].From);
                var entry = MainStart(links[i].To);
                var t1 = Turn(outRun);
                var t2 = Turn(inRun);
                points.Add(P(outRun.CrossA, exit));
                points.Add(P(outRun.CrossA, t1));
                points.Add(P(far, t1));
                points.Add(P(far, t2));
                points.Add(P(inRun.CrossB, t2));
                points.Add(P(inRun.CrossB, entry));
                var middle = (t1 + t2) / 2;
                var (lx, ly) = across ? (middle, far + 14) : (far + 6, middle - (labels[i].Count - 1) * LabelLineHeight / 2 + 4);
                labelEnd = Math.Max(labelEnd, across ? far + 4 + labels[i].Count * LabelLineHeight : far + 6 + LabelWidth(i));
                edges.Add(new DomainFlowchartEdge(chart.Links[i], DomainFlows.Rounded(points), labels[i], lx, ly, across ? "middle" : "start", true));
                continue;
            }

            (double From, double To, double Cross) stretch = default;
            for (var k = 0; k < mine.Count; k++)
            {
                var run = mine[k];
                var start = MainEnd(run.A);
                var turn = Turn(run);
                var entry = MainStart(run.B);
                points.Add(P(run.CrossA, start));
                points.Add(P(run.CrossA, turn));
                points.Add(P(run.CrossB, turn));
                points.Add(P(run.CrossB, entry));
                if (nearSource[i] && k == 0) stretch = (start, turn, run.CrossA);
                if (!nearSource[i] && k == mine.Count - 1) stretch = (turn, entry, run.CrossB);
            }
            var mid = (stretch.From + stretch.To) / 2;
            var (labelX, labelY) = across
                ? (mid, stretch.Cross - 6 - (labels[i].Count - 1) * LabelLineHeight)
                : (stretch.Cross + 6, mid - (labels[i].Count - 1) * LabelLineHeight / 2 + 4);
            edges.Add(new DomainFlowchartEdge(chart.Links[i], DomainFlows.Rounded(points), labels[i], labelX, labelY, across ? "middle" : "start", false));
        }

        var crossSize = Math.Max(crossEnd, labelEnd) + Margin;
        var (width, height) = across ? (mainEnd, crossSize) : (crossSize, mainEnd);
        return new DomainFlowchartLayout(width, height, boxes, edges);

        void Walk(int node)
        {
            walk[node] = 1;
            foreach (var i in outgoing[node])
            {
                var to = links[i].To;
                if (walk[to] == 1) back.Add(i);
                else if (walk[to] == 0) Walk(to);
            }
            walk[node] = 2;
        }

        void Sort(List<int> layer, List<int>[] neighbours)
        {
            var key = layer.ToDictionary(s => s, s => neighbours[s].Count > 0 ? neighbours[s].Average(n => place[n]) : place[s]);
            var sorted = layer.OrderBy(s => key[s]).ThenBy(s => place[s]).ToList();
            layer.Clear();
            layer.AddRange(sorted);
            for (var k = 0; k < layer.Count; k++) place[layer[k]] = k;
        }

        void Spread(int node, List<(int Link, double Toward)> ends, Dictionary<(int Link, int Slot), double> ports)
        {
            if (ends.Count == 0) return;
            var size = across ? heights[node] : widths[node];
            var start = cross[node] - size / 2;
            var pointed = chart.Nodes[node].Shape == DomainFlowchartShape.Decision;
            // A question's pointed end, across the page, is a single tip.
            if (across && pointed)
            {
                foreach (var end in ends) ports[(end.Link, node)] = cross[node];
                return;
            }
            var inset = Math.Min(pointed ? Chamfer + 6 : PortInset, size / 4);
            var sorted = ends.OrderBy(end => end.Toward).ThenBy(end => end.Link).ToList();
            for (var k = 0; k < sorted.Count; k++)
            {
                ports[(sorted[k].Link, node)] = sorted.Count == 1
                    ? cross[node]
                    : start + inset + (size - 2 * inset) * k / (sorted.Count - 1);
            }
        }
    }

    /// <summary>Two runs in one gap clash when their spans overlap, unless they
    /// leave the same node — a fork may share a lane; lines gathering on one node
    /// each keep their own, so every line can be followed to where it starts.</summary>
    private static bool Clash(Run run, Run other) =>
        run.Low < other.High + 6 && other.Low < run.High + 6
        && run.A != other.A;

    /// <summary>One stretch of a link across the gap after rank
    /// <see cref="Gap"/> (-1: before the first), from slot <see cref="A"/> at
    /// <see cref="CrossA"/> to slot <see cref="B"/> at <see cref="CrossB"/>; a
    /// negative slot is the outside lane of a link back.</summary>
    private sealed class Run(int link, int gap, double crossA, double crossB, int a, int b)
    {
        public int Link { get; } = link;
        public int Gap { get; } = gap;
        public double CrossA { get; } = crossA;
        public double CrossB { get; } = crossB;
        public int A { get; } = a;
        public int B { get; } = b;
        public double Low => Math.Min(CrossA, CrossB);
        public double High => Math.Max(CrossA, CrossB);
        public int Lane { get; set; }
    }

    [GeneratedRegex(@"^(flowchart|graph)\s+(TD|TB|LR)\s*;?$")]
    private static partial Regex Header();

    [GeneratedRegex(@"[A-Za-z0-9_]+")]
    private static partial Regex NodeId();

    [GeneratedRegex(@":::[A-Za-z0-9_-]+")]
    private static partial Regex ClassSuffix();

    [GeneratedRegex(@"(?:(?<op>-->|---|-\.->|-\.-|==>|===)(?:\|(?<pipe>[^|]*)\|)?|(?<open>--|-\.|==)\s+(?<text>[^|]+?)\s+(?<close>-->|---|\.->|\.-|==>|===))")]
    private static partial Regex Link();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreak();
}
