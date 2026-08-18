using System.Globalization;

namespace Backlog.UI.Components.UnitTests;

public sealed class MetricStackedAreaTests
{
    /// <summary>Two series, three buckets, and a split that is not the same in every
    /// bucket — a fixture where every bucket happened to be 50/50 would pass a chart
    /// that ignored the data.</summary>
    private static IReadOnlyList<MetricSeries> Split =>
    [
        new("alpha", [new MetricPoint("w1", 30m), new MetricPoint("w2", 10m), new MetricPoint("w3", 5m)]),
        new("beta", [new MetricPoint("w1", 10m), new MetricPoint("w2", 10m), new MetricPoint("w3", 15m)])
    ];

    /// <summary>The percentages out of one band's hidden-table row.</summary>
    private static IReadOnlyList<string> RowShares(IRenderedComponent<MetricStackedArea> chart, int row) =>
        [.. chart.FindAll("tbody tr")[row].QuerySelectorAll("td").Select(cell => cell.TextContent)];

    [Fact]
    public void Raw_amounts_become_shares_of_their_own_bucket()
    {
        // The caller passes hours; the chart divides. 30 of 40 is 75%, and the same
        // series is 25% of the next bucket without its own figure changing much.
        using var context = new BunitContext();

        var chart = context.Render<MetricStackedArea>(parameters => parameters
            .Add(a => a.Series, Split));

        // Last cell of each row is the share of the whole period, not of a bucket.
        Assert.Equal(["75%", "50%", "25%", "56.3%"], RowShares(chart, 0));
        Assert.Equal(["25%", "50%", "75%", "43.8%"], RowShares(chart, 1));
    }

    [Fact]
    public void Every_bucket_adds_up_to_a_hundred_percent()
    {
        // The property the whole component exists for. Three series with awkward
        // amounts, so this is not passing on a fixture that happened to be tidy.
        using var context = new BunitContext();

        var chart = context.Render<MetricStackedArea>(parameters => parameters
            .Add(a => a.Series,
            [
                new MetricSeries("a", [new MetricPoint("w1", 7m), new MetricPoint("w2", 1m)]),
                new MetricSeries("b", [new MetricPoint("w1", 11m), new MetricPoint("w2", 1m)]),
                new MetricSeries("c", [new MetricPoint("w1", 13m), new MetricPoint("w2", 1m)])
            ]));

        var rows = chart.FindAll("tbody tr");

        foreach (var bucket in new[] { 0, 1 })
        {
            var total = rows.Sum(row => decimal.Parse(
                row.QuerySelectorAll("td")[bucket].TextContent.TrimEnd('%'),
                CultureInfo.InvariantCulture));

            // A tenth of slack: the cells are rounded for display, and the point is
            // that they read as a whole rather than that they are exact to the bit.
            Assert.InRange(total, 99.9m, 100.1m);
        }
    }

    [Fact]
    public void The_bands_stack_from_the_baseline_in_the_order_given()
    {
        // Order is meaning: the first series sits on the stable baseline and the last
        // absorbs everyone else's movement at the top.
        using var context = new BunitContext();

        var chart = context.Render<MetricStackedArea>(parameters => parameters
            .Add(a => a.Series, Split));

        var polygons = chart.FindAll("polygon");

        // alpha is 75% of w1, so its top edge is a quarter of the way down the box and
        // its bottom edge is the baseline.
        Assert.StartsWith("0,25 ", polygons[0].GetAttribute("points"), StringComparison.Ordinal);
        Assert.EndsWith(" 0,100", polygons[0].GetAttribute("points"), StringComparison.Ordinal);

        // beta sits on top of alpha: from alpha's top edge up to 100%.
        Assert.StartsWith("0,0 ", polygons[1].GetAttribute("points"), StringComparison.Ordinal);
        Assert.EndsWith(" 0,25", polygons[1].GetAttribute("points"), StringComparison.Ordinal);
    }

    [Fact]
    public void Bands_take_ramp_steps_bottom_to_top_and_repeat_past_the_fourth()
    {
        // Four shades of one hue is all the palette has. Past that the in-band name is
        // what tells two bands apart, which is why it is not optional here.
        using var context = new BunitContext();

        var chart = context.Render<MetricStackedArea>(parameters => parameters
            .Add(a => a.Series,
            [.. Enumerable.Range(1, 5).Select(n =>
                new MetricSeries($"s{n}", [new MetricPoint("w1", 10m), new MetricPoint("w2", 10m)]))]));

        var polygons = chart.FindAll("polygon");

        Assert.Contains("metric-stacked-area__band--1", polygons[0].ClassList);
        Assert.Contains("metric-stacked-area__band--4", polygons[3].ClassList);
        Assert.Contains("metric-stacked-area__band--1", polygons[4].ClassList);
    }

    [Fact]
    public void A_bucket_nobody_logged_breaks_the_bands_rather_than_being_bridged()
    {
        // A straight line drawn across a week with nothing in it is an invention, and
        // there is no honest way to draw a share of nothing.
        using var context = new BunitContext();

        var chart = context.Render<MetricStackedArea>(parameters => parameters
            .Add(a => a.Series,
            [
                new MetricSeries("a", [new MetricPoint("w1", 5m), new MetricPoint("w2", 0m), new MetricPoint("w3", 5m)]),
                new MetricSeries("b", [new MetricPoint("w1", 5m), new MetricPoint("w2", 0m), new MetricPoint("w3", 5m)])
            ]));

        // Two runs per band, either side of the gap, rather than one polygon across it.
        Assert.Equal(4, chart.FindAll("polygon").Count);
        Assert.Equal("not reported", RowShares(chart, 0)[1]);
    }

    [Fact]
    public void A_series_missing_from_a_bucket_is_a_share_of_zero_not_a_gap()
    {
        // The two are different facts: nobody logged anything that week, versus this
        // project got none of a week that others did log.
        using var context = new BunitContext();

        var chart = context.Render<MetricStackedArea>(parameters => parameters
            .Add(a => a.Series,
            [
                new MetricSeries("steady", [new MetricPoint("w1", 10m), new MetricPoint("w2", 10m)]),
                new MetricSeries("late", [new MetricPoint("w1", 0m), new MetricPoint("w2", 10m)])
            ]));

        Assert.Equal(["100%", "50%", "66.7%"], RowShares(chart, 0));
        Assert.Equal(["0%", "50%", "33.3%"], RowShares(chart, 1));
    }

    [Fact]
    public void Bucket_totals_are_printed_because_the_chart_cannot_show_volume()
    {
        // A hundred percent of a 40-hour week and of a 4-hour week draw identically.
        using var context = new BunitContext();

        var chart = context.Render<MetricStackedArea>(parameters => parameters
            .Add(a => a.Series, Split)
            .Add(a => a.FormatTotal, hours => $"{hours}h"));

        var totals = chart.FindAll(".metric-stacked-area__tick-total").Select(e => e.TextContent).ToList();

        Assert.Equal(["40h", "20h", "20h"], totals);
        // And in the hidden table, where every bucket is listed rather than every nth.
        Assert.Contains("40h", chart.Find("tfoot").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Totals_can_be_turned_off_but_the_hidden_table_keeps_them()
    {
        using var context = new BunitContext();

        var chart = context.Render<MetricStackedArea>(parameters => parameters
            .Add(a => a.Series, Split)
            .Add(a => a.ShowTotals, false));

        Assert.Empty(chart.FindAll(".metric-stacked-area__tick-total"));
        Assert.Contains("Logged in total", chart.Find("tfoot").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_band_thick_enough_gets_its_name_written_across_it()
    {
        using var context = new BunitContext();

        var chart = context.Render<MetricStackedArea>(parameters => parameters
            .Add(a => a.Series, Split));

        var labels = chart.FindAll(".metric-stacked-area__in-band").Select(e => e.TextContent.Trim()).ToList();

        Assert.Equal(["alpha", "beta"], labels);
    }

    [Fact]
    public void A_band_too_thin_to_hold_a_name_is_left_to_the_legend()
    {
        using var context = new BunitContext();

        var chart = context.Render<MetricStackedArea>(parameters => parameters
            .Add(a => a.Series,
            [
                new MetricSeries("dominant", [new MetricPoint("w1", 99m), new MetricPoint("w2", 99m)]),
                new MetricSeries("sliver", [new MetricPoint("w1", 1m), new MetricPoint("w2", 1m)])
            ]));

        Assert.Equal(["dominant"], chart.FindAll(".metric-stacked-area__in-band").Select(e => e.TextContent.Trim()));
        // Still identified, just not in place.
        Assert.Contains("sliver", chart.Find(".metric-stacked-area__legend").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_label_reads_against_whichever_shade_it_landed_on()
    {
        // Steps 3 and 4 are light enough that only inverse ink is legible on them.
        // Getting this wrong is how a name disappears into its own band.
        using var context = new BunitContext();

        var chart = context.Render<MetricStackedArea>(parameters => parameters
            .Add(a => a.Series,
            [.. Enumerable.Range(1, 4).Select(n =>
                new MetricSeries($"s{n}", [new MetricPoint("w1", 10m), new MetricPoint("w2", 10m)]))]));

        var labels = chart.FindAll(".metric-stacked-area__in-band");

        Assert.Contains("metric-stacked-area__in-band--1", labels[0].ClassList);
        Assert.Contains("metric-stacked-area__in-band--4", labels[3].ClassList);
    }

    [Fact]
    public void Selecting_a_band_mutes_the_others_and_does_not_reorder_the_stack()
    {
        // A stack whose bands move between renders is not the same chart, and the
        // reader loses the position they were using to find things.
        using var context = new BunitContext();

        var chart = context.Render<MetricStackedArea>(parameters => parameters
            .Add(a => a.Series, Split)
            .Add(a => a.Selected, "beta"));

        var polygons = chart.FindAll("polygon");

        Assert.Contains("metric-stacked-area__band--muted", polygons[0].ClassList);
        Assert.DoesNotContain("metric-stacked-area__band--muted", polygons[1].ClassList);
        // Order untouched: alpha is still the band on the baseline.
        Assert.EndsWith(" 0,100", polygons[0].GetAttribute("points"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_picture_is_hidden_and_the_readings_are_in_a_table()
    {
        using var context = new BunitContext();

        var chart = context.Render<MetricStackedArea>(parameters => parameters
            .Add(a => a.Series, Split)
            .Add(a => a.NameHeading, "Repository"));

        Assert.Equal("true", chart.Find(".metric-stacked-area__canvas-wrap").GetAttribute("aria-hidden"));
        // Hidden by the wrapper, not the table: a table ignores overflow and a wide
        // hidden one drags the page sideways.
        Assert.Contains("sr-only", chart.Find("table").ParentElement!.ClassList);
        Assert.Contains("Repository", chart.Find("thead").TextContent, StringComparison.Ordinal);
        Assert.Equal(2, chart.FindAll("tbody tr").Count);
    }

    [Fact]
    public void Coordinates_are_written_invariant()
    {
        // A comma decimal separator inside a points list is the separator between x
        // and y, and the polygon folds up.
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("nl-NL");

            using var context = new BunitContext();

            var chart = context.Render<MetricStackedArea>(parameters => parameters
                .Add(a => a.Series,
                [
                    new MetricSeries("a", [new MetricPoint("w1", 1m), new MetricPoint("w2", 1m), new MetricPoint("w3", 1m)]),
                    new MetricSeries("b", [new MetricPoint("w1", 2m), new MetricPoint("w2", 2m), new MetricPoint("w3", 2m)])
                ]));

            var points = chart.FindAll("polygon")[0].GetAttribute("points")!;

            Assert.DoesNotContain(",,", points, StringComparison.Ordinal);
            Assert.Equal(6, points.Split(' ').Length);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void A_single_reported_bucket_becomes_a_column_rather_than_vanishing()
    {
        // One bucket has no width to make an area from, and dropping it would lose the
        // only reading there is.
        using var context = new BunitContext();

        var chart = context.Render<MetricStackedArea>(parameters => parameters
            .Add(a => a.Series,
            [
                new MetricSeries("a", [new MetricPoint("w1", 0m), new MetricPoint("w2", 5m), new MetricPoint("w3", 0m)]),
                new MetricSeries("b", [new MetricPoint("w1", 0m), new MetricPoint("w2", 5m), new MetricPoint("w3", 0m)])
            ]));

        var polygons = chart.FindAll("polygon");

        Assert.Equal(2, polygons.Count);
        // A sliver centred on w2 at x=50, a quarter-step either side.
        Assert.Contains("37.5,", polygons[0].GetAttribute("points")!, StringComparison.Ordinal);
        Assert.Contains("62.5,", polygons[0].GetAttribute("points")!, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_logged_at_all_is_an_empty_period()
    {
        using var context = new BunitContext();

        var chart = context.Render<MetricStackedArea>(parameters => parameters
            .Add(a => a.Series,
            [
                new MetricSeries("a", [new MetricPoint("w1", 0m), new MetricPoint("w2", 0m)])
            ])
            .Add(a => a.EmptyMessage, "No time logged in this period."));

        Assert.Empty(chart.FindAll("polygon"));
        Assert.Contains("No time logged in this period.", chart.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void No_series_at_all_is_an_empty_period()
    {
        using var context = new BunitContext();

        var chart = context.Render<MetricStackedArea>();

        Assert.Empty(chart.FindAll("polygon"));
        Assert.NotNull(chart.Find(".metric-status"));
    }

    [Fact]
    public void Axis_labels_are_thinned_and_the_last_bucket_always_survives()
    {
        using var context = new BunitContext();

        var chart = context.Render<MetricStackedArea>(parameters => parameters
            .Add(a => a.Series,
            [
                new MetricSeries("a", [.. Enumerable.Range(1, 12).Select(n => new MetricPoint($"w{n}", 5m))])
            ])
            .Add(a => a.MaxLabels, 4));

        var printed = chart.FindAll(".metric-stacked-area__tick-label").Select(e => e.TextContent).ToList();

        Assert.Equal(["w1", "w4", "w7", "w10", "w12"], printed);
    }
}
