namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The marks a spend chart needs once it runs past today: a projected marker on a budget
/// meter, faded projected columns and a budget line on a column chart, and tokens split
/// by direction in a breakdown.
/// </summary>
public sealed class MetricProjectionTests
{
    [Fact]
    public void A_meter_draws_its_marker_where_the_projection_falls()
    {
        using var context = new BunitContext();

        var meter = context.Render<MetricMeter>(parameters => parameters
            .Add(m => m.Value, 34m)
            .Add(m => m.Max, 50m)
            .Add(m => m.Marker, 46m)
            .Add(m => m.MarkerLabel, "Projected $46"));

        var marker = meter.Find(".metric-meter__marker");

        Assert.Equal("left: 92%", marker.GetAttribute("style"));
        Assert.Equal("Projected $46", marker.GetAttribute("title"));
        Assert.Equal("true", marker.GetAttribute("aria-hidden"));
    }

    [Fact]
    public void A_projection_past_the_cap_stands_at_the_end_of_the_track()
    {
        using var context = new BunitContext();

        var meter = context.Render<MetricMeter>(parameters => parameters
            .Add(m => m.Value, 178m)
            .Add(m => m.Max, 250m)
            .Add(m => m.Marker, 266m));

        Assert.Equal("left: 100%", meter.Find(".metric-meter__marker").GetAttribute("style"));
    }

    [Fact]
    public void A_meter_without_a_marker_draws_none()
    {
        using var context = new BunitContext();

        var meter = context.Render<MetricMeter>(parameters => parameters
            .Add(m => m.Value, 34m)
            .Add(m => m.Max, 50m));

        Assert.Empty(meter.FindAll(".metric-meter__marker"));
    }

    [Fact]
    public void Columns_from_the_projected_index_on_are_faded_and_say_so_in_the_table()
    {
        using var context = new BunitContext();

        var bars = context.Render<MetricBars>(parameters => parameters
            .Add(b => b.Points, [new MetricPoint("1", 4m), new MetricPoint("2", 6m), new MetricPoint("3", 5m)])
            .Add(b => b.ProjectedFrom, 2));

        var columns = bars.FindAll(".metric-bars__column");
        Assert.DoesNotContain("metric-bars__column--projected", columns[1].ClassList);
        Assert.Contains("metric-bars__column--projected", columns[2].ClassList);

        var cells = bars.FindAll("tbody td");
        Assert.Equal("6", cells[1].TextContent.Trim());
        Assert.EndsWith("(projected)", cells[2].TextContent.Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_reference_line_is_drawn_at_its_share_of_the_scale_and_labelled()
    {
        using var context = new BunitContext();

        var bars = context.Render<MetricBars>(parameters => parameters
            .Add(b => b.Label, "Spend this month")
            .Add(b => b.Points, [new MetricPoint("1", 50m), new MetricPoint("2", 200m)])
            .Add(b => b.Reference, 100m)
            .Add(b => b.ReferenceLabel, "Budget $100"));

        var line = bars.Find(".metric-bars__reference");
        Assert.Equal("--metric-bars-reference: 0.5", line.GetAttribute("style"));
        Assert.Equal("Budget $100", bars.Find(".metric-bars__reference-label").TextContent);
        Assert.Contains("Budget $100", bars.Find("caption").TextContent, StringComparison.Ordinal);
    }

    /// <summary>A budget above every column still has to be on the chart, so the scale
    /// stretches to it and the line sits at the top.</summary>
    [Fact]
    public void A_reference_above_every_column_stretches_the_scale()
    {
        using var context = new BunitContext();

        var bars = context.Render<MetricBars>(parameters => parameters
            .Add(b => b.Points, [new MetricPoint("1", 50m)])
            .Add(b => b.Reference, 200m));

        Assert.Equal("--metric-bars-reference: 1", bars.Find(".metric-bars__reference").GetAttribute("style"));
        Assert.Equal("height: 25%", bars.Find(".metric-bars__column-fill").GetAttribute("style"));
    }

    [Fact]
    public void Input_and_output_tokens_are_columns_of_their_own_with_a_dash_where_none_were_reported()
    {
        using var context = new BunitContext();

        var table = context.Render<MetricBreakdown>(parameters => parameters
            .Add(b => b.Rows,
            [
                new MetricRow("opus", Cost: new MoneyAmount(131m, "USD")) { InputTokens = 41_200_000, OutputTokens = 3_100_000 },
                new MetricRow("gpt-5", Cost: new MoneyAmount(12m, "USD"))
            ])
            .Add(b => b.ShareFirst, true));

        var headings = table.FindAll("thead th").Select(cell => cell.TextContent.Trim()).ToList();
        Assert.Equal(["Name", "Share", "Input tokens", "Output tokens", "Cost"], headings);

        var gpt = table.FindAll("tbody tr")[1].QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()).ToList();
        Assert.Equal("—", gpt[1]);
        Assert.Equal("—", gpt[2]);

        var opus = table.FindAll("tbody tr")[0];
        Assert.NotNull(opus.QuerySelector(".metric-breakdown__share-cell .metric-breakdown__share-fill"));
    }

    [Fact]
    public void Without_a_split_the_breakdown_keeps_its_columns()
    {
        using var context = new BunitContext();

        var table = context.Render<MetricBreakdown>(parameters => parameters
            .Add(b => b.Rows, [new MetricRow("opus", 1_000L, new MoneyAmount(1m, "USD"))]));

        var headings = table.FindAll("thead th").Select(cell => cell.TextContent.Trim()).ToList();
        Assert.Equal(["Name", "Tokens", "Cost", "Share"], headings);
    }
}
