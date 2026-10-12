namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A sparkline drawn against an axis: a series that leaves out the buckets it has no
/// reading for — a median over nothing — keeps each point over its own bucket rather than
/// pushing the readings it has together (entry 19, the Batch A review).
/// </summary>
public sealed class MetricSparklineAxisTests
{
    [Fact]
    public void Without_an_axis_the_points_are_spread_evenly()
    {
        using var context = new BunitContext();

        var line = context.Render<MetricSparkline>(parameters => parameters
                .Add(s => s.Points, [new MetricPoint("W1", 1m), new MetricPoint("W4", 1m)]))
            .Find("polyline").GetAttribute("points");

        Assert.Equal("0,0 100,0", line);
    }

    [Fact]
    public void Against_an_axis_each_point_sits_over_its_own_bucket()
    {
        using var context = new BunitContext();

        var line = context.Render<MetricSparkline>(parameters => parameters
                .Add(s => s.Points, [new MetricPoint("W2", 1m), new MetricPoint("W4", 1m), new MetricPoint("W5", 1m)])
                .Add(s => s.Axis, ["W1", "W2", "W3", "W4", "W5"]))
            .Find("polyline").GetAttribute("points");

        Assert.Equal("25,0 75,0 100,0", line);
    }

    [Fact]
    public void A_point_the_axis_does_not_name_is_left_out()
    {
        using var context = new BunitContext();

        var line = context.Render<MetricSparkline>(parameters => parameters
                .Add(s => s.Points, [new MetricPoint("W1", 1m), new MetricPoint("W9", 1m), new MetricPoint("W3", 1m)])
                .Add(s => s.Axis, ["W1", "W2", "W3"]))
            .Find("polyline").GetAttribute("points");

        Assert.Equal("0,0 100,0", line);
    }
}
