namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A knowledge file carries two kinds of <c>meta</c> fence: one under every
/// <c>##</c> chapter, describing that chapter, and one under the <c>#</c> title,
/// describing the file itself. Only the second is about the file, and the header
/// is where this pane already says what the file is — so that is where it is
/// drawn, beside the name, on the part of the pane that stays put while the body
/// scrolls.
///
/// <para>The chapters are untouched by that: their records still belong to the
/// headings they were written under, and those headings are in the body.</para>
/// </summary>
public sealed class FileViewFileMetadataTests
{
    /// <summary>A knowledge file as the convention writes one: the title with the
    /// file's own record under it, and a chapter carrying a record of its
    /// own.</summary>
    private const string Knowledge = """
        # Shared Technologies

        ```meta
        status: adopted
        related: [".tech/technology-graph.md"]
        ```

        What the technologies are.

        ## Hosting

        ```meta
        status: trial
        ```

        Where it runs.
        """;

    /// <summary>The same file without a block of its own: the first heading is a
    /// chapter, and nothing describes the whole file.</summary>
    private const string ChaptersOnly = """
        What the technologies are.

        ## Hosting

        ```meta
        status: trial
        ```

        Where it runs.
        """;

    private static IRenderedComponent<FileView> Render(
        BunitContext context,
        Action<ComponentParameterCollectionBuilder<FileView>> extra,
        string body = Knowledge) =>
        context.Render<FileView>(parameters =>
        {
            parameters
                .Add(v => v.Name, "shared-technologies.md")
                .Add(v => v.Body, body)
                .Add(v => v.TestId, "file");
            extra(parameters);
        });

    [Fact]
    public void The_files_own_record_is_drawn_in_the_header_beside_its_name()
    {
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true));

        var headline = view.Find(".file-view__header .knowledge-record__headline");

        Assert.Equal("shared-technologies.md", headline.QuerySelector("h3.file-view__name")!.TextContent);
        Assert.Equal("adopted", headline.QuerySelector(".badge--status")!.TextContent);

        // Named for the file, because the header already holds several groups and
        // "Knowledge metadata" on its own does not say which file's.
        Assert.Equal(
            "shared-technologies.md metadata",
            view.Find("[data-testid='file-file-metadata']").GetAttribute("aria-label"));
    }

    [Fact]
    public void The_body_draws_neither_the_record_nor_the_fence_it_was_read_from()
    {
        // Drawn once. The fence used to be folded into the H1 in the body, where
        // it scrolled away from the reader who wanted it; hoisting it into the
        // header only helps if it does not also stay behind — and it must not
        // fall back to a raw code block either, which is the fence again with the
        // record taken off.
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true));

        var body = view.Find(".file-view__body");

        Assert.Empty(body.QuerySelectorAll("pre.md-code"));

        // One record in the body, and it is the chapter's: the file's own is not
        // drawn a second time down here.
        var record = Assert.Single(body.QuerySelectorAll(".knowledge-record"));
        Assert.Equal("Hosting", record.QuerySelector("p.md-heading")!.TextContent);

        // The title is still in the body, as the plain heading it is.
        Assert.Equal("Shared Technologies", body.QuerySelector("p.md-heading--1")!.TextContent);
    }

    [Fact]
    public void A_chapters_record_is_still_folded_into_the_heading_it_belongs_to()
    {
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true));

        var headline = view.Find(".file-view__body .knowledge-record__headline");

        Assert.Equal("Hosting", headline.QuerySelector("p.md-heading")!.TextContent);
        Assert.Equal("trial", headline.QuerySelector(".badge--status")!.TextContent);
    }

    [Fact]
    public void Without_being_asked_the_files_fence_is_the_code_block_it_has_always_been()
    {
        // All of this is opt-in: a host that says nothing gets the markup it got
        // before any of it existed, header included.
        using var context = new BunitContext();

        var view = Render(context, _ => { });

        Assert.Contains(
            "status: adopted",
            view.Find(".file-view__body pre.md-code code").TextContent,
            StringComparison.Ordinal);
        Assert.Empty(view.FindAll(".knowledge-record"));
        Assert.Empty(view.FindAll("[data-testid='file-file-metadata']"));
    }

    [Fact]
    public void A_file_with_no_block_of_its_own_keeps_the_header_it_always_had()
    {
        // The name must not disappear into a record that renders nothing: an
        // empty block draws no group at all, so the identity column has to be the
        // markup it was before whenever there is no record to wrap it in.
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true)
            .Add(v => v.Source, "GitHub Copilot"), ChaptersOnly);

        var identity = view.Find(".file-view__identity");

        Assert.Equal("shared-technologies.md", identity.QuerySelector("h3.file-view__name")!.TextContent);
        Assert.Equal("GitHub Copilot", identity.QuerySelector("p.file-view__meta")!.TextContent);
        Assert.Empty(identity.QuerySelectorAll(".knowledge-record"));

        // And the chapter that does state one still has it.
        Assert.Equal("trial", view.Find(".file-view__body .knowledge-record__headline .badge--status").TextContent);
    }

    [Fact]
    public void A_reading_surface_that_wants_the_status_alone_gets_it_in_the_header_too()
    {
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true)
            .Add(v => v.RenderKnowledgeMetadataFields, false));

        Assert.Equal("adopted", view.Find(".file-view__header .badge--status").TextContent);
        Assert.Empty(view.FindAll(".file-view__header dl.knowledge-fields"));
    }

    [Fact]
    public void The_fields_the_file_states_are_drawn_under_its_name()
    {
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true));

        Assert.Equal(
            ".tech/technology-graph.md",
            view.Find(".file-view__header dl.knowledge-fields .knowledge-ref").TextContent);
    }

    [Fact]
    public void A_status_picked_in_the_header_is_reported_against_the_title()
    {
        // Block 0 and the title's text, exactly as a chapter's change is reported:
        // the record moved into the header, the fence it was read from did not
        // move at all, and block 0 is what the rest of the view anchors by.
        using var context = new BunitContext();
        var changes = new List<KnowledgeStatusChange>();

        var view = Render(context, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true)
            .Add(v => v.KnowledgeFolder, KnowledgeFolder.Tech)
            .Add(v => v.OnKnowledgeStatusChanged, EventCallback.Factory.Create<KnowledgeStatusChange>(this, changes.Add)));

        view.Find(".file-view__header .status-editor select").Change("retired");

        var change = Assert.Single(changes);

        Assert.Equal("retired", change.Status);
        Assert.Equal("Shared Technologies", change.Heading);
        Assert.Equal(0, change.BlockIndex);
    }

    [Fact]
    public void The_record_stays_in_the_header_while_the_host_draws_the_body()
    {
        // The header is the part that stays put, so it keeps the file's status in
        // all three modes. It has to be read from the text to manage that: a host
        // that supplies its own body leaves this pane with no blocks at all, and a
        // status that vanished the moment a reader pressed Edit would be missing
        // from the one mode in which they are changing the file.
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.RenderKnowledgeMetadata, true)
            .Add(v => v.CanEdit, true)
            .Add(v => v.Editing, true)
            .Add(v => v.EditBodyContent, (RenderFragment)(builder => builder.AddMarkupContent(0, "<textarea></textarea>"))));

        Assert.Equal("adopted", view.Find(".file-view__header .badge--status").TextContent);
        Assert.Single(view.FindAll(".file-view__body textarea"));
    }

    [Fact]
    public void A_block_that_states_nothing_does_not_take_the_name_down_with_it()
    {
        // An empty record draws nothing at all, and the name is drawn inside the
        // record — so a fence with nothing in it has to count as no record here,
        // or the header loses the one thing it exists to say.
        using var context = new BunitContext();

        var view = Render(
            context,
            parameters => parameters.Add(v => v.RenderKnowledgeMetadata, true),
            "# Shared Technologies\n\n```meta\n```\n\nWhat the technologies are.\n");

        Assert.Equal("shared-technologies.md", view.Find(".file-view__identity h3.file-view__name").TextContent);
        Assert.Empty(view.FindAll(".knowledge-record"));
    }

    [Fact]
    public void A_code_file_has_no_title_for_a_record_to_hang_off()
    {
        // A `#` comment at the top of a `.cs` file is a comment, and this pane
        // never reads a code file as markdown — so there is nothing here to read a
        // record out of either.
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "Program.cs")
            .Add(v => v.Body, "# not a title\n\n```meta\nstatus: adopted\n```\n")
            .Add(v => v.TestId, "file")
            .Add(v => v.RenderKnowledgeMetadata, true));

        Assert.Empty(view.FindAll(".knowledge-record"));
        Assert.Equal("Program.cs", view.Find(".file-view__identity h3.file-view__name").TextContent);
    }
}
