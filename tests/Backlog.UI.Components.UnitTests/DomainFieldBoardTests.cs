using Backlog.UI.Components.Devbook;
using Backlog.UI.Components.Diagrams;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The Model page's Fields and Events readings out of the chapters as written: a
/// model file's Fields chapter, the invariants a bullet counts, and what a
/// context listens to from its dependency tables.
/// </summary>
public sealed class DomainFieldBoardTests
{
    [Fact]
    public void A_fields_chapter_reads_into_purpose_groups_with_their_question_meanings_and_rule_links()
    {
        const string markdown = """
            # Tasks

            ## Model diagram

            ## Fields

            ### Work state

            Where is the work?

            - `status` — The recorded lifecycle state. Moves only along
              [the lifecycle](flow.md#task-lifecycle). Rules: [draft](domain.invariants.md#a), [moves](domain.invariants.md#b).
            - `area` · `order` - The person's own grouping and rank.
            - A bullet that names no field is prose.

            ## Relationship notes

            - `status` — not a field bullet here.
            """;

        var chapter = Assert.Single(DomainFieldPurposes.Read(markdown));
        Assert.Null(chapter.Root);
        var purpose = Assert.Single(chapter.Purposes);
        Assert.Equal("Work state", purpose.Title);
        Assert.Equal("Where is the work?", purpose.Question);
        Assert.Equal(2, purpose.Entries.Count);

        var status = purpose.Entries[0];
        Assert.Equal(["status"], status.Names);
        Assert.Equal("The recorded lifecycle state. Moves only along [the lifecycle](flow.md#task-lifecycle).", status.Meaning);
        Assert.Equal(["domain.invariants.md#a", "domain.invariants.md#b"], status.Rules);

        Assert.Equal(["area", "order"], purpose.Entries[1].Names);
        Assert.Empty(purpose.Entries[1].Rules);
    }

    [Fact]
    public void A_fields_chapter_names_its_root_when_the_model_holds_several()
    {
        const string markdown = """
            ## Order fields

            ### State

            - `status` — Where the order is.

            ## Fields

            ### Other

            - `id` — Its identity.
            """;

        var chapters = DomainFieldPurposes.Read(markdown);

        Assert.Equal("Order", DomainFieldPurposes.For(chapters, "Order")!.Root);
        Assert.Null(DomainFieldPurposes.For(chapters, "Invoice")!.Root);
    }

    [Fact]
    public void The_rule_index_counts_the_invariants_under_each_heading()
    {
        const string invariants = """
            # Invariants

            ## Task

            ```meta
            type: invariants
            ```

            ### How a task moves

            #### Invariant: A new task starts in draft

            ```meta
            type: invariant
            ```

            Text.

            #### Invariant: Status changes follow the lifecycle

            ```meta
            type: invariant
            ```

            Text.
            """;

        var index = DomainFieldBoards.RuleIndex([DomainUnits.Parse(invariants, ".devbook/domain/tasks/domain.invariants.md")]);

        Assert.Equal(["A new task starts in draft", "Status changes follow the lifecycle"], index["domain.invariants.md#how-a-task-moves"]);
        Assert.Equal(["A new task starts in draft"], index["domain.invariants.md#invariant-a-new-task-starts-in-draft"]);
        Assert.Equal("domain.invariants.md#how-a-task-moves", DomainFieldBoards.RuleKey("../tasks/Domain.Invariants.md#How-A-Task-Moves"));
    }

    [Fact]
    public void A_field_type_says_the_kind_of_time_an_id_or_the_models_type()
    {
        var diagram = DomainClassDiagramReader.TryRead("""
            classDiagram
                class Task {
                    <<aggregate root>>
                    +Date due_on
                    +LocalDateTime remind_at
                    +Timestamp updated_at
                    +List~String~ repo_ids
                    +TaskStatus status
                    +Recurrence recurrence
                    +Integer effort
                }
                class Recurrence {
                    <<value object>>
                    +Integer interval
                }
                class TaskStatus {
                    <<enumeration>>
                    draft
                }
                Task "1" *-- "0..1" Recurrence : repeats by
            """)!;
        var root = diagram.Find("Task")!;

        DomainFieldType? Type(string name) => DomainFieldBoards.TypeOf(diagram, root.Attributes.Single(a => a.Name == name));

        Assert.Equal(new DomainFieldType("date", DomainFieldTone.Date), Type("due_on"));
        Assert.Equal(new DomainFieldType("local time", DomainFieldTone.LocalTime), Type("remind_at"));
        Assert.Equal(new DomainFieldType("instant", DomainFieldTone.Instant), Type("updated_at"));
        Assert.Equal(new DomainFieldType("id", DomainFieldTone.Id), Type("repo_ids"));
        Assert.Equal(new DomainFieldType("Task Status", DomainFieldTone.Enumeration), Type("status"));
        Assert.Equal(new DomainFieldType("Recurrence", DomainFieldTone.ValueObject), Type("recurrence"));
        Assert.Equal(new DomainFieldType("number", DomainFieldTone.Plain), Type("effort"));
    }
}
