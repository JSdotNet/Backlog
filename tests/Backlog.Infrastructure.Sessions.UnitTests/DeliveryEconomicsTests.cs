namespace Backlog.Infrastructure.Sessions.UnitTests;

/// <summary>
/// The Delivery economics page's figures: the finished runs of a flow inside a window,
/// the flows to choose from, the configuration a stage ran under, one row per phase with
/// its median cost and share of the median run, the configurations that ran a phase side
/// by side, and the cost-only lesson drawn once two of them have five priced runs each.
/// </summary>
public sealed class DeliveryEconomicsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Month = TimeSpan.FromDays(30);

    private static DeliveryRun Run(
        string id,
        DateTimeOffset started,
        string status = "done",
        string skill = "flow-code",
        params DeliveryRunStage[] stages) =>
        new(id, "backlog", "wt-" + id, "wt", "machine", "Machine", skill, "A run", status, null, [],
            started, started.AddMinutes(30),
            stages.Length > 0 ? stages : [Stage("Implement"), Stage("Review")],
            null, null, [], []);

    private static DeliveryRunStage Stage(string name, string? execution = null, string status = "done") =>
        new(name, status, 60_000, 1) { Execution = execution };

    // Seam 1 — the runs of a flow inside the window.

    [Fact]
    public void The_window_holds_the_finished_runs_of_the_flow_placed_by_their_start()
    {
        var inside = Run("inside", Now.AddDays(-29));
        var edge = Run("edge", Now - Month);
        var before = Run("before", Now - Month - TimeSpan.FromMinutes(1));
        var unfinished = Run("unfinished", Now.AddDays(-1), "in_progress");
        var failed = Run("failed", Now.AddDays(-1), "failed");
        var otherFlow = Run("other", Now.AddDays(-1), skill: "flow-spec");
        var shouted = Run("shouted", Now.AddDays(-2), skill: "FLOW-CODE");

        var runs = DeliveryEconomics.Runs([inside, edge, before, unfinished, failed, otherFlow, shouted], "flow-code", Month, Now);

        Assert.Equal(["shouted", "inside", "edge"], runs.Select(run => run.Id));
    }

    [Fact]
    public void A_run_with_no_start_is_placed_by_its_last_update()
    {
        var unstamped = Run("unstamped", Now.AddDays(-60)) with { StartedAt = null, UpdatedAt = Now.AddDays(-3) };

        Assert.Equal("unstamped", Assert.Single(DeliveryEconomics.Runs([unstamped], "flow-code", Month, Now)).Id);
    }

    // Seam 2 — the flows to choose from.

    [Fact]
    public void The_flows_are_the_skills_finished_inside_the_window_most_runs_first()
    {
        DeliveryRun[] catalog =
        [
            Run("s1", Now.AddDays(-1), skill: "flow-spec"),
            Run("c1", Now.AddDays(-2)),
            Run("c2", Now.AddDays(-3), skill: "Flow-Code"),
            Run("c3", Now.AddDays(-4)),
            Run("s2", Now.AddDays(-5), skill: "flow-spec"),
            Run("old", Now.AddDays(-40), skill: "flow-old"),
            Run("live", Now.AddDays(-1), "in_progress", "flow-live")
        ];

        var flows = DeliveryEconomics.Flows(catalog, Month, Now);

        Assert.Equal([new DeliveryFlow("flow-code", 3), new DeliveryFlow("flow-spec", 2)], flows);
    }

    [Fact]
    public void The_flow_asked_for_is_chosen_else_the_one_with_most_runs()
    {
        IReadOnlyList<DeliveryFlow> flows = [new("flow-code", 3), new("flow-spec", 2)];

        Assert.Equal("flow-spec", DeliveryEconomics.DefaultFlow(flows, "flow-spec"));
        Assert.Equal("flow-old", DeliveryEconomics.DefaultFlow(flows, "flow-old"));
        Assert.Equal("flow-code", DeliveryEconomics.DefaultFlow(flows, null));
        Assert.Null(DeliveryEconomics.DefaultFlow([], null));
    }

    // Seam 3 — the configuration a stage ran under.

    private static DeliveryRunStageExecution Resolved(string execution, string name = "Implement") =>
        Assert.Single(DeliveryRunStageResolution.Of(Run("r", Now, stages: Stage(name, execution))));

    [Fact]
    public void A_configuration_is_the_model_family_the_effort_the_agent_and_the_mode_that_ran()
    {
        var stage = Resolved("""{"mode":"delegate","agent":"csharp-coding:coding","model":"claude-sonnet-5-1","effort":"High"}""");

        var configuration = DeliveryEconomics.ConfigurationOf(stage);

        Assert.Equal(new DeliveryConfiguration("sonnet", "high", "csharp-coding:coding", DeliveryRunStageMode.Delegate), configuration);
        Assert.Equal("Sonnet", configuration.ModelLabel);
        Assert.Equal("Sonnet at high effort via csharp-coding:coding, delegated", configuration.Label);
    }

    [Fact]
    public void An_effort_nobody_recorded_stays_unknown()
    {
        var configuration = DeliveryEconomics.ConfigurationOf(Resolved("""{"mode":"fork","model":"claude-opus-5-5"}"""));

        Assert.Null(configuration.Effort);
        Assert.Equal("Opus, forked", configuration.Label);
    }

    [Fact]
    public void A_stage_that_ran_in_the_owner_session_ran_on_the_owner_session_model()
    {
        var inline = Resolved("""{"mode":"inline"}""", "Scope");

        Assert.Equal("opus", DeliveryEconomics.ConfigurationOf(inline, "claude-opus-5-5").Family);
        Assert.Null(DeliveryEconomics.ConfigurationOf(inline).Family);
        Assert.Equal("Session model, inline", DeliveryEconomics.ConfigurationOf(inline).Label);
    }

    // Seam 4 — one row per phase.

    private const string Sonnet = """{"mode":"delegate","agent":"csharp-coding:coding","model":"claude-sonnet-5-1","effort":"high"}""";

    private const string Opus = """{"mode":"delegate","agent":"csharp-coding:coding","model":"claude-opus-5-5"}""";

    private static CostFigure Usd(decimal dollars, CostSource source = CostSource.Reported) => new((long)(dollars * 1_000_000m), source);

    private static IReadOnlyList<DeliveryRunStageCost> Costs(params decimal?[] dollars) =>
        [.. dollars.Select(value => new DeliveryRunStageCost(value is { } usd ? Usd(usd) : null, []))];

    private static PricedDeliveryRun Priced(string id, int daysAgo, string implement, params decimal?[] dollars) =>
        new(Run(id, Now.AddDays(-daysAgo), stages: [Stage("Implement", implement), Stage("Review")]), Costs(dollars));

    [Fact]
    public void Each_phase_shows_its_priced_runs_its_median_cost_and_its_share_of_the_flow()
    {
        var report = DeliveryEconomics.Of(
        [
            Priced("r1", 1, Sonnet, 4m, 1m),
            Priced("r2", 2, Opus, 2m, 1m),
            Priced("r3", 3, Sonnet, 3m, null)
        ]);

        Assert.Equal(3, report.Runs);

        // r3 left Review unpriced: its total is its priced stages ($3), and it is
        // counted as a run some of whose stages were not priced.
        Assert.Equal(3_000_000, report.MedianRun!.UsdMicros);
        Assert.Equal(3, report.TotalRuns);
        Assert.Equal(1, report.PartialRuns);

        // The share is the phase's median over the sum of the phase medians: $3 of $4.
        var implement = report.Phases[0];
        Assert.Equal("Implement", implement.Name);
        Assert.Equal(3, implement.Runs);
        Assert.Equal(3, implement.PricedRuns);
        Assert.Equal(3_000_000, implement.Median!.UsdMicros);
        Assert.Equal(0.75, implement.Share!.Value, 3);
        Assert.Equal(2, implement.Configurations);
        Assert.Equal(new DeliveryConfiguration("sonnet", "high", "csharp-coding:coding", DeliveryRunStageMode.Delegate), implement.Now);

        var review = report.Phases[1];
        Assert.Equal(3, review.Runs);
        Assert.Equal(2, review.PricedRuns);
        Assert.Equal(1_000_000, review.Median!.UsdMicros);
        Assert.Equal(0.25, review.Share!.Value, 3);
    }

    [Fact]
    public void A_phase_no_run_priced_has_no_cost_rather_than_zero()
    {
        var report = DeliveryEconomics.Of([Priced("r1", 1, Sonnet, 4m, null), Priced("r2", 2, Sonnet, 2m, null)]);

        var review = report.Phases[1];

        Assert.Equal(0, review.PricedRuns);
        Assert.Null(review.Median);
        Assert.Null(review.Share);
        Assert.Equal(1.0, report.Phases[0].Share!.Value, 3);
    }

    [Fact]
    public void A_run_nothing_priced_has_no_whole_run_cost()
    {
        var report = DeliveryEconomics.Of([Priced("r1", 1, Sonnet, null, null)]);

        Assert.Null(report.MedianRun);
        Assert.Equal(0, report.TotalRuns);
        Assert.Null(report.Phases[0].Share);
    }

    [Fact]
    public void No_share_exceeds_the_whole_and_the_shares_add_up_to_it()
    {
        // The real-data shape: Implement priced where Review was not and the other way
        // round. Over the runs priced in every stage, Implement once read 275% of the run.
        var report = DeliveryEconomics.Of(
        [
            Priced("a", 1, Sonnet, 10m, null),
            Priced("b", 2, Sonnet, 1m, 1m),
            Priced("c", 3, Sonnet, null, 1m)
        ]);

        var shares = report.Phases.Select(phase => phase.Share).OfType<double>().ToList();

        Assert.Equal(2, shares.Count);
        Assert.All(shares, share => Assert.InRange(share, 0, 1));
        Assert.Equal(1.0, shares.Sum(), 6);
        Assert.Equal(5.5 / 6.5, report.Phases[0].Share!.Value, 6);
    }

    [Fact]
    public void The_phases_follow_the_run_order_with_a_phase_only_older_runs_ran_in_its_place()
    {
        var latest = new PricedDeliveryRun(Run("new", Now.AddDays(-1), stages: [Stage("Scope"), Stage("Implement"), Stage("Review")]), Costs(1m, 1m, 1m));
        var older = new PricedDeliveryRun(Run("old", Now.AddDays(-2), stages: [Stage("Scope"), Stage("Plan"), Stage("Implement"), Stage("Review")]), Costs(1m, 1m, 1m, 1m));

        var report = DeliveryEconomics.Of([older, latest]);

        Assert.Equal(["Scope", "Plan", "Implement", "Review"], report.Phases.Select(phase => phase.Name));
    }

    [Fact]
    public void The_configuration_now_is_what_the_latest_run_configured_and_else_what_it_ran()
    {
        var configured = Run("r1", Now.AddDays(-1), stages: [Stage("Implement", Sonnet)]) with
        {
            RunContext = """{"phases":{"flow-code":{"phase-implement":{"mode":"delegate","agent":"csharp-coding:coding","model":"haiku"}}}}"""
        };

        var phase = Assert.Single(DeliveryEconomics.Of([new PricedDeliveryRun(configured, Costs(1m))]).Phases);

        Assert.Equal(new DeliveryConfiguration("haiku", "high", "csharp-coding:coding", DeliveryRunStageMode.Delegate), phase.Now);
    }

    [Fact]
    public void The_agent_and_the_mode_join_a_configuration_only_where_they_differ_within_the_phase()
    {
        const string Other = """{"mode":"delegate","agent":"general-purpose","model":"claude-sonnet-5-1","effort":"high"}""";

        var shared = DeliveryEconomics.Of([Priced("r1", 1, Sonnet, 1m, 1m), Priced("r2", 2, Opus, 1m, 1m)]).Phases[0];
        var split = DeliveryEconomics.Of([Priced("r1", 1, Sonnet, 1m, 1m), Priced("r2", 2, Other, 1m, 1m)]).Phases[0];

        Assert.Equal(
            [new DeliveryConfiguration("sonnet", "high", null, null), new DeliveryConfiguration("opus", null, null, null)],
            shared.Samples.Select(sample => sample.Configuration));
        Assert.Equal(
            [new DeliveryConfiguration("sonnet", "high", "csharp-coding:coding", null), new DeliveryConfiguration("sonnet", "high", "general-purpose", null)],
            split.Samples.Select(sample => sample.Configuration));
    }

    [Fact]
    public void The_run_figures_say_where_the_costs_came_from_and_the_ninetieth_percentile()
    {
        var runs = Enumerable.Range(1, 10)
            .Select(index => new PricedDeliveryRun(
                Run($"r{index}", Now.AddDays(-index), stages: [Stage("Implement")]),
                [new DeliveryRunStageCost(Usd(index, index <= 3 ? CostSource.Estimated : CostSource.Reported), [])]))
            .Append(new PricedDeliveryRun(Run("unpriced", Now.AddDays(-11), stages: [Stage("Implement")]), Costs([null])))
            .ToList();

        var report = DeliveryEconomics.Of(runs);

        Assert.Equal(11, report.Runs);
        Assert.Equal(7, report.ReportedRuns);
        Assert.Equal(3, report.EstimatedRuns);
        Assert.Equal(5_500_000, report.MedianRun!.UsdMicros);
        Assert.True(report.MedianRun.Estimated);
        Assert.Equal(9_000_000, report.P90Run!.UsdMicros);
        Assert.Equal(0, report.QualityRuns);
    }

    // Seam 5 — the configurations that ran a phase, side by side.

    [Fact]
    public void Each_configuration_shows_its_runs_its_median_phase_cost_and_its_median_whole_run_cost()
    {
        var phase = DeliveryEconomics.Of(
        [
            Priced("s1", 1, Sonnet, 1m, 1m),
            Priced("o1", 2, Opus, 4m, 1m),
            Priced("s2", 3, Sonnet, 2m, 1m),
            Priced("o2", 4, Opus, 6m, 1m),
            Priced("s3", 5, Sonnet, 3m, null)
        ]).Phases[0];

        var comparison = DeliveryEconomics.Compare(phase);

        Assert.Equal(2, comparison.Count);

        var sonnet = comparison[0];
        Assert.Equal(new DeliveryConfiguration("sonnet", "high", null, null), sonnet.Configuration);
        Assert.Equal(3, sonnet.Runs);
        Assert.Equal(3, sonnet.PricedRuns);
        Assert.Equal(2_000_000, sonnet.MedianPhase!.UsdMicros);

        // s3 left Review unpriced: it joins the whole-run median at its priced stages
        // ($3), beside s1 ($2) and s2 ($3), and is counted as partly priced.
        Assert.Equal(3_000_000, sonnet.MedianRun!.UsdMicros);
        Assert.Equal(3, sonnet.TotalRuns);
        Assert.Equal(1, sonnet.PartialRuns);

        var opus = comparison[1];
        Assert.Equal(2, opus.PricedRuns);
        Assert.Equal(5_000_000, opus.MedianPhase!.UsdMicros);
        Assert.Equal(6_000_000, opus.MedianRun!.UsdMicros);
        Assert.Equal(0, opus.PartialRuns);
    }

    [Fact]
    public void A_configuration_that_ran_the_phase_but_priced_none_of_it_is_shown_last_with_its_runs_and_no_cost()
    {
        var phase = DeliveryEconomics.Of(
        [
            Priced("o1", 1, Opus, null, 1m),
            Priced("o2", 2, Opus, null, 1m),
            Priced("s1", 3, Sonnet, 2m, 1m)
        ]).Phases[0];

        var comparison = DeliveryEconomics.Compare(phase);

        Assert.Equal(["sonnet", "opus"], comparison.Select(cost => cost.Configuration.Family));

        var opus = comparison[1];
        Assert.Equal(2, opus.Runs);
        Assert.Equal(0, opus.PricedRuns);
        Assert.Null(opus.MedianPhase);
    }

    [Fact]
    public void A_configuration_reports_no_quality_while_no_run_reported_one()
    {
        var phase = DeliveryEconomics.Of([Priced("s1", 1, Sonnet, 1m, 1m)]).Phases[0];

        var only = Assert.Single(DeliveryEconomics.Compare(phase));

        Assert.Null(only.Quality.Value);
        Assert.Equal(0, only.QualityRuns);
        Assert.Equal("not reported", only.Quality.Label);
    }

    [Fact]
    public void A_reported_quality_is_the_mean_over_the_runs_that_reported_it()
    {
        var phase = DeliveryEconomics.Of([Priced("s1", 1, Sonnet, 1m, 1m), Priced("s2", 2, Sonnet, 1m, 1m), Priced("s3", 3, Sonnet, 1m, 1m)]).Phases[0];
        var reported = phase with
        {
            Samples = [phase.Samples[0] with { Quality = 0.5 }, phase.Samples[1] with { Quality = 0.9 }, phase.Samples[2]]
        };

        var only = Assert.Single(DeliveryEconomics.Compare(reported));

        Assert.Equal(0.7, only.Quality.Value!.Value, 3);
        Assert.Equal(2, only.QualityRuns);
    }

    // Seam 6 — the lesson, cost only while no run reports quality.

    /// <summary>Runs of Implement then Review under one configuration, the latest first
    /// from <paramref name="from"/> days ago.</summary>
    private static IEnumerable<PricedDeliveryRun> Series(string name, int count, int from, string implement, decimal implementUsd, decimal reviewUsd) =>
        Enumerable.Range(0, count).Select(index => Priced($"{name}{index}", from + index, implement, implementUsd, reviewUsd));

    [Fact]
    public void No_lesson_is_drawn_until_two_configurations_each_priced_five_runs_of_the_phase()
    {
        var phase = DeliveryEconomics.Of([.. Series("s", 5, 1, Sonnet, 1m, 1m), .. Series("o", 4, 10, Opus, 3m, 1m)]).Phases[0];

        Assert.Null(DeliveryEconomics.Lesson("flow-code", phase));
    }

    [Fact]
    public void A_lesson_names_the_cheaper_configuration_the_saving_the_whole_run_difference_and_both_sample_sizes()
    {
        var phase = DeliveryEconomics.Of([.. Series("o", 5, 1, Opus, 3m, 1m), .. Series("s", 6, 10, Sonnet, 1m, 1m)]).Phases[0];

        var lesson = DeliveryEconomics.Lesson("flow-code", phase);

        Assert.NotNull(lesson);
        Assert.Equal("Sonnet at high effort runs Implement for $2.00 less than Opus (medians), and whole runs cost $2.00 less", lesson.Headline);
        Assert.Equal("Sonnet at high effort: 6 runs · Opus: 5 runs", lesson.Samples);
        Assert.Equal(new DeliveryConfiguration("sonnet", "high", null, null), lesson.Best);
        Assert.DoesNotContain("quality", lesson.Headline, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Quality not reported: this compares cost only.", lesson.QualityNote);
        Assert.Equal(
            "Use devbook-config:local to set the Implement phase of flow-code to Sonnet at high effort in my personal overlay. "
            + "The Delivery economics page found it $2.00 cheaper per run of that phase than Opus, over 6 and 5 priced runs, and the whole run $2.00 cheaper. Quality was not compared.",
            lesson.Request);
    }

    [Fact]
    public void A_cheaper_phase_that_makes_the_run_dearer_says_so_and_marks_nothing_best()
    {
        // Sonnet saves $2 in Implement, and its runs' Review costs $5 more.
        var report = DeliveryEconomics.Of([.. Series("o", 5, 1, Opus, 3m, 1m), .. Series("s", 5, 10, Sonnet, 1m, 6m)]);

        var lesson = DeliveryEconomics.Lesson("flow-code", report.Phases[0]);

        Assert.NotNull(lesson);
        Assert.Equal("Sonnet at high effort runs Implement for $2.00 less than Opus (medians), but whole runs cost $3.00 more", lesson.Headline);
        Assert.Null(lesson.Best);
        Assert.Contains("and the whole run $3.00 dearer", lesson.Request, StringComparison.Ordinal);
    }

    private const string Haiku = """{"mode":"delegate","agent":"csharp-coding:coding","model":"claude-haiku-5"}""";

    [Fact]
    public void A_lesson_compares_the_cheapest_with_the_configuration_now_rather_than_the_most_run()
    {
        // Opus is the configuration now, Sonnet ran most often, Haiku is the cheapest.
        var phase = DeliveryEconomics.Of(
        [
            .. Series("o", 5, 1, Opus, 5m, 1m),
            .. Series("s", 7, 10, Sonnet, 3m, 1m),
            .. Series("h", 5, 20, Haiku, 1m, 1m)
        ]).Phases[0];

        var lesson = DeliveryEconomics.Lesson("flow-code", phase);

        Assert.NotNull(lesson);
        Assert.Equal("haiku", lesson.Cheaper.Configuration.Family);
        Assert.Equal("opus", lesson.Dearer.Configuration.Family);
        Assert.False(lesson.KeepsNow);
        Assert.Equal("Haiku runs Implement for $4.00 less than Opus (medians), and whole runs cost $4.00 less", lesson.Headline);
        Assert.StartsWith("Use devbook-config:local to set the Implement phase of flow-code to Haiku", lesson.Request, StringComparison.Ordinal);
    }

    [Fact]
    public void Where_the_configuration_now_is_the_cheaper_the_lesson_says_keep_it_and_copies_no_request()
    {
        // Sonnet is the configuration now and the cheaper: there is nothing to change.
        var phase = DeliveryEconomics.Of([.. Series("s", 5, 1, Sonnet, 1m, 1m), .. Series("o", 5, 10, Opus, 3m, 1m)]).Phases[0];

        var lesson = DeliveryEconomics.Lesson("flow-code", phase);

        Assert.NotNull(lesson);
        Assert.True(lesson.KeepsNow);
        Assert.Null(lesson.Request);
        Assert.Equal("Keep Sonnet at high effort for Implement: it runs it for $2.00 less than Opus (medians), and whole runs cost $2.00 less", lesson.Headline);
        Assert.Equal("Sonnet at high effort: 5 runs · Opus: 5 runs", lesson.Samples);
    }

    // Ready fix round 1 — a lesson compares whole runs over complete totals only.

    [Fact]
    public void A_lesson_over_runs_with_a_stage_unpriced_says_the_whole_run_difference_is_unknown()
    {
        // The reviewer's case: Sonnet's runs priced Implement at $1 and left Review
        // unpriced, so their $1 totals stand on part of the run.
        var phase = DeliveryEconomics.Of(
        [
            .. Enumerable.Range(0, 5).Select(index => Priced($"o{index}", 1 + index, Opus, 3m, 1m)),
            .. Enumerable.Range(0, 5).Select(index => Priced($"s{index}", 10 + index, Sonnet, 1m, null))
        ]).Phases[0];

        var lesson = DeliveryEconomics.Lesson("flow-code", phase);

        Assert.NotNull(lesson);
        Assert.Null(lesson.RunDifferenceUsdMicros);
        Assert.Null(lesson.Best);
        Assert.Equal("Sonnet at high effort runs Implement for $2.00 less than Opus (medians); the whole-run difference is unknown", lesson.Headline);
        Assert.Contains("the whole-run difference is unknown", lesson.Request, StringComparison.Ordinal);
        Assert.DoesNotContain("whole run $", lesson.Request, StringComparison.Ordinal);

        // The card still shows its whole-run median over part of the runs, and says so.
        var sonnet = DeliveryEconomics.Compare(phase).Single(cost => cost.Configuration.Family == "sonnet");
        Assert.Equal(1_000_000, sonnet.MedianRun!.UsdMicros);
        Assert.Equal(5, sonnet.PartialRuns);
    }

    [Fact]
    public void Where_both_sides_have_partly_priced_runs_the_lesson_compares_their_complete_runs_only()
    {
        // Each side: five complete runs and six with Review unpriced. Over every total the
        // medians are $1 and $3; over the complete runs, $2 and $5.
        var phase = DeliveryEconomics.Of(
        [
            .. Enumerable.Range(0, 5).Select(index => Priced($"oc{index}", 1 + index, Opus, 3m, 2m)),
            .. Enumerable.Range(0, 6).Select(index => Priced($"op{index}", 10 + index, Opus, 3m, null)),
            .. Enumerable.Range(0, 5).Select(index => Priced($"sc{index}", 20 + index, Sonnet, 1m, 1m)),
            .. Enumerable.Range(0, 6).Select(index => Priced($"sp{index}", 30 + index, Sonnet, 1m, null))
        ]).Phases[0];

        var lesson = DeliveryEconomics.Lesson("flow-code", phase);

        Assert.NotNull(lesson);
        Assert.Equal(-3_000_000, lesson.RunDifferenceUsdMicros);
        Assert.Equal(new DeliveryConfiguration("sonnet", "high", null, null), lesson.Best);
        Assert.EndsWith("and whole runs cost $3.00 less", lesson.Headline, StringComparison.Ordinal);
    }

    // Ready fix round 1 — the configuration now is matched on the fields a card keeps.

    private const string Architect = """{"mode":"fork","agent":"architecture:architect","model":"claude-sonnet-5-1","effort":"high"}""";

    private const string OwnerFork = """{"mode":"fork","model":"claude-sonnet-5-1","effort":"high"}""";

    [Fact]
    public void A_card_that_ran_in_the_owner_session_is_not_the_configuration_now_when_the_phase_keeps_agents()
    {
        // Every run forked, so the mode is dropped; the agents differ, so they are kept.
        var phase = DeliveryEconomics.Of(
        [
            .. Enumerable.Range(0, 5).Select(index => Priced($"a{index}", 1 + index, Architect, 3m, 1m)),
            .. Enumerable.Range(0, 5).Select(index => Priced($"o{index}", 10 + index, OwnerFork, 1m, 1m))
        ]).Phases[0];

        var cards = DeliveryEconomics.Compare(phase);
        var owner = cards.Single(cost => cost.Configuration.Agent is null);
        var architect = cards.Single(cost => cost.Configuration.Agent is not null);

        Assert.False(DeliveryEconomics.Matches(phase, owner.Configuration));
        Assert.True(DeliveryEconomics.Matches(phase, architect.Configuration));

        var lesson = DeliveryEconomics.Lesson("flow-code", phase);
        Assert.NotNull(lesson);
        Assert.False(lesson.KeepsNow);
        Assert.NotNull(lesson.Request);
    }

    [Fact]
    public void A_card_matches_the_configuration_now_on_the_fields_the_phase_dropped()
    {
        var phase = DeliveryEconomics.Of([Priced("s1", 1, Sonnet, 1m, 1m), Priced("o1", 2, Opus, 1m, 1m)]).Phases[0];

        Assert.True(DeliveryEconomics.Matches(phase, new DeliveryConfiguration("sonnet", "high", null, null)));
        Assert.False(DeliveryEconomics.Matches(phase, new DeliveryConfiguration("opus", null, null, null)));
    }

    // Ready fix round 1 — the share is of the phases the flow runs now.

    [Fact]
    public void A_phase_only_older_runs_carried_has_no_share_and_does_not_shrink_the_current_ones()
    {
        var latest = new PricedDeliveryRun(Run("new", Now.AddDays(-1), stages: [Stage("Scope"), Stage("Implement")]), Costs(1m, 1m));
        var older = new PricedDeliveryRun(Run("old", Now.AddDays(-2), stages: [Stage("Scope"), Stage("Plan"), Stage("Implement")]), Costs(1m, 5m, 1m));

        var report = DeliveryEconomics.Of([latest, older]);

        Assert.Equal(["Scope", "Plan", "Implement"], report.Phases.Select(phase => phase.Name));
        Assert.Equal(0.5, report.Phases[0].Share!.Value, 6);
        Assert.Null(report.Phases[1].Share);
        Assert.Equal(0.5, report.Phases[2].Share!.Value, 6);
    }

    [Fact]
    public void A_stage_that_ran_in_the_owner_session_is_grouped_by_the_owner_session_model()
    {
        var run = new PricedDeliveryRun(Run("r1", Now.AddDays(-1), stages: [Stage("Scope", """{"mode":"inline"}""")]), Costs(1m))
        {
            OwnerModel = "claude-opus-5-5"
        };

        var scope = Assert.Single(DeliveryEconomics.Of([run]).Phases);

        Assert.Equal(new DeliveryConfiguration("opus", null, null, null), Assert.Single(scope.Samples).Configuration);
        Assert.Equal("opus", scope.Now!.Family);
    }

    // Seam 7 — the quality measure per phase, from what the delivery phases reported.

    [Theory]
    [InlineData("Scope", DeliveryQualityMeasure.ScopedAgain)]
    [InlineData("Implement", DeliveryQualityMeasure.ReviewRounds)]
    [InlineData("review", DeliveryQualityMeasure.BlockersPerRound)]
    [InlineData("Build & Test", DeliveryQualityMeasure.GreenFirstTime)]
    [InlineData("Build and Test", DeliveryQualityMeasure.GreenFirstTime)]
    [InlineData("Verify", DeliveryQualityMeasure.DefectFound)]
    [InlineData("Spec Check", DeliveryQualityMeasure.None)]
    [InlineData("Personal Validation", DeliveryQualityMeasure.None)]
    public void Each_phase_is_read_on_its_own_quality_measure_and_every_other_phase_on_none(string phase, DeliveryQualityMeasure measure) =>
        Assert.Equal(measure, DeliveryEconomics.MeasureOf(phase));

    private static DeliveryRunStage Reported(string name, int doneCount = 1, int? round = null, int? blockers = null, int? defects = null, string status = "done") =>
        new(name, status, 60_000, doneCount) { Round = round, Blockers = blockers, DefectsFound = defects };

    /// <summary>A run of the given stages, each priced at <paramref name="dollars"/>.</summary>
    private static PricedDeliveryRun Flow(string id, int daysAgo, DeliveryRunStage[] stages, decimal dollars = 1m) =>
        new(Run(id, Now.AddDays(-daysAgo), stages: stages), Costs([.. stages.Select(_ => (decimal?)dollars)]));

    /// <summary>Scope, Implement, Review, Build &amp; Test and Verify, with what each
    /// reported.</summary>
    private static DeliveryRunStage[] FiveStages(
        int scopePasses = 1,
        int? rounds = null,
        int? blockers = null,
        int buildPasses = 1,
        int? defects = null,
        string implementStatus = "done") =>
    [
        Reported("Scope", scopePasses),
        Reported("Implement", implementStatus == "pending" ? 0 : 1, rounds, status: implementStatus),
        Reported("Review", 1, rounds, blockers),
        Reported("Build & Test", buildPasses),
        Reported("Verify", 1, defects: defects)
    ];

    [Fact]
    public void Implement_is_read_on_the_mean_review_rounds_its_runs_reported()
    {
        var report = DeliveryEconomics.Of(
        [
            Flow("a", 1, FiveStages(rounds: 1)),
            Flow("b", 2, FiveStages(rounds: 3)),
            Flow("c", 3, FiveStages())
        ]);

        var quality = report.Phases.Single(phase => phase.Name == "Implement").Quality;

        Assert.Equal(DeliveryQualityMeasure.ReviewRounds, quality.Measure);
        Assert.Equal(2.0, quality.Value!.Value, 6);
        Assert.Equal(2, quality.Runs);
        Assert.Equal("2 rounds", quality.Label);
    }

    [Fact]
    public void A_run_takes_the_highest_round_its_Implement_or_Review_reported()
    {
        var run = Run("r", Now, stages: [Reported("Implement", round: 2), Reported("Review", round: 3), Reported("Verify")]);

        Assert.Equal(3, DeliveryEconomics.RoundsOf(run));
        Assert.Null(DeliveryEconomics.RoundsOf(Run("old", Now, stages: [Stage("Implement"), Stage("Review")])));
    }

    [Fact]
    public void Review_is_read_on_blockers_per_round_and_the_defects_that_escaped_to_Verify()
    {
        var report = DeliveryEconomics.Of(
        [
            Flow("a", 1, FiveStages(rounds: 1, blockers: 0, defects: 2)),
            Flow("b", 2, FiveStages(rounds: 2, blockers: 1, defects: 0)),
            Flow("c", 3, FiveStages())
        ]);

        var quality = report.Phases.Single(phase => phase.Name == "Review").Quality;

        Assert.Equal(0.5, quality.Value!.Value, 6);
        Assert.Equal(2, quality.Runs);
        Assert.Equal(2, quality.Escaped);
        Assert.Equal(2, quality.EscapedRuns);
        Assert.Equal("0.5 blockers per round · 2 escaped to Verify", quality.Label);
    }

    [Fact]
    public void Scope_is_read_on_the_share_scoped_again_once_Implement_had_started()
    {
        var report = DeliveryEconomics.Of(
        [
            Flow("again", 1, FiveStages(scopePasses: 2)),
            Flow("once", 2, FiveStages()),
            Flow("once2", 3, FiveStages()),
            Flow("before-implement", 4, FiveStages(scopePasses: 2, implementStatus: "pending"))
        ]);

        var quality = report.Phases.Single(phase => phase.Name == "Scope").Quality;

        // The run that never started Implement could not go back to Scope from it.
        Assert.Equal(3, quality.Runs);
        Assert.Equal(1.0 / 3, quality.Value!.Value, 6);
        Assert.Equal("33% scoped again", quality.Label);
    }

    [Fact]
    public void Build_and_Test_is_read_on_the_share_green_first_time()
    {
        var report = DeliveryEconomics.Of(
        [
            Flow("green", 1, FiveStages()),
            Flow("green2", 2, FiveStages()),
            Flow("green3", 3, FiveStages()),
            Flow("red", 4, FiveStages(buildPasses: 3))
        ]);

        var quality = report.Phases.Single(phase => phase.Name == "Build & Test").Quality;

        Assert.Equal(0.75, quality.Value!.Value, 6);
        Assert.Equal(4, quality.Runs);
        Assert.Equal("75% green first time", quality.Label);
    }

    [Fact]
    public void Verify_is_read_on_the_share_of_runs_with_a_defect_found_over_those_that_reported_a_count()
    {
        var report = DeliveryEconomics.Of(
        [
            Flow("found", 1, FiveStages(defects: 2)),
            Flow("clean", 2, FiveStages(defects: 0)),
            Flow("silent", 3, FiveStages())
        ]);

        var quality = report.Phases.Single(phase => phase.Name == "Verify").Quality;

        Assert.Equal(0.5, quality.Value!.Value, 6);
        Assert.Equal(2, quality.Runs);
        Assert.Equal("50% found a defect", quality.Label);
    }

    [Fact]
    public void A_phase_without_a_measure_says_so_and_one_nobody_reported_says_not_reported()
    {
        var report = DeliveryEconomics.Of([Flow("a", 1, [Reported("Spec Check"), Reported("Verify")])]);

        Assert.Equal("no measure", report.Phases[0].Quality.Label);
        Assert.False(report.Phases[0].Quality.Reported);
        Assert.Equal("not reported", report.Phases[1].Quality.Label);
    }

    [Fact]
    public void Each_configuration_carries_the_phase_quality_over_its_own_runs_and_the_agent_as_recorded()
    {
        static PricedDeliveryRun Ran(string id, int daysAgo, string implement, int rounds) =>
            new(Run(id, Now.AddDays(-daysAgo), stages: [Stage("Implement", implement) with { Round = rounds }, Stage("Review") with { Round = rounds }]), Costs(1m, 1m));

        var phase = DeliveryEconomics.Of([Ran("s1", 1, Sonnet, 1), Ran("s2", 2, Sonnet, 1), Ran("o1", 3, Opus, 3)]).Phases[0];

        var cards = DeliveryEconomics.Compare(phase);
        var sonnet = cards.Single(card => card.Configuration.Family == "sonnet");
        var opus = cards.Single(card => card.Configuration.Family == "opus");

        Assert.Equal(1.0, sonnet.Quality.Value!.Value, 6);
        Assert.Equal(2, sonnet.QualityRuns);
        Assert.Equal(3.0, opus.Quality.Value!.Value, 6);

        // Every run shared the agent, so it is not part of the key; the card still says it.
        Assert.Null(sonnet.Configuration.Agent);
        Assert.Equal("csharp-coding:coding", sonnet.Agent);
    }

    // Seam 8 — the flow's quality and spend figures at a glance.

    [Fact]
    public void Implement_and_Review_share_is_their_cost_over_all_the_spend()
    {
        var report = DeliveryEconomics.Of(
        [
            new PricedDeliveryRun(Run("a", Now.AddDays(-1), stages: [Stage("Scope"), Stage("Implement"), Stage("Review"), Stage("Verify")]), Costs(1m, 4m, 2m, 1m)),
            new PricedDeliveryRun(Run("b", Now.AddDays(-2), stages: [Stage("Scope"), Stage("Implement"), Stage("Review"), Stage("Verify")]), Costs(1m, 2m, null, 1m))
        ]);

        // (4 + 2 + 2) over (8 + 4).
        Assert.Equal(8.0 / 12, report.ImplementReviewShare!.Value, 6);
        Assert.Null(DeliveryEconomics.Of([Priced("n", 1, Sonnet, null, null)]).ImplementReviewShare);
    }

    [Fact]
    public void Runs_with_two_or_more_review_rounds_are_counted_and_their_cost_set_against_the_rest()
    {
        var report = DeliveryEconomics.Of(
        [
            Flow("one-a", 1, FiveStages(rounds: 1), 1m),
            Flow("one-b", 2, FiveStages(rounds: 1), 1m),
            Flow("two", 3, FiveStages(rounds: 2), 2m),
            Flow("three", 4, FiveStages(rounds: 3), 4m),
            Flow("unreported", 5, FiveStages(), 9m)
        ]);

        Assert.Equal(4, report.RoundRuns);
        Assert.Equal(2, report.MultiRoundRuns);

        // Median $15 (of $10 and $20) over the one-round median of $5.
        Assert.Equal(3.0, report.MultiRoundCostRatio!.Value, 6);
    }

    [Fact]
    public void The_round_cost_ratio_leaves_out_partly_priced_runs_and_needs_both_sides()
    {
        var partial = new PricedDeliveryRun(
            Run("partial", Now.AddDays(-3), stages: [Reported("Implement", round: 1), Reported("Review", round: 1)]),
            Costs(0.1m, null));

        var onlyMulti = DeliveryEconomics.Of([Flow("two", 1, FiveStages(rounds: 2)), partial]);

        Assert.Equal(2, onlyMulti.RoundRuns);
        Assert.Equal(1, onlyMulti.MultiRoundRuns);
        Assert.Null(onlyMulti.MultiRoundCostRatio);
    }

    [Fact]
    public void Defects_that_escaped_review_are_summed_over_the_runs_whose_Verify_reported_a_count()
    {
        var report = DeliveryEconomics.Of(
        [
            Flow("a", 1, FiveStages(defects: 2)),
            Flow("b", 2, FiveStages(defects: 1)),
            Flow("c", 3, FiveStages(defects: 0)),
            Flow("d", 4, FiveStages())
        ]);

        Assert.Equal(3, report.EscapedDefects);
        Assert.Equal(3, report.EscapedRuns);

        var silent = DeliveryEconomics.Of([Flow("d", 1, FiveStages())]);
        Assert.Null(silent.EscapedDefects);
        Assert.Equal(0, silent.EscapedRuns);
        Assert.Equal(0, silent.RoundRuns);
    }
}
