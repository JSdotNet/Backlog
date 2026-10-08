namespace Backlog.Infrastructure.Sessions.UnitTests;

/// <summary>
/// A run's cost against its usual: the median of the same stage over the last 30
/// finished runs of the same flow, the four bands a ratio is drawn in, nothing for an
/// unfinished stage or under five earlier runs, and the insight line naming the stage
/// that explains the difference.
/// </summary>
public sealed class DeliveryRunBaselinesTests
{
    private static readonly DateTimeOffset Nine = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

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

    private static DeliveryRunStage Stage(string name, string status = "done", int done = 1) => new(name, status, 60_000, done);

    private static CostFigure Usd(decimal dollars) => new((long)(dollars * 1_000_000m), CostSource.Reported);

    private static IReadOnlyList<DeliveryRunStageCost> Costs(params decimal?[] dollars) =>
        [.. dollars.Select(value => new DeliveryRunStageCost(value is { } usd ? Usd(usd) : null, []))];

    /// <summary>Earlier runs of two stages, Implement at the given costs and Review at $1.</summary>
    private static List<PricedDeliveryRun> Earlier(params decimal[] implement) =>
    [
        .. implement.Select((usd, index) => new PricedDeliveryRun(Run($"e{index}", Nine.AddDays(-index - 1)), Costs(usd, 1m)))
    ];

    [Fact]
    public void The_median_is_the_middle_value_and_the_mean_of_the_middle_two()
    {
        Assert.Equal(3m, DeliveryRunBaselines.Median([5, 1, 3]));
        Assert.Equal(2.5m, DeliveryRunBaselines.Median([4, 1, 3, 2]));
        Assert.Equal(7m, DeliveryRunBaselines.Median([7]));
        Assert.Null(DeliveryRunBaselines.Median([]));
    }

    [Fact]
    public void One_runaway_run_does_not_move_the_median()
    {
        Assert.Equal(2m, DeliveryRunBaselines.Median([1, 2, 2, 3, 1_000_000]));
    }

    [Theory]
    [InlineData(0.5, CostBand.Lower)]
    [InlineData(0.8, CostBand.Lower)]
    [InlineData(0.81, CostBand.Plain)]
    [InlineData(0.94, CostBand.Plain)]
    [InlineData(0.95, CostBand.Typical)]
    [InlineData(1.0, CostBand.Typical)]
    [InlineData(1.05, CostBand.Typical)]
    [InlineData(1.06, CostBand.Plain)]
    [InlineData(1.49, CostBand.Plain)]
    [InlineData(1.5, CostBand.Higher)]
    [InlineData(3.0, CostBand.Higher)]
    public void A_ratio_reads_typical_within_five_percent_green_at_or_below_point_eight_and_highlighted_from_one_and_a_half(double ratio, CostBand band)
    {
        Assert.Equal(band, DeliveryRunBaselines.Band(ratio));
    }

    [Fact]
    public void A_typical_ratio_is_labelled_typical_and_any_other_by_its_ratio()
    {
        Assert.Equal("typical", new CostAgainstUsual(1.02, 1_000_000, 5).Label);
        Assert.Equal("2.4×", new CostAgainstUsual(2.4, 1_000_000, 5).Label);
        Assert.Equal("0.7×", new CostAgainstUsual(0.7, 1_000_000, 5).Label);
    }

    [Theory]
    [InlineData(1.46, "1.46×")]
    [InlineData(0.84, "0.84×")]
    [InlineData(1.5, "1.5×")]
    [InlineData(1.3, "1.3×")]
    [InlineData(0.04, "0.04×")]
    public void A_ratio_one_decimal_would_round_across_a_band_edge_keeps_two(double ratio, string label)
    {
        Assert.Equal(label, DeliveryRunBaselines.RatioLabel(ratio));
    }

    [Fact]
    public void A_run_of_the_same_id_in_another_worktree_is_still_an_earlier_run()
    {
        var run = Run("same", Nine);
        var other = Run("same", Nine.AddDays(-1)) with { Worktree = "wt-elsewhere" };

        Assert.Same(other, Assert.Single(DeliveryRunBaselines.Earlier(run, [run, other])));
    }

    [Fact]
    public void Each_stage_shows_its_share_of_the_run_and_its_ratio_to_the_stage_median()
    {
        var run = Run("now", Nine);

        var comparison = DeliveryRunBaselines.Compare(run, Costs(6m, 2m), Earlier(2m, 3m, 4m, 5m, 1m));

        Assert.Equal(8_000_000, comparison.Total!.UsdMicros);

        var implement = comparison.Stages[0]!;
        Assert.Equal(0.75, implement.Share!.Value, 3);
        Assert.Equal(2.0, implement.Usual!.Ratio, 3);
        Assert.Equal(3_000_000m, implement.Usual.MedianUsdMicros);
        Assert.Equal(5, implement.Usual.Runs);
        Assert.Equal(CostBand.Higher, implement.Usual.Band);

        var review = comparison.Stages[1]!;
        Assert.Equal(0.25, review.Share!.Value, 3);
        Assert.Equal(CostBand.Higher, review.Usual!.Band);
    }

    [Fact]
    public void Nothing_is_compared_while_fewer_than_five_earlier_runs_priced_the_stage()
    {
        var comparison = DeliveryRunBaselines.Compare(Run("now", Nine), Costs(6m, 2m), Earlier(2m, 3m, 4m, 5m));

        Assert.NotNull(comparison.Stages[0]);
        Assert.NotNull(comparison.Stages[0]!.Share);
        Assert.Null(comparison.Stages[0]!.Usual);
        Assert.Null(comparison.Insight);
    }

    [Fact]
    public void A_stage_that_has_not_finished_is_compared_with_nothing()
    {
        var run = Run("now", Nine, "in_progress", "flow-code", Stage("Implement", "in_progress"), Stage("Review", "pending"));

        var comparison = DeliveryRunBaselines.Compare(run, Costs(6m, null), Earlier(2m, 3m, 4m, 5m, 1m));

        Assert.Equal(6_000_000, comparison.Stages[0]!.Cost.UsdMicros);
        Assert.Null(comparison.Stages[0]!.Usual);
        Assert.Null(comparison.Stages[1]);
        Assert.Null(comparison.Insight);
    }

    [Fact]
    public void Only_finished_earlier_runs_of_the_same_flow_that_started_before_count_and_only_the_latest_thirty()
    {
        var run = Run("now", Nine);
        var catalog = new List<DeliveryRun>
        {
            run,
            Run("later", Nine.AddHours(1)),
            Run("parked", Nine.AddDays(-1), "parked"),
            Run("spec", Nine.AddDays(-1), skill: "flow-spec")
        };

        catalog.AddRange(Enumerable.Range(1, 35).Select(day => Run($"d{day}", Nine.AddDays(-day))));

        var earlier = DeliveryRunBaselines.Earlier(run, catalog);

        Assert.Equal(30, earlier.Count);
        Assert.Equal("d1", earlier[0].Id);
        Assert.Equal("d30", earlier[^1].Id);
        Assert.DoesNotContain(earlier, other => other.Id is "now" or "later" or "parked" or "spec");
    }

    [Fact]
    public void An_earlier_stage_that_did_not_finish_is_not_a_sample()
    {
        var earlier = Earlier(2m, 3m, 4m, 5m);
        earlier.Add(new PricedDeliveryRun(Run("blocked-stage", Nine.AddDays(-9), "done", "flow-code", Stage("Implement", "blocked"), Stage("Review")), Costs(100m, 1m)));

        var comparison = DeliveryRunBaselines.Compare(Run("now", Nine), Costs(6m, 1m), earlier);

        Assert.Null(comparison.Stages[0]!.Usual);
        Assert.Equal(5, comparison.Stages[1]!.Usual!.Runs);
    }

    [Fact]
    public void The_insight_names_the_runs_ratio_and_the_stage_that_explains_most_of_it_with_its_re_entries()
    {
        var run = Run("now", Nine, "done", "flow-code", Stage("Implement", done: 3), Stage("Review"));

        // Earlier totals: Implement 2..6 plus Review 1 = 3..7, median 5. This run: 12 + 1.
        var comparison = DeliveryRunBaselines.Compare(run, Costs(12m, 1m), Earlier(2m, 3m, 4m, 5m, 6m));

        var insight = comparison.Insight!;
        Assert.Equal(2.6, insight.Usual.Ratio, 3);
        Assert.Equal("Implement", insight.Stage);
        Assert.Equal(2, insight.ReEntries);
        Assert.Equal(8_000_000m, insight.StageDifferenceUsdMicros);
        Assert.Equal("2.6× the usual flow-code run · Implement explains most of it: $8.00 over its usual, entered again twice", insight.Text);
    }

    [Fact]
    public void A_cheaper_run_is_explained_by_the_stage_furthest_below_its_usual()
    {
        var earlier = Enumerable.Range(0, 5)
            .Select(index => new PricedDeliveryRun(Run($"e{index}", Nine.AddDays(-index - 1)), Costs(4m, 4m)))
            .ToList();

        var comparison = DeliveryRunBaselines.Compare(Run("now", Nine), Costs(3.5m, 1m), earlier);

        Assert.Equal("Review", comparison.Insight!.Stage);
        Assert.Equal(CostBand.Lower, comparison.Insight.Usual.Band);
        Assert.StartsWith("0.6× the usual flow-code run · Review explains most of it: $3.00 under its usual", comparison.Insight.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_run_is_compared_like_for_like_with_earlier_runs_that_priced_the_same_stages()
    {
        // Five earlier runs priced both stages at $4 + $1; five older ones priced only
        // Review, as runs from before per-stage tokens do. Summed as found, those would
        // read as $1 runs and sink the median to $3 — this $5 run would read 1.7×.
        var earlier = Earlier(4m, 4m, 4m, 4m, 4m);
        earlier.AddRange(Enumerable.Range(10, 5).Select(day => new PricedDeliveryRun(Run($"old{day}", Nine.AddDays(-day)), Costs(null, 1m))));

        var comparison = DeliveryRunBaselines.Compare(Run("now", Nine), Costs(4m, 1m), earlier);

        Assert.Equal(1.0, comparison.Insight!.Usual.Ratio, 3);
        Assert.Equal(5, comparison.Insight.Usual.Runs);
        Assert.Equal(CostBand.Typical, comparison.Insight.Usual.Band);
    }

    [Fact]
    public void A_typical_run_names_no_stage()
    {
        var comparison = DeliveryRunBaselines.Compare(Run("now", Nine), Costs(4m, 1m), Earlier(4m, 4m, 4m, 4m, 4m));

        Assert.Null(comparison.Insight!.Stage);
        Assert.Equal("About the usual flow-code run (1.0× its median)", comparison.Insight.Text);
    }

    [Fact]
    public void A_run_still_under_way_has_no_insight()
    {
        var run = Run("now", Nine, "in_progress");

        var comparison = DeliveryRunBaselines.Compare(run, Costs(12m, 1m), Earlier(2m, 3m, 4m, 5m, 6m));

        Assert.NotNull(comparison.Stages[0]!.Usual);
        Assert.Null(comparison.Insight);
    }

    [Fact]
    public void A_run_nothing_priced_has_no_total_and_no_insight()
    {
        var comparison = DeliveryRunBaselines.Compare(Run("now", Nine), Costs(null, null), Earlier(2m, 3m, 4m, 5m, 6m));

        Assert.Null(comparison.Total);
        Assert.All(comparison.Stages, Assert.Null);
        Assert.Null(comparison.Insight);
    }
}
