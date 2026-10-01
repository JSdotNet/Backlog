using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.Services;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// The reader's paces: the typed one, and three measured from finished effort over
/// the last two, four and eight weeks — per calendar day, today included, because
/// the roadmap draws its windows in calendar days.
/// </summary>
public class PlanningPaceTests
{
    private static readonly DateOnly Today = new(2026, 9, 25);

    /// <summary>Midday on <see cref="Today"/>, in UTC, which is also the clock's
    /// local zone — so "today" is the same date on every machine.</summary>
    private static readonly DateTimeOffset Noon = new(Today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

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
    [InlineData(PaceSource.Manual, 7)] // no stretch chosen reads as the last two weeks
    [InlineData(PaceSource.LastTwoWeeks, 7)]
    [InlineData(PaceSource.LastFourWeeks, 3.5)]
    [InlineData(PaceSource.LastEightWeeks, 1.75)]
    public void ThePaceInUseIsTheOneChosen(PaceSource source, double expected)
    {
        var paces = PlanningPace.Paces(3m, source, [new(Today, 14)], Today);

        Assert.Equal((decimal)expected, paces.InUse);
        Assert.False(paces.FellBack);
    }

    [Theory]
    [InlineData(PaceSource.Manual)]
    [InlineData(PaceSource.LastTwoWeeks)]
    [InlineData(PaceSource.LastEightWeeks)]
    public void WithNoStretchMeasuredTheTypedPaceIsUsed(PaceSource source)
    {
        var paces = PlanningPace.Paces(3m, source, [], Today);

        Assert.Equal(PaceSource.Manual, paces.InEffect);
        Assert.Equal(3m, paces.InUse);
        Assert.True(paces.FellBack);
    }

    [Fact]
    public void AChosenStretchThatMeasuredNothingGivesWayToOneThatDid()
    {
        // Twenty days ago: outside the last two weeks, inside the last four and eight.
        var paces = PlanningPace.Paces(3m, PaceSource.LastTwoWeeks, [new(Today.AddDays(-20), 28)], Today);

        Assert.Equal(PaceSource.LastFourWeeks, paces.InEffect);
        Assert.Equal(7m, paces.InUse);
        Assert.False(paces.FellBack);
    }

    [Fact]
    public async Task TheImportersPaceIsTheOneInUseAsOfTheClock()
    {
        var settings = new Settings(3m, PaceSource.LastTwoWeeks);
        var finished = new Finished([new(Today, 14), new(Today.AddDays(-60), 100)]);
        var pace = new PlanningPace(settings, finished, new FakeTimeProvider(Noon));

        Assert.Equal(7m, await pace.GetStoryPointsPerWeekAsync([], TestContext.Current.CancellationToken)); // 14 over 2 weeks

        // One read covers the longest stretch, so nothing older is asked for.
        Assert.Equal(Today.AddDays(-55), finished.AskedSince);
    }

    [Fact]
    public void TheViewsWritesGoToTheSettings()
    {
        var settings = new Settings(1m, PaceSource.Manual);
        var pace = new PlanningPace(settings, new Finished([]), new FakeTimeProvider(Noon));

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
        var pace = new PlanningPace(new Settings(3m, PaceSource.Manual), finished, new FakeTimeProvider(Noon));

        var backlog = await pace.ReadAsync("backlog", TestContext.Current.CancellationToken);
        var site = await pace.ReadAsync("SITE", TestContext.Current.CancellationToken);
        var global = await pace.ReadAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(10m, backlog.LastTwoWeeks); // 20 over 2 weeks
        Assert.Equal(17m, site.LastTwoWeeks);    // 34 over 2 weeks, whatever the case asked in
        Assert.Equal(26m, global.LastTwoWeeks);  // 52 over 2 weeks: each task once
    }

    [Fact]
    public async Task ARepositoryReadsTheOneTypedPaceNotAnOldOneOfItsOwn()
    {
        var settings = new Settings(3m, PaceSource.Manual);
        settings.Own["backlog"] = (9m, PaceSource.Manual); // kept by an earlier version
        var pace = new PlanningPace(settings, new Finished([], "backlog"), new FakeTimeProvider(Noon));

        var paces = await pace.ReadAsync("backlog", TestContext.Current.CancellationToken);

        Assert.Equal(3m, paces.Manual);
        Assert.Equal(3m, await pace.GetStoryPointsPerWeekAsync(["backlog"], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ARepositoryWithNoChoiceOfItsOwnReadsTheGlobalTypedPaceAndChoice()
    {
        var settings = new Settings(3m, PaceSource.LastTwoWeeks);
        var finished = new Finished([new(Today, 28) { RepositoryAliases = ["backlog"] }], "backlog");
        var pace = new PlanningPace(settings, finished, new FakeTimeProvider(Noon));

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
        var pace = new PlanningPace(settings, finished, new FakeTimeProvider(Noon));

        var paces = await pace.ReadAsync("gone", TestContext.Current.CancellationToken);

        // The global paces: measured over all finished work, "gone"'s included.
        Assert.Equal(3m, paces.Manual);
        Assert.Equal(14m, await pace.GetStoryPointsPerWeekAsync(["gone"], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SeveralRepositoriesPlaceAtTheLowestPaceInUseAmongThem()
    {
        var settings = new Settings(7m, PaceSource.Manual);
        var finished = new Finished(
            [
                new(Today, 28) { RepositoryAliases = ["backlog"] }, // 14 a week
                new(Today, 6) { RepositoryAliases = ["site"] }      // 3 a week
            ],
            "backlog", "site", "docs");
        var pace = new PlanningPace(settings, finished, new FakeTimeProvider(Noon));

        Assert.Equal(3m, await pace.GetStoryPointsPerWeekAsync(["backlog", "site"], TestContext.Current.CancellationToken));
        Assert.Equal(7m, await pace.GetStoryPointsPerWeekAsync(["backlog", "docs"], TestContext.Current.CancellationToken)); // docs measured nothing: the typed 7
        Assert.Equal(14m, await pace.GetStoryPointsPerWeekAsync(["backlog"], TestContext.Current.CancellationToken));
        Assert.Equal(14m, await pace.GetStoryPointsPerWeekAsync(["unknown", "backlog"], TestContext.Current.CancellationToken)); // not configured: the global 17
    }

    [Fact]
    public async Task NoRepositoriesPlaceAtTheGlobalPace()
    {
        var settings = new Settings(5m, PaceSource.Manual);
        settings.Own["backlog"] = (1m, PaceSource.Manual);
        var pace = new PlanningPace(settings, new Finished([], "backlog"), new FakeTimeProvider(Noon));

        Assert.Equal(5m, await pace.GetStoryPointsPerWeekAsync([], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EveryPaceIsCountedFromOneReadOfTheBacklog()
    {
        var finished = new Finished([], "backlog", "site", "docs");
        var pace = new PlanningPace(new Settings(5m, PaceSource.Manual), finished, new FakeTimeProvider(Noon));

        var paces = await pace.ReadPacesInUseAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, finished.Reads);
        Assert.Equal(["backlog", "docs", "site"], paces.ByRepository.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AChoiceForARepositoryGoesToThatRepositoryAndTheTypedPaceIsGlobal()
    {
        var settings = new Settings(1m, PaceSource.Manual);
        var pace = new PlanningPace(settings, new Finished([], "backlog"), new FakeTimeProvider(Noon));

        Assert.Null(pace.Choose(PaceSource.LastEightWeeks, "backlog"));
        Assert.Null(pace.SetManual("2.5"));

        Assert.Equal(PaceSource.LastEightWeeks, settings.Source("backlog"));
        Assert.Equal(PaceSource.Manual, settings.Source());
        Assert.Equal(2.5m, settings.Manual());
    }

    // --- A pace set by hand, per lane ----------------------------------------------

    [Fact]
    public void APaceSetByHandIsInEffectWhateverWasMeasured()
    {
        var paces = PlanningPace.Paces(3m, PaceSource.Set, [new(Today, 14)], Today, own: 5m);

        Assert.Equal(PaceSource.Set, paces.InEffect);
        Assert.Equal(5m, paces.InUse);
        Assert.Equal(5m, paces.Of(PaceSource.Set));
        Assert.False(paces.FellBack);
        Assert.Equal(7m, paces.LastTwoWeeks); // still measured, for the hint
    }

    [Fact]
    public void APaceSetByHandIsInEffectWhenNothingWasMeasuredToo()
    {
        var paces = PlanningPace.Paces(3m, PaceSource.Set, [], Today, own: 5m);

        Assert.Equal(5m, paces.InUse);
        Assert.False(paces.FellBack);
    }

    [Fact]
    public void OwnIsTheTypedPaceUntilAScopeSetItsOwn()
    {
        var paces = PlanningPace.Paces(3m, PaceSource.Manual, [], Today);

        Assert.Equal(3m, paces.Own);
        Assert.Equal(3m, paces.Of(PaceSource.Set));
        Assert.Equal(3m, new PlanningPacesDto(3m, null, null, null, PaceSource.Set).InUse);
    }

    [Fact]
    public void NoChoiceStillReadsAsTheLastTwoWeeksEvenWithAnOwnPace()
    {
        var paces = PlanningPace.Paces(3m, PaceSource.Manual, [new(Today, 14)], Today, own: 5m);

        Assert.Equal(PaceSource.LastTwoWeeks, paces.InEffect);
        Assert.Equal(7m, paces.InUse);
    }

    [Fact]
    public async Task SettingALanesOwnPaceChoosesItForThatLaneAlone()
    {
        var settings = new Settings(3m, PaceSource.Manual);
        var finished = new Finished(
            [new(Today, 28) { RepositoryAliases = ["backlog"] }, new(Today, 28) { RepositoryAliases = ["site"] }],
            "backlog", "site");
        var pace = new PlanningPace(settings, finished, new FixedClock(Today));

        Assert.Null(pace.SetOwn(2.25m, "backlog"));

        var backlog = await pace.ReadAsync("backlog", TestContext.Current.CancellationToken);
        var site = await pace.ReadAsync("site", TestContext.Current.CancellationToken);
        var global = await pace.ReadAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(PaceSource.Set, backlog.InEffect);
        Assert.Equal(2.25m, backlog.Own);
        Assert.Equal(2.25m, backlog.InUse);
        Assert.Equal(3m, backlog.Manual); // the heading's typed pace is untouched

        Assert.Equal(14m, site.InUse);   // measured, as before
        Assert.Equal(PaceSource.LastTwoWeeks, site.InEffect);
        Assert.Equal(28m, global.InUse); // 56 over two weeks, each task once
        Assert.Equal(3m, settings.Manual());
    }

    [Fact]
    public async Task TheGlobalScopesOwnPaceIsTheHeadingsTypedPace()
    {
        var settings = new Settings(3m, PaceSource.Manual);
        var pace = new PlanningPace(settings, new Finished([]), new FixedClock(Today));

        Assert.Null(pace.SetOwn(4.5m));

        var global = await pace.ReadAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(4.5m, settings.Manual());
        Assert.Equal(PaceSource.Set, settings.Source());
        Assert.Equal(4.5m, global.Own);
        Assert.Equal(4.5m, global.Manual);
        Assert.Equal(4.5m, global.InUse);
    }

    [Fact]
    public async Task AnItemInTwoLanesStillUsesTheLowestPaceEvenWhenOneWasSetByHand()
    {
        var settings = new Settings(7m, PaceSource.Manual);
        var finished = new Finished([new(Today, 28) { RepositoryAliases = ["backlog"] }], "backlog", "site");
        var pace = new PlanningPace(settings, finished, new FixedClock(Today));

        Assert.Null(pace.SetOwn(2m, "site"));
        Assert.Equal(2m, await pace.GetStoryPointsPerWeekAsync(["backlog", "site"], TestContext.Current.CancellationToken));

        Assert.Null(pace.SetOwn(30m, "site"));
        Assert.Equal(14m, await pace.GetStoryPointsPerWeekAsync(["backlog", "site"], TestContext.Current.CancellationToken));
    }

    [Fact]
    public void SettingALanesOwnPaceIsOneChange()
    {
        var settings = new Settings(3m, PaceSource.Manual);
        var raised = 0;
        settings.Changed += () => raised++;
        var pace = new PlanningPace(settings, new Finished([], "backlog"), new FixedClock(Today));

        Assert.Null(pace.SetOwn(2.25m, "backlog"));

        Assert.Equal(1, raised);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AFigureThatIsNotAPaceIsRefusedAndChoosesNothing(int refused)
    {
        var settings = new Settings(3m, PaceSource.Manual);
        var pace = new PlanningPace(settings, new Finished([], "backlog"), new FixedClock(Today));

        Assert.NotNull(pace.SetOwn(refused, "backlog"));

        Assert.Equal(PaceSource.Manual, settings.Source("backlog"));
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
            if (value <= 0) return "Give a pace above zero.";
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

        public string? SetOwn(string? typed, string? repository = null)
        {
            var value = decimal.Parse(typed!, System.Globalization.CultureInfo.InvariantCulture);
            if (value <= 0) return "Give a pace above zero.";
            if (repository is null) (_manual, _source) = (value, PaceSource.Set);
            else Own[repository] = (value, PaceSource.Set);
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
}
