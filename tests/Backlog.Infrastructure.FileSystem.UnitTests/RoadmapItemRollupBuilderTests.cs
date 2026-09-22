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
        IReadOnlyList<string>? knowledgeRefs = null,
        Guid? id = null) =>
        new(
            id ?? Guid.NewGuid(),
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

    private static TaskItemDto Entry(
        Guid id,
        string title,
        string[] tags,
        int? effort,
        EntryStatus status = EntryStatus.Ready,
        IReadOnlyList<string>? dependsOn = null,
        string? importItemId = null,
        string? importPlanId = null) =>
        new(
            id,
            title,
            Body: "",
            EntryType.Task,
            Priority.Medium,
            status,
            Area: null,
            Tags: tags,
            Order: 0,
            TotalSubItems: 0,
            CompletedSubItems: 0,
            Projections: [],
            DependsOn: dependsOn,
            Effort: effort,
            ImportPlanId: importPlanId,
            ImportItemId: importItemId);

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

    /// <summary>The backlog stores a tag that names a roadmap item as <c>+slug</c>;
    /// the plan holds the bare slug. Both forms roll up — the sigilled one because it
    /// is the stored form now, the bare one because it is what every entry filed
    /// before the sigil existed still carries.</summary>
    [Fact]
    public void AnEntryWearingThePlanSigil_AndOneWearingTheBareSlug_BothRollUp()
    {
        var item = Item("release-q4");

        var rollup = RoadmapItemRollupBuilder.Build(
            item,
            [
                Entry(Guid.NewGuid(), "Filed under the plan tag", tags: ["+release-q4"], effort: 3),
                Entry(Guid.NewGuid(), "Filed before the sigil", tags: ["release-q4"], effort: 2),
                Entry(Guid.NewGuid(), "Unrelated", tags: ["other"], effort: 99)
            ],
            []);

        Assert.Equal(
            ["Filed under the plan tag", "Filed before the sigil"],
            rollup.BacklogEntries.Select(link => link.Title));
        Assert.All(rollup.BacklogEntries, link => Assert.Equal(RollupOrigin.Tag, link.Origin));
        Assert.Equal(5, rollup.TotalEffort);
    }

    /// <summary>Only the plan sigil is lifted before the compare. A person tag that
    /// happens to spell the slug names somebody, and somebody is not a roadmap item.</summary>
    [Fact]
    public void APersonTagSpellingTheSlug_DoesNotRollUp()
    {
        var item = Item("release-q4");

        var rollup = RoadmapItemRollupBuilder.Build(
            item,
            [Entry(Guid.NewGuid(), "Assigned to a person called release-q4", tags: ["@release-q4"], effort: 3)],
            []);

        Assert.Empty(rollup.BacklogEntries);
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

    /// <summary>The backlog's five statuses read in the roadmap's four words. The
    /// pair worth stating is the last one: an archived entry has finished its work,
    /// and the roadmap has no colour for "finished, then filed away".</summary>
    [Theory]
    [InlineData(EntryStatus.Draft, RoadmapProgress.Planned)]
    [InlineData(EntryStatus.Ready, RoadmapProgress.Ready)]
    [InlineData(EntryStatus.InProgress, RoadmapProgress.InProgress)]
    [InlineData(EntryStatus.Done, RoadmapProgress.Done)]
    [InlineData(EntryStatus.Archived, RoadmapProgress.Done)]
    public void EachBacklogStatus_ArrivesAsItsRoadmapProgress(EntryStatus status, RoadmapProgress expected)
    {
        var rollup = RoadmapItemRollupBuilder.Build(
            Item("sync"),
            [Entry(Guid.NewGuid(), "A step", tags: ["+sync"], effort: 3, status: status)],
            []);

        Assert.Equal(expected, Assert.Single(rollup.BacklogEntries).Progress);
    }

    /// <summary>A knowledge chapter has no status to read, so it registers no
    /// progress rather than being called planned.</summary>
    [Fact]
    public void AKnowledgeChapter_RegistersNoProgress()
    {
        var rollup = RoadmapItemRollupBuilder.Build(
            Item("sync", knowledgeRefs: ["ops/runbook.md#restart"]),
            [],
            [Chapter("ops/runbook.md#restart", "Restarting", 2)]);

        var link = Assert.Single(rollup.KnowledgeChapters);
        Assert.Null(link.Progress);
        Assert.False(link.IsDone);
        Assert.Empty(link.Waits);
    }

    [Fact]
    public void TheDoneFigures_AreReadOffTheGatheredStatuses()
    {
        var rollup = RoadmapItemRollupBuilder.Build(
            Item("sync"),
            [
                Entry(Guid.NewGuid(), "Finished and sized", tags: ["+sync"], effort: 5, status: EntryStatus.Done),
                Entry(Guid.NewGuid(), "Finished, unsized", tags: ["+sync"], effort: null, status: EntryStatus.Archived),
                Entry(Guid.NewGuid(), "Still going", tags: ["+sync"], effort: 8, status: EntryStatus.InProgress)
            ],
            []);

        Assert.Equal(5, rollup.DoneEffort);
        Assert.Equal(2, rollup.DoneCount);
        Assert.Equal(13, rollup.TotalEffort);
        Assert.Equal(1, rollup.UnestimatedCount);
    }

    /// <summary>A step's <c>after:</c> chain survives into the roadmap as the keys of
    /// the things this same item gathered.</summary>
    [Fact]
    public void AStepsDependencies_ArriveAsTheGatheredKeysItWaitsOn()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var rollup = RoadmapItemRollupBuilder.Build(
            Item("sync"),
            [
                Entry(first, "First", tags: ["+sync"], effort: 1),
                Entry(second, "Second", tags: ["+sync"], effort: 1, dependsOn: [first.ToString()])
            ],
            []);

        Assert.Empty(rollup.BacklogEntries[0].Waits);
        Assert.Equal([first.ToString()], rollup.BacklogEntries[1].Waits);
    }

    /// <summary>A dependency on work outside the item is dropped. It still blocks the
    /// entry, but it is not an edge between two steps drawn in one bar, and a key
    /// naming no step would order nothing.</summary>
    [Fact]
    public void ADependencyOnSomethingTheItemDidNotGather_IsNotCarried()
    {
        var outsider = Guid.NewGuid();

        var rollup = RoadmapItemRollupBuilder.Build(
            Item("sync"),
            [
                Entry(Guid.NewGuid(), "Waits on other work", tags: ["+sync"], effort: 1, dependsOn: [outsider.ToString()]),
                Entry(outsider, "Another plan's work", tags: ["other"], effort: 1)
            ],
            []);

        Assert.Empty(Assert.Single(rollup.BacklogEntries).Waits);
    }

    /// <summary>A plan brought in over two sittings stores the second half's
    /// <c>after:</c> as the local <c>id:</c> it was written as. The chain is resolved
    /// against the store before it is filtered, so those edges survive instead of
    /// being silently dropped as keys that match nothing.</summary>
    [Fact]
    public void AnUnresolvedLocalId_IsResolvedAgainstTheStore_SoTheChainSurvives()
    {
        var first = Guid.NewGuid();

        var rollup = RoadmapItemRollupBuilder.Build(
            Item("sync"),
            [
                Entry(first, "First", tags: ["+sync"], effort: 1, importItemId: "add-command", importPlanId: "+sync"),
                Entry(
                    Guid.NewGuid(),
                    "Second",
                    tags: ["+sync"],
                    effort: 1,
                    dependsOn: ["add-command"],
                    importItemId: "wire-toolbar",
                    importPlanId: "+sync")
            ],
            []);

        Assert.Equal([first.ToString()], rollup.BacklogEntries[1].Waits);
    }

    /// <summary>The rule the whole rollup rests on, now that a link carries more than
    /// a title: an entry held by two threads is still one step, with one status and
    /// one chain, wearing <see cref="RollupOrigin.Both"/>.</summary>
    [Fact]
    public void AnEntryBothLinkedAndTagged_IsOneStep_KeepingItsStatusAndItsChain()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var rollup = RoadmapItemRollupBuilder.Build(
            Item("sync", taskId: second),
            [
                Entry(first, "First", tags: ["+sync"], effort: 2),
                Entry(
                    second,
                    "Linked and tagged",
                    tags: ["+sync"],
                    effort: 3,
                    status: EntryStatus.Done,
                    dependsOn: [first.ToString()])
            ],
            []);

        Assert.Equal(2, rollup.BacklogEntries.Count);

        var link = rollup.BacklogEntries[1];
        Assert.Equal(RollupOrigin.Both, link.Origin);
        Assert.Equal(RoadmapProgress.Done, link.Progress);
        Assert.Equal([first.ToString()], link.Waits);

        // Counted once by every figure, not twice.
        Assert.Equal(5, rollup.TotalEffort);
        Assert.Equal(3, rollup.DoneEffort);
        Assert.Equal(1, rollup.DoneCount);
        Assert.Equal(2, rollup.GatheredCount);
    }

    [Fact]
    public void BuildPlan_AnswersEveryItem_IncludingTheOnesThatGatheredNothing()
    {
        var gathers = Guid.NewGuid();
        var gathersNothing = Guid.NewGuid();

        var plan = new RoadmapPlanDto(
            [Item("sync", id: gathers), Item("nothing-here", id: gathersNothing)],
            [],
            []);

        var rollups = RoadmapItemRollupBuilder.BuildPlan(
            plan,
            [Entry(Guid.NewGuid(), "A step", tags: ["+sync"], effort: 5, status: EntryStatus.Done)],
            []);

        Assert.Equal(2, rollups.Count);
        Assert.Equal(5, rollups[gathers].DoneEffort);
        Assert.True(rollups[gathersNothing].IsEmpty);
    }

    /// <summary>A tag is not exclusive, so one entry may sit under two items of a
    /// plan. Each item's rollup names it; deciding which bar may draw it is a
    /// question for whoever draws them.</summary>
    [Fact]
    public void BuildPlan_LetsTwoItemsGatherTheSameEntry()
    {
        var entry = Guid.NewGuid();
        var byTag = Guid.NewGuid();
        var byLink = Guid.NewGuid();

        var plan = new RoadmapPlanDto(
            [Item("sync", id: byTag), Item("other", taskId: entry, id: byLink)],
            [],
            []);

        var rollups = RoadmapItemRollupBuilder.BuildPlan(
            plan,
            [Entry(entry, "Shared", tags: ["+sync"], effort: 3)],
            []);

        Assert.Equal(RollupOrigin.Tag, Assert.Single(rollups[byTag].BacklogEntries).Origin);
        Assert.Equal(RollupOrigin.Direct, Assert.Single(rollups[byLink].BacklogEntries).Origin);
    }

    /// <summary>An item drawn from the same sources through either entry point reads
    /// the same — the plan build is one read, not a second set of rules.</summary>
    [Fact]
    public void BuildPlan_AgreesWithBuild_ForTheSameItem()
    {
        var id = Guid.NewGuid();
        var item = Item("sync", id: id);
        IReadOnlyList<TaskItemDto> backlog =
        [
            Entry(Guid.NewGuid(), "Done", tags: ["+sync"], effort: 5, status: EntryStatus.Done),
            Entry(Guid.NewGuid(), "Doing", tags: ["+sync"], effort: null, status: EntryStatus.InProgress)
        ];

        var one = RoadmapItemRollupBuilder.Build(item, backlog, []);
        var plan = RoadmapItemRollupBuilder.BuildPlan(new RoadmapPlanDto([item], [], []), backlog, []);

        // Member by member rather than record equality: the two lists are equal in
        // content and are different objects, which is all a record compares them as.
        Assert.Equal(
            one.BacklogEntries.Select(link => (link.Key, link.Progress, link.Effort, link.Origin)),
            plan[id].BacklogEntries.Select(link => (link.Key, link.Progress, link.Effort, link.Origin)));
        Assert.Equal(one.TotalEffort, plan[id].TotalEffort);
        Assert.Equal(one.DoneEffort, plan[id].DoneEffort);
        Assert.Equal(one.DoneCount, plan[id].DoneCount);
        Assert.Equal(one.UnestimatedCount, plan[id].UnestimatedCount);
    }
}
