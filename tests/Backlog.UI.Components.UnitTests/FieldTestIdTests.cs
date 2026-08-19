namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A test id is a promise to whatever drives the DOM from outside, and `Bare`
/// used to break it: the attribute lived on the wrapper alone, and `Bare` is the
/// mode that has no wrapper. What these pin is that the promise survives either
/// way, and that honouring it in bare mode did not put the same id on two
/// elements in the wrapped one - a duplicate is what makes a Playwright
/// `getByTestId` throw rather than fail to find.
/// </summary>
public sealed class FieldTestIdTests
{
    [Fact]
    public void A_bare_textarea_carries_its_test_id_on_the_textarea()
    {
        using var context = new BunitContext();

        var field = context.Render<TextArea>(parameters => parameters
            .Add(f => f.Bare, true)
            .Add(f => f.TestId, "doc-editor-surface"));

        var found = Assert.Single(field.FindAll("[data-testid=\"doc-editor-surface\"]"));
        Assert.Equal("TEXTAREA", found.TagName);
    }

    [Fact]
    public void A_bare_text_field_carries_its_test_id_on_the_input()
    {
        using var context = new BunitContext();

        var field = context.Render<TextField>(parameters => parameters
            .Add(f => f.Bare, true)
            .Add(f => f.TestId, "task-rename"));

        var found = Assert.Single(field.FindAll("[data-testid=\"task-rename\"]"));
        Assert.Equal("INPUT", found.TagName);
    }

    [Fact]
    public void A_wrapped_textarea_keeps_its_test_id_on_the_wrapper_alone()
    {
        using var context = new BunitContext();

        var field = context.Render<TextArea>(parameters => parameters
            .Add(f => f.TestId, "input-notes"));

        // One match, not two: the wrapper keeps it, and the textarea must not
        // also claim it or every locator for this id becomes ambiguous.
        var found = Assert.Single(field.FindAll("[data-testid=\"input-notes\"]"));
        Assert.Equal("DIV", found.TagName);
        Assert.Contains("field", found.GetAttribute("class"));
    }

    [Fact]
    public void A_wrapped_text_field_keeps_its_test_id_on_the_wrapper_alone()
    {
        using var context = new BunitContext();

        var field = context.Render<TextField>(parameters => parameters
            .Add(f => f.TestId, "input-title"));

        var found = Assert.Single(field.FindAll("[data-testid=\"input-title\"]"));
        Assert.Equal("DIV", found.TagName);
        Assert.Contains("field", found.GetAttribute("class"));
    }

    [Fact]
    public void A_bare_host_that_spells_the_attribute_out_itself_still_wins()
    {
        using var context = new BunitContext();

        // Settings and the AI panel pass `data-testid` straight through rather
        // than via TestId. Splatting is last in the markup, so it stays the one
        // that lands even now that TestId writes the same attribute.
        var field = context.Render<TextArea>(parameters => parameters
            .Add(f => f.Bare, true)
            .AddUnmatched("data-testid", "ai-question-input"));

        var found = Assert.Single(field.FindAll("[data-testid=\"ai-question-input\"]"));
        Assert.Equal("TEXTAREA", found.TagName);
    }

    [Fact]
    public void The_markdown_editors_surface_is_addressable_by_its_test_id()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<MarkdownEditor>(parameters => parameters
            .Add(e => e.Value, "# Heading")
            .Add(e => e.TestId, "doc-editor"));

        // The surface is the textarea the toolbar acts on, and automation that
        // cannot find it cannot type into the editor at all.
        var surface = view.Find("[data-testid=\"doc-editor-surface\"]");
        Assert.Equal("TEXTAREA", surface.TagName);
        Assert.Equal("# Heading", surface.TextContent);
    }
}
