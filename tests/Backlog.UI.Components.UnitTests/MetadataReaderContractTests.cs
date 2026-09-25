namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The fields devbook contract 16 added to a <c>meta</c> block, read back.
///
/// <para>Every block here is quoted from the vendored rule text rather than
/// invented, and <see cref="Every_fixture_is_quoted_from_the_rule_text"/> checks
/// that it still is: a contract bump that rewrites an example fails here first,
/// naming the line, instead of leaving these tests pinning a shape the rule no
/// longer shows.</para>
/// </summary>
public sealed class MetadataReaderContractTests
{
    /// <summary>The <c>tests</c> example under the field's own bullet in
    /// <c>devbook-chapter-metadata.md</c>.</summary>
    private const string TestsLine =
        "tests: [unit:dotnet:Ordering.Domain.Tests.OrderTests, e2e:playwright:tests/checkout.spec.ts#Guest checkout completes]";

    /// <summary>The invariant example: one entry, written as a scalar, whose
    /// selector is a method name.</summary>
    private const string InvariantTestsLine =
        "tests: unit:dotnet:Ordering.Domain.Tests.OrderTests.CannotConfirmAnAlreadyConfirmedOrder";

    /// <summary>The extension example, verbatim.</summary>
    private const string ExtLine = "ext.some-plugin.checked: 2026-09-17";

    /// <summary>The <c>context.md</c> file block from <c>devbook-domain.md</c>'s
    /// structure.</summary>
    private const string ContextBlock = """
        status: draft
        index: root
        type: context
        deployment: module
        """;

    [Fact]
    public void Every_fixture_is_quoted_from_the_rule_text()
    {
        Assert.Contains(TestsLine, DevbookRuleText.ChapterMetadata, StringComparison.Ordinal);
        Assert.Contains(InvariantTestsLine, DevbookRuleText.ChapterMetadata, StringComparison.Ordinal);
        Assert.Contains(ExtLine, DevbookRuleText.ChapterMetadata, StringComparison.Ordinal);
        Assert.Contains(ContextBlock.Replace("\r\n", "\n"), DevbookRuleText.Domain.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void Type_is_a_field_of_its_own_and_never_an_extra_key()
    {
        var meta = MetadataReader.Parse("type: aggregate");

        Assert.Equal("aggregate", meta.Type);
        Assert.False(meta.TypeReadFromKind);
        Assert.Null(meta.Kind);
        Assert.Empty(meta.Extra);
        Assert.False(meta.IsEmpty);
    }

    [Fact]
    public void The_legacy_kind_spelling_fills_type_and_says_so()
    {
        var meta = MetadataReader.Parse("""
            status: adopted
            kind: library
            """);

        Assert.Equal("library", meta.Type);
        Assert.True(meta.TypeReadFromKind);

        // Still readable under the old name, for the callers that always read it.
        Assert.Equal("library", meta.Kind);
    }

    [Fact]
    public void Type_wins_over_kind_when_a_block_writes_both()
    {
        var meta = MetadataReader.Parse("""
            type: tool
            kind: runtime
            """);

        Assert.Equal("tool", meta.Type);
        Assert.False(meta.TypeReadFromKind);
        Assert.Equal("runtime", meta.Kind);
    }

    [Fact]
    public void Without_type_takes_the_type_out_and_hands_back_the_same_record_when_there_is_none()
    {
        var typed = MetadataReader.Parse("type: aggregate\nstatus: draft");
        var stripped = typed.WithoutType();

        Assert.Null(stripped.Type);
        Assert.Equal("draft", stripped.Status);

        var untyped = MetadataReader.Parse("status: draft");
        Assert.Same(untyped, untyped.WithoutType());
    }

    [Fact]
    public void Without_type_takes_a_legacy_kind_with_it_and_leaves_a_second_kind_alone()
    {
        var legacy = MetadataReader.Parse("kind: aggregate").WithoutType();
        Assert.Null(legacy.Type);
        Assert.Null(legacy.Kind);
        Assert.True(legacy.IsEmpty);

        var both = MetadataReader.Parse("type: aggregate\nkind: runtime").WithoutType();
        Assert.Null(both.Type);
        Assert.Equal("runtime", both.Kind);
    }

    [Fact]
    public void Tests_are_plain_strings_and_only_the_list_splits_them()
    {
        var meta = MetadataReader.Parse(TestsLine);

        Assert.Equal(
            ["unit:dotnet:Ordering.Domain.Tests.OrderTests", "e2e:playwright:tests/checkout.spec.ts#Guest checkout completes"],
            meta.Tests);

        // Test identifiers, not chapter references: no edge comes out of them.
        Assert.Empty(meta.References);
        Assert.Empty(meta.Extra);
    }

    [Fact]
    public void A_scalar_tests_entry_keeps_every_colon_after_the_key()
    {
        var meta = MetadataReader.Parse(InvariantTestsLine);

        Assert.Equal(["unit:dotnet:Ordering.Domain.Tests.OrderTests.CannotConfirmAnAlreadyConfirmedOrder"], meta.Tests);
    }

    [Fact]
    public void A_pytest_selector_keeps_the_colons_of_its_own()
    {
        // "Only the first two colons delimit": a node id is full of them, and the
        // reader must hand the entry back whole.
        var meta = MetadataReader.Parse("tests: [integration:pytest:tests/api/test_orders.py::TestCheckout::test_guest]");

        Assert.Equal(["integration:pytest:tests/api/test_orders.py::TestCheckout::test_guest"], meta.Tests);
    }

    [Fact]
    public void The_context_file_block_reads_its_index_type_and_deployment()
    {
        var meta = MetadataReader.Parse(ContextBlock);

        Assert.Equal("draft", meta.Status);
        Assert.Equal("root", meta.Index);
        Assert.Equal("context", meta.Type);
        Assert.Equal("module", meta.Deployment);
        Assert.Empty(meta.Extra);
    }

    [Theory]
    [InlineData("number: 9", 9)]
    [InlineData("number: 0", 0)]
    [InlineData("number: -1", null)]
    [InlineData("number: nine", null)]
    public void Number_reads_as_a_non_negative_integer_or_not_at_all(string line, int? expected)
    {
        var meta = MetadataReader.Parse(line);

        Assert.Equal(expected, meta.Number);
        Assert.Empty(meta.Extra);
    }

    [Fact]
    public void Date_is_kept_as_authored()
    {
        Assert.Equal("2026-09-17", MetadataReader.Parse("date: 2026-09-17").Date);
    }

    [Fact]
    public void An_extension_key_is_collected_under_ext_and_never_under_extra()
    {
        var meta = MetadataReader.Parse(ExtLine);

        Assert.Equal("2026-09-17", Assert.Single(meta.Ext, pair => pair.Key == "some-plugin.checked").Value);
        Assert.Empty(meta.Extra);
        Assert.False(meta.IsEmpty);
    }

    [Fact]
    public void An_extension_keeps_its_authors_casing_and_its_value_verbatim()
    {
        var meta = MetadataReader.Parse("""
            ext.Some-Plugin.LastRun: "2026-09-17 10:00"
            ext.other.list: [a, b]
            ext.other.nothing: null
            """);

        Assert.Equal("\"2026-09-17 10:00\"", meta.Ext["Some-Plugin.LastRun"]);
        Assert.False(meta.Ext.ContainsKey("some-plugin.lastrun"));

        // Not split, not unquoted, not read as "no value" — the reader has no
        // opinion on what an extension means.
        Assert.Equal("[a, b]", meta.Ext["other.list"]);
        Assert.Equal("null", meta.Ext["other.nothing"]);
    }

    [Fact]
    public void An_empty_extension_value_is_kept_because_omit_when_empty_does_not_reach_it()
    {
        var meta = MetadataReader.Parse("""
            status: draft
            ext.some-plugin.seen:
            """);

        Assert.Equal(string.Empty, meta.Ext["some-plugin.seen"]);
        Assert.Empty(meta.Extra);
    }

    [Fact]
    public void A_block_holding_only_an_empty_extension_is_not_empty()
    {
        Assert.False(MetadataReader.Parse("ext.some-plugin.seen:").IsEmpty);
    }

    [Fact]
    public void The_nine_state_fields_are_state_and_never_extra()
    {
        var meta = MetadataReader.Parse("""
            status: accepted
            approved-by: ana
            approved-at: 2026-09-01
            approved-hash: sha256:0a1b2c3d
            accepted-by: bo
            accepted-at: 2026-09-17
            accepted-hash: sha256:0a1b2c3d
            review: cleared
            reviewer: cy
            review-at: 2026-08-30
            """);

        Assert.Empty(meta.Extra);
        Assert.Equal("ana", meta.State.ApprovedBy);
        Assert.Equal("2026-09-01", meta.State.ApprovedAt);
        Assert.Equal("sha256:0a1b2c3d", meta.State.ApprovedHash);
        Assert.Equal("bo", meta.State.AcceptedBy);
        Assert.Equal("2026-09-17", meta.State.AcceptedAt);
        Assert.Equal("sha256:0a1b2c3d", meta.State.AcceptedHash);
        Assert.Equal("cleared", meta.State.Review);
        Assert.Equal("cy", meta.State.Reviewer);
        Assert.Equal("2026-08-30", meta.State.ReviewAt);

        foreach (var field in DevbookSchema.StateFields)
        {
            Assert.NotNull(meta.State[field]);
        }
    }

    [Fact]
    public void A_block_with_state_and_nothing_else_is_not_empty()
    {
        var meta = MetadataReader.Parse("review: requested\nreviewer: cy\nreview-at: 2026-09-20");

        Assert.False(meta.State.IsEmpty);
        Assert.True(meta.State.HasReview);
        Assert.False(meta.IsEmpty);
    }

    [Fact]
    public void Implements_still_parses_for_the_legacy_folder()
    {
        var meta = MetadataReader.Parse("implements: [.domain/tasks/features.md#feature-roadmap-planning]");

        Assert.Equal(".domain/tasks/features.md#feature-roadmap-planning", Assert.Single(meta.Implements).Raw);
    }
}
