using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.Services;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Dashboard.UnitTests;

/// <summary>
/// The Hours worked part's figures: the source's days over the dashboard's window, laid
/// out per day and per calendar week, and the source's refusals turned into a reason
/// rather than a window of zeros (local ADR 0019, §7).
/// </summary>
public class HoursWorkedInsightsTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    /// <summary>Thursday 8 October 2026, noon in Amsterdam. Four weeks back is Thursday
    /// 10 September, whose week starts Monday 7 September — the first week the other
    /// weekly parts draw too.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.FromHours(2));

    private static readonly DateOnly Today = new(2026, 10, 8);

    private static readonly DashboardScope FourWeeks = new(Period: DashboardPeriod.FourWeeks);

    private static readonly DashboardScope TwelveWeeks = new(Period: DashboardPeriod.TwelveWeeks);

    [Fact]
    public async Task The_window_runs_from_the_monday_of_its_first_week_through_today()
    {
        var source = new StubSource();

        var result = await Insights(source).GetHoursWorkedAsync(FourWeeks, TestContext.Current.CancellationToken);

        Assert.Equal((new DateOnly(2026, 9, 7), Today), Assert.Single(source.Asked));
        Assert.True(result.HasValue);
        Assert.Equal(32, result.Value!.Days.Count);
        Assert.Equal(new DateOnly(2026, 9, 7), result.Value.Days[0].From);
        Assert.Equal(Today, result.Value.Days[^1].Through);
    }

    [Fact]
    public async Task Weeks_are_calendar_weeks_starting_monday_and_the_last_runs_through_today()
    {
        var result = await Insights(new StubSource()).GetHoursWorkedAsync(FourWeeks, TestContext.Current.CancellationToken);

        var weeks = result.Value!.Weeks;
        Assert.Equal(
            [new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 5)],
            weeks.Select(week => week.From));
        Assert.All(weeks, week => Assert.Equal(DayOfWeek.Monday, week.From.DayOfWeek));
        Assert.Equal(new DateOnly(2026, 9, 13), weeks[0].Through);
        Assert.Equal(Today, weeks[^1].Through);
    }

    [Fact]
    public async Task The_twelve_week_window_reaches_back_twelve_weeks()
    {
        var source = new StubSource();

        var result = await Insights(source).GetHoursWorkedAsync(TwelveWeeks, TestContext.Current.CancellationToken);

        Assert.Equal(new DateOnly(2026, 7, 13), Assert.Single(source.Asked).From);
        Assert.Equal(13, result.Value!.Weeks.Count);
    }

    [Fact]
    public async Task A_week_sums_its_days()
    {
        var source = new StubSource
        {
            Hours =
            {
                [new DateOnly(2026, 10, 5)] = (Hours(8), Hours(0.5), Hours(8.5)),
                [new DateOnly(2026, 10, 7)] = (Hours(6), Hours(2), Hours(8.5)),
                [Today] = (Hours(1.5), Hours(1.5), Hours(8.5))
            }
        };

        var result = await Insights(source).GetHoursWorkedAsync(FourWeeks, TestContext.Current.CancellationToken);

        var week = result.Value!.Weeks[^1];
        Assert.Equal(Hours(15.5), week.Inside);
        Assert.Equal(Hours(4), week.Outside);
        Assert.Equal(Hours(8.5 * 4), week.Planned);
        Assert.Equal(Hours(19.5), week.Actual);

        Assert.Equal(Hours(15.5), result.Value.Inside);
        Assert.Equal(Hours(4), result.Value.Outside);
    }

    [Fact]
    public async Task A_source_that_cannot_state_the_hours_is_unavailable_not_zero()
    {
        var result = await Insights(new StubSource { Unstated = true })
            .GetHoursWorkedAsync(FourWeeks, TestContext.Current.CancellationToken);

        Assert.False(result.HasValue);
        Assert.Contains("Sessions", result.Availability.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_read_is_unavailable_with_its_reason()
    {
        var result = await Insights(new StubSource { Failure = new IOException("transcript locked") })
            .GetHoursWorkedAsync(FourWeeks, TestContext.Current.CancellationToken);

        Assert.False(result.HasValue);
        Assert.Contains("transcript locked", result.Availability.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cancelled_read_is_not_turned_into_a_reason()
    {
        var source = new StubSource { Failure = new OperationCanceledException() };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Insights(source).GetHoursWorkedAsync(FourWeeks, TestContext.Current.CancellationToken));
    }

    private static TimeSpan Hours(double hours) => TimeSpan.FromHours(hours);

    private static HoursWorkedInsights Insights(IHoursWorkedSource source)
    {
        var time = new FakeTimeProvider(Now);
        time.SetLocalTimeZone(Amsterdam);
        return new HoursWorkedInsights(source, time);
    }

    private sealed class StubSource : IHoursWorkedSource
    {
        public List<(DateOnly From, DateOnly Through)> Asked { get; } = [];

        public Dictionary<DateOnly, (TimeSpan Inside, TimeSpan Outside, TimeSpan Planned)> Hours { get; } = [];

        public bool Unstated { get; init; }

        public Exception? Failure { get; init; }

        public Task<IReadOnlyList<HoursWorkedDay>?> ReadAsync(
            DateOnly from,
            DateOnly through,
            CancellationToken cancellationToken = default)
        {
            Asked.Add((from, through));

            if (Failure is not null) return Task.FromException<IReadOnlyList<HoursWorkedDay>?>(Failure);
            if (Unstated) return Task.FromResult<IReadOnlyList<HoursWorkedDay>?>(null);

            var days = Enumerable.Range(0, through.DayNumber - from.DayNumber + 1)
                .Select(from.AddDays)
                .Select(date => Hours.TryGetValue(date, out var hours)
                    ? new HoursWorkedDay(date, hours.Inside, hours.Outside, hours.Planned)
                    : new HoursWorkedDay(date, TimeSpan.Zero, TimeSpan.Zero, date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? TimeSpan.Zero : TimeSpan.FromHours(8.5)))
                .ToList();

            return Task.FromResult<IReadOnlyList<HoursWorkedDay>?>(days);
        }
    }
}
