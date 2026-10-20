namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The Flow page's other diagrams (local ADR 0036): a flowchart laid out in ranks
/// and a sequence laid out as cards and lifelines, both in the lifecycle's cards,
/// lines and arrowheads, and a Mermaid source neither can read left to Mermaid.
/// </summary>
public sealed class DomainFlowFiguresTests
{
    private const string Readiness = """
        flowchart TD
            A{"ticked off (completed)?"} -->|yes| D["Readiness: done"]
            A -->|no| B{"does every id in depends_on resolve to a completed task?"}
            B -->|yes| R["Readiness: ready"]
            B -->|"no, or an id resolves to nothing"| K["Readiness: blocked"]
        """;

    private const string Apply = """
        flowchart TD
            A["Apply"] --> B["Take the next accepted proposal, in list order"]
            B --> C{"Refused?"}
            C -->|no| D["The decision lands as one undo step"]
            D --> E{"More accepted proposals?"}
            E -->|yes| B
            E -->|no| F["Screen closes"]
            C -->|yes| G["Stop. What already landed stays"]
        """;

    private const string Adoption = """
        flowchart LR
            DevPC[Dev PC Management] -->|tool-version reports| AT
            Repo[Repository Management] -->|tech-stack scans| AT
            Registry["Registry<br/>(supplier)"] -.-> AT
            AT[Adoption Tracking] -->|daily snapshots| AA[Adoption Analytics]
            Exists -- no --> AA
        """;

    private const string Intake = """
        sequenceDiagram
            participant P as Phone / IDE
            participant S as Sync Service
            participant I as Inbox intake
            P->>S: POST /api/sync/inbox
            S->>S: write document
            S-->>P: accepted
            alt unknown id, live
                S->>I: ReceiveAsync(capture)
                loop each attachment
                    I->>S: GET attachment
                end
            else otherwise
                I-->>S: AlreadyKnown
            end
            Note over P,S: later — the reader routes the item
            Note right of I: settled
        """;

    [Fact]
    public void A_flowchart_reads_its_nodes_shapes_and_labelled_links()
    {
        var chart = DomainFlowcharts.Parse(Readiness)!;

        Assert.False(chart.Across);
        Assert.Equal(["A", "D", "B", "R", "K"], chart.Nodes.Select(node => node.Id));
        Assert.Equal(DomainFlowchartShape.Decision, chart.Node("A").Shape);
        Assert.Equal(DomainFlowchartShape.Box, chart.Node("D").Shape);
        Assert.Equal("Readiness: done", chart.Node("D").Label);
        Assert.Equal("no, or an id resolves to nothing", chart.Links.Single(link => link.To == "K").Label);
        Assert.All(chart.Links, link => Assert.True(link.Arrow));
    }

    [Fact]
    public void A_flowchart_reads_dotted_links_spaced_labels_line_breaks_and_bare_ids()
    {
        var chart = DomainFlowcharts.Parse(Adoption)!;

        Assert.True(chart.Across);
        Assert.Equal("Adoption Tracking", chart.Node("AT").Label);
        Assert.Equal("Registry\n(supplier)", chart.Node("Registry").Label);
        Assert.Equal(DomainFlowchartLine.Dotted, chart.Links.Single(link => link.From == "Registry").Line);
        Assert.Equal("no", chart.Links.Single(link => link.From == "Exists").Label);
        Assert.Equal("Exists", chart.Node("Exists").Label);
    }

    [Fact]
    public void Each_node_sits_a_rank_past_every_node_linking_to_it_and_cards_never_overlap()
    {
        var layout = DomainFlowcharts.Layout(DomainFlowcharts.Parse(Readiness)!);

        int Rank(string id) => layout.Boxes.Single(box => box.Node.Id == id).Rank;
        Assert.Equal(0, Rank("A"));
        Assert.Equal(1, Rank("D"));
        Assert.Equal(1, Rank("B"));
        Assert.Equal(2, Rank("R"));
        Assert.Equal(2, Rank("K"));
        var a = layout.Boxes.Single(box => box.Node.Id == "A");
        var b = layout.Boxes.Single(box => box.Node.Id == "B");
        Assert.True(b.Y > a.Y + a.Height, "top to bottom, a later rank stands lower");
        AssertNoOverlap(layout.Boxes.Select(box => (box.X, box.Y, box.Width, box.Height)));
    }

    [Fact]
    public void Across_the_page_a_later_rank_stands_to_the_right()
    {
        var layout = DomainFlowcharts.Layout(DomainFlowcharts.Parse(Adoption)!);

        var tracking = layout.Boxes.Single(box => box.Node.Id == "AT");
        var analytics = layout.Boxes.Single(box => box.Node.Id == "AA");
        Assert.True(analytics.X > tracking.X + tracking.Width);
        AssertNoOverlap(layout.Boxes.Select(box => (box.X, box.Y, box.Width, box.Height)));
    }

    [Fact]
    public void A_link_back_to_an_earlier_rank_runs_round_the_outside()
    {
        var layout = DomainFlowcharts.Layout(DomainFlowcharts.Parse(Apply)!);

        var loop = layout.Edges.Single(edge => edge.Link.From == "E" && edge.Link.To == "B");
        Assert.True(loop.Back);
        Assert.All(layout.Edges.Where(edge => edge != loop), edge => Assert.False(edge.Back));
        Assert.True(loop.LabelX >= layout.Boxes.Max(box => box.X + box.Width), "the loop and its label stand right of every card");
    }

    [Theory]
    [InlineData("flowchart TD\n    subgraph One\n    A --> B\n    end")]
    [InlineData("flowchart TD\n    A & B --> C")]
    [InlineData("flowchart TD\n    A --> A")]
    [InlineData("flowchart TD\n    A[(Database)] --> B")]
    [InlineData("flowchart BT\n    A --> B")]
    [InlineData("flowchart TD\n    A")]
    [InlineData("graph\n    A --> B")]
    public void A_flowchart_this_cannot_draw_is_left_to_mermaid(string source)
    {
        Assert.Null(DomainFlowcharts.Parse(source));
    }

    [Fact]
    public void A_sequence_reads_its_participants_messages_notes_and_blocks()
    {
        var sequence = DomainSequences.Parse(Intake)!;

        Assert.Equal(["P", "S", "I"], sequence.Participants.Select(p => p.Id));
        Assert.Equal("Phone / IDE", sequence.Participant("P").Label);
        var messages = sequence.Steps.OfType<DomainSequenceMessage>().ToList();
        Assert.Equal(6, messages.Count);
        Assert.True(messages.Single(m => m.Text == "accepted").Reply);
        Assert.False(messages[0].Reply);
        Assert.Equal(["alt", "loop"], sequence.Steps.OfType<DomainSequenceBlock>().Select(block => block.Keyword));
        Assert.Equal("otherwise", Assert.Single(sequence.Steps.OfType<DomainSequenceBranch>()).Label);
        var notes = sequence.Steps.OfType<DomainSequenceNote>().ToList();
        Assert.Equal(("P", "S"), (notes[0].First, notes[0].Last));
        Assert.Equal(DomainSequencePlacement.RightOf, notes[1].Placement);
    }

    [Fact]
    public void A_sequence_stands_its_cards_in_a_row_and_frames_every_block_round_its_steps()
    {
        var layout = DomainSequences.Layout(DomainSequences.Parse(Intake)!);

        Assert.Equal(["P", "S", "I"], layout.Columns.OrderBy(column => column.X).Select(column => column.Participant.Id));
        AssertNoOverlap(layout.Columns.Select(column => (column.X, column.Y, column.Width, column.Height)));

        var arrowYs = layout.Arrows.Select(arrow => double.Parse(arrow.Path.Split(' ')[2], System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(arrowYs.OrderBy(y => y), arrowYs);

        var alt = layout.Frames.Single(frame => frame.Keyword == "alt");
        var loop = layout.Frames.Single(frame => frame.Keyword == "loop");
        Assert.True(loop.X > alt.X && loop.X + loop.Width < alt.X + alt.Width, "the inner frame stands inside the outer one");
        Assert.True(loop.Y > alt.Y && loop.Y + loop.Height < alt.Y + alt.Height);
        Assert.Single(alt.Dividers);
        Assert.All(layout.Columns, column => Assert.True(column.LifelineEnd >= alt.Y + alt.Height));
    }

    [Fact]
    public void A_spaced_link_label_may_hold_hyphens_and_full_stops()
    {
        var chart = DomainFlowcharts.Parse("flowchart TD\n    A -- re-open, e.g. later --> B")!;

        Assert.Equal("re-open, e.g. later", Assert.Single(chart.Links).Label);
    }

    [Fact]
    public void A_cross_ended_message_is_read_as_mermaid_reads_it()
    {
        // Mermaid's own reading: the cross arrow is taken first, the id after it.
        var sequence = DomainSequences.Parse("sequenceDiagram\n    A--xavier: hi\n    A-xB: bye")!;

        Assert.Equal(["A", "avier", "B"], sequence.Participants.Select(p => p.Id));
        sequence = DomainSequences.Parse("sequenceDiagram\n    A->>xavier: hi")!;
        Assert.Equal("xavier", sequence.Steps.OfType<DomainSequenceMessage>().Single().To);
    }

    [Fact]
    public void A_frame_is_wide_enough_for_its_tag_and_condition()
    {
        var layout = DomainSequences.Layout(DomainSequences.Parse("sequenceDiagram\n    critical a fairly long condition label here\n        A->>A: hi\n    end")!);

        var frame = Assert.Single(layout.Frames);
        var text = DomainSequences.TagWidth("critical") + 8 + ("a fairly long condition label here".Length + 2) * 6.6;
        Assert.True(frame.Width >= text, $"{frame.Width} < {text}");
        Assert.True(layout.Width >= frame.X + frame.Width);
    }

    [Theory]
    [InlineData("sequenceDiagram\n    autonumber\n    A->>B: hi")]
    [InlineData("sequenceDiagram\n    alt open\n    A->>B: hi")]
    [InlineData("sequenceDiagram\n    box Aqua Group\n    participant A\n    end")]
    [InlineData("sequenceDiagram\n    participant A")]
    [InlineData("sequenceDiagram\n    A->>B: hi\n    else stray")]
    public void A_sequence_this_cannot_draw_is_left_to_mermaid(string source)
    {
        Assert.Null(DomainSequences.Parse(source));
    }

    [Fact]
    public void A_sequence_reads_its_steps_to_a_screen_reader_as_a_list()
    {
        using var context = new BunitContext();
        var figure = context.Render<DomainSequence>(parameters => parameters.Add(p => p.Diagram, DomainSequences.Parse(Intake)!));

        Assert.Equal("true", figure.Find("svg").GetAttribute("aria-hidden"));
        var items = figure.FindAll("figcaption li").Select(item => item.TextContent).ToList();
        Assert.Contains("Phone / IDE to Sync Service: POST /api/sync/inbox", items);
        Assert.Contains("Sync Service to Phone / IDE (reply): accepted", items);
        Assert.Contains("Sync Service to itself: write document", items);
        Assert.Contains("alt: unknown id, live", items);
    }

    [Fact]
    public void A_flowchart_reads_its_links_to_a_screen_reader_as_a_list()
    {
        using var context = new BunitContext();
        var figure = context.Render<DomainFlowchart>(parameters => parameters.Add(p => p.Diagram, DomainFlowcharts.Parse(Readiness)!));

        Assert.Equal("true", figure.Find("svg").GetAttribute("aria-hidden"));
        Assert.Equal(5, figure.FindAll("[data-testid='domain-flowchart-node']").Count);
        Assert.Single(figure.FindAll("[data-testid='domain-flowchart-node'] polygon"), p => p.ParentElement!.GetAttribute("data-node") == "A");
        var items = figure.FindAll("figcaption li").Select(item => item.TextContent).ToList();
        Assert.Contains("ticked off (completed)? to Readiness: done: yes", items);
    }

    /// <summary>Every Mermaid diagram the repository's own flow files hold is drawn
    /// by the page: a state row, a flowchart or a sequence, none left to Mermaid,
    /// and no two cards of a drawing overlap.</summary>
    [Fact]
    public void Every_diagram_in_the_repository_flow_files_is_drawn_in_the_lifecycle_style()
    {
        var domain = Path.Combine(RepositoryRoot.Root.FullName, ".devbook", "domain");
        var files = Directory.EnumerateFiles(domain, "flow*.md", SearchOption.AllDirectories).ToList();
        Assert.NotEmpty(files);

        var kinds = new HashSet<string>();
        foreach (var file in files)
        {
            var parsed = DomainUnits.Parse(File.ReadAllText(file), Path.GetRelativePath(RepositoryRoot.Root.FullName, file).Replace('\\', '/'));
            foreach (var section in parsed.AllChapters().Select(DomainFlows.Section))
            {
                Assert.DoesNotContain("```mermaid", section.Prose);
                foreach (var figure in section.Diagrams)
                {
                    kinds.Add(figure.GetType().Name);
                    switch (figure)
                    {
                        case DomainFlowchartDiagram chart:
                            AssertNoOverlap(DomainFlowcharts.Layout(chart).Boxes.Select(box => (box.X, box.Y, box.Width, box.Height)), $"{file} › {section.Chapter.Title}");
                            break;
                        case DomainSequenceDiagram sequence:
                            AssertNoOverlap(DomainSequences.Layout(sequence).Columns.Select(column => (column.X, column.Y, column.Width, column.Height)), $"{file} › {section.Chapter.Title}");
                            break;
                        case DomainFlowDiagram row:
                            Assert.NotEmpty(DomainFlows.Layout(row).Boxes);
                            break;
                    }
                }
            }
        }

        Assert.Equal(["DomainFlowDiagram", "DomainFlowchartDiagram", "DomainSequenceDiagram"], kinds.Order(StringComparer.Ordinal));
    }

    private static void AssertNoOverlap(IEnumerable<(double X, double Y, double Width, double Height)> boxes, string? where = null)
    {
        var list = boxes.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            for (var j = i + 1; j < list.Count; j++)
            {
                var (a, b) = (list[i], list[j]);
                var overlap = a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
                Assert.False(overlap, $"cards {i} and {j} overlap{(where is null ? string.Empty : " in " + where)}");
            }
        }
    }
}
