namespace Backlog.SharedKernel.UnitTests;

/// <summary>
/// A duration as an exact decimal. The cases sit on the hundredth's midpoints, where the
/// double <see cref="TimeSpan.TotalHours"/> lies a hair to one side and a .NET 11
/// <c>(decimal)</c> cast keeps that hair: the figures here are the duration's own.
/// </summary>
public sealed class ExactDurationTests
{
    [Theory]
    [InlineData(7 * 3600 + 18, "7.005")]
    [InlineData(54, "0.015")]
    [InlineData(18, "0.005")]
    [InlineData(26 * 60 + 42, "0.445")]
    public void Hours_are_the_ticks_divided_exactly(int seconds, string expected)
    {
        Assert.Equal(Parse(expected), ExactDuration.Hours(TimeSpan.FromSeconds(seconds)));
    }

    /// <summary>Banker's rounding on the duration itself: 7.005 is a midpoint and goes to
    /// the even 7.00, 0.015 goes to 0.02 — where the double gives 0.0149999… and 0.01.</summary>
    [Theory]
    [InlineData(7 * 3600 + 18, "7.00", "7.01")]
    [InlineData(7 * 3600 + 54, "7.02", "7.02")]
    [InlineData(54, "0.02", "0.02")]
    [InlineData(26 * 60 + 42, "0.44", "0.45")]
    public void A_midpoint_rounds_by_the_rule_and_not_by_the_binary_fraction(int seconds, string toEven, string awayFromZero)
    {
        var hours = ExactDuration.Hours(TimeSpan.FromSeconds(seconds));

        Assert.Equal(Parse(toEven), Math.Round(hours, 2));
        Assert.Equal(Parse(awayFromZero), Math.Round(hours, 2, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void Seconds_keep_the_fraction_of_a_second()
    {
        Assert.Equal(1.5m, ExactDuration.Seconds(TimeSpan.FromMilliseconds(1500)));
        Assert.Equal(0.0000001m, ExactDuration.Seconds(TimeSpan.FromTicks(1)));
    }

    [Fact]
    public void A_share_is_exact_and_an_empty_whole_has_none()
    {
        Assert.Equal(0.0005m, ExactDuration.Share(TimeSpan.FromSeconds(18), TimeSpan.FromHours(10)));
        Assert.Equal(0m, ExactDuration.Share(TimeSpan.FromHours(1), TimeSpan.Zero));
    }

    private static decimal Parse(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
