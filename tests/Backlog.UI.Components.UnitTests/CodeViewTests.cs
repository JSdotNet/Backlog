namespace Backlog.UI.Components.UnitTests;

public sealed class CodeViewTests
{
    [Fact]
    public void Every_source_line_is_a_line_in_the_block()
    {
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(c => c.Source, "var a = 1;\n\nvar b = 2;")
            .Add(c => c.Language, "csharp"));

        Assert.Equal(3, view.FindAll(".code-view__line").Count);
        Assert.Equal("var a = 1;\nvar b = 2;", string.Join('\n', view.FindAll(".code-view__line").Select(l => l.TextContent).Where(t => t.Length > 0)));
    }

    [Fact]
    public void The_language_decides_the_colouring_and_the_badge()
    {
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(c => c.Source, "const answer = 42;")
            .Add(c => c.Language, "ts"));

        Assert.Equal("TypeScript", view.Find("[data-testid='code-view-language']").TextContent);
        Assert.Equal("const", view.Find(".code-token--keyword").TextContent);
        Assert.Equal("42", view.Find(".code-token--number").TextContent);
    }

    [Fact]
    public void A_language_nothing_here_knows_still_renders_readably()
    {
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(c => c.Source, "(defn greet [name] name)")
            .Add(c => c.Language, "clojure"));

        Assert.Equal("clojure", view.Find("[data-testid='code-view-language']").TextContent);
        Assert.Empty(view.FindAll(".code-token"));
        Assert.Equal("(defn greet [name] name)", view.Find(".code-view__line").TextContent);
    }

    [Fact]
    public void Line_numbers_are_a_style_and_never_part_of_the_text()
    {
        // They are a CSS counter, so selecting the block by hand copies the
        // code and not the gutter.
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(c => c.Source, "one\ntwo")
            .Add(c => c.ShowLineNumbers, true));

        Assert.Contains("code-view--numbered", view.Find(".code-view").ClassList);
        Assert.Equal("onetwo", view.Find(".code-view__code").TextContent);
    }

    [Fact]
    public void A_long_line_wraps_unless_the_caller_says_otherwise()
    {
        // Wrapping is the default, so the wrapped block is the one with no
        // modifier on it: every surface here is narrower than the code written
        // for it, and a line off the right edge is a line nobody reads.
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters.Add(c => c.Source, "var a = 1;"));

        Assert.DoesNotContain("code-view--nowrap", view.Find(".code-view").ClassList);
    }

    [Fact]
    public void Turning_wrapping_off_keeps_the_line_one_line()
    {
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(c => c.Source, "var a = 1;")
            .Add(c => c.Wrap, false));

        Assert.Contains("code-view--nowrap", view.Find(".code-view").ClassList);
    }

    [Fact]
    public void Bare_gives_up_the_frame_so_a_host_can_supply_its_own()
    {
        // The header, the copy button, the status line and the block's own
        // scroll region all belong to the host in this shape — FileView already
        // has a header that names the file and a body that scrolls it.
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(c => c.Source, "var a = 1;")
            .Add(c => c.Language, "csharp")
            .Add(c => c.Title, "Program.cs")
            .Add(c => c.Bare, true));

        Assert.Empty(view.FindAll("figure"));
        Assert.Empty(view.FindAll(".code-view__header"));
        Assert.Empty(view.FindAll("[data-testid='code-view-copy']"));
        Assert.Empty(view.FindAll("[tabindex]"));

        // What it does still contribute is the coloured code.
        Assert.Contains("code-view--bare", view.Find(".code-view").ClassList);
        Assert.Equal("var", view.Find(".code-token--keyword").TextContent);
    }

    [Fact]
    public void Bare_still_takes_the_line_numbers_and_the_wrapping()
    {
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(c => c.Source, "one\ntwo")
            .Add(c => c.Bare, true)
            .Add(c => c.ShowLineNumbers, true)
            .Add(c => c.Wrap, false));

        var root = view.Find(".code-view");

        Assert.Contains("code-view--numbered", root.ClassList);
        Assert.Contains("code-view--nowrap", root.ClassList);
        Assert.Equal(2, view.FindAll(".code-view__line").Count);
    }

    [Fact]
    public void A_block_with_nothing_to_say_in_its_header_does_not_have_one()
    {
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(c => c.Source, "plain text")
            .Add(c => c.AllowCopy, false));

        Assert.Empty(view.FindAll(".code-view__header"));
    }

    [Fact]
    public void The_block_is_reachable_and_named_for_a_keyboard_user()
    {
        // It scrolls, so it has to be focusable, so it has to have a name.
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(c => c.Source, "var a = 1;")
            .Add(c => c.Language, "csharp")
            .Add(c => c.Title, "Program.cs"));

        var body = view.Find("pre.code-view__body");

        Assert.Equal("0", body.GetAttribute("tabindex"));
        Assert.Equal("Program.cs, C# code", body.GetAttribute("aria-label"));
    }

    [Fact]
    public void Copying_hands_the_clipboard_the_source_exactly_as_it_was_given()
    {
        using var context = new BunitContext();
        context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        var view = Render(context, parameters => parameters
            .Add(c => c.Source, "  var indented = 1;\n")
            .Add(c => c.Language, "csharp"));

        view.Find("[data-testid='code-view-copy']").Click();

        var invocation = Assert.Single(context.JSInterop.Invocations["backlogClipboard.copy"]);

        Assert.Equal("  var indented = 1;\n", invocation.Arguments[0]);
        Assert.Equal("Copied", view.Find("[data-testid='code-view-status']").TextContent);
    }

    [Fact]
    public void A_clipboard_that_refuses_is_reported_rather_than_assumed()
    {
        // A WebView without clipboard permission returns false, and telling the
        // reader it worked loses whatever they thought they had copied.
        using var context = new BunitContext();
        context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(false);

        var view = Render(context, parameters => parameters.Add(c => c.Source, "var a = 1;"));

        view.Find("[data-testid='code-view-copy']").Click();

        Assert.Contains("Could not copy", view.Find("[data-testid='code-view-status']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Source_is_escaped_so_a_snippet_cannot_inject_markup()
    {
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(c => c.Source, "<script>alert(1)</script>")
            .Add(c => c.Language, "html"));

        Assert.Empty(view.FindAll("script"));
        Assert.Contains("alert(1)", view.Find(".code-view__body").TextContent, StringComparison.Ordinal);
    }

    private static IRenderedComponent<CodeView> Render(BunitContext context, Action<ComponentParameterCollectionBuilder<CodeView>> parameters)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        return context.Render(parameters);
    }
}
