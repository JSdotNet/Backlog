namespace Backlog.UI.Components.UnitTests;

public sealed class MetricStatusTests
{
    [Fact]
    public void Ready_says_nothing_at_all()
    {
        // Same posture as SaveIndicator when it is idle: a caller binds Kind to
        // whatever it knows and gets an empty element only when there is
        // something to say.
        using var context = new BunitContext();

        var status = context.Render<MetricStatus>(parameters => parameters
            .Add(s => s.Kind, MetricStatusKind.Ready));

        Assert.Equal(string.Empty, status.Markup.Trim());
    }

    [Theory]
    [InlineData(MetricStatusKind.Loading, "Loading...", "metric-status--loading")]
    [InlineData(MetricStatusKind.Empty, "No usage in this period.", "metric-status--empty")]
    [InlineData(MetricStatusKind.Unavailable, "Usage reporting is not available.", "metric-status--unavailable")]
    public void Every_other_kind_has_its_own_words_and_modifier(MetricStatusKind kind, string text, string modifier)
    {
        using var context = new BunitContext();

        var status = context.Render<MetricStatus>(parameters => parameters.Add(s => s.Kind, kind));

        Assert.Contains(text, status.Markup, StringComparison.Ordinal);
        Assert.Contains(modifier, status.Find("p").ClassList);
    }

    [Fact]
    public void The_providers_own_reason_replaces_the_default()
    {
        // That sentence is the only thing on screen naming the credential that is
        // missing, so it has to survive verbatim.
        const string reason = "No GitHub credential is available. Copilot usage needs an organization.";

        using var context = new BunitContext();

        var status = context.Render<MetricStatus>(parameters => parameters
            .Add(s => s.Kind, MetricStatusKind.Unavailable)
            .Add(s => s.Message, reason));

        Assert.Contains(reason, status.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("not available.", status.Find("p").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Not_having_a_number_is_announced_without_interrupting()
    {
        using var context = new BunitContext();

        var status = context.Render<MetricStatus>(parameters => parameters
            .Add(s => s.Kind, MetricStatusKind.Loading));

        Assert.Equal("status", status.Find("p").GetAttribute("role"));
    }

    [Fact]
    public void A_host_can_replace_the_block_class()
    {
        using var context = new BunitContext();

        var status = context.Render<MetricStatus>(parameters => parameters
            .Add(s => s.Kind, MetricStatusKind.Empty)
            .Add(s => s.BaseClass, "usage-status"));

        var classes = status.Find("p").ClassList;

        Assert.Contains("usage-status", classes);
        Assert.Contains("usage-status--empty", classes);
        Assert.DoesNotContain("metric-status", classes);
    }
}

public sealed class MetricTileTests
{
    [Fact]
    public void A_tile_is_a_label_a_number_and_its_unit()
    {
        using var context = new BunitContext();

        var tile = context.Render<MetricTile>(parameters => parameters
            .Add(t => t.Label, "Total tokens")
            .Add(t => t.Value, "412.4M")
            .Add(t => t.Unit, "tokens"));

        Assert.Equal("Total tokens", tile.Find(".metric-tile__label").TextContent);
        Assert.Contains("412.4M", tile.Find(".metric-tile__value").TextContent, StringComparison.Ordinal);
        Assert.Equal("tokens", tile.Find(".metric-tile__unit").TextContent);
    }

    [Fact]
    public void A_delta_says_which_way_and_against_what()
    {
        using var context = new BunitContext();

        var tile = context.Render<MetricTile>(parameters => parameters
            .Add(t => t.Label, "Spend")
            .Add(t => t.Value, "487.20 USD")
            .Add(t => t.Delta, new MetricDelta(0.224m, MetricDeltaUnit.Percent, "previous 14 days")));

        Assert.Contains("up 22.4% vs previous 14 days", tile.Find(".metric-tile__delta").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_delta_with_nothing_to_compare_against_drops_the_clause()
    {
        using var context = new BunitContext();

        var tile = context.Render<MetricTile>(parameters => parameters
            .Add(t => t.Value, "17")
            .Add(t => t.Delta, new MetricDelta(3m, MetricDeltaUnit.Absolute, "")));

        // No space in the text: the arrow is its own element and the gap between
        // them is the flex rule, not a character.
        Assert.Equal("▲up 3", tile.Find(".metric-tile__delta").TextContent.Trim());
    }

    [Theory]
    // More sessions is good news; the same rise in spend is not. Neither the
    // wording nor the number changes — only which modifier the row carries.
    [InlineData(0.2, true, "metric-tile__delta--better")]
    [InlineData(0.2, false, "metric-tile__delta--worse")]
    [InlineData(-0.2, true, "metric-tile__delta--worse")]
    [InlineData(-0.2, false, "metric-tile__delta--better")]
    [InlineData(0.0, true, "metric-tile__delta--flat")]
    public void Whether_up_is_good_belongs_to_the_caller(double value, bool higherIsBetter, string modifier)
    {
        using var context = new BunitContext();

        var tile = context.Render<MetricTile>(parameters => parameters
            .Add(t => t.Value, "1")
            .Add(t => t.Delta, new MetricDelta((decimal)value, MetricDeltaUnit.Percent, "last week", higherIsBetter)));

        Assert.Contains(modifier, tile.Find(".metric-tile__delta").ClassList);
    }

    [Fact]
    public void The_arrow_is_hidden_because_the_word_beside_it_carries_the_direction()
    {
        using var context = new BunitContext();

        var tile = context.Render<MetricTile>(parameters => parameters
            .Add(t => t.Value, "1")
            .Add(t => t.Delta, new MetricDelta(0.2m, MetricDeltaUnit.Percent, "last week")));

        Assert.Equal("true", tile.Find(".metric-tile__delta-arrow").GetAttribute("aria-hidden"));
    }

    [Fact]
    public void Unchanged_gets_no_arrow_at_all()
    {
        // There is no glyph for "sideways" that reads as one, and the word is
        // already there.
        using var context = new BunitContext();

        var tile = context.Render<MetricTile>(parameters => parameters
            .Add(t => t.Value, "1")
            .Add(t => t.Delta, new MetricDelta(0m, MetricDeltaUnit.Percent, "last week")));

        Assert.Empty(tile.FindAll(".metric-tile__delta-arrow"));
        Assert.Contains("unchanged", tile.Find(".metric-tile__delta").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tile_with_no_number_keeps_its_label()
    {
        // Losing it would lose the only clue as to what it is that cannot be
        // shown.
        using var context = new BunitContext();

        var tile = context.Render<MetricTile>(parameters => parameters
            .Add(t => t.Label, "Copilot usage")
            .Add(t => t.Value, "412.4M")
            .Add(t => t.Status, MetricStatusKind.Unavailable)
            .Add(t => t.StatusMessage, "No GitHub credential is available."));

        Assert.Equal("Copilot usage", tile.Find(".metric-tile__label").TextContent);
        Assert.Empty(tile.FindAll(".metric-tile__value"));
        Assert.Contains("No GitHub credential is available.", tile.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tile_with_no_number_shows_no_delta_or_trend_either()
    {
        // A delta against a figure that is not on screen is a claim about
        // nothing.
        using var context = new BunitContext();

        var tile = context.Render<MetricTile>(parameters => parameters
            .Add(t => t.Label, "Spend")
            .Add(t => t.Status, MetricStatusKind.Loading)
            .Add(t => t.Delta, new MetricDelta(0.2m, MetricDeltaUnit.Percent, "last week"))
            .Add(t => t.Trend, builder => builder.AddMarkupContent(0, "<span class=\"trend\"></span>")));

        Assert.Empty(tile.FindAll(".metric-tile__delta"));
        Assert.Empty(tile.FindAll(".metric-tile__trend"));
    }

    [Fact]
    public void Emphasis_is_a_modifier_on_the_tile_rather_than_a_different_component()
    {
        using var context = new BunitContext();

        var tile = context.Render<MetricTile>(parameters => parameters
            .Add(t => t.Value, "487.20 USD")
            .Add(t => t.Emphasis, true));

        Assert.Contains("metric-tile--emphasis", tile.Find("article").ClassList);
    }

    [Fact]
    public void A_host_can_replace_the_block_class_and_every_part_with_it()
    {
        using var context = new BunitContext();

        var tile = context.Render<MetricTile>(parameters => parameters
            .Add(t => t.Label, "Spend")
            .Add(t => t.Value, "487.20 USD")
            .Add(t => t.BaseClass, "usage-tile"));

        Assert.Contains("usage-tile", tile.Find("article").ClassList);
        Assert.NotNull(tile.Find(".usage-tile__label"));
        Assert.NotNull(tile.Find(".usage-tile__value"));
        Assert.DoesNotContain("metric-tile", tile.Markup, StringComparison.Ordinal);
    }
}

public sealed class MetricGridTests
{
    [Fact]
    public void The_minimum_column_width_is_published_as_a_custom_property()
    {
        // So a host can widen it for a denser tile without restating the grid
        // rule.
        using var context = new BunitContext();

        var grid = context.Render<MetricGrid>(parameters => parameters
            .Add(g => g.MinColumnWidth, "22rem"));

        Assert.Contains("--metric-grid-min: 22rem", grid.Find("div").GetAttribute("style")!, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unlabelled_grid_carries_no_role()
    {
        // A group with no accessible name announces a boundary and then cannot
        // say what is inside it.
        using var context = new BunitContext();

        var grid = context.Render<MetricGrid>();

        Assert.Null(grid.Find("div").GetAttribute("role"));
    }

    [Fact]
    public void A_labelled_grid_is_a_group()
    {
        using var context = new BunitContext();

        var grid = context.Render<MetricGrid>(parameters => parameters
            .Add(g => g.Label, "AI usage and cost"));

        var element = grid.Find("div");

        Assert.Equal("group", element.GetAttribute("role"));
        Assert.Equal("AI usage and cost", element.GetAttribute("aria-label"));
    }
}

public sealed class MetricSparklineTests
{
    private static IReadOnlyList<MetricPoint> Series(params decimal[] values) =>
        [.. values.Select((value, index) => new MetricPoint($"d{index}", value))];

    [Fact]
    public void One_point_is_a_dot_pretending_to_be_a_trend()
    {
        using var context = new BunitContext();

        var sparkline = context.Render<MetricSparkline>(parameters => parameters
            .Add(s => s.Points, Series(42m)));

        Assert.Equal(string.Empty, sparkline.Markup.Trim());
    }

    [Fact]
    public void No_points_renders_nothing_rather_than_an_empty_frame()
    {
        using var context = new BunitContext();

        var sparkline = context.Render<MetricSparkline>();

        Assert.Equal(string.Empty, sparkline.Markup.Trim());
    }

    [Fact]
    public void The_scale_runs_zero_to_max_and_not_min_to_max()
    {
        // Zeroed on its own minimum, a series that moved two percent becomes a
        // mountain range. 50 and 100 against a 30-unit box put the first point
        // halfway up, not on the floor.
        using var context = new BunitContext();

        var sparkline = context.Render<MetricSparkline>(parameters => parameters
            .Add(s => s.Points, Series(50m, 100m)));

        Assert.Equal("0,15 100,0", sparkline.Find("polyline").GetAttribute("points"));
    }

    [Fact]
    public void A_flat_series_of_zeroes_sits_on_the_floor_rather_than_dividing_by_it()
    {
        using var context = new BunitContext();

        var sparkline = context.Render<MetricSparkline>(parameters => parameters
            .Add(s => s.Points, Series(0m, 0m, 0m)));

        Assert.Equal("0,30 50,30 100,30", sparkline.Find("polyline").GetAttribute("points"));
    }

    [Fact]
    public void Coordinates_are_written_invariant()
    {
        // A comma decimal separator would emit "12,4" into a points list, where
        // the comma is the separator between x and y, and the line folds up.
        var original = System.Globalization.CultureInfo.CurrentCulture;

        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("nl-NL");

            using var context = new BunitContext();

            var sparkline = context.Render<MetricSparkline>(parameters => parameters
                .Add(s => s.Points, Series(1m, 2m, 3m)));

            var points = sparkline.Find("polyline").GetAttribute("points")!;

            Assert.Equal(3, points.Split(' ').Length);
            Assert.DoesNotContain(",,", points, StringComparison.Ordinal);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void The_picture_is_hidden_and_the_sentence_is_not()
    {
        // The value it is about is printed an inch away in the tile, so the
        // drawing has nothing to add to the tree — but a host that wants a
        // sentence gets one.
        using var context = new BunitContext();

        var sparkline = context.Render<MetricSparkline>(parameters => parameters
            .Add(s => s.Points, Series(1m, 2m))
            .Add(s => s.Description, "Rose steadily over the last fortnight."));

        Assert.Equal("true", sparkline.Find("svg").GetAttribute("aria-hidden"));
        Assert.Equal("Rose steadily over the last fortnight.", sparkline.Find(".sr-only").TextContent);
    }

    [Fact]
    public void The_wash_and_the_marker_can_both_be_turned_off()
    {
        using var context = new BunitContext();

        var sparkline = context.Render<MetricSparkline>(parameters => parameters
            .Add(s => s.Points, Series(1m, 2m))
            .Add(s => s.Filled, false)
            .Add(s => s.ShowEndMarker, false));

        Assert.Empty(sparkline.FindAll(".metric-sparkline__area"));
        Assert.Empty(sparkline.FindAll(".metric-sparkline__marker"));
        Assert.NotNull(sparkline.Find("polyline"));
    }
}

public sealed class MetricBarsTests
{
    private static IReadOnlyList<MetricPoint> Days(int count) =>
        [.. Enumerable.Range(1, count).Select(day => new MetricPoint($"{day:00} Aug", day * 10m))];

    [Fact]
    public void No_buckets_is_an_empty_period_rather_than_an_empty_frame()
    {
        using var context = new BunitContext();

        var bars = context.Render<MetricBars>(parameters => parameters
            .Add(b => b.EmptyMessage, "No spend in this period."));

        Assert.Empty(bars.FindAll(".metric-bars__columns"));
        Assert.Contains("No spend in this period.", bars.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_bucket_is_a_column_and_a_row_in_the_hidden_table()
    {
        // The picture is aria-hidden, so the table is the only readable copy of
        // these figures — and unlike a sparkline's, they appear nowhere else.
        using var context = new BunitContext();

        var bars = context.Render<MetricBars>(parameters => parameters
            .Add(b => b.Points, Days(14))
            .Add(b => b.ValueHeading, "Tokens"));

        Assert.Equal(14, bars.FindAll(".metric-bars__column").Count);
        Assert.Equal(14, bars.FindAll("tbody tr").Count);
        Assert.Equal("true", bars.Find(".metric-bars__plot").GetAttribute("aria-hidden"));
        // The wrapper is hidden, not the table: a table ignores overflow: hidden, so
        // a wide hidden table escapes the clip box and pushes the page sideways.
        Assert.Contains("sr-only", bars.Find("table").ParentElement!.ClassList);
        Assert.DoesNotContain("sr-only", bars.Find("table").ClassList);
        Assert.Contains("Tokens", bars.Find("thead").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Axis_labels_are_thinned_and_the_last_one_always_survives()
    {
        // Fourteen dates under a chart this size overlap into a grey smear, and
        // the most recent bucket is the one a reader looks for first.
        using var context = new BunitContext();

        var bars = context.Render<MetricBars>(parameters => parameters
            .Add(b => b.Points, Days(14))
            .Add(b => b.MaxLabels, 7));

        var printed = bars.FindAll(".metric-bars__column-label")
            .Select(label => label.TextContent)
            .Where(text => text.Length > 0)
            .ToList();

        Assert.Equal(["01 Aug", "03 Aug", "05 Aug", "07 Aug", "09 Aug", "11 Aug", "13 Aug", "14 Aug"], printed);
    }

    [Fact]
    public void A_short_series_prints_every_label()
    {
        using var context = new BunitContext();

        var bars = context.Render<MetricBars>(parameters => parameters
            .Add(b => b.Points, Days(5))
            .Add(b => b.MaxLabels, 7));

        Assert.Equal(5, bars.FindAll(".metric-bars__column-label")
            .Count(label => label.TextContent.Length > 0));
    }

    [Fact]
    public void Heights_are_a_share_of_the_peak()
    {
        using var context = new BunitContext();

        var bars = context.Render<MetricBars>(parameters => parameters
            .Add(b => b.Points, [new MetricPoint("a", 25m), new MetricPoint("b", 100m)]));

        var fills = bars.FindAll(".metric-bars__column-fill");

        Assert.Contains("height: 25%", fills[0].GetAttribute("style")!, StringComparison.Ordinal);
        Assert.Contains("height: 100%", fills[1].GetAttribute("style")!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_period_of_nothing_leaves_every_column_empty_rather_than_full()
    {
        // A week with no AI usage in it is legitimate, and dividing by its peak
        // would fill every bar to the top.
        using var context = new BunitContext();

        var bars = context.Render<MetricBars>(parameters => parameters
            .Add(b => b.Points, [new MetricPoint("a", 0m), new MetricPoint("b", 0m)]));

        Assert.All(
            bars.FindAll(".metric-bars__column-fill"),
            fill => Assert.Contains("height: 0%", fill.GetAttribute("style")!, StringComparison.Ordinal));
    }

    [Fact]
    public void Only_the_last_column_can_be_the_current_one()
    {
        using var context = new BunitContext();

        var bars = context.Render<MetricBars>(parameters => parameters
            .Add(b => b.Points, Days(4))
            .Add(b => b.HighlightLast, true));

        var current = bars.FindAll(".metric-bars__column--current");

        Assert.Single(current);
        Assert.Contains("04 Aug", current[0].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hidden_table_is_captioned_with_what_it_is_about()
    {
        using var context = new BunitContext();

        var named = context.Render<MetricBars>(parameters => parameters
            .Add(b => b.Points, Days(3))
            .Add(b => b.Label, "Spend per day, in USD"));

        Assert.Equal("Spend per day, in USD", named.Find("caption").TextContent);

        var unnamed = context.Render<MetricBars>(parameters => parameters
            .Add(b => b.Points, Days(3))
            .Add(b => b.BucketHeading, "Day")
            .Add(b => b.ValueHeading, "Cost"));

        Assert.Equal("Cost per day", unnamed.Find("caption").TextContent);
    }

    [Fact]
    public void A_formatter_is_how_a_chart_over_money_keeps_its_currency()
    {
        using var context = new BunitContext();

        var bars = context.Render<MetricBars>(parameters => parameters
            .Add(b => b.Points, [new MetricPoint("a", 41.28m), new MetricPoint("b", 55.94m)])
            .Add(b => b.FormatValue, amount => MetricFormat.Money(new MoneyAmount(amount, "USD"))));

        Assert.Contains("41.28 USD", bars.Find("tbody").TextContent, StringComparison.Ordinal);
        Assert.Equal("55.94 USD", bars.Find(".metric-bars__scale-max").TextContent);
    }
}

public sealed class MetricBreakdownBarTests
{
    private static readonly IReadOnlyList<MetricPart> TokenKinds =
    [
        new("Cache read", 750m),
        new("Input", 150m),
        new("Cache write", 75m),
        new("Output", 25m)
    ];

    [Fact]
    public void Shares_are_of_the_total_and_add_up_to_all_of_it()
    {
        using var context = new BunitContext();

        var bar = context.Render<MetricBreakdownBar>(parameters => parameters
            .Add(b => b.Parts, TokenKinds));

        var shares = bar.FindAll(".metric-breakdown-bar__legend-share").Select(cell => cell.TextContent).ToList();

        Assert.Equal(["75%", "15%", "7.5%", "2.5%"], shares);
    }

    [Fact]
    public void Parts_take_ramp_steps_in_the_order_given()
    {
        // The caller owns the ordinal scale, which for tokens means cheapest
        // first so the bar grows more expensive to the right.
        using var context = new BunitContext();

        var bar = context.Render<MetricBreakdownBar>(parameters => parameters
            .Add(b => b.Parts, TokenKinds));

        var segments = bar.FindAll(".metric-breakdown-bar__segment");

        Assert.Contains("metric-breakdown-bar__segment--1", segments[0].ClassList);
        Assert.Contains("metric-breakdown-bar__segment--4", segments[3].ClassList);
    }

    [Fact]
    public void Past_the_fourth_part_the_ramp_repeats()
    {
        // The palette carries one saturated hue and four distinguishable steps of
        // it; beyond that the legend tells two parts apart, not the colour.
        using var context = new BunitContext();

        var bar = context.Render<MetricBreakdownBar>(parameters => parameters
            .Add(b => b.Parts, [.. Enumerable.Range(1, 5).Select(n => new MetricPart($"p{n}", 10m))]));

        var segments = bar.FindAll(".metric-breakdown-bar__segment");

        Assert.Contains("metric-breakdown-bar__segment--1", segments[4].ClassList);
    }

    [Fact]
    public void A_part_worth_nothing_is_not_a_part()
    {
        using var context = new BunitContext();

        var bar = context.Render<MetricBreakdownBar>(parameters => parameters
            .Add(b => b.Parts, [new MetricPart("Cache read", 100m), new MetricPart("Output", 0m)]));

        Assert.Single(bar.FindAll(".metric-breakdown-bar__segment"));
        Assert.Equal("100%", bar.Find(".metric-breakdown-bar__legend-share").TextContent);
    }

    [Fact]
    public void Every_part_zero_is_the_same_as_no_parts()
    {
        // There is no whole to be a part of, and dividing by it is the only way
        // to find out.
        using var context = new BunitContext();

        var bar = context.Render<MetricBreakdownBar>(parameters => parameters
            .Add(b => b.Parts, [new MetricPart("Cache read", 0m), new MetricPart("Output", 0m)])
            .Add(b => b.EmptyMessage, "No tokens in this period."));

        Assert.Empty(bar.FindAll(".metric-breakdown-bar__track"));
        Assert.Contains("No tokens in this period.", bar.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_bar_is_hidden_and_the_legend_is_the_content()
    {
        // A share rendered only as a width is not a number anyone can read.
        using var context = new BunitContext();

        var bar = context.Render<MetricBreakdownBar>(parameters => parameters
            .Add(b => b.Parts, TokenKinds));

        Assert.Equal("true", bar.Find(".metric-breakdown-bar__track").GetAttribute("aria-hidden"));
        Assert.Equal(4, bar.FindAll(".metric-breakdown-bar__legend-item").Count);
        Assert.Equal("Cache read", bar.Find(".metric-breakdown-bar__legend-label").TextContent);
    }

    [Fact]
    public void Widths_are_written_invariant()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;

        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("nl-NL");

            using var context = new BunitContext();

            var bar = context.Render<MetricBreakdownBar>(parameters => parameters
                .Add(b => b.Parts, [new MetricPart("a", 1m), new MetricPart("b", 3m)]));

            Assert.Contains("width: 25%", bar.FindAll(".metric-breakdown-bar__segment")[0].GetAttribute("style")!,
                StringComparison.Ordinal);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void The_legend_can_be_dropped_but_then_so_is_the_only_readable_copy()
    {
        using var context = new BunitContext();

        var bar = context.Render<MetricBreakdownBar>(parameters => parameters
            .Add(b => b.Parts, TokenKinds)
            .Add(b => b.ShowLegend, false));

        Assert.Empty(bar.FindAll(".metric-breakdown-bar__legend"));
        Assert.NotNull(bar.Find(".metric-breakdown-bar__track"));
    }
}

public sealed class MetricMeterTests
{
    [Fact]
    public void The_reading_and_the_cap_reach_the_tree_as_words()
    {
        // "34 of 50" announced as a bare number loses the currency.
        using var context = new BunitContext();

        var meter = context.Render<MetricMeter>(parameters => parameters
            .Add(m => m.Label, "Spend this month")
            .Add(m => m.Value, 612.40m)
            .Add(m => m.Max, 1_000m)
            .Add(m => m.FormatValue, amount => MetricFormat.Money(new MoneyAmount(amount, "USD"))));

        var track = meter.Find("[role=meter]");

        Assert.Equal("612.4", track.GetAttribute("aria-valuenow"));
        Assert.Equal("0", track.GetAttribute("aria-valuemin"));
        Assert.Equal("1000", track.GetAttribute("aria-valuemax"));
        Assert.Equal("612.40 USD of 1,000.00 USD", track.GetAttribute("aria-valuetext"));
    }

    [Fact]
    public void The_fill_is_the_share_of_the_cap()
    {
        using var context = new BunitContext();

        var meter = context.Render<MetricMeter>(parameters => parameters
            .Add(m => m.Value, 250m)
            .Add(m => m.Max, 1_000m));

        Assert.Contains("width: 25%", meter.Find(".metric-meter__fill").GetAttribute("style")!, StringComparison.Ordinal);
        Assert.Contains("25% of the cap", meter.Find(".metric-meter__caption").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Past_the_threshold_is_a_modifier_and_a_sentence()
    {
        using var context = new BunitContext();

        var meter = context.Render<MetricMeter>(parameters => parameters
            .Add(m => m.Value, 874.20m)
            .Add(m => m.Max, 1_000m)
            .Add(m => m.Threshold, 800m));

        Assert.Contains("metric-meter--warning", meter.Find("div").ClassList);
        Assert.Contains("past the 800 warning mark", meter.Find(".metric-meter__caption").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Over_the_cap_the_fill_stops_and_the_words_carry_on()
    {
        // The fill has nowhere left to go, so the state has to be readable
        // somewhere that is not a length — and never as a red number, which the
        // palette has no token for.
        using var context = new BunitContext();

        var meter = context.Render<MetricMeter>(parameters => parameters
            .Add(m => m.Value, 1_148.65m)
            .Add(m => m.Max, 1_000m)
            .Add(m => m.Threshold, 800m));

        Assert.Contains("metric-meter--over", meter.Find("div").ClassList);
        Assert.DoesNotContain("metric-meter--warning", meter.Find("div").ClassList);
        Assert.Contains("width: 100%", meter.Find(".metric-meter__fill").GetAttribute("style")!, StringComparison.Ordinal);
        Assert.Contains("over by 148.65", meter.Find(".metric-meter__caption").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_threshold_outside_the_track_is_not_drawn()
    {
        // A tick at or past the cap marks the end of the bar, which the end of
        // the bar already does.
        using var context = new BunitContext();

        var atCap = context.Render<MetricMeter>(parameters => parameters
            .Add(m => m.Value, 10m)
            .Add(m => m.Max, 100m)
            .Add(m => m.Threshold, 100m));

        Assert.Empty(atCap.FindAll(".metric-meter__threshold"));

        var inside = context.Render<MetricMeter>(parameters => parameters
            .Add(m => m.Value, 10m)
            .Add(m => m.Max, 100m)
            .Add(m => m.Threshold, 80m));

        Assert.Contains("left: 80%", inside.Find(".metric-meter__threshold").GetAttribute("style")!, StringComparison.Ordinal);
    }

    [Fact]
    public void No_cap_is_not_a_meter_and_says_so_by_staying_empty()
    {
        using var context = new BunitContext();

        var meter = context.Render<MetricMeter>(parameters => parameters
            .Add(m => m.Value, 250m)
            .Add(m => m.Max, 0m));

        Assert.Contains("width: 0%", meter.Find(".metric-meter__fill").GetAttribute("style")!, StringComparison.Ordinal);
        Assert.Equal("250", meter.Find(".metric-meter__reading").TextContent);
        Assert.Empty(meter.FindAll(".metric-meter__caption"));
    }

    [Fact]
    public void A_credit_against_a_budget_is_not_a_reading_below_zero()
    {
        using var context = new BunitContext();

        var meter = context.Render<MetricMeter>(parameters => parameters
            .Add(m => m.Value, -50m)
            .Add(m => m.Max, 100m));

        Assert.Equal("0", meter.Find("[role=meter]").GetAttribute("aria-valuenow"));
        Assert.Contains("width: 0%", meter.Find(".metric-meter__fill").GetAttribute("style")!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_caller_can_replace_the_caption_or_drop_it()
    {
        using var context = new BunitContext();

        var replaced = context.Render<MetricMeter>(parameters => parameters
            .Add(m => m.Value, 9m)
            .Add(m => m.Max, 12m)
            .Add(m => m.Caption, "3 of 12 seats unassigned."));

        Assert.Equal("3 of 12 seats unassigned.", replaced.Find(".metric-meter__caption").TextContent);

        var dropped = context.Render<MetricMeter>(parameters => parameters
            .Add(m => m.Value, 9m)
            .Add(m => m.Max, 12m)
            .Add(m => m.Caption, string.Empty));

        Assert.Empty(dropped.FindAll(".metric-meter__caption"));
    }

    [Fact]
    public void An_unlabelled_meter_takes_its_name_from_the_parameter_instead()
    {
        using var context = new BunitContext();

        var meter = context.Render<MetricMeter>(parameters => parameters
            .Add(m => m.Value, 1m)
            .Add(m => m.Max, 2m)
            .Add(m => m.AriaLabel, "Spend against the monthly cap"));

        var track = meter.Find("[role=meter]");

        Assert.Equal("Spend against the monthly cap", track.GetAttribute("aria-label"));
        Assert.Null(track.GetAttribute("aria-labelledby"));
    }
}

public sealed class MetricBreakdownTests
{
    private static readonly IReadOnlyList<MetricRow> ByModel =
    [
        new("claude-opus-5", 168_400_000L, new MoneyAmount(331.85m, "USD")),
        new("claude-sonnet-5", 201_700_000L, new MoneyAmount(138.42m, "USD")),
        new("claude-haiku-4-5", 42_300_000L, new MoneyAmount(16.93m, "USD"))
    ];

    [Fact]
    public void A_row_per_model_plus_a_total()
    {
        using var context = new BunitContext();

        var table = context.Render<MetricBreakdown>(parameters => parameters
            .Add(b => b.Rows, ByModel)
            .Add(b => b.NameHeading, "Model"));

        Assert.Equal(3, table.FindAll("tbody tr").Count);
        Assert.Contains("412.4M", table.Find("tfoot").TextContent, StringComparison.Ordinal);
        Assert.Contains("487.20 USD", table.Find("tfoot").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Tokens_and_cost_do_not_produce_the_same_ranking()
    {
        // Sonnet burns more tokens than Opus and costs less than half as much,
        // which is why which column the share is of is the caller's to say.
        using var context = new BunitContext();

        var byCost = context.Render<MetricBreakdown>(parameters => parameters
            .Add(b => b.Rows, ByModel)
            .Add(b => b.ShareOf, MetricShareOf.Cost));

        var byTokens = context.Render<MetricBreakdown>(parameters => parameters
            .Add(b => b.Rows, ByModel)
            .Add(b => b.ShareOf, MetricShareOf.Tokens));

        Assert.Equal("68.1%", byCost.FindAll(".metric-breakdown__share-text")[0].TextContent);
        Assert.Equal("40.8%", byTokens.FindAll(".metric-breakdown__share-text")[0].TextContent);
    }

    [Fact]
    public void A_column_nobody_reported_into_is_not_drawn()
    {
        // GitHub publishes no figures per Copilot seat, and a wholly em-dashed
        // cost column reads as a broken feature rather than as data that does
        // not exist.
        using var context = new BunitContext();

        var table = context.Render<MetricBreakdown>(parameters => parameters
            .Add(b => b.Rows,
            [
                new MetricRow("j.schepers", Detail: "VS Code · active 2 hours ago"),
                new MetricRow("a.dekker", Detail: "Visual Studio · active yesterday")
            ])
            .Add(b => b.NameHeading, "Seat")
            .Add(b => b.ShowShare, false)
            .Add(b => b.ShowTotal, false));

        var headings = table.FindAll("thead th").Select(cell => cell.TextContent).ToList();

        Assert.Equal(["Seat"], headings);
        Assert.Contains("VS Code", table.Find("tbody").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_figure_that_was_not_reported_is_an_em_dash_and_not_a_zero()
    {
        // "Not reported" and "zero" are different facts about the world.
        using var context = new BunitContext();

        var table = context.Render<MetricBreakdown>(parameters => parameters
            .Add(b => b.Rows,
            [
                new MetricRow("claude-opus-5", 168_400_000L, new MoneyAmount(331.85m, "USD")),
                new MetricRow("unknown model", null, new MoneyAmount(4.10m, "USD"))
            ]));

        var cells = table.FindAll("tbody tr")[1].QuerySelectorAll("td");

        Assert.Equal("—", cells[0].TextContent);
    }

    [Fact]
    public void Two_currencies_do_not_add_up_so_the_total_refuses_to()
    {
        // A total that silently picks one of them is a wrong number rather than a
        // missing one.
        using var context = new BunitContext();

        var table = context.Render<MetricBreakdown>(parameters => parameters
            .Add(b => b.Rows,
            [
                new MetricRow("acme-eu", 118_400_000L, new MoneyAmount(214.60m, "EUR")),
                new MetricRow("acme-us", 294_000_000L, new MoneyAmount(272.60m, "USD"))
            ]));

        var footer = table.Find("tfoot").QuerySelectorAll("td");

        // Tokens still add up; the money does not.
        Assert.Equal("412.4M", footer[0].TextContent);
        Assert.Equal("—", footer[1].TextContent);
    }

    [Fact]
    public void Ready_with_nothing_in_it_is_empty()
    {
        // MetricStatus renders nothing for Ready, so without this the caller gets
        // a heading over a blank space.
        using var context = new BunitContext();

        var table = context.Render<MetricBreakdown>(parameters => parameters
            .Add(b => b.Rows, [])
            .Add(b => b.Label, "By model"));

        Assert.Empty(table.FindAll("table"));
        Assert.Contains("metric-status--empty", table.Find(".metric-status").ClassList);
    }

    [Fact]
    public void Unavailable_beats_having_rows_to_show()
    {
        using var context = new BunitContext();

        var table = context.Render<MetricBreakdown>(parameters => parameters
            .Add(b => b.Rows, ByModel)
            .Add(b => b.Status, MetricStatusKind.Unavailable)
            .Add(b => b.StatusMessage, "No Claude admin key is configured."));

        Assert.Empty(table.FindAll("table"));
        Assert.Contains("No Claude admin key is configured.", table.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_share_bar_repeats_the_percentage_so_it_adds_nothing_to_the_tree()
    {
        using var context = new BunitContext();

        var table = context.Render<MetricBreakdown>(parameters => parameters
            .Add(b => b.Rows, ByModel));

        Assert.Equal("true", table.Find(".metric-breakdown__share-track").GetAttribute("aria-hidden"));
    }

    [Fact]
    public void A_label_names_the_table_and_a_parameter_names_an_unlabelled_one()
    {
        using var context = new BunitContext();

        var labelled = context.Render<MetricBreakdown>(parameters => parameters
            .Add(b => b.Rows, ByModel)
            .Add(b => b.Label, "By model"));

        Assert.NotNull(labelled.Find("table").GetAttribute("aria-labelledby"));
        Assert.Null(labelled.Find("table").GetAttribute("aria-label"));

        var unlabelled = context.Render<MetricBreakdown>(parameters => parameters
            .Add(b => b.Rows, ByModel)
            .Add(b => b.AriaLabel, "Usage and cost by model"));

        Assert.Equal("Usage and cost by model", unlabelled.Find("table").GetAttribute("aria-label"));
    }

    [Fact]
    public void Per_model_costs_can_ask_for_the_decimals_they_actually_carry()
    {
        using var context = new BunitContext();

        var table = context.Render<MetricBreakdown>(parameters => parameters
            .Add(b => b.Rows, [new MetricRow("claude-haiku-4-5", 1_000L, new MoneyAmount(0.0043m, "USD"))])
            .Add(b => b.CostDecimals, 4));

        Assert.Contains("0.0043 USD", table.Find("tbody").TextContent, StringComparison.Ordinal);
    }
}
