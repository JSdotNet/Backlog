namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The Flow page's row (local ADR 0044): a flow's <c>stateDiagram-v2</c> read as one
/// row of states, forward transitions above it, the ways back below it, and the
/// restore loop — the last state back to the first — round the outside; a
/// flowchart and a sequence drawn in the same style; and a diagram none of them
/// can read left to the rendering it already has.
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

        ## Task model

        ```mermaid
        classDiagram
            class Task
        ```

        A task has a title.
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
    public void Moves_between_neighbours_run_straight_forward_on_top_and_back_underneath_and_the_restore_loop_runs_outermost_above()
    {
        var diagram = DomainFlows.Parse(Tasks)!;
        var layout = DomainFlows.Layout(diagram);
        var top = layout.Boxes.First(box => box.State.Id == "Draft").Y;

        DomainFlowEdge Edge(string from, string to) => layout.Edges.Single(e => e.Transition.From == from && e.Transition.To == to);

        // The next state and the one before are reached straight along the row,
        // the forward move on top and the way back underneath it.
        Assert.Equal(-1, Edge("Draft", "Ready").Level);
        Assert.Equal(-1, Edge("Ready", "Draft").Level);
        Assert.True(Edge("Ready", "Draft").LabelY > Edge("Draft", "Ready").LabelY);
        Assert.All(
            layout.Edges.Where(e => e.Transition.Direction == DomainFlowDirection.Back),
            edge => Assert.Equal(-1, edge.Level));

        // A skip ahead arcs over the row, and the restore loop runs outside it.
        var skip = Edge("Draft", "Archived");
        var restore = Edge("Archived", "Draft");
        Assert.True(skip.LabelY < top);
        Assert.True(restore.Level > skip.Level);
        Assert.True(restore.LabelY < skip.LabelY);
    }

    [Fact]
    public void A_way_back_over_a_state_runs_in_a_lane_below_the_row()
    {
        var layout = DomainFlows.Layout(DomainFlows.Parse("""
            stateDiagram-v2
                [*] --> A
                A --> B
                B --> C
                C --> D
                C --> A : back over B
            """)!);

        var bottom = layout.Boxes.Max(box => box.Y + box.Height);
        Assert.True(layout.Edges.Single(e => e.Transition.From == "C" && e.Transition.To == "A").LabelY > bottom);
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
    [InlineData("stateDiagram-v2\n    [*] --> A\n    note right of A\n        a note\n    end note")]
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
        Assert.Equal("Where a task is.", lifecycle.Lead);
        Assert.DoesNotContain("Where a task is.", lifecycle.Prose);
        Assert.Contains("A done task can be restored.", lifecycle.Prose);

        var intake = DomainFlows.Section(file.Chapters[1]);
        Assert.IsType<DomainSequenceDiagram>(Assert.Single(intake.Diagrams));
        Assert.DoesNotContain("sequenceDiagram", intake.Prose);
        Assert.Contains("The intake parses no token.", intake.Prose);

        var model = DomainFlows.Section(file.Chapters[2]);
        Assert.Empty(model.Diagrams);
        Assert.Contains("classDiagram", model.Prose);
    }

    [Fact]
    public void The_flow_page_draws_the_row_with_the_prose_beneath_and_mermaid_for_the_rest()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var page = context.Render<DomainFlowPage>(parameters => parameters
            .Add(p => p.File, DomainUnits.Parse(FlowFile, ".devbook/domain/tasks/flow.md")));

        var sections = page.FindAll("[data-testid='domain-flow-section']");
        Assert.Equal(["task-lifecycle", "capture-intake", "task-model"], sections.Select(section => section.GetAttribute("data-flow")));

        var lifecycle = sections[0];
        var row = lifecycle.QuerySelector("[data-testid='domain-flow-row']")!;
        var list = lifecycle.QuerySelector("ul:not([data-testid='domain-flow-legend'])")!;
        Assert.Contains("A done task can be restored.", list.TextContent);
        Assert.True(row.CompareDocumentPosition(list).HasFlag(AngleSharp.Dom.DocumentPositions.Following));
        Assert.Empty(lifecycle.QuerySelectorAll("[data-testid='diagram-view']"));

        Assert.Single(sections[1].QuerySelectorAll("[data-testid='domain-sequence']"));
        Assert.Empty(sections[1].QuerySelectorAll("[data-testid='diagram-view']"));

        Assert.Single(sections[2].QuerySelectorAll("[data-testid='diagram-view']"));
        Assert.Empty(sections[2].QuerySelectorAll("[data-testid='domain-flow-row'], [data-testid='domain-flowchart'], [data-testid='domain-sequence']"));
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

    /// <summary>The lifecycle as its board draws it: a box per state with its
    /// description, short labels, the events as chips, one note for the drop with
    /// an archived repository, and a caption under the first state.</summary>
    internal const string Lifecycle = """
        stateDiagram-v2
            state "In progress" as InProgress
            Draft : every new task
            InProgress : stamps started_on once
            Done : work is over
            [*] --> Draft : created
            Draft --> Ready : refined
            Ready --> InProgress : started (TaskProjected, not yet emitted)
            InProgress --> Done : finished (TaskCompleted)
            Done --> Archived : archived
            Archived --> Draft : restored
            Ready --> Draft : revise
            InProgress --> Ready : paused
            Draft --> Archived : drop straight there when their repository is archived
            Ready --> Archived : drop straight there when their repository is archived
            InProgress --> Archived : drop straight there when their repository is archived
            note right of Draft : StatusChanged on every move
            classDef active stroke-width:2px
            class InProgress active
            class Done,Archived ended
        """;

    [Fact]
    public void A_state_box_reads_its_alias_its_description_and_its_class()
    {
        var diagram = DomainFlows.Parse(Lifecycle)!;

        var progress = diagram.State("InProgress");
        Assert.Equal("In progress", progress.Label);
        Assert.Equal("stamps started_on once", progress.Description);
        Assert.Equal(["active"], progress.Classes);
        Assert.Equal(["ended"], diagram.State("Archived").Classes);
        Assert.Equal(string.Empty, diagram.State("Ready").Description);

        using var context = new BunitContext();
        var row = context.Render<DomainFlowRow>(parameters => parameters.Add(p => p.Diagram, diagram));
        var box = row.Find("[data-testid='domain-flow-state'][data-state='InProgress']");
        Assert.Contains("In progress", box.TextContent);
        Assert.DoesNotContain("InProgress", box.TextContent);
        Assert.Equal("stamps started_on once", box.QuerySelector("[data-testid='domain-flow-description']")!.TextContent);
        Assert.Contains("domain-flow__state--active", box.GetAttribute("class"));
        Assert.Contains("domain-flow__state--ended", row.Find("[data-state='Done']").GetAttribute("class"));
    }

    [Fact]
    public void An_event_in_a_label_is_a_chip_under_its_transition_dashed_until_it_is_emitted()
    {
        var diagram = DomainFlows.Parse(Lifecycle)!;
        var started = diagram.Transitions.Single(t => t.From == "Ready" && t.To == "InProgress");
        Assert.Equal("started", started.Label);
        Assert.Equal(new DomainFlowEvent("TaskProjected", Emitted: false), started.Event);
        Assert.Equal(new DomainFlowEvent("TaskCompleted", Emitted: true), diagram.Transitions.Single(t => t.From == "InProgress" && t.To == "Done").Event);

        var layout = DomainFlows.Layout(diagram);
        var chip = layout.Marks.Single(mark => mark.Kind == DomainFlowMarkKind.Event && mark.Text == "TaskProjected");
        var edge = layout.Edges.Single(e => e.Transition == started);
        Assert.Equal(edge.LabelX, chip.X + chip.Width / 2, 1);
        Assert.True(chip.Y > layout.Boxes.Max(box => box.Y + box.Height));

        DevbookReference? followed = null;
        using var context = new BunitContext();
        var row = context.Render<DomainFlowRow>(parameters => parameters
            .Add(p => p.Diagram, diagram)
            .Add(p => p.DocumentPath, ".devbook/domain/tasks/flow.md")
            .Add(p => p.OnNavigate, reference => followed = reference));
        var projected = row.Find("[data-testid='domain-flow-event'][data-event='TaskProjected']");
        Assert.Contains("domain-flow__event--documented", projected.GetAttribute("class"));
        Assert.DoesNotContain("domain-flow__event--documented", row.Find("[data-event='TaskCompleted']").GetAttribute("class"));
        projected.QuerySelector("button")!.Click();
        Assert.Equal(".devbook/domain/tasks/model.md", followed?.Raw);
        Assert.Contains("Ready to In progress: started, raising TaskProjected (documented, not emitted yet)", row.FindAll("figcaption li").Select(item => item.TextContent));
    }

    [Fact]
    public void A_way_in_from_several_states_under_one_label_is_one_note_not_an_arc_each()
    {
        var diagram = DomainFlows.Parse(Lifecycle)!;

        var drop = Assert.Single(diagram.Drops);
        Assert.Equal("Archived", drop.To);
        Assert.Equal(["Draft", "Ready", "InProgress"], drop.From);
        var layout = DomainFlows.Layout(diagram);
        Assert.DoesNotContain(layout.Edges, edge => edge.Transition.To == "Archived" && edge.Transition.From != "Done");

        using var context = new BunitContext();
        var row = context.Render<DomainFlowRow>(parameters => parameters.Add(p => p.Diagram, diagram));
        Assert.Equal(
            "Also into Archived: Draft, Ready and In progress drop straight there when their repository is archived.",
            row.Find("[data-testid='domain-flow-drop']").TextContent.Trim());
    }

    [Fact]
    public void A_one_line_note_is_a_caption_under_its_state_and_a_start_into_the_first_state_is_not_drawn()
    {
        var diagram = DomainFlows.Parse(Lifecycle)!;
        Assert.Equal([new DomainFlowNote("Draft", "StatusChanged on every move")], diagram.Notes);

        var layout = DomainFlows.Layout(diagram);
        Assert.DoesNotContain(layout.Boxes, box => box.State.Kind == DomainFlowStateKind.Start);
        Assert.DoesNotContain(layout.Edges, edge => edge.Transition.From == "[*]start");
        var draft = layout.Boxes.Single(box => box.State.Id == "Draft");
        var caption = layout.Marks.Single(mark => mark.Kind == DomainFlowMarkKind.Note);
        Assert.Equal(draft.X, caption.X);
        Assert.True(caption.Y > draft.Y + draft.Height);

        using var context = new BunitContext();
        var row = context.Render<DomainFlowRow>(parameters => parameters.Add(p => p.Diagram, diagram));
        Assert.Equal("StatusChanged", row.Find("[data-testid='domain-flow-note'] code").TextContent);
    }

    [Fact]
    public void The_row_scales_to_its_column_rather_than_scrolling()
    {
        using var context = new BunitContext();
        var row = context.Render<DomainFlowRow>(parameters => parameters.Add(p => p.Diagram, DomainFlows.Parse(Lifecycle)!));

        var layout = DomainFlows.Layout(DomainFlows.Parse(Lifecycle)!);
        Assert.Contains("domain-flow__svg--fit", row.Find("svg").GetAttribute("class"));
        Assert.Contains($"max-width: {Math.Round(layout.Width, 1).ToString(System.Globalization.CultureInfo.InvariantCulture)}px", row.Find(".domain-flow__canvas").GetAttribute("style"));
    }

    [Fact]
    public void Marks_that_would_meet_under_the_row_take_separate_lines()
    {
        var layout = DomainFlows.Layout(DomainFlows.Parse("""
            stateDiagram-v2
                A --> B : go (TaskStarted)
                B --> A : back (TaskStopped)
                note right of A : AnEventNamedAtLength on every move
            """)!);

        var marks = layout.Marks;
        Assert.Equal(3, marks.Count);
        foreach (var mark in marks)
        {
            foreach (var other in marks.Where(other => other != mark && other.Y == mark.Y))
            {
                Assert.True(mark.X + mark.Width <= other.X || other.X + other.Width <= mark.X, $"{mark.Text} meets {other.Text}");
            }
        }
        Assert.True(layout.Height >= marks.Max(mark => mark.Y) + 22);
    }

    private const string BesideFile = """
        # Tasks

        ## Task lifecycle

        Five recorded states.

        ```mermaid
        stateDiagram-v2
            Draft --> Ready : refined (TaskProjected, not yet emitted)
        ```

        ## Beside the status, not states

        ### The tick

        `completed_on`

        - Ticking an open task also moves it to Done.

        [3 requirements →](requirements.md#requirement-ticking-an-open-task-off-moves-it-to-done)

        ### Readiness

        *derived on read*

        - `ready` — nothing it waits on is open.
        """;

    [Fact]
    public void The_first_chapter_titles_the_page_with_its_lead_and_a_legend_and_untyped_sub_chapters_read_as_cards()
    {
        var file = DomainUnits.Parse(BesideFile, ".devbook/domain/tasks/flow.md");
        var beside = DomainFlows.Section(file.Chapters[1]);
        Assert.Empty(beside.Prose);
        Assert.Equal(["The tick", "Readiness"], beside.Cards.Select(card => card.Title));
        Assert.Equal("`completed_on`", beside.Cards[0].Tag);
        Assert.Equal("- Ticking an open task also moves it to Done.", beside.Cards[0].Body);
        Assert.StartsWith("[3 requirements →]", beside.Cards[0].Footer);
        Assert.Equal("*derived on read*", beside.Cards[1].Tag);
        Assert.Empty(beside.Cards[1].Footer);

        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var page = context.Render<DomainFlowPage>(parameters => parameters.Add(p => p.File, file));

        var titles = page.FindAll("[data-testid='domain-flow-title']");
        Assert.Contains("domain-flows__title--page", titles[0].GetAttribute("class"));
        Assert.DoesNotContain("domain-flows__title--page", titles[1].GetAttribute("class"));
        Assert.Contains("Five recorded states.", page.Find("[data-testid='domain-flow-lead']").TextContent);
        var legend = page.Find("[data-testid='domain-flow-legend']").TextContent;
        Assert.Contains("forward", legend);
        Assert.Contains("event, documented but not emitted yet", legend);
        Assert.DoesNotContain("event, emitted", legend);
        Assert.Equal(["the-tick", "readiness"], page.FindAll("[data-testid='domain-flow-card']").Select(card => card.GetAttribute("data-card")));
        Assert.Contains("completed_on", page.Find("[data-testid='domain-flow-card-tag']").TextContent);
        Assert.Contains("3 requirements", page.Find("[data-testid='domain-flow-card-footer']").TextContent);
    }
}
