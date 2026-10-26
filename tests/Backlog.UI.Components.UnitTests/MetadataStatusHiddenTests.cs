namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A host that has switched the status display off: the record keeps its
/// heading, its sync direction and its fields, and draws no status — neither the
/// select nor the badge — and no decision state, which only qualifies a status.
/// </summary>
public sealed class MetadataStatusHiddenTests
{
    private const string ApprovedChapter = """
        status: approved
        type: feature
        approved-by: ana
        approved-at: 2026-09-01
        related: [".domain/context.md"]
        """;

    private const string Devbook = """
        # Shared Technologies

        ```meta
        status: adopted
        ```

        What the technologies are.

        ## Hosting

        ```meta
        status: trial
        ```

        Where it runs.
        """;

    private static RenderFragment Heading(string text) => builder =>
    {
        builder.OpenElement(0, "p");
        builder.AddAttribute(1, "class", "md-heading md-heading--2");
        builder.AddContent(2, text);
        builder.CloseElement();
    };

    [Fact]
    public void A_record_with_its_status_hidden_keeps_the_heading_and_the_fields()
    {
        using var context = new BunitContext();

        var view = context.Render<MetadataView>(parameters => parameters
            .Add(v => v.Metadata, MetadataReader.Parse(ApprovedChapter))
            .Add(v => v.Vocabulary, DevbookStatus.Vocabulary(DevbookFolder.Domain))
            .Add(v => v.Heading, Heading("Approvals"))
            .Add(v => v.ShowStatus, false));

        Assert.Equal("Approvals", view.Find(".devbook-record__headline p.md-heading").TextContent);
        Assert.Empty(view.FindAll(".status-editor"));
        Assert.Empty(view.FindAll(".badge--status"));
        Assert.Empty(view.FindAll(".devbook-record__state"));
        Assert.Contains("related", view.FindAll("dt").Select(label => label.TextContent));
    }

    /// <summary>A finding about the status prints the status word, and one about
    /// a sign-off names who signed: both go with the status. A finding about any
    /// other field stays.</summary>
    [Fact]
    public void Findings_about_the_status_and_its_sign_off_go_with_it()
    {
        using var context = new BunitContext();

        // `approved` is a domain/ rung, so in arc42/ it is a status finding, and
        // the approval record is a decision-state finding; `index` on a chapter is
        // neither.
        var view = context.Render<MetadataView>(parameters => parameters
            .Add(v => v.Metadata, MetadataReader.Parse("status: approved\napproved-by: ana\napproved-at: 2026-09-01\nindex: root"))
            .Add(v => v.Vocabulary, DevbookStatus.Vocabulary(DevbookFolder.Arc42))
            .Add(v => v.Folder, DevbookFolder.Arc42)
            .Add(v => v.Level, DevbookMetadataLevel.Chapter)
            .Add(v => v.Heading, Heading("Decision"))
            .Add(v => v.ShowStatus, false));

        var fields = view.FindAll(".devbook-findings li .metadata-scalar, .devbook-findings li").Select(item => item.TextContent).ToList();
        Assert.DoesNotContain(fields, text => text.Contains("approved", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(fields, text => text.Contains("index", StringComparison.Ordinal));
    }

    [Fact]
    public void A_record_whose_only_findings_are_about_a_hidden_status_draws_nothing()
    {
        using var context = new BunitContext();

        var view = context.Render<MetadataView>(parameters => parameters
            .Add(v => v.Metadata, MetadataReader.Parse("status: approved"))
            .Add(v => v.Vocabulary, DevbookStatus.Vocabulary(DevbookFolder.Arc42))
            .Add(v => v.Folder, DevbookFolder.Arc42)
            .Add(v => v.ShowStatus, false));

        Assert.Equal(string.Empty, view.Markup.Trim());
    }

    [Fact]
    public void A_status_only_record_with_no_heading_draws_nothing_when_its_status_is_hidden()
    {
        using var context = new BunitContext();

        var view = context.Render<MetadataView>(parameters => parameters
            .Add(v => v.Metadata, MetadataReader.Parse("status: adopted"))
            .Add(v => v.ShowStatus, false));

        Assert.Equal(string.Empty, view.Markup.Trim());
    }

    [Fact]
    public void A_file_view_with_its_status_hidden_names_the_file_and_its_chapters_without_one()
    {
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "shared-technologies.md")
            .Add(v => v.Body, Devbook)
            .Add(v => v.TestId, "file")
            .Add(v => v.RenderDevbookMetadata, true)
            .Add(v => v.DevbookFolder, DevbookFolder.Tech)
            .Add(v => v.ShowDevbookStatus, false));

        Assert.Equal("shared-technologies.md", view.Find(".file-view__header h3.file-view__name").TextContent);
        Assert.Contains(view.FindAll(".file-view__body p.md-heading"), heading => heading.TextContent == "Hosting");
        Assert.Empty(view.FindAll(".status-editor"));
        Assert.Empty(view.FindAll(".badge--status"));
    }

    /// <summary>An emptied fence under a heading used to stay on the page through
    /// its "No status" select; with the status hidden there is no select, and the
    /// heading must not go with it.</summary>
    [Fact]
    public void A_heading_over_an_empty_fence_stays_when_the_status_is_hidden()
    {
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "0027.md")
            .Add(v => v.Body, "# Decision\n\n### 6. Links run one way\n\n```meta\n```\n\nProse.\n")
            .Add(v => v.TestId, "file")
            .Add(v => v.RenderDevbookMetadata, true)
            .Add(v => v.DevbookFolder, DevbookFolder.Arc42)
            .Add(v => v.ShowDevbookStatus, false));

        Assert.Contains("6. Links run one way", view.Find(".file-view__body").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_view_shows_both_statuses_by_default()
    {
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "shared-technologies.md")
            .Add(v => v.Body, Devbook)
            .Add(v => v.TestId, "file")
            .Add(v => v.RenderDevbookMetadata, true)
            .Add(v => v.DevbookFolder, DevbookFolder.Tech));

        Assert.Equal(2, view.FindAll(".status-editor").Count);
    }
}
