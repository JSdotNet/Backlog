using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// A roadmap item's Brain notes (local ADR 0053 §1, §3 and §5): a Knowledge Ref written
/// <c>#brain:&lt;path&gt;</c> resolves from this device's Brain alone and reads as
/// unresolved everywhere else, and gathering by tag reaches the notes whose
/// <c>roadmap</c> list names the item's tag. Never matched against a repository's
/// devbook.
/// </summary>
public class RoadmapItemRollupBuilderBrainTests
{
    private static RoadmapItemDto Item(string tag, IReadOnlyList<string>? knowledgeRefs = null, Guid? id = null) =>
        new(
            id ?? Guid.NewGuid(),
            "Ship the thing",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31),
            PlanningPriority.Medium,
            [],
            Lane: null,
            TaskId: null,
            DependsOn: [],
            Tag: tag,
            KnowledgeRefs: knowledgeRefs);

    private static RoadmapBrainNote Note(string path, string title, int? effort, string? slug = null, string? titleSlug = null, params string[] roadmap) =>
        new(path, slug, title, effort, roadmap, titleSlug);

    private static TaskItemDto Entry(string title, string tag, int? effort, EntryStatus status) =>
        new(Guid.NewGuid(), title, "", EntryType.Task, Priority.Medium, status, null, [tag], 0, 0, 0, [], Effort: effort);

    [Fact]
    public void ADirectBrainRef_ResolvesForTheOwner_ToTheNotesTitleAndEffort()
    {
        var rollup = RoadmapItemRollupBuilder.Build(
            Item("sync", ["#brain:projects/sync.md"]),
            [],
            [],
            brainNotes: [Note("projects/sync.md", "Sync rework", 5)]);

        Assert.Empty(rollup.KnowledgeChapters);
        var note = Assert.Single(rollup.BrainNotes);
        Assert.Equal("#brain:projects/sync.md", note.Key);
        Assert.Equal("Sync rework", note.Title);
        Assert.Equal(5, note.Effort);
        Assert.Equal(RollupOrigin.Direct, note.Origin);
        Assert.Null(note.Progress);
        Assert.False(note.Unresolved);
    }

    [Fact]
    public void ADirectBrainRef_ToAChapterOfANote_ResolvesToThatChapter()
    {
        var rollup = RoadmapItemRollupBuilder.Build(
            Item("sync", ["#Brain:projects\\sync.md#later"]),
            [],
            [],
            brainNotes: [Note("projects/sync.md", "Sync rework", 5), Note("projects/sync.md", "Later", 2, slug: "later")]);

        var note = Assert.Single(rollup.BrainNotes);
        Assert.Equal("#brain:projects/sync.md#later", note.Key);
        Assert.Equal("Later", note.Title);
        Assert.Equal(2, note.Effort);
    }

    [Fact]
    public void ARefWrittenToANotesTopHeading_MeetsTheNoteItself()
    {
        var rollup = RoadmapItemRollupBuilder.Build(
            Item("sync", ["#brain:projects/sync.md#sync-rework"]),
            [],
            [],
            brainNotes: [Note("projects/sync.md", "Sync rework", 5, titleSlug: "sync-rework", roadmap: "sync")]);

        // One note, reached both ways, under the note's own key.
        var note = Assert.Single(rollup.BrainNotes);
        Assert.Equal("#brain:projects/sync.md", note.Key);
        Assert.Equal(RollupOrigin.Both, note.Origin);
    }

    [Fact]
    public void WithNoBrain_ADirectBrainRef_IsStillListed_Unresolved_ByItsAddress()
    {
        var rollup = RoadmapItemRollupBuilder.Build(Item("sync", ["#brain:projects/sync.md"]), [], [], brainNotes: null);

        var note = Assert.Single(rollup.BrainNotes);
        Assert.Equal("#brain:projects/sync.md", note.Title);
        Assert.Null(note.Effort);
        Assert.True(note.Unresolved);
        Assert.Equal(RoadmapUnresolvedReason.NoBrain, note.UnresolvedBecause);
        Assert.Equal(1, rollup.UnestimatedCount);
    }

    /// <summary>Why a ref is unresolved travels with it, so the reader is told the true
    /// reason: no Brain readable on this device, a Brain that holds no such note — an
    /// empty Brain included — or a reference that is no Brain address at all, which no
    /// Brain could answer.</summary>
    [Fact]
    public void AnUnresolvedRef_SaysWhy_NoBrain_NotInThisBrain_OrNotAnAddress()
    {
        var item = Item("sync", ["#brain:projects/gone.md", "#brain:../outside.md"]);

        IReadOnlyList<RoadmapUnresolvedReason?> Reasons(IReadOnlyList<RoadmapBrainNote>? notes) =>
            [.. RoadmapItemRollupBuilder.Build(item, [], [], brainNotes: notes).BrainNotes.Select(note => note.UnresolvedBecause)];

        Assert.Equal([RoadmapUnresolvedReason.NoBrain, RoadmapUnresolvedReason.NotAnAddress], Reasons(null));
        Assert.Equal([RoadmapUnresolvedReason.NotInBrain, RoadmapUnresolvedReason.NotAnAddress], Reasons([]));
        Assert.Equal(
            [RoadmapUnresolvedReason.NotInBrain, RoadmapUnresolvedReason.NotAnAddress],
            Reasons([Note("projects/sync.md", "Sync rework", 5)]));
    }

    [Fact]
    public void AResolvedRef_CarriesNoReason()
    {
        var rollup = RoadmapItemRollupBuilder.Build(
            Item("sync", ["#brain:projects/sync.md"]),
            [],
            [],
            brainNotes: [Note("projects/sync.md", "Sync rework", 5)]);

        Assert.Null(Assert.Single(rollup.BrainNotes).UnresolvedBecause);
    }

    [Fact]
    public void ANoteMissingFromASetBrain_ReadsUnresolved_AsNoBrainDoes()
    {
        var rollup = RoadmapItemRollupBuilder.Build(
            Item("sync", ["#brain:projects/gone.md", "#brain:../outside.md"]),
            [],
            [],
            brainNotes: [Note("projects/sync.md", "Sync rework", 5)]);

        Assert.Equal(["#brain:projects/gone.md", "#brain:../outside.md"], rollup.BrainNotes.Select(note => note.Title));
        Assert.All(rollup.BrainNotes, note => Assert.True(note.Unresolved));
        Assert.All(rollup.BrainNotes, note => Assert.Null(note.Effort));
    }

    [Fact]
    public void ABrainRef_IsNeverLookedUpInTheRepositoryGraph_EvenWhenANodeSharesItsId()
    {
        var rollup = RoadmapItemRollupBuilder.Build(
            Item("sync", ["#brain:projects/sync.md"]),
            [],
            [new KnowledgeGraphNode("#brain:projects/sync.md", "A repository chapter", 13, ["sync"])],
            brainNotes: []);

        // The graph node is a repository chapter carrying the tag: it is gathered by
        // the tag as a chapter, and the Brain ref stays the Brain's — unresolved here.
        var chapter = Assert.Single(rollup.KnowledgeChapters);
        Assert.Equal(RollupOrigin.Tag, chapter.Origin);
        var note = Assert.Single(rollup.BrainNotes);
        Assert.True(note.Unresolved);
        Assert.Null(note.Effort);
    }

    [Fact]
    public void GatheringByTag_ReachesEveryNoteNamingTheTag_CaseInsensitively_OnceEach()
    {
        var rollup = RoadmapItemRollupBuilder.Build(
            Item("sync", ["#brain:projects/sync.md"]),
            [],
            [],
            brainNotes:
            [
                Note("projects/sync.md", "Sync rework", 5, roadmap: "sync"),
                Note("projects/sync.md", "Later", 2, slug: "later", roadmap: "SYNC"),
                Note("work/other.md", "Other", 8, roadmap: "other"),
                Note("work/quiet.md", "Quiet", null)
            ]);

        Assert.Collection(
            rollup.BrainNotes,
            note =>
            {
                Assert.Equal("#brain:projects/sync.md", note.Key);
                Assert.Equal(RollupOrigin.Both, note.Origin);
            },
            note =>
            {
                Assert.Equal("#brain:projects/sync.md#later", note.Key);
                Assert.Equal(RollupOrigin.Tag, note.Origin);
            });
        Assert.Equal(7, rollup.TotalEffort);
    }

    [Fact]
    public void WithNoBrain_GatheringByTag_GathersNoNote()
    {
        var rollup = RoadmapItemRollupBuilder.Build(Item("sync"), [], [], brainNotes: null);

        Assert.Empty(rollup.BrainNotes);
        Assert.True(rollup.IsEmpty);
    }

    [Fact]
    public void ATaggedNote_CountsInTheEffort_AndNeverFinishesTheItem()
    {
        var rollup = RoadmapItemRollupBuilder.Build(
            Item("sync"),
            [Entry("Done step", "+sync", 3, EntryStatus.Done)],
            [],
            brainNotes: [Note("projects/sync.md", "Sync rework", 5, roadmap: "sync"), Note("projects/later.md", "Later", null, roadmap: "sync")]);

        Assert.Equal(8, rollup.TotalEffort);
        Assert.Equal(1, rollup.UnestimatedCount);
        Assert.Equal(1, rollup.DoneCount);
        Assert.All(rollup.BrainNotes, note => Assert.False(note.IsDone));
        Assert.True(rollup.IsFinished);

        var onlyNotes = RoadmapItemRollupBuilder.Build(
            Item("sync"),
            [],
            [],
            brainNotes: [Note("projects/sync.md", "Sync rework", 5, roadmap: "sync")]);

        Assert.False(onlyNotes.IsFinished);
        Assert.False(RoadmapItemCompletion.HasTasks(onlyNotes));
        Assert.Equal(0, onlyNotes.DoneCount);
    }

    [Fact]
    public void BuildPlan_GathersBrainNotesPerItem_AsBuildDoes()
    {
        var syncId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var sync = Item("sync", ["#brain:work/missing.md"], id: syncId);
        var other = Item("other", ["#brain:projects/sync.md"], id: otherId);
        IReadOnlyList<RoadmapBrainNote> notes =
        [
            Note("projects/sync.md", "Sync rework", 5, roadmap: "sync"),
            Note("work/other.md", "Other", 8, roadmap: "other")
        ];

        var plan = RoadmapItemRollupBuilder.BuildPlan(new RoadmapPlanDto([sync, other], [], []), [], [], brainNotes: notes);

        foreach (var item in new[] { sync, other })
        {
            var one = RoadmapItemRollupBuilder.Build(item, [], [], brainNotes: notes);
            Assert.Equal(one.BrainNotes, plan[item.Id].BrainNotes);
            Assert.Equal(one.TotalEffort, plan[item.Id].TotalEffort);
            Assert.Equal(one.UnestimatedCount, plan[item.Id].UnestimatedCount);
        }

        Assert.Equal(2, plan[syncId].BrainNotes.Count);
        Assert.Equal(2, plan[otherId].BrainNotes.Count);
    }
}
