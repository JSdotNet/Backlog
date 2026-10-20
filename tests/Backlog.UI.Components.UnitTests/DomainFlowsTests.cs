namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The Flow page's row (local ADR 0036): a flow's <c>stateDiagram-v2</c> read as one
/// row of states, forward transitions above it, the ways back below it, and the
/// restore loop — the last state back to the first — round the outside; every
/// other Mermaid diagram left to the rendering it already has.
/// </summary>
public sealed class DomainFlowsTests
{
    internal const string Tasks = """
        stateDiagram-v2
            [*] --> Draft : Created
            Draft --> Ready : Refined and actionable
            Ready --> InProgress : Work started
            InProgress --> Done : Work completed
            Done --> Archived : Archived
            Archived --> Draft : Restored
            Ready --> Draft : Revision needed
            InProgress --> Ready : Paused
            Done --> InProgress : Reopened
            Draft --> Archived : Dropped
        """;

    internal const string FlowFile = """
        # Tasks

        ```meta
        type: flow
        ```

        ## Task lifecycle

        Where a task is.

        ```mermaid
        stateDiagram-v2
            [*] --> Draft
            Draft --> Done : Completed
            Done --> Draft : Restored
        ```

        - A done task can be restored.

        ## Capture intake

        ```mermaid
        sequenceDiagram
            participant P as Phone
            P->>P: capture
        ```

        The intake parses no token.
        """;

    [Fact]
    public void The_states_stand_in_one_row_in_the_order_the_forward_transitions_run()
    {
        var diagram = DomainFlows.Parse(Tasks)!;

        Assert.Equal(["[*]start", "Draft", "Ready", "InProgress", "Done", "Archived"], diagram.States.Select(state => state.Id));
        Assert.Equal(DomainFlowStateKind.Start, diagram.States[0].Kind);
    }

    [Fact]
    public void Forward_transitions_run_right_the_ways_back_left_and_the_last_to_the_first_is_the_restore_loop()
    {
        var diagram = DomainFlows.Parse(Tasks)!;

        DomainFlowDirection Of(string from, string to) => diagram.Transitions.Single(t => t.From == from && t.To == to).Direction;
        Assert.Equal(DomainFlowDirection.Forward, Of("Draft", "Ready"));
        Assert.Equal(DomainFlowDirection.Forward, Of("Draft", "Archived"));
        Assert.Equal(DomainFlowDirection.Back, Of("Ready", "Draft"));
        Assert.Equal(DomainFlowDirection.Back, Of("Done", "InProgress"));
        Assert.Equal(DomainFlowDirection.Restore, Of("Archived", "Draft"));
    }

    [Fact]
    public void Forward_arcs_are_drawn_above_the_row_the_ways_back_below_and_the_restore_loop_outermost()
    {
        var diagram = DomainFlows.Parse(Tasks)!;
        var layout = DomainFlows.Layout(diagram);
        var top = layout.Boxes.First(box => box.State.Id == "Draft").Y;
        var bottom = top + layout.Boxes.First(box => box.State.Id == "Draft").Height;

        DomainFlowEdge Edge(string from, string to) => layout.Edges.Single(e => e.Transition.From == from && e.Transition.To == to);

        // The next state is reached along the axis; a skip ahead arcs over the row.
        Assert.Equal(-1, Edge("Draft", "Ready").Level);
        Assert.True(Edge("Draft", "Archived").LabelY < top);

        // Every way back is below the row, and the restore loop is beneath them all.
        var back = layout.Edges.Where(e => e.Transition.Direction == DomainFlowDirection.Back).ToList();
        Assert.All(back, edge => Assert.True(edge.LabelY > bottom));
        var restore = Edge("Archived", "Draft");
        Assert.All(back, edge => Assert.True(restore.Level > edge.Level));
        Assert.True(restore.LabelY > back.Max(edge => edge.LabelY));
    }

    [Fact]
    public void Arcs_sharing_a_stretch_of_the_row_take_different_lanes()
    {
        var layout = DomainFlows.Layout(DomainFlows.Parse("""
            stateDiagram-v2
                [*] --> A
                A --> B
                B --> C
                C --> D
                C --> A : back over B
                D --> B : back over C
            """)!);

        int Lane(string from) => layout.Edges.Single(e => e.Transition.From == from && e.Transition.Direction == DomainFlowDirection.Back).Level;
        Assert.NotEqual(Lane("C"), Lane("D"));
    }

    [Fact]
    public void Start_and_end_sit_at_the_ends_and_a_self_transition_loops_above_its_state()
    {
        var diagram = DomainFlows.Parse("""
            stateDiagram-v2
                state "Waiting for review" as Waiting
                [*] --> Waiting : Captured
                Waiting --> Waiting : Change the date
                Waiting --> Done : Routed
                Waiting --> Done : Dismissed
                Done --> [*]
            """)!;

        Assert.Equal(["[*]start", "Waiting", "Done", "[*]end"], diagram.States.Select(state => state.Id));
        Assert.Equal("Waiting for review", diagram.State("Waiting").Label);
        Assert.Equal(DomainFlowDirection.Self, diagram.Transitions.Single(t => t.From == "Waiting" && t.To == "Waiting").Direction);
        Assert.Equal("Routed / Dismissed", diagram.Transitions.Single(t => t.From == "Waiting" && t.To == "Done").Label);

        var layout = DomainFlows.Layout(diagram);
        var self = layout.Edges.Single(e => e.Transition.Direction == DomainFlowDirection.Self);
        Assert.True(self.LabelY < layout.Boxes.Single(box => box.State.Id == "Waiting").Y);
    }

    [Theory]
    [InlineData("stateDiagram\n    [*] --> A")]
    [InlineData("sequenceDiagram\n    A->>B: hi")]
    [InlineData("stateDiagram-v2\n    state Busy {\n        [*] --> Working\n    }")]
    [InlineData("stateDiagram-v2\n    state Fork <<fork>>\n    [*] --> Fork")]
    [InlineData("stateDiagram-v2\n    [*] --> A\n    note right of A : a note")]
    [InlineData("stateDiagram-v2\n    A : only a description")]
    public void A_diagram_the_row_cannot_hold_is_left_to_mermaid(string source)
    {
        Assert.Null(DomainFlows.Parse(source));
    }

    [Fact]
    public void A_flow_chapter_draws_its_state_diagram_and_keeps_its_prose_and_other_diagrams()
    {
        var file = DomainUnits.Parse(FlowFile, ".devbook/domain/tasks/flow.md");

        var lifecycle = DomainFlows.Section(file.Chapters[0]);
        Assert.Single(lifecycle.Diagrams);
        Assert.DoesNotContain("stateDiagram-v2", lifecycle.Prose);
        Assert.Contains("Where a task is.", lifecycle.Prose);
        Assert.Contains("A done task can be restored.", lifecycle.Prose);

        var intake = DomainFlows.Section(file.Chapters[1]);
        Assert.Empty(intake.Diagrams);
        Assert.Contains("sequenceDiagram", intake.Prose);
    }

    [Fact]
    public void The_flow_page_draws_the_row_with_the_prose_beneath_and_mermaid_for_the_rest()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var page = context.Render<DomainFlowPage>(parameters => parameters
            .Add(p => p.File, DomainUnits.Parse(FlowFile, ".devbook/domain/tasks/flow.md")));

        var sections = page.FindAll("[data-testid='domain-flow-section']");
        Assert.Equal(["task-lifecycle", "capture-intake"], sections.Select(section => section.GetAttribute("data-flow")));

        var lifecycle = sections[0];
        var row = lifecycle.QuerySelector("[data-testid='domain-flow-row']")!;
        var list = lifecycle.QuerySelector("ul")!;
        Assert.Contains("A done task can be restored.", list.TextContent);
        Assert.True(row.CompareDocumentPosition(list).HasFlag(AngleSharp.Dom.DocumentPositions.Following));
        Assert.Empty(lifecycle.QuerySelectorAll("[data-testid='diagram-view']"));

        Assert.Single(sections[1].QuerySelectorAll("[data-testid='diagram-view']"));
        Assert.Empty(sections[1].QuerySelectorAll("[data-testid='domain-flow-row']"));
    }

    [Fact]
    public void The_row_reads_its_transitions_to_a_screen_reader_as_a_list()
    {
        using var context = new BunitContext();
        var row = context.Render<DomainFlowRow>(parameters => parameters.Add(p => p.Diagram, DomainFlows.Parse(Tasks)!));

        Assert.Equal("true", row.Find("svg").GetAttribute("aria-hidden"));
        var items = row.FindAll("figcaption li").Select(item => item.TextContent).ToList();
        Assert.Contains("Archived to Draft (restore): Restored", items);
        Assert.Contains("Ready to Draft (a way back): Revision needed", items);
        Assert.Contains("Start to Draft: Created", items);
    }
}
