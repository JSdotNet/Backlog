using Backlog.UI.Components.Diagrams;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The Model page's Fields reading of a classDiagram: each class that holds state,
/// with its attributes grouped by what their types say — dates and times, ids
/// pointing elsewhere, owned parts, vocabularies, other values.
/// </summary>
public sealed class DomainModelFieldsTests
{
    private const string Tasks = """
        classDiagram
            class Task {
                <<aggregate root>>
                +Id id
                +String title
                +TaskStatus status
                +Timestamp created_at
                +Date due_on
                +List~Id~ depends_on
                +String source_inbox_id
                +Recurrence recurrence
                +List~Weekday~ days
                +Integer effort
            }
            class SubItem {
                <<entity>>
                +String title
            }
            class Recurrence {
                <<value object>>
                +Integer interval
            }
            class TaskStatus {
                <<enumeration>>
                draft
                done
            }
            class Weekday {
                <<enumeration>>
                monday
            }
            class Projection {
                <<service>>
                +void project(Task)
            }
            Task "1" *-- "0..*" SubItem : contains
            Task "1" *-- "0..1" Recurrence : repeats by
        """;

    [Fact]
    public void Attributes_are_grouped_by_kind_in_the_tab_order()
    {
        var task = Assert.Single(DomainModelFields.TryRead(Tasks)!, c => c.Name == "Task");

        Assert.Equal(
            [DomainFieldKind.DateTime, DomainFieldKind.Reference, DomainFieldKind.OwnedPart, DomainFieldKind.Vocabulary, DomainFieldKind.Value],
            task.Groups.Select(g => g.Kind));
        Assert.Equal(["created_at", "due_on"], Names(task, DomainFieldKind.DateTime));
        Assert.Equal(["depends_on", "source_inbox_id"], Names(task, DomainFieldKind.Reference));
        Assert.Equal(["recurrence", "contains"], Names(task, DomainFieldKind.OwnedPart));
        Assert.Equal(["status", "days"], Names(task, DomainFieldKind.Vocabulary));
        Assert.Equal(["id", "title", "effort"], Names(task, DomainFieldKind.Value));
    }

    [Fact]
    public void A_vocabulary_names_its_values_and_a_part_its_multiplicity()
    {
        var task = Assert.Single(DomainModelFields.TryRead(Tasks)!, c => c.Name == "Task");

        var status = Field(task, "status");
        Assert.Equal("draft, done", status.Detail);

        var recurrence = Field(task, "recurrence");
        Assert.Equal("0..1", recurrence.Detail);

        // A part composed in with no attribute naming it is listed by its line.
        var contains = Field(task, "contains");
        Assert.Equal("SubItem", contains.Type);
        Assert.Equal("0..*", contains.Detail);

        Assert.Equal("List<Id>", Field(task, "depends_on").Type);
    }

    [Fact]
    public void Enumerations_and_services_are_not_listed_as_classes()
    {
        var classes = DomainModelFields.TryRead(Tasks)!;

        Assert.Equal(["Task", "SubItem", "Recurrence"], classes.Select(c => c.Name));
    }

    [Fact]
    public void An_id_written_as_its_type_points_elsewhere()
    {
        var goal = Assert.Single(DomainModelFields.TryRead("""
            classDiagram
                class Goal {
                    <<aggregate root>>
                    +Id id
                    +TaskId Task
                    +List~OwnerId~ owners
                }
            """)!);

        Assert.Equal([DomainFieldKind.Reference, DomainFieldKind.Value], goal.Groups.Select(g => g.Kind));
        Assert.Equal(["Task", "owners"], Names(goal, DomainFieldKind.Reference));
        Assert.Equal(["id"], Names(goal, DomainFieldKind.Value));
    }

    [Fact]
    public void A_fence_the_reader_cannot_read_reads_as_nothing()
    {
        Assert.Null(DomainModelFields.TryRead("classDiagram\n    Task <--> Other"));
        Assert.Null(DomainModelFields.TryRead("flowchart LR\n  A --> B"));
    }

    private static IEnumerable<string> Names(DomainFieldsClass element, DomainFieldKind kind) =>
        element.Groups.Single(g => g.Kind == kind).Fields.Select(f => f.Name);

    private static DomainField Field(DomainFieldsClass element, string name) =>
        element.Groups.SelectMany(g => g.Fields).Single(f => f.Name == name);
}
