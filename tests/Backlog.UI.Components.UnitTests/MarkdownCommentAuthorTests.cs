namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A remark left by a tool carries the tool's slug as its author —
/// <c>claude/chapter-review</c> — which is an address, not a name. The view reads
/// it as one; every author a person typed stays exactly as they typed it.
/// </summary>
public sealed class MarkdownCommentAuthorTests
{
    [Theory]
    [InlineData("claude/chapter-review", "Claude · chapter review")]
    [InlineData("claude/chapter_review", "Claude · chapter review")]
    [InlineData("devbook/annotations/v2", "Devbook · annotations · v2")]
    [InlineData("copilot/pr-review.bot", "Copilot · pr review.bot")]
    public void A_slug_reads_as_a_name(string author, string expected)
    {
        Assert.Equal(expected, MarkdownCommentAuthor.Display(author));
    }

    [Theory]
    [InlineData("You")]
    [InlineData("DEV-LAPTOP")]
    [InlineData("Reviewer")]
    [InlineData("agent via MCP")]
    [InlineData("jobsc")]
    [InlineData("someone@example.test")]
    [InlineData("a b/c")]
    [InlineData("claude/")]
    [InlineData("/chapter-review")]
    [InlineData("claude//review")]
    [InlineData("claude/re view")]
    [InlineData("")]
    public void Anything_that_is_not_a_slug_stays_as_written(string author)
    {
        Assert.Equal(author, MarkdownCommentAuthor.Display(author));
    }

    [Fact]
    public void Only_a_slug_is_said_to_be_one()
    {
        Assert.True(MarkdownCommentAuthor.IsSlug("claude/chapter-review"));
        Assert.False(MarkdownCommentAuthor.IsSlug("DEV-LAPTOP"));
        Assert.False(MarkdownCommentAuthor.IsSlug(null));
    }
}
