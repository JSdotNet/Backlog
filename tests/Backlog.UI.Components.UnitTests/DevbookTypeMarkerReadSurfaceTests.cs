namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The type mark on the surface a `.domain` document is actually read from: the
/// file pane, and the markdown view inside it.
///
/// <para>The marks shipped once already on the wrong surface. The domain panel
/// draws a summary card for a document nobody opened and hands the opened one
/// whole to <see cref="FileView"/>, and the mark was wired into the card — so
/// every route a reader has to a chapter landed on the branch with no mark in it.
/// These tests are written against the branch tree navigation produces: the file
/// pane, given the file's text.</para>
///
/// <para>Two marks are in play here and they answer different questions. The
/// <c>#</c> title's <c>type</c> says what kind of file this is and belongs on the
/// name in the header; each <c>##</c> chapter's says what that chapter describes
/// and belongs on its heading. Both are drawn, both are named, and neither adds a
/// text node to the element it leads.</para>
///
/// <para>And both are gated on the folder rather than on the value.
/// <c>.tech</c> writes a <c>type</c> of its own out of a different vocabulary, and
/// it is still a row in its strip.</para>
/// </summary>
public sealed class DevbookTypeMarkerReadSurfaceTests
{
    /// <summary>A `.domain` file as the convention writes one: the file's own type
    /// under the title, and a chapter with its own type under each heading.</summary>
    private const string DomainFile = """
        # Inbox

        ```meta
        type: domain
        status: draft
        ```

        What the Inbox owns.

        ## Inbox Item

        ```meta
        type: aggregate
        status: draft
        related: [".domain/capture/domain.md#capture"]
        ```

        The thing that arrives.

        ## Capture Source

        ```meta
        type: term
        status: draft
        ```

        Where it came from.
        """;

    /// <summary>The same shape with a <c>type</c> nobody has drawn a glyph for. The
    /// `.domain` vocabulary grows and this library is always the last to hear.</summary>
    private const string UnknownTypes = """
        # Inbox

        ```meta
        type: overview
        status: draft
        ```

        What the Inbox owns.

        ## Retention Policy

        ```meta
        type: policy-fragment
        status: draft
        ```

        How long it is kept.
        """;

    /// <summary>A `.tech` chapter, which states a <c>type</c> out of an entirely
    /// different vocabulary under the same field name.</summary>
    private const string TechFile = """
        # Shared Technologies

        ```meta
        status: adopted
        ```

        The technologies more than one channel uses.

        ## Markdown

        ```meta
        type: format
        status: adopted
        ```

        The language a task's content is written in.
        """;

    private static IRenderedComponent<FileView> Pane(
        BunitContext context,
        string body,
        DevbookFolder folder,
        bool showFields = true) =>
        context.Render<FileView>(parameters => parameters
            .Add(view => view.Name, "domain.md")
            .Add(view => view.Body, body)
            .Add(view => view.TestId, "file")
            .Add(view => view.RenderDevbookMetadata, true)
            .Add(view => view.RenderDevbookMetadataFields, showFields)
            .Add(view => view.DevbookFolder, folder));

    [Fact]
    public void An_opened_domain_document_marks_every_chapter_it_shows()
    {
        // The claim the card branch could never make: the marks are on the chapters
        // of the file the reader opened, in the order the file writes them.
        using var context = new BunitContext();

        var pane = Pane(context, DomainFile, DevbookFolder.Domain);

        var marks = pane.FindAll(".file-view__body .md-heading [data-testid='markdown-chapter-type-mark']");

        Assert.Equal(
            ["devbook-type-marker--aggregate", "devbook-type-marker--term"],
            marks.Select(Modifier));
    }

    [Fact]
    public void A_chapter_heading_is_announced_with_its_type_and_still_reads_as_itself()
    {
        // Both halves at once, which is what makes the mark free to lead a heading:
        // the accessible name is an attribute, the tooltip that would have been a
        // text node is declined, and the heading's own text does not move.
        using var context = new BunitContext();

        var pane = Pane(context, DomainFile, DevbookFolder.Domain);

        var heading = pane.FindAll(".file-view__body .md-heading")[1];
        var mark = heading.QuerySelector("svg")!;

        Assert.Equal("img", mark.GetAttribute("role"));
        Assert.Equal("type: aggregate", mark.GetAttribute("aria-label"));
        Assert.Null(mark.GetAttribute("aria-hidden"));

        Assert.Empty(mark.QuerySelectorAll("title"));
        Assert.Equal("Inbox Item", heading.TextContent);

        // The heading is still a heading at its own level. A mark that had cost the
        // element its role or its level would have taken heading navigation with it.
        Assert.Equal("heading", heading.GetAttribute("role"));
        Assert.Equal("2", heading.GetAttribute("aria-level"));
    }

    [Fact]
    public void The_files_own_type_marks_the_name_in_the_header()
    {
        // The `#` block's type, on the one line that says which file this is. The
        // header is the part of the pane that stays put, so it is where a fact about
        // the whole file belongs.
        using var context = new BunitContext();

        var pane = Pane(context, DomainFile, DevbookFolder.Domain);

        var name = pane.Find(".file-view__header h3.file-view__name");
        var mark = name.QuerySelector("[data-testid='file-file-type-mark']")!;

        Assert.Equal("devbook-type-marker--domain", Modifier(mark));
        Assert.Equal("img", mark.GetAttribute("role"));
        Assert.Equal("type: domain", mark.GetAttribute("aria-label"));
        Assert.Empty(mark.QuerySelectorAll("title"));

        // And the name is the name. The header truncates this element, so anything
        // the mark added to its text would have been eating the file's own name.
        Assert.Equal("domain.md", name.TextContent);
    }

    [Fact]
    public void A_marked_type_stops_being_a_row_in_the_strip_under_the_heading()
    {
        // The trade. `type` was a label-and-value row sitting among the fields that
        // point somewhere, and it is not one of those: it is a fact about the
        // heading above it. Drawn as the mark, the row goes — and what the strip is
        // for stays.
        using var context = new BunitContext();

        var pane = Pane(context, DomainFile, DevbookFolder.Domain);

        var fields = pane.Find(".file-view__body dl.devbook-fields");

        Assert.DoesNotContain("type", fields.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("aggregate", fields.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".domain/capture/domain.md#capture", fields.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_files_own_type_stops_being_a_row_in_the_headers_strip()
    {
        using var context = new BunitContext();

        var pane = Pane(context, DomainFile, DevbookFolder.Domain);

        // Nothing left for the header's own field strip to draw: the file states a
        // status, which the headline carries, and a type, which the name now does.
        Assert.Empty(pane.FindAll(".file-view__record-fields"));

        // The status is untouched by the trade. It is the other half of the record
        // and the one a reader asks for first.
        Assert.Equal("draft", pane.Find(".file-view__header .devbook-record__headline select").GetAttribute("value"));
    }

    [Fact]
    public void A_type_the_set_does_not_know_keeps_its_row_and_gets_no_glyph()
    {
        // Suppressing the row and drawing nothing would take the fact off the screen
        // altogether, which is the one outcome a growing vocabulary must not have.
        using var context = new BunitContext();

        var pane = Pane(context, UnknownTypes, DevbookFolder.Domain);

        Assert.Empty(pane.FindAll(".file-view__header h3.file-view__name svg"));
        Assert.Empty(pane.FindAll(".file-view__body .md-heading svg"));

        Assert.Contains("policy-fragment", pane.Find(".file-view__body dl.devbook-fields").TextContent, StringComparison.Ordinal);
        Assert.Contains("overview", pane.Find(".file-view__record-fields").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Another_folders_type_is_left_exactly_as_it_was()
    {
        // `.tech` writes `type: format`, which shares the field name with `.domain`
        // and none of the vocabulary. The gate is the folder rather than the value,
        // so the day the two sets do collide nothing here starts guessing.
        using var context = new BunitContext();

        var pane = Pane(context, TechFile, DevbookFolder.Tech);

        Assert.Empty(pane.FindAll(".file-view__header h3.file-view__name svg"));
        Assert.Empty(pane.FindAll(".file-view__body .md-heading svg"));
        Assert.Contains("format", pane.Find(".file-view__body dl.devbook-fields").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_mark_survives_the_pane_being_told_to_draw_no_fields_at_all()
    {
        // Which is the setting the domain panel actually passes, and the reason the
        // suppression above is not the whole story: with the fields off there is no
        // row to remove, and the mark is then the only statement of the type on the
        // screen. It has to be there, and it has to be named.
        using var context = new BunitContext();

        var pane = Pane(context, DomainFile, DevbookFolder.Domain, showFields: false);

        Assert.Empty(pane.FindAll("dl.devbook-fields"));
        Assert.Equal(2, pane.FindAll("[data-testid='markdown-chapter-type-mark']").Count);
        Assert.Equal("type: domain", pane.Find("[data-testid='file-file-type-mark']").GetAttribute("aria-label"));
    }

    [Fact]
    public void Markdown_that_is_not_a_knowledge_document_renders_exactly_what_it_did()
    {
        // The default has to stay what it always was. Every entry body in this
        // product goes through the same view, and `RenderDevbookMetadata` off means
        // a `meta` fence is a code block again — mark included.
        using var context = new BunitContext();

        var plain = context.Render<MarkdownView>(parameters => parameters
            .Add(view => view.Blocks, MarkdownPreview.ParseDocument(DomainFile)));

        Assert.Empty(plain.FindAll("svg"));
        Assert.NotEmpty(plain.FindAll("pre.md-code"));
    }

    [Fact]
    public void An_opened_additional_page_is_marked_as_the_page_it_is()
    {
        // Contract 16: a domain/ context's additional page states its own filename
        // as its type. The pane knows the file's path, and with it the value is a
        // known type — drawn as the page sheet, named with what the file says, and
        // its row gone like any other marked type's.
        using var context = new BunitContext();

        const string page = """
            # Billing

            ```meta
            type: go-live-takeover
            status: draft
            ```

            How the predecessor system's data was taken over.
            """;

        var pane = context.Render<FileView>(parameters => parameters
            .Add(view => view.Name, "go-live-takeover.md")
            .Add(view => view.Body, page)
            .Add(view => view.TestId, "file")
            .Add(view => view.RenderDevbookMetadata, true)
            .Add(view => view.DevbookFolder, DevbookFolder.Domain)
            .Add(view => view.DevbookDocumentPath, ".domain/billing/go-live-takeover.md"));

        var mark = pane.Find(".file-view__header h3.file-view__name [data-testid='file-file-type-mark']");
        Assert.Equal("devbook-type-marker--page", Modifier(mark));
        Assert.Equal("type: go-live-takeover", mark.GetAttribute("aria-label"));

        // The same file under another name is not that page, and keeps its word.
        var elsewhere = context.Render<FileView>(parameters => parameters
            .Add(view => view.Name, "domain.md")
            .Add(view => view.Body, page)
            .Add(view => view.TestId, "file")
            .Add(view => view.RenderDevbookMetadata, true)
            .Add(view => view.DevbookFolder, DevbookFolder.Domain)
            .Add(view => view.DevbookDocumentPath, ".domain/billing/domain.md"));

        Assert.Empty(elsewhere.FindAll(".file-view__header h3.file-view__name svg"));
    }

    /// <summary>The value a rendered mark is drawing, read back off its modifier
    /// class — the same thing the stylesheet matches on.</summary>
    private static string Modifier(AngleSharp.Dom.IElement mark) =>
        mark.ClassList.First(name =>
            name.StartsWith("devbook-type-marker--", StringComparison.Ordinal));
}
