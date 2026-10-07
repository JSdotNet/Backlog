using Backlog.UI.Components.Roadmap;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// An item whose tasks hand over between repositories is drawn as that sequence — a
/// segment per repository per phase — each segment placed after the segments it waits on
/// at its own repository's pace, rather than one bar per repository all running the same
/// window or a window shared out by size. What decides a phase, where each segment lands,
/// which arrows the hand-overs draw, and how a drag on one segment moves the item.
/// </summary>
public class RoadmapPlanViewHandOverTests
{
    private static readonly List<PlannedRepository> Configured =
    [
        new("backlog", "JSdotNet/Backlog", 1),
        new("fincent", "JSdotNet/Fincent", 2)
    ];

    /// <summary>Monday 12 October 2026, on the default working week: backlog gets through 8
    /// points a week and fincent 4.</summary>
    private static readonly RoadmapForecast OnMonday = new(
        new DateOnly(2026, 10, 12),
        new PacesInUseDto(7m, new Dictionary<string, decimal> { ["backlog"] = 8m, ["fincent"] = 4m }));

    private static DateOnly October(int day) => new(2026, 10, day);

    private static RoadmapItemDto Item(
        Guid? id = null,
        Guid[]? dependsOn = null,
        int startDay = 1,
        int endDay = 30,
        ImportPlacement? placedBy = ImportPlacement.Effort) =>
        new(
            id ?? Guid.NewGuid(),
            "Spans both",
            October(startDay),
            October(endDay),
            PlanningPriority.Medium,
            ["backlog", "fincent"],
            null,
            null,
            dependsOn ?? [],
            null,
            "",
            null,
            placedBy);

    private static RoadmapGatheredLink Task(string key, int effort, string repository, params string[] waits) =>
        new(key, key.ToUpperInvariant(), effort, RollupOrigin.Tag, RoadmapProgress.Planned, waits, [repository]);

    private static RoadmapTimelineModel Draw(RoadmapItemDto item, params RoadmapGatheredLink[] tasks) =>
        Draw([item], new Dictionary<Guid, RoadmapItemRollupDto> { [item.Id] = new(tasks, []) });

    private static RoadmapTimelineModel Draw(
        IEnumerable<RoadmapItemDto> items,
        IReadOnlyDictionary<Guid, RoadmapItemRollupDto> rollups) =>
        RoadmapPlanView.From(new RoadmapPlanDto([.. items], [], [], null), Configured, rollups, OnMonday);

    private static RoadmapGatheredLink Done(string key, string repository, int started, int completed, params string[] waits) =>
        new(key, key.ToUpperInvariant(), 5, RollupOrigin.Tag, RoadmapProgress.Done, waits, [repository],
            StartedOn: new DateOnly(2026, 1, started), CompletedOn: new DateOnly(2026, 1, completed));

    private static RoadmapItemDto January() =>
        new(Guid.NewGuid(), "Spans both", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 30), PlanningPriority.Medium,
            ["backlog", "fincent"], null, null, [], null, "", null);

    [Fact]
    public void AnItemInFlight_DrawsItsDoneSegmentsWhereTheyRan_AndOnlyTheOpenOnesForward()
    {
        // Planned across all of January; backlog and fincent finished their parts in
        // the first week, and one point is left in backlog. Today is Saturday the 10th,
        // so the point left is forecast for Monday the 12th, the first worked day.
        var item = January();
        var rollups = new Dictionary<Guid, RoadmapItemRollupDto>
        {
            [item.Id] = new(
            [
                Done("a", "JSdotNet/Backlog", 1, 2),
                Done("b", "JSdotNet/Fincent", 3, 4, "a"),
                new RoadmapGatheredLink("c", "C", 1, RollupOrigin.Tag, RoadmapProgress.Ready, ["b"], ["JSdotNet/Backlog"])
            ], [])
        };

        var view = RoadmapPlanView.From(
            new RoadmapPlanDto([item], [], [], null),
            Configured,
            rollups,
            forecast: new RoadmapForecast(new DateOnly(2026, 1, 10), new PacesInUseDto(7m, new Dictionary<string, decimal>())));

        var bars = view.Bars.OrderBy(bar => bar.Start).ToList();
        Assert.Equal(
            [(1, 2), (3, 4), (12, 12)],
            bars.Select(bar => (bar.Start.Day, bar.End.Day)));
        Assert.All(bars, bar => Assert.True(bar.Locked));
    }

    [Fact]
    public void AnItemInFlight_OffersTheEndOfEveryOpenSegment_AndNoneWhereTheWorkRan()
    {
        // Backlog finished its first part; fincent and then backlog again are still
        // open. Today is the 10th.
        var item = January();
        var rollups = new Dictionary<Guid, RoadmapItemRollupDto>
        {
            [item.Id] = new(
            [
                Done("a", "JSdotNet/Backlog", 1, 2),
                new RoadmapGatheredLink("b", "B", 3, RollupOrigin.Tag, RoadmapProgress.InProgress, ["a"], ["JSdotNet/Fincent"],
                    StartedOn: new DateOnly(2026, 1, 3)),
                new RoadmapGatheredLink("c", "C", 2, RollupOrigin.Tag, RoadmapProgress.Ready, ["b"], ["JSdotNet/Backlog"])
            ], [])
        };

        var view = RoadmapPlanView.From(
            new RoadmapPlanDto([item], [], [], null),
            Configured,
            rollups,
            forecast: new RoadmapForecast(new DateOnly(2026, 1, 10), new PacesInUseDto(7m, new Dictionary<string, decimal>())));

        var bars = view.Bars.OrderBy(bar => bar.Start).ToList();
        Assert.Equal(3, bars.Count);
        Assert.All(bars, bar => Assert.True(bar.Locked));
        Assert.Equal([false, true, true], bars.Select(bar => bar.EndResizable));
    }

    /// <summary>AC10 (Q2): in-flight work whose backlog part is forecast to end on Friday the
    /// 16th and whose fincent part, waiting on it, on Friday the 23rd. The end pinned at the
    /// 30th draws the fincent part to the pin and leaves the backlog part on its forecast.</summary>
    [Fact]
    public void APinnedEnd_MovesOnlyThePartThatEndsLatest()
    {
        var item = Item(startDay: 12, endDay: 30, placedBy: null) with { EndPinned = true };
        var view = Draw(
            item,
            new RoadmapGatheredLink("a", "A", 8, RollupOrigin.Tag, RoadmapProgress.InProgress, null, ["JSdotNet/Backlog"],
                StartedOn: October(12)),
            Task("b", 4, "JSdotNet/Fincent", "a"));

        var backlog = Assert.Single(view.Bars, bar => bar.Id.Contains("@backlog", StringComparison.Ordinal));
        var fincent = Assert.Single(view.Bars, bar => bar.Id.Contains("@fincent", StringComparison.Ordinal));

        Assert.Equal((October(12), October(16)), (backlog.Start, backlog.End));
        Assert.Equal((October(19), October(30)), (fincent.Start, fincent.End));

        // The pinned part keeps its own forecast in its detail; the other part was not moved.
        Assert.Contains("Forecast: 23 Oct 2026 at 4 pt/wk", fincent.Detail!.Split('\n'));
        Assert.DoesNotContain("Forecast:", backlog.Detail, StringComparison.Ordinal);
    }

    /// <summary>AC10: dragging the end of a part that is not the latest pins the item's end at
    /// the drawn envelope's end, moved by as many days as that part's end was.</summary>
    [Fact]
    public void PullingAnEarlierPartsEnd_PinsTheItemsEnd_AsManyDaysLater()
    {
        var item = Item();
        var bars = new[]
        {
            new RoadmapBar($"{item.Id}@backlog", "row", "Spans both", October(12), October(16), Locked: true, EndResizable: true),
            new RoadmapBar($"{item.Id}@fincent", "row", "Spans both", October(19), October(23), Locked: true, EndResizable: true)
        };

        // The earlier part's end, pulled out three days: the item ends three days later.
        var earlier = RoadmapPlanView.PinnedEndFor(bars, bars[0],
            new RoadmapChange(bars[0].Id, "row", bars[0].Start, October(19), RoadmapDrag.ResizeEnd));
        Assert.Equal(October(26), earlier);

        // The latest part's end is the item's: it pins where it was dropped.
        var last = RoadmapPlanView.PinnedEndFor(bars, bars[1],
            new RoadmapChange(bars[1].Id, "row", bars[1].Start, October(30), RoadmapDrag.ResizeEnd));
        Assert.Equal(October(30), last);
    }

    /// <summary>AC2 and the hand-over: backlog, then fincent waiting on it, then backlog
    /// again waiting on fincent — each segment from the worked day after the one it waits on,
    /// for its own points at its own repository's pace.</summary>
    [Fact]
    public void TasksHandingOverBetweenRepositories_AreDrawnAsConsecutiveSegments_EachAtItsOwnPace()
    {
        var item = Item();

        var view = Draw(
            item,
            Task("a", 8, "JSdotNet/Backlog"),
            Task("b", 4, "JSdotNet/Fincent", "a"),
            Task("c", 8, "JSdotNet/Backlog", "b"));

        var bars = view.Bars.OrderBy(bar => bar.Start).ToList();
        Assert.Equal(
            [$"{item.Id}@backlog#1", $"{item.Id}@fincent#2", $"{item.Id}@backlog#3"],
            bars.Select(bar => bar.Id));

        // Every segment is the one stored item, so opening or moving any opens or moves it.
        Assert.All(bars, bar => Assert.Equal(item.Id, RoadmapPlanView.NodeIdOf(bar.Id)));

        // 8 points at 8 a week, then 4 at 4 a week, then 8 at 8 a week: a working week each.
        Assert.Equal(
            [(October(12), October(16)), (October(19), October(23)), (October(26), October(30))],
            bars.Select(bar => (bar.Start, bar.End)));

        Assert.Equal(["a"], bars[0].StepList.Select(step => step.Id));
        Assert.Equal(["b"], bars[1].StepList.Select(step => step.Id));
        Assert.Equal(["c"], bars[2].StepList.Select(step => step.Id));

        // Both backlog segments share a row: they follow on, they do not overlap.
        Assert.Equal(bars[0].RowId, bars[2].RowId);
        Assert.Contains("handed over between repositories", bars[1].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheWaitsThatCrossARepository_BecomeArrows()
    {
        var item = Item();

        var view = Draw(
            item,
            Task("a", 2, "JSdotNet/Backlog"),
            // Waits inside its own repository: same phase, no arrow.
            Task("a2", 2, "JSdotNet/Backlog", "a"),
            Task("b", 2, "JSdotNet/Fincent", "a2"));

        Assert.Equal(2, view.Bars.Count);
        var link = Assert.Single(view.Links);
        Assert.Equal(new RoadmapLink($"{item.Id}@backlog#1", $"{item.Id}@fincent#2"), link);
    }

    /// <summary>AC1: repositories that never hand over keep one part each, side by side, but
    /// each at its own pace — 4 points at backlog's 8 a week is half a week, 4 at fincent's
    /// 4 a week a whole one.</summary>
    [Fact]
    public void RepositoriesThatNeverHandOver_KeepOnePartEach_EachAtItsOwnPace()
    {
        var item = Item();

        var view = Draw(
            item,
            Task("a", 2, "JSdotNet/Backlog"),
            Task("a2", 2, "JSdotNet/Backlog", "a"),
            Task("b", 4, "JSdotNet/Fincent"));

        var backlog = Assert.Single(view.Bars, bar => bar.Id == $"{item.Id}@backlog");
        var fincent = Assert.Single(view.Bars, bar => bar.Id == $"{item.Id}@fincent");

        Assert.Equal((October(12), October(14)), (backlog.Start, backlog.End));
        Assert.Equal((October(12), October(16)), (fincent.Start, fincent.End));
        Assert.Empty(view.Links);
    }

    /// <summary>AC6: a due date or a person placed the window, so the work is not cut into
    /// phases — every part draws over the one stored window, and nothing hands over.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData(ImportPlacement.DueDate)]
    public void AHandOverInAWindowNotSizedByItsEffort_DrawsEveryPartOverTheStoredWindow(ImportPlacement? placedBy)
    {
        var item = Item(startDay: 12, endDay: 30, placedBy: placedBy);

        var view = Draw(
            item,
            Task("a", 8, "JSdotNet/Backlog"),
            Task("b", 4, "JSdotNet/Fincent", "a"),
            Task("c", 8, "JSdotNet/Backlog", "b"));

        Assert.Equal(
            [$"{item.Id}@backlog", $"{item.Id}@fincent"],
            view.Bars.Select(bar => bar.Id).Order(StringComparer.Ordinal));
        Assert.All(view.Bars, bar => Assert.Equal((item.Start, item.End), (bar.Start, bar.End)));
        Assert.Empty(view.Links);
    }

    /// <summary>Q4: a task filed in both repositories in the middle of a hand-over belongs to
    /// a segment in each, and what waits on it waits on both.</summary>
    [Fact]
    public void ATaskFiledInBothRepositoriesInAHandOver_IsASegmentInEach()
    {
        var item = Item();

        var view = Draw(
            item,
            Task("a", 4, "JSdotNet/Backlog"),
            new RoadmapGatheredLink("b", "B", 4, RollupOrigin.Tag, RoadmapProgress.Planned, ["a"], ["JSdotNet/Backlog", "JSdotNet/Fincent"]),
            Task("c", 4, "JSdotNet/Fincent", "b"));

        var withB = view.Bars.Where(bar => bar.StepList.Any(step => step.Id == "b")).Select(bar => bar.Id).Order(StringComparer.Ordinal);
        Assert.Equal([$"{item.Id}@backlog#1", $"{item.Id}@fincent#2"], withB);
    }

    [Fact]
    public void APlanDependencyOnASequencedItem_IsOneArrow_FromItsLastSegment()
    {
        var first = Item();
        var then = Item(dependsOn: [first.Id], startDay: 31, endDay: 31, placedBy: null);

        var view = Draw(
            [first, then],
            new Dictionary<Guid, RoadmapItemRollupDto>
            {
                [first.Id] = new([Task("a", 1, "JSdotNet/Backlog"), Task("b", 1, "JSdotNet/Fincent", "a")], [])
            });

        // The hand-over inside first, and one arrow from where first finishes to then —
        // not one into every part of it.
        Assert.Contains(new RoadmapLink($"{first.Id}@backlog#1", $"{first.Id}@fincent#2"), view.Links);
        var planArrow = Assert.Single(view.Links, link => RoadmapPlanView.NodeIdOf(link.ToId) == then.Id);
        Assert.Equal($"{first.Id}@fincent#2", planArrow.FromId);
    }

    /// <summary>AC5: the AC2 plan draws backlog 12–16 and fincent 19–23 October. Dragging the
    /// fincent part a week moves the whole item a week from the window it is drawn over —
    /// not from whatever was stored — and pulling that part's end out lengthens it.</summary>
    [Fact]
    public void DraggingOnePart_MovesTheWholeItemFromItsDrawnWindow_AndPullingItsEnd_LengthensIt()
    {
        var item = Item(startDay: 1, endDay: 2);
        var view = RoadmapPlanView.From(
            new RoadmapPlanDto([item], [], [], null),
            Configured,
            new Dictionary<Guid, RoadmapItemRollupDto>
            {
                [item.Id] = new([Task("a", 4, "JSdotNet/Backlog"), Task("b", 4, "JSdotNet/Fincent", "a")], [])
            },
            OnMonday with { Paces = OnMonday.Paces with { ByRepository = new Dictionary<string, decimal> { ["backlog"] = 4m, ["fincent"] = 4m } } });
        var second = view.Bars.Single(bar => bar.Id.EndsWith("#2", StringComparison.Ordinal));
        Assert.Equal((October(19), October(23)), (second.Start, second.End));

        var moved = RoadmapPlanView.ItemWindowFor(view.Bars, second,
            new RoadmapChange(second.Id, second.RowId, second.Start.AddDays(7), second.End.AddDays(7), RoadmapDrag.Move));
        Assert.Equal((October(19), October(30)), moved);

        var longer = RoadmapPlanView.ItemWindowFor(view.Bars, second,
            new RoadmapChange(second.Id, second.RowId, second.Start, second.End.AddDays(7), RoadmapDrag.ResizeEnd));
        Assert.Equal((October(12), October(30)), longer);
    }

    /// <summary>QA fix round 1: an item in flight whose fincent part has begun — one task done
    /// from the 5th to the 9th, one still open — and whose backlog part holds 7 points nobody
    /// has started. Only the begun part is drawn from when its work began and says so; the
    /// backlog part is placed from today, as a part not begun is, and says that instead.</summary>
    [Fact]
    public void InAnItemInFlight_APartWhoseWorkHasNotBegun_IsNotDrawnOrDescribedAsBegun()
    {
        var item = Item(startDay: 1, endDay: 30, placedBy: null);
        var view = Draw(
            item,
            Task("a", 7, "JSdotNet/Backlog"),
            new RoadmapGatheredLink("b", "B", 2, RollupOrigin.Tag, RoadmapProgress.Done, null, ["JSdotNet/Fincent"],
                StartedOn: October(5), CompletedOn: October(9)),
            Task("c", 4, "JSdotNet/Fincent"));

        var backlog = Assert.Single(view.Bars, bar => bar.Id.Contains("@backlog", StringComparison.Ordinal));
        var fincent = Assert.Single(view.Bars, bar => bar.Id.Contains("@fincent", StringComparison.Ordinal));

        // The begun part starts where its work did; the other from today, not from fincent's work.
        Assert.Equal(October(5), fincent.Start);
        Assert.Equal(October(12), backlog.Start);

        Assert.Contains("in progress, drawn from when the work began", fincent.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("in progress", backlog.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("when the work began", backlog.Detail, StringComparison.Ordinal);
        Assert.Contains("not started yet, placed after what it waits on", backlog.Detail, StringComparison.Ordinal);
    }
}
