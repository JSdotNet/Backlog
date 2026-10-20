using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Backlog.UI.Components.Devbook;

/// <summary>A state of a flow's row: a named state, or the start or end
/// pseudo-state <c>[*]</c> stands for at either end.</summary>
/// <param name="Id">The state's name in the diagram source; <c>[*]start</c> and
/// <c>[*]end</c> for the pseudo-states.</param>
/// <param name="Label">What the box reads: its <c>state "…" as Id</c> label, else
/// its name.</param>
/// <param name="Index">Its place in the row, from the left.</param>
public sealed record DomainFlowState(string Id, string Label, int Index, DomainFlowStateKind Kind);

/// <summary>Whether a <see cref="DomainFlowState"/> is a state or a pseudo-state.</summary>
public enum DomainFlowStateKind
{
    State,
    Start,
    End
}

/// <summary>Which way a transition runs along the row, which is where it is drawn.</summary>
public enum DomainFlowDirection
{
    /// <summary>Rightward: on the axis to the next state, else above the row.</summary>
    Forward,

    /// <summary>Leftward, a way back: below the row.</summary>
    Back,

    /// <summary>From the last state back to the first: the restore loop, drawn
    /// round the outside, below every other way back.</summary>
    Restore,

    /// <summary>From a state to itself: a loop above it.</summary>
    Self
}

/// <summary>One transition of a flow, every label the source gives the same
/// pair joined with " / ".</summary>
public sealed record DomainFlowTransition(string From, string To, string Label, DomainFlowDirection Direction);

/// <summary>A Mermaid diagram of a flow chapter the Flow page draws itself, in
/// the lifecycle's cards, lines and type: a state row
/// (<see cref="DomainFlowDiagram"/>), a flowchart
/// (<see cref="DomainFlowchartDiagram"/>) or a sequence
/// (<see cref="DomainSequenceDiagram"/>).</summary>
public abstract record DomainFlowFigure
{
    // Closed to this assembly: the Flow page draws exactly these three.
    private protected DomainFlowFigure()
    {
    }
}

/// <summary>A <c>stateDiagram-v2</c> read as one row of states.</summary>
/// <param name="States">The states in row order, the pseudo-states at the ends.</param>
/// <param name="Transitions">In source order.</param>
public sealed record DomainFlowDiagram(IReadOnlyList<DomainFlowState> States, IReadOnlyList<DomainFlowTransition> Transitions) : DomainFlowFigure
{
    public DomainFlowState State(string id) => States.First(state => state.Id == id);
}

/// <summary>A state's box on the row.</summary>
public sealed record DomainFlowBox(DomainFlowState State, double X, double Y, double Width, double Height)
{
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
}

/// <summary>A transition as drawn: its path and where its label sits.</summary>
/// <param name="Label">The label as drawn, cut short when long.</param>
/// <param name="Level">Its lane above or below the row, from 0 nearest the row;
/// -1 for a transition drawn on the axis.</param>
public sealed record DomainFlowEdge(DomainFlowTransition Transition, string Path, double LabelX, double LabelY, string Label, int Level);

/// <summary>A flow row laid out: the size of the drawing, its boxes and its edges.</summary>
public sealed record DomainFlowLayout(double Width, double Height, IReadOnlyList<DomainFlowBox> Boxes, IReadOnlyList<DomainFlowEdge> Edges);

/// <summary>A flow chapter's prose split around its diagrams: the diagrams the
/// page draws itself, in source order, and the rest of the chapter's markdown.</summary>
public sealed record DomainFlowSection(DomainChapter Chapter, IReadOnlyList<DomainFlowFigure> Diagrams, string Prose);

/// <summary>
/// Reads a flow's <c>stateDiagram-v2</c> into one row of states and lays it out
/// (local ADR 0036, the Flow page): forward transitions above the row, the ways
/// back below, and the restore loop — the way from the last state back to the
/// first — round the outside. Pure, so the storybook draws the same row from a
/// sample string.
/// <para>
/// A diagram this row cannot hold — composite or concurrent states, choice and
/// fork pseudo-states, notes — reads as <see langword="null"/>. A flowchart and a
/// sequence are read by <see cref="DomainFlowcharts"/> and
/// <see cref="DomainSequences"/>; a fence none of the three can read keeps the
/// Mermaid rendering every other page gives it.
/// </para>
/// </summary>
public static partial class DomainFlows
{
    private const string StartId = "[*]start";
    private const string EndId = "[*]end";

    private const double Margin = 16;
    private const double BoxHeight = 36;
    private const double MinBoxWidth = 72;
    private const double BoxPadding = 16;
    private const double CharWidth = 8.0;
    private const double LabelCharWidth = 6.6;
    private const double PseudoSize = 16;
    private const double MinGap = 56;
    private const double LaneGap = 24;
    private const double FirstLane = 24;
    private const double Corner = 6;
    private const double PortInset = 10;
    private const int MaxLabel = 36;

    /// <summary>The section a flow chapter reads as: each diagram it holds that
    /// the page can draw — a state row, a flowchart or a sequence — and its
    /// markdown with those fences taken out. A fence none of them can read stays
    /// in the markdown, for Mermaid.</summary>
    public static DomainFlowSection Section(DomainChapter chapter)
    {
        var diagrams = new List<DomainFlowFigure>();
        var prose = new StringBuilder();
        var lines = chapter.Prose.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal)
                && string.Equals(trimmed[3..].Trim(), "mermaid", StringComparison.OrdinalIgnoreCase))
            {
                var end = i + 1;
                while (end < lines.Length && !lines[end].Trim().StartsWith("```", StringComparison.Ordinal)) end++;
                var source = string.Join('\n', lines[(i + 1)..Math.Min(end, lines.Length)]);
                if (Figure(source) is { } diagram)
                {
                    diagrams.Add(diagram);
                    i = end;
                    continue;
                }
            }
            prose.Append(lines[i]).Append('\n');
        }
        return new DomainFlowSection(chapter, diagrams, prose.ToString().Trim());
    }

    /// <summary>Reads a Mermaid source as whichever figure the page draws, or
    /// <see langword="null"/> when none of them can read it.</summary>
    public static DomainFlowFigure? Figure(string? source) =>
        (DomainFlowFigure?)Parse(source) ?? (DomainFlowFigure?)DomainFlowcharts.Parse(source) ?? DomainSequences.Parse(source);

    /// <summary>Reads a Mermaid source as a flow row, or <see langword="null"/>
    /// when it is not a <c>stateDiagram-v2</c> the row can hold.</summary>
    public static DomainFlowDiagram? Parse(string? source)
    {
        var lines = (source ?? string.Empty).Replace("\r\n", "\n").Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("%%", StringComparison.Ordinal))
            .ToList();
        if (lines.Count == 0 || lines[0] != "stateDiagram-v2") return null;

        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        var appearance = new List<string>();
        var raw = new List<(string From, string To, string Label)>();
        foreach (var line in lines.Skip(1))
        {
            if (line.StartsWith("direction ", StringComparison.Ordinal)
                || line.StartsWith("classDef ", StringComparison.Ordinal)
                || line.StartsWith("class ", StringComparison.Ordinal)
                || line.StartsWith("style ", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("state ", StringComparison.Ordinal))
            {
                var declared = StateAlias().Match(line);
                if (!declared.Success) return null; // composite, choice, fork, join
                labels[declared.Groups[2].Value] = declared.Groups[1].Value;
                Note(appearance, declared.Groups[2].Value);
                continue;
            }

            if (line.StartsWith("note ", StringComparison.Ordinal) || line == "--" || line.Contains('{') || line.Contains('}')) return null;

            var transition = Transition().Match(line);
            if (transition.Success)
            {
                var from = transition.Groups[1].Value;
                var to = transition.Groups[2].Value;
                var label = transition.Groups[3].Success ? transition.Groups[3].Value.Trim() : string.Empty;
                if (from == "[*]" && to == "[*]") return null;
                if (from != "[*]") Note(appearance, from);
                if (to != "[*]") Note(appearance, to);
                raw.Add((from == "[*]" ? StartId : from, to == "[*]" ? EndId : to, label));
                continue;
            }

            // `Idle : waiting` describes a state; the row names it and nothing more.
            if (StateDescription().Match(line) is { Success: true } described)
            {
                Note(appearance, described.Groups[1].Value);
                continue;
            }

            return null;
        }

        if (raw.Count == 0 || appearance.Count == 0) return null;

        var order = Order(appearance, raw);
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var states = new List<DomainFlowState>();
        if (raw.Any(edge => edge.From == StartId)) Add(StartId, "start", DomainFlowStateKind.Start);
        foreach (var id in order) Add(id, labels.GetValueOrDefault(id, id), DomainFlowStateKind.State);
        if (raw.Any(edge => edge.To == EndId)) Add(EndId, "end", DomainFlowStateKind.End);

        var first = order[0];
        var last = order[^1];
        var transitions = raw
            .GroupBy(edge => (edge.From, edge.To))
            .Select(group =>
            {
                var (from, to) = group.Key;
                var label = string.Join(" / ", group.Select(edge => edge.Label).Where(text => text.Length > 0).Distinct(StringComparer.Ordinal));
                var direction = from == to ? DomainFlowDirection.Self
                    : index[to] > index[from] ? DomainFlowDirection.Forward
                    : from == last && to == first && order.Count > 1 ? DomainFlowDirection.Restore
                    : DomainFlowDirection.Back;
                return new DomainFlowTransition(from, to, label, direction);
            })
            .ToList();

        return new DomainFlowDiagram(states, transitions);

        void Add(string id, string label, DomainFlowStateKind kind)
        {
            index[id] = states.Count;
            states.Add(new DomainFlowState(id, label, states.Count, kind));
        }
    }

    /// <summary>
    /// The row order of the named states: a walk from the start marks the ways
    /// back — a transition to a state still on the walk — and the rest are ordered
    /// so every one of them runs rightward, a tie going to the state the source
    /// names first.
    /// </summary>
    private static List<string> Order(List<string> appearance, List<(string From, string To, string Label)> edges)
    {
        var rank = appearance.Select((id, i) => (id, i)).ToDictionary(pair => pair.id, pair => pair.i, StringComparer.Ordinal);
        var next = appearance.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var (from, to, _) in edges)
        {
            if (from == StartId || to == EndId || from == to) continue;
            if (!next[from].Contains(to)) next[from].Add(to);
        }

        var back = new HashSet<(string, string)>();
        var state = new Dictionary<string, int>(StringComparer.Ordinal); // 1 on the walk, 2 done
        var roots = edges.Where(edge => edge.From == StartId).Select(edge => edge.To).Where(rank.ContainsKey)
            .Concat(appearance).Distinct(StringComparer.Ordinal);
        foreach (var root in roots)
        {
            if (!state.ContainsKey(root)) Walk(root);
        }

        var incoming = appearance.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        foreach (var (from, targets) in next)
        {
            foreach (var to in targets.Where(to => !back.Contains((from, to)))) incoming[to]++;
        }

        var order = new List<string>();
        var ready = new SortedSet<int>(appearance.Where(id => incoming[id] == 0).Select(id => rank[id]));
        while (ready.Count > 0)
        {
            var id = appearance[ready.Min];
            ready.Remove(ready.Min);
            order.Add(id);
            foreach (var to in next[id].Where(to => !back.Contains((id, to))))
            {
                if (--incoming[to] == 0) ready.Add(rank[to]);
            }
        }
        return order;

        void Walk(string id)
        {
            state[id] = 1;
            foreach (var to in next[id])
            {
                if (state.TryGetValue(to, out var seen))
                {
                    if (seen == 1) back.Add((id, to));
                    continue;
                }
                Walk(to);
            }
            state[id] = 2;
        }
    }

    /// <summary>Lays <paramref name="diagram"/> out as one row: boxes left to
    /// right, a forward transition to the next state on the axis and every other
    /// one in a lane above, each way back in a lane below, and the restore loop in
    /// the outermost lane beneath them all.</summary>
    public static DomainFlowLayout Layout(DomainFlowDiagram diagram)
    {
        var count = diagram.States.Count;
        var widths = diagram.States
            .Select(state => state.Kind == DomainFlowStateKind.State
                ? Math.Max(MinBoxWidth, state.Label.Length * CharWidth + 2 * BoxPadding)
                : PseudoSize)
            .ToArray();

        // The gap after each state holds the label of the axis transition into
        // the next one, if there is one.
        var gaps = new double[Math.Max(0, count - 1)];
        for (var i = 0; i < gaps.Length; i++) gaps[i] = MinGap;
        foreach (var transition in diagram.Transitions)
        {
            if (!OnAxis(diagram, transition)) continue;
            var at = diagram.State(transition.From).Index;
            gaps[at] = Math.Max(gaps[at], LabelWidth(Short(transition.Label)) + 24);
        }

        var xs = new double[count];
        var x = Margin;
        for (var i = 0; i < count; i++)
        {
            xs[i] = x;
            x += widths[i] + (i < gaps.Length ? gaps[i] : 0);
        }
        var width = x + Margin;

        // Lanes: each arc takes the lowest lane where its span and its label clear
        // every arc already there, shortest first, so a longer arc nests outside.
        var above = new List<Arc>();
        var below = new List<Arc>();
        foreach (var transition in diagram.Transitions)
        {
            if (OnAxis(diagram, transition)) continue;
            var from = diagram.State(transition.From).Index;
            var to = diagram.State(transition.To).Index;
            var arc = new Arc(transition, from, to);
            if (transition.Direction is DomainFlowDirection.Back or DomainFlowDirection.Restore) below.Add(arc);
            else above.Add(arc);
        }

        AssignPorts(above, xs, widths);
        AssignPorts(below, xs, widths);
        var aboveLanes = Pack(above);
        var belowLanes = Pack(below.Where(arc => arc.Transition.Direction != DomainFlowDirection.Restore).ToList());
        foreach (var restore in below.Where(arc => arc.Transition.Direction == DomainFlowDirection.Restore)) restore.Lane = belowLanes++;

        var top = aboveLanes > 0 ? Margin + FirstLane + (aboveLanes - 1) * LaneGap + 8 : Margin + 12;
        var bottom = top + BoxHeight;
        var height = bottom + (belowLanes > 0 ? FirstLane + (belowLanes - 1) * LaneGap + 14 : 0) + Margin;
        var axis = top + BoxHeight / 2;

        var boxes = diagram.States
            .Select(state => state.Kind == DomainFlowStateKind.State
                ? new DomainFlowBox(state, xs[state.Index], top, widths[state.Index], BoxHeight)
                : new DomainFlowBox(state, xs[state.Index], axis - PseudoSize / 2, PseudoSize, PseudoSize))
            .ToList();

        var edges = new List<DomainFlowEdge>();
        foreach (var transition in diagram.Transitions)
        {
            var label = Short(transition.Label);
            if (OnAxis(diagram, transition))
            {
                var from = boxes[diagram.State(transition.From).Index];
                var to = boxes[diagram.State(transition.To).Index];
                var x1 = from.X + from.Width;
                var x2 = to.X;
                edges.Add(new DomainFlowEdge(transition, $"M {N(x1)} {N(axis)} H {N(x2)}", (x1 + x2) / 2, axis - 7, label, -1));
                continue;
            }

            var arc = above.Concat(below).First(candidate => candidate.Transition == transition);
            var isAbove = above.Contains(arc);
            var lane = isAbove ? top - FirstLane - arc.Lane * LaneGap : bottom + FirstLane + arc.Lane * LaneGap;
            var y1 = Edge(boxes[arc.From], isAbove);
            var y2 = Edge(boxes[arc.To], isAbove);
            edges.Add(new DomainFlowEdge(transition, Bracket(arc.X1, y1, arc.X2, y2, lane), (arc.X1 + arc.X2) / 2, lane + (isAbove ? -5 : 13), label, arc.Lane));
        }

        return new DomainFlowLayout(width, height, boxes, edges);
    }

    /// <summary>A forward transition to the very next box is drawn on the axis.</summary>
    private static bool OnAxis(DomainFlowDiagram diagram, DomainFlowTransition transition) =>
        transition.Direction == DomainFlowDirection.Forward
        && diagram.State(transition.To).Index == diagram.State(transition.From).Index + 1;

    /// <summary>
    /// Spreads the ends of the arcs that leave or reach each box across its edge,
    /// so two arcs never share a port. On each box the ends of arcs running left
    /// come first and those running right after, and within each the longer arc
    /// takes the port nearer the box's middle — which is what lets it pass outside
    /// the shorter ones without crossing them.
    /// </summary>
    private static void AssignPorts(List<Arc> arcs, double[] xs, double[] widths)
    {
        var ends = new List<(Arc Arc, bool IsFrom, int Box, double Key)>();
        foreach (var arc in arcs)
        {
            var center = (int box) => xs[box] + widths[box] / 2;
            if (arc.From == arc.To)
            {
                ends.Add((arc, true, arc.From, 0.5));
                ends.Add((arc, false, arc.From, 0.6));
                continue;
            }
            ends.Add((arc, true, arc.From, Key(center(arc.From), center(arc.To))));
            ends.Add((arc, false, arc.To, Key(center(arc.To), center(arc.From))));
        }

        foreach (var group in ends.GroupBy(end => end.Box))
        {
            var sorted = group.OrderBy(end => end.Key).ToList();
            var box = group.Key;
            var inset = Math.Min(PortInset, widths[box] / 4);
            var span = widths[box] - 2 * inset;
            for (var i = 0; i < sorted.Count; i++)
            {
                var px = sorted.Count == 1 ? xs[box] + widths[box] / 2 : xs[box] + inset + span * i / (sorted.Count - 1);
                if (sorted[i].IsFrom) sorted[i].Arc.X1 = px;
                else sorted[i].Arc.X2 = px;
            }
        }

        // Leftward ends sort first, nearest the middle the farther the other end;
        // rightward ends after, nearest the middle the farther the other end.
        static double Key(double self, double other) =>
            other < self ? -1_000_000 + (self - other) : 1_000_000 - (other - self);
    }

    private static int Pack(List<Arc> arcs)
    {
        var lanes = new List<List<(double From, double To)>>();
        foreach (var arc in arcs.OrderBy(arc => Math.Abs(arc.To - arc.From)).ThenBy(arc => Math.Min(arc.From, arc.To)))
        {
            var labelHalf = LabelWidth(Short(arc.Transition.Label)) / 2;
            var middle = (arc.X1 + arc.X2) / 2;
            var extent = (From: Math.Min(Math.Min(arc.X1, arc.X2), middle - labelHalf) - 4, To: Math.Max(Math.Max(arc.X1, arc.X2), middle + labelHalf) + 4);
            var lane = 0;
            while (lane < lanes.Count && lanes[lane].Any(taken => taken.From < extent.To && extent.From < taken.To)) lane++;
            if (lane == lanes.Count) lanes.Add([]);
            lanes[lane].Add(extent);
            arc.Lane = lane;
        }
        return lanes.Count;
    }

    /// <summary>The top of a box, or its bottom, where an arc meets it.</summary>
    private static double Edge(DomainFlowBox box, bool top) => top ? box.Y : box.Y + box.Height;

    /// <summary>An orthogonal path with rounded corners from (x1, y1) out to the
    /// lane at y, across, and back to (x2, y2).</summary>
    private static string Bracket(double x1, double y1, double x2, double y2, double y)
    {
        var up = y < y1 ? -1 : 1;
        var right = x2 >= x1 ? 1 : -1;
        if (Math.Abs(x2 - x1) < 2 * Corner)
        {
            return $"M {N(x1)} {N(y1)} V {N(y)} H {N(x2)} V {N(y2)}";
        }
        return $"M {N(x1)} {N(y1)} V {N(y - up * Corner)} Q {N(x1)} {N(y)} {N(x1 + right * Corner)} {N(y)} "
            + $"H {N(x2 - right * Corner)} Q {N(x2)} {N(y)} {N(x2)} {N(y - up * Corner)} V {N(y2)}";
    }

    private static double LabelWidth(string label) => label.Length * LabelCharWidth;

    /// <summary>A label cut to fit a lane; the full label stays the tooltip.</summary>
    public static string Short(string label) =>
        label.Length <= MaxLabel ? label : label[..(MaxLabel - 1)].TrimEnd() + "…";

    internal static string N(double value) => Math.Round(value, 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>Words wrapped into lines of at most <paramref name="width"/>
    /// characters, a hard break (a newline) kept; a word longer than a line stands alone.</summary>
    internal static IReadOnlyList<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var line = new StringBuilder();
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > width)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                }
                if (line.Length > 0) line.Append(' ');
                line.Append(word);
            }
            lines.Add(line.ToString());
        }
        return lines;
    }

    /// <summary>An orthogonal polyline as an SVG path, each corner rounded by up
    /// to <paramref name="corner"/>, collinear and repeated points dropped.</summary>
    internal static string Rounded(IReadOnlyList<(double X, double Y)> points, double corner = Corner)
    {
        var kept = new List<(double X, double Y)>();
        foreach (var point in points)
        {
            if (kept.Count > 0 && Math.Abs(kept[^1].X - point.X) < 0.05 && Math.Abs(kept[^1].Y - point.Y) < 0.05) continue;
            if (kept.Count >= 2)
            {
                var (a, b) = (kept[^2], kept[^1]);
                var collinear = (Math.Abs(a.X - b.X) < 0.05 && Math.Abs(b.X - point.X) < 0.05)
                    || (Math.Abs(a.Y - b.Y) < 0.05 && Math.Abs(b.Y - point.Y) < 0.05);
                if (collinear) kept[^1] = point;
                else kept.Add(point);
                continue;
            }
            kept.Add(point);
        }

        var path = new StringBuilder($"M {N(kept[0].X)} {N(kept[0].Y)}");
        for (var i = 1; i < kept.Count; i++)
        {
            var at = kept[i];
            if (i == kept.Count - 1)
            {
                path.Append($" L {N(at.X)} {N(at.Y)}");
                break;
            }
            var before = kept[i - 1];
            var after = kept[i + 1];
            var r = Math.Min(corner, Math.Min(Distance(before, at), Distance(at, after)) / 2);
            var (inX, inY) = Toward(at, before, r);
            var (outX, outY) = Toward(at, after, r);
            path.Append($" L {N(inX)} {N(inY)} Q {N(at.X)} {N(at.Y)} {N(outX)} {N(outY)}");
        }
        return path.ToString();

        static double Distance((double X, double Y) a, (double X, double Y) b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

        static (double, double) Toward((double X, double Y) from, (double X, double Y) to, double by)
        {
            var length = Distance(from, to);
            return length == 0 ? from : (from.X + (to.X - from.X) / length * by, from.Y + (to.Y - from.Y) / length * by);
        }
    }

    private static void Note(List<string> appearance, string id)
    {
        if (!appearance.Contains(id)) appearance.Add(id);
    }

    private sealed class Arc(DomainFlowTransition transition, int from, int to)
    {
        public DomainFlowTransition Transition { get; } = transition;
        public int From { get; } = from;
        public int To { get; } = to;
        public double X1 { get; set; }
        public double X2 { get; set; }
        public int Lane { get; set; }
    }

    [GeneratedRegex(@"^state\s+""([^""]+)""\s+as\s+([^\s{]+)$")]
    private static partial Regex StateAlias();

    [GeneratedRegex(@"^([^\s:]+)\s*-->\s*([^\s:]+)\s*(?::(.*))?$")]
    private static partial Regex Transition();

    [GeneratedRegex(@"^([^\s:\[\]]+)\s*:\s*(.+)$")]
    private static partial Regex StateDescription();
}
