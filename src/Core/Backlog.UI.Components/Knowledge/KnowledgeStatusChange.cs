namespace Backlog.UI.Components.Knowledge;

/// <summary>
/// A reader picked a different status in the metadata record beside a chapter's
/// heading.
/// <para>
/// Both keys travel, because neither is enough on its own. The block index is
/// what the view knows and what it anchors everything else by; the heading is
/// what a host knows the chapter as — a knowledge panel addresses a chapter's
/// status by the section anchor it derived from that heading, and it has no way
/// back from a block index to one. A host uses whichever of the two it holds.
/// </para>
/// <para>
/// The status is the word the folder uses, unchanged. What writing it means —
/// which file, which fence, and what happens when the file has moved on since it
/// was read — is the host's, exactly as it is for every other callback in this
/// library.
/// </para>
/// </summary>
/// <param name="BlockIndex">Which block of the rendered document the record
/// belongs to, from zero: the heading's index, not the fence's.</param>
/// <param name="Heading">The heading text, when the view could recover it from
/// the source. Null when the view was given blocks and no source to read the
/// chapter titles back out of.</param>
/// <param name="Status">The status that was chosen.</param>
public sealed record KnowledgeStatusChange(int BlockIndex, string? Heading, string Status);
