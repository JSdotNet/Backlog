namespace Backlog.UI.Components.UnitTests;

public sealed class MarkdownViewTests
{
    [Fact]
    public void A_heading_renders_at_its_own_level()
    {
        using var context = new BunitContext();

        var view = Render(context, "# Prepare review");

        Assert.Contains("md-heading--1", view.Find(".md-heading").ClassList);
        Assert.Equal("Prepare review", view.Find(".md-heading").TextContent);
    }

    [Fact]
    public void Checkboxes_belong_to_task_lines_and_only_to_those()
    {
        using var context = new BunitContext();

        var view = Render(context, "- [ ] Book the room\n- Just a bullet\n");

        Assert.Single(view.FindAll("[data-testid='entry-checkbox']"));

        // The task modifier is per item, not per list: the plain bullet in the
        // same list keeps its marker instead of reading as stray prose.
        var items = view.FindAll("li");
        Assert.Contains("md-item--task", items[0].ClassList);
        Assert.DoesNotContain("md-item--task", items[1].ClassList);
    }

    [Fact]
    public void Toggling_a_task_reports_the_index_of_the_task_and_not_of_the_line()
    {
        // The caller rewrites the raw markdown from this index, so counting
        // plain bullets in would rewrite the wrong line.
        using var context = new BunitContext();
        var toggled = new List<int>();

        var view = Render(context, "- [ ] Book the room\n- A plain bullet\n- [x] Write the summary\n", index => toggled.Add(index));

        view.FindAll("[data-testid='entry-checkbox']")[1].Click();

        Assert.Equal([1], toggled);
    }

    [Fact]
    public void A_checkbox_is_named_after_the_item_it_ticks()
    {
        // Every checkbox used to announce itself as "Mark done", so a screen
        // reader on a four-item checklist heard four identical buttons.
        using var context = new BunitContext();

        var view = Render(context, "- [x] Book the room\n- [ ] Send the agenda\n");
        var checkboxes = view.FindAll("[data-testid='entry-checkbox']");

        Assert.Equal("checkbox", checkboxes[0].GetAttribute("role"));
        Assert.Equal("true", checkboxes[0].GetAttribute("aria-checked"));
        Assert.Equal("Book the room", checkboxes[0].GetAttribute("aria-label"));

        Assert.Equal("false", checkboxes[1].GetAttribute("aria-checked"));
        Assert.Equal("Send the agenda", checkboxes[1].GetAttribute("aria-label"));
    }

    [Fact]
    public void Without_a_toggle_handler_a_checkbox_is_state_and_not_a_control()
    {
        // A button that does nothing still takes focus, so a read-only body used
        // to hand a keyboard user one dead tab stop per checklist item.
        using var context = new BunitContext();

        var view = context.Render<MarkdownView>(parameters => parameters
            .Add(v => v.Blocks, MarkdownPreview.Parse("- [x] Book the room\n")));

        var checkbox = view.Find("[data-testid='entry-checkbox']");

        Assert.Empty(view.FindAll("button"));
        Assert.Equal("img", checkbox.GetAttribute("role"));
        Assert.Equal("Done", checkbox.GetAttribute("aria-label"));
    }

    [Fact]
    public void A_heading_carries_its_level_to_a_screen_reader()
    {
        // The level used to live in a CSS class alone, so heading navigation —
        // the way you move through a long body — had nothing to move between.
        using var context = new BunitContext();

        var view = Render(context, "# Prepare review\n\n#### A detail\n");
        var headings = view.FindAll(".md-heading");

        Assert.Equal("heading", headings[0].GetAttribute("role"));
        Assert.Equal("1", headings[0].GetAttribute("aria-level"));
        Assert.Equal("4", headings[1].GetAttribute("aria-level"));
    }

    [Theory]
    [InlineData("javascript:doEvil")]
    [InlineData("JaVaScRiPt:doEvil")]
    [InlineData("data:text/html,<b>hi</b>")]
    public void A_link_we_will_not_navigate_to_renders_as_text(string url)
    {
        // Escaping keeps typed markup out of the document; an href is the other
        // door, and this one opens in the app's own origin under WebView2.
        using var context = new BunitContext();

        var view = Render(context, $"A [click me]({url}) line");

        Assert.Empty(view.FindAll("a"));
        Assert.Equal("click me", view.Find(".md-link--inert").TextContent);
    }

    [Theory]
    [InlineData("https://example.com/x")]
    [InlineData("http://example.com/x")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("notes/entry.md")]
    [InlineData("notes/10:30.md")]
    public void A_link_we_will_navigate_to_stays_a_link(string url)
    {
        using var context = new BunitContext();

        var view = Render(context, $"A [click me]({url}) line");

        Assert.Equal(url, view.Find("a.md-link").GetAttribute("href"));
    }

    [Fact]
    public void Quotes_code_and_dividers_each_render_as_themselves()
    {
        using var context = new BunitContext();

        var view = Render(context, "> Worth remembering\n\n```\nplain code\n```\n\n---\n");

        Assert.Equal("Worth remembering", view.Find("blockquote.md-quote").TextContent);
        Assert.Equal("plain code", view.Find("pre.md-code code").TextContent);
        Assert.Single(view.FindAll("hr.md-divider"));
    }

    [Fact]
    public void A_mermaid_block_is_routed_to_the_diagram_view()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = Render(context, "```mermaid\ngraph TD; a-->b;\n```\n");

        Assert.NotNull(view.Find("[data-testid='diagram-view']"));
        Assert.Empty(view.FindAll("pre.md-code"));
        Assert.Single(context.JSInterop.Invocations["backlogDiagrams.render"]);
    }

    [Fact]
    public void A_code_block_nothing_can_draw_stays_a_code_block()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = Render(context, "```csharp\nvar x = 1;\n```\n");

        Assert.Empty(view.FindAll("[data-testid='diagram-view']"));
        Assert.Equal("var x = 1;", view.Find("pre.md-code code").TextContent);
    }

    [Fact]
    public void Text_is_escaped_so_an_entry_cannot_inject_markup()
    {
        using var context = new BunitContext();

        var view = Render(context, "A <script>alert(1)</script> line");

        Assert.Empty(view.FindAll("script"));
        Assert.Contains("alert(1)", view.Find(".md-p").TextContent, StringComparison.Ordinal);
    }

    private static IRenderedComponent<MarkdownView> Render(BunitContext context, string body, Action<int>? onToggled = null) =>
        context.Render<MarkdownView>(parameters => parameters
            .Add(v => v.Blocks, MarkdownPreview.Parse(body))
            .Add(v => v.OnTaskItemToggled, (int index) => onToggled?.Invoke(index)));
}
