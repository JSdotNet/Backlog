namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The document is a read view with a way into an editor, so what is worth
/// pinning down is the seam between the two modes: which affordances each one
/// offers, and what leaving the text means once you are in the editor.
/// <para>
/// Blur is the one that has to be held still. It asks the host to save, and it is
/// not the author saying they are finished — the formatting toolbar sits outside
/// the textarea, so every reach for bold blurs it. An editor that closed on blur
/// would take the button away as it was being pressed.
/// </para>
/// </summary>
public sealed class MarkdownDocumentTests
{
    private const string Body = """
        # Before tagging

        - [x] Tests green on main
        - [ ] Release notes written
        """;

    [Fact]
    public void Leaving_the_text_asks_the_host_to_save()
    {
        using var context = new BunitContext();
        var flushes = 0;

        var view = Render(context, parameters => parameters
            .Add(d => d.Editing, true)
            .Add(d => d.OnBlur, () => flushes++));

        view.Find("textarea").Blur();

        Assert.Equal(1, flushes);
    }

    [Fact]
    public void Leaving_the_text_does_not_leave_the_editor()
    {
        // Focus moving to a formatting button is a blur. Closing here would mean
        // the editor vanished on the way to the click that was about to format
        // the selection.
        using var context = new BunitContext();
        var modes = new List<bool>();

        var view = Render(context, parameters => parameters
            .Add(d => d.Editing, true)
            .Add(d => d.OnBlur, () => { })
            .Add(d => d.EditingChanged, (bool editing) => modes.Add(editing)));

        view.Find("textarea").Blur();

        Assert.Single(view.FindAll("textarea"));
        Assert.Empty(view.FindAll("[data-testid='doc-read']"));
        Assert.Contains("markdown-document--editing", view.Find("[data-testid='doc']").ClassList);
        Assert.Empty(modes);
    }

    [Fact]
    public void A_host_with_nothing_to_save_is_not_listened_to_at_all()
    {
        // OnBlur is additive, and every caller that predates it passes nothing.
        // Blazor wires no handler for a callback nobody assigned, so those hosts
        // keep a textarea with no blur traffic on it rather than paying for an
        // event they never asked for.
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters.Add(d => d.Editing, true));

        Assert.DoesNotContain("onblur", view.Find("textarea").OuterHtml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Without_permission_to_edit_there_is_no_way_in()
    {
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters.Add(d => d.CanEdit, false));

        Assert.Empty(view.FindAll("[data-testid='doc-edit']"));
        Assert.NotNull(view.Find("[data-testid='doc-read']"));
    }

    [Fact]
    public void Edit_opens_the_editor_and_done_hands_back_the_rendering()
    {
        using var context = new BunitContext();

        var view = Render(context);

        // Read is the resting state, so the rendering is what is there first.
        Assert.NotNull(view.Find("[data-testid='doc-read']"));
        Assert.Empty(view.FindAll("textarea"));

        view.Find("[data-testid='doc-edit']").Click();

        Assert.Single(view.FindAll("textarea"));
        Assert.Empty(view.FindAll("[data-testid='doc-read']"));

        view.Find("[data-testid='doc-done']").Click();

        Assert.NotNull(view.Find("[data-testid='doc-read']"));
        Assert.Empty(view.FindAll("textarea"));
    }

    [Fact]
    public void The_edited_text_is_reported_as_it_is_typed()
    {
        // One text either way: what comes back out is the same string the editor
        // was handed, so a host can store it without reconciling two copies.
        using var context = new BunitContext();
        var reported = new List<string>();

        var view = Render(context, parameters => parameters
            .Add(d => d.Editing, true)
            .Add(d => d.ValueChanged, (string value) => reported.Add(value)));

        view.Find("textarea").Input("# After tagging");

        Assert.Equal(["# After tagging"], reported);
    }

    [Fact]
    public void The_editor_is_named_after_the_document_it_is_editing()
    {
        using var context = new BunitContext();

        var view = Render(context, p => p.Add(d => d.Editing, true));

        Assert.Equal("release-checklist.md source", view.Find("textarea").GetAttribute("aria-label"));
    }

    [Fact]
    public void An_editor_under_a_host_that_names_the_file_is_still_named_something()
    {
        using var context = new BunitContext();

        // Rendered without the shared helper because this is the one test that
        // needs no title at all, and the helper always names the document.
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        // A host with its own header hands in no title. Interpolating that
        // absence used to label the textarea " source".
        var view = context.Render<MarkdownDocument>(p => p
            .Add(d => d.Value, Body)
            .Add(d => d.TestId, "doc")
            .Add(d => d.Editing, true));

        Assert.Equal("Markdown source", view.Find("textarea").GetAttribute("aria-label"));
    }

    /// <summary>
    /// Loose interop throughout: the editor asks the browser to keep its
    /// highlight layer in step with the textarea, and none of these tests are
    /// about that.
    /// </summary>
    private static IRenderedComponent<MarkdownDocument> Render(
        BunitContext context,
        Action<ComponentParameterCollectionBuilder<MarkdownDocument>>? extra = null)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        return context.Render<MarkdownDocument>(parameters =>
        {
            parameters
                .Add(d => d.Value, Body)
                .Add(d => d.Title, "release-checklist.md")
                .Add(d => d.TestId, "doc");

            extra?.Invoke(parameters);
        });
    }
}
