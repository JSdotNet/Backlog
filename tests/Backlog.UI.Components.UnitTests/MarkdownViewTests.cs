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
        Assert.Contains("md-list--tasks", view.Find("ul").ClassList);
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
    public void A_done_task_reads_as_pressed_and_offers_the_opposite_action()
    {
        using var context = new BunitContext();

        var view = Render(context, "- [x] Book the room\n");
        var checkbox = view.Find("[data-testid='entry-checkbox']");

        Assert.Equal("true", checkbox.GetAttribute("aria-pressed"));
        Assert.Equal("Mark not done", checkbox.GetAttribute("aria-label"));
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
