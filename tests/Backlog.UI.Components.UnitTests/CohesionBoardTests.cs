using AngleSharp.Dom;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The cohesion board: boxes styled by rung with a clock for a scheduled usage,
/// arrows dashed for a candidate and red for the open loop, the four views, the
/// labels on whatever is lit, the panel a picked usage fills, and the platforms
/// row lighting the usages built on one.
/// </summary>
public sealed class CohesionBoardTests
{
    private static CohesionNode N(string id, string status, int stage, bool onPath = true, bool scheduled = false, params string[] platforms) =>
        new(id, id.ToUpperInvariant(), status, scheduled, stage, onPath ? $"Stage {stage}" : "Upkeep loop", onPath, 40 + 226 * (stage - 1), onPath ? 100 : 400, 196, 66, platforms);

    private static CohesionEdge E(string from, string to, CohesionEdgeKind kind = CohesionEdgeKind.Feed, bool dashed = false, string? label = "hands on") =>
        new(from, to, kind, dashed, label, [new(0, 0), new(30, 0), new(30, 40)], new(30, 20));

    // spec (1) → gate (2) forward; spec → plan (1) within a stage; gate → qa (3)
    // forward; alerts (7, candidate) → spec the open loop; upkeep (a layer) → qa
    // out of a layer; worktree supports gate.
    private static readonly CohesionGraph Graph = new(
        1632, 600, 264, 46, 929,
        [new("decide", "Decide", 25, 226, true), new("code", "Code", 251, 226, true), new("verify", "Verify", 477, 226, true), new("integrate", "Integrate", 703, 226, false)],
        [new("upkeep", "Upkeep loop", 380, 102)],
        [
            N("spec", "adopted", 1, platforms: ["engine", "devbook"]),
            N("plan", "trial", 1),
            N("gate", "adopted", 2, platforms: ["engine"]),
            N("qa", "candidate", 3),
            N("alerts", "candidate", 7, platforms: ["engine"]),
            N("upkeep", "trial", 3, onPath: false, scheduled: true, platforms: ["schedule"]),
            N("worktree", "retired", 2, onPath: false)
        ],
        [
            E("spec", "gate", label: "apply-change"),
            E("spec", "plan", label: "agreed spec"),
            E("gate", "qa", dashed: true, label: "verify"),
            E("alerts", "spec", CohesionEdgeKind.OpenLoop, dashed: true, label: "would enter Decide"),
            E("upkeep", "qa", label: "pull requests"),
            E("worktree", "gate", CohesionEdgeKind.Support, label: null)
        ],
        [new("engine", "Delivery engine"), new("devbook", "Devbook plugin"), new("schedule", "Delivery schedule")]);

    private static IRenderedComponent<CohesionBoard> Render(BunitContext context, CohesionView view = CohesionView.HandOffs, Action<string>? opened = null) =>
        context.Render<CohesionBoard>(parameters => parameters
            .Add(p => p.Graph, Graph)
            .Add(p => p.View, view)
            .Add(p => p.OnOpenUsage, (string id) => opened?.Invoke(id)));

    private static IElement Node(IRenderedComponent<CohesionBoard> board, string id) =>
        board.Find($"[data-testid='cohesion-node'][data-id='{id}']");

    private static string[] Edges(IRenderedComponent<CohesionBoard> board, string? withClass = null) =>
        [.. board.FindAll("[data-testid='cohesion-edge']")
            .Where(edge => withClass is null || edge.ClassList.Contains(withClass))
            .Select(edge => $"{edge.GetAttribute("data-from")}>{edge.GetAttribute("data-to")}")];

    private static string[] Labels(IRenderedComponent<CohesionBoard> board) =>
        [.. board.FindAll("[data-testid='cohesion-label']").Select(label => label.TextContent)];

    private static string[] Lit(IRenderedComponent<CohesionBoard> board) =>
        [.. board.FindAll("[data-testid='cohesion-node']")
            .Where(node => !node.ClassList.Contains("cohesion__node--dim"))
            .Select(node => node.GetAttribute("data-id")!)];

    [Fact]
    public void Every_usage_is_a_button_named_by_its_name_rung_schedule_and_place_and_styled_by_its_rung()
    {
        using var context = new BunitContext();
        var board = Render(context);

        var upkeep = Node(board, "upkeep");
        Assert.Equal("BUTTON", upkeep.TagName);
        Assert.Equal("button", upkeep.GetAttribute("type"));
        Assert.Equal("UPKEEP, trial, scheduled, Upkeep loop", upkeep.GetAttribute("aria-label"));
        Assert.Contains("cohesion__node--trial", upkeep.ClassList);
        Assert.NotNull(upkeep.QuerySelector("[data-testid='cohesion-scheduled']"));

        var gate = Node(board, "gate");
        Assert.Equal("GATE, adopted, Stage 2", gate.GetAttribute("aria-label"));
        Assert.Null(gate.QuerySelector("[data-testid='cohesion-scheduled']"));
        Assert.Contains("cohesion__node--candidate", Node(board, "qa").ClassList);
        Assert.Contains("cohesion__node--retired", Node(board, "worktree").ClassList);
        Assert.Equal(7, board.FindAll("[data-testid='cohesion-node']").Count);
    }

    [Fact]
    public void Hand_offs_draws_every_hand_off_with_an_arrowhead_and_no_support_link_or_label()
    {
        using var context = new BunitContext();
        var board = Render(context);

        Assert.Equal(["spec>gate", "spec>plan", "gate>qa", "alerts>spec", "upkeep>qa"], Edges(board));
        Assert.All(board.FindAll("[data-testid='cohesion-edge']"), edge => Assert.StartsWith("M", edge.QuerySelector(".cohesion__head")!.GetAttribute("d")));
        Assert.Equal(["gate>qa", "alerts>spec"], Edges(board, "cohesion__edge--dashed"));
        Assert.Equal(["alerts>spec"], Edges(board, "cohesion__edge--open-loop"));
        Assert.Empty(Labels(board));
        Assert.Equal(7, Lit(board).Length);
        Assert.Empty(board.FindAll("[data-testid='cohesion-step']"));
    }

    [Fact]
    public void Forward_lights_the_hand_offs_to_a_later_stage_with_their_labels_and_numbers_each_usage_by_its_stage()
    {
        using var context = new BunitContext();
        var board = Render(context, CohesionView.Forward);

        Assert.Equal(["spec>gate", "gate>qa"], Edges(board, "cohesion__edge--forward"));
        Assert.Equal(["apply-change", "verify"], Labels(board));
        Assert.Equal(["spec", "gate", "qa"], Lit(board));
        Assert.Equal(["1", "2", "3"], board.FindAll("[data-testid='cohesion-step']").Select(step => step.TextContent));
    }

    [Fact]
    public void What_feeds_back_lights_the_hand_offs_to_an_earlier_stage_or_out_of_a_layer()
    {
        using var context = new BunitContext();
        var board = Render(context, CohesionView.FeedsBack);

        Assert.Equal(["alerts>spec", "upkeep>qa"], Edges(board, "cohesion__edge--back"));
        // The open loop stays red rather than taking the view's colour, and is lit.
        Assert.Equal(["alerts>spec", "upkeep>qa"], Edges(board).Where(edge => !Edges(board, "cohesion__edge--dim").Contains(edge)));
        Assert.Equal(["would enter Decide", "pull requests"], Labels(board));
        Assert.Equal(["spec", "qa", "alerts", "upkeep"], Lit(board));
        Assert.Empty(board.FindAll("[data-testid='cohesion-step']"));
    }

    [Fact]
    public void Everything_adds_the_support_links()
    {
        using var context = new BunitContext();
        var board = Render(context, CohesionView.Everything);

        Assert.Contains("worktree>gate", Edges(board, "cohesion__edge--support"));
        Assert.Equal(6, Edges(board).Length);
        Assert.Empty(Labels(board));
    }

    [Fact]
    public void Picking_a_view_announces_it_and_clears_the_pick()
    {
        using var context = new BunitContext();
        CohesionView? announced = null;
        var board = context.Render<CohesionBoard>(parameters => parameters
            .Add(p => p.Graph, Graph)
            .Add(p => p.SelectedId, "gate")
            .Add(p => p.ViewChanged, (CohesionView view) => announced = view));

        board.Find("[data-testid='cohesion-view-forward']").Click();

        Assert.Equal(CohesionView.Forward, announced);
        Assert.Equal("true", board.Find("[data-testid='cohesion-view-forward']").GetAttribute("aria-checked"));
        Assert.All(board.FindAll("[data-testid='cohesion-node']"), node => Assert.Equal("false", node.GetAttribute("aria-pressed")));
    }

    [Fact]
    public void Picking_a_usage_lights_its_neighbours_shows_its_support_links_and_fills_the_panel()
    {
        using var context = new BunitContext();
        string? opened = null;
        var board = Render(context, opened: id => opened = id);

        Node(board, "gate").Click();

        Assert.Equal("true", Node(board, "gate").GetAttribute("aria-pressed"));
        Assert.Equal(["spec", "gate", "qa", "worktree"], Lit(board));
        Assert.Contains("worktree>gate", Edges(board));
        Assert.Equal(["spec>gate", "gate>qa", "worktree>gate"], Edges(board, "cohesion__edge--picked"));
        Assert.Equal(["apply-change", "verify"], Labels(board));

        var detail = board.Find("[data-testid='cohesion-detail']");
        Assert.Contains("GATE", detail.TextContent);
        Assert.Contains("SPEC (apply-change)", board.Find("[data-testid='cohesion-fed-by']").TextContent);
        Assert.Contains("QA (verify)", board.Find("[data-testid='cohesion-hands-on-to']").TextContent);
        Assert.Contains("Delivery engine", board.Find("[data-testid='cohesion-built-on']").TextContent);

        board.Find("[data-testid='cohesion-open']").Click();
        Assert.Equal("gate", opened);

        Node(board, "gate").Click();
        Assert.Equal("false", Node(board, "gate").GetAttribute("aria-pressed"));
        Assert.DoesNotContain("worktree>gate", Edges(board));
        Assert.Contains("Pick any usage", board.Find("[data-testid='cohesion-detail']").TextContent);
    }

    [Fact]
    public void The_panel_says_nothing_recorded_where_a_usage_has_no_links()
    {
        using var context = new BunitContext();
        var board = context.Render<CohesionBoard>(parameters => parameters
            .Add(p => p.Graph, Graph)
            .Add(p => p.SelectedId, "plan"));

        Assert.Contains("nothing recorded", board.Find("[data-testid='cohesion-hands-on-to']").TextContent);
        Assert.Contains("nothing recorded", board.Find("[data-testid='cohesion-built-on']").TextContent);
        Assert.Empty(board.FindAll("[data-testid='cohesion-open']"));
    }

    [Fact]
    public void The_platforms_row_counts_the_usages_on_each_and_picking_one_lights_them()
    {
        using var context = new BunitContext();
        var board = Render(context);

        var platforms = board.FindAll("[data-testid='cohesion-platform']");
        Assert.Equal(["engine", "schedule", "devbook"], platforms.Select(platform => platform.GetAttribute("data-id")));
        Assert.Equal(["3", "1", "1"], platforms.Select(platform => platform.QuerySelector(".cohesion__platform-count")!.TextContent));
        Assert.Equal("Delivery engine, 3 usages", platforms[0].GetAttribute("aria-label"));

        platforms[0].Click();

        Assert.Equal("true", board.FindAll("[data-testid='cohesion-platform']")[0].GetAttribute("aria-pressed"));
        Assert.Equal(["spec", "gate", "alerts"], Lit(board));
        Assert.Empty(Labels(board));
        Assert.Contains("3 usages built on it", board.Find("[data-testid='cohesion-detail']").TextContent);
        Assert.Equal("SPEC · GATE · ALERTS", board.Find("[data-testid='cohesion-platform-users']").TextContent);

        // Picking a usage drops the platform.
        Node(board, "qa").Click();
        Assert.Equal("false", board.FindAll("[data-testid='cohesion-platform']")[0].GetAttribute("aria-pressed"));
    }

    [Fact]
    public void An_arrowhead_points_along_the_last_segment_and_the_line_stops_short_of_it()
    {
        CohesionPoint[] points = [new(0, 0), new(30, 0), new(30, 40)];

        Assert.Equal("M30 40 L25.5 31 L34.5 31 Z", CohesionBoard.HeadPath(points));
        Assert.EndsWith("L30 33", CohesionBoard.LinePath(points));
    }
}
