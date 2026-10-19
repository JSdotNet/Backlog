using Backlog.UI.Components.Diagrams;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A bounded context's <c>classDiagram</c>, read and arranged the way the Domain
/// view's Model page draws it (local ADR 0036): the aggregate root in its
/// consistency-boundary band with the parts composed into it, what it holds by
/// identity outside the band, the domain services below — and nothing at all when
/// the fence is not one the reader understands in full, so the page falls back to
/// mermaid rather than drawing a picture with a class missing.
/// </summary>
public sealed class DomainModelArrangementTests
{
    /// <summary>The shape of <c>.devbook/domain/tasks/model.md</c>, cut down.</summary>
    private const string Tasks = """
        classDiagram
            class Task {
                <<aggregate root>>
                +Id id
                +String title
                +List~String~ repo_ids
                +String source_inbox_id
                +List~Id~ depends_on
                +TaskStatus status
            }
            class SubItem {
                <<entity>>
                +Id id
                +String title
            }
            class Recurrence {
                <<value object>>
                +Integer interval
                +RecurrenceUnit unit
            }
            class RecurrenceUnit {
                <<enumeration>>
                day
                week
            }
            class TaskStatus {
                <<enumeration>>
                draft
                done
            }
            class Readiness {
                <<enumeration>>
                ready
            }
            class PlanSequencing {
                <<service>>
                +List~Task~ order(List~Task~)
            }

            %% the parts
            Task "1" *-- "0..*" SubItem : contains
            Task "1" *-- "0..1" Recurrence : repeats by
            Recurrence --> RecurrenceUnit : counted in
            Task --> TaskStatus : has status
            Task "0..*" ..> "0..*" Task : depends on
            Task ..> Readiness : derived as
        """;

    [Fact]
    public void The_reader_takes_classes_stereotypes_members_and_relations()
    {
        var diagram = DomainClassDiagramReader.TryRead(Tasks);

        Assert.NotNull(diagram);
        var task = diagram.Find("Task")!;
        Assert.Equal(DomainModelKind.AggregateRoot, task.Kind);
        Assert.Contains(task.Attributes, a => a is { Name: "depends_on", Type: "List~Id~" });
        Assert.Equal(DomainModelKind.Enumeration, diagram.Find("RecurrenceUnit")!.Kind);
        Assert.Equal(["day", "week"], diagram.Find("RecurrenceUnit")!.Attributes.Select(a => a.Name));
        Assert.Equal(DomainModelKind.Service, diagram.Find("PlanSequencing")!.Kind);
        Assert.Single(diagram.Find("PlanSequencing")!.Operations);

        var contains = Assert.Single(diagram.Relations, r => r.Label == "contains");
        Assert.Equal(("Task", "SubItem", DomainModelRelationKind.Composition, "1", "0..*"), (contains.From, contains.To, contains.Kind, contains.FromMultiplicity, contains.ToMultiplicity));
    }

    [Fact]
    public void A_composition_written_from_the_part_reads_the_same()
    {
        var diagram = DomainClassDiagramReader.TryRead("""
            classDiagram
                class Order {
                    <<aggregate root>>
                }
                OrderLine "1..*" --* "1" Order : lines
            """);

        var relation = Assert.Single(diagram!.Relations);
        Assert.Equal(("Order", "OrderLine", "1", "1..*"), (relation.From, relation.To, relation.FromMultiplicity, relation.ToMultiplicity));
    }

    [Fact]
    public void The_root_sits_in_its_band_with_its_composed_parts_and_their_multiplicity()
    {
        var arrangement = DomainModelArrangement.TryArrange(Tasks);

        var boundary = Assert.Single(arrangement!.Boundaries);
        Assert.Equal("Task", boundary.Root.Name);
        Assert.Equal(
            [("SubItem", "Task", "0..*", "contains", 1), ("Recurrence", "Task", "0..1", "repeats by", 1)],
            boundary.Parts.Select(p => (p.Class.Name, p.Owner, p.Multiplicity, p.Label, p.Depth)));
    }

    [Fact]
    public void Dashed_lines_and_id_attributes_are_references_outside_the_band()
    {
        var boundary = Assert.Single(DomainModelArrangement.TryArrange(Tasks)!.Boundaries);

        Assert.Equal(
            [
                ("Task", DomainModelReferenceSource.Relation, "depends on"),
                ("Readiness", DomainModelReferenceSource.Relation, "derived as"),
                ("repo_ids", DomainModelReferenceSource.Attribute, null),
                ("source_inbox_id", DomainModelReferenceSource.Attribute, null),
                ("depends_on", DomainModelReferenceSource.Attribute, null)
            ],
            boundary.References.Select(r => (r.Target, r.Source, r.Label)));

        Assert.False(new DomainModelAttribute("cells", "List~Grid~").IsIdReference);
        Assert.True(new DomainModelAttribute("owners", "List~UserId~").IsIdReference);

        // The class's own identity is not a reference to anything.
        Assert.DoesNotContain(boundary.References, r => r.Target == "id");
        Assert.DoesNotContain(boundary.Parts, p => p.Class.Name is "Readiness" or "TaskStatus");
    }

    [Fact]
    public void Domain_services_sit_below_and_what_has_no_place_is_named()
    {
        var arrangement = DomainModelArrangement.TryArrange(Tasks)!;

        Assert.Equal(["PlanSequencing"], arrangement.Services.Select(s => s.Name));
        Assert.Equal(["RecurrenceUnit", "TaskStatus"], arrangement.Unplaced.Select(c => c.Name));
    }

    [Fact]
    public void A_part_of_a_part_stays_in_the_band_one_level_deeper()
    {
        var arrangement = DomainModelArrangement.TryArrange("""
            classDiagram
                class Order {
                    <<aggregate root>>
                }
                class OrderLine {
                    <<entity>>
                }
                class Money {
                    <<value object>>
                }
                Order *-- "1..*" OrderLine
                OrderLine *-- "1" Money : priced in
            """);

        var boundary = Assert.Single(arrangement!.Boundaries);
        Assert.Equal([("OrderLine", "Order", 1), ("Money", "OrderLine", 2)], boundary.Parts.Select(p => (p.Class.Name, p.Owner, p.Depth)));
    }

    [Fact]
    public void Each_aggregate_root_gets_its_own_band_and_never_sits_in_another()
    {
        var arrangement = DomainModelArrangement.TryArrange("""
            classDiagram
                class InboxItem {
                    <<aggregate root>>
                }
                class InboxList {
                    <<aggregate root>>
                }
                InboxList *-- InboxItem : holds
            """);

        Assert.Equal(["InboxItem", "InboxList"], arrangement!.Boundaries.Select(b => b.Root.Name));
        Assert.All(arrangement.Boundaries, b => Assert.Empty(b.Parts));
    }

    [Theory]
    [InlineData("flowchart LR\n    a --> b")]
    [InlineData("classDiagram\n    class Task {\n        <<aggregate root>>\n")]
    [InlineData("classDiagram\n    class Task {\n        <<aggregate root>>\n    }\n    Task <--> Other")]
    [InlineData("classDiagram\n    class Task {\n        <<aggregate root>>\n    }\n    this is not mermaid")]
    [InlineData("classDiagram\n    class Session {\n        <<value object>>\n    }")]
    [InlineData("")]
    [InlineData("classDiagram\n    class Task {\n        <<aggregate root>>\n    }\n    Task --order Other")]
    public void A_fence_that_does_not_parse_or_has_no_root_is_not_arranged(string source)
    {
        Assert.Null(DomainModelArrangement.TryArrange(source));
    }

    /// <summary>The shape of the second diagram in <c>.devbook/domain/devbook/model.md</c>:
    /// a root with no parts, beside a value object with parts of its own that no
    /// root composes. Bands would draw the root alone and lose that structure.</summary>
    [Fact]
    public void A_class_with_structure_no_band_shows_sends_the_fence_to_mermaid()
    {
        Assert.Null(DomainModelArrangement.TryArrange("""
            classDiagram
                class ChapterMetadata {
                    <<value object>>
                }
                class DecisionState {
                    <<value object>>
                }
                class Remark {
                    <<aggregate root>>
                }
                ChapterMetadata "1" *-- "0..1" DecisionState : domain chapters only
            """));
    }

    [Fact]
    public void A_mermaid_generic_reads_as_a_generic()
    {
        Assert.Equal("List<Id>", DomainModelArrangement.TypeLabel("List~Id~"));
    }

    [Fact]
    public void Styling_directives_and_comments_do_not_stop_the_read()
    {
        var arrangement = DomainModelArrangement.TryArrange("""
            classDiagram
                direction LR
                %% a comment
                note for Task "the root"
                class Task {
                    <<aggregate root>>
                }
                classDef hot fill:#f00
                cssClass "Task" hot
            """);

        Assert.Equal("Task", Assert.Single(arrangement!.Boundaries).Root.Name);
    }

    [Fact]
    public void Every_model_in_this_repository_either_arranges_or_falls_back_without_throwing()
    {
        var domain = new DirectoryInfo(Path.Combine(RepositoryRoot(), ".devbook", "domain"));
        var fences = domain.EnumerateFiles("model*.md", SearchOption.AllDirectories)
            .SelectMany(file => ClassDiagramFences(File.ReadAllText(file.FullName)))
            .ToList();

        Assert.NotEmpty(fences);

        // Every class diagram the repository writes is read: none is handed to
        // mermaid for a line this reader does not know. The ones with no
        // aggregate root (roadmap, sessions, sync, progress) do fall back.
        Assert.All(fences, fence => Assert.NotNull(DomainClassDiagramReader.TryRead(fence)));
        Assert.Contains(fences, fence => DomainModelArrangement.TryArrange(fence) is not null);
    }

    private static IEnumerable<string> ClassDiagramFences(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() != "```mermaid") continue;
            var body = new List<string>();
            for (i++; i < lines.Length && lines[i].Trim() != "```"; i++) body.Add(lines[i]);
            if (body.FirstOrDefault(l => l.Trim().Length > 0)?.Trim() == "classDiagram") yield return string.Join('\n', body);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Backlog.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("No Backlog.sln above the test output.");
    }
}
