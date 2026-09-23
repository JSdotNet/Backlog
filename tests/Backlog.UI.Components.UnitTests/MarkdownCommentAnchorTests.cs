using System.Globalization;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Re-anchoring: what happens to a remark when the chapter under it is edited.
/// <para>
/// Every test here works the same way — parse a chapter, take the digest of the
/// block a remark is about, then parse a <em>different</em> chapter and render
/// the remark against that. That is exactly what a second device, or a
/// pull, or tomorrow's edit does to a stored remark, and it is the only thing
/// the digest exists for.
/// </para>
/// </summary>
public sealed class MarkdownCommentAnchorTests
{
    private const string Original = """
        # A heading

        The first paragraph.

        The paragraph a reader remarked on.

        The last paragraph.
        """;

    private static IReadOnlyList<MdBlock> Parse(string markdown) => MarkdownPreview.ParseDocument(markdown);

    private static IRenderedComponent<MarkdownView> Render(
        BunitContext context,
        IReadOnlyList<MdBlock> blocks,
        params MarkdownComment[] comments)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        return context.Render<MarkdownView>(p => p
            .Add(v => v.Blocks, blocks)
            .Add(v => v.Comments, comments));
    }

    /// <summary>The block index the remark in these tests was left on.</summary>
    private const int RemarkedOn = 2;

    private static string DigestOfRemarkedBlock() => MdBlockDigest.Of(Parse(Original), RemarkedOn)!;

    private static MarkdownComment Remark(int blockIndex, string? digest) =>
        new("c1", blockIndex, "Is this still true?", "Reviewer", "today", BlockHash: digest);

    /// <summary>Which block a comment was drawn against, read off the row's own
    /// <c>data-block</c>, or -1 when it was drawn in the orphan region at the
    /// end. Rows exist only for blocks something hangs off, so the attribute is
    /// the only honest way to ask.</summary>
    private static int RowOf(IRenderedComponent<MarkdownView> view, string commentId)
    {
        var comment = view.Find($"[data-testid='markdown-comment-body-{commentId}']");

        for (var node = comment.ParentElement; node is not null; node = node.ParentElement)
        {
            if (node.ClassList.Contains("md-comments--orphaned")) return -1;

            if (node.GetAttribute("data-block") is { } block)
            {
                return int.Parse(block, CultureInfo.InvariantCulture);
            }
        }

        return -2;
    }

    /// <summary>What the row a comment landed on actually says.</summary>
    private static string RowTextOf(IRenderedComponent<MarkdownView> view, string commentId) =>
        view.Find($".md-block-row[data-block='{RowOf(view, commentId)}']").TextContent;

    [Fact]
    public void An_unmoved_block_keeps_the_index_the_remark_was_left_on()
    {
        // The common case, and the one that must stay cheap: nothing has
        // changed, the digest matches where it says, and no search happens.
        using var context = new BunitContext();

        var view = Render(context, Parse(Original), Remark(RemarkedOn, DigestOfRemarkedBlock()));

        Assert.Empty(view.FindAll(".md-comments--orphaned"));
        Assert.Contains("The paragraph a reader remarked on.", RowTextOf(view, "c1"));
    }

    [Fact]
    public void A_remark_follows_its_paragraph_when_the_chapter_grows_above_it()
    {
        // Two paragraphs inserted above push the remarked block from 2 to 4.
        // Index alone would leave the remark on "The last paragraph."
        using var context = new BunitContext();

        var edited = Parse("""
            # A heading

            The first paragraph.

            Something new.

            Something else new.

            The paragraph a reader remarked on.

            The last paragraph.
            """);

        var view = Render(context, edited, Remark(RemarkedOn, DigestOfRemarkedBlock()));

        Assert.Empty(view.FindAll(".md-comments--orphaned"));
        Assert.Contains("The paragraph a reader remarked on.", RowTextOf(view, "c1"));
    }

    [Fact]
    public void A_remark_follows_its_paragraph_when_the_chapter_shrinks_above_it()
    {
        // The other direction, which searching only forwards would miss.
        using var context = new BunitContext();

        var edited = Parse("""
            # A heading

            The paragraph a reader remarked on.

            The last paragraph.
            """);

        var view = Render(context, edited, Remark(RemarkedOn, DigestOfRemarkedBlock()));

        Assert.Empty(view.FindAll(".md-comments--orphaned"));
        Assert.Contains("The paragraph a reader remarked on.", RowTextOf(view, "c1"));
    }

    [Fact]
    public void A_remark_survives_its_paragraph_being_rewrapped()
    {
        // The reason the digest is taken from the block's text rather than the
        // lines it was parsed from: this edit changes every byte of the source
        // and not one word of what the paragraph says.
        using var context = new BunitContext();

        var edited = Parse("""
            # A heading

            The first paragraph.

            The paragraph a reader
            remarked on.

            The last paragraph.
            """);

        var view = Render(context, edited, Remark(RemarkedOn, DigestOfRemarkedBlock()));

        Assert.Empty(view.FindAll(".md-comments--orphaned"));
        Assert.Equal(RemarkedOn, RowOf(view, "c1"));
    }

    [Fact]
    public void A_remark_whose_paragraph_was_deleted_shows_at_the_end()
    {
        // Pinning to the end is the last resort, not the first rule — but it is
        // still what happens, because a lost remark is worse than a stray one.
        using var context = new BunitContext();

        var edited = Parse("""
            # A heading

            The first paragraph.

            The last paragraph.
            """);

        var view = Render(context, edited, Remark(RemarkedOn, DigestOfRemarkedBlock()));

        Assert.Single(view.FindAll(".md-comments--orphaned"));
        Assert.Equal(-1, RowOf(view, "c1"));
    }

    [Fact]
    public void A_remark_whose_paragraph_changed_wording_shows_at_the_end()
    {
        // Rewriting the passage is not moving it. The remark was about what it
        // used to say, and saying so is more honest than attaching it to prose
        // it was never about.
        using var context = new BunitContext();

        var edited = Parse("""
            # A heading

            The first paragraph.

            The paragraph now says something else entirely.

            The last paragraph.
            """);

        var view = Render(context, edited, Remark(RemarkedOn, DigestOfRemarkedBlock()));

        Assert.Single(view.FindAll(".md-comments--orphaned"));
    }

    [Fact]
    public void A_remark_without_a_digest_is_anchored_by_index_exactly_as_before()
    {
        // Every remark stored before the digest existed. It has to keep landing
        // where it always did, wrong-but-unchanged included.
        using var context = new BunitContext();

        var edited = Parse("""
            # A heading

            The first paragraph.

            Something new.

            The paragraph a reader remarked on.

            The last paragraph.
            """);

        var view = Render(context, edited, Remark(RemarkedOn, digest: null));

        Assert.Empty(view.FindAll(".md-comments--orphaned"));
        Assert.Equal(RemarkedOn, RowOf(view, "c1"));
        Assert.Contains("Something new.", RowTextOf(view, "c1"));
    }

    [Fact]
    public void A_repeated_passage_takes_the_copy_nearest_the_remembered_index()
    {
        // Two blocks that genuinely say the same thing. Left-to-right would hand
        // the remark to the first one whatever the index said; nearest-first
        // keeps it where the reader left it.
        using var context = new BunitContext();

        var repeated = """
            # A heading

            Ditto.

            The first paragraph.

            Ditto.

            The last paragraph.
            """;

        var blocks = Parse(repeated);
        var digest = MdBlockDigest.Of(blocks, 3)!;

        var view = Render(context, blocks, Remark(3, digest));

        Assert.Equal(3, RowOf(view, "c1"));
    }
}
