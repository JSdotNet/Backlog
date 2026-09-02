using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The join a roadmap item's rollup is built from: its direct link plus everything
/// carrying its tag, as one de-duplicated list per source. Asserted over the pure
/// builder, without a store or a file.
/// </summary>
public class RoadmapItemRollupBuilderTests
{
    private static RoadmapItemDto Item(
        string tag,
        Guid? taskId = null,
        IReadOnlyList<string>? knowledgeRefs = null) =>
        new(
            Guid.NewGuid(),
            "Ship the thing",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31),
            PlanningPriority.Medium,
            [],
            Lane: null,
            TaskId: taskId,
            DependsOn: [],
            Tag: tag,
            KnowledgeRefs: knowledgeRefs);

    private static TaskItemDto Entry(Guid id, string title, string[] tags, int? effort) =>
        new(
            id,
            title,
            Body: "",
            EntryType.Task,
            Priority.Medium,
            EntryStatus.Ready,
            Area: null,
            Tags: tags,
            Order: 0,
            TotalSubItems: 0,
            CompletedSubItems: 0,
            Projections: [],
            Effort: effort);

    private static KnowledgeGraphNode Chapter(string id, string label, int? effort, params string[] roadmap) =>
        new(id, label, effort, roadmap);

    [Fact]
    public void TheDirectEntryAndEveryTaggedEntry_ArriveAsOneList_WithTheirOrigins()
    {
        var linked = Guid.NewGuid();
        var item = Item("sync", taskId: linked);

        var rollup = RoadmapItemRollupBuilder.Build(
            item,
            [
                Entry(linked, "The linked one", tags: [], effort: 5),
                Entry(Guid.NewGuid(), "Carries the tag", tags: ["sync"], effort: 3),
                Entry(Guid.NewGuid(), "Unrelated", tags: ["other"], effort: 99)
            ],
            []);

        Assert.Collection(
            rollup.BacklogEntries,
            link =>
            {
                Assert.Equal("The linked one", link.Title);
                Assert.Equal(RollupOrigin.Direct, link.Origin);
            },
            link =>
            {
                Assert.Equal("Carries the tag", link.Title);
                Assert.Equal(RollupOrigin.Tag, link.Origin);
            });
    }

    [Fact]
    public void AnEntryBothLinkedAndTagged_IsCountedOnce_WearingBoth()
    {
        var id = Guid.NewGuid();
        var item = Item("sync", taskId: id);

        var rollup = RoadmapItemRollupBuilder.Build(
            item,
            [Entry(id, "Linked and tagged", tags: ["sync"], effort: 8)],
            []);

        var link = Assert.Single(rollup.BacklogEntries);
        Assert.Equal(RollupOrigin.Both, link.Origin);
        Assert.Equal(8, link.Effort);
    }

    [Fact]
    public void NullEffort_IsLeftOutOfTheSum_ButKeptInTheUnestimatedCount()
    {
        var item = Item("sync");

        var rollup = RoadmapItemRollupBuilder.Build(
            item,
            [
                Entry(Guid.NewGuid(), "Estimated", tags: ["sync"], effort: 5),
                Entry(Guid.NewGuid(), "Not estimated", tags: ["sync"], effort: null)
            ],
            []);

        Assert.Equal(5, rollup.TotalEffort);
        Assert.Equal(1, rollup.UnestimatedCount);
        Assert.Equal(2, rollup.GatheredCount);
    }

    [Fact]
    public void DirectKnowledgeRefsAndRoadmapTaggedChapters_ArriveAsOneList()
    {
        var item = Item("sync", knowledgeRefs: ["ops/runbook.md#restart"]);

        var rollup = RoadmapItemRollupBuilder.Build(
            item,
            [],
            [
                Chapter("ops/runbook.md#restart", "Restarting", 2),
                Chapter("arch/sync.md#design", "Sync design", 13, "sync"),
                Chapter("arch/other.md#x", "Unrelated", 1, "other")
            ]);

        Assert.Collection(
            rollup.KnowledgeChapters,
            link =>
            {
                Assert.Equal("Restarting", link.Title);
                Assert.Equal(RollupOrigin.Direct, link.Origin);
            },
            link =>
            {
                Assert.Equal("Sync design", link.Title);
                Assert.Equal(RollupOrigin.Tag, link.Origin);
            });
    }

    [Fact]
    public void AChapterBothReferencedAndRoadmapTagged_IsCountedOnce_WearingBoth()
    {
        var item = Item("sync", knowledgeRefs: ["arch/sync.md#design"]);

        var rollup = RoadmapItemRollupBuilder.Build(
            item,
            [],
            [Chapter("arch/sync.md#design", "Sync design", 13, "sync")]);

        var link = Assert.Single(rollup.KnowledgeChapters);
        Assert.Equal(RollupOrigin.Both, link.Origin);
        Assert.Equal(13, link.Effort);
    }

    [Fact]
    public void AnItemNothingPointsAt_GathersNothing()
    {
        var rollup = RoadmapItemRollupBuilder.Build(
            Item("sync"),
            [Entry(Guid.NewGuid(), "Unrelated", tags: ["other"], effort: 3)],
            [Chapter("arch/other.md#x", "Unrelated", 1, "other")]);

        Assert.True(rollup.IsEmpty);
    }
}
