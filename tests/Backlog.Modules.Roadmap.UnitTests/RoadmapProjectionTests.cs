using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// The keep-up projection: every item the import sized by its effort, whose work is not
/// finished, is laid out again from today — part by part, from the effort still open, after
/// whatever it waits on as that now stands — and its window becomes the parts' envelope (ADR
/// 0013, ruling 5 as amended on 2026-09-27 and 2026-10-07). Everything else is handed back
/// as stored, and still holds back what waits on it. Every date below is on the default
/// working week, Monday to Friday.
/// </summary>
public class RoadmapProjectionTests
{
    /// <summary>Monday 12 October 2026.</summary>
    private static readonly DateOnly Today = new(2026, 10, 12);

    private static readonly DateOnly Friday16 = new(2026, 10, 16);
    private static readonly DateOnly Monday19 = new(2026, 10, 19);
    private static readonly DateOnly Friday23 = new(2026, 10, 23);

    /// <summary><c>app</c> gets through 8 points a week, <c>site</c> 4, anything else 7.</summary>
    private static readonly PacesInUseDto Paces = new(
        7m,
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["app"] = 8m, ["site"] = 4m })
    {
        AliasesById = new Dictionary<string, string> { ["JSdotNet/App"] = "app", ["JSdotNet/Site"] = "site" }
    };

    private static RoadmapItemDto Item(
        string title,
        ImportPlacement? placement = ImportPlacement.Effort,
        DateOnly? start = null,
        DateOnly? end = null,
        Guid[]? after = null,
        bool endPinned = false,
        params string[] repositories) =>
        new(
            Guid.NewGuid(),
            title,
            start ?? new DateOnly(2026, 9, 28),
            end ?? new DateOnly(2026, 10, 2),
            PlanningPriority.Medium,
            repositories,
            null,
            null,
            after ?? [],
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

    private static IReadOnlyList<RoadmapItemDto> Project(
        IReadOnlyList<RoadmapItemDto> items,
        Dictionary<Guid, RoadmapItemRollupDto> rollups,
        IReadOnlyList<RoadmapMilestoneDto>? milestones = null) =>
        RoadmapProjection.Project(items, rollups, Paces, Today, milestones);

    private static RoadmapItemDto Read(IReadOnlyList<RoadmapItemDto> projected, RoadmapItemDto item) =>
        Assert.Single(projected, read => read.Id == item.Id);

    /// <summary>AC8: stored 28 Sep–2 Oct, 8 points in <c>app</c> at 8 a week, nothing begun:
    /// laid out again from today, still the import's.</summary>
    [Fact]
    public void AnEffortPlacedItemNotBegun_MovesToTodayForItsEffort_StillPlacedByEffort()
    {
        var item = Item("Plan", repositories: ["app"]);

        var read = Read(Project([item], new() { [item.Id] = Gathers(Work("t", 8, ["JSdotNet/App"])) }), item);

        Assert.Equal((Today, Friday16), (read.Start, read.End));
        Assert.Equal(ImportPlacement.Effort, read.PlacedByImport);
    }

    /// <summary>AC1 and AC3: the window is the parts' envelope — the earliest part start to
    /// the latest part end, each part at its own repository's pace.</summary>
    [Fact]
    public void TheWindowIsThePartsEnvelope()
    {
        var item = Item("Plan", repositories: ["app", "site"]);

        var read = Read(Project([item], new() { [item.Id] = Gathers(Work("t", 8, ["JSdotNet/App", "JSdotNet/Site"])) }), item);

        Assert.Equal((Today, Friday23), (read.Start, read.End)); // app 12–16, site 12–23
    }

    /// <summary>A begun item keeps its earliest part start, and finished tasks no longer
    /// count: 4 of 12 points done leave 8 open at 8 a week, counted from today.</summary>
    [Fact]
    public void ABegunItem_KeepsItsEarliestStart_AndIsSizedByWhatIsLeft()
    {
        var item = Item("Plan", repositories: ["app"]);
        var began = new DateOnly(2026, 10, 5);

        var read = Read(Project([item], new()
        {
            [item.Id] = Gathers(
                Work("done", 4, ["app"], progress: RoadmapProgress.Done, started: began, completed: new DateOnly(2026, 10, 7)),
                Work("open", 8, ["app"]))
        }), item);

        Assert.Equal((began, Friday16), (read.Start, read.End));
        Assert.Equal(ImportPlacement.Effort, read.PlacedByImport);
    }

    /// <summary>AC8: an unbegun successor starts the worked day after its predecessor's
    /// projected end — whatever order the plan lists them in.</summary>
    [Fact]
    public void AnUnbegunSuccessor_StartsTheWorkedDayAfterItsPredecessorsProjectedEnd()
    {
        var first = Item("First", repositories: ["app"]);
        var second = Item("Second", after: [first.Id], repositories: ["site"]);

        var projected = Project([second, first], new()
        {
            [first.Id] = Gathers(Work("a", 8, ["app"])),
            [second.Id] = Gathers(Work("s", 4, ["site"]))
        });

        Assert.Equal([second.Id, first.Id], projected.Select(read => read.Id)); // the plan's order kept
        Assert.Equal((Today, Friday16), (Read(projected, first).Start, Read(projected, first).End));
        Assert.Equal((Monday19, Friday23), (Read(projected, second).Start, Read(projected, second).End));
    }

    /// <summary>AC3: a plan that waits on a two-repository plan starts after its latest
    /// part end, not its first.</summary>
    [Fact]
    public void ASuccessor_WaitsOnThePredecessorsLatestPartEnd()
    {
        var first = Item("First", repositories: ["app", "site"]);
        var second = Item("Second", after: [first.Id], repositories: ["app"]);

        var projected = Project([first, second], new()
        {
            [first.Id] = Gathers(Work("a", 8, ["app"]), Work("s", 4, ["site"], after: ["a"])),
            [second.Id] = Gathers(Work("b", 4, ["app"]))
        });

        Assert.Equal((Today, Friday23), (Read(projected, first).Start, Read(projected, first).End)); // app 12–16, site 19–23
        Assert.Equal(new DateOnly(2026, 10, 26), Read(projected, second).Start);
    }

    /// <summary>A due date, a window a person placed, a pinned end, finished work and an
    /// item that gathers nothing are handed back as stored.</summary>
    [Theory]
    [InlineData("due-date")]
    [InlineData("by hand")]
    [InlineData("pinned")]
    [InlineData("finished")]
    [InlineData("gathers nothing")]
    public void AnythingNotSizedByOpenEffort_IsHandedBackAsStored(string kind)
    {
        var item = kind switch
        {
            "due-date" => Item("Plan", placement: ImportPlacement.DueDate, repositories: ["app"]),
            "by hand" => Item("Plan", placement: null, repositories: ["app"]),
            "pinned" => Item("Plan", endPinned: true, repositories: ["app"]),
            _ => Item("Plan", repositories: ["app"])
        };
        var rollup = kind switch
        {
            "finished" => Gathers(Work("t", 8, ["app"], progress: RoadmapProgress.Done, completed: new DateOnly(2026, 10, 1))),
            "gathers nothing" => RoadmapItemRollupDto.Empty,
            "pinned" => Gathers(Work("t", 8, ["app"], progress: RoadmapProgress.InProgress, started: new DateOnly(2026, 9, 28))),
            _ => Gathers(Work("t", 8, ["app"]))
        };

        Assert.Same(item, Read(Project([item], new() { [item.Id] = rollup }), item));
    }

    /// <summary>A stored window still holds back what waits on it: a due date ending
    /// Tuesday 20 October puts its successor from the Wednesday.</summary>
    [Fact]
    public void AStoredPredecessorsEnd_StillFloorsItsSuccessor()
    {
        var due = Item("Due", placement: ImportPlacement.DueDate, start: Today, end: new DateOnly(2026, 10, 20), repositories: ["app"]);
        var next = Item("Next", after: [due.Id], repositories: ["app"]);

        var projected = Project([due, next], new()
        {
            [due.Id] = Gathers(Work("d", 40, ["app"])),
            [next.Id] = Gathers(Work("n", 8, ["app"]))
        });

        Assert.Same(due, Read(projected, due));
        Assert.Equal((new DateOnly(2026, 10, 21), new DateOnly(2026, 10, 27)), (Read(projected, next).Start, Read(projected, next).End));
    }

    /// <summary>A milestone the item waits on is a floor too; one in the past is today.</summary>
    [Theory]
    [InlineData(2026, 10, 16, 2026, 10, 19)]
    [InlineData(2026, 10, 1, 2026, 10, 12)]
    public void AMilestoneItWaitsOn_FloorsItsStart(int year, int month, int day, int y, int m, int d)
    {
        var milestone = new RoadmapMilestoneDto(Guid.NewGuid(), "Freeze", new DateOnly(year, month, day), MilestoneKind.Freeze, [], null, []);
        var item = Item("Plan", after: [milestone.Id], repositories: ["app"]);

        var projected = Project([item], new() { [item.Id] = Gathers(Work("t", 8, ["app"])) }, [milestone]);

        Assert.Equal(new DateOnly(y, m, d), Read(projected, item).Start);
    }

    /// <summary>A wait on something the plan does not hold is no floor, and a circle — which
    /// the plan refuses, but a reader must not hang on — is released at the earliest item.</summary>
    [Fact]
    public void AnUnknownWaitIsNoFloor_AndACircleIsReleased()
    {
        var lost = Item("Lost", after: [Guid.NewGuid()], repositories: ["app"]);
        var a = Item("A", repositories: ["app"]);
        var b = Item("B", after: [a.Id], repositories: ["app"]);
        a = a with { DependsOn = [b.Id] };

        var projected = Project([lost, a, b], new()
        {
            [lost.Id] = Gathers(Work("l", 8, ["app"])),
            [a.Id] = Gathers(Work("a", 8, ["app"])),
            [b.Id] = Gathers(Work("b", 8, ["app"]))
        });

        Assert.Equal(Today, Read(projected, lost).Start);
        Assert.Equal(Today, Read(projected, a).Start);   // released: the earliest waiter
        Assert.Equal(Monday19, Read(projected, b).Start); // still after A
    }

    /// <summary>The projection is a pure function, and projecting its own answer again the
    /// same day moves nothing — so a second opening of the roadmap stores nothing (AC8).</summary>
    [Fact]
    public void TheSameInputGivesTheSameAnswer_AndASecondProjectionMovesNothing()
    {
        var first = Item("First", repositories: ["app"]);
        var second = Item("Second", after: [first.Id], repositories: ["app", "site"]);
        var rollups = new Dictionary<Guid, RoadmapItemRollupDto>
        {
            [first.Id] = Gathers(Work("a", 8, ["app"])),
            [second.Id] = Gathers(Work("b", 4, ["app"]), Work("s", 4, ["site"], after: ["b"]))
        };

        var once = Project([first, second], rollups);
        var again = Project([first, second], rollups);
        var twice = Project(once, rollups);

        Assert.Equal(once, again);
        Assert.Equal(once, twice);
    }

    /// <summary>The layout behind each projected window is the parts service's, so a
    /// reader that draws parts and one that reports the window agree.</summary>
    [Fact]
    public void EachItemsLayoutIsThePartsTheWindowWasTakenFrom()
    {
        var first = Item("First", repositories: ["app"]);
        var second = Item("Second", after: [first.Id], repositories: ["site"]);
        var hand = Item("Hand", placement: null, repositories: ["app"]);

        var plan = RoadmapProjection.Lay([first, second, hand], new Dictionary<Guid, RoadmapItemRollupDto>
        {
            [first.Id] = Gathers(Work("a", 8, ["app"])),
            [second.Id] = Gathers(Work("s", 4, ["site"]))
        }, Paces, Today);

        var layout = plan.Layouts[second.Id];
        Assert.Equal(PartsPlacement.ByEffort, layout.Placement);
        Assert.Equal((Monday19, Friday23), (layout.Start, layout.End));
        Assert.Equal((layout.Start, layout.End), (Read(plan.Items, second).Start, Read(plan.Items, second).End));
        Assert.Equal(PartsPlacement.Stored, plan.Layouts[hand.Id].Placement);
    }

    /// <summary>The import's start rule: the day after the latest predecessor end, or today
    /// when it waits on nothing. Not yet held to today or moved to a worked day: the parts
    /// do both, for every caller alike.</summary>
    [Fact]
    public void StartAfter_IsTheDayAfterTheLatestPredecessorEnd_OrToday()
    {
        Assert.Equal(Today, RoadmapProjection.StartAfter([], Today));
        Assert.Equal(Monday19.AddDays(1), RoadmapProjection.StartAfter([Friday16, Monday19], Today));
        Assert.Equal(new DateOnly(2026, 10, 2), RoadmapProjection.StartAfter([new DateOnly(2026, 10, 1)], Today));
    }
}
