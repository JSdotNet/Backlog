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

    [Fact]
    public void A_priced_stage_shows_its_share_of_the_run_and_its_ratio_in_its_band_and_an_uncompared_one_its_share_alone()
    {
        using var context = new BunitContext();

        IReadOnlyList<DeliveryRunStageCost> costs =
        [
            new(new CostFigure(3_000_000, CostSource.Reported), []),
            new(new CostFigure(1_000_000, CostSource.Reported), []),
            new(new CostFigure(500_000, CostSource.Reported), []),
            DeliveryRunStageCost.None(0)
        ];

        var comparison = new DeliveryRunCostComparison(
            new CostFigure(4_500_000, CostSource.Reported),
            [
                new StageCostAgainstUsual(costs[0].Stage!, 2 / 3d, new CostAgainstUsual(2.4, 1_250_000, 12)),
                new StageCostAgainstUsual(costs[1].Stage!, 2 / 9d, new CostAgainstUsual(1.03, 970_000, 12)),
                new StageCostAgainstUsual(costs[2].Stage!, 1 / 9d, null),
                null
            ],
            null);

        var list = context.Render<DeliveryRunStages>(parameters => parameters
            .Add(p => p.Run, Run())
            .Add(p => p.Costs, costs)
            .Add(p => p.Comparison, comparison));
        var stages = list.FindAll("[data-testid='sessions-stage']");

        Assert.Equal("67% of the run's cost", stages[0].QuerySelector("[data-testid='sessions-stage-share']")!.TextContent);
        var high = stages[0].QuerySelector("[data-testid='sessions-stage-ratio']")!;
        Assert.Equal("2.4× its usual", high.TextContent);
        Assert.Equal("higher", high.GetAttribute("data-band"));
        Assert.Contains("over the last 12 runs", high.GetAttribute("title"), StringComparison.Ordinal);

        var typical = stages[1].QuerySelector("[data-testid='sessions-stage-ratio']")!;
        Assert.Equal("typical for this stage", typical.TextContent);
        Assert.Equal("typical", typical.GetAttribute("data-band"));

        // Under five earlier runs, or not finished: the share, and no ratio.
        Assert.StartsWith("11%", stages[2].QuerySelector("[data-testid='sessions-stage-share']")!.TextContent);
        Assert.Null(stages[2].QuerySelector("[data-testid='sessions-stage-ratio']"));
        Assert.Null(stages[3].QuerySelector("[data-testid='sessions-stage-usual']"));
    }

    // What each pass found — Stage Counts on the rows, the run's Quality Facts in the panel.

    [Fact]
    public async Task A_run_with_cumulative_facts_shows_each_pass_on_its_row_and_the_run_across_every_pass_in_the_panel()
    {
        using var context = new BunitContext();

        var run = await Fixture.ReadAsync("quality-facts.json");
        var list = context.Render<DeliveryRunStages>(parameters => parameters.Add(p => p.Run, run));
        var stages = list.FindAll("[data-testid='sessions-stage']");

        // The rows: what the latest pass reported, which here ended clean.
        Assert.Equal("round 1", Found(stages[2])!.TextContent.Trim());
        Assert.Equal("round 1 · no blockers", Found(stages[3])!.TextContent.Trim());
        Assert.Equal("clean", Found(stages[3])!.GetAttribute("data-tone"));
        Assert.Equal("no defects", Found(stages[5])!.TextContent.Trim());
        Assert.Null(Found(stages[4]));

        // The panels: the run's record across every pass, which did not.
        Assert.Equal(
            "Latest pass: round 1 · no blockers. Across the run: 3 review rounds, 2 blockers left open over 3 rounds",
            FoundFact(stages[3]));
        Assert.Equal("Latest pass: no defects. Across the run: 1 defect found", FoundFact(stages[5]));
        Assert.Equal("Latest pass: round 1. Across the run: 3 review rounds", FoundFact(stages[2]));

        // Implement's one sub-agent call worked the stage's round.
        Assert.Contains("round 1", stages[2].QuerySelector("[data-testid='sessions-stage-worker']")!.TextContent);
    }

    [Fact]
    public async Task A_run_without_cumulative_facts_shows_its_stage_counts_and_says_nothing_across_the_run()
    {
        using var context = new BunitContext();

        var run = await Fixture.ReadAsync("quality-signals.json");
        var list = context.Render<DeliveryRunStages>(parameters => parameters.Add(p => p.Run, run));
        var stages = list.FindAll("[data-testid='sessions-stage']");

        Assert.Equal("round 2 · no blockers", Found(stages[3])!.TextContent.Trim());

        var verify = Found(stages[5])!;
        Assert.Equal("1 defect found", verify.TextContent.Trim());
        Assert.Equal("found", verify.GetAttribute("data-tone"));

        Assert.Equal("Latest pass: round 2 · no blockers", FoundFact(stages[3]));

        // Each of Review's calls names the round it worked.
        var workers = stages[3].QuerySelectorAll("[data-testid='sessions-stage-worker']");
        Assert.Contains("round 1", workers[0].TextContent);
        Assert.Contains("round 2", workers[1].TextContent);
    }

    [Fact]
    public async Task A_run_from_before_the_counts_shows_none_and_never_a_zero()
    {
        using var context = new BunitContext();

        var run = await Fixture.ReadAsync("delegated-phase-map.json");
        var list = context.Render<DeliveryRunStages>(parameters => parameters.Add(p => p.Run, run));

        Assert.Empty(list.FindAll("[data-testid='sessions-stage-found']"));
        Assert.Empty(list.FindAll("[data-testid='sessions-stage-found-fact']"));
    }

    [Fact]
    public void A_revise_call_carries_its_round_on_its_badge()
    {
        using var context = new BunitContext();

        var run = Run();
        var stages = run.Stages.ToList();
        stages[2] = stages[2] with { Round = 3 };

        var list = context.Render<DeliveryRunStages>(parameters => parameters.Add(p => p.Run, run with { Stages = stages }));
        var gate = list.FindAll("[data-testid='sessions-stage-panel']")[2];

        Assert.Equal("revise · round 3", gate.QuerySelector("[data-testid='sessions-stage-revise']")!.TextContent.Trim());
        Assert.DoesNotContain("round 3", gate.QuerySelector(".sessions-stage__worker-facts")!.TextContent);
    }

    [Fact]
    public void A_sub_agent_call_with_no_round_names_none()
    {
        var worker = new DeliveryRunStageWorker("Explore", "claude-sonnet-5", null, "contract", 1_000, 10, 1, false, false, false, true);

        Assert.DoesNotContain("round", DeliveryRunStages.WorkerFacts(worker));
        Assert.Contains("slice contract · round 2", DeliveryRunStages.WorkerFacts(worker with { Round = 2 }));
        Assert.Equal("revise round", DeliveryRunStages.ReviseLabel(worker));
    }

    [Fact]
    public void A_run_whose_rounds_left_no_blocker_open_says_so_in_words()
    {
        var review = new DeliveryRunStageExecution(
            "Review", "done", DeliveryRunStageMode.Inline, true, null, null, null, null, null, [], [], null, null, [], [], 1, null, null, 0, false)
        {
            Round = 1,
            Blockers = 0
        };
        var facts = new DeliveryQualityFacts(1, 1, 1, 0, 0, 0);

        Assert.Equal(
            "Latest pass: round 1 · no blockers. Across the run: 1 review round, no blockers left open over 1 round",
            DeliveryRunStages.FoundFact(review, facts));
    }

    private static AngleSharp.Dom.IElement? Found(AngleSharp.Dom.IElement stage) =>
        stage.QuerySelector("[data-testid='sessions-stage-found']");

    private static string? FoundFact(AngleSharp.Dom.IElement stage) =>
        stage.QuerySelector("[data-testid='sessions-stage-found-fact']")?.TextContent.Trim();

    /// <summary>A recorded run file read through the product's own reader, laid out as
    /// the Backlog surface lays its folder out.</summary>
    private static class Fixture
    {
        public static async Task<DeliveryRun> ReadAsync(string name)
        {
            var home = Path.Combine(Path.GetTempPath(), "backlog-stage-list-tests", Guid.NewGuid().ToString("n"));
            var folder = Path.Combine(home, "backlog", "fixture-1a2b3c4d", "runs");

            Directory.CreateDirectory(folder);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "delivery-runs", name), Path.Combine(folder, name));

            try
            {
                var catalog = await new Backlog.Infrastructure.Sessions.LocalDeliveryRunSource(home, "machine", "Machine")
                    .GetRunsAsync(TestContext.Current.CancellationToken);

                return Assert.Single(catalog.Runs);
            }
            finally
            {
                Directory.Delete(home, recursive: true);
            }
        }
    }
}
