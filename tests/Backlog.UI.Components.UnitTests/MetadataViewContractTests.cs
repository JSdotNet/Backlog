namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// How the fields contract 16 added are drawn: the ordinary ones as rows, the
/// decision and review state beside the status and never as rows, the extension
/// namespace as one row, and the findings under the headline only when the caller
/// names the folder.
/// </summary>
public sealed class MetadataViewContractTests
{
    private const string ApprovedChapter = """
        status: approved
        type: feature
        approved-by: ana
        approved-at: 2026-09-01
        approved-hash: sha256:0a1b2c3d
        """;

    private static RenderFragment Heading(string text) => builder =>
    {
        builder.OpenElement(0, "p");
        builder.AddAttribute(1, "class", "md-heading md-heading--2");
        builder.AddContent(2, text);
        builder.CloseElement();
    };

    private static IRenderedComponent<MetadataView> Render(
        BunitContext context,
        string block,
        DevbookFolder? folder = null,
        DevbookMetadataLevel level = DevbookMetadataLevel.Chapter,
        string? fileName = null) =>
        context.Render<MetadataView>(parameters =>
        {
            parameters.Add(view => view.Metadata, MetadataReader.Parse(block));
            if (folder is { } known)
            {
                parameters
                    .Add(view => view.Vocabulary, DevbookStatus.Vocabulary(known))
                    .Add(view => view.Folder, known)
                    .Add(view => view.Level, level)
                    .Add(view => view.FileName, fileName);
            }
        });

    [Fact]
    public void Decision_state_is_drawn_in_the_headline_beside_the_status()
    {
        using var context = new BunitContext();

        var view = Render(context, ApprovedChapter, DevbookFolder.Domain);

        var state = view.Find(".devbook-record__headline > .devbook-record__state");
        Assert.Equal("Decision and review state", state.GetAttribute("aria-label"));

        var approval = view.Find("[data-testid='devbook-state-approval']");
        Assert.Equal("approved by ana on 2026-09-01", approval.TextContent);
        Assert.Contains("badge--decision", approval.ClassList);

        // The hash means nothing to a reader; it rides on the tooltip.
        Assert.Equal("approved-hash: sha256:0a1b2c3d", approval.GetAttribute("title"));
    }

    [Fact]
    public void Decision_state_is_never_a_row_in_the_body()
    {
        using var context = new BunitContext();

        var view = Render(context, ApprovedChapter + "\nrelated: [.devbook/domain/orders/context.md]", DevbookFolder.Domain);

        var labels = view.FindAll("dt").Select(label => label.TextContent).ToList();
        foreach (var field in DevbookSchema.StateFields)
        {
            Assert.DoesNotContain(field, labels);
        }

        Assert.DoesNotContain("sha256", view.Find("dl.devbook-fields").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_block_holding_only_state_still_draws_the_state()
    {
        using var context = new BunitContext();

        var view = Render(context, "review: changes-requested\nreviewer: cy\nreview-at: 2026-09-20");

        Assert.Equal(
            "review: changes-requested · reviewer cy · 2026-09-20",
            view.Find("[data-testid='devbook-state-review']").TextContent);
        Assert.Empty(view.FindAll("dl"));
    }

    [Fact]
    public void An_accepted_chapter_states_both_records_in_the_order_they_were_climbed()
    {
        using var context = new BunitContext();

        var view = Render(context, """
            status: accepted
            approved-by: ana
            approved-at: 2026-09-01
            accepted-by: bo
            accepted-at: 2026-09-17
            """);

        Assert.Equal(
            ["approved by ana on 2026-09-01", "accepted by bo on 2026-09-17"],
            view.FindAll(".devbook-record__state .badge").Select(badge => badge.TextContent));
    }

    [Fact]
    public void A_record_with_no_state_draws_no_state_element()
    {
        using var context = new BunitContext();

        var view = Render(context, "status: draft\ntype: feature", DevbookFolder.Domain);

        Assert.Empty(view.FindAll(".devbook-record__state"));
    }

    [Fact]
    public void Tests_number_index_date_and_deployment_are_ordinary_rows()
    {
        using var context = new BunitContext();

        var view = Render(context, """
            number: 9
            index: root
            date: 2026-09-17
            deployment: module
            tests: [unit:dotnet:Ordering.Domain.Tests.OrderTests, e2e:playwright:tests/checkout.spec.ts#Guest checkout completes]
            """);

        string Value(string label) =>
            view.FindAll(".devbook-fields__row").Single(row => row.QuerySelector("dt")!.TextContent == label)
                .QuerySelector("dd")!.TextContent;

        Assert.Equal("9", Value("number"));
        Assert.Equal("root", Value("index"));
        Assert.Equal("2026-09-17", Value("date"));
        Assert.Equal("module", Value("deployment"));

        // Plain strings in the value face, one code element per entry, never links.
        var tests = view.FindAll("[data-testid='devbook-tests'] code.devbook-value");
        Assert.Equal(
            ["unit:dotnet:Ordering.Domain.Tests.OrderTests", "e2e:playwright:tests/checkout.spec.ts#Guest checkout completes"],
            tests.Select(entry => entry.TextContent));
        Assert.Empty(view.FindAll("[data-testid='devbook-tests'] a"));
    }

    [Fact]
    public void Extensions_are_one_row_with_every_key_and_value_verbatim()
    {
        using var context = new BunitContext();

        var view = Render(context, """
            ext.some-plugin.checked: 2026-09-17
            ext.Other.Seen:
            """);

        var row = view.Find("[data-testid='devbook-ext']");
        var entries = row.QuerySelectorAll("code.devbook-value");

        Assert.Equal(["some-plugin.checked: 2026-09-17", "Other.Seen:"], entries.Select(entry => entry.TextContent));
        Assert.Equal("ext.some-plugin.checked", entries[0].GetAttribute("title"));
        Assert.Single(view.FindAll("dt"), label => label.TextContent == "ext");
    }

    [Fact]
    public void A_type_row_is_still_drawn_where_the_file_writes_type()
    {
        // `.tech` has shown its `type` as a row since before the field was
        // modelled; promoting it must not take it off the screen.
        using var context = new BunitContext();

        var view = Render(context, "status: adopted\ntype: format", DevbookFolder.Tech);

        var row = view.FindAll(".devbook-fields__row").Single(r => r.QuerySelector("dt")!.TextContent == "type");
        Assert.Equal("format", row.QuerySelector("code.devbook-value")!.TextContent);
    }

    [Fact]
    public void A_type_read_from_the_legacy_kind_is_the_kind_chip_and_not_a_second_row()
    {
        using var context = new BunitContext();

        var view = Render(context, "status: adopted\nkind: library");

        Assert.Equal("library", view.Find(".badge--kind").TextContent);
        Assert.DoesNotContain(view.FindAll("dt"), label => label.TextContent == "type");
    }

    [Fact]
    public void Without_a_folder_no_findings_are_drawn()
    {
        using var context = new BunitContext();

        var view = Render(context, "status: approved\nkind: library\nindex: sideways");

        Assert.Empty(view.FindAll("[data-testid='devbook-findings']"));
    }

    [Fact]
    public void With_a_folder_the_findings_sit_between_the_headline_and_the_fields()
    {
        using var context = new BunitContext();

        var view = Render(context, "status: adopted\nkind: library\nversion: \"1.0\"", DevbookFolder.Tech);

        var record = view.Find(".devbook-record");
        var children = record.Children.Select(child => child.ClassName).ToList();
        Assert.Equal(["devbook-record__headline", "devbook-findings", "devbook-fields"], children);

        var item = view.Find(".devbook-findings__item");
        Assert.Contains("devbook-findings__item--warning", item.ClassList);
        Assert.Equal("warning", item.QuerySelector(".badge--finding")!.TextContent);
        Assert.Equal("kind", item.QuerySelector("code.devbook-value")!.TextContent);
        Assert.Contains("rename", item.QuerySelector(".devbook-findings__message")!.TextContent, StringComparison.Ordinal);
        Assert.Equal("Metadata findings", view.Find("ul.devbook-findings").GetAttribute("aria-label"));
    }

    [Fact]
    public void An_explicit_resting_status_is_reported_in_the_view()
    {
        using var context = new BunitContext();

        var view = Render(context, "status: active", DevbookFolder.Arc42);

        var item = view.Find(".devbook-findings__item");
        Assert.Equal("status", item.QuerySelector("code.devbook-value")!.TextContent);
    }

    [Fact]
    public void The_chapter_and_file_shapes_hand_the_folder_and_their_level_down()
    {
        using var context = new BunitContext();

        // `number` is file-level only: reported on the chapter, silent on the file.
        var chapter = context.Render<MetadataChapterView>(parameters => parameters
            .Add(view => view.Metadata, MetadataReader.Parse("number: 9"))
            .Add(view => view.Heading, Heading("Introduction"))
            .Add(view => view.Folder, DevbookFolder.Arc42));

        var file = context.Render<MetadataFileView>(parameters => parameters
            .Add(view => view.Metadata, MetadataReader.Parse("number: 9"))
            .Add(view => view.Heading, Heading("Introduction"))
            .Add(view => view.Folder, DevbookFolder.Arc42));

        Assert.Single(chapter.FindAll(".devbook-findings__item"));
        Assert.Empty(file.FindAll(".devbook-findings__item"));
    }

    [Fact]
    public void The_file_shape_uses_the_file_name_to_judge_a_deployment()
    {
        using var context = new BunitContext();

        IRenderedComponent<MetadataFileView> File(string name) =>
            context.Render<MetadataFileView>(parameters => parameters
                .Add(view => view.Metadata, MetadataReader.Parse("type: domain\ndeployment: module"))
                .Add(view => view.Heading, Heading("Orders"))
                .Add(view => view.Folder, DevbookFolder.Domain)
                .Add(view => view.FileName, name));

        Assert.Single(File(".devbook/domain/orders/domain.md").FindAll(".devbook-findings__item"));
        Assert.Empty(File(".devbook/domain/orders/context.md").FindAll(".devbook-findings__item")
            .Where(item => item.QuerySelector("code")!.TextContent == "deployment"));
    }
}
