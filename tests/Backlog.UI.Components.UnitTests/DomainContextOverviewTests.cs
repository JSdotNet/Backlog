namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// What the Domain view's Overview tab reads about a bounded context (local ADR
/// 0037): the boundary prose above <c>context.md</c>'s first chapter, the chapters
/// per kind across the context's files, how many requirements and rules name a
/// test, the status spread, and the context's switches and settings.
/// </summary>
public sealed class DomainContextOverviewTests
{
    internal const string Context = """
        # Tasks

        ```meta
        status: draft
        index: root
        type: context
        ```

        Tasks maintains a personal backlog of [prompts](../inbox/domain.md#inbox-item).

        Outside it: what arrives.

        ```annotation
        id: n1
        note: Should this say more?
        ```

        ## Task backlog

        ```meta
        type: feature-flag
        key: backlog
        default: on
        ```

        Create, edit and store tasks. It is always enabled.

        ## Week start

        ```meta
        status: draft
        type: setting
        key: week-start
        tests: [unit:dotnet:Some.Tests]
        ```

        The day a week starts on.
        """;

    internal const string Domain = """
        # Domain: Tasks

        ```meta
        type: domain
        status: draft
        ```

        ## Task

        ```meta
        type: aggregate
        status: proposed
        ```

        A refined item.

        ```mermaid
        ## not a heading
        ```

        ## Ubiquitous Language

        ```meta
        type: ubiquitous-language
        ```

        ### Entry

        ```meta
        type: term
        status: draft
        ```

        One line of the backlog.
        """;

    internal const string Requirements = """
        # Requirements: Tasks

        ```meta
        type: requirements
        status: draft
        ```

        ## Task creation

        ```meta
        type: requirements
        status: draft
        ```

        ### Requirement: A task has a title

        ```meta
        type: requirement
        status: draft
        tests:
          - unit:dotnet:Backlog.Tests.TaskTests
        ```

        The system SHALL refuse a task without a title.

        ### Requirement: A task has a status

        ```meta
        type: requirement
        status: deprecated
        ```

        The system SHALL give every task a status.
        """;

    internal const string Invariants = """
        # Invariants: Tasks

        ## A task is never its own parent

        ```meta
        type: invariant
        status: draft
        tests: unit:dotnet:Backlog.Tests.ParentTests
        ```

        No cycle.
        """;

    [Fact]
    public void The_boundary_is_the_prose_above_the_first_chapter_without_its_notes()
    {
        var overview = DomainContextOverview.Read(Context, []);

        Assert.StartsWith("Tasks maintains a personal backlog", overview.Boundary, StringComparison.Ordinal);
        Assert.Contains("Outside it: what arrives.", overview.Boundary, StringComparison.Ordinal);
        Assert.DoesNotContain("Should this say more", overview.Boundary, StringComparison.Ordinal);
        Assert.DoesNotContain("index: root", overview.Boundary, StringComparison.Ordinal);
        Assert.DoesNotContain("Task backlog", overview.Boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void Chapters_are_counted_per_kind_in_plain_words_and_file_types_are_not()
    {
        var overview = DomainContextOverview.Read(Context, [Domain, Requirements, Invariants]);

        Assert.Equal(
            [("Aggregates", 1), ("Terms", 1), ("Requirements", 2), ("Always-true rules", 1)],
            overview.Counts.Select(count => (count.Label, count.Count)));
    }

    [Fact]
    public void Proof_coverage_counts_the_requirements_and_rules_that_name_a_test()
    {
        var overview = DomainContextOverview.Read(Context, [Domain, Requirements, Invariants]);

        Assert.Equal(3, overview.Provable);
        Assert.Equal(2, overview.Proved);
    }

    [Fact]
    public void How_settled_reads_least_settled_first()
    {
        var overview = DomainContextOverview.Read(Context, [Domain, Requirements, Invariants]);

        Assert.Equal(
            [("draft", 4), ("proposed", 1), ("active", 1), ("deprecated", 1)],
            overview.Statuses.Select(status => (status.Status, status.Count)));
    }

    [Fact]
    public void A_chapter_that_leaves_out_its_status_rests_at_active()
    {
        var overview = DomainContextOverview.Read(Context, []);

        Assert.Equal("active", overview.Switches[0].Status);
        Assert.Contains(overview.Statuses, status => status is { Status: "active", Count: 1 });
        Assert.DoesNotContain(overview.Statuses, status => status.Status == "none");
    }

    [Fact]
    public void The_switches_and_settings_are_context_mds_flag_and_setting_chapters()
    {
        var overview = DomainContextOverview.Read(Context, [Domain]);

        Assert.Collection(overview.Switches,
            flag =>
            {
                Assert.Equal("Task backlog", flag.Title);
                Assert.False(flag.IsSetting);
                Assert.Equal("backlog", flag.Key);
                Assert.Equal("on", flag.Default);
                Assert.Equal("active", flag.Status);
                Assert.Equal("Create, edit and store tasks.", flag.Summary);
            },
            setting =>
            {
                Assert.True(setting.IsSetting);
                Assert.Equal("week-start", setting.Key);
                Assert.Null(setting.Default);
            });
    }
}
