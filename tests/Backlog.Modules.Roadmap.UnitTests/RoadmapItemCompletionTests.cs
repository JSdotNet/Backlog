using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// An item no task carries can be completed by hand, and then reads as finished wherever the
/// plan is read: the layout, the keep-up projection and the dashboard. Once tasks carry the
/// item, their progress wins and the hand-set day is not read.
/// </summary>
public class RoadmapItemCompletionTests
{
    /// <summary>Monday 12 October 2026.</summary>
    private static readonly DateOnly Today = new(2026, 10, 12);

    private static readonly DateOnly PlannedStart = new(2026, 9, 28);
    private static readonly DateOnly PlannedEnd = new(2026, 10, 2);
    private static readonly DateOnly Done = new(2026, 10, 5);

    private static readonly PacesInUseDto Paces = new(
        7m,
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["app"] = 8m, ["site"] = 4m });

    private static RoadmapItemDto Item(
        DateOnly? completedOn,
        ImportPlacement? placement = null,
        params string[] repositories) =>
        new(
            Guid.NewGuid(),
            "Write the launch post",
            PlannedStart,
            PlannedEnd,
            PlanningPriority.Medium,
            repositories,
            null,
            null,
            [],
            PlacedByImport: placement,
            CompletedOn: completedOn);

    private static RoadmapGatheredLink Task(string key, RoadmapProgress progress, DateOnly? completed = null) =>
        new(key, key, 3, RollupOrigin.Tag, progress, [], ["app"], CompletedOn: completed);

    private static readonly RoadmapItemRollupDto OnlyAChapter = new([], [new RoadmapGatheredLink("docs/a.md#b", "A", null, RollupOrigin.Direct)]);

    // ---- Whether it is finished, and since when ------------------------------------------

    [Fact]
    public void ATaskLessItemCompletedByHand_IsFinished_OnTheDayItWasCompleted()
    {
        var item = Item(Done);

        Assert.True(RoadmapItemCompletion.IsFinished(item, RoadmapItemRollupDto.Empty));
        Assert.True(RoadmapItemCompletion.IsCompletedByHand(item, RoadmapItemRollupDto.Empty));
        Assert.Equal(Done, RoadmapItemCompletion.FinishedOn(item, RoadmapItemRollupDto.Empty));
    }

    [Fact]
    public void AKnowledgeChapter_IsNoTask_SoTheHandSetDayStillCounts()
    {
        var item = Item(Done);

        Assert.True(RoadmapItemCompletion.IsFinished(item, OnlyAChapter));
        Assert.False(RoadmapItemCompletion.HasTasks(OnlyAChapter));
    }

    [Fact]
    public void ATaskLessItemNobodyCompleted_IsNotFinished()
    {
        var item = Item(completedOn: null);

        Assert.False(RoadmapItemCompletion.IsFinished(item, RoadmapItemRollupDto.Empty));
        Assert.False(RoadmapItemCompletion.IsCompletedByHand(item, RoadmapItemRollupDto.Empty));
        Assert.Null(RoadmapItemCompletion.FinishedOn(item, RoadmapItemRollupDto.Empty));
    }

    [Fact]
    public void OnceTasksCarryTheItem_TheirProgressWins_OverTheHandSetDay()
    {
        var item = Item(Done);
        var open = new RoadmapItemRollupDto([Task("a", RoadmapProgress.Ready)], []);

        Assert.True(RoadmapItemCompletion.HasTasks(open));
        Assert.False(RoadmapItemCompletion.IsFinished(item, open));
        Assert.False(RoadmapItemCompletion.IsCompletedByHand(item, open));
    }

    [Fact]
    public void WhenTasksCarryTheItemAndAreAllDone_ItEndsWhenTheLastTaskWasTickedOff()
    {
        var item = Item(Done);
        var finished = new RoadmapItemRollupDto([Task("a", RoadmapProgress.Done, new DateOnly(2026, 10, 7))], []);

        Assert.True(RoadmapItemCompletion.IsFinished(item, finished));
        Assert.Equal(new DateOnly(2026, 10, 7), RoadmapItemCompletion.FinishedOn(item, finished));
    }

    [Fact]
    public void AnItemWhoseWorkWasNotRead_ReadsAsHavingGatheredNothing()
    {
        Assert.True(RoadmapItemCompletion.IsFinished(Item(Done), rollup: null));
        Assert.False(RoadmapItemCompletion.HasTasks(null));
    }

    // ---- The layout ------------------------------------------------------------------------

    [Fact]
    public void ATaskLessItemCompletedByHand_IsLaidOutFinished_OverTheWindowItWasPlannedIn()
    {
        var layout = RoadmapItemParts.Of(Item(Done, repositories: ["app", "site"]), RoadmapItemRollupDto.Empty, Paces, Today);

        Assert.Equal(PartsPlacement.WhereItRan, layout.Placement);
        Assert.Equal((PlannedStart, PlannedEnd), (layout.Start, layout.End));
        Assert.Equal(2, layout.Parts.Count);
        Assert.All(layout.Parts, part =>
        {
            Assert.True(part.Ran);
            Assert.Equal((PlannedStart, PlannedEnd), (part.Start, part.End));
        });
    }

    [Fact]
    public void ATaskLessItemCompletedByHand_IsLaidOutFinished_EvenWithNoDayToPlaceFrom()
    {
        var layout = RoadmapItemParts.Of(Item(Done), rollup: null, Paces, today: null);

        Assert.Equal(PartsPlacement.WhereItRan, layout.Placement);
        Assert.True(Assert.Single(layout.Parts).Ran);
    }

    [Fact]
    public void ATaskLessItemNobodyCompleted_StaysOverItsStoredWindow()
    {
        var layout = RoadmapItemParts.Of(Item(completedOn: null), RoadmapItemRollupDto.Empty, Paces, Today);

        Assert.Equal(PartsPlacement.Stored, layout.Placement);
        Assert.False(Assert.Single(layout.Parts).Ran);
    }

    [Fact]
    public void OnceTasksCarryTheItem_TheLayoutFollowsTheTasks_NotTheHandSetDay()
    {
        var item = Item(Done, ImportPlacement.Effort, "app");
        var open = new RoadmapItemRollupDto([Task("a", RoadmapProgress.Ready)], []);

        var layout = RoadmapItemParts.Of(item, open, Paces, Today);

        Assert.Equal(PartsPlacement.ByEffort, layout.Placement);
    }

    // ---- Keep-up ---------------------------------------------------------------------------

    [Fact]
    public void TheKeepUpProjection_LeavesAnItemCompletedByHandWhereItWasPlanned()
    {
        var item = Item(Done, ImportPlacement.Effort, "app");

        var (read, layout) = RoadmapProjection.One(item, RoadmapItemRollupDto.Empty, Paces, Today);

        Assert.Same(item, read);
        Assert.Equal(PartsPlacement.WhereItRan, layout.Placement);
    }

    /// <summary>The work was done on 5 October although its window runs to the 30th, so
    /// what waits on it is free from today, Monday 12 October — not from the 31st.</summary>
    [Fact]
    public void WhatWaitsOnAnItemCompletedByHand_IsReleasedWhenItWasCompleted_NotWhenItsWindowEnds()
    {
        var done = Item(Done, repositories: "app") with { End = new DateOnly(2026, 10, 30) };
        var waiting = Item(completedOn: null, ImportPlacement.Effort, "app") with { DependsOn = [done.Id] };
        var rollups = new Dictionary<Guid, RoadmapItemRollupDto>
        {
            [waiting.Id] = new([Task("a", RoadmapProgress.Ready)], [])
        };

        var laid = RoadmapProjection.Lay([done, waiting], rollups, Paces, Today);

        Assert.Equal(Today, laid.Items[1].Start);
    }

    [Fact]
    public void WhatWaitsOnAnOpenTaskLessItem_StillWaitsForItsWindowToEnd()
    {
        var open = Item(completedOn: null, repositories: "app") with { End = new DateOnly(2026, 10, 30) };
        var waiting = Item(completedOn: null, ImportPlacement.Effort, "app") with { DependsOn = [open.Id] };
        var rollups = new Dictionary<Guid, RoadmapItemRollupDto>
        {
            [waiting.Id] = new([Task("a", RoadmapProgress.Ready)], [])
        };

        var laid = RoadmapProjection.Lay([open, waiting], rollups, Paces, Today);

        Assert.Equal(new DateOnly(2026, 11, 2), laid.Items[1].Start);
    }
}
