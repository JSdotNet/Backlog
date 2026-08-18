namespace Backlog.UI.Components.Markdown;

/// <summary>
/// A remark left against one block of a read view.
/// <para>
/// Anchored to a block index rather than to a character range, and that is the
/// whole of the design decision. A range survives nothing: insert a word above
/// it and every offset below moves, and a comment that follows the text it was
/// about needs the editor to rewrite every anchor on every keystroke. A block
/// index moves only when blocks are added or removed, which is something a host
/// can notice and fix up — and when it is wrong it is wrong by a paragraph, not
/// by half a sentence.
/// </para>
/// <para>
/// Which means the host owns re-anchoring. This library renders comments against
/// the blocks it is told and never guesses where one should have gone.
/// </para>
/// </summary>
/// <param name="Id">Unique within the view, and what a callback reports.</param>
/// <param name="BlockIndex">Which block of the rendered list it hangs off, from
/// zero. Out of range means the block went away; the view shows it at the end
/// rather than dropping it, because a lost comment is worse than a stray one.</param>
/// <param name="Body">What was said.</param>
/// <param name="Author">Who said it. Null for a note to self.</param>
/// <param name="Timestamp">When, already formatted — what "2 hours ago" is, and
/// in what language, belongs to the host.</param>
/// <param name="Resolved">Whether it has been dealt with. A resolved comment
/// stays visible and quiet rather than disappearing: the reason a paragraph
/// reads the way it does is usually in the comment that got it there.</param>
public sealed record MarkdownComment(
    string Id,
    int BlockIndex,
    string Body,
    string? Author = null,
    string? Timestamp = null,
    bool Resolved = false);

/// <summary>Where a read view draws the remarks against its blocks.</summary>
public enum MarkdownCommentLayout
{
    /// <summary>Under the block they belong to, indented and ruled. Closest to
    /// what they are about, and the only shape that works in a narrow column —
    /// but it pushes the body apart, so a heavily annotated document stops
    /// reading as a document.</summary>
    Inline,

    /// <summary>In a column beside the body, each one level with the block it
    /// belongs to. The prose stays continuous and the remarks stay findable,
    /// which is what you want when reviewing rather than when reading.
    /// <para>
    /// Needs the room. Below the narrow breakpoint it falls back to
    /// <see cref="Inline"/> on its own rather than squeezing two columns into
    /// one — a margin note in a phone-width column is an inline note that has
    /// been made harder to read.
    /// </para></summary>
    Margin
}
