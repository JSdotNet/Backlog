using Backlog.Desktop.UI.Devbook;
using Backlog.SharedKernel.Devbook;
using Backlog.SharedKernel.Metadata;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Cohesion graph's layout is geometry, not drawing: these tests pin where
/// the boxes go, how the edges run between them, and that no edge runs through
/// a box or along another edge — on fixtures, with no renderer.
/// </summary>
public sealed class AiCohesionLayoutTests
{
    private const double W = AiCohesionLayout.NodeWidth;
    private const double H = AiCohesionLayout.NodeHeight;

    [Fact]
    public void The_seven_stages_are_columns_with_the_pull_request_between_verify_and_integrate()
    {
        var layout = Layout(Usage("solo", stage: ["code"]));

        Assert.Equal(["decide", "specify", "code", "verify", "integrate", "release", "operate"], layout.Columns.Select(column => column.Stage.Key));
        Assert.Equal([true, true, true, true, false, false, false], layout.Columns.Select(column => column.BeforePullRequest));
        Assert.Equal(1632, layout.Width);

        var verifyRight = AiCohesionLayout.ColumnX(3) + W;
        var integrateLeft = AiCohesionLayout.ColumnX(4);
        Assert.Equal((verifyRight + integrateLeft) / 2, layout.PullRequestX);

        // Lanes tile the path: each one a node and a gutter wide, edge to edge.
        for (var i = 1; i < layout.Columns.Count; i++)
        {
            Assert.Equal(layout.Columns[i - 1].X + layout.Columns[i - 1].Width, layout.Columns[i].X);
        }

        var node = Assert.Single(layout.Nodes);
        Assert.Equal(2, node.Column);
        Assert.Equal(AiCohesionLayout.ColumnX(2), node.X);
        Assert.Equal(AiCohesionLayout.PathTop, node.Y);
    }

    [Fact]
    public void A_usage_sits_on_the_path_at_its_first_stage_and_a_layer_only_usage_in_the_band_of_its_first_layer()
    {
        var layout = Layout(
            Usage("gate", stage: ["verify", "integrate"]),
            Usage("checks", stage: ["verify"], layer: ["thread"]),
            Usage("worktree", layer: ["harness"]),
            Usage("upkeep", layer: ["upkeep", "thread"]),
            Usage("sync", layer: ["thread"]));

        Assert.Equal(["thread", "upkeep", "harness"], layout.Bands.Select(band => band.Layer.Key));

        var gate = Node(layout, "gate");
        Assert.Equal(3, gate.Column);
        Assert.Null(gate.Layer);

        // A stage puts it on the path even when it also names a layer.
        var checks = Node(layout, "checks");
        Assert.Equal(3, checks.Column);
        Assert.Null(checks.Layer);
        Assert.Equal([gate.Y, gate.Y + H + AiCohesionLayout.RowGap], new[] { gate, checks }.Select(node => node.Y).Order());

        // The bands stack below the lanes, in vocabulary order, a node row each.
        Assert.True(layout.Bands[0].Y > layout.LaneHeight);
        Assert.True(layout.Bands[1].Y >= layout.Bands[0].Y + layout.Bands[0].Height);
        Assert.True(layout.Bands[2].Y >= layout.Bands[1].Y + layout.Bands[1].Height);
        Assert.Equal(layout.Bands[2].Y + layout.Bands[2].Height, layout.Height);

        Assert.Equal("thread", Node(layout, "sync").Layer);
        Assert.Equal("upkeep", Node(layout, "upkeep").Layer);
        Assert.Equal("harness", Node(layout, "worktree").Layer);
        foreach (var node in layout.Nodes.Where(node => node.Layer is not null))
        {
            var band = layout.Bands.Single(band => band.Layer.Key == node.Layer);
            Assert.InRange(node.Y, band.Y + AiCohesionLayout.BandHeaderHeight, band.Y + band.Height - H);
        }
    }

    [Fact]
    public void A_band_usage_takes_the_free_column_nearest_what_it_links_to()
    {
        var layout = Layout(
            Usage("merge", stage: ["integrate"]),
            Usage("deploy", stage: ["release"]),
            Usage("upkeep", layer: ["upkeep"], feeds: ["merge: pull requests"]),
            Usage("pkg", layer: ["upkeep"], feeds: ["merge: package pull request"]),
            Usage("instr", layer: ["upkeep"], feeds: ["deploy: draft"]),
            Usage("loose", layer: ["upkeep"]));

        Assert.Equal(4, Node(layout, "upkeep").Column);
        Assert.Equal(5, Node(layout, "instr").Column);
        Assert.Equal(3, Node(layout, "pkg").Column); // 4 is taken; 3 and 5 tie and the left one wins.
        Assert.Equal(0, Node(layout, "loose").Column); // linked to nothing: the leftmost free column.
        Assert.Single(layout.Bands, band => band.Layer.Key == "upkeep" && band.Rows == 1);
    }

    [Fact]
    public void A_band_grows_a_row_only_when_all_seven_columns_are_taken()
    {
        var layout = Layout([.. Enumerable.Range(1, 8).Select(i => Usage($"h{i}", layer: ["harness"]))]);

        var harness = layout.Bands.Single(band => band.Layer.Key == "harness");
        Assert.Equal(2, harness.Rows);
        Assert.Equal(Enumerable.Range(0, 7), layout.Nodes.Take(7).Select(node => node.Column));
        Assert.Equal(0, layout.Nodes[7].Column);
        Assert.Equal(layout.Nodes[0].Y + H + AiCohesionLayout.RowGap, layout.Nodes[7].Y);
    }

    [Fact]
    public void Usages_are_ordered_within_a_column_to_shorten_their_edges()
    {
        // In reading order both hand-offs would cross; reordered, both run straight.
        var layout = Layout(
            Usage("x", stage: ["specify"], feeds: ["b: to b"]),
            Usage("y", stage: ["specify"], feeds: ["a: to a"]),
            Usage("a", stage: ["code"]),
            Usage("b", stage: ["code"]));

        Assert.Equal(Node(layout, "x").Row, Node(layout, "b").Row);
        Assert.Equal(Node(layout, "y").Row, Node(layout, "a").Row);
        Assert.All(layout.Edges, edge =>
        {
            Assert.Equal(2, edge.Points.Count);
            Assert.Equal(edge.Points[0].Y, edge.Points[1].Y);
        });
    }

    [Fact]
    public void Each_edge_keeps_its_kind_its_dash_and_its_label()
    {
        var layout = Layout(
            Usage("tracker", "adopted", stage: ["decide"], feeds: ["spec: what you pick"]),
            Usage("spec", "adopted", stage: ["specify"], related: [".devbook/ai/01.md#tracker"]),
            Usage("sync", "candidate", layer: ["thread"], feeds: ["spec: draft pull requests"]),
            Usage("alerts", "candidate", stage: ["operate"], feeds: ["tracker: would enter Decide"]));

        var solid = Edge(layout, "tracker", "spec", AiCohesionEdgeKind.Feed);
        Assert.False(solid.IsDashed);
        Assert.Equal("what you pick", solid.Label);

        var candidate = Edge(layout, "sync", "spec", AiCohesionEdgeKind.Feed);
        Assert.True(candidate.IsDashed);

        var openLoop = Edge(layout, "alerts", "tracker", AiCohesionEdgeKind.OpenLoop);
        Assert.True(openLoop.IsDashed);
        Assert.Equal("would enter Decide", openLoop.Label);

        var support = Edge(layout, "spec", "tracker", AiCohesionEdgeKind.Support);
        Assert.Null(support.Label);
        Assert.False(support.IsDashed);

        Assert.Equal(4, layout.Edges.Count);
    }

    [Fact]
    public void An_edge_runs_from_the_edge_of_one_box_to_the_edge_of_the_other_with_its_label_on_its_longest_segment()
    {
        var layout = Layout(
            Usage("from", stage: ["decide"], feeds: ["to: a long way"]),
            Usage("blocker", stage: ["verify"]),
            Usage("to", layer: ["harness"]));

        var edge = Assert.Single(layout.Edges);
        var from = Node(layout, "from");
        var to = Node(layout, "to");
        Assert.True(OnBoundary(edge.Points[0], from));
        Assert.True(OnBoundary(edge.Points[^1], to));

        var longest = Segments(edge).OrderByDescending(segment => Length(segment)).First();
        Assert.Equal(new AiCohesionPoint((longest.A.X + longest.B.X) / 2, (longest.A.Y + longest.B.Y) / 2), edge.LabelAt);
    }

    [Fact]
    public void Parallel_edges_in_one_gutter_get_distinct_offsets_inside_it()
    {
        var layout = Layout(
            Usage("a", stage: ["code"], feeds: ["z: one"]),
            Usage("b", stage: ["code"], feeds: ["z: two"]),
            Usage("c", stage: ["code"], feeds: ["z: three"]),
            Usage("z", stage: ["verify"]));

        var gutterLow = AiCohesionLayout.ColumnX(2) + W;
        var gutterHigh = AiCohesionLayout.ColumnX(3);
        var runs = layout.Edges
            .SelectMany(edge => Segments(edge).Where(segment => segment.A.X == segment.B.X && segment.A.Y != segment.B.Y))
            .ToList();

        Assert.NotEmpty(runs);
        Assert.All(runs, run => Assert.InRange(run.A.X, gutterLow + 1, gutterHigh - 1));
        Assert.Equal(runs.Count, runs.Select(run => run.A.X).Distinct().Count());

        // And the three ends on z's side each get their own height.
        Assert.Equal(3, layout.Edges.Select(edge => edge.Points[^1].Y).Distinct().Count());
    }

    [Fact]
    public void On_a_full_board_every_segment_is_orthogonal_misses_every_box_and_overlaps_no_other_edge()
    {
        var layout = Layout(FullBoard());

        Assert.Equal(32, layout.Nodes.Count);
        Assert.Equal(34, layout.Edges.Count);

        AssertRoutesAreClean(layout);

        Assert.All(layout.Nodes, node => Assert.InRange(node.Right, 0, layout.Width));
        Assert.All(layout.Nodes, node => Assert.InRange(node.Bottom, 0, layout.Height));
        Assert.Equal(2, layout.Edges.Count(edge => edge.Kind == AiCohesionEdgeKind.OpenLoop));
    }

    [Fact]
    public void Long_hops_along_a_row_and_back_again_stay_clear_of_boxes_and_of_each_other()
    {
        var layout = Layout(
            Usage("a", stage: ["decide"], feeds: ["c: far along the row", "x: down a row", "d: to the end"]),
            Usage("c", stage: ["verify"], feeds: ["a: and back"]),
            Usage("x", stage: ["code"], feeds: ["a: back up", "c: across"]),
            Usage("d", stage: ["operate"], feeds: ["a: all the way back"], related: [".devbook/ai/01.md#c"]),
            Usage("h", layer: ["harness"], feeds: ["a: up from the harness", "d: across the harness"]));

        Assert.Equal(10, layout.Edges.Count);
        AssertRoutesAreClean(layout);
    }

    [Fact]
    public void Two_way_neighbours_and_a_hop_round_a_box_in_one_column_stay_clear()
    {
        var layout = Layout(
            Usage("p", stage: ["specify"], feeds: ["q: there"]),
            Usage("q", stage: ["code"], feeds: ["p: and back"]),
            Usage("top", stage: ["decide"], feeds: ["bottom: round the middle"]),
            Usage("middle", stage: ["decide"]),
            Usage("bottom", stage: ["decide"], feeds: ["top: and up again"]));

        Assert.Equal(4, layout.Edges.Count);
        AssertRoutesAreClean(layout);

        // Between neighbours on one row each way runs straight, at its own height.
        var there = Edge(layout, "p", "q", AiCohesionEdgeKind.Feed);
        var back = Edge(layout, "q", "p", AiCohesionEdgeKind.Feed);
        Assert.Equal(2, there.Points.Count);
        Assert.Equal(2, back.Points.Count);
        Assert.NotEqual(there.Points[0].Y, back.Points[0].Y);

        // Round the box in the middle: out and back in on the right, through the gutter.
        var round = Edge(layout, "top", "bottom", AiCohesionEdgeKind.Feed);
        Assert.Equal(4, round.Points.Count);
        Assert.InRange(round.Points[1].X, AiCohesionLayout.ColumnX(0) + W, AiCohesionLayout.ColumnX(1));
    }

    [Fact]
    public void A_model_with_nothing_on_it_still_lays_out_the_path_and_the_bands()
    {
        var layout = AiCohesionLayout.From(AiModel.From([]));

        Assert.Empty(layout.Nodes);
        Assert.Empty(layout.Edges);
        Assert.Equal(7, layout.Columns.Count);
        Assert.Equal([1, 1, 1], layout.Bands.Select(band => band.Rows));
        Assert.True(layout.Height > layout.LaneHeight);
    }

    [Fact]
    public void No_run_crosses_a_band_s_label()
    {
        var layout = Layout(FullBoard());

        var runs = layout.Edges.SelectMany(Segments).Where(segment => segment.A.Y == segment.B.Y);
        Assert.All(runs, run => Assert.DoesNotContain(layout.Bands, band => run.A.Y >= band.Y && run.A.Y <= band.Y + AiCohesionLayout.BandHeaderHeight));
    }

    /// <summary>No two boxes overlap; every segment is orthogonal, misses every
    /// box and runs along no other edge; every edge leaves and reaches its own box.</summary>
    private static void AssertRoutesAreClean(AiCohesionLayout layout)
    {
        // No two boxes overlap.
        foreach (var (a, i) in layout.Nodes.Select((node, i) => (node, i)))
        {
            foreach (var b in layout.Nodes.Skip(i + 1))
            {
                Assert.False(a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom, $"{a.Usage.Heading} overlaps {b.Usage.Heading}");
            }
        }

        var all = layout.Edges.SelectMany((edge, index) => Segments(edge).Select(segment => (index, edge, segment))).ToList();
        foreach (var (_, edge, segment) in all)
        {
            Assert.True(segment.A.X == segment.B.X || segment.A.Y == segment.B.Y, $"{Name(edge)} has a diagonal segment");
            foreach (var node in layout.Nodes)
            {
                Assert.False(Crosses(segment, node), $"{Name(edge)} runs through {node.Usage.Heading}");
            }
        }

        for (var i = 0; i < all.Count; i++)
        {
            for (var j = i + 1; j < all.Count; j++)
            {
                if (all[i].index == all[j].index) continue;
                Assert.False(Overlap(all[i].segment, all[j].segment), $"{Name(all[i].edge)} runs along {Name(all[j].edge)}");
            }
        }

        foreach (var edge in layout.Edges)
        {
            Assert.True(OnBoundary(edge.Points[0], Node(layout, edge.From.Heading)), $"{Name(edge)} does not leave its box");
            Assert.True(OnBoundary(edge.Points[^1], Node(layout, edge.To.Heading)), $"{Name(edge)} does not reach its box");
        }

    }

    /// <summary>The design's own Cohesion board: 32 usages, 25 hand-offs and 9
    /// support links.</summary>
    private static Fixture[] FullBoard() =>
    [
        Usage("sweep", "trial", stage: ["decide", "code"], feeds: ["pv: draft pull requests park here"]),
        Usage("tracker", "adopted", stage: ["decide"], feeds: ["spec: what you pick"]),
        Usage("findings", "retired", stage: ["decide"], related: [".devbook/ai/01.md#sweep"]),
        Usage("remarks", "trial", stage: ["specify"], feeds: ["spec: answers in the chapter"]),
        Usage("spec", "adopted", stage: ["specify"], layer: ["thread"], feeds: ["import: agreed spec", "gate: apply-change"]),
        Usage("import", "adopted", stage: ["specify"], feeds: ["paste: prompt entries"]),
        Usage("estimate", "trial", stage: ["specify"], feeds: ["import: effort:"]),
        Usage("gate", "adopted", stage: ["code", "verify"], feeds: ["qa: verify", "checks: spec check"]),
        Usage("paste", "adopted", stage: ["code"], feeds: ["gate: through the gate"]),
        Usage("checks", "trial", stage: ["verify"], layer: ["thread"], feeds: ["pv: drift found"]),
        Usage("qa", "adopted", stage: ["verify"], feeds: ["pv: evidence"]),
        Usage("pv", "adopted", stage: ["verify", "integrate"], feeds: ["merge: pull request"]),
        Usage("procs", "trial", stage: ["verify"], related: [".devbook/ai/01.md#qa"]),
        Usage("mreview", "trial", stage: ["integrate"], feeds: ["merge: verdict comment"]),
        Usage("mergeready", "trial", stage: ["integrate"], feeds: ["merge: green, level"]),
        Usage("merge", "adopted", stage: ["integrate"], feeds: ["deploy: main"]),
        Usage("ci", "adopted", stage: ["integrate"], feeds: ["merge: checks pass"]),
        Usage("deploy", "adopted", stage: ["release"], feeds: ["alerts: exceptions"]),
        Usage("reports", "trial", stage: ["operate"]),
        Usage("security", "candidate", stage: ["operate"], feeds: ["sweep: would open issues"]),
        Usage("alerts", "candidate", stage: ["operate"], feeds: ["tracker: would enter Decide"]),
        Usage("sync", "candidate", layer: ["thread"], feeds: ["pv: draft pull requests"]),
        Usage("upkeep", "trial", layer: ["upkeep"], feeds: ["ci: pull requests", "sync: drift issues"], related: [".devbook/ai/01.md#checks"]),
        Usage("instr", "trial", layer: ["upkeep"], feeds: ["ci: draft pull request"], related: [".devbook/ai/01.md#rules"]),
        Usage("pkg", "trial", layer: ["upkeep"], feeds: ["ci: package pull request"]),
        Usage("guidance", "retired", layer: ["harness"], related: [".devbook/ai/01.md#rules"]),
        Usage("rules", "adopted", layer: ["harness"], related: [".devbook/ai/01.md#gate"]),
        Usage("worktree", "adopted", layer: ["harness"], related: [".devbook/ai/01.md#paste"]),
        Usage("roles", "adopted", layer: ["harness"], related: [".devbook/ai/01.md#gate"]),
        Usage("runs", "adopted", layer: ["harness"]),
        Usage("telemetry", "adopted", layer: ["harness"], related: [".devbook/ai/01.md#runs"]),
        Usage("limits", "trial", layer: ["harness"])
    ];

    private readonly record struct Segment(AiCohesionPoint A, AiCohesionPoint B);

    private static IEnumerable<Segment> Segments(AiCohesionEdge edge) =>
        edge.Points.Zip(edge.Points.Skip(1), (a, b) => new Segment(a, b));

    private static double Length(Segment segment) =>
        Math.Abs(segment.B.X - segment.A.X) + Math.Abs(segment.B.Y - segment.A.Y);

    /// <summary>Whether the segment enters the box's interior; touching its edge is fine.</summary>
    private static bool Crosses(Segment segment, AiCohesionNode node)
    {
        var (x1, x2) = (Math.Min(segment.A.X, segment.B.X), Math.Max(segment.A.X, segment.B.X));
        var (y1, y2) = (Math.Min(segment.A.Y, segment.B.Y), Math.Max(segment.A.Y, segment.B.Y));
        return x1 < node.Right && node.X < x2 && y1 < node.Bottom && node.Y < y2
            || (x1 == x2 && node.X < x1 && x1 < node.Right && y1 < node.Bottom && node.Y < y2)
            || (y1 == y2 && node.Y < y1 && y1 < node.Bottom && x1 < node.Right && node.X < x2);
    }

    /// <summary>Whether two segments lie on one line and share a stretch of it.</summary>
    private static bool Overlap(Segment s, Segment t)
    {
        if (s.A.X == s.B.X && t.A.X == t.B.X && s.A.X == t.A.X)
        {
            return Math.Max(Math.Min(s.A.Y, s.B.Y), Math.Min(t.A.Y, t.B.Y)) < Math.Min(Math.Max(s.A.Y, s.B.Y), Math.Max(t.A.Y, t.B.Y));
        }

        if (s.A.Y == s.B.Y && t.A.Y == t.B.Y && s.A.Y == t.A.Y)
        {
            return Math.Max(Math.Min(s.A.X, s.B.X), Math.Min(t.A.X, t.B.X)) < Math.Min(Math.Max(s.A.X, s.B.X), Math.Max(t.A.X, t.B.X));
        }

        return false;
    }

    private static bool OnBoundary(AiCohesionPoint point, AiCohesionNode node) =>
        ((point.X == node.X || point.X == node.Right) && point.Y >= node.Y && point.Y <= node.Bottom)
        || ((point.Y == node.Y || point.Y == node.Bottom) && point.X >= node.X && point.X <= node.Right);

    private static string Name(AiCohesionEdge edge) => $"{edge.From.Heading} → {edge.To.Heading}";

    private static AiCohesionNode Node(AiCohesionLayout layout, string heading) =>
        layout.Nodes.Single(node => node.Usage.Heading == heading);

    private static AiCohesionEdge Edge(AiCohesionLayout layout, string from, string to, AiCohesionEdgeKind kind) =>
        layout.Edges.Single(edge => edge.From.Heading == from && edge.To.Heading == to && edge.Kind == kind);

    private sealed record Fixture(string Heading, string Status, string[]? Stage, string[]? Layer, string[]? Feeds, string[]? Related);

    private static Fixture Usage(
        string heading,
        string status = "adopted",
        string[]? stage = null,
        string[]? layer = null,
        string[]? feeds = null,
        string[]? related = null) =>
        new(heading, status, stage, layer, feeds, related);

    /// <summary>Every usage as a chapter of one file, <c>01.md</c>; a feed is
    /// written <c>"&lt;heading&gt;: &lt;label&gt;"</c> and addressed in that file.</summary>
    private static AiCohesionLayout Layout(params Fixture[] usages)
    {
        var sections = usages.Select(usage =>
        {
            var extra = new Dictionary<string, IReadOnlyList<string>>();
            if (usage.Stage is not null) extra["stage"] = usage.Stage;
            if (usage.Layer is not null) extra["layer"] = usage.Layer;
            if (usage.Feeds is not null) extra["feeds"] = [.. usage.Feeds.Select(feed => ".devbook/ai/01.md#" + feed)];

            var meta = new MetadataRecord
            {
                Status = usage.Status,
                Type = "skill",
                Extra = extra,
                Related = [.. (usage.Related ?? []).Select(raw => DevbookReference.Parse(raw)!)]
            };
            return new DocumentDevbookSection(usage.Heading, $"ai-{usage.Heading}", meta, []);
        });

        var model = AiModel.From([new DocumentDevbookFile("01.md", "01.md", string.Empty, MetadataRecord.Empty, [.. sections])]);
        return AiCohesionLayout.From(model);
    }
}
