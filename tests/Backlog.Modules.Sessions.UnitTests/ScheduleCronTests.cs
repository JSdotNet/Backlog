using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Modules.Sessions.UnitTests;

/// <summary>
/// The five-field cron a schedule reports, read for its next due time — what Missed is
/// measured from (local ADR 0051 §2).
/// </summary>
public sealed class ScheduleCronTests
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset? Next(string cron, DateTimeOffset after, TimeZoneInfo? zone = null) =>
        ScheduleCron.TryParse(cron)!.NextAfter(after, zone ?? TimeZoneInfo.Utc);

    [Theory]
    [InlineData("0 * * * *", "2026-11-09T13:00:00+00:00")]
    [InlineData("*/15 * * * *", "2026-11-09T12:15:00+00:00")]
    [InlineData("30 5 * * *", "2026-11-10T05:30:00+00:00")]
    [InlineData("30 5 * * 3", "2026-11-11T05:30:00+00:00")]
    [InlineData("30 5 * * wed", "2026-11-11T05:30:00+00:00")]
    [InlineData("0 9 * * 1-5", "2026-11-10T09:00:00+00:00")]
    [InlineData("0 9 * * 0", "2026-11-15T09:00:00+00:00")]
    [InlineData("0 9 * * 7", "2026-11-15T09:00:00+00:00")]
    [InlineData("0 9 1 * *", "2026-12-01T09:00:00+00:00")]
    [InlineData("0 9 1 jan *", "2027-01-01T09:00:00+00:00")]
    [InlineData("0 6,18 * * *", "2026-11-09T18:00:00+00:00")]
    [InlineData("5/20 12 * * *", "2026-11-09T12:05:00+00:00")]
    [InlineData("0 0 29 2 *", "2028-02-29T00:00:00+00:00")]
    [InlineData("0 9 * * fri-sun", "2026-11-13T09:00:00+00:00")]
    [InlineData("0 9 * * sat-mon", "2026-11-14T09:00:00+00:00")]
    public void The_next_due_time_is_the_first_match_strictly_after(string cron, string expected)
    {
        Assert.Equal(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), Next(cron, Monday));
    }

    [Fact]
    public void A_due_time_equal_to_now_is_not_next()
    {
        Assert.Equal(Monday.AddDays(1), Next("0 12 * * *", Monday));
    }

    [Fact]
    public void Both_day_fields_restricted_match_either()
    {
        // The 10th or a Friday: Tuesday 10 November comes before Friday 13.
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero), Next("0 9 10 * 5", Monday));
    }

    [Theory]
    [InlineData("")]
    [InlineData("* * * *")]
    [InlineData("* * * * * *")]
    [InlineData("60 * * * *")]
    [InlineData("* 24 * * *")]
    [InlineData("* * 0 * *")]
    [InlineData("* * * 13 *")]
    [InlineData("* * * * 8")]
    [InlineData("*/0 * * * *")]
    [InlineData("5-1 * * * *")]
    [InlineData("* * * * sat-sun/2")]
    [InlineData("a * * * *")]
    [InlineData("1,,2 * * * *")]
    public void A_cron_this_reader_does_not_understand_is_none(string cron)
    {
        Assert.Null(ScheduleCron.TryParse(cron));
    }

    [Fact]
    public void A_cron_is_evaluated_in_the_zone_given()
    {
        var amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

        // 05:30 in Amsterdam in November is 04:30 UTC.
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 5, 30, 0, TimeSpan.FromHours(1)), Next("30 5 * * *", Monday, amsterdam));
    }

    [Fact]
    public void A_due_time_in_a_daylight_saving_gap_does_not_occur()
    {
        var amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
        var beforeTheGap = new DateTimeOffset(2027, 3, 27, 12, 0, 0, TimeSpan.FromHours(1));

        // 28 March 2027 skips 02:00-03:00 there: its 02:30 is not a time, so the next is 29 March's.
        Assert.Equal(new DateTimeOffset(2027, 3, 29, 2, 30, 0, TimeSpan.FromHours(2)), Next("30 2 * * *", beforeTheGap, amsterdam));
    }

    [Fact]
    public void An_ambiguous_due_time_is_its_first_occurrence()
    {
        var amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
        var beforeTheFold = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.FromHours(2));

        // 25 October 2026 has 02:30 twice; the first is still summer time.
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(2)), Next("30 2 * * *", beforeTheFold, amsterdam));
    }
}
