namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The delivery path board: stage heads with their where-labels and the
/// pull-request marker, cards that say "no AI" when empty, the feedback arrow,
/// the layers beneath, and a picked usage lighting every card it sits in.
/// </summary>
public sealed class DeliveryPathBoardTests
{
    private static readonly DeliveryPathUsage Gate = new("code-gate", "Code gate", "adopted");
    private static readonly DeliveryPathUsage Qa = new("qa", "QA agent", "trial");
    private static readonly DeliveryPathUsage Spec = new("spec", "Chapters", "adopted");

    private static readonly IReadOnlyList<DeliveryPathStage> Stages =
    [
        new("specify", "Specify & plan", "attended", true, [new("Specify", [Spec])]),
        new("code", "Code", "your session", true, [new("Start", [Gate]), new("Implement", [Gate])]),
        new("verify", "Verify", "your session", true, [new(null, [Qa])]),
        new("integrate", "Integrate", "on the PR", false, [new(null, [])]),
        new("operate", "Operate & feedback", "production", false, [new(null, [])])
    ];

    private static readonly IReadOnlyList<DeliveryPathLayer> Layers =
    [
        new("thread", "Spec ↔ code thread", [Spec, Gate]),
        new("upkeep", "Upkeep loop", [])
    ];

    private static IRenderedComponent<DeliveryPathBoard> Render(BunitContext context, Action<string>? opened = null) =>
        context.Render<DeliveryPathBoard>(parameters => parameters
            .Add(p => p.Stages, Stages)
            .Add(p => p.Layers, Layers)
            .Add(p => p.OnOpenUsage, (string id) => opened?.Invoke(id)));

    [Fact]
    public void Every_stage_has_its_number_where_label_and_name_and_the_first_after_the_pull_request_its_marker()
    {
        using var context = new BunitContext();
        var board = Render(context);

        var heads = board.FindAll(".delivery-path__head");
        Assert.Equal(5, heads.Count);
        Assert.Equal(["01", "02", "03", "04", "05"], board.FindAll(".delivery-path__number").Select(n => n.TextContent));
        Assert.Equal(["attended", "your session", "your session", "on the PR", "production"], board.FindAll(".delivery-path__where").Select(n => n.TextContent));

        var marker = Assert.Single(board.FindAll("[data-testid='delivery-path-pull-request']"));
        Assert.Contains("Integrate", marker.ParentElement!.TextContent);
        Assert.Contains("delivery-path__stage--pull-request", board.Find("[data-testid='delivery-path-stage-integrate']").ClassName);
    }

    [Fact]
    public void Named_cards_carry_their_name_and_an_empty_card_says_no_ai()
    {
        using var context = new BunitContext();
        var board = Render(context);

        Assert.Equal(["Specify", "Start", "Implement"], board.FindAll(".delivery-path__card-name").Select(n => n.TextContent));

        // Integrate, Operate and the upkeep layer hold nothing.
        Assert.Equal(3, board.FindAll(".delivery-path__none").Count);
        Assert.All(board.FindAll(".delivery-path__none"), none => Assert.Equal("no AI", none.TextContent));
    }

    [Fact]
    public void The_feedback_arrow_runs_back_into_the_first_stage()
    {
        using var context = new BunitContext();
        var board = Render(context);

        Assert.Equal("feedback into Specify & plan", board.Find(".delivery-path__feedback-label").TextContent);
    }

    [Fact]
    public void The_layers_are_rows_beneath_the_path()
    {
        using var context = new BunitContext();
        var board = Render(context);

        Assert.Equal(["Spec ↔ code thread", "Upkeep loop"], board.FindAll(".delivery-path__layer-name").Select(n => n.TextContent));
        Assert.Equal(2, board.FindAll("[data-testid='delivery-path-layer-thread'] .delivery-path__usage").Count);
    }

    [Fact]
    public void Picking_a_usage_lights_every_card_it_sits_in_and_dims_the_rest()
    {
        using var context = new BunitContext();
        var board = Render(context);

        Assert.Empty(board.FindAll(".delivery-path__card--lit"));
        Assert.Empty(board.FindAll(".delivery-path__card--dim"));

        board.Find("[data-usage-id='code-gate']").Click();

        // Start, Implement and the thread row hold it; the other five cards do not.
        Assert.Equal(3, board.FindAll(".delivery-path__card--lit").Count);
        Assert.Equal(5, board.FindAll(".delivery-path__card--dim").Count);
        Assert.All(board.FindAll("[data-usage-id='code-gate']"), chip => Assert.Equal("true", chip.GetAttribute("aria-pressed")));
        Assert.Equal("in 3 places", board.Find("[data-testid='delivery-path-reach']").TextContent);

        // Picking it again puts the board back at rest.
        board.FindAll("[data-usage-id='code-gate']")[0].Click();
        Assert.Empty(board.FindAll(".delivery-path__card--lit"));
        Assert.Empty(board.FindAll(".delivery-path__card--dim"));
    }

    [Fact]
    public void Open_chapter_hands_back_the_picked_usage_s_id()
    {
        using var context = new BunitContext();
        var opened = new List<string>();
        var board = Render(context, opened.Add);

        board.Find("[data-usage-id='qa']").Click();
        board.Find("[data-testid='delivery-path-open']").Click();

        Assert.Equal(["qa"], opened);
    }
}
