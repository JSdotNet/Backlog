using Backlog.SharedKernel.Markdown;

namespace Backlog.UI.Components.Markdown;

/// <summary>
/// The editing state of a live markdown editor: every block rendered but the one
/// holding the caret, which shows its own source in a textarea.
/// <para>
/// The text stays the truth. The whole source is always the untouched segments
/// before the active region, what was typed into it, and the untouched segments
/// after — <see cref="MarkdownSourceBlocks.Replace"/> on the document as it was
/// last cut — so a block nobody touched is never written out of the parser's
/// lossy model, and clicking into a block and out again changes no byte.
/// </para>
/// <para>
/// The document holds still while the caret is in a block. Typing does not cut
/// it again: a half-typed opening fence would otherwise swallow every block
/// after it mid-word, and the textarea under the caret would be rebuilt on every
/// keystroke. It is cut again when the caret leaves — <see cref="Commit"/>, and
/// every move that implies one — and from then on a block is found by the lines
/// it sits on rather than by its index, because the edit may have turned one
/// block into two or two into one.
/// </para>
/// <para>
/// Plain C#, no Blazor, so its rules are tested without a renderer; the
/// component draws <see cref="Document"/> and forwards the keys.
/// </para>
/// </summary>
public sealed class MarkdownLiveState
{
    /// <summary>One past the last segment of the active region. A region is one
    /// segment until a join grows it over its neighbours.</summary>
    private int _activeEnd;

    /// <summary>What the textarea holds, in <c>\n</c> line ends, once anything
    /// has been typed or joined into the region; null while it still holds the
    /// region's own text.</summary>
    private string? _typed;

    /// <summary>The last source handed to the host, so the host handing it back
    /// — trimmed or not — is read as the echo it is. See
    /// <see cref="Reset"/>.</summary>
    private string? _emitted;

    /// <summary>
    /// How many steps undo keeps. A step holds whole sources, so a long session
    /// in a long chapter would otherwise keep a copy of the chapter per word for
    /// as long as the editor lives; past this the oldest steps go.
    /// </summary>
    public const int HistoryLimit = 500;

    /// <summary>The steps undo takes back, newest last.</summary>
    private readonly List<HistoryEntry> _undo = [];

    /// <summary>The steps undo has taken back and redo puts back, newest
    /// last.</summary>
    private readonly List<HistoryEntry> _redo = [];

    /// <summary>What the step still open for more keystrokes is doing, or
    /// <see cref="EditKind.None"/> when the next edit starts a step of its own.
    /// See <see cref="Type"/>.</summary>
    private EditKind _burst;

    /// <summary>Where the last edit may have left the caret, in the box's text —
    /// where the next keystroke of the same step has to take up. A range, not a
    /// place: a character typed beside one it repeats could have gone in on
    /// either side of it, and the text alone cannot say which.</summary>
    private (int Low, int High) _burstCaret;

    public MarkdownLiveState(string? source)
    {
        Source = source ?? string.Empty;
        Document = MarkdownSourceBlocks.Split(Source);
    }

    /// <summary>The whole source as it stands, typing included.</summary>
    public string Source { get; private set; }

    /// <summary>The source as it was last cut: what is drawn around the active
    /// region. It lags <see cref="Source"/> while the caret is in a block that
    /// has been typed into.</summary>
    public MdSourceDocument Document { get; private set; }

    /// <summary>The first segment of the active region, or null when no block
    /// holds the caret.</summary>
    public int? ActiveIndex { get; private set; }

    /// <summary>How many segments the active region covers — one, unless a join
    /// grew it; zero when nothing is active.</summary>
    public int ActiveSegmentCount => ActiveIndex is { } start ? _activeEnd - start : 0;

    /// <summary>The lines of <see cref="Document"/> the active region covers, or
    /// <see cref="MdSourceSpan.None"/>.</summary>
    public MdSourceSpan ActiveLines => ActiveIndex is { } start
        ? new MdSourceSpan(Document.Segments[start].Lines.StartLine, Document.Segments[_activeEnd - 1].Lines.EndLineExclusive)
        : MdSourceSpan.None;

    /// <summary>
    /// What the active textarea holds: the region's
    /// <see cref="MdSourceSegment.Editable"/> text until it is typed into.
    /// <para>
    /// In <c>\n</c> line ends whatever the document is written with, because that
    /// is what a browser textarea holds and reports — and every caret offset
    /// this state hands out counts characters of this text, so it has to be the
    /// text the browser counts in.
    /// </para>
    /// </summary>
    public string ActiveText => ActiveIndex is null ? string.Empty : _typed ?? AsTyped(RegionText(ActiveIndex.Value, _activeEnd));

    /// <summary>The task the last <see cref="ToggleTask"/> ticked, by the number
    /// the parser gives it in <see cref="Source"/> now — which an edit in the
    /// block that was open can make differ from the number the checkbox was drawn
    /// with — or null when that tick rewrote nothing. It is what the editor puts
    /// focus back on.</summary>
    public int? TickedTask { get; private set; }

    /// <summary>Whether segment <paramref name="index"/> is drawn as the
    /// textarea rather than rendered.</summary>
    public bool Covers(int index) => ActiveIndex is { } start && index >= start && index < _activeEnd;

    /// <summary>Whether what was typed has moved the source away from the
    /// document as it was last cut.</summary>
    private bool Dirty => !string.Equals(Source, Document.Source, StringComparison.Ordinal);

    /// <summary>How many lines the edit added (or, negative, removed) — the
    /// shift every line below the active region takes when the document is cut
    /// again.</summary>
    private int LineDelta =>
        MarkdownSourceBlocks.Lines(Source).Count - MarkdownSourceBlocks.Lines(Document.Source).Count;

    /// <summary>
    /// Puts the caret in segment <paramref name="index"/> of <see cref="Document"/>
    /// as it is drawn — what a click or an Enter on a rendered block does.
    /// <para>
    /// When another block was being edited, the caret leaves it first and the
    /// document is cut again; the index named the block as it was drawn, so it
    /// is found again by its first line, shifted by the lines the edit added
    /// above it.
    /// </para>
    /// </summary>
    /// <returns>False when the segment cannot hold the caret: it does not exist,
    /// or it is blank lines between blocks.</returns>
    public bool Activate(int index)
    {
        if (index < 0 || index >= Document.Segments.Count) return false;
        if (Covers(index)) return true;
        if (!CanHoldCaret(Document.Segments[index])) return false;

        if (ActiveIndex is not null && Dirty)
        {
            var line = Document.Segments[index].Lines.StartLine;
            if (line >= ActiveLines.EndLineExclusive) line += LineDelta;

            Commit();
            index = SegmentAt(line);
            if (index < 0 || !CanHoldCaret(Document.Segments[index])) return false;
        }

        Open(index, index + 1);
        return true;
    }

    /// <summary>
    /// What typing does: <paramref name="text"/> is the active textarea's whole
    /// value, and the return is the whole source it makes — the value to raise
    /// to the host.
    /// <para>
    /// Every change is also a step of the document's history, or part of one.
    /// A step runs for as long as the reader keeps doing the same thing in the
    /// same block, and one rule says when it ends: a new step starts when a word
    /// starts — a space or a line break typed after something that is not one —
    /// when typing turns into deleting or back, when a selection is typed over,
    /// and whenever the caret leaves the block or the block grows
    /// (<see cref="Commit"/>, <see cref="Step"/>, <see cref="Join"/>, a new
    /// <see cref="Activate"/>), at every tick, and at every undo and redo. A word
    /// is then what one undo takes back, which is what a reader expects of it;
    /// a clock would split a word typed slowly and merge a sentence typed fast.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">No block holds the caret.</exception>
    public string Type(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (ActiveIndex is null) throw new InvalidOperationException("No block holds the caret to type into.");

        var typed = AsTyped(text);
        var old = ActiveText;

        if (string.Equals(typed, old, StringComparison.Ordinal))
        {
            _emitted = Source;
            return Source;
        }

        var edit = Diff(old, typed, _burst == EditKind.None ? null : _burstCaret);
        var before = Source;
        var beforeSpot = SpotNow(edit.CaretBefore);

        _typed = typed;
        Source = MarkdownSourceBlocks.Replace(Document.Source, ActiveLines, _typed);
        _emitted = Source;

        var afterSpot = SpotNow(edit.CaretAfter);

        // A keystroke takes up where the last one left the caret: an insert or a
        // forward delete at it, a Backspace just before it. Anything else — a
        // paste, typing somewhere else in the box — is not the same burst.
        var oneKey = edit.Inserted <= 1;

        if (_burst == EditKind.None || edit.Kind != _burst || edit.Kind == EditKind.Replace || edit.StartsWord
            || !edit.TakesUp || !oneKey || _undo.Count == 0)
        {
            Push(new HistoryEntry(before, beforeSpot, Source, afterSpot));
        }
        else
        {
            _undo[^1].After = Source;
            _undo[^1].AfterSpot = afterSpot;
        }

        _redo.Clear();
        // A paste ends its own step, so what is typed after it is not undone
        // with it.
        _burst = oneKey ? edit.Kind : EditKind.None;
        _burstCaret = edit.CaretAfterRange;
        return Source;
    }

    /// <summary>The caret leaves the active block: the document is cut again
    /// from the source, so a block typed into two paragraphs is two blocks from
    /// here on, and no block holds the caret.</summary>
    public void Commit()
    {
        if (Dirty) Document = MarkdownSourceBlocks.Split(Source);

        ActiveIndex = null;
        _activeEnd = 0;
        _typed = null;
        _burst = EditKind.None;
    }

    /// <summary>
    /// Takes back the newest step: the whole source as it was before it, and the
    /// block it was made in open again — wherever the caret has gone since — with
    /// the caret where the step began.
    /// <para>
    /// Whole sources, not deltas, because the source is the truth: a step
    /// restores the bytes as they were, every <c>\r\n</c> included, and the
    /// document is cut again from them, so nothing a later edit did to the
    /// blocks can leave an undo pointing at lines that moved.
    /// </para>
    /// </summary>
    /// <returns>The source to raise and where the caret goes; null when there is
    /// nothing to undo. A step made on a rendered block — a tick — reopens
    /// nothing.</returns>
    public HistoryStep? Undo()
    {
        if (_undo.Count == 0) return null;

        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(entry);

        return Restore(entry.Before, entry.BeforeSpot, entry.Task);
    }

    /// <summary>Puts back the newest step undo took back, in the block it was
    /// made in, with the caret where the step ended.</summary>
    /// <returns>The source to raise and where the caret goes; null when there is
    /// nothing to redo — nothing was undone, or something was typed since.</returns>
    public HistoryStep? Redo()
    {
        if (_redo.Count == 0) return null;

        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(entry);

        return Restore(entry.After, entry.AfterSpot, entry.Task);
    }

    /// <summary>
    /// An arrow key pressed on the active block's first line (up,
    /// <paramref name="direction"/> negative) or last line (down, positive): the
    /// caret moves to the nearest block in that direction, skipping blank lines
    /// — at the end of it going up, at the start going down, as an arrow key
    /// moves through a single textarea.
    /// <para>
    /// The neighbour is found after the document is cut again, by line: the
    /// last block that starts above the edited region going up, the first that
    /// reaches below it going down. When the edit merged into that block, the
    /// caret lands on the line next to the edit inside it.
    /// </para>
    /// </summary>
    /// <returns>Where the caret lands, or null at the first or last block —
    /// where nothing moves, nothing is committed, and the block stays
    /// active.</returns>
    public CaretTarget? Step(int direction)
    {
        if (direction == 0) throw new ArgumentOutOfRangeException(nameof(direction), direction, "A step goes up or down.");
        if (ActiveIndex is not { } start) return null;

        var down = direction > 0;
        if (Neighbour(start, down) < 0) return null;

        var top = ActiveLines.StartLine;
        var bottom = ActiveLines.EndLineExclusive + LineDelta;

        Commit();

        var segments = Document.Segments;
        var target = -1;

        for (var index = 0; index < segments.Count; index++)
        {
            if (segments[index].Kind == MdSourceSegmentKind.Blank) continue;

            if (down && segments[index].Lines.EndLineExclusive > bottom)
            {
                target = index;
                break;
            }

            if (!down && segments[index].Lines.StartLine < top) target = index;
        }

        if (target < 0) return null;

        Open(target, target + 1);

        // The target may hold the edited lines too: an unclosed fence typed into
        // the block runs on over the one below, a list item typed under prose
        // joins the paragraph above. Then the caret goes one line on inside it,
        // as the arrow would in one textarea — to the start of the first line
        // below the edit, or the end of the last line above it — rather than
        // back to the edge of the text just typed.
        var lines = segments[target].Lines;
        var offset = down
            ? lines.StartLine < bottom ? TypedLength(lines.StartLine, bottom) : 0
            : lines.EndLineExclusive > top ? TypedLength(lines.StartLine, top) - 1 : ActiveText.Length;

        return new CaretTarget(target, offset);
    }

    /// <summary>
    /// Backspace at the very start of the active block (<paramref name="direction"/>
    /// negative) or Delete at its very end (positive): the region grows over the
    /// nearest block in that direction and the blank lines between, so the next
    /// Backspace or Delete removes the line break the way it would in one
    /// textarea. What was typed is kept; the source does not change.
    /// </summary>
    /// <returns>The caret offset in the grown <see cref="ActiveText"/> that keeps
    /// the caret beside the same character. At the first or last block nothing
    /// grows, and the caret stays where it was: at 0, or at the end.</returns>
    public int Join(int direction)
    {
        if (direction == 0) throw new ArgumentOutOfRangeException(nameof(direction), direction, "A join goes up or down.");
        if (ActiveIndex is not { } start) return 0;

        var text = ActiveText;
        var down = direction > 0;
        var neighbour = Neighbour(start, down);

        if (neighbour < 0) return down ? text.Length : 0;

        // The region is a different region now: the next edit is a step of its
        // own, so undoing the break the next key removes leaves the rest alone.
        _burst = EditKind.None;

        if (down)
        {
            // The region's own last line had a line end — something follows it —
            // and the joined lines bring theirs, all but the last.
            _typed = text + "\n" + AsTyped(RegionText(_activeEnd, neighbour + 1));
            _activeEnd = neighbour + 1;
            return text.Length;
        }

        // Every joined line above the region ends on a line end, the one that
        // separated it from what follows.
        var above = AsTyped(string.Concat(Document.Segments.Skip(neighbour).Take(start - neighbour).Select(segment => segment.Text)));
        _typed = above + text;
        ActiveIndex = neighbour;
        return above.Length;
    }

    /// <summary>
    /// The host set <see cref="Source"/>'s parameter to <paramref name="value"/>.
    /// <para>
    /// Usually that is the editor's own text coming back: the host stored what
    /// was raised and renders with it. A host that stores the body through a
    /// parser hands it back with its leading and trailing newlines trimmed —
    /// the rule <c>MarkdownEditor</c> reads the same echo by — and starting
    /// over on that would close the block being typed in and lose the blank
    /// line just typed. Anything else is a new document.
    /// </para>
    /// </summary>
    /// <returns>True when the state started over on the new value; false when it
    /// was the echo, or the source already.</returns>
    public bool Reset(string? value)
    {
        var incoming = value ?? string.Empty;

        if (string.Equals(incoming, Source, StringComparison.Ordinal)) return false;
        if (_emitted is not null && SameButForTheEdges(incoming, _emitted)) return false;

        Source = incoming;
        Document = MarkdownSourceBlocks.Split(incoming);
        ActiveIndex = null;
        _activeEnd = 0;
        _typed = null;
        _emitted = null;

        // A new document, so a new history: undoing into the one before it would
        // write the old document over whatever the host just set.
        _undo.Clear();
        _redo.Clear();
        _burst = EditKind.None;
        return true;
    }

    /// <summary>
    /// A checkbox ticked in a rendered list: that task's line rewritten with its
    /// marker flipped, the source cut again, and no block active.
    /// <para>
    /// <paramref name="taskIndex"/> is the one the checkbox was drawn with, from
    /// <see cref="Document"/>, and the task is found by the line it was drawn
    /// from rather than by its number. An edit in the active block can renumber
    /// every task after it — a task typed or deleted there, or a fence opened
    /// that turns the list below into code, or that pairs with a later fence and
    /// turns a code sample into a task — so no count can say which line was
    /// clicked. The line, shifted by the lines the edit added above it, can.
    /// When that line of the source is no task any more, the tick rewrites
    /// nothing: the checkbox stood for a line that is code now.
    /// </para>
    /// <para>
    /// Only the one line is written, through <see cref="MarkdownSourceBlocks.Replace"/>,
    /// so every other byte stays the author's. <see cref="MarkdownPreview.ToggleTask"/>
    /// still decides what counts as a task and how its marker flips — it is
    /// asked, on a <c>\n</c> copy, so the walk is the parser's — but its result
    /// is not the new source, because it writes the whole body back with
    /// <c>\n</c> and a <c>\r\n</c> file would come out with every line end
    /// changed.
    /// </para>
    /// </summary>
    /// <returns>The whole source, to raise to the host — unchanged, with the
    /// state as it was, when the index names no task or the task's line is no
    /// task now.</returns>
    public string ToggleTask(int taskIndex)
    {
        TickedTask = null;
        if (TaskLine(Document.Source, taskIndex) is not { } drawn) return Source;

        var line = drawn.Line;

        if (ActiveIndex is not null)
        {
            var region = ActiveLines;

            // A checkbox inside the region is not drawn — the region is the
            // textarea — so a tick naming one is not a click on anything.
            if (line >= region.StartLine && line < region.EndLineExclusive) return Source;
            if (line >= region.EndLineExclusive) line += LineDelta;
        }

        if (TickedAt(Source, line) is not { } tick) return Source;

        var ticked = tick.Ticked;

        // A step of its own. Undoing it reopens the block that was open when the
        // box was ticked, if one was, with the caret at its end.
        var before = Source;
        var beforeSpot = ActiveIndex is null ? null : SpotNow(ActiveText.Length);

        Source = MarkdownSourceBlocks.Replace(Source, new MdSourceSpan(line, line + 1), ticked);
        Document = MarkdownSourceBlocks.Split(Source);
        ActiveIndex = null;
        _activeEnd = 0;
        _typed = null;
        _emitted = Source;

        Push(new HistoryEntry(before, beforeSpot, Source, null) { Task = tick.Index });
        TickedTask = tick.Index;
        _redo.Clear();
        _burst = EditKind.None;
        return Source;
    }

    /// <summary>Whether a segment can hold the caret: any but blank lines — and
    /// those too when they are the whole document, which is what an empty
    /// document is, because an editor needs somewhere to type.</summary>
    private bool CanHoldCaret(MdSourceSegment segment) =>
        segment.Kind != MdSourceSegmentKind.Blank || Document.Segments.Count == 1;

    private void Open(int start, int end)
    {
        ActiveIndex = start;
        _activeEnd = end;
        _typed = null;
        _burst = EditKind.None;
    }

    /// <summary>The open region as a history step remembers it: its lines in the
    /// source as it stands — the cut's lines, shifted by what typing has added —
    /// and the caret at <paramref name="caret"/> in its text.</summary>
    private Spot SpotNow(int caret) =>
        new(ActiveLines.StartLine, ActiveLines.EndLineExclusive + LineDelta, caret);

    /// <summary>
    /// Puts <paramref name="source"/> back as the whole document, cut again, and
    /// reopens the blocks <paramref name="spot"/>'s lines fall in — blank lines at
    /// either end left out, as a region never starts or ends on them — with the
    /// caret on the same character it was on.
    /// </summary>
    private HistoryStep Restore(string source, Spot? spot, int? task)
    {
        Source = source;
        Document = MarkdownSourceBlocks.Split(source);
        ActiveIndex = null;
        _activeEnd = 0;
        _typed = null;
        _emitted = Source;
        _burst = EditKind.None;

        if (spot is null) return new HistoryStep(Source, null, task);

        var segments = Document.Segments;
        int first = -1, last = -1;

        for (var index = 0; index < segments.Count; index++)
        {
            var lines = segments[index].Lines;
            if (lines.StartLine >= spot.EndLine || lines.EndLineExclusive <= spot.StartLine) continue;
            if (segments[index].Kind == MdSourceSegmentKind.Blank) continue;

            if (first < 0) first = index;
            last = index;
        }

        if (first < 0)
        {
            // Only blank lines: the empty document, whose one blank segment is
            // where typing starts. Anything else blank holds no caret.
            if (segments.Count != 1) return new HistoryStep(Source, null, task);
            first = last = 0;
        }

        Open(first, last + 1);

        var opens = segments[first].Lines.StartLine;
        var shift = opens <= spot.StartLine ? TypedLength(opens, spot.StartLine) : -TypedLength(spot.StartLine, opens);
        var offset = Math.Clamp(spot.Caret + shift, 0, ActiveText.Length);

        return new HistoryStep(Source, new CaretTarget(first, offset), null);
    }

    /// <summary>
    /// What one change of the box's text did: what was taken out and what was put
    /// in, where, and where the caret stood on either side of it.
    /// <para>
    /// Where is not always one place. A character typed beside one it repeats —
    /// a space before a space, an "e" before an "e" — makes the same text on
    /// either side of it, so a pure insert or delete inside a run of equal
    /// characters could have landed anywhere along the run. Every such place is
    /// kept, and the one that takes up from <paramref name="previous"/> — where
    /// the last keystroke of the step may have left the caret — is the one taken.
    /// Taking the first place the texts differ instead put the edit one character
    /// off the caret it followed, and split one word into several steps.
    /// </para>
    /// </summary>
    private static Edit Diff(string old, string now, (int Low, int High)? previous)
    {
        var shorter = Math.Min(old.Length, now.Length);

        var prefix = 0;
        while (prefix < shorter && old[prefix] == now[prefix]) prefix++;

        // The longest common tail on its own, not limited by the prefix: the two
        // overlap exactly where the edit can slide.
        var tail = 0;
        while (tail < shorter && old[old.Length - 1 - tail] == now[now.Length - 1 - tail]) tail++;

        if (now.Length > old.Length && prefix + tail >= old.Length)
        {
            var inserted = now.Length - old.Length;
            var (low, high, takesUp) = Place(Math.Max(0, old.Length - tail), prefix, previous, 0);
            var at = high;
            var startsWord = char.IsWhiteSpace(now[at]) && (at == 0 || !char.IsWhiteSpace(old[at - 1]));

            return new Edit(EditKind.Insert, at, at, at + inserted, inserted, startsWord, takesUp, (low + inserted, high + inserted));
        }

        if (now.Length < old.Length && prefix + tail >= now.Length)
        {
            var removed = old.Length - now.Length;
            var (low, high, takesUp) = Place(Math.Max(0, now.Length - tail), prefix, previous, removed);

            return new Edit(EditKind.Delete, high, high + removed, high, 0, false, takesUp, (low, high));
        }

        // A replace: one place, the first the texts differ at.
        var suffix = Math.Min(tail, shorter - prefix);
        var taken = old.Length - prefix - suffix;
        var put = now.Length - prefix - suffix;
        var follows = previous is { } last && ((prefix >= last.Low && prefix <= last.High) || (prefix + taken >= last.Low && prefix + taken <= last.High));

        return new Edit(EditKind.Replace, prefix, prefix + taken, prefix + put, put, false, follows, (prefix + put, prefix + put));
    }

    /// <summary>
    /// The places from <paramref name="low"/> to <paramref name="high"/> an
    /// insert or a delete of <paramref name="removed"/> characters could start
    /// at, narrowed to the ones that take up where <paramref name="previous"/>
    /// left the caret — at it, or, for a Backspace, just before it.
    /// </summary>
    /// <returns>The narrowed places, and whether any took up; all of them, and
    /// false, when none did or there was no previous keystroke.</returns>
    private static (int Low, int High, bool TakesUp) Place(int low, int high, (int Low, int High)? previous, int removed)
    {
        if (previous is not { } last) return (low, high, false);

        var from = -1;
        var to = -1;

        for (var at = low; at <= high; at++)
        {
            var follows = (at >= last.Low && at <= last.High) || (removed > 0 && at + removed >= last.Low && at + removed <= last.High);
            if (!follows) continue;

            if (from < 0) from = at;
            to = at;
        }

        return from < 0 ? (low, high, false) : (from, to, true);
    }

    private enum EditKind
    {
        None,
        Insert,
        Delete,
        Replace
    }

    /// <summary>One change of the box's text: what it did, where it starts, where
    /// the caret stood on either side of it, how much it put in, whether it takes
    /// up where the step's last keystroke left the caret, and every place that
    /// keystroke may have left it after.</summary>
    private readonly record struct Edit(
        EditKind Kind,
        int Prefix,
        int CaretBefore,
        int CaretAfter,
        int Inserted,
        bool StartsWord,
        bool TakesUp,
        (int Low, int High) CaretAfterRange);

    /// <summary>A new step, newest last, with the oldest let go past
    /// <see cref="HistoryLimit"/>.</summary>
    private void Push(HistoryEntry entry)
    {
        _undo.Add(entry);
        if (_undo.Count > HistoryLimit) _undo.RemoveRange(0, _undo.Count - HistoryLimit);
    }

    /// <summary>Where the open region was in one version of the source — its
    /// lines in that source, and the caret in its text.</summary>
    private sealed record Spot(int StartLine, int EndLine, int Caret);

    /// <summary>One step: the whole source on either side of it, and where the
    /// caret was in each. Its after side is moved on while keystrokes
    /// coalesce into it.</summary>
    private sealed class HistoryEntry(string before, Spot? beforeSpot, string after, Spot? afterSpot)
    {
        public string Before { get; } = before;

        public Spot? BeforeSpot { get; } = beforeSpot;

        public string After { get; set; } = after;

        public Spot? AfterSpot { get; set; } = afterSpot;

        /// <summary>The task a tick ticked, by the index the parser numbers tasks
        /// with; null for every other step. A tick changes no task's number, so
        /// the one index names the task on both sides of it.</summary>
        public int? Task { get; init; }
    }

    /// <summary>The nearest segment that is not blank, above the region or below
    /// it; -1 when there is none.</summary>
    private int Neighbour(int start, bool down)
    {
        var segments = Document.Segments;

        if (down)
        {
            for (var index = _activeEnd; index < segments.Count; index++)
            {
                if (segments[index].Kind != MdSourceSegmentKind.Blank) return index;
            }

            return -1;
        }

        for (var index = start - 1; index >= 0; index--)
        {
            if (segments[index].Kind != MdSourceSegmentKind.Blank) return index;
        }

        return -1;
    }

    /// <summary>The segment of <see cref="Document"/> that line
    /// <paramref name="line"/> is in; -1 past the end.</summary>
    private int SegmentAt(int line)
    {
        var segments = Document.Segments;

        for (var index = 0; index < segments.Count; index++)
        {
            if (line >= segments[index].Lines.StartLine && line < segments[index].Lines.EndLineExclusive) return index;
        }

        return -1;
    }

    /// <summary>Segments <paramref name="start"/> up to <paramref name="end"/>
    /// as one piece of editable text: their bytes without the line end of the
    /// last line — the rule <see cref="MdSourceSegment.Editable"/> applies to
    /// one segment, applied to the run.</summary>
    private string RegionText(int start, int end)
    {
        var segments = Document.Segments;
        var lines = new MdSourceSpan(segments[start].Lines.StartLine, segments[end - 1].Lines.EndLineExclusive);
        var text = string.Concat(segments.Skip(start).Take(end - start).Select(segment => segment.Text));

        return new MdSourceSegment(MdSourceSegmentKind.Block, lines, text, null).Editable;
    }

    /// <summary>Source text the way a textarea holds it: <c>\n</c> line
    /// ends.</summary>
    private static string AsTyped(string text) => text.Replace("\r\n", "\n");

    /// <summary>Whether <paramref name="value"/> is <paramref name="emitted"/>
    /// with its leading and trailing newlines gone, read the way a textarea reads
    /// line ends — <c>MarkdownEditor</c>'s rule for the same echo.</summary>
    private static bool SameButForTheEdges(string value, string emitted)
    {
        static string Core(string text) => text.Replace("\r\n", "\n").Trim('\n');

        return string.Equals(Core(value), Core(emitted), StringComparison.Ordinal);
    }

    /// <summary>
    /// The line task <paramref name="taskIndex"/> of <paramref name="body"/> is
    /// written on, and that line with its marker flipped; null when the index
    /// names no task.
    /// <para>
    /// Found by asking <see cref="MarkdownPreview.ToggleTask"/> and seeing which
    /// line it changed, on a <c>\n</c> copy so the lines line up one for one.
    /// Its walk is the one the parser numbers tasks by — the same idea of a task
    /// line, the same blindness to fenced code — and a second walk written here
    /// would be one more place for the two to disagree about which line a
    /// number means.
    /// </para>
    /// </summary>
    private static (int Line, string Ticked)? TaskLine(string body, int taskIndex)
    {
        var lines = AsTyped(body).Split('\n');
        var ticked = MarkdownPreview.ToggleTask(string.Join('\n', lines), taskIndex).Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            if (!string.Equals(lines[index], ticked[index], StringComparison.Ordinal)) return (index, ticked[index]);
        }

        return null;
    }

    /// <summary>Line <paramref name="line"/> of <paramref name="body"/> with its
    /// marker flipped, and the number the parser gives that task in
    /// <paramref name="body"/>; null when that line is no task the parser counts —
    /// prose, or a <c>- [ ]</c> inside a fence. Tasks are numbered down the
    /// file, so the walk stops at the first one past the line.</summary>
    private static (int Index, string Ticked)? TickedAt(string body, int line)
    {
        for (var taskIndex = 0; TaskLine(body, taskIndex) is { } task; taskIndex++)
        {
            if (task.Line == line) return (taskIndex, task.Ticked);
            if (task.Line > line) return null;
        }

        return null;
    }

    /// <summary>How many characters lines <paramref name="from"/> up to
    /// <paramref name="to"/> of <see cref="Document"/> take in a textarea — their
    /// text and their line ends, in <c>\n</c>.</summary>
    private int TypedLength(int from, int to)
    {
        var lines = MarkdownSourceBlocks.Lines(Document.Source);
        var length = 0;

        for (var index = from; index < to; index++) length += AsTyped(lines[index]).Length;

        return length;
    }
}

/// <summary>Where the caret goes after a move: the segment of
/// <see cref="MarkdownLiveState.Document"/> now active, and the offset into its
/// <see cref="MarkdownLiveState.ActiveText"/>.</summary>
public readonly record struct CaretTarget(int Index, int Offset);

/// <summary>What an undo or a redo did: the whole source to raise, where the
/// caret goes — null when the step reopens no block — and, for a tick, which task
/// it ticked, so focus can go back to that task's checkbox.</summary>
public readonly record struct HistoryStep(string Source, CaretTarget? Caret, int? Task = null);
