namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A bare <c>http</c> or <c>https</c> URL written in a body reads as the same link
/// a <c>[text](url)</c> does — the existing <c>md-link</c>, with nothing new about
/// how it opens — and code stays code, and a link the author wrote keeps its words.
/// Display only: what is stored is the text the author typed.
/// </summary>
public sealed class MarkdownBareUrlTests
{
    private const string Url = "https://claude.ai/artifact/UkBhdNFEcb95CNmbrJbzWr";

    [Fact]
    public void A_bare_url_in_a_paragraph_renders_as_a_link()
    {
        using var context = new BunitContext();

        var view = Render(context, $"Concurrency issue: {Url}");

        var link = view.Find("a.md-link");
        Assert.Equal(Url, link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
        Assert.Equal(Url, link.TextContent);
        Assert.Equal($"Concurrency issue: {Url}", view.Find("p").TextContent);
    }

    [Fact]
    public void A_bare_url_draws_the_same_anchor_a_written_link_to_it_draws()
    {
        using var context = new BunitContext();

        var bare = Render(context, $"see {Url}");
        var written = Render(context, $"see [{Url}]({Url})");

        Assert.Equal(written.Markup, bare.Markup);
    }

    [Fact]
    public void The_full_stop_after_a_url_stays_in_the_sentence()
    {
        using var context = new BunitContext();

        var view = Render(context, "see https://example.com/a.");

        Assert.Equal("https://example.com/a", view.Find("a.md-link").GetAttribute("href"));
        Assert.Equal("see https://example.com/a.", view.Find("p").TextContent);
    }

    [Fact]
    public void A_url_in_brackets_leaves_the_closing_bracket_outside_the_link()
    {
        using var context = new BunitContext();

        var view = Render(context, "(https://example.com/a)");

        Assert.Equal("https://example.com/a", view.Find("a.md-link").GetAttribute("href"));
        Assert.Equal("(https://example.com/a)", view.Find("p").TextContent);
    }

    [Fact]
    public void A_url_in_a_code_span_stays_code()
    {
        using var context = new BunitContext();

        var view = Render(context, "run `curl https://example.com/a` first");

        Assert.Empty(view.FindAll("a"));
        Assert.Equal("curl https://example.com/a", view.Find("code.md-inline-code").TextContent);
    }

    [Fact]
    public void A_url_in_a_fenced_block_stays_code()
    {
        using var context = new BunitContext();

        var view = Render(context, "```\nhttps://example.com/a\n```\n");

        Assert.Empty(view.FindAll("a"));
    }

    [Fact]
    public void A_written_link_keeps_its_own_words()
    {
        using var context = new BunitContext();

        var view = Render(context, "A [x](https://a.b) line");

        var link = Assert.Single(view.FindAll("a"));
        Assert.Equal("x", link.TextContent);
        Assert.Equal("https://a.b", link.GetAttribute("href"));
    }

    [Fact]
    public void A_written_link_whose_words_are_a_url_is_still_one_link()
    {
        using var context = new BunitContext();

        var view = Render(context, "A [https://shown.example](https://target.example) line");

        var link = Assert.Single(view.FindAll("a"));
        Assert.Equal("https://target.example", link.GetAttribute("href"));
        Assert.Equal("https://shown.example", link.TextContent);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<b>hi</b>")]
    [InlineData("ftp://example.com/file.txt")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("www.example.com")]
    public void Only_http_and_https_become_links(string text)
    {
        using var context = new BunitContext();

        var view = Render(context, $"try {text} now");

        Assert.Empty(view.FindAll("a"));
        Assert.Equal($"try {text} now", view.Find("p").TextContent);
    }

    [Fact]
    public void A_fragment_in_a_url_is_not_a_tag()
    {
        using var context = new BunitContext();

        var view = Render(context, "Fix #deploy https://x.com/a#frag now");

        Assert.Equal(["#deploy"], view.FindAll(".tag-chip").Select(chip => chip.TextContent));
        Assert.Equal("https://x.com/a#frag", view.Find("a.md-link").GetAttribute("href"));
    }

    [Fact]
    public void A_bare_url_links_in_a_list_item_and_a_table_cell_too()
    {
        using var context = new BunitContext();

        var view = Render(context, "- see https://one.example\n\n| Where |\n| --- |\n| https://two.example |\n");

        Assert.Equal(
            ["https://one.example", "https://two.example"],
            view.FindAll("a.md-link").Select(link => link.GetAttribute("href")));
    }

    [Fact]
    public void An_angle_bracket_autolink_renders_its_url_without_the_brackets()
    {
        // The product writes this shape into Brain notes itself, and the devbook's
        // own chapters use it: CommonMark's autolink, not a bare URL.
        using var context = new BunitContext();

        var view = Render(context, "Source: <https://example.com/es>");

        var link = Assert.Single(view.FindAll("a"));
        Assert.Equal("https://example.com/es", link.GetAttribute("href"));
        Assert.Equal("https://example.com/es", link.TextContent);
        Assert.Contains("md-link", link.ClassList);
        Assert.Equal("Source: https://example.com/es", view.Find("p").TextContent);
    }

    [Fact]
    public void An_angle_bracket_autolink_in_brackets_keeps_the_brackets_around_it()
    {
        using var context = new BunitContext();

        var view = Render(context, "(<https://example.com/a>)");

        Assert.Equal("https://example.com/a", Assert.Single(view.FindAll("a")).GetAttribute("href"));
        Assert.Equal("(https://example.com/a)", view.Find("p").TextContent);
    }

    [Theory]
    [InlineData("<ftp://example.com/file.txt>")]
    [InlineData("<javascript:alert(1)>")]
    [InlineData("<mailto:someone@example.com>")]
    [InlineData("<not a url>")]
    [InlineData("<https:///path>")]
    [InlineData("<https://.>")]
    public void An_angle_bracket_that_is_not_a_web_url_stays_literal_text(string text)
    {
        using var context = new BunitContext();

        var view = Render(context, $"try {text} now");

        Assert.Empty(view.FindAll("a"));
        Assert.Equal($"try {text} now", view.Find("p").TextContent);
    }

    [Fact]
    public void A_code_span_that_starts_inside_a_url_binds_first()
    {
        // GFM reads code spans before it looks for an extended autolink, so the
        // address ends where the backtick opens.
        using var context = new BunitContext();

        var view = Render(context, "see https://example.com/`path` here");

        Assert.Equal("path", view.Find("code.md-inline-code").TextContent);
        Assert.Equal("https://example.com/", Assert.Single(view.FindAll("a")).GetAttribute("href"));
        Assert.Equal("see https://example.com/path here", view.Find("p").TextContent);
    }

    [Fact]
    public void A_written_link_that_starts_inside_a_url_binds_first()
    {
        using var context = new BunitContext();

        var view = Render(context, "see https://example.com/[x](https://y.example) here");

        Assert.Equal(
            [("https://example.com/", "https://example.com/"), ("https://y.example", "x")],
            view.FindAll("a.md-link").Select(link => (link.GetAttribute("href"), link.TextContent)));
    }

    [Fact]
    public void A_code_span_after_emphasis_inside_a_url_still_binds_first()
    {
        // The emphasis does not end the URL — a URL keeps its `*` — so the code
        // span further in is still the first thing that does.
        using var context = new BunitContext();

        var view = Render(context, "see https://a.com/*x*`y` here");

        Assert.Equal("y", view.Find("code.md-inline-code").TextContent);
        Assert.Equal("https://a.com/*x", Assert.Single(view.FindAll("a")).GetAttribute("href"));
        Assert.Equal("see https://a.com/*x*y here", view.Find("p").TextContent);
    }

    [Fact]
    public void A_footnote_mark_with_a_note_behind_it_ends_the_url()
    {
        using var context = new BunitContext();

        var view = Render(context, "See https://a.com/page[^1] for it.\n\n[^1]: The note.\n");

        Assert.Equal("https://a.com/page", view.Find("p a.md-link").GetAttribute("href"));
        Assert.NotNull(view.Find("sup.md-fnref"));
    }

    [Fact]
    public void A_footnote_mark_with_no_note_behind_it_stays_in_the_url()
    {
        // Without a definition the mark is only the characters typed, and GFM
        // reads text characters into an extended autolink like any other.
        using var context = new BunitContext();

        var view = Render(context, "See https://a.com/page[^1] for it.");

        Assert.Equal("https://a.com/page[^1]", Assert.Single(view.FindAll("a")).GetAttribute("href"));
        Assert.Empty(view.FindAll("sup.md-fnref"));
        Assert.Equal("See https://a.com/page[^1] for it.", view.Find("p").TextContent);
    }

    [Fact]
    public void A_url_after_a_construct_that_cut_one_short_still_links()
    {
        using var context = new BunitContext();

        var view = Render(context, "see https://`x` and https://two.example");

        Assert.Equal("x", view.Find("code.md-inline-code").TextContent);
        Assert.Equal("https://two.example", Assert.Single(view.FindAll("a")).GetAttribute("href"));
    }

    [Fact]
    public void The_parse_keeps_the_url_as_its_own_words()
    {
        var parts = MarkdownPreview.ParseInlines("see https://example.com/a.");

        Assert.Collection(parts,
            part => Assert.Equal(new MdText("see "), part),
            part => Assert.Equal(new MdLink("https://example.com/a", "https://example.com/a"), part),
            part => Assert.Equal(new MdText("."), part));
    }

    [Fact]
    public void A_line_without_a_url_parses_as_it_always_has()
    {
        var parts = MarkdownPreview.ParseInlines("a **bold** and `code` and #tag and [link](https://example.com)");

        Assert.Equal(
            [new MdText("a "), new MdStrong("bold"), new MdText(" and "), new MdCodeSpan("code"), new MdText(" and "),
             new MdTag("tag"), new MdText(" and "), new MdLink("link", "https://example.com")],
            parts);
    }

    private static IRenderedComponent<MarkdownView> Render(BunitContext context, string body) =>
        context.Render<MarkdownView>(parameters => parameters.Add(v => v.Blocks, MarkdownPreview.Parse(body)));
}
