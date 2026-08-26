namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The table decides one visible thing: whether the app offers to generate an
/// artifact for a diagram it has none for. An offer is a promise, so the case
/// worth pinning hardest is the one that must produce no offer — a class diagram,
/// which is what most of this repository's mermaid blocks are and which none of
/// Archify's five types can express.
/// </summary>
public sealed class ArchifyDiagramTypesTests
{
    [Theory]
    [InlineData("flowchart TD\n    A --> B", "workflow")]
    [InlineData("graph LR\n    A --> B", "workflow")]
    [InlineData("sequenceDiagram\n    A ->> B: ask", "sequence")]
    [InlineData("stateDiagram-v2\n    [*] --> Draft", "lifecycle")]
    [InlineData("stateDiagram\n    [*] --> Draft", "lifecycle")]
    [InlineData("C4Context\n    Person(user, \"User\")", "architecture")]
    [InlineData("C4Container\n    Person(user, \"User\")", "architecture")]
    [InlineData("C4Component\n    Person(user, \"User\")", "architecture")]
    public void A_kind_Archify_can_re_author_maps_to_the_type_it_would_be_authored_as(string source, string expected)
    {
        Assert.Equal(expected, ArchifyDiagramTypes.For(source));
        Assert.True(ArchifyDiagramTypes.IsSupported(source));
    }

    /// <summary>
    /// The case the whole "generate" affordance is gated on. Twelve of this
    /// repository's fifty-one mermaid blocks are class diagrams and every one is a
    /// bounded context's aggregate model; Archify has no way to say "aggregate
    /// root", "value object" or "0..*", so there is nothing to generate and the
    /// app must not say there is.
    /// </summary>
    [Fact]
    public void A_class_diagram_has_no_Archify_type_and_is_never_offered()
    {
        const string source = """
            classDiagram
                class Order {
                    +OrderId Id
                }
                Order "1" --> "0..*" OrderLine
            """;

        Assert.Null(ArchifyDiagramTypes.For(source));
        Assert.False(ArchifyDiagramTypes.IsSupported(source));
        Assert.Equal("classDiagram", ArchifyDiagramTypes.MermaidKind(source));
    }

    [Theory]
    [InlineData("erDiagram\n    ORDER ||--o{ LINE : has")]
    [InlineData("gantt\n    title Roadmap")]
    [InlineData("pie\n    \"a\" : 1")]
    [InlineData("mindmap\n  root")]
    [InlineData("gitGraph\n    commit")]
    [InlineData("architecture-beta\n    group api")]
    public void A_kind_that_is_known_to_have_no_type_is_not_offered_either(string source)
    {
        Assert.Null(ArchifyDiagramTypes.For(source));
        Assert.False(ArchifyDiagramTypes.IsSupported(source));
        Assert.NotNull(ArchifyDiagramTypes.MermaidKind(source));
    }

    /// <summary>A kind the table has never heard of is treated as unsupported
    /// rather than guessed at, because offering where nothing can be generated is
    /// the worse of the two failures.</summary>
    [Theory]
    [InlineData("quantumDiagram\n    A --> B")]
    [InlineData("nonsense")]
    // The generator's `/^[A-Za-z0-9_-]+/` reads this as the word "--" too, so both
    // sides agree on the wrong-looking answer and both call it unsupported.
    [InlineData("--> B")]
    public void An_unknown_first_keyword_is_unsupported_rather_than_guessed(string source)
    {
        Assert.Null(ArchifyDiagramTypes.For(source));
        Assert.False(ArchifyDiagramTypes.IsSupported(source));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   \n\t\n")]
    [InlineData("%% nothing but a comment")]
    [InlineData("[*] --> Draft")]
    public void A_source_with_no_keyword_at_all_has_no_kind(string? source)
    {
        Assert.Null(ArchifyDiagramTypes.MermaidKind(source));
        Assert.Null(ArchifyDiagramTypes.For(source));
        Assert.False(ArchifyDiagramTypes.IsSupported(source));
    }

    /// <summary>A fence is allowed to open with a comment or a theme directive and
    /// several in this repository do, so the keyword is the first line that is
    /// neither.</summary>
    [Fact]
    public void A_leading_comment_does_not_hide_the_keyword()
    {
        const string source = """
            %% The order lifecycle, as .domain describes it.
            %% Keep in step with domain.md.
            stateDiagram-v2
                [*] --> Draft
            """;

        Assert.Equal("stateDiagram-v2", ArchifyDiagramTypes.MermaidKind(source));
        Assert.Equal("lifecycle", ArchifyDiagramTypes.For(source));
    }

    [Fact]
    public void A_leading_init_directive_does_not_hide_the_keyword()
    {
        const string source = """
            %%{init: {'theme':'dark'}}%%
            flowchart TD
                A --> B
            """;

        Assert.Equal("flowchart", ArchifyDiagramTypes.MermaidKind(source));
        Assert.Equal("workflow", ArchifyDiagramTypes.For(source));
    }

    [Fact]
    public void Blank_and_indented_leading_lines_do_not_hide_the_keyword()
    {
        const string source = "\n\n   %%{init: {'theme':'dark'}}%%\n   sequenceDiagram\n       A ->> B: ask";

        Assert.Equal("sequenceDiagram", ArchifyDiagramTypes.MermaidKind(source));
        Assert.Equal("sequence", ArchifyDiagramTypes.For(source));
    }

    [Fact]
    public void The_keyword_is_read_case_insensitively_because_the_generators_table_is_lowercased()
    {
        Assert.Equal("workflow", ArchifyDiagramTypes.For("FLOWCHART TD\n    A --> B"));
        Assert.Null(ArchifyDiagramTypes.For("CLASSDIAGRAM\n    class Order"));
    }

    [Fact]
    public void The_keyword_stops_at_the_first_character_that_cannot_be_part_of_one()
    {
        Assert.Equal("flowchart", ArchifyDiagramTypes.MermaidKind("flowchart TD"));
        Assert.Equal("stateDiagram-v2", ArchifyDiagramTypes.MermaidKind("stateDiagram-v2"));
        Assert.Equal("graph", ArchifyDiagramTypes.MermaidKind("graph LR;"));
    }

    [Fact]
    public void The_five_types_Archify_has_are_the_five_the_table_can_produce()
    {
        Assert.Equal(
            new[] { "architecture", "workflow", "sequence", "dataflow", "lifecycle" },
            ArchifyDiagramTypes.All);
    }
}
