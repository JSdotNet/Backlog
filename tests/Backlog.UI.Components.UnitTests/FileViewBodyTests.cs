namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// FileView's body when the host brings its own. The two built-in shapes are
/// covered in <see cref="FileViewTests"/>; these pin what changes the moment a
/// caller hands a body in — and, mostly, what deliberately does not.
/// </summary>
public sealed class FileViewBodyTests
{
    /// <summary>Stands in for whatever a host would really pass: something
    /// focusable, which is the reason a host passes anything at all.</summary>
    private static RenderFragment Editor() => builder =>
    {
        builder.OpenElement(0, "textarea");
        builder.AddAttribute(1, "data-testid", "supplied-editor");
        builder.AddAttribute(2, "aria-label", "Chapter source");
        builder.AddContent(3, "# Title\n");
        builder.CloseElement();
    };

    [Fact]
    public void A_body_the_host_supplies_is_what_the_scroll_region_shows()
    {
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "05-building-block-view.md")
            .Add(v => v.Body, "# Building block view\n\nA paragraph.\n")
            .Add(v => v.BodyContent, Editor()));

        // Inside the one scroll region and not beside it: the header still stays
        // put and the body still scrolls under it.
        Assert.NotNull(view.Find(".file-view__body [data-testid='supplied-editor']"));

        // In place of the built-in shape, not as well as it.
        Assert.Empty(view.FindAll(".file-view__body .md-heading"));
        Assert.Empty(view.FindAll(".file-view__body .md-p"));
        Assert.Empty(view.FindAll(".file-view__body .code-view"));
    }

    [Fact]
    public void A_supplied_body_never_asks_the_file_name_what_shape_to_take()
    {
        // A caller handing in a body has already decided what the body is, so the
        // name-driven choice has nothing left to decide — not even for a name the
        // highlighter has a grammar for.
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "Program.cs")
            .Add(v => v.Body, "var app = builder.Build();")
            .Add(v => v.BodyContent, Editor()));

        Assert.NotNull(view.Find(".file-view__body [data-testid='supplied-editor']"));
        Assert.Empty(view.FindAll(".file-view__body .code-view"));
        Assert.Empty(view.FindAll(".file-view__body .code-view__line"));
    }

    [Fact]
    public void The_header_is_the_same_header_whoever_renders_the_body()
    {
        // The identity is what a reader trusts the content against, and who
        // rendered the content does not change which file it is.
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "domain.md")
            .Add(v => v.Path, @".domain\backlog\domain.md")
            .Add(v => v.Source, "Repository")
            .Add(v => v.Kind, "Domain model")
            .Add(v => v.SizeInBytes, 2048)
            .Add(v => v.BodyContent, Editor()));

        Assert.Equal("domain.md", view.Find(".file-view__name").TextContent);
        Assert.Equal(@".domain\backlog\domain.md", view.Find(".file-view__path").TextContent);
        Assert.Equal(
            $"Repository · Domain model · {FileHeader.FormatSize(2048)}",
            view.Find(".file-view__meta").TextContent);
    }

    [Fact]
    public void A_supplied_body_gives_up_the_regions_synthetic_tab_stop()
    {
        // The tabindex is there because the built-in shapes render nothing
        // focusable, so the region is the only way a keyboard reaches the
        // overflow. A host supplies a body because it has controls to put in it,
        // and a stop in front of those announces the pane and does nothing.
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "domain.md")
            .Add(v => v.BodyContent, Editor()));

        var body = view.Find(".file-view__body");

        Assert.Null(body.GetAttribute("tabindex"));
        Assert.Empty(view.FindAll(".file-view__body[tabindex]"));

        // Naming it is not conditional: it is still the region a screen reader
        // announces around whatever the host put inside.
        Assert.Equal("region", body.GetAttribute("role"));
        Assert.Equal("domain.md", body.GetAttribute("aria-label"));
    }

    [Fact]
    public void Without_a_supplied_body_the_region_is_still_the_only_way_in()
    {
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "domain.md")
            .Add(v => v.Body, "# Domain\n"));

        Assert.Equal("0", view.Find(".file-view__body").GetAttribute("tabindex"));
        Assert.Single(view.FindAll("[tabindex='0']"));
    }

    [Fact]
    public void The_height_the_host_set_still_governs_a_supplied_body()
    {
        using var context = new BunitContext();

        var capped = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "domain.md")
            .Add(v => v.MaxHeight, "12rem")
            .Add(v => v.BodyContent, Editor()));

        Assert.Equal("max-height: 12rem", capped.Find(".file-view__body").GetAttribute("style"));

        var filled = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "domain.md")
            .Add(v => v.Fill, true)
            .Add(v => v.MaxHeight, "12rem")
            .Add(v => v.BodyContent, Editor()));

        Assert.Contains("file-view--fill", filled.Find(".file-view").ClassList);
        Assert.Null(filled.Find(".file-view__body").GetAttribute("style"));
    }

    [Fact]
    public void A_host_editor_on_screen_says_so_on_the_article_and_on_the_body()
    {
        // The two modifiers are one statement in two places, because the height
        // has to be handed down through both: the article takes what the pane
        // gives it, and the body stretches to what is left under the header. A
        // reader who pressed Edit and got a fourteen-row box with dead space
        // under it is what these prevent.
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "domain.md")
            .Add(v => v.Body, "# Domain\n")
            .Add(v => v.CanEdit, true)
            .Add(v => v.Editing, true)
            .Add(v => v.EditBodyContent, Editor()));

        Assert.Contains("file-view--editing", view.Find(".file-view").ClassList);
        Assert.Contains("file-view__body--edit", view.Find(".file-view__body").ClassList);

        // The class the rest of the stylesheet addresses is still the first one
        // on the element: a modifier that replaced the block would take the
        // padding, the overflow and the scrollbar with it.
        Assert.Contains("file-view__body", view.Find(".file-view__body").ClassList);
    }

    [Fact]
    public void Reading_the_file_leaves_both_modifiers_off()
    {
        // A host that supplied an editor is not editing until a reader asks, and
        // the read view already fills the pane the way it always did.
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "domain.md")
            .Add(v => v.Body, "# Domain\n")
            .Add(v => v.CanEdit, true)
            .Add(v => v.EditBodyContent, Editor()));

        Assert.DoesNotContain("file-view--editing", view.Find(".file-view").ClassList);
        Assert.DoesNotContain("file-view__body--edit", view.Find(".file-view__body").ClassList);
    }

    [Fact]
    public void A_body_supplied_for_both_modes_is_not_the_editing_state()
    {
        // BodyContent is whatever the host draws in every mode, and this
        // component has no idea whether that is an editor, a picture or a table.
        // Stretching it on the strength of Editing would be sizing a body on a
        // flag that says nothing about what is in it — the modifiers follow the
        // fragment that is on screen, and here that is not EditBodyContent.
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "domain.md")
            .Add(v => v.Body, "# Domain\n")
            .Add(v => v.CanEdit, true)
            .Add(v => v.Editing, true)
            .Add(v => v.BodyContent, Editor()));

        Assert.DoesNotContain("file-view--editing", view.Find(".file-view").ClassList);
        Assert.DoesNotContain("file-view__body--edit", view.Find(".file-view__body").ClassList);
    }

    [Fact]
    public void A_test_id_still_reaches_the_scroll_region_a_host_filled()
    {
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "domain.md")
            .Add(v => v.TestId, "file-view-domain")
            .Add(v => v.BodyContent, Editor()));

        Assert.NotNull(view.Find("[data-testid='file-view-domain']"));
        Assert.NotNull(view.Find("[data-testid='file-view-domain-body'] [data-testid='supplied-editor']"));
    }

    [Fact]
    public void Taking_the_supplied_body_away_gives_the_file_back()
    {
        // The file is not parsed while a host owns the body, so it has to be read
        // on the render where it starts mattering again: a cache still holding
        // "already parsed" from the skipped pass would hand back nothing.
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "domain.md")
            .Add(v => v.Body, "# Domain\n\nA paragraph.\n")
            .Add(v => v.BodyContent, Editor()));

        Assert.Empty(view.FindAll(".file-view__body .md-heading"));

        view.Render(parameters => parameters
            .Add(v => v.BodyContent, (RenderFragment?)null));

        Assert.Equal("Domain", view.Find(".file-view__body .md-heading").TextContent);
        Assert.Equal("A paragraph.", view.Find(".file-view__body .md-p").TextContent);
        Assert.Equal("0", view.Find(".file-view__body").GetAttribute("tabindex"));
        Assert.Empty(view.FindAll("[data-testid='supplied-editor']"));
    }
}
