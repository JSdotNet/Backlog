using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// The de-duplication a roadmap item's rollup depends on: a thing reached both
/// directly and by tag is one thing, and it wears <see cref="RollupOrigin.Both"/>
/// rather than being counted twice or losing one of its two threads. A pure function,
/// asserted without a store behind it.
/// </summary>
public class RoadmapRollupMergeTests
{
    private static RoadmapGatheredLink Link(
        string key,
        RollupOrigin origin,
        int? effort = null,
        string? title = null) =>
        new(key, title ?? key, effort, origin);

    [Fact]
    public void ADirectCandidate_isKeptAsDirect()
    {
        var link = Assert.Single(RoadmapRollup.Merge([Link("a", RollupOrigin.Direct)]));

        Assert.Equal("a", link.Key);
        Assert.Equal(RollupOrigin.Direct, link.Origin);
    }

    [Fact]
    public void ATagCandidate_isKeptAsTag()
    {
        var link = Assert.Single(RoadmapRollup.Merge([Link("a", RollupOrigin.Tag)]));

        Assert.Equal(RollupOrigin.Tag, link.Origin);
    }

    [Fact]
    public void TheSameThingReachedBothWays_isCountedOnce_andWearsBoth()
    {
        var merged = RoadmapRollup.Merge(
        [
            Link("a", RollupOrigin.Direct, effort: 3, title: "First seen"),
            Link("a", RollupOrigin.Tag, effort: 8, title: "Second seen")
        ]);

        var link = Assert.Single(merged);
        Assert.Equal(RollupOrigin.Both, link.Origin);

        // The first title and effort win — it is one thing that registered one of
        // each, reached twice.
        Assert.Equal("First seen", link.Title);
        Assert.Equal(3, link.Effort);
    }

    [Fact]
    public void Order_isTheOrderAKeyFirstAppears()
    {
        var merged = RoadmapRollup.Merge(
        [
            Link("b", RollupOrigin.Direct),
            Link("a", RollupOrigin.Tag),
            Link("b", RollupOrigin.Tag)
        ]);

        Assert.Equal(["b", "a"], merged.Select(link => link.Key));
        Assert.Equal(RollupOrigin.Both, merged[0].Origin);
    }

    [Fact]
    public void Keys_compareCaseInsensitively_soOneThingIsNotTwo()
    {
        var merged = RoadmapRollup.Merge(
        [
            Link("A", RollupOrigin.Direct),
            Link("a", RollupOrigin.Tag)
        ]);

        var link = Assert.Single(merged);
        Assert.Equal(RollupOrigin.Both, link.Origin);
    }
}

/// <summary>
/// The arithmetic beside the rollup lists: a total that sums only the work that
/// registered a number, and a separate count of the work that registered none — so
/// the total is never read as smaller than the work is.
/// </summary>
public class RoadmapItemRollupDtoTotalsTests
{
    private static RoadmapGatheredLink Link(string key, int? effort) =>
        new(key, key, effort, RollupOrigin.Direct);

    [Fact]
    public void AMixOfEstimatedAndUnestimated_SumsTheOne_AndCountsTheOther()
    {
        var dto = new RoadmapItemRollupDto(
            [Link("a", 5), Link("b", null), Link("c", 8)],
            [Link("d", null)]);

        Assert.Equal(13, dto.TotalEffort);
        Assert.Equal(2, dto.EstimatedCount);
        Assert.Equal(2, dto.UnestimatedCount);
        Assert.Equal(4, dto.GatheredCount);
        Assert.False(dto.IsEmpty);
    }

    [Fact]
    public void Zero_IsAnEstimate_AndContributesZero()
    {
        var dto = new RoadmapItemRollupDto([Link("a", 0), Link("b", 5)], []);

        Assert.Equal(5, dto.TotalEffort);
        Assert.Equal(2, dto.EstimatedCount);
        Assert.Equal(0, dto.UnestimatedCount);
    }

    [Fact]
    public void Null_ContributesNothing_ButRaisesTheUnestimatedCount()
    {
        var dto = new RoadmapItemRollupDto([Link("a", null)], []);

        Assert.Equal(0, dto.TotalEffort);
        Assert.Equal(0, dto.EstimatedCount);
        Assert.Equal(1, dto.UnestimatedCount);
        Assert.False(dto.IsEmpty);
    }

    [Fact]
    public void NothingGathered_IsEmpty()
    {
        Assert.True(RoadmapItemRollupDto.Empty.IsEmpty);
        Assert.Equal(0, RoadmapItemRollupDto.Empty.GatheredCount);
        Assert.Equal(0, RoadmapItemRollupDto.Empty.TotalEffort);
    }
}

/// <summary>
/// The second pair of figures, the ones a progress fill is drawn from: how much of
/// the registered effort is finished, and how many finished things there are. The
/// second is not decoration — finished work that registered no estimate is invisible
/// to the first, and a fill that hid it would read as further along than the plan is.
/// </summary>
public class RoadmapItemRollupDtoProgressTests
{
    private static RoadmapGatheredLink Link(string key, int? effort, RoadmapProgress? progress) =>
        new(key, key, effort, RollupOrigin.Direct, progress);

    [Fact]
    public void DoneEffort_SumsOnlyWhatIsFinished_AndTotalEffortStillSumsTheLot()
    {
        var dto = new RoadmapItemRollupDto(
            [
                Link("done", 5, RoadmapProgress.Done),
                Link("doing", 3, RoadmapProgress.InProgress),
                Link("ready", 2, RoadmapProgress.Ready),
                Link("planned", 1, RoadmapProgress.Planned)
            ],
            []);

        Assert.Equal(5, dto.DoneEffort);
        Assert.Equal(11, dto.TotalEffort);
        Assert.Equal(1, dto.DoneCount);
    }

    /// <summary>The rule the fill is not allowed to break: finished work nobody
    /// sized adds nothing to the numerator, and is counted where it can be
    /// seen.</summary>
    [Fact]
    public void FinishedWorkThatRegisteredNoEstimate_RaisesTheDoneCount_ButNotTheDoneEffort()
    {
        var dto = new RoadmapItemRollupDto(
            [Link("sized", 5, RoadmapProgress.Done), Link("unsized", null, RoadmapProgress.Done)],
            []);

        Assert.Equal(5, dto.DoneEffort);
        Assert.Equal(2, dto.DoneCount);
        Assert.Equal(1, dto.UnestimatedCount);
    }

    /// <summary>A knowledge chapter registers no progress at all, and unknown is
    /// never read as finished.</summary>
    [Fact]
    public void SomethingThatRegisteredNoProgress_IsNotDone()
    {
        var dto = new RoadmapItemRollupDto([], [Link("chapter", 8, progress: null)]);

        Assert.Equal(0, dto.DoneEffort);
        Assert.Equal(0, dto.DoneCount);
        Assert.Equal(8, dto.TotalEffort);
        Assert.False(Assert.Single(dto.KnowledgeChapters).IsDone);
    }

    [Fact]
    public void ZeroIsAFinishedEstimate_AndContributesZeroToTheFill()
    {
        var dto = new RoadmapItemRollupDto([Link("free", 0, RoadmapProgress.Done)], []);

        Assert.Equal(0, dto.DoneEffort);
        Assert.Equal(1, dto.DoneCount);
        Assert.Equal(0, dto.UnestimatedCount);
    }

    [Fact]
    public void NothingGathered_HasNothingDone()
    {
        Assert.Equal(0, RoadmapItemRollupDto.Empty.DoneEffort);
        Assert.Equal(0, RoadmapItemRollupDto.Empty.DoneCount);
    }
}

/// <summary>
/// The order a plan's steps are drawn in: nothing before what it waits on, ties by
/// the order the caller handed them in, and a cycle tolerated rather than refused —
/// a person can write one with an <c>after:</c>, so a drawing that threw on one
/// would be a surface they could break by typing.
/// </summary>
public class RoadmapRollupOrderTests
{
    private static RoadmapGatheredLink Link(string key, params string[] waitsOn) =>
        new(key, key, null, RollupOrigin.Tag, RoadmapProgress.Ready, waitsOn);

    private static IReadOnlyList<string> Order(params RoadmapGatheredLink[] links) =>
        [.. RoadmapRollup.InDependencyOrder(links).Select(link => link.Key)];

    [Fact]
    public void AStepComesAfterTheOneItWaitsOn_WhateverOrderItArrivedIn()
    {
        Assert.Equal(["a", "b"], Order(Link("b", "a"), Link("a")));
    }

    [Fact]
    public void AChain_ComesOutInChainOrder()
    {
        Assert.Equal(["a", "b", "c"], Order(Link("c", "b"), Link("b", "a"), Link("a")));
    }

    /// <summary>Two steps equally free to go next keep the order they arrived in, so
    /// the caller decides what "first" means by deciding what it hands in.</summary>
    [Fact]
    public void TwoStepsWaitingOnNothing_KeepTheOrderTheyArrivedIn()
    {
        Assert.Equal(["second", "first"], Order(Link("second"), Link("first")));
    }

    [Fact]
    public void AStepFreedLaterStillLosesATieToOneThatArrivedEarlier()
    {
        // "early" and "late" both become free once "root" is out; "early" arrived first.
        Assert.Equal(["root", "early", "late"], Order(Link("root"), Link("early", "root"), Link("late", "root")));
    }

    /// <summary>A dependency on something the item did not gather is not an edge
    /// here: it is real, and it really blocks, but there is no position in this list
    /// for it to order against.</summary>
    [Fact]
    public void ADependencyOnSomethingAbsent_OrdersNothing()
    {
        Assert.Equal(["b", "a"], Order(Link("b", "nowhere"), Link("a")));
    }

    [Fact]
    public void ACycle_FallsBackToAppearanceOrder_RatherThanThrowing()
    {
        Assert.Equal(["a", "b", "c"], Order(Link("a", "c"), Link("b", "a"), Link("c", "b")));
    }

    /// <summary>Only the deadlock falls back. Work downstream of a cycle is still
    /// ordered after it, because the pass resumes once the earliest waiting step is
    /// released.</summary>
    [Fact]
    public void WorkDownstreamOfACycle_StillLandsAfterIt()
    {
        // "a" and "b" wait on each other; "after" waits on "b" and is not part of
        // the knot. Releasing the earliest of the two frees the rest of the pass.
        Assert.Equal(["a", "b", "after"], Order(Link("a", "b"), Link("b", "a"), Link("after", "b")));
    }

    /// <summary>A one-step loop is ignored rather than deadlocking the step: an
    /// entry naming itself waits on nothing that could ever arrive, and holding it
    /// back until the fallback released it would put it behind work it does not
    /// actually follow.</summary>
    [Fact]
    public void AStepWaitingOnItself_IsNotHeldUpByItself()
    {
        Assert.Equal(["loop", "free"], Order(Link("loop", "loop"), Link("free")));
    }

    [Fact]
    public void KeysCompareCaseInsensitively_SoAnEdgeIsNotLostToCasing()
    {
        Assert.Equal(["A", "b"], Order(Link("b", "a"), Link("A")));
    }

    [Fact]
    public void NothingAndOneThing_ComeBackAsTheyWere()
    {
        Assert.Empty(RoadmapRollup.InDependencyOrder([]));
        Assert.Equal(["only"], Order(Link("only")));
    }

    [Fact]
    public void EveryStepComesBackExactlyOnce()
    {
        var ordered = Order(Link("c", "a", "b"), Link("b", "a"), Link("a"), Link("d", "c"));

        Assert.Equal(["a", "b", "c", "d"], ordered);
    }
}
