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
    public void Domain_and_feature_chapters_are_counted_per_kind_in_plain_words_and_file_types_are_not()
    {
        var overview = DomainContextOverview.Read(Context, [Domain, Requirements, Invariants]);

        Assert.Equal(
            [("Aggregate", 1), ("Term", 1)],
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

    internal const string Root = ".devbook/domain/tasks/context.md";

    internal const string Boundary = """
        # Tasks

        ```meta
        status: draft
        type: context
        deployment: module
        ```

        Tasks keeps a personal backlog. It turns triaged [Inbox Items](../inbox/domain.md#inbox-item) into tasks.

        Inside the boundary: a task's status and its priority.

        Outside it: what arrives and how it is triaged, which the
        [Inbox](../inbox/domain.md#inbox-item) answers; when planned work happens,
        which [Roadmap Planning](../roadmap/domain.md#roadmap-item) answers; and the
        knowledge a task links to, which [Devbook](../devbook/domain.md#knowledge-note)
        answers. The model itself is in [domain.md](domain.md).

        ## GitHub integration

        ```meta
        status: draft
        type: feature-flag
        key: github-integration
        default: on
        related: [".devbook/domain/tasks/features.md#issue-projection", ".devbook/domain/tasks/domain.md#task"]
        ```

        Turning it on offers GitHub access. More words.

        ## Clear My Day overnight

        ```meta
        status: draft
        type: setting
        key: my-day-preferences.json
        scope: user
        default: on
        related: [features.md#my-day]
        ```

        Whether today's My Day starts empty.
        """;

    internal const string Features = """
        # Features: Tasks

        ## Issue projection

        ```meta
        type: feature
        status: draft
        ```

        A task becomes an issue.

        ### Hand-off

        ```meta
        type: sub-feature
        ```

        Hand it on.

        ## My Day

        ```meta
        type: feature
        status: proposed
        ```

        Today's list.
        """;

    internal const string Domain2 = """
        # Domain: Tasks

        ## Task

        ```meta
        type: aggregate
        status: draft
        ```

        A refined item.

        ### Sub-Item

        ```meta
        type: entity
        ```

        A step.

        ### Priority

        ```meta
        type: value-object
        ```

        How urgent.

        ## Ubiquitous Language

        ```meta
        type: ubiquitous-language
        ```

        ### Entry

        ```meta
        type: term
        ```

        One line.
        """;

    internal const string Rules = """
        # Invariants: Tasks

        ## A task is never its own parent

        ```meta
        type: invariant
        tests: [unit:dotnet:Backlog.Tests.ParentTests]
        ```

        No cycle.

        Enforced at: constructor

        ## A task keeps its creation time

        ```meta
        type: invariant
        ```

        Never moves.

        Enforced at: open
        """;

    internal static IReadOnlyList<DomainContextFile> Files =>
    [
        new(".devbook/domain/tasks/domain.md", Domain2),
        new(".devbook/domain/tasks/domain.invariants.md", Rules),
        new(".devbook/domain/tasks/features.md", Features),
        new(".devbook/domain/tasks/requirements.md", Requirements)
    ];

    [Fact]
    public void The_boundary_reads_as_its_lead_what_is_inside_and_each_question_answered_outside()
    {
        var overview = DomainContextOverview.Read(Boundary, Root, Files);

        Assert.StartsWith("Tasks keeps a personal backlog.", overview.Lead, StringComparison.Ordinal);
        Assert.DoesNotContain("Inside the boundary", overview.Lead, StringComparison.Ordinal);
        Assert.Equal("a task's status and its priority.", overview.Inside);
        Assert.Equal(
            [
                ("what arrives and how it is triaged", "Inbox", ".devbook/domain/inbox/domain.md#inbox-item"),
                ("when planned work happens", "Roadmap Planning", ".devbook/domain/roadmap/domain.md#roadmap-item"),
                ("the knowledge a task links to", "Devbook", ".devbook/domain/devbook/domain.md#knowledge-note")
            ],
            overview.Outside.Select(question => (question.Question, question.Answers.Single().Title, question.Answers.Single().Reference)));
        Assert.Equal("The model itself is in [domain.md](domain.md).", overview.OutsideNote);
    }

    [Fact]
    public void An_outside_list_reads_one_question_per_item_and_an_item_nobody_answers_has_no_link()
    {
        const string context = """
            # Devbook

            Devbook is a knowledge base.

            Outside it:

            - a repository's folders themselves, which the repository owns;
            - the work items a chapter links to, which [Tasks](../tasks/domain.md#task) answers.
            """;

        var overview = DomainContextOverview.Read(context, ".devbook/domain/devbook/context.md", []);

        Assert.Equal(
            [("a repository's folders themselves, which the repository owns", 0), ("the work items a chapter links to", 1)],
            overview.Outside.Select(question => (question.Question, question.Answers.Count)));
        Assert.Equal(string.Empty, overview.OutsideNote);
    }

    [Fact]
    public void An_outside_paragraph_that_names_no_answer_is_kept_whole()
    {
        const string context = """
            # Monitoring

            Monitoring tracks progress.

            Outside it: every piece of state it shows, which stays with the context that reports it.
            """;

        var overview = DomainContextOverview.Read(context, ".devbook/domain/monitoring/context.md", []);

        Assert.Empty(overview.Outside);
        Assert.Equal("every piece of state it shows, which stays with the context that reports it.", overview.OutsideNote);
    }

    [Fact]
    public void Two_answered_clauses_joined_by_a_comma_are_two_questions()
    {
        const string context = """
            # Repository Management

            Repository Management keeps a registry.

            Outside it: what a baseline says and which versions are approved, which
            [Technology Stack](../technology-stack/domain.md#technology-registry) answers, and the work items a finding turns into, which
            [Tasks](../tasks/domain.md#task) answers.
            """;

        var overview = DomainContextOverview.Read(context, ".devbook/domain/repository-management/context.md", []);

        Assert.Equal(
            [("what a baseline says and which versions are approved", "Technology Stack"), ("the work items a finding turns into", "Tasks")],
            overview.Outside.Select(question => (question.Question, question.Answers.Single().Title)));
    }

    [Fact]
    public void Paragraphs_after_the_inside_and_outside_cards_are_neither_cards()
    {
        const string context = """
            # Progress

            Progress owns goals.

            Inside the boundary:

            - each goal;
            - each impediment.

            Outside it: the task itself, which [Tasks](../tasks/domain.md#task) answers.

            Progress used to lay sprint series.
            """;

        var overview = DomainContextOverview.Read(context, ".devbook/domain/progress/context.md", []);

        Assert.Equal("- each goal;\n- each impediment.", overview.Inside);
        Assert.Equal(["the task itself"], overview.Outside.Select(question => question.Question));
        Assert.Equal(string.Empty, overview.OutsideNote);
        Assert.Equal("Progress used to lay sprint series.", overview.More);
    }

    [Fact]
    public void A_boundary_with_neither_inside_nor_outside_is_all_lead()
    {
        var overview = DomainContextOverview.Read("# Sessions\n\nOne.\n\nTwo.\n", ".devbook/domain/sessions/context.md", []);

        Assert.Equal("One.\n\nTwo.", overview.Lead);
        Assert.Equal(string.Empty, overview.Inside);
        Assert.Empty(overview.Outside);
    }

    [Fact]
    public void The_header_reads_the_context_mds_own_status_and_deployment()
    {
        var overview = DomainContextOverview.Read(Boundary, Root, []);

        Assert.Equal("draft", overview.Status);
        Assert.Equal("module", overview.Deployment);
        Assert.Null(overview.Subdomain);
    }

    [Fact]
    public void Each_tile_leads_to_the_unit_its_first_chapter_sits_in()
    {
        var overview = DomainContextOverview.Read(Boundary, Root, Files);

        Assert.Equal(
            [
                ("aggregate", "Aggregate", 1, ".devbook/domain/tasks/domain.md#task"),
                ("entity", "Entity", 1, ".devbook/domain/tasks/domain.md#task"),
                ("value-object", "Value object", 1, ".devbook/domain/tasks/domain.md#task"),
                ("term", "Term", 1, ".devbook/domain/tasks/domain.md#ubiquitous-language"),
                ("feature", "Features", 2, ".devbook/domain/tasks/features.md#issue-projection")
            ],
            overview.Counts.Select(count => (count.Type, count.Label, count.Count, count.Reference)));
    }

    [Fact]
    public void Proof_reads_requirements_with_their_scenarios_and_invariants_with_those_still_open()
    {
        var overview = DomainContextOverview.Read(Boundary, Root, Files);

        Assert.Equal(new DomainProof(2, 1, 0, ".devbook/domain/tasks/requirements.md"), overview.Requirements);
        Assert.Equal(new DomainProof(2, 1, 1, ".devbook/domain/tasks/domain.md#task"), overview.Invariants);
        Assert.Equal(4, overview.Provable);
        Assert.Equal(2, overview.Proved);
    }

    [Fact]
    public void Requirement_scenarios_are_counted_by_their_headings()
    {
        const string requirements = """
            # Requirements

            ### Requirement: One

            ```meta
            type: requirement
            ```

            The system SHALL.

            #### Scenario: First

            #### Scenario: Second
            """;

        var overview = DomainContextOverview.Read(Boundary, Root, [new(".devbook/domain/tasks/requirements.md", requirements)]);

        Assert.Equal(2, overview.Requirements.Extra);
    }

    [Fact]
    public void A_switch_gates_the_features_its_related_names_and_a_setting_says_its_scope()
    {
        var overview = DomainContextOverview.Read(Boundary, Root, Files);

        Assert.Collection(overview.Switches,
            flag =>
            {
                Assert.Null(flag.Scope);
                Assert.Equal("Turning it on offers GitHub access.", flag.Summary);
                Assert.Equal([new DomainOverviewLink("Issue projection", ".devbook/domain/tasks/features.md#issue-projection")], flag.Gates);
            },
            setting =>
            {
                Assert.Equal("user", setting.Scope);
                Assert.Equal([new DomainOverviewLink("My Day", ".devbook/domain/tasks/features.md#my-day")], setting.Gates);
            });
    }

    [Fact]
    public void The_lenses_are_the_pages_the_context_has()
    {
        var overview = DomainContextOverview.Read(Boundary, Root, Files);

        Assert.Equal(
            [
                ("Features", ".devbook/domain/tasks/features.md"),
                ("Requirements", ".devbook/domain/tasks/requirements.md"),
                ("Glossary", ".devbook/domain/tasks/domain.md#ubiquitous-language")
            ],
            overview.Lenses.Select(lens => (lens.Title, lens.Reference)));
        Assert.All(overview.Lenses, lens => Assert.EndsWith("?", lens.Question, StringComparison.Ordinal));
    }

    [Fact]
    public void The_brain_box_covers_context_md_and_its_chapters()
    {
        var overview = DomainContextOverview.Read(Boundary, Root, []);

        Assert.Equal(
            [Root, $"{Root}#github-integration", $"{Root}#clear-my-day-overnight"],
            overview.Chapters);
    }

    [Fact]
    public void Read_without_paths_links_nothing()
    {
        var overview = DomainContextOverview.Read(Boundary, Files.Select(file => file.Text));

        Assert.All(overview.Counts, count => Assert.Null(count.Reference));
        Assert.Null(overview.Requirements.Reference);
        Assert.Empty(overview.Lenses);
        Assert.Empty(overview.Chapters);
        Assert.All(overview.Switches, item => Assert.Empty(item.Gates));
    }
}
