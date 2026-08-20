namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The parser reads what the repository actually contains, not what the
/// convention says it should: inline lists appear both quoted and bare, and the
/// `.arc42` template still writes the empty forms the convention asks authors to
/// omit. The fixtures here are lifted from real files for exactly that reason.
/// </summary>
public sealed class KnowledgeMetaParserTests
{
    [Fact]
    public void A_block_with_only_a_status_reads_back_as_that_status()
    {
        var meta = KnowledgeMeta.Parse("status: active");

        Assert.Equal("active", meta.Status);
        Assert.False(meta.IsEmpty);
        Assert.Empty(meta.Related);
    }

    [Fact]
    public void A_quoted_inline_list_is_read_as_references()
    {
        // Verbatim from .tech/shared.md.
        var meta = KnowledgeMeta.Parse("""
            status: candidate
            related: [".tech/technology-graph.md", ".arc42/02-constraints.md#technical-constraints"]
            """);

        Assert.Equal("candidate", meta.Status);
        Assert.Equal(
            [".tech/technology-graph.md", ".arc42/02-constraints.md#technical-constraints"],
            meta.Related.Select(reference => reference.Raw));
        Assert.True(meta.Related[0].IsFile);
        Assert.Equal("technical-constraints", meta.Related[1].Slug);
    }

    [Fact]
    public void An_unquoted_inline_list_is_read_the_same_way()
    {
        // Verbatim from .backlog/domain-backlog.md.
        var meta = KnowledgeMeta.Parse("""
            status: draft
            implements: [.domain/backlog/features.md#feature-roadmap-planning]
            related: [.domain/environment/features.md#feature-environment-aware-work-context]
            """);

        Assert.Equal("draft", meta.Status);
        Assert.Equal(".domain/backlog/features.md#feature-roadmap-planning", Assert.Single(meta.Implements).Raw);
        Assert.Equal(".domain/environment/features.md#feature-environment-aware-work-context", Assert.Single(meta.Related).Raw);
    }

    [Fact]
    public void A_block_list_under_a_bare_key_is_read_as_references()
    {
        var meta = KnowledgeMeta.Parse("""
            status: active
            related:
              - .arc42/01-introduction.md
              - .domain/backlog/domain.md#aggregate-entry
            """);

        Assert.Equal(
            [".arc42/01-introduction.md", ".domain/backlog/domain.md#aggregate-entry"],
            meta.Related.Select(reference => reference.Raw));
    }

    [Fact]
    public void The_empty_forms_the_template_still_writes_read_as_absent()
    {
        // The convention says to omit these, and this repository's own .arc42
        // template writes them anyway. Both have to mean "nothing stated".
        var meta = KnowledgeMeta.Parse("""
            status: draft
            related: []
            issue: null
            """);

        Assert.Equal("draft", meta.Status);
        Assert.Empty(meta.Related);
        Assert.Null(meta.Issue);
        Assert.Empty(meta.Extra);
    }

    [Fact]
    public void A_hash_inside_a_reference_is_a_chapter_separator_and_never_a_comment()
    {
        var meta = KnowledgeMeta.Parse("""
            status: adopted
            depends-on: [".tech/shared.md#markdown"]
            issue: JSdotNet/Backlog#42
            """);

        Assert.Equal(".tech/shared.md#markdown", Assert.Single(meta.DependsOn).Raw);
        Assert.Equal("markdown", meta.DependsOn[0].Slug);
        Assert.Equal("JSdotNet/Backlog#42", meta.Issue);
    }

    [Fact]
    public void An_issue_url_keeps_its_colon_and_its_hash()
    {
        var meta = KnowledgeMeta.Parse("issue: https://github.com/JSdotNet/Backlog/issues/42#issuecomment-1");

        Assert.Equal("https://github.com/JSdotNet/Backlog/issues/42#issuecomment-1", meta.Issue);
    }

    [Fact]
    public void A_field_the_schema_does_not_define_is_kept_rather_than_dropped()
    {
        // A reader that discarded it would make a genuine schema addition look
        // like a file that never said anything.
        var meta = KnowledgeMeta.Parse("""
            status: active
            owner: platform-team
            reviewers: [ana, bo]
            """);

        Assert.Equal(["platform-team"], meta.Extra["owner"]);
        Assert.Equal(["ana", "bo"], meta.Extra["reviewers"]);
    }

    [Fact]
    public void A_reference_entry_that_does_not_parse_is_kept_verbatim()
    {
        var meta = KnowledgeMeta.Parse("""
            status: active
            related: [".arc42/01-introduction.md", "#dangling"]
            """);

        Assert.Equal(".arc42/01-introduction.md", Assert.Single(meta.Related).Raw);
        Assert.Equal(["#dangling"], meta.Extra["related"]);
    }

    [Fact]
    public void Aliases_are_plain_strings_and_are_never_read_as_references()
    {
        var meta = KnowledgeMeta.Parse("""
            status: active
            aliases: ["OrderLine", "order_line_id"]
            """);

        Assert.Equal(["OrderLine", "order_line_id"], meta.Aliases);
        Assert.Empty(meta.Related);
        Assert.Empty(meta.References);
    }

    [Fact]
    public void Alternatives_are_plain_strings_too()
    {
        var meta = KnowledgeMeta.Parse("""
            status: adopted
            alternatives: [YamlDotNet, Markdig]
            """);

        Assert.Equal(["YamlDotNet", "Markdig"], meta.Alternatives);
    }

    [Fact]
    public void An_effort_reads_back_as_the_integer_it_states()
    {
        var meta = KnowledgeMeta.Parse("""
            status: draft
            effort: 5
            """);

        Assert.Equal(5, meta.Effort);
        Assert.False(meta.IsEmpty);
        Assert.Empty(meta.Extra);
    }

    [Fact]
    public void An_effort_of_zero_is_an_estimate_and_never_read_as_unset()
    {
        // Zero story points is a real answer, distinct from "not estimated".
        var meta = KnowledgeMeta.Parse("""
            status: draft
            effort: 0
            """);

        Assert.Equal(0, meta.Effort);
        Assert.False(meta.IsEmpty);
    }

    [Fact]
    public void An_omitted_effort_is_null()
    {
        var meta = KnowledgeMeta.Parse("status: draft");

        Assert.Null(meta.Effort);
    }

    [Fact]
    public void An_effort_of_null_reads_as_no_estimate()
    {
        var meta = KnowledgeMeta.Parse("""
            status: draft
            effort: null
            """);

        Assert.Null(meta.Effort);
        Assert.Empty(meta.Extra);
    }

    [Theory]
    [InlineData("large")]
    [InlineData("-3")]
    [InlineData("2.5")]
    public void A_non_integer_effort_reads_as_no_estimate_rather_than_throwing(string value)
    {
        // This side is a reader: a value it cannot parse surfaces as "no effort"
        // rather than an exception. The generator is what flags it as an error.
        var meta = KnowledgeMeta.Parse($"""
            status: draft
            effort: {value}
            """);

        Assert.Null(meta.Effort);
    }

    [Fact]
    public void Roadmap_entries_are_plain_strings_and_are_never_read_as_references()
    {
        var meta = KnowledgeMeta.Parse("""
            status: draft
            roadmap: [sync-service, mobile-mvp]
            """);

        Assert.Equal(["sync-service", "mobile-mvp"], meta.Roadmap);
        Assert.Empty(meta.Related);
        Assert.Empty(meta.References);
        Assert.Empty(meta.Extra);
    }

    [Fact]
    public void A_roadmap_block_list_is_read_the_same_as_the_inline_form()
    {
        var meta = KnowledgeMeta.Parse("""
            status: draft
            roadmap:
              - sync-service
              - mobile-mvp
            """);

        Assert.Equal(["sync-service", "mobile-mvp"], meta.Roadmap);
    }

    [Fact]
    public void An_empty_roadmap_reads_as_absent()
    {
        var meta = KnowledgeMeta.Parse("""
            status: draft
            roadmap: []
            """);

        Assert.Empty(meta.Roadmap);
        Assert.Empty(meta.Extra);
    }

    [Fact]
    public void Order_lists_sibling_names_and_stays_as_written()
    {
        // Verbatim from .tech/technology-graph.md.
        var meta = KnowledgeMeta.Parse("""
            status: candidate
            order: ["shared.md", "backend.md", "web.md", "tooling.md"]
            """);

        Assert.Equal(["shared.md", "backend.md", "web.md", "tooling.md"], meta.Order);
        Assert.Empty(meta.References);
    }

    [Fact]
    public void Kind_and_version_are_read_and_a_quoted_version_loses_its_quotes()
    {
        var meta = KnowledgeMeta.Parse("""
            status: adopted
            kind: framework
            version: "10.0"
            """);

        Assert.Equal("framework", meta.Kind);
        Assert.Equal("10.0", meta.Version);
    }

    [Fact]
    public void Keys_are_read_case_insensitively_and_stray_whitespace_is_ignored()
    {
        var meta = KnowledgeMeta.Parse("""

              Status:   active
              Kind:  library

            """);

        Assert.Equal("active", meta.Status);
        Assert.Equal("library", meta.Kind);
    }

    [Fact]
    public void References_run_related_then_depends_on_then_implements_without_repeats()
    {
        var meta = KnowledgeMeta.Parse("""
            status: draft
            related: [.arc42/01-introduction.md]
            depends-on: [.tech/shared.md#markdown, .arc42/01-introduction.md]
            implements: [.domain/backlog/features.md#feature-roadmap-planning]
            """);

        Assert.Equal(
            [
                ".arc42/01-introduction.md",
                ".tech/shared.md#markdown",
                ".domain/backlog/features.md#feature-roadmap-planning"
            ],
            meta.References.Select(reference => reference.Raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  \n")]
    [InlineData("nothing parseable here")]
    public void A_block_that_states_nothing_is_empty(string? body)
    {
        Assert.True(KnowledgeMeta.Parse(body).IsEmpty);
    }

    [Theory]
    [InlineData("meta", true)]
    [InlineData("META", true)]
    [InlineData("  meta  ", true)]
    [InlineData("mermaid", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_a_meta_fence_opens_a_metadata_block(string? language, bool expected)
    {
        Assert.Equal(expected, KnowledgeMeta.IsMetaBlock(language));
    }

    [Fact]
    public void A_fence_that_is_not_meta_parses_to_nothing_even_when_the_body_would_have()
    {
        var body = "status: active";

        Assert.True(KnowledgeMeta.ParseFence("yaml", body).IsEmpty);
        Assert.Equal("active", KnowledgeMeta.ParseFence("meta", body).Status);
    }
}
