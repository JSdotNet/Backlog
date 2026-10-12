using Backlog.Modules.Dashboard.Abstractions;

namespace Backlog.Modules.Dashboard.UnitTests;

/// <summary>
/// The Window toggle's four periods (AC2): each maps to whole weeks, and the window it
/// draws is day-aligned at its start and ends at the moment asked about.
/// </summary>
public sealed class DashboardScopeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 15, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(DashboardPeriod.FourWeeks, 4)]
    [InlineData(DashboardPeriod.EightWeeks, 8)]
    [InlineData(DashboardPeriod.TwelveWeeks, 12)]
    [InlineData(DashboardPeriod.TwentySixWeeks, 26)]
    public void Each_period_is_a_whole_number_of_weeks(DashboardPeriod period, int weeks)
    {
        Assert.Equal(weeks, new DashboardScope(Period: period).Weeks);
    }

    [Theory]
    [InlineData(DashboardPeriod.FourWeeks, "2026-09-14")]
    [InlineData(DashboardPeriod.EightWeeks, "2026-08-17")]
    [InlineData(DashboardPeriod.TwelveWeeks, "2026-07-20")]
    [InlineData(DashboardPeriod.TwentySixWeeks, "2026-04-13")]
    public void Each_window_opens_at_midnight_that_many_weeks_back_and_ends_now(DashboardPeriod period, string opens)
    {
        var (from, to) = new DashboardScope(Period: period).Window(Now);

        Assert.Equal(new DateTimeOffset(DateTime.Parse(opens, System.Globalization.CultureInfo.InvariantCulture), TimeSpan.Zero), from);
        Assert.Equal(Now, to);
    }

    /// <summary>The widest period is what every source reads back to once, so the
    /// toggle moves without a read of its own.</summary>
    [Fact]
    public void The_horizon_reaches_back_as_far_as_the_widest_period()
    {
        Assert.Equal(TimeSpan.FromDays(7 * 26), DashboardScope.Horizon);
    }

    [Fact]
    public void The_dashboard_still_opens_on_twelve_weeks()
    {
        Assert.Equal(DashboardPeriod.TwelveWeeks, DashboardScope.Default.Period);
    }
}
