using Backlog.SharedKernel.Markdown;

namespace Backlog.SharedKernel.UnitTests;

/// <summary>
/// The CommonMark backslash escape: a <c>\</c> before ASCII punctuation is that
/// character as text, and the backslash is not shown. A follow-up filed from a
/// person's answer is written this way, so words that look like a step, a heading or
/// a <c>#tag</c> read in the view as the words they are. A backslash before
/// anything else, as in a Windows path, is kept, and so is one inside a code span.
/// </summary>
public sealed class MarkdownPreviewEscapeTests
{
    [Theory]
    [InlineData(@"\- [ ] delete the old table", "- [ ] delete the old table")]
    [InlineData(@"\* [x] already done", "* [x] already done")]
    [InlineData(@"\+ [ ] a plus list item", "+ [ ] a plus list item")]
    [InlineData(@"\## A chapter", "## A chapter")]
    [InlineData(@"\# A second entry", "# A second entry")]
    [InlineData(@"\```", "```")]
    [InlineData(@"\~~~", "~~~")]
    [InlineData(@"\`task\` \`!done\`", "`task` `!done`")]
    [InlineData(@"Ship it \#urgent and \#later", "Ship it #urgent and #later")]
    [InlineData(@"Move it under \+other-plan", "Move it under +other-plan")]
    [InlineData(@"\> not a quote", "> not a quote")]
    public void An_escaped_line_reads_as_its_text_in_one_paragraph(string body, string expected)
    {
        var blocks = MarkdownPreview.Parse(body);

        var paragraph = Assert.IsType<MdParagraph>(Assert.Single(blocks));
        var text = Assert.IsType<MdText>(Assert.Single(paragraph.Content));
        Assert.Equal(expected, text.Text);
    }

    [Fact]
    public void An_escaped_tag_is_text_and_an_unescaped_one_beside_it_is_still_a_tag()
    {
        var inlines = MarkdownPreview.ParseInlines(@"\#urgent but #later");

        Assert.Equal([new MdText("#urgent but "), new MdTag("later")], inlines);
    }

    [Fact]
    public void Escaped_emphasis_markers_are_text()
    {
        var inlines = MarkdownPreview.ParseInlines(@"\*not em\* and \*\*not bold\*\*");

        Assert.Equal([new MdText("*not em* and **not bold**")], inlines);
    }

    [Fact]
    public void An_escaped_backslash_is_one_backslash()
    {
        Assert.Equal([new MdText(@"a \ b")], MarkdownPreview.ParseInlines(@"a \\ b"));
    }

    [Theory]
    [InlineData(@"C:\Users\someone\notes")]
    [InlineData(@"a\b and \n")]
    [InlineData(@"ends with \")]
    public void A_backslash_before_anything_but_punctuation_is_kept(string text)
    {
        Assert.Equal([new MdText(text)], MarkdownPreview.ParseInlines(text));
    }

    [Fact]
    public void A_backslash_inside_a_code_span_is_kept()
    {
        Assert.Equal([new MdText("run "), new MdCodeSpan(@"a\*b")], MarkdownPreview.ParseInlines(@"run `a\*b`"));
    }

    [Fact]
    public void An_escaped_heading_in_a_document_is_a_paragraph_not_a_heading()
    {
        var blocks = MarkdownPreview.ParseDocument("Intro\n\n\\## Not a section");

        Assert.All(blocks, block => Assert.IsType<MdParagraph>(block));
        Assert.Equal("## Not a section", Assert.IsType<MdText>(Assert.Single(((MdParagraph)blocks[1]).Content)).Text);
    }
}
