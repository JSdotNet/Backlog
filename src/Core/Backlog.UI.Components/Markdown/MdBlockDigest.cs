using System.Security.Cryptography;
using System.Text;

namespace Backlog.UI.Components.Markdown;

/// <summary>
/// A short digest of what one block says — the second half of a comment's
/// anchor, beside its block index.
/// <para>
/// An index alone is only true of the chapter it was taken from. Insert a
/// paragraph above a remark and every index below it is off by one, silently:
/// the remark still points at a block, just not the one it was about. The
/// digest is what makes that detectable — the index says where to look first,
/// the digest says whether the block found there is the right one.
/// </para>
/// <para>
/// It is taken from the block's <em>text</em>, not from the source lines it was
/// parsed from, and the difference is deliberate. Rewrapping a paragraph,
/// re-indenting a list or changing emphasis markers all rewrite the source
/// without changing a word the person was remarking on, and an anchor that
/// breaks on those is an anchor that breaks on formatting. Flattening through
/// <see cref="MarkdownRender.PlainText"/> first makes the digest survive them.
/// A block whose wording actually changes gets a new digest, which is correct:
/// the remark was about the old wording, and the search that follows will say
/// so rather than pretending otherwise.
/// </para>
/// <para>
/// Eight hex characters of SHA-256, which is the short-digest length this
/// repository already uses for cache path folding. It names a block within one
/// chapter, so the collision that matters is against the handful of other
/// blocks in that chapter, not against the world.
/// </para>
/// </summary>
public static class MdBlockDigest
{
    /// <summary>How many hex characters of the hash are kept.</summary>
    private const int Length = 8;

    /// <summary>
    /// The digest of one block, or <c>null</c> for a block with nothing to say —
    /// a divider, or anything that flattens to no text. A null digest anchors
    /// nothing, so a comment on such a block keeps the index-only behaviour it
    /// has today rather than being given an anchor that can never match.
    /// </summary>
    public static string? Of(MdBlock? block)
    {
        var text = TextOf(block);
        return string.IsNullOrEmpty(text) ? null : Digest(text);
    }

    /// <summary>
    /// The digest of the block at <paramref name="index"/>, or <c>null</c> when
    /// the index names no block.
    /// </summary>
    public static string? Of(IReadOnlyList<MdBlock>? blocks, int index) =>
        blocks is null || index < 0 || index >= blocks.Count ? null : Of(blocks[index]);

    /// <summary>
    /// What a block says, flattened and canonicalised — the text the digest is
    /// taken from. Public because a test that explains why two blocks share a
    /// digest is clearer about the text than about the hash.
    /// </summary>
    public static string TextOf(MdBlock? block) => Canonical(Raw(block));

    private static string Raw(MdBlock? block) => block switch
    {
        MdHeading heading => MarkdownRender.PlainText(heading.Content),
        MdParagraph paragraph => MarkdownRender.PlainText(paragraph.Content),
        MdQuote quote => MarkdownRender.PlainText(quote.Content),

        // The fence body as written. It is already raw text, and normalising
        // its inner whitespace would fold away the indentation that is the
        // point of a code block.
        MdCode code => code.Text,

        // Items in order, separated so that two lists holding the same words in
        // a different order do not collide.
        MdList list => string.Join("\n", list.Items.Select(ItemText)),

        // Header first, then the body rows, so a table that gained a column
        // heading reads as changed.
        MdTable table => string.Join(
            "\n",
            ((MdTableRow[])[table.Header, .. table.Rows])
                .Select(row => string.Join("\t", row.Cells.Select(cell => MarkdownRender.PlainText(cell.Content))))),

        // A sub-item's own title plus everything under it, so nesting is part
        // of what the block says.
        MdSubItem subItem => string.Join(
            "\n",
            [MarkdownRender.PlainText(subItem.Title), .. subItem.Children.Select(Raw)]),

        // A divider says nothing, and a footnote block is chapter-wide
        // apparatus rather than a passage anyone remarks on.
        _ => string.Empty
    };

    private static string ItemText(MdListItem item) => string.Join(
        "\n",
        [
            MarkdownRender.PlainText(item.Content),
            .. item.Nested.SelectMany(nested => nested.Items.Select(ItemText))
        ]);

    /// <summary>
    /// Line endings folded, each line's outer whitespace dropped, blank lines at
    /// either end removed — the same canonicalisation
    /// <see cref="Diagrams.DiagramArtifacts" /> does before hashing, and for the
    /// same reason: a digest must not change because an editor rewrote
    /// whitespace nobody reads.
    /// </summary>
    private static string Canonical(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.Trim())
            .ToList();

        while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);

        return string.Join("\n", lines);
    }

    private static string Digest(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..Length];
}
