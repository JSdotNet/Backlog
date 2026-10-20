namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// How the Domain view's Neighbours tab reads a context's dependency tables
/// (local ADR 0036): outbound rows listen to, inbound rows listen to the context,
/// Partnership rows meet once below, an <c>(external)</c> row is outside the
/// domain, and each card's line is the row's reason cut to its first sentence.
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

        ## Inbound dependents (known)

        | Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
        |---|---|---|---|---|
        | [Inbox](../inbox/domain.md#inbox-item) | OHS + Published Language (Inbox = supplier) | Publishes `ItemTriaged` | `x` | Relies on Tasks creating a draft task from a triaged item. |
        | [Devbook](../devbook/domain.md#knowledge-note) | Partnership | Bi-directional link by id | `x` | Notes link to tasks and tasks link back. |

        ## Notes

        - Keep `repo_ids` opaque.
        """;

    [Fact]
    public void Outbound_rows_are_listened_to_and_inbound_rows_listen()
    {
        var neighbours = DomainNeighbours.Read(Dependencies, Path);

        Assert.Equal(["GitHub", "Sessions"], neighbours.ListensTo.Select(n => n.Name));
        Assert.Equal(["Inbox"], neighbours.ListenTo.Select(n => n.Name));
        Assert.Equal("Relies on Tasks creating a draft task from a triaged item.", neighbours.ListenTo[0].Line);
    }

    [Fact]
    public void A_card_line_is_the_reasons_first_sentence_without_the_pattern()
    {
        var sessions = DomainNeighbours.Read(Dependencies, Path).ListensTo.Single(n => n.Name == "Sessions");

        Assert.Equal("A task's work badge colours a linked session by its state.", sessions.Line);
        Assert.DoesNotContain("Customer", sessions.Line, StringComparison.Ordinal);
        Assert.Equal("sessions", sessions.Context);
        Assert.Equal(".devbook/domain/sessions/context.md", sessions.Reference);
    }

    [Fact]
    public void Partnership_rows_meet_once_below_with_the_outbound_reason()
    {
        var neighbours = DomainNeighbours.Read(Dependencies, Path);

        var devbook = Assert.Single(neighbours.HandInHand);
        Assert.Equal("Devbook", devbook.Name);
        Assert.Equal("Entries embed Knowledge Note content for context.", devbook.Line);
        Assert.DoesNotContain(neighbours.ListensTo, n => n.Name == "Devbook");
        Assert.DoesNotContain(neighbours.ListenTo, n => n.Name == "Devbook");
    }

    [Fact]
    public void An_external_row_is_marked_and_names_no_context()
    {
        var gitHub = DomainNeighbours.Read(Dependencies, Path).ListensTo[0];

        Assert.True(gitHub.External);
        Assert.Equal("GitHub", gitHub.Name);
        Assert.Null(gitHub.Context);
        Assert.Null(gitHub.Reference);
        Assert.Equal("Multi-repo tasks project to one GitHub issue per target repo.", gitHub.Line);
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
        Assert.Equal("Relies on the first event.", monitoring.Line);
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
