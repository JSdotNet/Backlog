namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Colouring markdown as source. The rule that matters most is the one about
/// losing nothing: this draws behind a textarea, so every character has to come
/// back out or the colours slide off the words.
/// </summary>
public sealed class MarkdownHighlighterTests
{
    private static string Rebuild(string source) =>
        string.Join('\n', MarkdownHighlighter.Highlight(source)
            .Select(line => string.Concat(line.Tokens.Select(t => t.Text))));

    [Theory]
    [InlineData("# A heading")]
    [InlineData("Plain **bold** and *emphasis* and ~~struck~~ and `code`.")]
    [InlineData("- [ ] A task\n- A bullet\n1. A number")]
    [InlineData("> A quote\n\n---\n\n| a | b |\n| - | - |")]
    [InlineData("A [link](https://example.com) and an ![image](p.png) and a #tag")]
    [InlineData("```csharp\nvar x = 1;\n```")]
    [InlineData("")]
    [InlineData("\n\n\n")]
    [InlineData("  indented, and trailing spaces   ")]
    public void Every_character_survives_being_coloured(string source)
    {
        Assert.Equal(source.Replace("\r\n", "\n"), Rebuild(source));
    }

    [Fact]
    public void There_is_one_line_out_for_every_line_in_including_the_blank_ones()
    {
        // The layer sits behind the textarea. A line dropped here is every colour
        // below it landing one line high.
        var lines = MarkdownHighlighter.Highlight("one\n\nthree\n");

        Assert.Equal(4, lines.Count);
        Assert.Empty(lines[1].Tokens);
    }

    [Fact]
    public void A_headings_hashes_are_syntax_and_its_words_are_a_heading()
    {
        var tokens = Assert.Single(MarkdownHighlighter.Highlight("## A section")).Tokens;

        // Its own kind, not the general grey: the hashes are how you see how deep
        // a section is while scrolling past it.
        Assert.Equal(MarkdownSyntaxKind.HeadingMarker, tokens[0].Kind);
        Assert.Equal("## ", tokens[0].Text);
        Assert.Equal(MarkdownSyntaxKind.Heading, tokens[1].Kind);
        Assert.Equal("A section", tokens[1].Text);
    }

    [Theory]
    [InlineData("- A bullet", "- ")]
    [InlineData("1. A number", "1. ")]
    [InlineData("> A quote", "> ")]
    [InlineData("- [ ] A task", "- [ ] ")]
    [InlineData("- [x] A done task", "- [x] ")]
    public void A_line_marker_is_syntax_and_the_rest_is_not(string source, string marker)
    {
        var tokens = Assert.Single(MarkdownHighlighter.Highlight(source)).Tokens;

        Assert.Equal(MarkdownSyntaxKind.Marker, tokens[0].Kind);
        Assert.Equal(marker, tokens[0].Text);
    }

    [Fact]
    public void A_task_is_read_as_a_task_and_not_as_a_bullet_with_brackets_in_it()
    {
        // Matching the bullet first would leave the checkbox coloured as content.
        var tokens = Assert.Single(MarkdownHighlighter.Highlight("- [ ] Write it down")).Tokens;

        Assert.Equal("- [ ] ", tokens[0].Text);
        Assert.Equal("Write it down", tokens[1].Text);
    }

    [Theory]
    [InlineData("**bold**", MarkdownSyntaxKind.Strong)]
    [InlineData("*emphasis*", MarkdownSyntaxKind.Emphasis)]
    [InlineData("~~struck~~", MarkdownSyntaxKind.Strike)]
    [InlineData("`code`", MarkdownSyntaxKind.Code)]
    [InlineData("#tag", MarkdownSyntaxKind.Tag)]
    public void An_inline_form_keeps_its_markers_inside_its_own_token(string source, MarkdownSyntaxKind expected)
    {
        var token = Assert.Single(Assert.Single(MarkdownHighlighter.Highlight(source)).Tokens);

        Assert.Equal(expected, token.Kind);
        Assert.Equal(source, token.Text);
    }

    [Fact]
    public void A_links_words_and_its_url_are_coloured_apart()
    {
        // One is prose and one is a machine string, and they are read apart.
        var tokens = Assert.Single(MarkdownHighlighter.Highlight("[the docs](https://example.com)")).Tokens;

        Assert.Equal(MarkdownSyntaxKind.LinkText, tokens[0].Kind);
        Assert.Equal("[the docs]", tokens[0].Text);
        Assert.Equal(MarkdownSyntaxKind.Url, tokens[1].Kind);
        Assert.Equal("(https://example.com)", tokens[1].Text);
    }

    [Fact]
    public void Nothing_inside_a_fence_is_markdown()
    {
        var lines = MarkdownHighlighter.Highlight("```\n# not a heading\n- [ ] not a task\n```");

        Assert.Equal(MarkdownSyntaxKind.Marker, Assert.Single(lines[0].Tokens).Kind);
        Assert.Equal(MarkdownSyntaxKind.Code, Assert.Single(lines[1].Tokens).Kind);
        Assert.Equal(MarkdownSyntaxKind.Code, Assert.Single(lines[2].Tokens).Kind);
        Assert.Equal(MarkdownSyntaxKind.Marker, Assert.Single(lines[3].Tokens).Kind);
    }

    [Fact]
    public void A_half_typed_marker_is_left_alone_rather_than_treated_as_a_failure()
    {
        // Someone is in the middle of writing it. Colouring it plain until it
        // closes is the answer; there is nothing to report.
        var tokens = Assert.Single(MarkdownHighlighter.Highlight("**bold that never closes")).Tokens;

        Assert.Equal(MarkdownSyntaxKind.Plain, Assert.Single(tokens).Kind);
    }

    [Fact]
    public void A_tables_pipes_are_syntax_and_its_cells_are_not()
    {
        var tokens = Assert.Single(MarkdownHighlighter.Highlight("| Name | Status |")).Tokens;

        Assert.Equal(MarkdownSyntaxKind.Marker, tokens[0].Kind);
        Assert.Equal("|", tokens[0].Text);
        Assert.Equal(" Name ", tokens[1].Text);
        Assert.Equal(3, tokens.Count(t => t.Kind is MarkdownSyntaxKind.Marker));
    }

    [Fact]
    public void The_class_comes_from_the_token_so_a_legend_cannot_drift_from_it()
    {
        Assert.Equal("md-syntax--strong", new MarkdownSyntaxToken(MarkdownSyntaxKind.Strong, "**a**").CssClass);
        Assert.Equal("md-syntax--plain", new MarkdownSyntaxToken(MarkdownSyntaxKind.Plain, "a").CssClass);
    }

    [Fact]
    public void The_editor_draws_the_colours_behind_text_it_leaves_addressable()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<MarkdownEditor>(p => p
            .Add(e => e.Value, "# Heading\n\n**bold**")
            .Add(e => e.TestId, "editor"));

        // The layer is the same text twice, so only one of them is real to a
        // screen reader.
        var layer = view.Find(".markdown-editor__highlight");
        Assert.Equal("true", layer.GetAttribute("aria-hidden"));

        Assert.NotEmpty(view.FindAll(".markdown-editor__highlight .md-syntax--heading"));
        Assert.NotEmpty(view.FindAll(".markdown-editor__highlight .md-syntax--strong"));

        // And the textarea still holds the text, unchanged.
        Assert.Equal("# Heading\n\n**bold**", view.Find("textarea").TextContent);
    }
}
