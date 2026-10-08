using Backlog.Modules.Sessions.UI;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The stage list a run's fold draws: the legend, each stage's chips and mark, and one
/// panel open at a time with the stage's whole execution. What each value means is
/// <c>DeliveryRunStageResolutionTests</c>'; this is what the list makes of it.
/// </summary>
public sealed class DeliveryRunStagesTests
{
    private const string Context = """
        {"layers":[],"phases":{"flow-code":{
          "phase-scope":{"mode":"delegate","agent":"architecture:architect","skill":"delivery:phase-scope","mcp":["backlog"]},
          "phase-implement":{"mode":"delegate","agent":"csharp-coding:coding","model":"sonnet","effort":"high","before":["devbook:validate"]},
          "phase-personal-validation":{"mode":"gate"}}}}
        """;

    private static DeliveryRun Run() => SessionRowsTests.Run("run-1", "keen-bose-1a2b3c4d", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1)) with
    {
        SkillId = "flow-code",
        RunContext = Context,
        Stages =
        [
            new DeliveryRunStage("Scope", "done", 60_000, 1)
            {
                Execution = """{"mode":"delegate","agent":"architecture:architect"}""",
                SubAgentRuns = [new DeliveryRunSubAgentRun("architecture:architect", "claude-opus-5-5", 141_000, 52_000, 33, Failed: false)]
            },
            new DeliveryRunStage("Implement", "done", 600_000, 2)
            {
                Execution = """{"mode":"delegate","agent":"general-purpose","model":"claude-opus-5-5","effort":"medium"}"""
            },
            new DeliveryRunStage("Personal Validation", "in_progress", null, 0)
            {
                SubAgentRuns = [new DeliveryRunSubAgentRun("csharp-coding:coding", "claude-opus-5-5", 90_000, 30_000, 12, Failed: false)]
            },
            new DeliveryRunStage("Summary", "pending", null, 0)
        ]
    };

    [Fact]
    public void A_legend_sits_above_the_stages()
    {
        using var context = new BunitContext();

        var list = context.Render<DeliveryRunStages>(parameters => parameters.Add(p => p.Run, Run()));
        var root = list.Find("[data-testid='sessions-stages']");

        Assert.Equal("stage-legend", root.Children[0].ClassList[0]);
        Assert.Equal(4, list.FindAll("[data-testid='sessions-stage']").Count);
    }

    [Fact]
    public void Each_stage_wears_its_mode_its_model_its_effort_and_at_most_one_mark()
    {
        using var context = new BunitContext();

        var list = context.Render<DeliveryRunStages>(parameters => parameters.Add(p => p.Run, Run()));
        var stages = list.FindAll("[data-testid='sessions-stage']");

        Assert.Equal("delegate", stages[0].QuerySelector("[data-testid='sessions-stage-mode']")!.TextContent.Trim());
        Assert.Equal("Opus 5.5", stages[0].QuerySelector("[data-testid='sessions-stage-model']")!.TextContent.Trim());
        Assert.Null(stages[0].QuerySelector("[data-testid='sessions-stage-mark']"));
        Assert.Null(stages[0].QuerySelector("[data-testid='sessions-stage-effort']"));

        // Implement ran another agent, model and effort than configured: one ≠, its
        // title listing every difference.
        var implement = stages[1];
        Assert.Equal("medium", implement.QuerySelector("[data-testid='sessions-stage-effort']")!.TextContent.Trim());
        var mark = implement.QuerySelector("[data-testid='sessions-stage-mark']")!;
        Assert.Equal("drift", mark.GetAttribute("data-mark"));
        Assert.Equal(
            "Not as configured: agent csharp-coding:coding → general-purpose · model Sonnet → Opus 5.5 · effort high → medium",
            mark.GetAttribute("title"));

        Assert.Equal("gate", stages[2].QuerySelector("[data-testid='sessions-stage-mode']")!.TextContent.Trim());

        // Summary has no entry in the map and recorded nothing: its mode is inferred.
        Assert.Equal("inferred", stages[3].QuerySelector("[data-testid='sessions-stage-mark']")!.GetAttribute("data-mark"));
    }

    [Fact]
    public void One_panel_is_open_at_a_time()
    {
        using var context = new BunitContext();

        var list = context.Render<DeliveryRunStages>(parameters => parameters.Add(p => p.Run, Run()));

        Assert.All(list.FindAll(".fold__region"), region => Assert.True(region.HasAttribute("hidden")));

        list.FindAll("[data-testid='sessions-stage-toggle']")[0].Click();
        list.FindAll("[data-testid='sessions-stage-toggle']")[1].Click();

        var regions = list.FindAll(".fold__region");
        Assert.True(regions[0].HasAttribute("hidden"));
        Assert.False(regions[1].HasAttribute("hidden"));
        Assert.Single(regions, region => !region.HasAttribute("hidden"));

        // Pressing the open one again closes it.
        list.FindAll("[data-testid='sessions-stage-toggle']")[1].Click();
        Assert.All(list.FindAll(".fold__region"), region => Assert.True(region.HasAttribute("hidden")));
    }

    [Fact]
    public void The_panel_says_what_ran_what_was_configured_and_where_from()
    {
        using var context = new BunitContext();

        var list = context.Render<DeliveryRunStages>(parameters => parameters.Add(p => p.Run, Run()));
        list.FindAll("[data-testid='sessions-stage-toggle']")[1].Click();

        var panel = list.FindAll("[data-testid='sessions-stage-panel']")[1];

        Assert.Equal("Delegated · general-purpose · Opus 5.5 · medium effort", panel.QuerySelector("[data-testid='sessions-stage-ran']")!.TextContent.Trim());
        Assert.Equal("csharp-coding:coding · Sonnet · high effort — from the team default", panel.QuerySelector("[data-testid='sessions-stage-configured']")!.TextContent.Trim());
        Assert.Equal("not recorded", panel.QuerySelector("[data-testid='sessions-stage-skill']")!.TextContent.Trim());
        Assert.Equal("before devbook:validate", panel.QuerySelector("[data-testid='sessions-stage-chores']")!.TextContent.Trim());
        Assert.Contains("Configured vs ran: agent csharp-coding:coding → general-purpose", panel.QuerySelector("[data-testid='sessions-stage-notes']")!.TextContent);
        Assert.Contains("Not recorded: the run saw no sub-agent call", panel.QuerySelector("[data-testid='sessions-stage-workers']")!.TextContent);

        var scope = list.FindAll("[data-testid='sessions-stage-panel']")[0];
        Assert.Equal("delivery:phase-scope", scope.QuerySelector("[data-testid='sessions-stage-skill']")!.TextContent.Trim());
        Assert.Equal("backlog", scope.QuerySelector("[data-testid='sessions-stage-mcp']")!.TextContent.Trim());
        var worker = scope.QuerySelector("[data-testid='sessions-stage-worker']")!;
        Assert.Equal("architecture:architect", worker.QuerySelector(".data-table__mono")!.TextContent.Trim());
        Assert.Equal(
            "Opus 5.5 · 2m 21s · 52.0K tokens · 33 tool calls · completed",
            worker.QuerySelector(".sessions-stage__worker-facts")!.TextContent.Trim());
    }

    [Fact]
    public void A_stage_nobody_reported_on_has_not_run_yet()
    {
        using var context = new BunitContext();

        var list = context.Render<DeliveryRunStages>(parameters => parameters.Add(p => p.Run, Run()));
        var summary = list.FindAll("[data-testid='sessions-stage-panel']")[3];

        Assert.Equal("Not run yet", summary.QuerySelector("[data-testid='sessions-stage-ran']")!.TextContent.Trim());
        Assert.Contains("None: no sub-agent ran", summary.QuerySelector("[data-testid='sessions-stage-workers']")!.TextContent);
    }

    [Fact]
    public void A_sub_agent_under_the_gate_says_it_was_a_revise_round()
    {
        using var context = new BunitContext();

        var list = context.Render<DeliveryRunStages>(parameters => parameters.Add(p => p.Run, Run()));
        var gate = list.FindAll("[data-testid='sessions-stage-panel']")[2];

        Assert.Equal("revise round", gate.QuerySelector("[data-testid='sessions-stage-revise']")!.TextContent.Trim());
        Assert.Equal("Your approval", gate.QuerySelector("[data-testid='sessions-stage-ran']")!.TextContent.Trim());
    }

    [Fact]
    public void Each_stage_row_and_each_sub_agent_row_shows_its_cost_and_marks_an_estimate()
    {
        using var context = new BunitContext();

        IReadOnlyList<DeliveryRunStageCost> costs =
        [
            new(new CostFigure(1_250_000, CostSource.Reported), [new CostFigure(1_000_000, CostSource.Reported)]),
            new(new CostFigure(420_000, CostSource.Estimated), []),
            DeliveryRunStageCost.None(1),
            DeliveryRunStageCost.None(0)
        ];

        var list = context.Render<DeliveryRunStages>(parameters => parameters
            .Add(p => p.Run, Run())
            .Add(p => p.Costs, costs));
        var stages = list.FindAll("[data-testid='sessions-stage']");

        var reported = stages[0].QuerySelector("[data-testid='sessions-stage-cost']")!;
        Assert.Equal("$1.25", reported.TextContent.Trim());
        Assert.Equal("reported", reported.GetAttribute("data-source"));

        var estimated = stages[1].QuerySelector("[data-testid='sessions-stage-cost']")!;
        Assert.Equal("estimated", estimated.GetAttribute("data-source"));
        Assert.Contains("$0.42", estimated.TextContent);
        Assert.Equal("estimated from tokens", estimated.QuerySelector("abbr")!.GetAttribute("title"));

        // With neither source there is no cost on the row, not a zero.
        Assert.Null(stages[2].QuerySelector("[data-testid='sessions-stage-cost']"));
        Assert.Null(stages[3].QuerySelector("[data-testid='sessions-stage-cost']"));

        // The sub-agent row says what its call cost.
        stages[0].QuerySelector("[data-testid='sessions-stage-toggle']")!.Click();
        var worker = list.Find("[data-testid='sessions-stage-worker']");
        Assert.Contains("$1.00 reported", worker.TextContent);
        Assert.Contains("$1.25 reported", list.Find("[data-testid='sessions-stage-ran']").TextContent);
    }

    [Fact]
    public void An_estimated_sub_agent_cost_says_so_and_an_unpriced_one_says_nothing()
    {
        Assert.Contains(
            "$0.0042 estimated from tokens",
            DeliveryRunStages.WorkerFacts(new DeliveryRunStageWorker("Explore", "claude-sonnet-5", null, null, 1_000, 10, 1, false, false, false, true), new CostFigure(4_200, CostSource.Estimated)));

        var none = DeliveryRunStages.WorkerFacts(new DeliveryRunStageWorker("Explore", "claude-sonnet-5", null, null, 1_000, 10, 1, false, false, false, true));
        Assert.DoesNotContain("$", none);
    }

    [Fact]
    public void Without_costs_no_stage_shows_one()
    {
        using var context = new BunitContext();

        var list = context.Render<DeliveryRunStages>(parameters => parameters.Add(p => p.Run, Run()));

        Assert.Empty(list.FindAll("[data-testid='sessions-stage-cost']"));
    }
}
