using Backlog.UI.Components.Diagrams;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A bounded context's <c>classDiagram</c>, read and arranged the way the Domain
/// view's Model page draws it (local ADR 0037): the aggregate root in its
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
    public void Each_type_the_root_names_by_id_sits_outside_the_band_once()
    {
        var boundary = Assert.Single(DomainModelArrangement.TryArrange(Tasks)!.Boundaries);

        // depends_on is the attribute the dashed "depends on" line draws: one
        // reference, not two. Readiness is an enumeration the task is derived as,
        // not something it names by id. With no chapters to read, an id's target
        // is its name in words.
        Assert.Equal(
            [
                ("Task", "0..*", new[] { "depends_on" }),
                ("Repo", "0..*", new[] { "repo_ids" }),
                ("Source inbox", "0..1", new[] { "source_inbox_id" })
            ],
            boundary.References.Select(r => (r.Target, r.Multiplicity, r.Via.ToArray())));

        Assert.False(new DomainModelAttribute("cells", "List~Grid~").IsIdReference);
        Assert.True(new DomainModelAttribute("owners", "List~UserId~").IsIdReference);

        // The class's own identity is not a reference to anything.
        Assert.DoesNotContain(boundary.References, r => r.Via.Contains("id"));
        Assert.DoesNotContain(boundary.References, r => r.Target == "Readiness");
        Assert.DoesNotContain(boundary.Parts, p => p.Class.Name is "Readiness" or "TaskStatus");
    }

    [Fact]
    public void A_part_s_own_ids_are_the_part_s_detail_not_the_aggregate_s_references()
    {
        var boundary = Assert.Single(DomainModelArrangement.TryArrange("""
            classDiagram
                class Task {
                    <<aggregate root>>
                    +List~String~ repo_ids
                }
                class ProjectionRef {
                    <<value object>>
                    +String repo_id
                    +String external_id
                }
                Task "1" *-- "0..*" ProjectionRef : projects to
            """)!.Boundaries);

        Assert.Equal(["repo_ids"], boundary.References.SelectMany(r => r.Via));
    }

    [Fact]
    public void An_id_points_at_the_chapter_whose_alias_or_heading_names_it()
    {
        DomainModelIdentity[] identities =
        [
            new("Inbox Group", "aggregate", [], "inbox/domain.md#inbox-group"),
            new("Repository", "entity", ["repo_id", "repo_ids"], "repository-management/domain.md#repository"),
            new("Inbox Item", "aggregate", ["InboxItem"], "inbox/domain.md#inbox-item"),
            new("Source Ref", "value-object", [], "tasks/domain.md#source-ref")
        ];

        var boundary = Assert.Single(DomainModelArrangement.TryArrange(Tasks, identities)!.Boundaries);

        Assert.Equal(
            [
                ("Task", null),
                ("Repository", "repository-management/domain.md#repository"),
                ("Inbox Group", "inbox/domain.md#inbox-group")
            ],
            boundary.References.Select(r => (r.Target, r.Chapter)));

        // The order the identities come in decides between two headings that both
        // fit: the caller puts the chapters the root relates to first.
        var preferred = Assert.Single(DomainModelArrangement.TryArrange(Tasks, [identities[2], identities[0], identities[1]])!.Boundaries);
        Assert.Equal("Inbox Item", preferred.References[2].Target);

        // Only an aggregate or an entity is something an id points at: a value
        // object has no identity, so "source" does not resolve to Source Ref.
        Assert.Equal(
            "Recurrence source",
            DomainModelArrangement.IdentityOf("recurrence_source_id", null, identities).Title);
    }

    [Fact]
    public void Two_ids_that_point_at_one_type_share_its_card()
    {
        var boundary = Assert.Single(DomainModelArrangement.TryArrange("""
            classDiagram
                class Task {
                    <<aggregate root>>
                    +String source_inbox_id
                    +List~String~ inbox_ids
                }
            """, [new DomainModelIdentity("Inbox Item", "aggregate", [], "inbox/domain.md#inbox-item")])!.Boundaries);

        var reference = Assert.Single(boundary.References);
        Assert.Equal(("Inbox Item", "0..*"), (reference.Target, reference.Multiplicity));
        Assert.Equal(["source_inbox_id", "inbox_ids"], reference.Via);
    }

    [Fact]
    public void An_id_and_a_line_to_the_same_type_share_one_card()
    {
        var boundary = Assert.Single(DomainModelArrangement.TryArrange("""
            classDiagram
                class Task {
                    <<aggregate root>>
                    +String source_inbox_id
                }
                Task ..> InboxItem : created from
            """, [new DomainModelIdentity("Inbox Item", "aggregate", [], "inbox/domain.md#inbox-item")])!.Boundaries);

        var reference = Assert.Single(boundary.References);
        Assert.Equal(("InboxItem", DomainModelReferenceSource.Relation), (reference.Target, reference.Source));
        Assert.Equal(["source_inbox_id"], reference.Via);
    }

    [Fact]
    public void A_heading_spelled_as_the_id_wins_over_one_that_starts_with_it()
    {
        DomainModelIdentity[] identities =
        [
            new("Task Template", "aggregate", [], "tasks/domain.md#task-template"),
            new("Task", "aggregate", [], "tasks/domain.md#task")
        ];

        Assert.Equal("Task", DomainModelArrangement.IdentityOf("parent_task_id", "String", identities).Title);
        Assert.Equal("User", DomainModelArrangement.IdentityOf("owner", "UserId", identities).Title);
    }

    [Fact]
    public void The_root_card_lists_its_vocabularies_dates_and_values_but_no_id_or_part()
    {
        var boundary = Assert.Single(DomainModelArrangement.TryArrange("""
            classDiagram
                class Task {
                    <<aggregate root>>
                    +Id id
                    +String title
                    +Date due_on
                    +TaskStatus status
                    +String source_inbox_id
                    +Recurrence recurrence
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
            """)!.Boundaries);

        Assert.Equal(["status", "due_on", "title"], boundary.KeyFields);
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
    /// root composes. The value objects are cards outside every band, the one
    /// saying what it owns; the fence is not handed to mermaid for them.</summary>
    [Fact]
    public void A_class_no_band_holds_is_drawn_outside_every_aggregate()
    {
        var arrangement = DomainModelArrangement.TryArrange("""
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
                class TaskChange
                class Stage {
                    <<enumeration>>
                    draft
                }
                ChapterMetadata "1" *-- "0..1" DecisionState : domain chapters only
            """);

        Assert.NotNull(arrangement);
        Assert.Equal("Remark", Assert.Single(arrangement!.Boundaries).Root.Name);
        Assert.Equal(["ChapterMetadata", "DecisionState", "TaskChange"], arrangement.Outside.Select(o => o.Class.Name));
        Assert.Equal(["DecisionState"], arrangement.Outside[0].Owns);
        Assert.Equal(["Stage"], arrangement.Unplaced.Select(c => c.Name));
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

    /// <summary>
    /// Every bounded context's model under <c>.devbook/domain/</c> opens on the
    /// arranged Diagram, none on mermaid: each class diagram parses, names an
    /// aggregate root, and writes every class with one of the convention's
    /// stereotypes — or none, for a type another context owns — as the Devbook
    /// feature <c>.devbook/domain/devbook/features.md#domain-view-by-unit</c> says a
    /// model file is written.
    /// </summary>
    [Fact]
    public void Every_model_in_this_repository_arranges()
    {
        var domain = new DirectoryInfo(Path.Combine(RepositoryRoot(), ".devbook", "domain"));
        var models = domain.EnumerateFiles("model*.md", SearchOption.AllDirectories)
            .Where(file => !file.FullName.Contains("_archify", StringComparison.Ordinal))
            .Select(file => (Context: file.Directory!.Name, Fences: ClassDiagramFences(File.ReadAllText(file.FullName)).ToList()))
            .ToList();

        var contexts = domain.EnumerateDirectories()
            .Where(d => File.Exists(Path.Combine(d.FullName, "context.md")))
            .Select(d => d.Name);
        Assert.Superset(contexts.ToHashSet(StringComparer.Ordinal), models.Select(m => m.Context).ToHashSet(StringComparer.Ordinal));
        string[] convention = ["aggregate root", "entity", "value object", "enumeration", "domain service"];
        foreach (var (context, fences) in models)
        {
            Assert.True(fences.Count > 0, $"{context}: the model file holds no classDiagram fence.");
            foreach (var fence in fences)
            {
                var diagram = DomainClassDiagramReader.TryRead(fence);
                Assert.True(diagram is not null, $"{context}: a classDiagram fence does not parse.");
                var stray = diagram!.Classes.Where(c => c.Stereotype is { } s && !convention.Contains(s.Trim())).Select(c => $"{c.Name} <<{c.Stereotype}>>").ToList();
                Assert.True(stray.Count == 0, $"{context}: stereotypes outside the convention: {string.Join(", ", stray)}");
                Assert.True(DomainModelArrangement.Arrange(diagram) is not null, $"{context}: a classDiagram fence names no <<aggregate root>>.");
            }
        }
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
