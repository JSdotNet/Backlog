namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// How the Domain view's Neighbours tab reads a context's dependency tables
/// (local ADR 0037): each neighbour stands by the way information goes — the
/// pattern's role mark, else its table — a Partnership neighbour stands only below,
/// an <c>(external)</c> row is outside the domain, outside systems sharing a
/// contract are one card, and each card's line is the row's reason cut to a phrase.
/// </summary>
public sealed class DomainNeighboursTests
{
    internal const string Path = ".devbook/domain/tasks/dependencies.md";

    internal const string Dependencies = """
        # Tasks

        ```meta
        type: dependencies
        status: draft
        ```

        > Dependencies this bounded context has.

        ## Outbound dependencies

        | Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
        |---|---|---|---|---|
        | GitHub (external) | ACL | REST call | `.devbook/domain/tasks/domain.md#projection` | Multi-repo tasks project to one GitHub issue per target repo. Status syncs both ways. |
        | [Devbook](../devbook/domain.md#knowledge-note) | Partnership | Id-based cross-link | `.devbook/domain/devbook/domain.md#cross-linking` | Entries embed Knowledge Note content for context. |
        | [Sessions](../sessions/domain.md#session-state) | Customer/Supplier (Tasks = customer) | Port `A \| B` | `x` | A task's work badge colours a linked session by its state. Tasks keeps only the id. |
        | [Productivity](../productivity/domain.md#productivity-ledger) | OHS + Published Language (Tasks = supplier) | Publishes `AIWorkLogged` | `y` | AI-assisted activity on a task is available for analysis. |
        | [Roadmap Planning](../roadmap/domain.md#roadmap-item) | Customer/Supplier (Tasks = customer) | Reads the tag vocabulary | `z` | The tag picker offers every Roadmap Item tag. |

        ## Inbound dependents (known)

        | Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
        |---|---|---|---|---|
        | [Inbox](../inbox/domain.md#inbox-item) | OHS + Published Language (Inbox = supplier) | Publishes `ItemTriaged` | `x` | Relies on Tasks creating a draft task from a triaged item. |
        | [Devbook](../devbook/domain.md#knowledge-note) | Partnership | Bi-directional link by id | `x` | Notes link to tasks and tasks link back. |
        | [Roadmap Planning](../roadmap/domain.md#roadmap-plan) | Partnership | Optional cross-link | `z` | Relies on a task id staying resolvable. |
        | [Productivity](../productivity/domain.md#productivity-ledger) | OHS + Published Language (Tasks = supplier) | Subscribes to `AIWorkLogged` | `y` | Relies on AI-assisted activity evidence. |

        ## Notes

        - Keep `repo_ids` opaque.
        """;

    [Fact]
    public void Each_neighbour_stands_by_the_way_information_goes()
    {
        var neighbours = DomainNeighbours.Read(Dependencies, Path);

        // Inbox supplies Tasks from the inbound table; Tasks supplies Productivity from both.
        Assert.Equal(["GitHub", "Sessions", "Inbox"], neighbours.ListensTo.Select(n => n.Name));
        Assert.Equal(["Productivity"], neighbours.ListenTo.Select(n => n.Name));
        Assert.Equal("AI-assisted activity on a task is available for analysis", neighbours.ListenTo[0].Line);
    }

    [Fact]
    public void A_role_mark_naming_this_context_or_a_conformist_places_the_neighbour_the_other_way()
    {
        const string tables = """
            ## Outbound dependencies

            | Depends on | DDD pattern | Why |
            |---|---|---|
            | [Monitoring](../monitoring/domain.md#signal) | Customer/Supplier (Monitoring = customer) | Monitoring reads our signals. |
            | Shared kernel device identity | Conformist (Sync = conformist) | The service stamps a record. |

            ## Inbound dependents (known)

            | Consumer | DDD pattern | What it relies on |
            |---|---|---|
            | [Tasks](../tasks/domain.md#task) | Customer/Supplier (Sync = supplier) | A task document is stored whole. |
            """;

        var neighbours = DomainNeighbours.Read(tables, ".devbook/domain/sync/context.md");

        Assert.Equal(["Shared kernel device identity"], neighbours.ListensTo.Select(n => n.Name));
        Assert.Equal(["Monitoring", "Tasks"], neighbours.ListenTo.Select(n => n.Name));
    }

    [Fact]
    public void A_card_line_is_the_reasons_first_sentence_without_the_pattern()
    {
        var sessions = DomainNeighbours.Read(Dependencies, Path).ListensTo.Single(n => n.Name == "Sessions");

        Assert.Equal("A task's work badge colours a linked session by its state", sessions.Line);
        Assert.Equal("A task's work badge colours a linked session by its state.", sessions.Sentence);
        Assert.DoesNotContain("Customer", sessions.Line, StringComparison.Ordinal);
        Assert.Equal("sessions", sessions.Context);
        Assert.Equal(".devbook/domain/sessions/context.md", sessions.Reference);
    }

    [Theory]
    [InlineData("Relies on Tasks creating a draft task from a triaged item.", "Relies on Tasks creating a draft task from a triaged item")]
    [InlineData(
        "Multi-repo tasks project to one GitHub issue per target repo; status syncs bidirectionally through an adapter.",
        "Multi-repo tasks project to one GitHub issue per target repo")]
    [InlineData(
        "A task's work badge colours a linked session by its state — stalled highlighted as the state that needs the person.",
        "A task's work badge colours a linked session by its state")]
    [InlineData(
        "Work views can expose quick links to relevant environments without Tasks owning endpoint or launch semantics.",
        "Work views can expose quick links to relevant environments…")]
    public void A_long_reason_is_cut_to_a_phrase_at_a_clause_or_a_word(string sentence, string phrase)
    {
        Assert.Equal(phrase, DomainNeighbours.Phrase(sentence));
        Assert.True(phrase.Length <= DomainNeighbours.PhraseLength);
    }

    [Fact]
    public void Partnership_rows_meet_once_below_and_stand_on_no_side()
    {
        var neighbours = DomainNeighbours.Read(Dependencies, Path);

        Assert.Equal(["Devbook", "Roadmap Planning"], neighbours.HandInHand.Select(n => n.Name));
        Assert.Equal("Entries embed Knowledge Note content for context", neighbours.HandInHand[0].Line);
        Assert.DoesNotContain(neighbours.ListensTo, n => n.Name is "Devbook" or "Roadmap Planning");
        Assert.DoesNotContain(neighbours.ListenTo, n => n.Name is "Devbook" or "Roadmap Planning");
    }

    [Fact]
    public void A_partner_spelled_differently_in_another_row_is_still_only_below()
    {
        const string tables = """
            ## Outbound dependencies

            | Depends on | DDD pattern | Why |
            |---|---|---|
            | [Roadmap Planning](../roadmap/domain.md#roadmap-item) | Customer/Supplier (Tasks = customer) | Reads the tag vocabulary. |

            ## Inbound dependents (known)

            | Consumer | DDD pattern | What it relies on |
            |---|---|---|
            | [Roadmap](../roadmap/domain.md#roadmap-plan) | Partnership | Relies on a task id. |
            """;

        var neighbours = DomainNeighbours.Read(tables, Path);

        Assert.Equal(["Roadmap"], neighbours.HandInHand.Select(n => n.Name));
        Assert.Empty(neighbours.ListensTo);
        Assert.Empty(neighbours.ListenTo);
    }

    [Fact]
    public void An_external_row_is_marked_and_names_no_context()
    {
        var gitHub = DomainNeighbours.Read(Dependencies, Path).ListensTo[0];

        Assert.True(gitHub.External);
        Assert.Equal("GitHub", gitHub.Name);
        Assert.Null(gitHub.Context);
        Assert.Null(gitHub.Reference);
        Assert.Equal("Multi-repo tasks project to one GitHub issue per target repo", gitHub.Line);
    }

    [Fact]
    public void Outside_systems_on_one_side_that_share_a_contract_are_one_card()
    {
        const string tables = """
            ## Outbound dependencies

            | Depends on | DDD pattern | Contract | Why |
            |---|---|---|---|
            | GitHub (external) | ACL (Tasks = supplier) | `#projection` | Tasks project to GitHub issues. |
            | Copilot CLI (external) | ACL (Tasks = supplier) | `#projection` | Tasks project to CLI tasks. |
            | npm (external) | ACL | `#packages` | Packages come from npm. |
            """;

        var neighbours = DomainNeighbours.Read(tables, Path);

        var outside = Assert.Single(neighbours.ListenTo);
        Assert.Equal("GitHub · Copilot CLI", outside.Name);
        Assert.Equal("Tasks project to GitHub issues", outside.Line);
        Assert.True(outside.External);
        Assert.Equal(["npm"], neighbours.ListensTo.Select(n => n.Name));
    }

    [Fact]
    public void Describe_names_a_context_neighbour_by_its_context_and_tags_core()
    {
        var neighbours = DomainNeighbours.Read(Dependencies, Path).Describe(
            new Dictionary<string, DomainNeighbourContext>(StringComparer.OrdinalIgnoreCase)
            {
                ["inbox"] = new("Inbox", true),
                ["productivity"] = new("Productivity & Insight", false),
            });

        Assert.True(neighbours.ListensTo.Single(n => n.Name == "Inbox").Core);
        Assert.False(neighbours.ListensTo.Single(n => n.Name == "GitHub").Core);
        Assert.Equal("Productivity & Insight", Assert.Single(neighbours.ListenTo).Name);
    }

    [Fact]
    public void The_tables_under_a_context_files_Dependencies_chapter_read_the_same()
    {
        const string context = """
            # Sync

            ## Dependencies

            Sync is upstream.

            ### Outbound dependencies

            | Depends on | DDD pattern | Why |
            |---|---|---|
            | Shared kernel device identity | Conformist | The service stamps a session record. More. |

            ### Inbound dependents (known)

            | Consumer | DDD pattern | What it relies on |
            |---|---|---|
            | [Tasks](../tasks/domain.md#task) | OHS | A task document is stored whole. |
            """;

        var neighbours = DomainNeighbours.Read(context, ".domain/sync/context.md");

        var identity = Assert.Single(neighbours.ListensTo);
        Assert.Equal("Shared kernel device identity", identity.Name);
        Assert.True(identity.External);
        Assert.Null(identity.Context);
        Assert.Equal("tasks", Assert.Single(neighbours.ListenTo).Context);
    }

    [Fact]
    public void A_placeholder_row_is_nobody_and_a_neighbour_met_twice_on_one_side_is_one_card()
    {
        const string tables = """
            ## Outbound dependencies

            | Depends on | DDD pattern | Why |
            |---|---|---|
            | [GitHub](https://github.com/README.md) | ACL | Issues live there. |

            ## Inbound dependents (known)

            | Consumer | DDD pattern | What it relies on |
            |---|---|---|
            | None | | The briefs no longer read it. |
            | [Monitoring](../monitoring/domain.md#signal) | OHS | Relies on the first event. |
            | [Monitoring](../monitoring/domain.md#signal) | OHS | Relies on the second event. |
            """;

        var neighbours = DomainNeighbours.Read(tables, Path);

        var monitoring = Assert.Single(neighbours.ListenTo);
        Assert.Equal("Relies on the first event", monitoring.Line);
        var gitHub = Assert.Single(neighbours.ListensTo);
        Assert.Null(gitHub.Context);
        Assert.True(gitHub.External);
    }

    [Fact]
    public void No_tables_is_no_neighbours()
    {
        Assert.True(DomainNeighbours.Read("# Tasks\n\nNothing here.", Path).IsEmpty);
        Assert.True(DomainNeighbours.Read(null, Path).IsEmpty);
    }
}
