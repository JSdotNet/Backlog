using Backlog.Modules.Dashboard.UI.Parts;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// U15: a session's length with its minutes ("2 h 40 m"), and how long ago a sweep
/// checked a unit ("2 h ago", "yesterday", "N days ago"), against a fixed clock.
/// </summary>
public sealed class DashboardFormatTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 15, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, 40, "40 m")]
    [InlineData(2, 40, "2 h 40 m")]
    [InlineData(3, 0, "3 h")]
    [InlineData(23, 59, "23 h 59 m")]
    public void Under_a_day_a_length_keeps_its_minutes(int hours, int minutes, string text)
    {
        Assert.Equal(text, DashboardFormat.Length(new TimeSpan(hours, minutes, 20)));
    }

    [Fact]
    public void A_day_or_more_reads_as_the_coarse_duration()
    {
        Assert.Equal("1.5d", DashboardFormat.Length(TimeSpan.FromHours(36)));
    }

    [Fact]
    public void Under_a_minute_is_still_a_minute()
    {
        Assert.Equal("1 m", DashboardFormat.Length(TimeSpan.FromSeconds(20)));
    }

    [Theory]
    [InlineData("2026-10-12T15:29:40Z", "just now")]
    [InlineData("2026-10-12T15:05:00Z", "25 m ago")]
    [InlineData("2026-10-12T13:30:00Z", "2 h ago")]
    [InlineData("2026-10-12T00:10:00Z", "15 h ago")]
    [InlineData("2026-10-11T22:00:00Z", "yesterday")]
    [InlineData("2026-10-11T08:00:00Z", "yesterday")]
    [InlineData("2026-10-10T23:00:00Z", "2 days ago")]
    [InlineData("2026-10-01T12:00:00Z", "11 days ago")]
    public void A_check_reads_as_how_long_ago_it_was(string at, string text)
    {
        Assert.Equal(text, DashboardFormat.Ago(DateTimeOffset.Parse(at, System.Globalization.CultureInfo.InvariantCulture), Now));
    }

    [Fact]
    public void A_check_in_the_future_reads_as_just_now()
    {
        Assert.Equal("just now", DashboardFormat.Ago(Now.AddMinutes(3), Now));
    }
}
