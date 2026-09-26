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

        Assert.Equal(7m, await pace.GetStoryPointsPerWeekAsync([], TestContext.Current.CancellationToken)); // 14 over 2 weeks

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

        Assert.Equal(2.5m, settings.Manual());
        Assert.Equal(PaceSource.LastEightWeeks, settings.Source());
    }

    // --- Per repository ---------------------------------------------------------

    [Fact]
    public async Task ARepositorysMeasuredPacesCountOnlyItsOwnWork()
    {
        var finished = new Finished(
            [
                new(Today, 14) { RepositoryAliases = ["backlog"] },
                new(Today, 28) { RepositoryAliases = ["site"] },
                new(Today, 6) { RepositoryAliases = ["backlog", "site"] }, // counts in full toward both
                new(Today, 4) // unfiled: the global pace's alone
            ],
            "backlog", "site");
        var pace = new PlanningPace(new Settings(3m, PaceSource.Manual), finished, new FixedClock(Today));

        var backlog = await pace.ReadAsync("backlog", TestContext.Current.CancellationToken);
        var site = await pace.ReadAsync("SITE", TestContext.Current.CancellationToken);
        var global = await pace.ReadAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(10m, backlog.LastTwoWeeks); // 20 over 2 weeks
        Assert.Equal(17m, site.LastTwoWeeks);    // 34 over 2 weeks, whatever the case asked in
        Assert.Equal(26m, global.LastTwoWeeks);  // 52 over 2 weeks: each task once
    }

    [Fact]
    public async Task ARepositoryWithNoPaceOfItsOwnReadsTheGlobalTypedPaceAndChoice()
    {
        var settings = new Settings(3m, PaceSource.LastTwoWeeks);
        var finished = new Finished([new(Today, 28) { RepositoryAliases = ["backlog"] }], "backlog");
        var pace = new PlanningPace(settings, finished, new FixedClock(Today));

        var paces = await pace.ReadAsync("backlog", TestContext.Current.CancellationToken);

        Assert.Equal(3m, paces.Manual);
        Assert.Equal(PaceSource.LastTwoWeeks, paces.Source);
        Assert.Equal(14m, paces.InUse); // the global choice, over its own work
    }

    [Fact]
    public async Task ARepositoryNobodyConfiguredReadsAsTheGlobalPaces()
    {
        var settings = new Settings(3m, PaceSource.Manual);
        settings.Own["gone"] = (9m, PaceSource.Manual);
        var finished = new Finished([new(Today, 28) { RepositoryAliases = ["gone"] }], "backlog");
        var pace = new PlanningPace(settings, finished, new FixedClock(Today));

        var paces = await pace.ReadAsync("gone", TestContext.Current.CancellationToken);

        Assert.Equal(3m, paces.Manual);
        Assert.Equal(3m, await pace.GetStoryPointsPerWeekAsync(["gone"], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SeveralRepositoriesPlaceAtTheLowestPaceInUseAmongThem()
    {
        var settings = new Settings(7m, PaceSource.Manual);
        settings.Own["backlog"] = (14m, PaceSource.Manual);
        settings.Own["site"] = (3m, PaceSource.Manual);
        var finished = new Finished([], "backlog", "site", "docs");
        var pace = new PlanningPace(settings, finished, new FixedClock(Today));

        Assert.Equal(3m, await pace.GetStoryPointsPerWeekAsync(["backlog", "site"], TestContext.Current.CancellationToken));
        Assert.Equal(7m, await pace.GetStoryPointsPerWeekAsync(["backlog", "docs"], TestContext.Current.CancellationToken)); // docs reads the global 7
        Assert.Equal(14m, await pace.GetStoryPointsPerWeekAsync(["backlog"], TestContext.Current.CancellationToken));
        Assert.Equal(7m, await pace.GetStoryPointsPerWeekAsync(["unknown", "backlog"], TestContext.Current.CancellationToken)); // not configured: global
    }

    [Fact]
    public async Task NoRepositoriesPlaceAtTheGlobalPace()
    {
        var settings = new Settings(5m, PaceSource.Manual);
        settings.Own["backlog"] = (1m, PaceSource.Manual);
        var pace = new PlanningPace(settings, new Finished([], "backlog"), new FixedClock(Today));

        Assert.Equal(5m, await pace.GetStoryPointsPerWeekAsync([], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EveryPaceIsCountedFromOneReadOfTheBacklog()
    {
        var finished = new Finished([], "backlog", "site", "docs");
        var pace = new PlanningPace(new Settings(5m, PaceSource.Manual), finished, new FixedClock(Today));

        var paces = await pace.ReadPacesInUseAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, finished.Reads);
        Assert.Equal(["backlog", "docs", "site"], paces.ByRepository.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheViewsWritesForARepositoryGoToThatRepository()
    {
        var settings = new Settings(1m, PaceSource.Manual);
        var pace = new PlanningPace(settings, new Finished([], "backlog"), new FixedClock(Today));

        Assert.Null(pace.SetManual("2.5", "backlog"));
        Assert.Null(pace.Choose(PaceSource.LastEightWeeks, "backlog"));

        Assert.Equal(2.5m, settings.Manual("backlog"));
        Assert.Equal(PaceSource.LastEightWeeks, settings.Source("backlog"));
        Assert.Equal(1m, settings.Manual());
        Assert.Equal(PaceSource.Manual, settings.Source());
    }

    /// <summary>A settings file in memory, with the store's inheritance: a repository
    /// with no pace of its own answers the global one, and the first change for it
    /// copies the other half from what it answered.</summary>
    private sealed class Settings(decimal manual, PaceSource source) : IPlanningVelocitySettings
    {
        public event Action? Changed;

        public Dictionary<string, (decimal Manual, PaceSource Source)> Own { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        private decimal _manual = manual;
        private PaceSource _source = source;

        public decimal Manual(string? repository = null) =>
            repository is not null && Own.TryGetValue(repository, out var own) ? own.Manual : _manual;

        public PaceSource Source(string? repository = null) =>
            repository is not null && Own.TryGetValue(repository, out var own) ? own.Source : _source;

        public string? SetManual(string? typed, string? repository = null)
        {
            var value = decimal.Parse(typed!, System.Globalization.CultureInfo.InvariantCulture);
            if (repository is null) _manual = value;
            else Own[repository] = (value, Source(repository));
            Changed?.Invoke();
            return null;
        }

        public string? Choose(PaceSource source, string? repository = null)
        {
            if (repository is null) _source = source;
            else Own[repository] = (Manual(repository), source);
            Changed?.Invoke();
            return null;
        }
    }

    private sealed class Finished(IReadOnlyList<CompletedEffortDto> finished, params string[] repositories)
        : IRoadmapCompletedWork
    {
        public DateOnly? AskedSince { get; private set; }

        public int Reads { get; private set; }

        public IReadOnlyList<string> Repositories => repositories;

        public Task<IReadOnlyList<CompletedEffortDto>> CompletedSinceAsync(
            DateOnly since,
            CancellationToken cancellationToken = default)
        {
            AskedSince = since;
            Reads++;
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
