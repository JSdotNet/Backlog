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
/// A comment that carries <see cref="MarkdownComment.BlockHash"/> is re-anchored
/// here, because this is the only place that holds the blocks to check it
/// against. That is not the view guessing: the digest either matches a block or
/// it does not, and a comment without one keeps the old rule exactly — rendered
/// against the index it was told, and shown as orphaned when that index is out
/// of range.
/// </para>
/// <para>
/// An orphan is never dropped and its anchor is never written back. Inline, it
/// shows in the labelled orphan group after the last block; in the margin, it
/// sits level with the block at its stored index, clamped into range, and is
/// marked approximate.
/// </para>
/// </summary>
/// <param name="Id">Unique within the view, and what a callback reports.</param>
/// <param name="BlockIndex">Which block of the rendered list it hangs off, from
/// zero. Where it goes when that is no longer true depends on
/// <paramref name="BlockHash"/>: without one, out of range means the block went
/// away and the view shows the comment as orphaned rather than dropping it,
/// because a lost comment is worse than a stray one — inline in the labelled
/// end group, in the margin level with the block at this index clamped into
/// range and marked approximate. The index is never rewritten.</param>
/// <param name="Body">What was said.</param>
/// <param name="Author">Who said it. Null for a note to self.</param>
/// <param name="Timestamp">When, already formatted — what "2 hours ago" is, and
/// in what language, belongs to the host.</param>
/// <param name="Resolved">Whether it has been dealt with. A resolved comment
/// stays visible and quiet rather than disappearing: the reason a paragraph
/// reads the way it does is usually in the comment that got it there.</param>
/// <param name="BlockHash">A digest of what the block said when the comment was
/// made, from <see cref="MdBlockDigest"/>. Given one, the view trusts
/// <paramref name="BlockIndex"/> only while the block there still matches, and
/// otherwise looks for the block that does — which is what keeps a comment on
/// its own passage when the chapter is edited above it. Null leaves the comment
/// anchored by index alone.</param>
public sealed record MarkdownComment(
    string Id,
    int BlockIndex,
    string Body,
    string? Author = null,
    string? Timestamp = null,
    bool Resolved = false,
    string? BlockHash = null);

/// <summary>Where a read view draws the remarks against its blocks.</summary>
public enum MarkdownCommentLayout
{
    /// <summary>Under the block they belong to, indented and ruled. Closest to
    /// what they are about, and the only shape that works in a narrow column —
    /// but it pushes the body apart, so a heavily annotated document stops
    /// reading as a document.</summary>
    Inline,

    /// <summary>In a column beside the body, each one level with the block it
    /// belongs to where there is room. The prose stays continuous and the remarks
    /// stay findable, which is what you want when reviewing rather than when
    /// reading. With the margin script attached, a block's row never grows to the
    /// height of its remarks: a long stack runs on down the column beside the
    /// blocks after it, the next block's remarks start below it, and a chapter
    /// insert under a heading is pushed below it. Without the script the rows
    /// stretch.
    /// <para>
    /// Orphans stay in the column too, remarks and AI proposals alike: each is
    /// placed level with the block at its stored index, clamped into range — a
    /// proposal that names no block beside the last one — and marked
    /// approximate. That is presentation only; the anchor is never written back.
    /// </para>
    /// <para>
    /// Needs the room. Below the narrow breakpoint the column is not drawn: each
    /// block's remarks stack under it, ruled as inline remarks are, and each
    /// orphan stacks under the block it is placed beside, keeping its label and
    /// its dashed rule. It is not <see cref="Inline"/>: no orphan goes to a group
    /// at the end.
    /// </para></summary>
    Margin
}
