namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// What the digest treats as the same block and what it treats as a different
/// one — the two halves of whether a remark stays put.
/// </summary>
public sealed class MdBlockDigestTests
{
    private static MdBlock Only(string markdown) => Assert.Single(MarkdownPreview.ParseDocument(markdown));

    [Fact]
    public void The_same_words_wrapped_differently_are_the_same_block()
    {
        // The reason the digest is taken from the text rather than the source
        // lines: reflowing a paragraph is not editing it.
        Assert.Equal(
            MdBlockDigest.Of(Only("A sentence that was wrapped by an editor.")),
            MdBlockDigest.Of(Only("A sentence that was\nwrapped by an editor.")));
    }

    [Fact]
    public void Emphasis_markers_do_not_change_the_block()
    {
        // Bolding a word changes the source and not what the paragraph says.
        Assert.Equal(
            MdBlockDigest.Of(Only("This matters a great deal.")),
            MdBlockDigest.Of(Only("This matters a **great** deal.")));
    }

    [Fact]
    public void Different_words_are_a_different_block()
    {
        Assert.NotEqual(
            MdBlockDigest.Of(Only("The first paragraph.")),
            MdBlockDigest.Of(Only("The second paragraph.")));
    }

    [Fact]
    public void A_list_reordered_is_a_different_block()
    {
        // Order is part of what a list says, so it is part of the digest.
        Assert.NotEqual(
            MdBlockDigest.Of(Only("- one\n- two")),
            MdBlockDigest.Of(Only("- two\n- one")));
    }

    [Fact]
    public void A_block_with_nothing_to_say_has_no_digest()
    {
        // A divider anchors nothing. Giving it a digest would mean every
        // divider in a chapter shared one, and a remark on one could be
        // re-anchored to any other.
        Assert.Null(MdBlockDigest.Of(Only("---")));
    }

    [Fact]
    public void An_index_naming_no_block_has_no_digest()
    {
        var blocks = MarkdownPreview.ParseDocument("# A heading\n\nA paragraph.");

        Assert.Null(MdBlockDigest.Of(blocks, -1));
        Assert.Null(MdBlockDigest.Of(blocks, blocks.Count));
    }

    [Fact]
    public void A_digest_is_eight_hex_characters()
    {
        var digest = MdBlockDigest.Of(Only("A paragraph."));

        Assert.NotNull(digest);
        Assert.Equal(8, digest.Length);
        Assert.All(digest, character => Assert.Contains(character, "0123456789abcdef"));
    }
}
