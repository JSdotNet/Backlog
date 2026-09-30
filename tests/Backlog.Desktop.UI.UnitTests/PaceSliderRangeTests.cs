using Backlog.Modules.Roadmap.UI;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The line a lane's pace slider runs along: from the pace the lane measured over the
/// last two weeks to the pace the reader typed, whichever is lower first, stepped a
/// quarter point at a time, and always long enough to hold what is in use.
/// </summary>
public sealed class PaceSliderRangeTests
{
    [Theory]
    [InlineData(3, 8, 7, 3, 8)]     // measured slower than typed
    [InlineData(12, 8, 10, 8, 12)]  // measured faster than typed
    [InlineData(2.5, 7.75, 5, 2.5, 7.75)]
    public void It_runs_between_the_two_weeks_and_the_typed_pace_lowest_first(
        double twoWeek, double typed, double current, double min, double max)
    {
        var range = PaceSliderRange.Of((decimal)twoWeek, (decimal)typed, (decimal)current);

        Assert.Equal((decimal)min, range.Min);
        Assert.Equal((decimal)max, range.Max);
        Assert.Equal(0.25m, range.Step);
    }

    [Fact]
    public void Nothing_measured_runs_from_half_the_typed_pace_to_double()
    {
        var range = PaceSliderRange.Of(null, 8m, 8m);

        Assert.Equal(4m, range.Min);
        Assert.Equal(16m, range.Max);
    }

    [Fact]
    public void A_measured_pace_equal_to_the_typed_one_leaves_no_line_so_it_runs_half_to_double()
    {
        var range = PaceSliderRange.Of(8m, 8m, 8m);

        Assert.Equal(4m, range.Min);
        Assert.Equal(16m, range.Max);
    }

    [Fact]
    public void An_end_not_on_a_step_is_moved_outward_onto_one_so_the_thumb_can_rest_on_it()
    {
        var range = PaceSliderRange.Of(3.1m, 7.9m, 5m);

        Assert.Equal(3m, range.Min);
        Assert.Equal(8m, range.Max);
    }

    [Theory]
    [InlineData(20, 3, 20)]   // in use is above the line
    [InlineData(1, 1, 8)]     // in use is below it
    public void The_pace_in_use_is_always_on_the_line(double current, double min, double max)
    {
        var range = PaceSliderRange.Of(3m, 8m, (decimal)current);

        Assert.Equal((decimal)min, range.Min);
        Assert.Equal((decimal)max, range.Max);
    }

    [Fact]
    public void A_pace_in_use_between_steps_extends_the_line_to_the_steps_around_it()
    {
        var range = PaceSliderRange.Of(3m, 8m, 9.3333m);

        Assert.Equal(9.5m, range.Max);
    }

    [Fact]
    public void A_tiny_pace_never_reaches_zero_which_has_no_length_to_draw()
    {
        var range = PaceSliderRange.Of(null, 0.3m, 0.3m);

        Assert.Equal(0.25m, range.Min);
        Assert.Equal(0.75m, range.Max);
        Assert.True(range.Max > range.Min);
    }

    [Theory]
    [InlineData(0.125, 0.125)]   // the 8-week stretch of a quiet lane
    [InlineData(0.0625, 0.0625)]
    public void A_pace_in_use_below_the_floor_is_still_on_the_line(double current, double min)
    {
        var range = PaceSliderRange.Of(3m, 8m, (decimal)current);

        Assert.Equal((decimal)min, range.Min);
        Assert.InRange((decimal)current, range.Min, range.Max);
    }

    [Fact]
    public void A_pace_at_the_floor_or_above_it_keeps_the_floor()
    {
        var range = PaceSliderRange.Of(null, 0.3m, 0.25m);

        Assert.Equal(0.25m, range.Min);
    }

    [Fact]
    public void A_line_always_has_length()
    {
        var range = PaceSliderRange.Of(0.25m, 0.25m, 0.25m);

        Assert.True(range.Max > range.Min);
    }
}
