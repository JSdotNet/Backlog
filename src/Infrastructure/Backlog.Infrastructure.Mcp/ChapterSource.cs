using System.Text;

using Backlog.UI.Components.Markdown;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// A chapter's own bytes, addressed by the line ranges the parse reports.
/// <para>
/// The whole of why this exists: a session that reads a chapter here goes on to
/// propose an edit to it, and an edit proposed against reformatted prose is an
/// edit that reformats the file. <see cref="MarkdownPreview"/> is a read-view
/// parser and lossy on purpose — it re-pipes a table, re-indents a list, joins a
/// wrapped paragraph onto one line, and renders raw HTML, a reference-style link
/// and a setext heading as plain text — so writing its model back out would hand
/// a session a document its author would not recognise. Nothing here writes
/// markdown. It cuts and it splices, and every byte it does not cut out is the
/// author's.
/// </para>
/// <para>
/// The index a span names is a line of the body split on <c>\n</c> after
/// <c>\r\n</c> is normalized, which is the split the parser makes. The segments
/// below are the same split with each line's own terminator left on it — same
/// count, same order, so a span indexes both — which is what makes
/// <see cref="Text"/> of every segment the file again, <c>\r\n</c> and all.
/// </para>
/// </summary>
internal static class ChapterSource
{
    /// <summary>
    /// The body's lines, each carrying the newline that ended it. Concatenating
    /// them is the body, exactly — including a final line with no terminator,
    /// and including a lone <c>\r</c>, which the parser does not treat as a line
    /// break either.
    /// </summary>
    internal static IReadOnlyList<string> Lines(string body)
    {
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
        // too, and a segment list one shorter would knock every later index out.
        segments.Add(body[start..]);

        return segments;
    }

    /// <summary>The line ending this document uses, for anything spliced into
    /// it. A file written with <c>\r\n</c> gets <c>\r\n</c> back rather than a
    /// few stray Unix lines in the middle of it.</summary>
    internal static string Newline(string body) =>
        body.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    /// <summary>
    /// The source behind one span, verbatim, without the newline that ended its
    /// last line — a block's text, not a block's text and a blank.
    /// </summary>
    internal static string Slice(IReadOnlyList<string> segments, MdSourceSpan span)
    {
        if (!span.IsKnown) return string.Empty;

        var builder = new StringBuilder();

        for (var index = span.StartLine; index < span.EndLineExclusive && index < segments.Count; index++)
        {
            builder.Append(segments[index]);
        }

        if (builder.Length > 0 && builder[^1] == '\n')
        {
            builder.Length -= builder.Length > 1 && builder[^2] == '\r' ? 2 : 1;
        }

        return builder.ToString();
    }
}
