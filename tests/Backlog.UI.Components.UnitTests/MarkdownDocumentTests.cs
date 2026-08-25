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

    /// <summary>An instruction file, which is the one kind of file in this
    /// product that opens with a frontmatter block.</summary>
    private const string Frontmattered = """
        ---
        applyTo: "src/App/**"
        description: What the screens may do.
        ---

        # UI components

        A paragraph.
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

    [Fact]
    public void A_host_already_showing_the_frontmatter_can_keep_it_out_of_the_read_view()
    {
        // The instructions panel: a file view's strip above this one draws the
        // description and the globs as facts, so drawing the same four lines here
        // again would say it twice — the second time as a divider and a paragraph
        // of run-together `key: value`.
        using var context = new BunitContext();

        var view = RenderInstructionFile(context, parameters => parameters
            .Add(d => d.HideFrontmatter, true));

        var read = view.Find("[data-testid='doc-read']");

        Assert.DoesNotContain("applyTo:", read.TextContent, StringComparison.Ordinal);
        Assert.Empty(read.QuerySelectorAll("hr.md-divider"));
        Assert.Equal("UI components", read.QuerySelector(".md-heading")!.TextContent);
    }

    [Fact]
    public void What_the_editor_gets_is_the_whole_file_either_way()
    {
        // Read mode only, and this is why it matters: the buffer is written back
        // over the source, so a textarea four lines short would save the file
        // without them.
        using var context = new BunitContext();

        var view = RenderInstructionFile(context, parameters => parameters
            .Add(d => d.HideFrontmatter, true)
            .Add(d => d.Editing, true));

        // Against the DOM's own line endings rather than the file's: what is
        // being pinned is that no line went missing on the way to the textarea.
        Assert.Equal(Frontmattered.ReplaceLineEndings("\n"), view.Find("textarea").TextContent);
    }

    [Fact]
    public void The_read_view_keeps_the_frontmatter_until_a_host_says_otherwise()
    {
        using var context = new BunitContext();

        var view = RenderInstructionFile(context, _ => { });

        Assert.Contains("applyTo:", view.Find("[data-testid='doc-read']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void An_editor_told_to_fill_wears_the_modifier_that_lets_it()
    {
        // Rows is the only height a textarea has of its own, and inside a pane
        // that already knows how tall it is that number is a guess which leaves
        // dead space under the last row. The modifier is how the editor gives the
        // height back to the layout.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<MarkdownEditor>(parameters => parameters
            .Add(e => e.Value, Body)
            .Add(e => e.Fill, true)
            .Add(e => e.CssClass, "chapter-editor"));

        var editor = view.Find(".markdown-editor");

        Assert.Contains("markdown-editor--fill", editor.ClassList);

        // Beside whatever the host was already putting there, not instead of it.
        Assert.Contains("chapter-editor", editor.ClassList);
    }

    [Fact]
    public void An_editor_nobody_sized_renders_exactly_as_it_always_did()
    {
        // Every editor in this product predates the parameter, and Rows is still
        // what sizes them. Off is the default for that reason.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<MarkdownEditor>(parameters => parameters
            .Add(e => e.Value, Body));

        Assert.DoesNotContain("markdown-editor--fill", view.Find(".markdown-editor").ClassList);
    }

    [Fact]
    public void A_document_that_fills_hands_the_room_straight_down_to_the_editor()
    {
        // The document is a pass-through here and nothing more: it holds no
        // height of its own, so the only useful thing it can do with the answer
        // is give it to the surface that has to stretch.
        using var context = new BunitContext();

        var filled = Render(context, parameters => parameters
            .Add(d => d.Editing, true)
            .Add(d => d.Fill, true));

        Assert.Contains("markdown-editor--fill", filled.Find(".markdown-editor").ClassList);

        var sized = Render(context, parameters => parameters.Add(d => d.Editing, true));

        Assert.DoesNotContain("markdown-editor--fill", sized.Find(".markdown-editor").ClassList);
    }

    [Fact]
    public void An_editor_inside_somebody_elses_frame_gives_up_its_own()
    {
        // Two borders a pixel apart read as two things with something between
        // them, and inside a file view's body there is nothing between them. The
        // focus ring is what the border was also carrying, so it comes back as an
        // outline in the stylesheet rather than going with it.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<MarkdownEditor>(parameters => parameters
            .Add(e => e.Value, Body)
            .Add(e => e.Bare, true));

        Assert.Contains("markdown-editor--bare", view.Find(".markdown-editor").ClassList);
    }

    [Fact]
    public void An_editor_standing_on_its_own_keeps_its_frame()
    {
        // An editor on a page rather than inside something is its own object and
        // has to look like one, which is why off is the default.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<MarkdownEditor>(parameters => parameters
            .Add(e => e.Value, Body));

        Assert.DoesNotContain("markdown-editor--bare", view.Find(".markdown-editor").ClassList);
    }

    [Fact]
    public void A_document_told_the_frame_is_the_hosts_hands_that_down_too()
    {
        // The same pass-through the height takes, for the same reason: the frame
        // in question is the editor's, and this component draws none of its own.
        using var context = new BunitContext();

        var bare = Render(context, parameters => parameters
            .Add(d => d.Editing, true)
            .Add(d => d.BareEditor, true));

        Assert.Contains("markdown-editor--bare", bare.Find(".markdown-editor").ClassList);

        var framed = Render(context, parameters => parameters.Add(d => d.Editing, true));

        Assert.DoesNotContain("markdown-editor--bare", framed.Find(".markdown-editor").ClassList);
    }

    [Fact]
    public void A_bar_with_nothing_in_it_is_not_drawn()
    {
        // No title, no copy, no way into editing: a host carrying all three in
        // its own header. Drawn anyway, the row was two rems of empty space
        // between that header and the first line of the file.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<MarkdownDocument>(parameters => parameters
            .Add(d => d.Value, Body)
            .Add(d => d.Editing, true)
            .Add(d => d.CanEdit, false)
            .Add(d => d.AllowCopy, false));

        Assert.Empty(view.FindAll(".markdown-document__bar"));
    }

    [Fact]
    public void A_bar_with_anything_in_it_still_is()
    {
        // One of the three is enough, and the title is the one a host hands over
        // when it has no header of its own.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var titled = context.Render<MarkdownDocument>(parameters => parameters
            .Add(d => d.Value, Body)
            .Add(d => d.Title, "notes.md"));

        Assert.Single(titled.FindAll(".markdown-document__bar"));

        var editable = context.Render<MarkdownDocument>(parameters => parameters
            .Add(d => d.Value, Body)
            .Add(d => d.CanEdit, true));

        Assert.Single(editable.FindAll(".markdown-document__bar"));
    }

    /// <summary>The same document, holding a file that opens with a frontmatter
    /// block. Its own renderer because the shared one below has already named the
    /// text it hands over.</summary>
    private static IRenderedComponent<MarkdownDocument> RenderInstructionFile(
        BunitContext context,
        Action<ComponentParameterCollectionBuilder<MarkdownDocument>> extra)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        return context.Render<MarkdownDocument>(parameters =>
        {
            parameters
                .Add(d => d.Value, Frontmattered)
                .Add(d => d.TestId, "doc");

            extra(parameters);
        });
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
