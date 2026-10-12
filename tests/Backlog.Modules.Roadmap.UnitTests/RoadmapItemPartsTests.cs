using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// A roadmap item filed under several repositories is laid out one part per repository:
/// each part counts the full points of the tasks filed there, at that repository's own
/// pace, and starts after whatever it waits on (ADR 0013, rulings 4 and 5 as amended on
/// 2026-10-07; local ADR 0019 for the counting). Every date below is on the default
/// working week, Monday to Friday.
/// </summary>
public class RoadmapItemPartsTests
{
    /// <summary>Monday 12 October 2026 — the day every scenario starts on.</summary>
    private static readonly DateOnly Today = new(2026, 10, 12);

    private static readonly DateOnly Friday16 = new(2026, 10, 16);
    private static readonly DateOnly Monday19 = new(2026, 10, 19);
    private static readonly DateOnly Friday23 = new(2026, 10, 23);
    private static readonly DateOnly Friday30 = new(2026, 10, 30);

    /// <summary><c>app</c> gets through 8 points a week, <c>site</c> 4, anything else 7.
    /// A task names its repository by the <c>owner/name</c> id the backlog stores.</summary>
    private static readonly PacesInUseDto Paces = new(
        7m,
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["app"] = 8m, ["site"] = 4m, ["docs"] = 4m })
    {
        AliasesById = new Dictionary<string, string>
        {
            ["JSdotNet/App"] = "app",
            ["JSdotNet/Site"] = "site",
            ["JSdotNet/Docs"] = "docs"
        }
    };

    private static RoadmapItemDto Item(
        ImportPlacement? placement = ImportPlacement.Effort,
        bool endPinned = false,
        DateOnly? start = null,
        DateOnly? end = null,
        params string[] repositories) =>
        new(
            Guid.NewGuid(),
            "Plan",
            start ?? new DateOnly(2026, 9, 28),
            end ?? new DateOnly(2026, 10, 2),
            PlanningPriority.Medium,
            repositories,
            null,
            null,
            [],
            PlacedByImport: placement,
            EndPinned: endPinned);

    private static RoadmapGatheredLink Work(
        string key,
        int? effort,
        string[] repositories,
        string[]? after = null,
        RoadmapProgress progress = RoadmapProgress.Ready,
        DateOnly? started = null,
        DateOnly? completed = null) =>
        new(key, key, effort, RollupOrigin.Tag, progress, after ?? [], repositories, started, completed);

    private static RoadmapItemRollupDto Gathers(params RoadmapGatheredLink[] tasks) => new(tasks, []);

    private static RoadmapItemLayout Lay(RoadmapItemDto item, RoadmapItemRollupDto? rollup, DateOnly? floor = null) =>
        RoadmapItemParts.Of(item, rollup, Paces, Today, floor: floor);

    private static RoadmapItemPart In(RoadmapItemLayout layout, string? band, int phase = 0) =>
        Assert.Single(layout.Parts, part => string.Equals(part.Band, band, StringComparison.OrdinalIgnoreCase) && part.Phase == phase);

    // ---- Part effort and part pace ----------------------------------------------------

    /// <summary>AC1: an 8-point task filed under <c>app</c> (8 a week) and <c>site</c>
    /// (4 a week) counts in full in both parts, each at its own pace.</summary>
    [Fact]
    public void ATaskFiledInTwoRepositories_CountsInFullInBothParts_EachAtItsOwnPace()
    {
        var item = Item(repositories: ["app", "site"]);

        var layout = Lay(item, Gathers(Work("t1", 8, ["JSdotNet/App", "JSdotNet/Site"])));

        Assert.Equal((Today, Friday16), (In(layout, "app").Start, In(layout, "app").End));
        Assert.Equal((Today, Friday23), (In(layout, "site").Start, In(layout, "site").End));
        Assert.Equal((Today, Friday23), (layout.Start, layout.End)); // the envelope
    }

    [Fact]
    public void EachPartCountsOnlyTheTasksFiledInItsRepository()
    {
        var item = Item(repositories: ["app", "site"]);

        var layout = Lay(item, Gathers(
            Work("a", 8, ["JSdotNet/App"]),
            Work("b", 8, ["JSdotNet/App"]),
            Work("s", 4, ["site"])));

        Assert.Equal(Friday23, In(layout, "app").End);  // 16 points at 8 a week: two weeks
        Assert.Equal(Friday16, In(layout, "site").End); // 4 points at 4 a week: one week
        Assert.Equal(["a", "b"], In(layout, "app").Links.Select(link => link.Key));
    }

    /// <summary>A task filed under no repository, or under one none of the parts stands
    /// for, goes to the first part, so no work drops out of the item.</summary>
    [Fact]
    public void ATaskFiledNowhereOrOutsideTheParts_GoesToTheFirstPart()
    {
        var item = Item(repositories: ["app", "site"]);

        var layout = Lay(item, Gathers(
            Work("nowhere", 4, []),
            Work("elsewhere", 4, ["JSdotNet/Docs"]),
            Work("s", 4, ["site"])));

        Assert.Equal(["nowhere", "elsewhere"], In(layout, "app").Links.Select(link => link.Key));
        Assert.Equal(Friday16, In(layout, "app").End); // 8 points at 8 a week
    }

    /// <summary>A repository nobody configured has no band of its own: the work is drawn
    /// in the no-repository band and placed at the global pace.</summary>
    [Fact]
    public void AnAliasNobodyConfigured_IsPlacedAtTheGlobalPaceInTheNoRepositoryBand()
    {
        var item = Item(repositories: ["gone"]);

        var layout = Lay(item, Gathers(Work("t", 14, ["gone"])));

        var part = Assert.Single(layout.Parts);
        Assert.Null(part.Band);
        Assert.Equal(7m, part.Pace);
        Assert.Equal(Friday23, part.End); // 14 points at 7 a week: two weeks
    }

    // ---- Part start and waits -----------------------------------------------------------

    /// <summary>AC2: 4 points in <c>docs</c> and 4 in <c>site</c>, both at 4 a week, the
    /// <c>site</c> task waiting on the <c>docs</c> one.</summary>
    [Fact]
    public void APartWaitingOnAPart_StartsTheWorkedDayAfterItEnds()
    {
        var item = Item(repositories: ["docs", "site"]);

        var layout = Lay(item, Gathers(
            Work("d", 4, ["JSdotNet/Docs"]),
            Work("s", 4, ["JSdotNet/Site"], after: ["d"])));

        var docs = In(layout, "docs");
        var site = In(layout, "site", phase: 1);
        Assert.Equal((Today, Friday16), (docs.Start, docs.End));
        Assert.Equal((Monday19, Friday23), (site.Start, site.End));
        Assert.Equal([layout.Parts.ToList().IndexOf(docs)], site.WaitsOn);
        Assert.Equal((Today, Friday23), (layout.Start, layout.End)); // AC3: the stored window
    }

    /// <summary>The floor is the day after the item's latest predecessor ends; a start on
    /// a day not worked moves to the next worked one, and a floor in the past is today.</summary>
    [Theory]
    [InlineData(2026, 10, 19, 2026, 10, 19)] // Monday: as given
    [InlineData(2026, 10, 17, 2026, 10, 19)] // Saturday: the next worked day
    [InlineData(2026, 10, 1, 2026, 10, 12)]  // in the past: today
    public void APartNotBegunStartsOnTheLaterOfTodayAndTheFloor(int year, int month, int day, int y, int m, int d)
    {
        var item = Item(repositories: ["docs"]);

        var layout = Lay(item, Gathers(Work("d", 4, ["docs"])), floor: new DateOnly(year, month, day));

        Assert.Equal(new DateOnly(y, m, d), Assert.Single(layout.Parts).Start);
    }

    /// <summary>Work that hands over and back gets a part per phase, each after the part it
    /// waits on at its own pace — no part is given a share of the window by its size.</summary>
    [Fact]
    public void WorkHandingOverAndBack_IsAPartPerPhase_EachAfterTheOneItWaitsOn()
    {
        var item = Item(repositories: ["docs", "site"]);

        var layout = Lay(item, Gathers(
            Work("d1", 4, ["docs"]),
            Work("s", 4, ["site"], after: ["d1"]),
            Work("d2", 4, ["docs"], after: ["s"])));

        Assert.Equal(3, layout.Parts.Count);
        Assert.Equal((Today, Friday16), (In(layout, "docs").Start, In(layout, "docs").End));
        Assert.Equal((Monday19, Friday23), (In(layout, "site", 1).Start, In(layout, "site", 1).End));
        Assert.Equal((new DateOnly(2026, 10, 26), Friday30), (In(layout, "docs", 2).Start, In(layout, "docs", 2).End));
        Assert.True(layout.HandsOver);
    }

    [Fact]
    public void AWaitOnWorkThePlanDidNotGather_IsNotCounted()
    {
        var item = Item(repositories: ["docs", "site"]);

        var layout = Lay(item, Gathers(
            Work("d", 4, ["docs"]),
            Work("s", 4, ["site"], after: ["elsewhere"])));

        Assert.Equal(Today, In(layout, "site").Start);
        Assert.Empty(In(layout, "site").WaitsOn);
    }

    /// <summary>A circle of waits between parts is broken at the earliest part still
    /// waiting, the way the gathering breaks one between tasks.</summary>
    [Fact]
    public void ACircleOfWaits_IsReleasedAtTheEarliestWaiter()
    {
        var item = Item(repositories: ["docs", "site"]);

        var layout = Lay(item, Gathers(
            Work("d", 4, ["docs"], after: ["s"]),
            Work("s", 4, ["site"], after: ["d"])));

        Assert.Equal((Today, Friday16), (In(layout, "docs").Start, In(layout, "docs").End));
        Assert.Empty(In(layout, "docs").WaitsOn);
        Assert.Equal(Monday19, In(layout, "site", 1).Start);
    }

    /// <summary>Q4: in a hand-over, a task filed in two repositories belongs to the part of
    /// each, and its waits — and every wait on it — apply in both.</summary>
    [Fact]
    public void ATaskFiledInTwoRepositoriesInAHandOver_HasAPartInEach_AndWhatWaitsOnItWaitsOnBoth()
    {
        var item = Item(repositories: ["docs", "site", "app"]);

        var layout = Lay(item, Gathers(
            Work("d1", 4, ["docs"]),
            Work("m", 8, ["site", "JSdotNet/App"], after: ["d1"]),
            Work("d2", 4, ["docs"], after: ["m"])));

        var parts = layout.Parts.ToList();
        var site = In(layout, "site", 1);
        var app = In(layout, "app", 1);
        var last = In(layout, "docs", 2);

        Assert.Equal((Monday19, Friday30), (site.Start, site.End)); // 8 points at 4 a week
        Assert.Equal((Monday19, Friday23), (app.Start, app.End));   // 8 points at 8 a week
        Assert.Equal([parts.IndexOf(site), parts.IndexOf(app)], last.WaitsOn.Order());
        Assert.Equal(new DateOnly(2026, 11, 2), last.Start);        // after the later of the two
    }

    // ---- Edge parts ---------------------------------------------------------------------

    [Fact]
    public void APartWithNothingSized_TakesOneWorkingWeek()
    {
        var item = Item(repositories: ["docs"]);

        var layout = Lay(item, Gathers(Work("d", null, ["docs"]), Work("e", null, ["docs"])));

        Assert.Equal((Today, Friday16), (layout.Parts[0].Start, layout.Parts[0].End));
    }

    /// <summary>A repository the item names that holds none of its tasks still draws a part,
    /// over the item's window, so the item still shows it is filed there.</summary>
    [Fact]
    public void ANamedRepositoryHoldingNoTask_SpansTheItemsWindow()
    {
        var item = Item(repositories: ["docs", "site", "app"]);

        var layout = Lay(item, Gathers(Work("d", 4, ["docs"]), Work("s", 8, ["site"])));

        var app = In(layout, "app");
        Assert.True(app.Empty);
        Assert.Equal((Today, Friday23), (app.Start, app.End));
        Assert.Equal((Today, Friday23), (layout.Start, layout.End));
    }

    [Fact]
    public void AnItemWithNoGatheredWork_DrawsEveryPartOverItsStoredWindow()
    {
        var item = Item(repositories: ["docs", "site"]);

        foreach (var rollup in new[] { null, RoadmapItemRollupDto.Empty })
        {
            var layout = Lay(item, rollup);

            Assert.Equal(PartsPlacement.Stored, layout.Placement);
            Assert.Equal(["docs", "site"], layout.Parts.Select(part => part.Band));
            Assert.All(layout.Parts, part => Assert.Equal((item.Start, item.End), (part.Start, part.End)));
            Assert.Equal((item.Start, item.End), (layout.Start, layout.End));
        }
    }

    /// <summary>AC5 and AC6: a due date or a person placed the window, so every part draws
    /// over it — one part per repository, even when the work hands over.</summary>
    [Theory]
    [InlineData(ImportPlacement.DueDate)]
    [InlineData(null)]
    public void AnItemNotSizedByItsEffort_DrawsEveryPartOverItsStoredWindow(ImportPlacement? placement)
    {
        var item = Item(placement, repositories: ["docs", "site"]);

        var layout = Lay(item, Gathers(
            Work("d1", 4, ["docs"]),
            Work("s", 4, ["site"], after: ["d1"]),
            Work("d2", 4, ["docs"], after: ["s"])));

        Assert.Equal(PartsPlacement.Stored, layout.Placement);
        Assert.Equal(["docs", "site"], layout.Parts.Select(part => part.Band));
        Assert.Equal(["d1", "d2"], In(layout, "docs").Links.Select(link => link.Key));
        Assert.All(layout.Parts, part => Assert.Equal((item.Start, item.End), (part.Start, part.End)));
        Assert.False(layout.HandsOver);
    }

    /// <summary>A pace nobody could divide by places nothing: every part draws over the
    /// stored window, begun or not, rather than the work being counted at no pace.</summary>
    [Theory]
    [InlineData(RoadmapProgress.Ready)]
    [InlineData(RoadmapProgress.InProgress)]
    public void APartAtNoPace_LeavesEveryPartOverTheStoredWindow(RoadmapProgress progress)
    {
        var paces = Paces with { ByRepository = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["app"] = 8m, ["site"] = 0m } };
        var item = Item(repositories: ["app", "site"]);

        var layout = RoadmapItemParts.Of(item, Gathers(Work("a", 8, ["app"], progress: progress, started: Today), Work("s", 4, ["site"])), paces, Today);

        Assert.Equal(PartsPlacement.Stored, layout.Placement);
        Assert.Equal((item.Start, item.End), (layout.Start, layout.End));
    }

    [Fact]
    public void WithNoTodayToPlaceFrom_EveryPartDrawsOverTheStoredWindow()
    {
        var item = Item(repositories: ["docs", "site"]);

        var layout = RoadmapItemParts.Of(item, Gathers(Work("d", 4, ["docs"]), Work("s", 8, ["site"])), Paces, today: null);

        Assert.Equal(PartsPlacement.Stored, layout.Placement);
        Assert.All(layout.Parts, part => Assert.Equal((item.Start, item.End), (part.Start, part.End)));
    }

    // ---- Work in flight -----------------------------------------------------------------

    /// <summary>A begun part runs from the day its work began to a forecast of its open
    /// points — an unestimated task counted as one — at its own pace, from today.</summary>
    [Theory]
    [InlineData(ImportPlacement.Effort)]
    [InlineData(null)]
    public void ABegunPart_RunsFromWhenItsWorkBegan_ToAForecastOfItsOpenPoints(ImportPlacement? placement)
    {
        var item = Item(placement, repositories: ["docs"]);

        var layout = Lay(item, Gathers(
            Work("d1", 4, ["docs"], progress: RoadmapProgress.Done, started: new(2026, 10, 5), completed: new(2026, 10, 9)),
            Work("d2", 8, ["docs"], progress: RoadmapProgress.InProgress, started: new(2026, 10, 8)),
            Work("d3", null, ["docs"])));

        var part = Assert.Single(layout.Parts);
        Assert.Equal(PartsPlacement.FromWork, layout.Placement);
        Assert.True(part.Begun);
        Assert.Equal(Today, part.OpenFrom);
        // 9 open points (8, and 1 for the unestimated) at 4 a week: 2.25 weeks, the twelfth worked day.
        Assert.Equal((new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 27)), (part.Start, part.End));
        Assert.Equal(part.End, part.Forecast);
    }

    /// <summary>A start a person has just chosen — a shelf plan dropped on a later day,
    /// which the importer places — holds a begun part back to it, its open points forecast
    /// from there rather than from the day the work began.</summary>
    [Fact]
    public void AChosenStartAfterTheWorkBegan_DrawsTheBegunPartFromThatStart()
    {
        var item = Item(repositories: ["docs"]);

        var layout = RoadmapItemParts.Of(
            item,
            Gathers(Work("d1", 8, ["docs"], progress: RoadmapProgress.InProgress, started: new(2026, 10, 8))),
            Paces,
            Today,
            floor: Monday19,
            startsOn: Monday19);

        var part = Assert.Single(layout.Parts);
        Assert.True(part.Begun);
        Assert.Equal(Monday19, part.OpenFrom);
        Assert.Equal((Monday19, Friday30), (part.Start, part.End)); // 8 points at 4 a week
    }

    /// <summary>Without a chosen start the same part is drawn from the day its work began,
    /// a hand-placed item's later stored start notwithstanding (ADR 0013, 2026-10-07).</summary>
    [Fact]
    public void WithoutAChosenStart_AHandPlacedBegunPartIsStillDrawnFromWhenItsWorkBegan()
    {
        var item = Item(placement: null, start: Monday19, end: Friday30, repositories: ["docs"]);

        var layout = Lay(item, Gathers(Work("d1", 8, ["docs"], progress: RoadmapProgress.InProgress, started: new(2026, 10, 8))));

        Assert.Equal(new DateOnly(2026, 10, 8), Assert.Single(layout.Parts).Start);
    }

    /// <summary>In a hand-over, a part whose tasks are all done is drawn where that work
    /// ran, and the open part after it is placed from today at its own pace.</summary>
    [Fact]
    public void ADonePartIsDrawnWhereItRan_AndTheOpenPartAfterItIsPlacedFromToday()
    {
        var item = Item(repositories: ["docs", "site"]);

        var layout = Lay(item, Gathers(
            Work("d", 4, ["docs"], progress: RoadmapProgress.Done, started: new(2026, 10, 1), completed: new(2026, 10, 7)),
            Work("s", 4, ["site"], after: ["d"])));

        var docs = In(layout, "docs");
        var site = In(layout, "site", 1);
        Assert.True(docs.Ran);
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 7)), (docs.Start, docs.End));
        Assert.Equal((Today, Friday16), (site.Start, site.End));
        Assert.Equal((new DateOnly(2026, 10, 1), Friday16), (layout.Start, layout.End));
    }

    [Fact]
    public void AFinishedItem_DrawsEachPartWhereItsWorkRan()
    {
        var item = Item(repositories: ["docs", "site"]);
        var rollup = Gathers(
            Work("d", 4, ["docs"], progress: RoadmapProgress.Done, started: new(2026, 10, 1), completed: new(2026, 10, 7)),
            Work("s", 4, ["site"], after: ["d"], progress: RoadmapProgress.Done, started: new(2026, 10, 6), completed: new(2026, 10, 9)));

        foreach (DateOnly? today in new DateOnly?[] { Today, null })
        {
            var layout = RoadmapItemParts.Of(item, rollup, Paces, today);

            Assert.Equal(PartsPlacement.WhereItRan, layout.Placement);
            Assert.All(layout.Parts, part => Assert.True(part.Ran));
            Assert.Equal((new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 9)), (In(layout, "site", 1).Start, In(layout, "site", 1).End));
            Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 9)), (layout.Start, layout.End));
        }
    }

    // ---- A pinned end (Q2) ----------------------------------------------------------------

    /// <summary>In-flight work whose <c>app</c> part (8 a week) ends 16 October and whose
    /// <c>site</c> part (4 a week) ends 23 October.</summary>
    private static RoadmapItemRollupDto InFlight(int appPoints = 8) => Gathers(
        Work("a", appPoints, ["JSdotNet/App"], progress: RoadmapProgress.InProgress, started: Today),
        Work("s", 8, ["JSdotNet/Site"]));

    /// <summary>AC10: a pinned end moves only the latest-ending part; every other part keeps
    /// its own forecast, and the forecast is kept beside the pin.</summary>
    [Fact]
    public void APinnedEnd_MovesOnlyTheLatestEndingPart()
    {
        var item = Item(placement: null, endPinned: true, start: Today, end: Friday30, repositories: ["app", "site"]);

        var layout = Lay(item, InFlight());

        Assert.Equal(Friday16, In(layout, "app").End);
        Assert.Equal(Friday30, In(layout, "site").End);
        Assert.Equal(Friday23, In(layout, "site").Forecast);
        Assert.Equal((Today, Friday30), (layout.Start, layout.End));
    }

    [Fact]
    public void APinEarlierThanTheForecast_DrawsTheLatestPartToThePin()
    {
        var item = Item(placement: null, endPinned: true, start: Today, end: new DateOnly(2026, 10, 20), repositories: ["app", "site"]);

        var layout = Lay(item, InFlight());

        Assert.Equal(Friday16, In(layout, "app").End);
        Assert.Equal(new DateOnly(2026, 10, 20), In(layout, "site").End);
    }

    [Fact]
    public void APinNeverEndsAPartBeforeItsOpenWorkCanStart()
    {
        var item = Item(placement: null, endPinned: true, start: new DateOnly(2026, 10, 1), end: new DateOnly(2026, 10, 9), repositories: ["app", "site"]);

        var layout = Lay(item, InFlight());

        Assert.Equal(Today, In(layout, "site").End);
    }

    [Fact]
    public void PartsTiedForTheLatestEnd_AllEndOnThePin()
    {
        var item = Item(placement: null, endPinned: true, start: Today, end: Friday30, repositories: ["app", "site"]);

        var layout = Lay(item, InFlight(appPoints: 16));

        Assert.Equal(Friday30, In(layout, "app").End);
        Assert.Equal(Friday30, In(layout, "site").End);
    }
}
