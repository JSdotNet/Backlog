namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// What a single block states that contract 16 reports, per folder and level.
///
/// <para>Each test names the rule sentence it pins, and the blocks are the rule's
/// own examples wherever it gives one. Severities follow the rule's wording: a
/// field that is "not in that folder's vocabulary" is an error, as the rule treats
/// unrecognised fields; something it says is "reported as a warning", or that is
/// tidying rather than a refusal, is a warning.</para>
/// </summary>
public sealed class DevbookMetadataFindingsTests
{
    private static IReadOnlyList<DevbookMetadataFinding> For(
        DevbookFolder folder,
        string block,
        DevbookMetadataLevel level = DevbookMetadataLevel.Chapter,
        string? fileName = null) =>
        DevbookMetadataFindings.For(folder, level, MetadataReader.Parse(block), fileName);

    private static DevbookMetadataFinding Only(IReadOnlyList<DevbookMetadataFinding> findings, string field) =>
        Assert.Single(findings, finding => finding.Field == field);

    [Fact]
    public void A_settled_domain_chapter_reports_nothing()
    {
        // "a settled chapter with no relations, no estimate, and no issue shows
        // only type where the folder defines one".
        Assert.Empty(For(DevbookFolder.Domain, "type: aggregate"));
    }

    [Theory]
    [InlineData(DevbookFolder.Unknown)]
    [InlineData(DevbookFolder.Backlog)]
    public void A_folder_with_no_contract_reports_nothing_at_all(DevbookFolder folder)
    {
        // .backlog is legacy and keeps its behaviour; an unnamed folder has no rule.
        Assert.Empty(For(folder, "status: approved\napproved-by: ana\ntype: nonsense\nindex: sideways"));
    }

    [Theory]
    [InlineData(DevbookFolder.Arc42)]
    [InlineData(DevbookFolder.Domain)]
    [InlineData(DevbookFolder.Design)]
    public void An_explicit_active_in_a_resting_folder_is_reported(DevbookFolder folder)
    {
        var finding = Only(For(folder, "status: active"), "status");

        Assert.Equal(DevbookFindingSeverity.Warning, finding.Severity);
    }

    [Theory]
    [InlineData(DevbookFolder.Tech)]
    [InlineData(DevbookFolder.Ai)]
    public void A_rating_folder_does_not_rest_by_omission(DevbookFolder folder)
    {
        Assert.DoesNotContain(For(folder, "status: active\ntype: library"), finding => finding.Message.Contains("resting", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(DevbookFolder.Tech, "type: library")]
    [InlineData(DevbookFolder.Ai, "type: practice")]
    public void A_missing_status_is_reported_where_the_folder_requires_one(DevbookFolder folder, string block)
    {
        Assert.Equal(DevbookFindingSeverity.Error, Only(For(folder, block), "status").Severity);
    }

    [Theory]
    [InlineData(DevbookFolder.Arc42)]
    [InlineData(DevbookFolder.Domain)]
    [InlineData(DevbookFolder.Design)]
    public void A_missing_status_is_the_resting_value_in_an_editorial_folder(DevbookFolder folder)
    {
        Assert.DoesNotContain(For(folder, "related: [.arc42/01-introduction.md]"), finding => finding.Field == "status");
    }

    [Theory]
    [InlineData(DevbookFolder.Arc42, "approved")]
    [InlineData(DevbookFolder.Tech, "accepted")]
    [InlineData(DevbookFolder.Design, "approved")]
    [InlineData(DevbookFolder.Ai, "accepted")]
    public void A_decision_rung_outside_domain_is_an_error(DevbookFolder folder, string rung)
    {
        var findings = For(folder, $"status: {rung}");

        Assert.Contains(findings, finding =>
            finding.Field == "status" && finding.Severity == DevbookFindingSeverity.Error && finding.Message.Contains(rung, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(DevbookFolder.Arc42)]
    [InlineData(DevbookFolder.Tech)]
    [InlineData(DevbookFolder.Design)]
    [InlineData(DevbookFolder.Ai)]
    public void Each_of_the_six_record_fields_outside_domain_is_not_in_the_vocabulary(DevbookFolder folder)
    {
        var block = string.Join('\n', DevbookSchema.DecisionRecordFields.Select(field => $"{field}: x"));

        var findings = For(folder, block);

        foreach (var field in DevbookSchema.DecisionRecordFields)
        {
            Assert.Equal(DevbookFindingSeverity.Error, Only(findings, field).Severity);
        }
    }

    [Fact]
    public void A_signed_and_dated_approval_in_domain_reports_nothing()
    {
        Assert.Empty(For(DevbookFolder.Domain, """
            status: approved
            type: feature
            approved-by: ana
            approved-at: 2026-09-01
            approved-hash: sha256:0a1b2c3d
            """));
    }

    [Theory]
    [InlineData("status: approved\ntype: feature")]
    [InlineData("status: approved\ntype: feature\napproved-by: ana")]
    [InlineData("status: approved\ntype: feature\napproved-at: 2026-09-01")]
    public void An_approval_nobody_signed_and_dated_is_an_error(string block)
    {
        var finding = Only(For(DevbookFolder.Domain, block), "approved-by");

        Assert.Equal(DevbookFindingSeverity.Error, finding.Severity);
    }

    [Fact]
    public void An_accepted_chapter_keeps_its_approval_record_and_reports_nothing()
    {
        // "an accepted chapter carries both records".
        Assert.Empty(For(DevbookFolder.Domain, """
            status: accepted
            type: feature
            approved-by: ana
            approved-at: 2026-09-01
            accepted-by: bo
            accepted-at: 2026-09-17
            """));
    }

    [Fact]
    public void An_acceptance_over_no_approval_is_reported()
    {
        var findings = For(DevbookFolder.Domain, """
            status: accepted
            type: feature
            accepted-by: bo
            accepted-at: 2026-09-17
            """);

        Assert.Contains(findings, finding =>
            finding.Field == "accepted-by" && finding.Severity == DevbookFindingSeverity.Error
            && finding.Message.Contains("no approval", StringComparison.Ordinal));
    }

    [Fact]
    public void An_approval_record_left_behind_on_a_lapsed_chapter_is_reported()
    {
        var finding = Only(For(DevbookFolder.Domain, """
            status: draft
            type: feature
            approved-by: ana
            approved-at: 2026-09-01
            """), "approved-by");

        Assert.Equal(DevbookFindingSeverity.Warning, finding.Severity);
    }

    [Theory]
    [InlineData("requested")]
    [InlineData("changes-requested")]
    [InlineData("cleared")]
    public void A_complete_review_triad_in_a_known_state_reports_nothing(string state)
    {
        Assert.Empty(For(DevbookFolder.Domain, $"type: feature\nreview: {state}\nreviewer: cy\nreview-at: 2026-09-20"));
    }

    [Fact]
    public void A_review_state_outside_the_three_is_an_error()
    {
        var finding = Only(For(DevbookFolder.Domain, "type: feature\nreview: approved\nreviewer: cy\nreview-at: 2026-09-20"), "review");

        Assert.Equal(DevbookFindingSeverity.Error, finding.Severity);
        Assert.Contains("approved", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("review: requested")]
    [InlineData("review: requested\nreviewer: cy")]
    [InlineData("reviewer: cy\nreview-at: 2026-09-20")]
    public void A_partly_written_review_triad_is_a_warning(string triad)
    {
        var finding = Only(For(DevbookFolder.Domain, $"type: feature\n{triad}"), "review");

        Assert.Equal(DevbookFindingSeverity.Warning, finding.Severity);
    }

    [Theory]
    [InlineData(DevbookFolder.Arc42)]
    [InlineData(DevbookFolder.Design)]
    public void A_type_in_a_folder_that_defines_none_is_a_warning(DevbookFolder folder)
    {
        var finding = Only(For(folder, "type: chapter"), "type");

        Assert.Equal(DevbookFindingSeverity.Warning, finding.Severity);
    }

    [Theory]
    [InlineData(DevbookFolder.Domain, "type: policy")]
    [InlineData(DevbookFolder.Tech, "status: adopted\ntype: layer")]
    [InlineData(DevbookFolder.Ai, "status: adopted\ntype: aggregate")]
    public void An_unknown_type_is_a_warning(DevbookFolder folder, string block)
    {
        Assert.Equal(DevbookFindingSeverity.Warning, Only(For(folder, block), "type").Severity);
    }

    [Fact]
    public void A_domain_pages_own_filename_is_a_known_file_type()
    {
        Assert.Empty(For(DevbookFolder.Domain, "type: regulatory-annex", DevbookMetadataLevel.File, ".devbook/domain/orders/regulatory-annex.md"));
        Assert.Single(For(DevbookFolder.Domain, "type: aggregate", DevbookMetadataLevel.File, ".devbook/domain/orders/regulatory-annex.md"));
    }

    [Fact]
    public void A_tech_type_read_from_kind_asks_for_the_rename()
    {
        // "The old name still parses … but it reports a warning — rename it to type."
        var finding = Only(For(DevbookFolder.Tech, "status: adopted\nkind: library"), "kind");

        Assert.Equal(DevbookFindingSeverity.Warning, finding.Severity);
        Assert.Contains("rename", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tech_type_written_as_type_reports_nothing()
    {
        Assert.Empty(For(DevbookFolder.Tech, "status: adopted\ntype: library"));
    }

    [Fact]
    public void The_rules_bounded_context_chapter_and_context_file_report_nothing()
    {
        Assert.Empty(For(DevbookFolder.Domain, """
            type: bounded-context
            deployment: module
            related: [.devbook/domain/orders/context.md]
            """));

        Assert.Empty(For(DevbookFolder.Domain, """
            status: draft
            index: root
            type: context
            deployment: module
            """, DevbookMetadataLevel.File, ".devbook/domain/orders/context.md"));
    }

    [Fact]
    public void A_bounded_context_whose_type_is_drawn_as_a_mark_is_still_judged_by_its_type()
    {
        // MarkdownView hands the strip the record with `type` taken out wherever
        // the heading draws it as a mark. The block still states it, and its
        // `deployment` is legal because of it.
        var drawn = MetadataReader.Parse("type: bounded-context\ndeployment: service").WithoutType();

        Assert.Null(drawn.Type);
        Assert.Empty(DevbookMetadataFindings.For(DevbookFolder.Domain, DevbookMetadataLevel.Chapter, drawn));
    }

    [Fact]
    public void A_deployment_outside_service_and_module_is_an_error()
    {
        var finding = Only(For(DevbookFolder.Domain, "type: bounded-context\ndeployment: lambda"), "deployment");

        Assert.Equal(DevbookFindingSeverity.Error, finding.Severity);
    }

    [Fact]
    public void A_deployment_on_a_chapter_that_is_not_a_bounded_context_is_a_warning()
    {
        var finding = Only(For(DevbookFolder.Domain, "type: aggregate\ndeployment: service"), "deployment");

        Assert.Equal(DevbookFindingSeverity.Warning, finding.Severity);
    }

    [Fact]
    public void A_file_level_deployment_off_context_md_is_a_warning_and_unjudged_without_a_name()
    {
        var finding = Only(For(DevbookFolder.Domain, "type: domain\ndeployment: service", DevbookMetadataLevel.File, "domain.md"), "deployment");
        Assert.Equal(DevbookFindingSeverity.Warning, finding.Severity);

        Assert.Empty(For(DevbookFolder.Domain, "type: domain\ndeployment: service", DevbookMetadataLevel.File));
    }

    [Theory]
    [InlineData("number: 9", "number")]
    [InlineData("index: root", "index")]
    public void Number_and_index_on_a_chapter_are_file_level_only(string line, string field)
    {
        var finding = Only(For(DevbookFolder.Arc42, line), field);

        Assert.Equal(DevbookFindingSeverity.Warning, finding.Severity);
    }

    [Theory]
    [InlineData("index: root")]
    [InlineData("index: exclude")]
    [InlineData("number: 9")]
    public void Number_and_index_on_a_file_report_nothing(string line)
    {
        Assert.Empty(For(DevbookFolder.Arc42, line, DevbookMetadataLevel.File));
    }

    [Fact]
    public void An_index_outside_root_and_exclude_is_an_error()
    {
        var finding = Only(For(DevbookFolder.Arc42, "index: first", DevbookMetadataLevel.File), "index");

        Assert.Equal(DevbookFindingSeverity.Error, finding.Severity);
    }

    [Theory]
    [InlineData(DevbookFolder.Arc42)]
    [InlineData(DevbookFolder.Domain)]
    [InlineData(DevbookFolder.Tech)]
    public void Extension_keys_are_never_validated(DevbookFolder folder)
    {
        // Including an empty value, and a key that shadows a schema field's name:
        // the namespace is opaque, so nothing in it is judged.
        var block = folder == DevbookFolder.Tech ? "status: adopted\ntype: library\n" : string.Empty;
        block += """
            ext.some-plugin.checked: 2026-09-17
            ext.some-plugin.status: active
            ext.other.approved-by:
            """;

        Assert.Empty(For(folder, block));
    }

    [Fact]
    public void Tests_and_date_are_ordinary_fields_everywhere()
    {
        Assert.Empty(For(DevbookFolder.Arc42, """
            date: 2026-09-17
            tests: [unit:dotnet:Ordering.Domain.Tests.OrderTests, e2e:playwright:tests/checkout.spec.ts#Guest checkout completes]
            """));
    }
}
