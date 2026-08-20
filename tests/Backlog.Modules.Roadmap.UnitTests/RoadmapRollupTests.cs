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
