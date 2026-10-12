namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A host that names the columns worth naming — the month's first day, today, its last —
/// rather than every nth one (board Cost, AC29). The table under the chart keeps every
/// bucket's own label.
/// </summary>
public sealed class MetricBarsAxisLabelTests
{
    private static readonly IReadOnlyList<MetricPoint> Days =
        [.. Enumerable.Range(1, 5).Select(day => new MetricPoint($"{day} Oct", day))];

    [Fact]
    public void The_host_chooses_which_columns_are_named_and_what_they_say()
    {
        using var context = new BunitContext();

        var bars = context.Render<MetricBars>(parameters => parameters
            .Add(b => b.Points, Days)
            .Add(b => b.AxisLabel, index => index switch { 0 => "1 Oct", 2 => "Today, 3 Oct", 4 => "5 Oct", _ => null }));

        Assert.Equal(
            ["1 Oct", "", "Today, 3 Oct", "", "5 Oct"],
            bars.FindAll(".metric-bars__column-label").Select(label => label.TextContent));
        Assert.Equal(
            ["1 Oct", "2 Oct", "3 Oct", "4 Oct", "5 Oct"],
            bars.FindAll("tbody th").Select(cell => cell.TextContent));
    }

    /// <summary>
    /// A named label is wider than a day's column - verify read "Today, 12 Oct" cut to
    /// "Toda…" in a 38 px column. It is laid over the column rather than clipped to it:
    /// the first hangs from the column's start, the last from its end, the rest centred.
    /// </summary>
    [Fact]
    public void A_named_label_hangs_over_its_column_rather_than_being_clipped_to_it()
    {
        using var context = new BunitContext();

        var bars = context.Render<MetricBars>(parameters => parameters
            .Add(b => b.Points, Days)
            .Add(b => b.AxisLabel, index => index switch { 0 => "1 Oct", 2 => "Today, 3 Oct", 4 => "5 Oct", _ => null }));

        var labels = bars.FindAll(".metric-bars__column-label");

        Assert.All(labels, label => Assert.Contains("metric-bars__column-label--named", label.ClassList));
        Assert.Equal("Today, 3 Oct", labels[2].QuerySelector(".metric-bars__axis-text")!.TextContent);
        Assert.Contains("metric-bars__column-label--start", labels[0].ClassList);
        Assert.Contains("metric-bars__column-label--end", labels[4].ClassList);
        Assert.Null(labels[1].QuerySelector(".metric-bars__axis-text"));
    }

    [Fact]
    public void Without_a_host_choice_every_nth_column_is_named_as_before()
    {
        using var context = new BunitContext();

        var bars = context.Render<MetricBars>(parameters => parameters
            .Add(b => b.Points, Days)
            .Add(b => b.MaxLabels, 3));

        Assert.Equal(
            ["1 Oct", "", "3 Oct", "", "5 Oct"],
            bars.FindAll(".metric-bars__column-label").Select(label => label.TextContent));
    }
}
