using System.Text.RegularExpressions;

namespace Backlog.UI.Components.Devbook;

/// <summary>A participant of a sequence: a column with its card on top.</summary>
/// <param name="Label">Its <c>participant X as …</c> label, else its id.</param>
/// <param name="Index">Its column, from the left.</param>
public sealed record DomainSequenceParticipant(string Id, string Label, int Index);

/// <summary>One step of a sequence, in source order.</summary>
public abstract record DomainSequenceStep;

/// <summary>A message from one participant to another, or to itself.</summary>
/// <param name="Reply">A dashed line (<c>--&gt;&gt;</c>), as a reply is drawn.</param>
/// <param name="Arrow">Whether it ends in an arrowhead; a cross (<c>-x</c>) or
/// an open end (<c>-)</c>) is drawn with the same arrowhead.</param>
public sealed record DomainSequenceMessage(string From, string To, string Text, bool Reply, bool Arrow) : DomainSequenceStep;

/// <summary>Where a note stands against its participants.</summary>
public enum DomainSequencePlacement
{
    Over,
    LeftOf,
    RightOf
}

/// <summary>A <c>Note over A,B: …</c>, <c>Note right of A: …</c> or <c>Note left of A: …</c>.</summary>
public sealed record DomainSequenceNote(DomainSequencePlacement Placement, string First, string Last, string Text) : DomainSequenceStep;

/// <summary>A framed block opening: <c>alt</c>, <c>opt</c>, <c>loop</c>, <c>par</c>, <c>critical</c> or <c>break</c>.</summary>
public sealed record DomainSequenceBlock(string Keyword, string Label) : DomainSequenceStep;

/// <summary>A block's next branch: <c>else</c>, <c>and</c> or <c>option</c>.</summary>
public sealed record DomainSequenceBranch(string Keyword, string Label) : DomainSequenceStep;

/// <summary>A block's <c>end</c>.</summary>
public sealed record DomainSequenceEnd : DomainSequenceStep;

/// <summary>A <c>sequenceDiagram</c> the Flow page draws itself.</summary>
public sealed record DomainSequenceDiagram(IReadOnlyList<DomainSequenceParticipant> Participants, IReadOnlyList<DomainSequenceStep> Steps) : DomainFlowFigure
{
    public DomainSequenceParticipant Participant(string id) => Participants.First(participant => participant.Id == id);
}

/// <summary>A participant's card and lifeline as drawn.</summary>
public sealed record DomainSequenceColumn(DomainSequenceParticipant Participant, double X, double Y, double Width, double Height, double LifelineEnd)
{
    public double CenterX => X + Width / 2;
}

/// <summary>A message as drawn: its path and where its text's lines sit.</summary>
public sealed record DomainSequenceArrow(DomainSequenceMessage Message, string Path, IReadOnlyList<string> Lines, double TextX, double TextY, string TextAnchor);

/// <summary>A note as drawn.</summary>
public sealed record DomainSequenceNoteBox(DomainSequenceNote Note, double X, double Y, double Width, double Height, IReadOnlyList<string> Lines);

/// <summary>A branch line inside a block's frame, with its condition.</summary>
public sealed record DomainSequenceDivider(double Y, string Keyword, string Label);

/// <summary>A block's frame as drawn: the keyword in its tag, its first
/// condition beside it, and a dashed divider before each further branch.</summary>
public sealed record DomainSequenceFrame(string Keyword, string Label, double X, double Y, double Width, double Height, IReadOnlyList<DomainSequenceDivider> Dividers, int Depth);

/// <summary>A sequence laid out.</summary>
public sealed record DomainSequenceLayout(
    double Width,
    double Height,
    IReadOnlyList<DomainSequenceColumn> Columns,
    IReadOnlyList<DomainSequenceArrow> Arrows,
    IReadOnlyList<DomainSequenceNoteBox> Notes,
    IReadOnlyList<DomainSequenceFrame> Frames);

/// <summary>
/// Reads a flow chapter's <c>sequenceDiagram</c> and lays it out in the
/// lifecycle's cards and lines (local ADR 0037, the Flow page): a card per
/// participant on top with its lifeline beneath, each message a line between two
/// lifelines with its text above it — a reply dashed, as a way back is — and each
/// <c>alt</c>, <c>opt</c>, <c>loop</c> or <c>par</c> block a frame round its
/// steps. Pure, so the storybook draws the same sequence from a sample string.
/// <para>
/// A sequence this cannot hold — <c>autonumber</c>, boxes, <c>rect</c>, created or
/// destroyed participants, an unclosed block — reads as <see langword="null"/>,
/// and its fence keeps the Mermaid rendering.
/// </para>
/// </summary>
public static partial class DomainSequences
{
    private const double Margin = 16;
    private const double CardHeight = 40;
    private const double MinCardWidth = 96;
    private const double CardPad = 16;
    private const double NameCharWidth = 8.0;
    private const double LabelCharWidth = 6.6;
    private const double LineHeight = 16;
    private const double ColumnGap = 32;
    private const double SelfWidth = 28;
    private const double SelfHeight = 18;
    private const double StepGap = 14;
    private const double NotePad = 10;
    private const double FramePad = 12;
    private const double FrameHeader = 28;
    private const int MaxTextChars = 44;

    private static readonly HashSet<string> Blocks = new(StringComparer.Ordinal) { "alt", "opt", "loop", "par", "critical", "break" };

    /// <summary>Reads a Mermaid source as a sequence, or <see langword="null"/>
    /// when it is not one this can draw.</summary>
    public static DomainSequenceDiagram? Parse(string? source)
    {
        var lines = (source ?? string.Empty).Replace("\r\n", "\n").Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("%%", StringComparison.Ordinal))
            .ToList();
        if (lines.Count == 0 || lines[0] != "sequenceDiagram") return null;

        var participants = new List<(string Id, string Label)>();
        var steps = new List<DomainSequenceStep>();
        var open = new Stack<string>();
        foreach (var line in lines.Skip(1))
        {
            var word = line.Split([' ', '\t'], 2)[0];
            var rest = line.Length > word.Length ? line[word.Length..].Trim() : string.Empty;
            switch (word)
            {
                case "participant":
                case "actor":
                {
                    var declared = Declared().Match(rest);
                    if (!declared.Success) return null;
                    var id = declared.Groups[1].Value;
                    if (participants.Any(p => p.Id == id)) return null;
                    participants.Add((id, declared.Groups[2].Success ? Text(declared.Groups[2].Value).Replace('\n', ' ') : id));
                    continue;
                }
                case "activate":
                case "deactivate":
                    continue;
                case "end":
                    if (open.Count == 0 || rest.Length > 0) return null;
                    open.Pop();
                    steps.Add(new DomainSequenceEnd());
                    continue;
                case "else":
                case "and":
                case "option":
                {
                    var expected = word switch { "else" => "alt", "and" => "par", _ => "critical" };
                    if (open.Count == 0 || open.Peek() != expected) return null;
                    steps.Add(new DomainSequenceBranch(word, rest));
                    continue;
                }
            }

            if (Blocks.Contains(word))
            {
                open.Push(word);
                steps.Add(new DomainSequenceBlock(word, rest));
                continue;
            }

            if (Note().Match(line) is { Success: true } note)
            {
                var placement = note.Groups[1].Value.ToLowerInvariant() switch
                {
                    "over" => DomainSequencePlacement.Over,
                    "left of" => DomainSequencePlacement.LeftOf,
                    _ => DomainSequencePlacement.RightOf
                };
                var first = note.Groups[2].Value;
                var last = note.Groups[3].Success ? note.Groups[3].Value : first;
                if (placement != DomainSequencePlacement.Over && first != last) return null;
                Mention(first);
                Mention(last);
                steps.Add(new DomainSequenceNote(placement, first, last, Text(note.Groups[4].Value)));
                continue;
            }

            if (Message().Match(line) is { Success: true } message)
            {
                var op = message.Groups[2].Value;
                var from = message.Groups[1].Value;
                var to = message.Groups[3].Value;
                Mention(from);
                Mention(to);
                steps.Add(new DomainSequenceMessage(from, to, Text(message.Groups[4].Value), op.StartsWith("--", StringComparison.Ordinal), op.EndsWith(">>", StringComparison.Ordinal) || op.EndsWith(')') || op.EndsWith('x')));
                continue;
            }

            return null; // autonumber, box, rect, create, destroy, title, links …
        }

        if (open.Count > 0 || participants.Count == 0 || !steps.Any(step => step is DomainSequenceMessage)) return null;
        return new DomainSequenceDiagram([.. participants.Select((p, i) => new DomainSequenceParticipant(p.Id, p.Label, i))], steps);

        void Mention(string id)
        {
            if (!participants.Any(p => p.Id == id)) participants.Add((id, id));
        }
    }

    /// <summary>The width of a frame's keyword tag.</summary>
    public static double TagWidth(string keyword) => keyword.Length * 7.4 + 16;

    private static string Text(string raw) => LineBreak().Replace(raw.Trim(), "\n").Replace("#quot;", "\"", StringComparison.Ordinal).Trim();

    /// <summary>
    /// Lays the sequence out: the columns as far apart as their cards and the
    /// widest text between them need, then each step a row beneath the last — a
    /// message's text above its line, a message to itself a small loop to the
    /// right with its text beside it, a note a card, and a block's frame round
    /// everything it holds, wider than any frame inside it.
    /// </summary>
    public static DomainSequenceLayout Layout(DomainSequenceDiagram diagram)
    {
        var count = diagram.Participants.Count;
        var index = diagram.Participants.ToDictionary(p => p.Id, p => p.Index, StringComparer.Ordinal);
        var widths = diagram.Participants.Select(p => Math.Max(MinCardWidth, p.Label.Length * NameCharWidth + 2 * CardPad)).ToArray();
        var wrapped = diagram.Steps.Select(step => step switch
        {
            DomainSequenceMessage message => DomainFlows.Wrap(message.Text, MaxTextChars),
            DomainSequenceNote note => DomainFlows.Wrap(note.Text, MaxTextChars),
            _ => (IReadOnlyList<string>)[]
        }).ToArray();
        double TextWidth(int step) => wrapped[step].Count == 0 ? 0 : wrapped[step].Max(line => line.Length) * LabelCharWidth;

        // How far apart the columns stand: their cards, then the text that has to
        // fit between them, a shortfall spread across the columns it spans.
        var sep = new double[Math.Max(0, count - 1)];
        for (var i = 0; i < sep.Length; i++) sep[i] = widths[i] / 2 + widths[i + 1] / 2 + ColumnGap;
        var rightOverhang = 0.0;
        var leftOverhang = 0.0;
        for (var s = 0; s < diagram.Steps.Count; s++)
        {
            switch (diagram.Steps[s])
            {
                case DomainSequenceMessage message when message.From == message.To:
                {
                    var at = index[message.From];
                    var need = SelfWidth + 8 + TextWidth(s) + 16;
                    if (at < sep.Length) sep[at] = Math.Max(sep[at], need);
                    else rightOverhang = Math.Max(rightOverhang, need - widths[at] / 2);
                    break;
                }
                case DomainSequenceMessage message:
                {
                    var (a, b) = (Math.Min(index[message.From], index[message.To]), Math.Max(index[message.From], index[message.To]));
                    Spread(a, b, TextWidth(s) + 32);
                    break;
                }
                case DomainSequenceNote { Placement: DomainSequencePlacement.RightOf } note:
                {
                    var at = index[note.First];
                    var need = 12 + TextWidth(s) + 2 * NotePad + 16;
                    if (at < sep.Length) sep[at] = Math.Max(sep[at], need);
                    else rightOverhang = Math.Max(rightOverhang, need - widths[at] / 2);
                    break;
                }
                case DomainSequenceNote { Placement: DomainSequencePlacement.LeftOf } note:
                {
                    var at = index[note.First];
                    var need = 12 + TextWidth(s) + 2 * NotePad + 16;
                    if (at > 0) sep[at - 1] = Math.Max(sep[at - 1], need);
                    else leftOverhang = Math.Max(leftOverhang, need - widths[at] / 2);
                    break;
                }
                case DomainSequenceNote note when note.First != note.Last:
                {
                    var (a, b) = (Math.Min(index[note.First], index[note.Last]), Math.Max(index[note.First], index[note.Last]));
                    Spread(a, b, TextWidth(s) + 2 * NotePad - 40);
                    break;
                }
            }
        }

        var centers = new double[count];
        centers[0] = widths[0] / 2;
        for (var i = 1; i < count; i++) centers[i] = centers[i - 1] + sep[i - 1];

        // Down the page, step by step.
        var y = Margin + CardHeight + 20;
        var arrows = new List<DomainSequenceArrow>();
        var notes = new List<DomainSequenceNoteBox>();
        var frames = new List<DomainSequenceFrame>();
        var stack = new Stack<OpenFrame>();
        var minX = 0.0;
        var maxX = centers[^1] + widths[^1] / 2;
        for (var s = 0; s < diagram.Steps.Count; s++)
        {
            var lines = wrapped[s];
            switch (diagram.Steps[s])
            {
                case DomainSequenceMessage message when message.From == message.To:
                {
                    var c = centers[index[message.From]];
                    var top = y + 4;
                    var path = DomainFlows.Rounded([(c, top), (c + SelfWidth, top), (c + SelfWidth, top + SelfHeight), (c, top + SelfHeight)], 5);
                    arrows.Add(new DomainSequenceArrow(message, path, lines, c + SelfWidth + 8, top + 4, "start"));
                    Touch(c, c + SelfWidth + 8 + TextWidth(s));
                    y = top + Math.Max(SelfHeight, lines.Count * LineHeight - 4) + StepGap + 4;
                    break;
                }
                case DomainSequenceMessage message:
                {
                    var x1 = centers[index[message.From]];
                    var x2 = centers[index[message.To]];
                    y += lines.Count * LineHeight + 2;
                    var textY = y - 7 - (lines.Count - 1) * LineHeight;
                    arrows.Add(new DomainSequenceArrow(message, $"M {DomainFlows.N(x1)} {DomainFlows.N(y)} H {DomainFlows.N(x2)}", lines, (x1 + x2) / 2, textY, "middle"));
                    Touch(Math.Min(x1, x2), Math.Max(x1, x2));
                    y += StepGap;
                    break;
                }
                case DomainSequenceNote note:
                {
                    var width = TextWidth(s) + 2 * NotePad;
                    var height = lines.Count * LineHeight + 2 * NotePad - 4;
                    var a = centers[index[note.First]];
                    var b = centers[index[note.Last]];
                    double x;
                    switch (note.Placement)
                    {
                        case DomainSequencePlacement.RightOf:
                            x = a + 12;
                            break;
                        case DomainSequencePlacement.LeftOf:
                            x = a - 12 - width;
                            break;
                        default:
                            var (low, high) = (Math.Min(a, b), Math.Max(a, b));
                            width = Math.Max(width, high - low + (low == high ? 0 : 48));
                            width = Math.Max(width, 80);
                            x = (low + high) / 2 - width / 2;
                            break;
                    }
                    y += 2;
                    notes.Add(new DomainSequenceNoteBox(note, x, y, width, height, lines));
                    Touch(x, x + width);
                    y += height + StepGap;
                    break;
                }
                case DomainSequenceBlock block:
                    stack.Push(new OpenFrame(block.Keyword, block.Label, y, stack.Count));
                    y += FrameHeader + 6;
                    break;
                case DomainSequenceBranch branch:
                    stack.Peek().Dividers.Add(new DomainSequenceDivider(y, branch.Keyword, branch.Label));
                    y += FrameHeader;
                    break;
                case DomainSequenceEnd:
                {
                    var frame = stack.Pop();
                    var pad = FramePad;
                    var low = frame.Low == double.MaxValue ? centers[0] : frame.Low;
                    var high = frame.High == double.MinValue ? centers[^1] : frame.High;
                    // The tag, then "[condition]" beside it; a divider's "[condition]" at its left.
                    var header = TagWidth(frame.Keyword) + 8 + (frame.Label.Length + 2) * LabelCharWidth + pad;
                    var dividers = frame.Dividers.Select(d => 8 + (d.Label.Length + 2) * LabelCharWidth + pad).DefaultIfEmpty(0).Max();
                    var left = low - pad;
                    var width = Math.Max(high + pad - left, Math.Max(header, dividers));
                    var bottom = y - StepGap + 10;
                    frames.Add(new DomainSequenceFrame(frame.Keyword, frame.Label, left, frame.Top, width, bottom - frame.Top, frame.Dividers, frame.Depth));
                    y = bottom + StepGap + 4;
                    if (stack.Count > 0) stack.Peek().Include(left, left + width);
                    minX = Math.Min(minX, left);
                    maxX = Math.Max(maxX, left + width);
                    break;
                }
            }
        }

        var lifelineEnd = y + 6;
        var drawingHeight = lifelineEnd + Margin;
        var shift = Margin + Math.Max(leftOverhang, -minX);
        maxX = Math.Max(maxX, centers[^1] + widths[^1] / 2 + rightOverhang);

        var columns = diagram.Participants
            .Select(p => new DomainSequenceColumn(p, centers[p.Index] - widths[p.Index] / 2 + shift, Margin, widths[p.Index], CardHeight, lifelineEnd))
            .ToList();
        return new DomainSequenceLayout(
            maxX + shift + Margin,
            drawingHeight,
            columns,
            [.. arrows.Select(a => a with { Path = Shifted(a.Path, shift), TextX = a.TextX + shift })],
            [.. notes.Select(n => n with { X = n.X + shift })],
            [.. frames.Select(f => f with { X = f.X + shift }).OrderBy(f => f.Depth)]);

        void Spread(int a, int b, double need)
        {
            var have = 0.0;
            for (var i = a; i < b; i++) have += sep[i];
            if (have >= need || b <= a) return;
            var more = (need - have) / (b - a);
            for (var i = a; i < b; i++) sep[i] += more;
        }

        void Touch(double low, double high)
        {
            if (stack.Count > 0) stack.Peek().Include(low, high);
            minX = Math.Min(minX, low);
            maxX = Math.Max(maxX, high);
        }
    }

    /// <summary>A path moved right by <paramref name="by"/>: every x of its M, L,
    /// H and Q commands.</summary>
    private static string Shifted(string path, double by)
    {
        var parts = path.Split(' ');
        var command = ' ';
        var position = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 1 && char.IsLetter(parts[i][0]))
            {
                command = parts[i][0];
                position = 0;
                continue;
            }
            var isX = command == 'H' || (command is 'M' or 'L' or 'Q' && position % 2 == 0);
            if (isX) parts[i] = DomainFlows.N(double.Parse(parts[i], System.Globalization.CultureInfo.InvariantCulture) + by);
            position++;
        }
        return string.Join(' ', parts);
    }

    private sealed class OpenFrame(string keyword, string label, double top, int depth)
    {
        public string Keyword { get; } = keyword;
        public string Label { get; } = label;
        public double Top { get; } = top;
        public int Depth { get; } = depth;
        public List<DomainSequenceDivider> Dividers { get; } = [];
        public double Low { get; private set; } = double.MaxValue;
        public double High { get; private set; } = double.MinValue;

        public void Include(double low, double high)
        {
            Low = Math.Min(Low, low);
            High = Math.Max(High, high);
        }
    }

    [GeneratedRegex(@"^([A-Za-z0-9_]+)(?:\s+as\s+(.+))?$")]
    private static partial Regex Declared();

    [GeneratedRegex(@"^[Nn]ote\s+(over|left of|right of)\s+([A-Za-z0-9_]+)(?:\s*,\s*([A-Za-z0-9_]+))?\s*:\s*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex Note();

    [GeneratedRegex(@"^([A-Za-z0-9_]+)\s*(-->>|->>|-->|->|--x|-x|--\)|-\))\s*[+-]?\s*([A-Za-z0-9_]+)\s*(?::(.*))?$")]
    private static partial Regex Message();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreak();
}
