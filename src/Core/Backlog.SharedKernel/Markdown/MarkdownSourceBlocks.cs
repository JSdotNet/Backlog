using System.Text;

namespace Backlog.SharedKernel.Markdown;

/// <summary>
/// A markdown body cut into the pieces a live editor edits one at a time: each
/// block the parse read, and the lines between blocks that no block claims.
/// <para>
/// The whole of why this exists: an editor that shows every block rendered but
/// the one holding the caret writes the document back as the untouched blocks
/// around what was typed, on every keystroke. <see cref="MarkdownPreview"/> is a
/// read-view parser and lossy on purpose — it re-pipes a table, joins a wrapped
/// paragraph and renders raw HTML as prose — so the untouched blocks cannot be
/// written out from its model. They are written out as the author's own bytes,
/// cut along the lines <see cref="MdBlock.Source"/> names. Nothing here writes
/// markdown; it cuts and it splices.
/// </para>
/// <para>
/// The cut is the one <c>ChapterSource</c> in the MCP adapter makes, kept here
/// because the kernel is what both the editor and the adapter may depend on: a
/// line is the body split on <c>\n</c> with its own terminator left on it, so
/// the line count and order are the parser's and a span indexes both.
/// </para>
/// </summary>
public static class MarkdownSourceBlocks
{
    /// <summary>
    /// The body as segments: every line in exactly one, in the order the lines
    /// came, joining back to <paramref name="body"/> byte for byte.
    /// <para>
    /// Parsed once, whole, with <see cref="MarkdownPreview.ParseDocument"/> —
    /// never <see cref="MarkdownPreview.Parse(string?, string?, IMarkdownMetadataReader?)"/>,
    /// which folds a <c>##</c> and everything under it into one sub-item — and
    /// each <see cref="MdSourceSegmentKind.Block"/> carries the block that parse
    /// returned. A segment's rendering is therefore the document's: a footnote
    /// reference keeps the number the whole body gave it, and a task its index
    /// into the whole body, where parsing the slice alone would restart both.
    /// </para>
    /// </summary>
    public static MdSourceDocument Split(string? body)
    {
        body ??= string.Empty;
        return Split(body, MarkdownPreview.ParseDocument(body));
    }

    /// <summary>
    /// The cut along <paramref name="blocks"/>' spans, for a parse the caller
    /// already has.
    /// <para>
    /// Fails safe. A span that overlaps the one before it, runs backwards, names
    /// a line past the end, or a block that names none — anything but the
    /// footnote list, which is collected rather than read — means the spans
    /// cannot be trusted, and a segment cut along a wrong span loses or repeats
    /// the author's lines on the next keystroke. The whole body becomes one
    /// <see cref="MdSourceSegmentKind.Unclaimed"/> segment instead: shown raw,
    /// still editable, and nothing lost.
    /// </para>
    /// </summary>
    internal static MdSourceDocument Split(string body, IReadOnlyList<MdBlock> blocks)
    {
        var lines = Lines(body);
        var newline = Newline(body);
        var footnotes = blocks.OfType<MdFootnotes>().LastOrDefault();
        var flow = blocks.Where(block => block is not MdFootnotes).ToList();

        if (!Consistent(flow, lines.Count))
        {
            return new MdSourceDocument(
                body,
                newline,
                [new MdSourceSegment(MdSourceSegmentKind.Unclaimed, new MdSourceSpan(0, lines.Count), body, null)],
                null);
        }

        var segments = new List<MdSourceSegment>();
        var cursor = 0;

        foreach (var block in flow)
        {
            AddGaps(segments, lines, cursor, block.Source.StartLine);
            segments.Add(new MdSourceSegment(MdSourceSegmentKind.Block, block.Source, Join(lines, block.Source), block));
            cursor = block.Source.EndLineExclusive;
        }

        AddGaps(segments, lines, cursor, lines.Count);

        return new MdSourceDocument(body, newline, segments, footnotes);
    }

    /// <summary>
    /// <paramref name="body"/> with the lines <paramref name="lines"/> names
    /// replaced by <paramref name="typed"/>, and every other byte its own.
    /// <para>
    /// <paramref name="typed"/> is what a textarea hands back, so its line ends
    /// are <c>\n</c>; they are written as the document's
    /// <see cref="Newline"/>, which keeps a <c>\r\n</c> file a <c>\r\n</c>
    /// file. The terminator that ended the span's last line is kept as it was —
    /// it separates the span from what follows rather than belonging to it, and
    /// a last line that had none still has none — which is why replacing a
    /// segment with its own <see cref="MdSourceSegment.Editable"/> text changes
    /// nothing.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The span names no line, or
    /// a line the body does not have.</exception>
    public static string Replace(string body, MdSourceSpan lines, string typed)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(typed);

        var all = Lines(body);
        if (!lines.IsKnown || lines.StartLine < 0 || lines.EndLineExclusive > all.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lines),
                lines,
                $"The body has {all.Count} lines; the span must name at least one of them.");
        }

        var last = all[lines.EndLineExclusive - 1];
        var terminator = last.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n"
            : last.EndsWith('\n') ? "\n"
            : string.Empty;

        var builder = new StringBuilder(body.Length + typed.Length);

        for (var index = 0; index < lines.StartLine; index++) builder.Append(all[index]);

        builder.Append(typed.Replace("\r\n", "\n").Replace("\n", Newline(body)));
        builder.Append(terminator);

        for (var index = lines.EndLineExclusive; index < all.Count; index++) builder.Append(all[index]);

        return builder.ToString();
    }

    /// <summary>
    /// The body's lines, each carrying the newline that ended it. Concatenating
    /// them is the body, exactly — including a final line with no terminator,
    /// and including a lone <c>\r</c>, which the parser does not treat as a line
    /// break either.
    /// </summary>
    public static IReadOnlyList<string> Lines(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var segments = new List<string>();
        var start = 0;

        for (var index = 0; index < body.Length; index++)
        {
            if (body[index] != '\n') continue;

            segments.Add(body[start..(index + 1)]);
            start = index + 1;
        }

        // The tail after the last newline, which is empty when the body ends on
        // one. Added either way: the parser's own Split produces that empty line
        // too, and a list one shorter would knock every later index out.
        segments.Add(body[start..]);

        return segments;
    }

    /// <summary>The line ending this document uses, for anything spliced into
    /// it. A file written with <c>\r\n</c> gets <c>\r\n</c> back rather than a
    /// few stray Unix lines in the middle of it.</summary>
    public static string Newline(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return body.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }

    /// <summary>Whether the spans run forward over lines the body has, each
    /// starting at or after the end of the one before — the property
    /// <c>MarkdownSourceSpanTests</c> pins on the parser, checked again here
    /// because the round trip depends on it rather than on the parser's
    /// promise.</summary>
    private static bool Consistent(IReadOnlyList<MdBlock> flow, int lineCount)
    {
        var cursor = 0;

        foreach (var block in flow)
        {
            var span = block.Source;
            if (!span.IsKnown || span.StartLine < cursor || span.EndLineExclusive > lineCount) return false;

            cursor = span.EndLineExclusive;
        }

        return true;
    }

    /// <summary>
    /// The lines between two blocks, as runs of their own: each run of blank
    /// lines one <see cref="MdSourceSegmentKind.Blank"/>, each run of the rest —
    /// the <c>[^label]:</c> definitions the parser lifts out of the flow — one
    /// <see cref="MdSourceSegmentKind.Unclaimed"/>. Blank is the parser's blank:
    /// a line that is nothing but whitespace.
    /// </summary>
    private static void AddGaps(List<MdSourceSegment> segments, IReadOnlyList<string> lines, int from, int to)
    {
        var start = from;

        while (start < to)
        {
            var blank = IsBlank(lines[start]);
            var end = start + 1;
            while (end < to && IsBlank(lines[end]) == blank) end++;

            var span = new MdSourceSpan(start, end);
            segments.Add(new MdSourceSegment(
                blank ? MdSourceSegmentKind.Blank : MdSourceSegmentKind.Unclaimed,
                span,
                Join(lines, span),
                null));

            start = end;
        }
    }

    private static bool IsBlank(string line) => string.IsNullOrWhiteSpace(line);

    private static string Join(IReadOnlyList<string> lines, MdSourceSpan span)
    {
        var builder = new StringBuilder();

        for (var index = span.StartLine; index < span.EndLineExclusive; index++)
        {
            builder.Append(lines[index]);
        }

        return builder.ToString();
    }
}

/// <summary>What a segment of the source is.</summary>
public enum MdSourceSegmentKind
{
    /// <summary>The lines one block of the parse was read from.</summary>
    Block,

    /// <summary>A run of lines that are empty or nothing but whitespace — the
    /// space between blocks, which holds no caret of its own.</summary>
    Blank,

    /// <summary>A run of lines no block claims and that are not blank: the
    /// <c>[^label]:</c> footnote definitions, or the whole body when its spans
    /// could not be trusted. Shown as raw markdown.</summary>
    Unclaimed
}

/// <summary>
/// One piece of the source: the lines it covers, and their bytes verbatim.
/// </summary>
/// <param name="Kind">What the lines are.</param>
/// <param name="Lines">The lines, indexed the way <see cref="MdSourceSpan"/>
/// indexes them.</param>
/// <param name="Text">The lines' own bytes, each line's terminator included —
/// the segments' texts joined are the body.</param>
/// <param name="Block">The block the parse read from these lines; null for
/// anything but <see cref="MdSourceSegmentKind.Block"/>.</param>
public sealed record MdSourceSegment(MdSourceSegmentKind Kind, MdSourceSpan Lines, string Text, MdBlock? Block)
{
    /// <summary>
    /// The text to edit: <see cref="Text"/> without the newline that ended its
    /// last line. That newline separates this segment from the next rather than
    /// belonging to it, so typing at the end of a paragraph types at the end of
    /// its last line.
    /// <para>
    /// Only the last <em>line's</em> newline. The body's final line has none,
    /// and a segment that ends on it — an unterminated fence, the blank tail
    /// after a closing newline — ends on a <c>\n</c> that closed the line before,
    /// inside the segment. Counted rather than guessed from the last character:
    /// a segment of n lines holds n newlines exactly when its last line has one.
    /// </para>
    /// </summary>
    public string Editable
    {
        get
        {
            if (Text.Count(character => character == '\n') < Lines.LineCount) return Text;

            return Text.EndsWith("\r\n", StringComparison.Ordinal) ? Text[..^2] : Text[..^1];
        }
    }
}

/// <summary>
/// A body and the segments it was cut into.
/// </summary>
/// <param name="Source">The body, verbatim.</param>
/// <param name="Newline">The line ending the body is written with — see
/// <see cref="MarkdownSourceBlocks.Newline"/>.</param>
/// <param name="Segments">Every line of the body in exactly one segment, in
/// order.</param>
/// <param name="Footnotes">The note list the parse collected, or null when no
/// reference resolved. It is no segment: its definitions were gathered from
/// wherever they were written, so it has no lines of its own to sit at.</param>
public sealed record MdSourceDocument(
    string Source,
    string Newline,
    IReadOnlyList<MdSourceSegment> Segments,
    MdFootnotes? Footnotes);
