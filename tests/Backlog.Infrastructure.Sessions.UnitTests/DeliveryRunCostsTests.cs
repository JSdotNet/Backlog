namespace Backlog.Infrastructure.Sessions.UnitTests;

/// <summary>
/// What a session, a delivery-run stage and a sub-agent call cost: Claude Code's
/// reported <c>api_request</c> cost matched to the stage's window and its workers, and
/// where nothing was reported, an estimate from the run's tokens at the person's rates —
/// and with neither, no figure at all rather than a zero.
/// </summary>
public sealed class DeliveryRunCostsTests
{
    private const string Owner = "session-owner";

    private static readonly DateTimeOffset Nine = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    private static readonly ModelPriceTable Prices = new(
    [
        new ModelPrice("claude-opus-5-5", 5m, 25m, 0.5m, 6.25m),
        new ModelPrice("sonnet", 3m, 15m, 0.3m, 3.75m)
    ]);

    private static ClaudeApiRequest Request(
        string id,
        DateTimeOffset at,
        long? micros,
        string? agent = null,
        string? source = null,
        string session = Owner) =>
        new(id, session, at, "claude-opus-5-5", "high", micros, 10, 100, 1000, 100, 500, source ?? (agent is null ? "repl_main_thread" : "agent:custom"), agent, null, null);

    /// <summary>A run of two stages: Scope 9:00–9:10 inline, Implement 9:10–9:30 with
    /// one call of <c>csharp-coding:coding</c> ending 9:25 after 10 minutes.</summary>
    private static DeliveryRun Run(
        DeliveryRunTokenUsage? usage = null,
        IReadOnlyList<string>? sessions = null,
        DeliveryRunSubAgentRun? call = null,
        DateTimeOffset? implementEnd = null)
    {
        var scope = new DeliveryRunStage("Scope", "done", 600_000, 1)
        {
            StartedAt = Nine,
            CompletedAt = Nine.AddMinutes(10),
            Execution = """{"mode":"inline","model":"claude-opus-5-5"}"""
        };

        var implement = new DeliveryRunStage("Implement", "done", 1_200_000, 1)
        {
            StartedAt = Nine.AddMinutes(10),
            CompletedAt = implementEnd ?? Nine.AddMinutes(30),
            SubAgentRuns =
            [
                call ?? new DeliveryRunSubAgentRun("csharp-coding:coding", "claude-sonnet-5", 600_000, 4000, 12, false)
                {
                    EndedAt = Nine.AddMinutes(25)
                }
            ]
        };

        return new DeliveryRun(
            "run-1", "backlog", "wt-1a2b3c4d", "wt", "machine", "Machine", "flow-code", "A run", "done", null, [],
            Nine, Nine.AddMinutes(30), [scope, implement], usage, null, [], [])
        {
            SessionIds = sessions ?? [Owner]
        };
    }

    private static DeliveryRunTokens Tokens(long input, long output, long cacheRead, long cacheWrite) =>
        new(1, input, output, 0, cacheRead, cacheWrite);

    private static DeliveryRunTokenUsage Usage() =>
        new(
            Tokens(3_000, 3_000, 3_000_000, 300_000),
            Tokens(2_000, 2_000, 2_000_000, 200_000),
            [
                new DeliveryRunStageTokens("Scope", Tokens(1_000, 1_000, 1_000_000, 100_000), Tokens(0, 0, 0, 0)),
                new DeliveryRunStageTokens("Implement", Tokens(2_000, 2_000, 2_000_000, 200_000), Tokens(2_000, 2_000, 2_000_000, 200_000))
            ],
            ["claude-opus-5-5", "claude-sonnet-5"]);

    [Fact]
    public void A_session_costs_the_sum_Claude_Code_reported_for_it()
    {
        var requests = new[]
        {
            Request("a", Nine, 120_000),
            Request("b", Nine.AddMinutes(5), 30_000, agent: "Explore"),
            Request("c", Nine.AddMinutes(6), 5_000, session: "another-session")
        };

        var cost = DeliveryRunCosts.Session(Owner, requests, [], ModelPriceTable.Empty, null);

        Assert.Equal(new CostFigure(150_000, CostSource.Reported), cost);
        Assert.Equal("$0.15", cost!.Label);
    }

    [Fact]
    public void A_stage_takes_the_owner_sessions_main_loop_requests_inside_its_window()
    {
        var run = Run();
        var requests = new[]
        {
            Request("before", Nine.AddSeconds(-1), 1),
            Request("start", Nine, 10),
            Request("middle", Nine.AddMinutes(5), 100),
            Request("end", Nine.AddMinutes(10), 1_000),
            Request("after", Nine.AddMinutes(10).AddSeconds(1), 10_000),
            Request("other-session", Nine.AddMinutes(5), 100_000, session: "another-session")
        };

        var costs = DeliveryRunCosts.Of(run, requests, ModelPriceTable.Empty);

        // Both ends are inside the window; the request a second after the end is the
        // next stage's, and another session's request is nobody's here.
        Assert.Equal(new CostFigure(1_110, CostSource.Reported), costs[0].Stage);
    }

    [Fact]
    public void A_stage_takes_the_sub_agent_requests_whose_agent_name_or_query_source_matches_its_workers()
    {
        var run = Run();
        var requests = new[]
        {
            Request("main", Nine.AddMinutes(12), 1),
            Request("by-name", Nine.AddMinutes(16), 10, agent: "csharp-coding:coding"),
            Request("by-short-name", Nine.AddMinutes(17), 100, agent: "coding"),
            Request("by-source", Nine.AddMinutes(18), 1_000, source: "agent:csharp-coding:coding"),
            Request("someone-else", Nine.AddMinutes(19), 10_000, agent: "Explore")
        };

        var costs = DeliveryRunCosts.Of(run, requests, ModelPriceTable.Empty);

        Assert.Equal(new CostFigure(1_111, CostSource.Reported), costs[1].Stage);

        // The worker's own call, 9:15–9:25, holds its three requests and not the main
        // loop's, nor another agent's.
        Assert.Equal(new CostFigure(1_110, CostSource.Reported), Assert.Single(costs[1].Workers));

        // A sub-agent request is never the main loop's, even inside a window.
        Assert.Null(costs[0].Stage);
    }

    [Fact]
    public void A_sub_agent_request_after_the_stage_ends_is_still_its_workers_while_the_call_runs()
    {
        // The stage was marked done at 9:20 while its worker ran on to 9:25.
        var run = Run(implementEnd: Nine.AddMinutes(20));
        var requests = new[]
        {
            Request("late", Nine.AddMinutes(24), 500, agent: "csharp-coding:coding"),
            Request("after-call", Nine.AddMinutes(26), 5_000, agent: "csharp-coding:coding")
        };

        var stage = DeliveryRunCosts.Of(run, requests, ModelPriceTable.Empty)[1];

        Assert.Equal(new CostFigure(500, CostSource.Reported), stage.Stage);
        Assert.Equal(new CostFigure(500, CostSource.Reported), stage.Workers[0]);
    }

    [Fact]
    public void A_call_with_no_end_claims_its_agents_requests_in_the_stage_only_when_it_is_the_only_call()
    {
        var run = Run(call: new DeliveryRunSubAgentRun("csharp-coding:coding", "claude-sonnet-5", null, null, null, false));
        var requests = new[] { Request("in-stage", Nine.AddMinutes(15), 42, agent: "coding") };

        Assert.Equal(new CostFigure(42, CostSource.Reported), DeliveryRunCosts.Of(run, requests, ModelPriceTable.Empty)[1].Workers[0]);
    }

    [Fact]
    public void A_run_naming_no_session_is_matched_in_the_session_it_was_joined_to()
    {
        var run = Run(sessions: []);
        var requests = new[] { Request("a", Nine.AddMinutes(1), 7, session: "joined") };

        Assert.Null(DeliveryRunCosts.Of(run, requests, ModelPriceTable.Empty)[0].Stage);
        Assert.Equal(new CostFigure(7, CostSource.Reported), DeliveryRunCosts.Of(run, requests, ModelPriceTable.Empty, sessionId: "joined")[0].Stage);
    }

    [Fact]
    public void A_stage_still_under_way_has_no_end_to_its_window()
    {
        var run = Run();
        var open = run with
        {
            Stages = [run.Stages[0] with { Status = "in_progress", DoneCount = 0, CompletedAt = null }, run.Stages[1]]
        };

        var requests = new[] { Request("hours-later", Nine.AddHours(3), 9) };

        Assert.Equal(new CostFigure(9, CostSource.Reported), DeliveryRunCosts.Of(open, requests, ModelPriceTable.Empty)[0].Stage);
    }

    [Fact]
    public void Without_telemetry_a_stage_is_estimated_from_its_tokens_at_the_rates_in_settings()
    {
        var costs = DeliveryRunCosts.Of(Run(Usage()), [], Prices, ownerModel: "claude-opus-5-5");

        // Scope ran inline on Opus: 1,000 in × $5 + 1,000 out × $25 + 1M cache read ×
        // $0.50 + 100k cache write × $6.25, per million tokens.
        Assert.Equal(new CostFigure(5_000 + 25_000 + 500_000 + 625_000, CostSource.Estimated), costs[0].Stage);
        Assert.True(costs[0].Stage!.Estimated);

        // Implement's tokens are all its worker's, on Sonnet, found by family.
        var sonnet = 2_000 * 3 + 2_000 * 15 + 2_000_000 * 0.3m + 200_000 * 3.75m;
        Assert.Equal(new CostFigure((long)sonnet, CostSource.Estimated), costs[1].Stage);
        Assert.Equal(new CostFigure((long)sonnet, CostSource.Estimated), costs[1].Workers[0]);
    }

    [Fact]
    public void Reported_cost_wins_over_an_estimate()
    {
        var costs = DeliveryRunCosts.Of(Run(Usage()), [Request("a", Nine.AddMinutes(1), 3)], Prices, ownerModel: "claude-opus-5-5");

        Assert.Equal(new CostFigure(3, CostSource.Reported), costs[0].Stage);
        Assert.Equal(CostSource.Estimated, costs[1].Stage!.Source);
    }

    [Fact]
    public void With_neither_telemetry_nor_a_price_there_is_no_cost_rather_than_zero()
    {
        var empty = DeliveryRunCosts.Of(Run(Usage()), [], ModelPriceTable.Empty, ownerModel: "claude-opus-5-5");

        Assert.All(empty, stage => Assert.Null(stage.Stage));
        Assert.All(empty, stage => Assert.All(stage.Workers, Assert.Null));

        // A model the table does not name prices nothing, and neither does a rate left empty.
        var opusOnly = new ModelPriceTable([new ModelPrice("opus", 5m, 25m, 0.5m, null)]);
        var costs = DeliveryRunCosts.Of(Run(Usage()), [], opusOnly, ownerModel: "claude-opus-5-5");

        Assert.Null(costs[0].Stage);
        Assert.Null(costs[1].Stage);

        // No tokens counted is no figure either.
        Assert.All(DeliveryRunCosts.Of(Run(), [], Prices, ownerModel: "claude-opus-5-5"), stage => Assert.Null(stage.Stage));
        Assert.Null(DeliveryRunCosts.Session(Owner, [], [Run()], Prices, "claude-opus-5-5"));
    }

    [Fact]
    public void A_session_with_no_telemetry_sums_its_runs_estimates()
    {
        var cost = DeliveryRunCosts.Session(Owner, [], [Run(Usage())], Prices, "claude-opus-5-5");

        var expected = DeliveryRunCosts.Of(Run(Usage()), [], Prices, ownerModel: "claude-opus-5-5")
            .Select(stage => stage.Stage!.UsdMicros)
            .Sum();

        Assert.Equal(new CostFigure(expected, CostSource.Estimated), cost);

        // A stage that spent tokens nothing can price leaves the session unpriced: a
        // partial sum would read as the whole.
        var opusOnly = new ModelPriceTable([new ModelPrice("claude-opus-5-5", 5m, 25m, 0.5m, 6.25m)]);
        Assert.Null(DeliveryRunCosts.Session(Owner, [], [Run(Usage())], opusOnly, "claude-opus-5-5"));
    }

    [Fact]
    public void Sub_agent_calls_share_the_stages_sub_agent_tokens_by_what_each_reported()
    {
        var run = Run(Usage());
        var implement = run.Stages[1] with
        {
            SubAgentRuns =
            [
                new DeliveryRunSubAgentRun("csharp-coding:coding", "claude-sonnet-5", 1_000, 3_000, 1, false),
                new DeliveryRunSubAgentRun("Explore", "claude-sonnet-5", 1_000, 1_000, 1, false)
            ]
        };

        var stage = DeliveryRunCosts.Of(run with { Stages = [run.Stages[0], implement] }, [], Prices)[1];

        var whole = 2_000 * 3 + 2_000 * 15 + 2_000_000 * 0.3m + 200_000 * 3.75m;
        Assert.Equal((long)(whole * 3 / 4), stage.Workers[0]!.UsdMicros);
        Assert.Equal((long)(whole / 4), stage.Workers[1]!.UsdMicros);
    }

    [Fact]
    public void Two_overlapping_calls_of_one_agent_claim_no_requests_and_fall_back_to_their_estimates()
    {
        var run = Run(Usage());
        var implement = run.Stages[1] with
        {
            SubAgentRuns =
            [
                new DeliveryRunSubAgentRun("csharp-coding:coding", "claude-sonnet-5", 600_000, 2_000, 1, false) { EndedAt = Nine.AddMinutes(25) },
                new DeliveryRunSubAgentRun("csharp-coding:coding", "claude-sonnet-5", 300_000, 2_000, 1, false) { EndedAt = Nine.AddMinutes(24) }
            ]
        };
        var fanOut = run with { Stages = [run.Stages[0], implement] };
        var requests = new[] { Request("shared", Nine.AddMinutes(21), 900, agent: "coding") };

        var stage = DeliveryRunCosts.Of(fanOut, requests, Prices)[1];

        // The stage still has the request: it is its worker's, whichever call made it.
        Assert.Equal(new CostFigure(900, CostSource.Reported), stage.Stage);

        // Neither call can be shown to have made it, so each is its share of the estimate.
        Assert.All(stage.Workers, worker => Assert.Equal(CostSource.Estimated, worker!.Source));
        Assert.Equal(stage.Workers[0], stage.Workers[1]);
    }

    [Fact]
    public void A_stage_re_entered_after_requested_changes_is_estimated_rather_than_reported_in_part()
    {
        var run = Run(Usage());
        var reentered = run with { Stages = [run.Stages[0] with { DoneCount = 2 }, run.Stages[1]] };

        // The writer restamped the start, so this window is the second pass only.
        var costs = DeliveryRunCosts.Of(reentered, [Request("second-pass", Nine.AddMinutes(1), 5)], Prices, ownerModel: "claude-opus-5-5");

        Assert.Equal(CostSource.Estimated, costs[0].Stage!.Source);
    }

    [Fact]
    public void A_session_with_no_telemetry_prices_its_own_tokens_and_its_runs_sub_agents()
    {
        IReadOnlyList<AgentModelUsage> own = [new AgentModelUsage("claude-opus-5-5", 1_000_000, 0, 0, 0)];

        var cost = DeliveryRunCosts.Session(Owner, [], [Run(Usage())], Prices, "claude-opus-5-5", own);

        // The session's own million input tokens at $5, plus Implement's sub-agent share
        // on Sonnet; Scope's tokens were the owner's and are already in its own usage.
        var sonnet = 2_000 * 3 + 2_000 * 15 + 2_000_000 * 0.3m + 200_000 * 3.75m;
        Assert.Equal(new CostFigure(5_000_000 + (long)sonnet, CostSource.Estimated), cost);

        // A model of its own nothing prices leaves the session unpriced.
        Assert.Null(DeliveryRunCosts.Session(Owner, [], [], Prices, null, [new AgentModelUsage("claude-haiku-5-5", 10, 0, 0, 0)]));
    }

    [Fact]
    public void Sub_agents_on_several_models_are_each_priced_at_their_own()
    {
        var run = Run(Usage());
        var implement = run.Stages[1] with
        {
            SubAgentRuns =
            [
                new DeliveryRunSubAgentRun("csharp-coding:coding", "claude-sonnet-5", 1_000, 1_000, 1, false),
                new DeliveryRunSubAgentRun("Explore", "claude-opus-5-5", 1_000, 1_000, 1, false)
            ]
        };

        var stage = DeliveryRunCosts.Of(run with { Stages = [run.Stages[0], implement] }, [], Prices)[1];

        Assert.Equal(CostFigure.Sum(stage.Workers), stage.Stage);
        Assert.NotEqual(stage.Workers[0], stage.Workers[1]);
    }

    [Fact]
    public void A_price_is_found_by_id_then_by_name_then_by_family()
    {
        var table = new ModelPriceTable(
        [
            new ModelPrice("opus", 1m, 1m, 1m, 1m),
            new ModelPrice("Opus 5.5", 2m, 2m, 2m, 2m),
            new ModelPrice("claude-opus-4-1", 3m, 3m, 3m, 3m)
        ]);

        Assert.Equal(3m, table.PriceFor("claude-opus-4-1")!.InputPerMTok);
        Assert.Equal(2m, table.PriceFor("claude-opus-5-5[1m]")!.InputPerMTok);
        Assert.Equal(1m, table.PriceFor("claude-opus-6")!.InputPerMTok);
        Assert.Null(table.PriceFor("claude-haiku-5-5"));
        Assert.Null(table.PriceFor(null));
    }

    [Fact]
    public void A_figure_reads_in_dollars_and_never_as_zero()
    {
        Assert.Equal("$1,234.57", new CostFigure(1_234_567_890, CostSource.Reported).Label);
        Assert.Equal("$0.0042", new CostFigure(4_200, CostSource.Reported).Label);
        Assert.Equal("< $0.0001", new CostFigure(0, CostSource.Reported).Label);
        Assert.Equal(CostSource.Estimated, (new CostFigure(1, CostSource.Reported) + new CostFigure(1, CostSource.Estimated)).Source);
    }
}
