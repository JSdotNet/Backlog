using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.Services;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// The reader's paces: the typed one, and three measured from finished effort over
/// the last two, four and eight weeks — per calendar day, today included, because
/// the roadmap draws its windows in calendar days.
/// </summary>
public class PlanningPaceTests
{
    private static readonly DateOnly Today = new(2026, 9, 25);

    [Fact]
    public void EachStretchCountsItsOwnDaysTodayIncluded()
    {
        CompletedEffortDto[] finished =
        [
            new(Today, 7),              // in all three
            new(Today.AddDays(-13), 7), // the first day of the two weeks
            new(Today.AddDays(-14), 14), // just outside two weeks, inside four
            new(Today.AddDays(-27), 14), // the first day of the four weeks
            new(Today.AddDays(-55), 14), // the first day of the eight weeks
            new(Today.AddDays(-56), 99)  // outside every stretch
        ];

        var paces = PlanningPace.Paces(2m, PaceSource.Manual, finished, Today);

        Assert.Equal(7m, paces.LastTwoWeeks);      // 14 over 2 weeks
        Assert.Equal(10.5m, paces.LastFourWeeks);  // 42 over 4 weeks
        Assert.Equal(7m, paces.LastEightWeeks);    // 56 over 8 weeks
        Assert.Equal(2m, paces.Manual);
    }

    [Fact]
    public void WorkFinishedAfterTodayIsNotCounted()
    {
        var paces = PlanningPace.Paces(1m, PaceSource.Manual, [new(Today.AddDays(1), 14)], Today);

        Assert.Null(paces.LastTwoWeeks);
    }

    [Fact]
    public void AStretchThatFinishedNothingMeasuredNoPace()
    {
        var paces = PlanningPace.Paces(1m, PaceSource.Manual, [new(Today.AddDays(-20), 28)], Today);

        Assert.Null(paces.LastTwoWeeks);
        Assert.Equal(7m, paces.LastFourWeeks);
        Assert.Equal(3.5m, paces.LastEightWeeks);
    }

    [Fact]
    public void AMeasuredPaceIsKeptToFourDecimals()
    {
        var pace = PlanningPace.Measured([new(Today, 13)], Today, 3);

        Assert.Equal(4.3333m, pace);
    }

    [Theory]
    [InlineData(PaceSource.Manual, 3)]
    [InlineData(PaceSource.LastTwoWeeks, 7)]
    [InlineData(PaceSource.LastFourWeeks, 3.5)]
    [InlineData(PaceSource.LastEightWeeks, 1.75)]
    public void ThePaceInUseIsTheOneChosen(PaceSource source, double expected)
    {
        var paces = PlanningPace.Paces(3m, source, [new(Today, 14)], Today);

        Assert.Equal((decimal)expected, paces.InUse);
        Assert.False(paces.FellBack);
    }

    [Fact]
    public void AChosenPaceThatMeasuredNothingFallsBackToTheTypedOne()
    {
        var paces = PlanningPace.Paces(3m, PaceSource.LastTwoWeeks, [], Today);

        Assert.Equal(3m, paces.InUse);
        Assert.True(paces.FellBack);
    }

    [Fact]
    public async Task TheImportersPaceIsTheOneInUseAsOfTheClock()
    {
        var settings = new Settings(3m, PaceSource.LastTwoWeeks);
        var finished = new Finished([new(Today, 14), new(Today.AddDays(-60), 100)]);
        var pace = new PlanningPace(settings, finished, new FixedClock(Today));

        Assert.Equal(7m, await pace.GetStoryPointsPerWeekAsync()); // 14 over 2 weeks

        // One read covers the longest stretch, so nothing older is asked for.
        Assert.Equal(Today.AddDays(-55), finished.AskedSince);
    }

    [Fact]
    public void TheViewsWritesGoToTheSettings()
    {
        var settings = new Settings(1m, PaceSource.Manual);
        var pace = new PlanningPace(settings, new Finished([]), new FixedClock(Today));

        Assert.Null(pace.SetManual("2.5"));
        Assert.Null(pace.Choose(PaceSource.LastEightWeeks));

        Assert.Equal(2.5m, settings.Manual);
        Assert.Equal(PaceSource.LastEightWeeks, settings.Source);
    }

    private sealed class Settings(decimal manual, PaceSource source) : IPlanningVelocitySettings
    {
        public event Action? Changed;

        public decimal Manual { get; private set; } = manual;

        public PaceSource Source { get; private set; } = source;

        public string? SetManual(string? typed)
        {
            Manual = decimal.Parse(typed!, System.Globalization.CultureInfo.InvariantCulture);
            Changed?.Invoke();
            return null;
        }

        public string? Choose(PaceSource source)
        {
            Source = source;
            Changed?.Invoke();
            return null;
        }
    }

    private sealed class Finished(IReadOnlyList<CompletedEffortDto> finished) : IRoadmapCompletedWork
    {
        public DateOnly? AskedSince { get; private set; }

        public Task<IReadOnlyList<CompletedEffortDto>> CompletedSinceAsync(
            DateOnly since,
            CancellationToken cancellationToken = default)
        {
            AskedSince = since;
            return Task.FromResult<IReadOnlyList<CompletedEffortDto>>(
                [.. finished.Where(entry => entry.CompletedOn >= since)]);
        }
    }

    private sealed class FixedClock(DateOnly today) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() =>
            new(today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
    }
}
