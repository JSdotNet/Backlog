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
public sealed record DomainFlowState(string Id, string Label, int Index, DomainFlowStateKind Kind)
{
    /// <summary>What the box says under its name: the state's
    /// <c>Id : text</c> lines, joined.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The classes a <c>class Id name</c> line gives it: <c>active</c>
    /// sets the box apart, <c>ended</c> sinks it.</summary>
    public IReadOnlyList<string> Classes { get; init; } = [];
}

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

    /// <summary>Leftward, a way back: on the axis to the state before, else below
    /// the row.</summary>
    Back,

    /// <summary>From the last state back to the first: the restore loop, drawn
    /// round the outside, above every other arc.</summary>
    Restore,

    /// <summary>From a state to itself: a loop above it.</summary>
    Self
}

/// <summary>The domain event a transition raises, read off its label's
/// <c>(EventName, not yet emitted)</c>.</summary>
/// <param name="Emitted">Whether the context emits it today; an event documented
/// but not yet emitted says so in its label.</param>
public sealed record DomainFlowEvent(string Name, bool Emitted);

/// <summary>One transition of a flow, every label the source gives the same
/// pair joined with " / ", each with its event taken out.</summary>
public sealed record DomainFlowTransition(string From, string To, string Label, DomainFlowDirection Direction)
{
    /// <summary>The event the transition raises, or <see langword="null"/>.</summary>
    public DomainFlowEvent? Event { get; init; }
}

/// <summary>A one-line <c>note … of State : text</c>: a caption under the state.</summary>
public sealed record DomainFlowNote(string State, string Text);

/// <summary>Two or more transitions into one state under one label — a way in
/// from several states at once, such as dropping with an archived repository.
/// The row says it once, in a note under the drawing, rather than as one arc each.</summary>
/// <param name="From">The states it leaves, in row order.</param>
public sealed record DomainFlowDrop(string To, IReadOnlyList<string> From, string Label);

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
    /// <summary>The one-line notes, in source order.</summary>
    public IReadOnlyList<DomainFlowNote> Notes { get; init; } = [];

    /// <summary>The ways in from several states under one label, said once.</summary>
    public IReadOnlyList<DomainFlowDrop> Drops { get; init; } = [];

    public DomainFlowState State(string id) => States.First(state => state.Id == id);

    /// <summary>Whether a transition is said by a <see cref="DomainFlowDrop"/>
    /// rather than drawn.</summary>
    public bool IsDropped(DomainFlowTransition transition) =>
        Drops.Any(drop => drop.To == transition.To && drop.Label == transition.Label && drop.From.Contains(transition.From));
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

/// <summary>What a <see cref="DomainFlowMark"/> is.</summary>
public enum DomainFlowMarkKind
{
    /// <summary>An event chip under the transition raising it.</summary>
    Event,

    /// <summary>A note's caption under its state.</summary>
    Note
}

/// <summary>A chip or caption under the row, placed in the drawing's units and
/// laid over it as text, so an event can be followed.</summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Transition">The transition an event chip belongs to.</param>
public sealed record DomainFlowMark(DomainFlowMarkKind Kind, double X, double Y, double Width, string Text, DomainFlowTransition? Transition);

/// <summary>A flow row laid out: the size of the drawing, its boxes and its edges.</summary>
public sealed record DomainFlowLayout(double Width, double Height, IReadOnlyList<DomainFlowBox> Boxes, IReadOnlyList<DomainFlowEdge> Edges)
{
    /// <summary>The event chips and note captions under the row.</summary>
    public IReadOnlyList<DomainFlowMark> Marks { get; init; } = [];
}

/// <summary>A sub-chapter of a flow chapter that draws no diagram, read as a card:
/// facts that sit beside a lifecycle rather than in it.</summary>
/// <param name="Tag">The first paragraph when it is only code spans or emphasis —
/// the field or phrase the card is about — else empty.</param>
/// <param name="Body">The card's markdown between its tag and its footer.</param>
/// <param name="Footer">The last paragraph when it is a lone link, else empty.</param>
public sealed record DomainFlowCard(string Title, string Anchor, string Tag, string Body, string Footer);

/// <summary>A flow chapter's prose split around its diagrams: the diagrams the
/// page draws itself, in source order, and the rest of the chapter's markdown.</summary>
public sealed record DomainFlowSection(DomainChapter Chapter, IReadOnlyList<DomainFlowFigure> Diagrams, string Prose)
{
    /// <summary>The markdown before the first drawn diagram — or before the first
    /// card — which the page sets as the chapter's lead.</summary>
    public string Lead { get; init; } = string.Empty;

    /// <summary>The chapter's sub-chapters as cards, when it draws no diagram and
    /// every one of them is untyped.</summary>
    public IReadOnlyList<DomainFlowCard> Cards { get; init; } = [];
}

/// <summary>
/// Reads a flow's <c>stateDiagram-v2</c> into one row of states and lays it out
/// (local ADR 0037, the Flow page): each state a box with its description, the
/// moves between neighbours straight along the row — forward on top, the way
/// back underneath — every other forward arc above the row, every other way back
/// below it, and the restore loop — the way from the last state back to the
/// first — round the outside on top. A transition's event is a chip under it, and
/// a way in from several states under one label is one note rather than an arc
/// each. Pure, so the storybook draws the same row from a sample string.
/// <para>
/// A diagram this row cannot hold — composite or concurrent states, choice and
/// fork pseudo-states, notes over several lines — reads as <see langword="null"/>.
/// A flowchart and a sequence are read by <see cref="DomainFlowcharts"/> and
/// <see cref="DomainSequences"/>; a fence none of the three can read keeps the
/// Mermaid rendering every other page gives it.
/// </para>
/// </summary>
public static partial class DomainFlows
{
    private const string StartId = "[*]start";
    private const string EndId = "[*]end";

    private const double Margin = 16;
    private const double PlainBoxHeight = 44;
    private const double DescribedBoxHeight = 64;
    private const double MinBoxWidth = 144;
    private const double BoxPadding = 16;
    private const double NameCharWidth = 8.4;
    private const double DescriptionCharWidth = 6.6;
    private const double LabelCharWidth = 7.0;
    private const double ChipCharWidth = 7.0;
    private const double ChipPadding = 20;
    private const double NoteCharWidth = 6.8;
    private const double PseudoSize = 16;
    private const double MinGap = 88;
    private const double PairOffset = 10;
    private const double LaneGap = 24;
    private const double FirstLane = 26;
    private const double MarkGap = 14;
    private const double MarkHeight = 22;
    private const double Corner = 6;
    private const double PortInset = 10;
    private const int MaxLabel = 36;

    /// <summary>The section a flow chapter reads as: each diagram it holds that
    /// the page can draw — a state row, a flowchart or a sequence — its markdown
    /// with those fences taken out, the markdown before the first of them as its
    /// lead, and — for a chapter drawing nothing whose sub-chapters are all
    /// untyped — those sub-chapters as cards. A fence none of them can read stays
    /// in the markdown, for Mermaid.</summary>
    public static DomainFlowSection Section(DomainChapter chapter)
    {
        var diagrams = new List<DomainFlowFigure>();
        var prose = new StringBuilder();
        var lead = string.Empty;
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
                    if (diagrams.Count == 0)
                    {
                        lead = prose.ToString().Trim();
                        prose.Clear();
                    }
                    diagrams.Add(diagram);
                    i = end;
                    continue;
                }
            }
            prose.Append(lines[i]).Append('\n');
        }

        if (diagrams.Count == 0
            && chapter.Children.Count > 0
            && chapter.Children.All(child => child.Type is null))
        {
            // The first sub-heading outside a fence: a `###` in a code block is code.
            var fenced = false;
            var heading = Array.FindIndex(lines, line =>
            {
                if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) fenced = !fenced;
                return !fenced && SubHeading().IsMatch(line);
            });
            var before = heading < 0 ? chapter.Prose : string.Join('\n', lines[..heading]);
            return new DomainFlowSection(chapter, diagrams, string.Empty)
            {
                Lead = before.Trim(),
                Cards = [.. chapter.Children.Select(Card)]
            };
        }

        return new DomainFlowSection(chapter, diagrams, prose.ToString().Trim()) { Lead = lead };
    }

    /// <summary>A sub-chapter read as a card: a first paragraph of nothing but code
    /// spans or emphasis is its tag, a last paragraph of one link its footer.</summary>
    private static DomainFlowCard Card(DomainChapter chapter)
    {
        var paragraphs = Regex.Split(chapter.Prose.Replace("\r\n", "\n").Trim(), @"\n\s*\n")
            .Select(paragraph => paragraph.Trim())
            .Where(paragraph => paragraph.Length > 0)
            .ToList();
        var tag = paragraphs.Count > 0 && CardTag().IsMatch(paragraphs[0]) ? paragraphs[0] : string.Empty;
        if (tag.Length > 0) paragraphs.RemoveAt(0);
        var footer = paragraphs.Count > 0 && LoneLink().IsMatch(paragraphs[^1]) ? paragraphs[^1] : string.Empty;
        if (footer.Length > 0) paragraphs.RemoveAt(paragraphs.Count - 1);
        return new DomainFlowCard(chapter.Title, chapter.Anchor, tag, string.Join("\n\n", paragraphs), footer);
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
        var descriptions = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var classes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var notes = new List<DomainFlowNote>();
        var appearance = new List<string>();
        var raw = new List<(string From, string To, string Label)>();
        foreach (var line in lines.Skip(1))
        {
            if (line.StartsWith("direction ", StringComparison.Ordinal)
                || line.StartsWith("classDef ", StringComparison.Ordinal)
                || line.StartsWith("style ", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("class ", StringComparison.Ordinal))
            {
                if (ClassLine().Match(line) is { Success: true } assigned)
                {
                    foreach (var id in assigned.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (!classes.TryGetValue(id, out var list)) classes[id] = list = [];
                        if (!list.Contains(assigned.Groups[2].Value)) list.Add(assigned.Groups[2].Value);
                    }
                }
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

            if (line.StartsWith("note ", StringComparison.Ordinal))
            {
                // A one-line note is a caption; a note over several lines is not the row's.
                var note = OneLineNote().Match(line);
                if (!note.Success) return null;
                notes.Add(new DomainFlowNote(note.Groups[1].Value, note.Groups[2].Value.Trim()));
                Note(appearance, note.Groups[1].Value);
                continue;
            }

            if (line == "--" || line.Contains('{') || line.Contains('}')) return null;

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

            // `Idle : waiting` describes a state; the box says it under its name. A
            // line holding an arrow the transition pattern refused — `A:::hot --> B` —
            // is not a description, and the row cannot read it.
            if (!line.Contains("-->", StringComparison.Ordinal)
                && StateDescription().Match(line) is { Success: true } described
                && !described.Groups[2].Value.StartsWith(':'))
            {
                var id = described.Groups[1].Value;
                Note(appearance, id);
                if (!descriptions.TryGetValue(id, out var said)) descriptions[id] = said = [];
                said.Add(described.Groups[2].Value.Trim());
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
                var read = group.Select(edge => SplitEvent(edge.Label)).ToList();
                var label = string.Join(" / ", read.Select(part => part.Label).Where(text => text.Length > 0).Distinct(StringComparer.Ordinal));
                var direction = from == to ? DomainFlowDirection.Self
                    : index[to] > index[from] ? DomainFlowDirection.Forward
                    : from == last && to == first && order.Count > 1 ? DomainFlowDirection.Restore
                    : DomainFlowDirection.Back;
                return new DomainFlowTransition(from, to, label, direction) { Event = read.Select(part => part.Event).FirstOrDefault(found => found is not null) };
            })
            .ToList();

        // A way in from two or more states under one label is said once. A move to
        // the very next state stays on the row, where it is drawn anyway.
        var drops = transitions
            .Where(transition => transition.Label.Length > 0
                && transition.Direction == DomainFlowDirection.Forward
                && index[transition.To] != index[transition.From] + 1)
            .GroupBy(transition => (transition.To, transition.Label))
            .Where(group => group.Count() > 1)
            .Select(group => new DomainFlowDrop(group.Key.To, [.. group.Select(transition => transition.From).OrderBy(from => index[from])], group.Key.Label))
            .ToList();

        return new DomainFlowDiagram(states, transitions)
        {
            Notes = [.. notes.Where(note => index.ContainsKey(note.State))],
            Drops = drops
        };

        void Add(string id, string label, DomainFlowStateKind kind)
        {
            index[id] = states.Count;
            states.Add(new DomainFlowState(id, label, states.Count, kind)
            {
                Description = descriptions.TryGetValue(id, out var said) ? string.Join(" ", said) : string.Empty,
                Classes = classes.TryGetValue(id, out var named) ? named : []
            });
        }
    }

    /// <summary>A transition label's event taken out: <c>started (TaskProjected,
    /// not yet emitted)</c> reads as <c>started</c> raising <c>TaskProjected</c>,
    /// documented but not emitted. A parenthesis that does not open on an event's
    /// name stays in the label.</summary>
    private static (string Label, DomainFlowEvent? Event) SplitEvent(string label)
    {
        var match = LabelEvent().Match(label);
        if (!match.Success) return (label, null);
        var said = match.Groups[3].Success ? match.Groups[3].Value : string.Empty;
        return (match.Groups[1].Value.Trim(), new DomainFlowEvent(match.Groups[2].Value, !NotEmitted().IsMatch(said)));
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
    /// right; a move to the next state or back to the one before straight between
    /// the two boxes — forward on top, back underneath when both run; every other
    /// forward arc in a lane above, every other way back in a lane below, and the
    /// restore loop in the outermost lane above. Event chips and note captions sit
    /// in a strip under the boxes. The start pseudo-state is left out when its one
    /// way in leads to the first state: the first box already says where a thing
    /// begins.</summary>
    public static DomainFlowLayout Layout(DomainFlowDiagram diagram)
    {
        var drawn = diagram.States.Where(state => !HiddenStart(diagram, state)).ToList();
        var slot = drawn.Select((state, i) => (state.Id, i)).ToDictionary(pair => pair.Id, pair => pair.i, StringComparer.Ordinal);
        var count = drawn.Count;
        var boxHeight = drawn.Any(state => state.Description.Length > 0) ? DescribedBoxHeight : PlainBoxHeight;
        var widths = drawn
            .Select(state => state.Kind == DomainFlowStateKind.State
                ? Math.Max(MinBoxWidth, Math.Max(state.Label.Length * NameCharWidth, state.Description.Length * DescriptionCharWidth) + 2 * BoxPadding)
                : PseudoSize)
            .ToArray();

        var visible = diagram.Transitions
            .Where(transition => !diagram.IsDropped(transition) && slot.ContainsKey(transition.From) && slot.ContainsKey(transition.To))
            .ToList();
        var forwardPairs = visible.Where(t => OnAxis(t, slot) == Axis.Forward).Select(t => slot[t.From]).ToHashSet();
        var backPairs = visible.Where(t => OnAxis(t, slot) == Axis.Back).Select(t => slot[t.To]).ToHashSet();

        // The gap after each state holds the labels and the event chips of the
        // moves between it and the next one.
        var gaps = new double[Math.Max(0, count - 1)];
        for (var i = 0; i < gaps.Length; i++) gaps[i] = MinGap;
        foreach (var transition in visible)
        {
            var axis = OnAxis(transition, slot);
            if (axis == Axis.None) continue;
            var at = Math.Min(slot[transition.From], slot[transition.To]);
            gaps[at] = Math.Max(gaps[at], LabelWidth(Short(transition.Label)) + 24);
            if (transition.Event is { } raised) gaps[at] = Math.Max(gaps[at], ChipWidth(raised) + 16);
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
        foreach (var transition in visible)
        {
            if (OnAxis(transition, slot) != Axis.None) continue;
            var arc = new Arc(transition, slot[transition.From], slot[transition.To]);
            if (transition.Direction == DomainFlowDirection.Back) below.Add(arc);
            else above.Add(arc);
        }

        AssignPorts(above, xs, widths);
        AssignPorts(below, xs, widths);
        var aboveLanes = Pack(above.Where(arc => arc.Transition.Direction != DomainFlowDirection.Restore).ToList());
        foreach (var restore in above.Where(arc => arc.Transition.Direction == DomainFlowDirection.Restore)) restore.Lane = aboveLanes++;
        var belowLanes = Pack(below);

        // The strip under the boxes: each note under its state and each event chip
        // under its transition, on the first line of the strip where it clears
        // every mark already there.
        var pending = new List<(DomainFlowMarkKind Kind, double X, double Width, string Text, DomainFlowTransition? Transition)>();
        foreach (var note in diagram.Notes.Where(note => slot.ContainsKey(note.State)))
        {
            pending.Add((DomainFlowMarkKind.Note, xs[slot[note.State]], note.Text.Length * NoteCharWidth, note.Text, null));
        }
        foreach (var transition in visible.Where(transition => transition.Event is not null))
        {
            var at = Math.Min(slot[transition.From], slot[transition.To]);
            var lane = above.Concat(below).FirstOrDefault(arc => arc.Transition == transition);
            var center = lane is null ? xs[at] + widths[at] + gaps[at] / 2 : (lane.X1 + lane.X2) / 2;
            var chip = ChipWidth(transition.Event!);
            pending.Add((DomainFlowMarkKind.Event, center - chip / 2, chip, transition.Event!.Name, transition));
        }
        var markLines = new List<List<(double From, double To)>>();
        var placed = new List<(DomainFlowMarkKind Kind, double X, double Width, string Text, DomainFlowTransition? Transition, int Line)>();
        foreach (var mark in pending)
        {
            var line = 0;
            while (line < markLines.Count && markLines[line].Any(taken => taken.From < mark.X + mark.Width + 8 && mark.X - 8 < taken.To)) line++;
            if (line == markLines.Count) markLines.Add([]);
            markLines[line].Add((mark.X, mark.X + mark.Width));
            placed.Add((mark.Kind, mark.X, mark.Width, mark.Text, mark.Transition, line));
        }

        var top = aboveLanes > 0 ? Margin + FirstLane + (aboveLanes - 1) * LaneGap + 8 : Margin + 4;
        var bottom = top + boxHeight;
        var markTop = bottom + MarkGap;
        var stripEnd = markLines.Count > 0 ? markTop + markLines.Count * (MarkHeight + 4) - 4 : bottom;
        var belowStart = markLines.Count > 0 ? stripEnd + 4 : bottom;
        var height = (belowLanes > 0 ? belowStart + FirstLane + (belowLanes - 1) * LaneGap + 18 : stripEnd) + Margin;
        var middle = top + boxHeight / 2;

        var boxes = drawn
            .Select((state, i) => state.Kind == DomainFlowStateKind.State
                ? new DomainFlowBox(state, xs[i], top, widths[i], boxHeight)
                : new DomainFlowBox(state, xs[i], middle - PseudoSize / 2, PseudoSize, PseudoSize))
            .ToList();

        var edges = new List<DomainFlowEdge>();
        foreach (var transition in visible)
        {
            var label = Short(transition.Label);
            var axis = OnAxis(transition, slot);
            if (axis != Axis.None)
            {
                var from = boxes[slot[transition.From]];
                var to = boxes[slot[transition.To]];
                var at = Math.Min(slot[transition.From], slot[transition.To]);
                var paired = forwardPairs.Contains(at) && backPairs.Contains(at);
                if (axis == Axis.Forward)
                {
                    var y = paired ? middle - PairOffset : middle;
                    var (x1, x2) = (from.X + from.Width, to.X);
                    edges.Add(new DomainFlowEdge(transition, $"M {N(x1)} {N(y)} H {N(x2)}", (x1 + x2) / 2, y - 7, label, -1));
                }
                else
                {
                    var y = paired ? middle + PairOffset : middle;
                    var (x1, x2) = (from.X, to.X + to.Width);
                    edges.Add(new DomainFlowEdge(transition, $"M {N(x1)} {N(y)} H {N(x2)}", (x1 + x2) / 2, y + 17, label, -1));
                }
                continue;
            }

            var arc = above.Concat(below).First(candidate => candidate.Transition == transition);
            var isAbove = above.Contains(arc);
            var lane = isAbove ? top - FirstLane - arc.Lane * LaneGap : belowStart + FirstLane + arc.Lane * LaneGap;
            var y1 = Edge(boxes[arc.From], isAbove);
            var y2 = Edge(boxes[arc.To], isAbove);
            edges.Add(new DomainFlowEdge(transition, Bracket(arc.X1, y1, arc.X2, y2, lane), (arc.X1 + arc.X2) / 2, lane + (isAbove ? -6 : 15), label, arc.Lane));
        }

        var marks = placed
            .Select(mark => new DomainFlowMark(mark.Kind, mark.X, markTop + mark.Line * (MarkHeight + 4), mark.Width, mark.Text, mark.Transition))
            .ToList();

        return new DomainFlowLayout(width, height, boxes, edges) { Marks = marks };
    }

    /// <summary>Whether the start pseudo-state goes undrawn: its one way in leads
    /// to the first state of the row.</summary>
    private static bool HiddenStart(DomainFlowDiagram diagram, DomainFlowState state) =>
        state.Kind == DomainFlowStateKind.Start
        && diagram.Transitions.Count(transition => transition.From == state.Id) == 1
        && diagram.Transitions.Single(transition => transition.From == state.Id).To is var first
        && diagram.States.FirstOrDefault(candidate => candidate.Kind == DomainFlowStateKind.State)?.Id == first;

    private enum Axis
    {
        None,
        Forward,
        Back
    }

    /// <summary>A move to the very next box, or back to the one before, runs
    /// straight between the two.</summary>
    private static Axis OnAxis(DomainFlowTransition transition, Dictionary<string, int> slot) =>
        transition.Direction == DomainFlowDirection.Forward && slot[transition.To] == slot[transition.From] + 1 ? Axis.Forward
        : transition.Direction is DomainFlowDirection.Back or DomainFlowDirection.Restore && slot[transition.To] == slot[transition.From] - 1 ? Axis.Back
        : Axis.None;

    private static double ChipWidth(DomainFlowEvent raised) => raised.Name.Length * ChipCharWidth + ChipPadding;

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

    [GeneratedRegex(@"^#{3,}\s")]
    private static partial Regex SubHeading();

    [GeneratedRegex(@"^(?:`[^`]+`|_[^_]+_|\*[^*]+\*)(?:\s*·\s*(?:`[^`]+`|_[^_]+_|\*[^*]+\*))*$")]
    private static partial Regex CardTag();

    [GeneratedRegex(@"^\[[^\]]+\]\([^)\s]+\)$")]
    private static partial Regex LoneLink();

    [GeneratedRegex(@"^class\s+(\S+)\s+([A-Za-z0-9_-]+)$")]
    private static partial Regex ClassLine();

    [GeneratedRegex(@"^note\s+(?:left|right)\s+of\s+([^\s:]+)\s*:\s*(.+)$")]
    private static partial Regex OneLineNote();

    [GeneratedRegex(@"^(.*?)\s*\(([A-Z][a-z0-9]+(?:[A-Z][A-Za-z0-9]*)+)(?:\s*,\s*([^)]*))?\)\s*$")]
    private static partial Regex LabelEvent();

    [GeneratedRegex(@"\bnot\b.*\bemitted\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotEmitted();
}
