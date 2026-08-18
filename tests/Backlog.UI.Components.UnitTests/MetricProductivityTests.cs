namespace Backlog.UI.Components.UnitTests;

public sealed class MetricScoringTests
{
    private static readonly IReadOnlyList<MetricScoreComponent> Inputs =
    [
        new("Pull requests", 9m, Max: 18m, Weight: 3m),
        new("Issues", 15m, Max: 30m, Weight: 1m)
    ];

    [Fact]
    public void Equal_weights_are_a_plain_average()
    {
        // Half marks on both inputs is half the scale, whatever the units were.
        var score = MetricScoring.Score(
        [
            new("a", 1m, Max: 2m),
            new("b", 50m, Max: 100m)
        ]);

        Assert.Equal(50m, score);
    }

    [Fact]
    public void Weights_are_normalised_rather_than_assumed_to_add_to_one()
    {
        // Both inputs are at half marks, so any weighting still scores 50 — which is
        // what stops a caller having to rebalance four weights to add a fifth input.
        Assert.Equal(50m, MetricScoring.Score(Inputs));
    }

    [Fact]
    public void An_input_in_larger_units_does_not_bury_the_others()
    {
        // Without normalising against Max, 4,000 lines would swamp 2 pull requests
        // and the score would only ever be about lines.
        var score = MetricScoring.Score(
        [
            new("Lines changed", 4_000m, Max: 4_000m, Weight: 1m),
            new("Pull requests", 0m, Max: 2m, Weight: 1m)
        ]);

        Assert.Equal(50m, score);
    }

    [Fact]
    public void An_input_past_full_marks_earns_nothing_extra()
    {
        // One runaway week would otherwise carry a whole quarter.
        var clamped = new MetricScoreComponent("Pull requests", 40m, Max: 18m);

        Assert.Equal(1m, clamped.Normalized);
        Assert.Equal(100m, MetricScoring.Score([clamped]));
    }

    [Fact]
    public void Nothing_to_score_is_zero_rather_than_a_division()
    {
        Assert.Equal(0m, MetricScoring.Score(null));
        Assert.Equal(0m, MetricScoring.Score([]));
        Assert.Equal(0m, MetricScoring.Score([new("a", 1m, Max: 2m, Weight: 0m)]));
        Assert.Equal(0m, MetricScoring.Score([new("a", 1m, Max: 0m)]));
    }

    [Fact]
    public void The_same_inputs_in_a_different_order_score_the_same()
    {
        // A score nobody can reproduce is a number people learn to ignore.
        Assert.Equal(MetricScoring.Score(Inputs), MetricScoring.Score([.. Inputs.Reverse()]));
    }

    [Theory]
    [InlineData(0.0, "Struggling")]
    [InlineData(39.9, "Struggling")]
    [InlineData(40.0, "Finding its feet")]
    [InlineData(77.9, "Steady")]
    [InlineData(100.0, "Strong")]
    public void A_score_lands_in_the_highest_band_whose_floor_it_clears(double score, string expected)
    {
        // Floors only, matched highest first, so a caller writes no ceilings that
        // then have to agree with the next band's floor.
        IReadOnlyList<MetricBand> bands =
        [
            new("Strong", 78m),
            new("Struggling", 0m),
            new("Steady", 60m),
            new("Finding its feet", 40m)
        ];

        Assert.Equal(expected, MetricScoring.BandFor((decimal)score, bands)?.Name);
    }

    [Fact]
    public void A_score_under_every_floor_has_no_band()
    {
        Assert.Null(MetricScoring.BandFor(10m, [new MetricBand("Strong", 78m)]));
        Assert.Null(MetricScoring.BandFor(10m, []));
        Assert.Null(MetricScoring.BandFor(10m, null));
    }

    [Theory]
    // Zero is its own step: "nothing happened" and "a little happened" are
    // different facts, and the palest shade means the second one.
    [InlineData(0.0, 0)]
    [InlineData(0.1, 1)]
    [InlineData(25.0, 1)]
    [InlineData(25.1, 2)]
    [InlineData(75.0, 3)]
    [InlineData(100.0, 4)]
    [InlineData(140.0, 4)]
    public void A_value_earns_a_ramp_step_against_the_max(double value, int expected) =>
        Assert.Equal(expected, MetricScoring.RampStep((decimal)value, 100m));

    [Fact]
    public void No_max_means_no_shade()
    {
        Assert.Equal(0, MetricScoring.RampStep(10m, 0m));
        Assert.Equal(0, MetricScoring.RampStep(10m, 100m, steps: 0));
    }

    [Fact]
    public void The_shared_max_is_the_largest_reading_anywhere_in_the_set()
    {
        // The only y-max that makes a set of small multiples comparable.
        IReadOnlyList<MetricSeries> series =
        [
            new("a", [new MetricPoint("w1", 10m), new MetricPoint("w2", 30m)]),
            new("b", [new MetricPoint("w1", 90m)])
        ];

        Assert.Equal(90m, MetricScoring.SharedMax(series));
        Assert.Equal(0m, MetricScoring.SharedMax([]));
        Assert.Equal(0m, MetricScoring.SharedMax(null));
    }
}

public sealed class MetricSeriesTests
{
    [Fact]
    public void The_latest_reading_is_the_last_one()
    {
        var series = new MetricSeries("backlog", [new MetricPoint("w1", 10m), new MetricPoint("w2", 30m)]);

        Assert.Equal(30m, series.Latest?.Value);
    }

    [Fact]
    public void Change_is_measured_from_the_first_reading_to_the_last()
    {
        var series = new MetricSeries("backlog", [new MetricPoint("w1", 40m), new MetricPoint("w2", 50m)]);

        Assert.Equal(0.25m, series.Change);
    }

    [Fact]
    public void Up_from_nothing_is_not_a_percentage()
    {
        // Null rather than infinity, and null rather than a made-up 100%.
        var fromZero = new MetricSeries("new", [new MetricPoint("w1", 0m), new MetricPoint("w2", 20m)]);

        Assert.Null(fromZero.Change);
    }

    [Fact]
    public void One_reading_has_nothing_to_compare_against()
    {
        Assert.Null(new MetricSeries("a", [new MetricPoint("w1", 10m)]).Change);
        Assert.Null(new MetricSeries("a", []).Latest);
    }
}

public sealed class MetricScoreTests
{
    private static readonly IReadOnlyList<MetricScoreComponent> Inputs =
    [
        new("Pull requests merged", 9m, Max: 18m, Weight: 3m),
        new("Issues closed", 15m, Max: 30m, Weight: 1m)
    ];

    private static readonly IReadOnlyList<MetricBand> Bands =
    [
        new("Struggling", 0m),
        new("Steady", 40m),
        new("Strong", 78m)
    ];

    [Fact]
    public void The_score_is_worked_out_from_the_inputs()
    {
        using var context = new BunitContext();

        var score = context.Render<MetricScore>(parameters => parameters
            .Add(s => s.Components, Inputs));

        Assert.Equal("50", score.Find(".metric-score__value").TextContent);
    }

    [Fact]
    public void Every_input_is_a_row_with_its_share_and_its_points()
    {
        // The whole reason the component exists: a reader who disagrees with the
        // score can see which weight to argue with.
        using var context = new BunitContext();

        var score = context.Render<MetricScore>(parameters => parameters
            .Add(s => s.Components, Inputs));

        var rows = score.FindAll("tbody tr");

        Assert.Equal(2, rows.Count);
        Assert.Contains("Pull requests merged", rows[0].TextContent, StringComparison.Ordinal);
        // Three parts of four weight, and half marks on it: 37.5 of the 100 points.
        Assert.Contains("75%", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("37.5", rows[0].TextContent, StringComparison.Ordinal);
    }

    [Theory]
    // Each of these rounds awkwardly on its own: floor every contribution
    // independently and the column sums to a tenth either side of the score.
    [InlineData(9, 15, 17, 38)]
    [InlineData(14, 21, 17, 38)]
    [InlineData(13, 22, 19, 41)]
    [InlineData(1, 1, 1, 1)]
    [InlineData(18, 30, 22, 52)]
    [InlineData(0, 0, 0, 0)]
    public void The_points_column_always_adds_up_to_the_score(int prs, int issues, int reviews, int builds)
    {
        // The one thing this component must never get wrong. A reader who adds the
        // column up and gets a different number from the one printed above it has
        // been given a reason to distrust every figure on the page.
        IReadOnlyList<MetricScoreComponent> inputs =
        [
            new("Pull requests merged", prs, Max: 18m, Weight: 3m),
            new("Issues closed", issues, Max: 30m, Weight: 2m),
            new("Review turnaround under a day", reviews, Max: 22m, Weight: 2m),
            new("Builds green on first run", builds, Max: 52m, Weight: 1m)
        ];

        using var context = new BunitContext();

        var score = context.Render<MetricScore>(parameters => parameters.Add(s => s.Components, inputs));

        var printed = score.Find(".metric-score__value").TextContent;
        var points = score.FindAll(".metric-score__points-text")
            .Sum(cell => decimal.Parse(cell.TextContent, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(printed, MetricFormat.Score(points));
    }

    [Fact]
    public void Spare_tenths_go_to_whichever_rows_lost_most_to_flooring()
    {
        // Largest remainder, ties to the earlier row, so the same inputs always
        // produce the same table rather than one that shuffles between renders.
        using var context = new BunitContext();

        var first = context.Render<MetricScore>(parameters => parameters.Add(s => s.Components, Inputs));
        var again = context.Render<MetricScore>(parameters => parameters.Add(s => s.Components, Inputs));

        Assert.Equal(
            first.FindAll(".metric-score__points-text").Select(cell => cell.TextContent),
            again.FindAll(".metric-score__points-text").Select(cell => cell.TextContent));
    }

    [Fact]
    public void A_score_with_no_visible_inputs_says_so()
    {
        using var context = new BunitContext();

        var score = context.Render<MetricScore>(parameters => parameters
            .Add(s => s.Value, 68.4m));

        Assert.Empty(score.FindAll("table"));
        Assert.Contains("does not show", score.Find(".metric-score__opaque").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_supplied_value_wins_over_the_computed_one()
    {
        // For a score that arrives already computed, with the components passed
        // alongside so the reader can still see the working.
        using var context = new BunitContext();

        var score = context.Render<MetricScore>(parameters => parameters
            .Add(s => s.Components, Inputs)
            .Add(s => s.Value, 91.2m));

        Assert.Equal("91.2", score.Find(".metric-score__value").TextContent);
        Assert.Equal(2, score.FindAll("tbody tr").Count);
    }

    [Fact]
    public void The_band_is_a_name_and_a_ramp_step_and_never_a_colour_alone()
    {
        using var context = new BunitContext();

        var score = context.Render<MetricScore>(parameters => parameters
            .Add(s => s.Value, 88m)
            .Add(s => s.Bands, Bands));

        var band = score.Find(".metric-score__band");

        Assert.Equal("Strong", band.TextContent);
        Assert.Contains("metric-score__band--4", band.ClassList);
    }

    [Fact]
    public void No_band_claims_the_score_and_none_is_shown()
    {
        using var context = new BunitContext();

        var score = context.Render<MetricScore>(parameters => parameters
            .Add(s => s.Value, 10m)
            .Add(s => s.Bands, [new MetricBand("Strong", 78m)]));

        Assert.Empty(score.FindAll(".metric-score__band"));
    }

    [Fact]
    public void Unavailable_replaces_the_figure_rather_than_sitting_under_it()
    {
        using var context = new BunitContext();

        var score = context.Render<MetricScore>(parameters => parameters
            .Add(s => s.Components, Inputs)
            .Add(s => s.Status, MetricStatusKind.Unavailable)
            .Add(s => s.StatusMessage, "No repository is selected."));

        Assert.Empty(score.FindAll(".metric-score__value"));
        Assert.Empty(score.FindAll("table"));
        Assert.Contains("No repository is selected.", score.Markup, StringComparison.Ordinal);
    }
}

public sealed class MetricTrellisTests
{
    private static IReadOnlyList<MetricSeries> TwoSeries =>
    [
        new("busy", [new MetricPoint("w1", 50m), new MetricPoint("w2", 100m)], "climbing"),
        new("quiet", [new MetricPoint("w1", 5m), new MetricPoint("w2", 10m)])
    ];

    [Fact]
    public void One_panel_per_series()
    {
        using var context = new BunitContext();

        var trellis = context.Render<MetricTrellis>(parameters => parameters
            .Add(t => t.Series, TwoSeries));

        var panels = trellis.FindAll(".metric-trellis__panel");

        Assert.Equal(2, panels.Count);
        Assert.Contains("busy", panels[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("climbing", panels[0].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_panel_is_drawn_against_the_same_scale()
    {
        // The whole mechanism. On its own scale the quiet series would end at the top
        // of its box exactly like the busy one, and the grid would say nothing at all.
        using var context = new BunitContext();

        var trellis = context.Render<MetricTrellis>(parameters => parameters
            .Add(t => t.Series, TwoSeries));

        var lines = trellis.FindAll("polyline");

        // Busy peaks at the shared max, so it reaches y=0. Quiet peaks at a tenth of
        // it, so it stops a tenth of the way up: y=27 of 30.
        Assert.Equal("0,15 100,0", lines[0].GetAttribute("points"));
        Assert.Equal("0,28.5 100,27", lines[1].GetAttribute("points"));
    }

    [Fact]
    public void The_shared_scale_is_stated_once_for_the_whole_grid()
    {
        using var context = new BunitContext();

        var trellis = context.Render<MetricTrellis>(parameters => parameters
            .Add(t => t.Series, TwoSeries));

        Assert.Contains("Same scale in every panel — 0 to 100", trellis.Find(".metric-trellis__scale").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_pinned_max_holds_the_scale_steady_while_the_window_changes()
    {
        // A score is out of 100 whatever window it is read over, and panels that
        // silently rescale under the reader are worse than no panels.
        using var context = new BunitContext();

        var trellis = context.Render<MetricTrellis>(parameters => parameters
            .Add(t => t.Series, [new MetricSeries("a", [new MetricPoint("w1", 25m), new MetricPoint("w2", 50m)])])
            .Add(t => t.SharedMax, 100m));

        Assert.Equal("0,22.5 100,15", trellis.Find("polyline").GetAttribute("points"));
    }

    [Fact]
    public void Each_panel_prints_its_own_latest_figure_and_change()
    {
        // The picture is never the only copy of a number.
        using var context = new BunitContext();

        var trellis = context.Render<MetricTrellis>(parameters => parameters
            .Add(t => t.Series, TwoSeries));

        var value = trellis.FindAll(".metric-trellis__value")[0].TextContent;

        Assert.Contains("100", value, StringComparison.Ordinal);
        Assert.Contains("up 100%", value, StringComparison.Ordinal);
    }

    [Fact]
    public void A_series_with_no_readings_gets_no_panel()
    {
        // An empty box under a name says "zero" where the data says "nothing was
        // reported".
        using var context = new BunitContext();

        var trellis = context.Render<MetricTrellis>(parameters => parameters
            .Add(t => t.Series, [new MetricSeries("silent", []), .. TwoSeries]));

        Assert.Equal(2, trellis.FindAll(".metric-trellis__panel").Count);
    }

    [Fact]
    public void Nothing_at_all_is_an_empty_period()
    {
        using var context = new BunitContext();

        var trellis = context.Render<MetricTrellis>(parameters => parameters
            .Add(t => t.EmptyMessage, "No repository reported in this window."));

        Assert.Empty(trellis.FindAll(".metric-trellis__panels"));
        Assert.Contains("No repository reported in this window.", trellis.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Highlight_marks_one_panel_and_leaves_the_others_alone()
    {
        // Emphasis, not selection: the point of a trellis is that every panel stays
        // readable at once.
        using var context = new BunitContext();

        var trellis = context.Render<MetricTrellis>(parameters => parameters
            .Add(t => t.Series, TwoSeries)
            .Add(t => t.Highlight, "quiet"));

        var highlighted = trellis.FindAll(".metric-trellis__panel--highlight");

        Assert.Single(highlighted);
        Assert.Contains("quiet", highlighted[0].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_panels_picture_is_described_for_a_screen_reader()
    {
        using var context = new BunitContext();

        var trellis = context.Render<MetricTrellis>(parameters => parameters
            .Add(t => t.Series, TwoSeries));

        Assert.Contains("busy: 2 readings from w1 to w2, between 50 and 100.",
            trellis.FindAll(".sr-only")[0].TextContent, StringComparison.Ordinal);
    }
}

public sealed class MetricHeatmapTests
{
    private static IReadOnlyList<MetricSeries> Grid =>
    [
        new("full", [new MetricPoint("w1", 100m), new MetricPoint("w2", 50m), new MetricPoint("w3", 0m)]),
        new("late", [new MetricPoint("w3", 25m)])
    ];

    [Fact]
    public void Rows_are_series_and_columns_are_buckets()
    {
        using var context = new BunitContext();

        var heatmap = context.Render<MetricHeatmap>(parameters => parameters
            .Add(h => h.Series, Grid));

        Assert.Equal(3, heatmap.FindAll("thead th").Count);
        Assert.Equal(2, heatmap.FindAll("tbody tr").Count);
        Assert.Equal(6, heatmap.FindAll(".metric-heatmap__cell").Count);
    }

    [Fact]
    public void The_columns_come_from_the_longest_series_not_the_first()
    {
        // One short row must not truncate the whole grid.
        using var context = new BunitContext();

        var heatmap = context.Render<MetricHeatmap>(parameters => parameters
            .Add(h => h.Series, [new MetricSeries("short", [new MetricPoint("w3", 5m)]), .. Grid]));

        Assert.Equal(["w1", "w2", "w3"], heatmap.FindAll("thead th").Select(cell => cell.TextContent));
    }

    [Fact]
    public void Zero_takes_the_track_and_a_missing_week_takes_an_outline()
    {
        // Three different facts: nothing happened, a little happened, and there is no
        // reading here. One shade for two of them would let the grid say neither.
        using var context = new BunitContext();

        var heatmap = context.Render<MetricHeatmap>(parameters => parameters
            .Add(h => h.Series, Grid)
            .Add(h => h.SharedMax, 100m));

        var firstRow = heatmap.FindAll("tbody tr")[0].QuerySelectorAll(".metric-heatmap__cell");
        var secondRow = heatmap.FindAll("tbody tr")[1].QuerySelectorAll(".metric-heatmap__cell");

        Assert.Contains("metric-heatmap__cell--4", firstRow[0].ClassList);
        Assert.Contains("metric-heatmap__cell--2", firstRow[1].ClassList);
        Assert.Contains("metric-heatmap__cell--0", firstRow[2].ClassList);
        Assert.Contains("metric-heatmap__cell--missing", secondRow[0].ClassList);
    }

    [Fact]
    public void A_shade_is_a_bucket_so_every_cell_also_carries_its_reading()
    {
        // Four steps against hundreds of values: the colour is the bucket and the
        // text is the number.
        using var context = new BunitContext();

        var heatmap = context.Render<MetricHeatmap>(parameters => parameters
            .Add(h => h.Series, Grid)
            .Add(h => h.SharedMax, 100m));

        var cell = heatmap.FindAll("tbody tr")[0].QuerySelectorAll(".metric-heatmap__cell")[1];

        Assert.Equal("50", cell.QuerySelector(".sr-only")!.TextContent);
        Assert.Equal("full, w2: 50", cell.GetAttribute("title"));
    }

    [Fact]
    public void A_week_nobody_reported_reads_as_not_reported_rather_than_zero()
    {
        using var context = new BunitContext();

        var heatmap = context.Render<MetricHeatmap>(parameters => parameters
            .Add(h => h.Series, Grid));

        var missing = heatmap.FindAll("tbody tr")[1].QuerySelectorAll(".metric-heatmap__cell")[0];

        Assert.Equal("not reported", missing.QuerySelector(".sr-only")!.TextContent);
    }

    [Fact]
    public void The_legend_names_the_range_each_shade_covers()
    {
        using var context = new BunitContext();

        var heatmap = context.Render<MetricHeatmap>(parameters => parameters
            .Add(h => h.Series, Grid)
            .Add(h => h.SharedMax, 100m));

        var legend = heatmap.FindAll(".metric-heatmap__legend-item").Select(item => item.TextContent).ToList();

        Assert.Equal(5, legend.Count);
        Assert.Contains("none", legend[0], StringComparison.Ordinal);
        Assert.Contains("75", legend[4], StringComparison.Ordinal);
        Assert.Contains("100", legend[4], StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_to_grid_is_an_empty_period()
    {
        using var context = new BunitContext();

        var heatmap = context.Render<MetricHeatmap>(parameters => parameters
            .Add(h => h.EmptyMessage, "No repository reported in this window."));

        Assert.Empty(heatmap.FindAll("table"));
        Assert.Contains("No repository reported in this window.", heatmap.Markup, StringComparison.Ordinal);
    }
}

public sealed class MetricSpotlightTests
{
    private static IReadOnlyList<MetricSeries> Pack =>
    [
        new("subject", [new MetricPoint("w1", 50m), new MetricPoint("w2", 100m)]),
        new("other", [new MetricPoint("w1", 10m), new MetricPoint("w2", 20m)]),
        new("third", [new MetricPoint("w1", 30m), new MetricPoint("w2", 40m)])
    ];

    [Fact]
    public void Only_the_selected_series_is_the_subject()
    {
        // How a multi-series line chart is done with one saturated hue: one series
        // takes it, the rest are context.
        using var context = new BunitContext();

        var chart = context.Render<MetricSpotlight>(parameters => parameters
            .Add(s => s.Series, Pack)
            .Add(s => s.Selected, "subject"));

        Assert.Equal(3, chart.FindAll("polyline").Count);
        Assert.Single(chart.FindAll(".metric-spotlight__line--subject"));
    }

    [Fact]
    public void The_subject_is_painted_last_so_it_sits_in_front()
    {
        // SVG paints in document order, so the subject has to come after the lines it
        // is meant to be in front of.
        using var context = new BunitContext();

        var chart = context.Render<MetricSpotlight>(parameters => parameters
            .Add(s => s.Series, Pack)
            .Add(s => s.Selected, "subject"));

        var lines = chart.FindAll("polyline");

        Assert.Contains("metric-spotlight__line--subject", lines[^1].ClassList);
    }

    [Fact]
    public void Selecting_nothing_leaves_every_line_as_context()
    {
        using var context = new BunitContext();

        var chart = context.Render<MetricSpotlight>(parameters => parameters
            .Add(s => s.Series, Pack));

        Assert.Equal(3, chart.FindAll("polyline").Count);
        Assert.Empty(chart.FindAll(".metric-spotlight__line--subject"));
    }

    [Fact]
    public void Every_line_shares_one_scale_because_that_is_what_an_overlay_is_for()
    {
        using var context = new BunitContext();

        var chart = context.Render<MetricSpotlight>(parameters => parameters
            .Add(s => s.Series, [Pack[0], Pack[1]])
            .Add(s => s.Selected, "subject"));

        var lines = chart.FindAll("polyline");

        // Context first: 'other' peaks at a fifth of the shared max of 100.
        Assert.Equal("0,36 100,32", lines[0].GetAttribute("points"));
        Assert.Equal("0,20 100,0", lines[1].GetAttribute("points"));
    }

    [Fact]
    public void A_series_with_one_reading_is_not_a_line()
    {
        using var context = new BunitContext();

        var chart = context.Render<MetricSpotlight>(parameters => parameters
            .Add(s => s.Series, [new MetricSeries("dot", [new MetricPoint("w1", 5m)]), .. Pack]));

        Assert.Equal(3, chart.FindAll("polyline").Count);
    }

    [Fact]
    public void The_legend_prints_each_latest_figure_and_marks_the_subject()
    {
        // There are too many lines here for any of them to be labelled in place.
        using var context = new BunitContext();

        var chart = context.Render<MetricSpotlight>(parameters => parameters
            .Add(s => s.Series, Pack)
            .Add(s => s.Selected, "other"));

        var items = chart.FindAll(".metric-spotlight__legend-item");

        Assert.Equal(3, items.Count);
        Assert.Contains("100", items[0].TextContent, StringComparison.Ordinal);
        Assert.Single(chart.FindAll(".metric-spotlight__legend-item--subject"));
        Assert.Contains("other", chart.Find(".metric-spotlight__legend-item--subject").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Every_reading_is_in_the_hidden_table()
    {
        // The picture is aria-hidden and a line is not a number.
        using var context = new BunitContext();

        var chart = context.Render<MetricSpotlight>(parameters => parameters
            .Add(s => s.Series, Pack)
            .Add(s => s.NameHeading, "Repository"));

        Assert.Equal("true", chart.Find(".metric-spotlight__plot").GetAttribute("aria-hidden"));
        // Hidden by its wrapper. Thirteen columns of hidden table with sr-only on the
        // table itself renders at full width and takes the page with it, because
        // overflow: hidden does not clip table layout.
        Assert.Contains("sr-only", chart.Find("table").ParentElement!.ClassList);
        Assert.DoesNotContain("sr-only", chart.Find("table").ClassList);
        Assert.Equal(3, chart.FindAll("tbody tr").Count);
        Assert.Contains("Repository", chart.Find("thead").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_to_overlay_is_an_empty_period()
    {
        using var context = new BunitContext();

        var chart = context.Render<MetricSpotlight>(parameters => parameters
            .Add(s => s.EmptyMessage, "No repository reported in this window."));

        Assert.Empty(chart.FindAll("svg"));
        Assert.Contains("No repository reported in this window.", chart.Markup, StringComparison.Ordinal);
    }
}

public sealed class MetricSparklineSharedScaleTests
{
    [Fact]
    public void A_supplied_max_replaces_the_series_own_peak()
    {
        // What makes a trellis comparable: this series peaks at 50 but is drawn
        // against 100, so it stops halfway up rather than filling the box.
        using var context = new BunitContext();

        var sparkline = context.Render<MetricSparkline>(parameters => parameters
            .Add(s => s.Points, [new MetricPoint("a", 0m), new MetricPoint("b", 50m)])
            .Add(s => s.Max, 100m));

        Assert.Equal("0,30 100,15", sparkline.Find("polyline").GetAttribute("points"));
    }

    [Fact]
    public void A_max_smaller_than_the_series_does_not_clip_the_line_out_of_the_box()
    {
        // A shared scale must still contain whatever it is given.
        using var context = new BunitContext();

        var sparkline = context.Render<MetricSparkline>(parameters => parameters
            .Add(s => s.Points, [new MetricPoint("a", 0m), new MetricPoint("b", 200m)])
            .Add(s => s.Max, 100m));

        Assert.Equal("0,30 100,0", sparkline.Find("polyline").GetAttribute("points"));
    }

    [Fact]
    public void No_max_still_scales_to_the_series_as_it_always_did()
    {
        using var context = new BunitContext();

        var sparkline = context.Render<MetricSparkline>(parameters => parameters
            .Add(s => s.Points, [new MetricPoint("a", 0m), new MetricPoint("b", 50m)]));

        Assert.Equal("0,30 100,0", sparkline.Find("polyline").GetAttribute("points"));
    }
}
