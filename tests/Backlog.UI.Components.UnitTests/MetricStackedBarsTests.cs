using System.Globalization;

namespace Backlog.UI.Components.UnitTests;

public sealed class MetricStackedBarsTests
{
    /// <summary>Two series, three buckets, uneven — a fixture where every bucket split
    /// the same way would pass a chart that ignored the data. The peak bucket differs
    /// between the modes on purpose: stacked, W2 is tallest at 30; grouped, W1's 25 is.</summary>
    private static IReadOnlyList<MetricSeries> Hours =>
    [
        new("Producing", [new MetricPoint("W1", 25m), new MetricPoint("W2", 10m), new MetricPoint("W3", 5m)], "40h over the period"),
        new("Waiting", [new MetricPoint("W1", 1m), new MetricPoint("W2", 20m), new MetricPoint("W3", 15m)])
    ];

    private static IRenderedComponent<MetricStackedBars> Render(
        BunitContext context,
        MetricBarsMode mode = MetricBarsMode.Stacked,
        IReadOnlyList<MetricSeries>? series = null) =>
        context.Render<MetricStackedBars>(parameters => parameters
            .Add(c => c.Series, series ?? Hours)
            .Add(c => c.Mode, mode)
            .Add(c => c.TestId, "chart"));

    private static IReadOnlyList<string> Row(IRenderedComponent<MetricStackedBars> chart, int row) =>
        [.. chart.FindAll("tbody tr")[row].QuerySelectorAll("td").Select(cell => cell.TextContent)];

    [Fact]
    public void A_stack_draws_one_box_per_series_in_every_column_and_prints_the_totals()
    {
        using var context = new BunitContext();

        var chart = Render(context);

        var columns = chart.FindAll(".metric-stacked-bars__column");
        Assert.Equal(3, columns.Count);
        Assert.All(columns, column => Assert.Equal(2, column.QuerySelectorAll(".metric-stacked-bars__segment").Length));

        // The scale tops out at the tallest stack, not the tallest single value.
        Assert.Equal("30", chart.Find(".metric-stacked-bars__scale-max").TextContent);

        // Every figure in the table, and a total row because a stack is a claim that
        // the sum is a figure.
        Assert.Equal(["25", "10", "5"], Row(chart, 0));
        Assert.Equal(["1", "20", "15"], Row(chart, 1));
        Assert.Equal(["26", "30", "20"], [.. chart.FindAll("tfoot td").Select(cell => cell.TextContent)]);
    }

    [Fact]
    public void A_group_draws_the_series_side_by_side_and_prints_no_total()
    {
        using var context = new BunitContext();

        var chart = Render(context, MetricBarsMode.Grouped);

        Assert.NotNull(chart.Find(".metric-stacked-bars__column-track--grouped"));
        Assert.Contains("metric-stacked-bars--grouped", chart.Find("figure").ClassList);

        // The scale is the tallest single bar: two peaks do not add up to anything.
        Assert.Equal("25", chart.Find(".metric-stacked-bars__scale-max").TextContent);
        Assert.Empty(chart.FindAll("tfoot"));
    }

    [Fact]
    public void Heights_are_shares_of_the_scale_so_the_stack_fills_the_height_it_has()
    {
        using var context = new BunitContext();

        var chart = Render(context);

        // W2 is the peak stack at 30: its two boxes are 33.33% and 66.67% of the track.
        var peak = chart.FindAll(".metric-stacked-bars__column")[1]
            .QuerySelectorAll(".metric-stacked-bars__segment")
            .Select(segment => segment.GetAttribute("style"))
            .ToList();

        Assert.Equal(["height: 33.33%", "height: 66.67%"], peak);
    }

    [Fact]
    public void The_legend_is_a_pressed_button_per_series_and_switching_one_off_takes_it_out_of_the_picture_and_the_table()
    {
        using var context = new BunitContext();

        var chart = Render(context);

        var waiting = chart.Find("[data-testid='chart-toggle-waiting']");
        Assert.Equal("true", waiting.GetAttribute("aria-pressed"));

        waiting.Click();

        // The state, not a tint, says it is off; the series is gone from every column,
        // from the table, and from the scale — what is left fills the height.
        Assert.Equal("false", chart.Find("[data-testid='chart-toggle-waiting']").GetAttribute("aria-pressed"));
        Assert.Contains("metric-stacked-bars__legend-item--off", chart.Find("[data-testid='chart-toggle-waiting']").ClassList);
        Assert.All(
            chart.FindAll(".metric-stacked-bars__column"),
            column => Assert.Single(column.QuerySelectorAll(".metric-stacked-bars__segment")));
        Assert.Single(chart.FindAll("tbody tr"));
        Assert.Equal("25", chart.Find(".metric-stacked-bars__scale-max").TextContent);
        Assert.Equal(["25", "10", "5"], [.. chart.FindAll("tfoot td").Select(cell => cell.TextContent)]);

        // And back again.
        chart.Find("[data-testid='chart-toggle-waiting']").Click();

        Assert.Equal(2, chart.FindAll("tbody tr").Count);
        Assert.Equal("30", chart.Find(".metric-stacked-bars__scale-max").TextContent);
    }

    [Fact]
    public void A_series_keeps_its_ramp_step_while_it_is_off()
    {
        // Hiding the first band must not recolour the second: the legend a reader just
        // learnt would be wrong the moment they used it.
        using var context = new BunitContext();

        var chart = Render(context);

        chart.Find("[data-testid='chart-toggle-producing']").Click();

        var remaining = chart.FindAll(".metric-stacked-bars__column")[0].QuerySelector(".metric-stacked-bars__segment")!;

        Assert.Contains("metric-stacked-bars__segment--4", remaining.ClassList);
        Assert.Contains("metric-stacked-bars__swatch--2", chart.Find("[data-testid='chart-toggle-producing'] .metric-stacked-bars__swatch").ClassList);
    }

    /// <summary>Two bands one shade apart read as one band with a seam, so fewer than
    /// four series are spread across the ramp rather than taken from its bottom; four
    /// or more take it in order, as the stacked area does.</summary>
    [Theory]
    [InlineData(1, new[] { 4 })]
    [InlineData(2, new[] { 2, 4 })]
    [InlineData(3, new[] { 2, 3, 4 })]
    [InlineData(5, new[] { 1, 2, 3, 4, 1 })]
    public void Ramp_steps_are_spread_when_there_are_few_series_and_taken_in_order_when_there_are_many(int count, int[] steps)
    {
        using var context = new BunitContext();

        var chart = Render(context, series:
        [
            .. Enumerable.Range(0, count).Select(index =>
                new MetricSeries($"s{index}", [new MetricPoint("W1", index + 1m)]))
        ]);

        var drawn = chart.FindAll(".metric-stacked-bars__column")[0]
            .QuerySelectorAll(".metric-stacked-bars__segment")
            .Select(segment => int.Parse(segment.ClassList.Single(name => name.StartsWith("metric-stacked-bars__segment--", StringComparison.Ordinal))["metric-stacked-bars__segment--".Length..], CultureInfo.InvariantCulture))
            .ToArray();

        Assert.Equal(steps, drawn);
    }

    [Fact]
    public void The_legend_carries_the_detail_the_caller_supplied_and_nothing_it_worked_out_itself()
    {
        // Whether a series' period figure is a sum or a peak is the caller's arithmetic;
        // the legend prints what it was handed and invents nothing for the series that
        // was handed nothing.
        using var context = new BunitContext();

        var chart = Render(context);

        Assert.Equal("40h over the period", chart.Find("[data-testid='chart-toggle-producing'] .metric-stacked-bars__legend-detail").TextContent);
        Assert.Empty(chart.FindAll("[data-testid='chart-toggle-waiting'] .metric-stacked-bars__legend-detail"));
    }

    [Fact]
    public void A_bucket_a_series_never_named_is_not_reported_rather_than_zero()
    {
        using var context = new BunitContext();

        var chart = Render(context, series:
        [
            new MetricSeries("a", [new MetricPoint("W1", 4m), new MetricPoint("W2", 6m)]),
            new MetricSeries("b", [new MetricPoint("W2", 1m)])
        ]);

        Assert.Equal(["4", "6"], Row(chart, 0));
        Assert.Equal(["—", "1"], Row(chart, 1));
    }

    [Fact]
    public void No_series_at_all_is_the_empty_state_not_an_empty_axis()
    {
        using var context = new BunitContext();

        var chart = context.Render<MetricStackedBars>(parameters => parameters
            .Add(c => c.Series, [])
            .Add(c => c.EmptyMessage, "Nothing this period"));

        Assert.Contains("Nothing this period", chart.Markup, StringComparison.Ordinal);
        Assert.Empty(chart.FindAll(".metric-stacked-bars__column"));
    }

    [Fact]
    public void The_scale_and_a_title_read_through_the_formatter()
    {
        using var context = new BunitContext();

        var chart = context.Render<MetricStackedBars>(parameters => parameters
            .Add(c => c.Series, Hours)
            .Add(c => c.FormatValue, value => $"{value}h"));

        Assert.Equal("30h", chart.Find(".metric-stacked-bars__scale-max").TextContent);
        Assert.Equal("W1 · Producing: 25h", chart.Find(".metric-stacked-bars__segment").GetAttribute("title"));
    }

    /// <summary>Two counts to draw over the hours: a different unit, on purpose, so the
    /// right scale has to be its own.</summary>
    private static IReadOnlyList<MetricSeries> Counts =>
    [
        new("Sessions", [new MetricPoint("W1", 4m), new MetricPoint("W2", 8m), new MetricPoint("W3", 2m)], "14 over the period"),
        new("Agents at once", [new MetricPoint("W1", 1m), new MetricPoint("W2", 3m), new MetricPoint("W3", 3m)])
    ];

    [Fact]
    public void Lines_are_drawn_over_the_columns_on_their_own_scale()
    {
        using var context = new BunitContext();

        var chart = context.Render<MetricStackedBars>(parameters => parameters
            .Add(c => c.Series, Hours)
            .Add(c => c.Lines, Counts)
            .Add(c => c.FormatLine, value => $"{value} n")
            .Add(c => c.TestId, "chart"));

        // The columns' scale is untouched by the lines, and the lines' is the tallest
        // line value in its own unit.
        Assert.Equal("30", chart.Find(".metric-stacked-bars__scale-max").TextContent);
        Assert.Equal("8 n", chart.Find(".metric-stacked-bars__scale--right .metric-stacked-bars__scale-max").TextContent);

        var lines = chart.FindAll("polyline.metric-stacked-bars__line");
        Assert.Equal(2, lines.Count);

        // One point per column at its centre; 8 of 8 is the top, 4 of 8 halfway.
        Assert.Equal("16.67,50 50,0 83.33,75", lines[0].GetAttribute("points"));
        Assert.Contains("metric-stacked-bars__line--dash-0", lines[0].ClassList);
        Assert.Contains("metric-stacked-bars__line--dash-1", lines[1].ClassList);

        // In the table after the columns, in their own unit, and outside the total.
        var rows = chart.FindAll("tbody tr");
        Assert.Equal(4, rows.Count);
        Assert.Equal("Sessions", rows[2].QuerySelector("th")!.TextContent);
        Assert.Equal(["4 n", "8 n", "2 n"], [.. rows[2].QuerySelectorAll("td").Select(cell => cell.TextContent)]);
        Assert.Equal(["26", "30", "20"], [.. chart.FindAll("tfoot td").Select(cell => cell.TextContent)]);
    }

    [Fact]
    public void A_line_switched_off_leaves_the_overlay_the_table_and_the_right_scale()
    {
        using var context = new BunitContext();

        var chart = context.Render<MetricStackedBars>(parameters => parameters
            .Add(c => c.Series, Hours)
            .Add(c => c.Lines, Counts)
            .Add(c => c.TestId, "chart"));

        var toggle = chart.Find("[data-testid='chart-toggle-sessions']");
        Assert.Equal("true", toggle.GetAttribute("aria-pressed"));

        toggle.Click();

        Assert.Equal("false", chart.Find("[data-testid='chart-toggle-sessions']").GetAttribute("aria-pressed"));
        Assert.Single(chart.FindAll("polyline.metric-stacked-bars__line"));
        Assert.Equal(3, chart.FindAll("tbody tr").Count);

        // The right scale is now the other line's own peak.
        Assert.Equal("3", chart.Find(".metric-stacked-bars__scale--right .metric-stacked-bars__scale-max").TextContent);

        chart.Find("[data-testid='chart-toggle-agents-at-once']").Click();

        // With every line off there is no right scale to print.
        Assert.Empty(chart.FindAll(".metric-stacked-bars__scale--right"));
        Assert.Empty(chart.FindAll("polyline"));
    }

    [Fact]
    public void Lines_alone_are_a_chart_with_no_columns_and_no_total()
    {
        using var context = new BunitContext();

        var chart = context.Render<MetricStackedBars>(parameters => parameters
            .Add(c => c.Series, [])
            .Add(c => c.Lines, Counts));

        Assert.Equal(3, chart.FindAll(".metric-stacked-bars__column").Count);
        Assert.All(chart.FindAll(".metric-stacked-bars__column"), column => Assert.Empty(column.QuerySelectorAll(".metric-stacked-bars__segment")));
        Assert.Equal(2, chart.FindAll("polyline").Count);
        Assert.Empty(chart.FindAll("tfoot"));
    }

    [Fact]
    public void A_band_that_resolves_to_an_identity_wears_it_instead_of_the_ramp()
    {
        // The repository hue, where the caller can name one; the ramp where it cannot.
        using var context = new BunitContext();

        var chart = context.Render<MetricStackedBars>(parameters => parameters
            .Add(c => c.Series, Hours)
            .Add(c => c.IdentityOf, name => name == "Producing" ? 3 : null)
            .Add(c => c.TestId, "chart"));

        var segments = chart.FindAll(".metric-stacked-bars__column")[0].QuerySelectorAll(".metric-stacked-bars__segment");

        Assert.Contains("metric-stacked-bars__segment--identity-3", segments[0].ClassList);
        Assert.DoesNotContain("metric-stacked-bars__segment--2", segments[0].ClassList);
        Assert.Contains("metric-stacked-bars__segment--4", segments[1].ClassList);

        Assert.Contains("metric-stacked-bars__swatch--identity-3", chart.Find("[data-testid='chart-toggle-producing'] .metric-stacked-bars__swatch").ClassList);
        Assert.Contains("metric-stacked-bars__swatch--4", chart.Find("[data-testid='chart-toggle-waiting'] .metric-stacked-bars__swatch").ClassList);
    }

    [Fact]
    public void A_bound_hidden_set_is_the_callers_and_every_toggle_hands_it_back()
    {
        // The caller owns the state, because the same choice drives another chart.
        using var context = new BunitContext();

        IReadOnlyCollection<string>? handed = null;

        var chart = context.Render<MetricStackedBars>(parameters => parameters
            .Add(c => c.Series, Hours)
            .Add(c => c.Hidden, new[] { "Waiting" })
            .Add(c => c.HiddenChanged, EventCallback.Factory.Create<IReadOnlyCollection<string>>(this, set => handed = set))
            .Add(c => c.TestId, "chart"));

        // Off from the first render, as bound.
        Assert.Equal("false", chart.Find("[data-testid='chart-toggle-waiting']").GetAttribute("aria-pressed"));
        Assert.All(chart.FindAll(".metric-stacked-bars__column"), column => Assert.Single(column.QuerySelectorAll(".metric-stacked-bars__segment")));

        chart.Find("[data-testid='chart-toggle-producing']").Click();

        Assert.NotNull(handed);
        Assert.Equal(["Producing", "Waiting"], handed!.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_key_legend_names_the_series_and_offers_nothing_to_press()
    {
        using var context = new BunitContext();

        var chart = context.Render<MetricStackedBars>(parameters => parameters
            .Add(c => c.Series, Hours)
            .Add(c => c.LegendInteractive, false)
            .Add(c => c.TestId, "chart"));

        var legend = chart.Find(".metric-stacked-bars__legend");

        Assert.Equal("UL", legend.TagName);
        Assert.Empty(legend.QuerySelectorAll("button"));
        Assert.Equal(2, legend.QuerySelectorAll(".metric-stacked-bars__legend-item--key").Length);
        Assert.Contains("40h over the period", chart.Find("[data-testid='chart-toggle-producing']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_legend_can_sit_above_the_plot()
    {
        using var context = new BunitContext();

        var chart = context.Render<MetricStackedBars>(parameters => parameters
            .Add(c => c.Series, Hours)
            .Add(c => c.LegendAbove, true));

        var figure = chart.Find("figure");
        var legend = figure.QuerySelector(".metric-stacked-bars__legend")!;
        var plot = figure.QuerySelector(".metric-stacked-bars__plot")!;

        Assert.True(legend.CompareDocumentPosition(plot).HasFlag(AngleSharp.Dom.DocumentPositions.Following));
    }

    [Fact]
    public void A_column_is_pressable_only_while_someone_listens_and_the_picked_one_says_so()
    {
        using var context = new BunitContext();

        string? picked = null;

        var silent = context.Render<MetricStackedBars>(parameters => parameters.Add(c => c.Series, Hours));

        Assert.Null(silent.Find(".metric-stacked-bars__column").GetAttribute("role"));

        var chart = context.Render<MetricStackedBars>(parameters => parameters
            .Add(c => c.Series, Hours)
            .Add(c => c.SelectedBucket, "W2")
            .Add(c => c.OnBucketSelected, EventCallback.Factory.Create<string>(this, label => picked = label)));

        var columns = chart.FindAll(".metric-stacked-bars__column");

        Assert.All(columns, column => Assert.Equal("button", column.GetAttribute("role")));
        Assert.Contains("metric-stacked-bars__column--selected", columns[1].ClassList);
        Assert.Equal("true", columns[1].GetAttribute("aria-pressed"));
        Assert.DoesNotContain("metric-stacked-bars__column--selected", columns[0].ClassList);

        columns[2].Click();

        Assert.Equal("W3", picked);
    }
}
