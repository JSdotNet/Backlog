namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The editor's bargain that the textarea is the truth, held at the one seam
/// where Blazor cannot see it. A host that stores the body through a parser —
/// the Tasks pane's Markdown tab, a step's notes — hands back a text with its
/// edges trimmed, and Blazor's diff, comparing render to render rather than
/// render to DOM, finds nothing to write: the textarea keeps the blank line the
/// reader just opened while the highlight layer and the auto-grow replica draw
/// the text without it. Every colour then sits a line away from its word.
/// </summary>
public sealed class MarkdownEditorTypedTextTests
{
    /// <summary>
    /// The regression: a blank line typed at the end of the body, handed back
    /// without it. The layer behind the text and the replica that sizes the
    /// box both have to keep drawing what the textarea still holds.
    /// </summary>
    [Fact]
    public void A_host_that_trims_the_edges_does_not_pull_the_layers_off_the_typed_text()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var stored = "Hello";
        var view = context.Render<MarkdownEditor>(parameters => parameters
            .Add(e => e.Value, stored)
            .Add(e => e.ValueChanged, value => stored = value.Trim('\n'))
            .Add(e => e.AutoGrow, true));

        view.Find("textarea").Input("Hello\n");
        Assert.Equal("Hello", stored);

        // What the Tasks pane does on its next render: re-derive the value from
        // the store, which no longer has the newline.
        view.Render(parameters => parameters.Add(e => e.Value, stored));

        var wrapper = view.Find("textarea").ParentElement!;
        Assert.Equal("Hello\n", wrapper.GetAttribute("data-replicated-value"));

        // One entry per line the textarea shows, a newline after each: "Hello",
        // then the blank one the reader opened.
        Assert.Equal("Hello\n\n", view.Find("pre.markdown-editor__highlight code").TextContent);
    }

    [Fact]
    public void Blank_lines_typed_at_the_top_are_kept_the_same_way()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var stored = "Hello";
        var view = context.Render<MarkdownEditor>(parameters => parameters
            .Add(e => e.Value, stored)
            .Add(e => e.ValueChanged, value => stored = value.Trim('\n'))
            .Add(e => e.AutoGrow, true));

        view.Find("textarea").Input("\n\nHello");
        view.Render(parameters => parameters.Add(e => e.Value, stored));

        Assert.Equal("\n\nHello", view.Find("textarea").ParentElement!.GetAttribute("data-replicated-value"));
        Assert.Equal("\n\nHello\n", view.Find("pre.markdown-editor__highlight code").TextContent);
    }

    /// <summary>
    /// The other half of the bargain: a host that changes the text — a reload
    /// from the store, another device's edit — is still obeyed. Only an echo
    /// with the edges trimmed is read as the typed text coming back.
    /// </summary>
    [Fact]
    public void A_host_that_replaces_the_text_still_wins()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<MarkdownEditor>(parameters => parameters
            .Add(e => e.Value, "Hello")
            .Add(e => e.AutoGrow, true));

        view.Find("textarea").Input("Hello\n");
        view.Render(parameters => parameters.Add(e => e.Value, "Something else"));

        Assert.Equal("Something else", view.Find("textarea").ParentElement!.GetAttribute("data-replicated-value"));
        Assert.Equal("Something else\n", view.Find("pre.markdown-editor__highlight code").TextContent);
    }

    /// <summary>
    /// And once the typed text has been superseded, a later trimmed echo of the
    /// old typed text is not mistaken for it — the guard is about the text the
    /// textarea holds now, not any text it ever held.
    /// </summary>
    [Fact]
    public void A_superseded_typed_text_no_longer_shadows_the_host()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<MarkdownEditor>(parameters => parameters
            .Add(e => e.Value, "Hello")
            .Add(e => e.AutoGrow, true));

        view.Find("textarea").Input("Hello\n");
        view.Render(parameters => parameters.Add(e => e.Value, "Something else"));
        view.Render(parameters => parameters.Add(e => e.Value, "Hello"));

        Assert.Equal("Hello", view.Find("textarea").ParentElement!.GetAttribute("data-replicated-value"));
    }
}
