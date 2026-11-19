using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// A Brain note an item gathered (local ADR 0053 §5): its effort counts toward the
/// item, it is never finished, and it never carries the item the way a task does.
/// </summary>
public class RoadmapRollupBrainNoteTests
{
    private static RoadmapGatheredLink Note(string key, int? effort, RollupOrigin origin = RollupOrigin.Tag, bool unresolved = false) =>
        new(key, key, effort, origin, UnresolvedBecause: unresolved ? RoadmapUnresolvedReason.NotInBrain : null);

    private static RoadmapGatheredLink Task(string key, int? effort, RoadmapProgress progress) =>
        new(key, key, effort, RollupOrigin.Tag, progress);

    [Fact]
    public void ABrainNotesEffort_CountsInTheTotal_AndAnUnsizedOne_InTheUnestimated()
    {
        var rollup = new RoadmapItemRollupDto(
            [Task("t1", 3, RoadmapProgress.Ready)],
            [],
            [Note("#brain:projects/sync.md", 5), Note("#brain:projects/later.md", null, unresolved: true)]);

        Assert.Equal(8, rollup.TotalEffort);
        Assert.Equal(1, rollup.UnestimatedCount);
        Assert.Equal(3, rollup.GatheredCount);
        Assert.False(rollup.IsEmpty);
    }

    [Fact]
    public void ABrainNote_IsNeverDone_SoItNeverFinishesTheItem()
    {
        var rollup = new RoadmapItemRollupDto(
            [Task("t1", 3, RoadmapProgress.Done)],
            [],
            [Note("#brain:projects/sync.md", 5)]);

        Assert.All(rollup.BrainNotes, note => Assert.False(note.IsDone));
        Assert.Equal(1, rollup.DoneCount);
        Assert.Equal(3, rollup.DoneEffort);
        // Finished is read off the tasks alone: the note neither blocks nor completes it.
        Assert.True(rollup.IsFinished);
    }

    [Fact]
    public void AnItemThatGatheredOnlyBrainNotes_HasNoTasks_AndIsNotFinished()
    {
        var rollup = new RoadmapItemRollupDto([], [], [Note("#brain:projects/sync.md", 5)]);

        Assert.False(RoadmapItemCompletion.HasTasks(rollup));
        Assert.False(rollup.IsFinished);
        Assert.Equal(0, rollup.DoneCount);
    }

    [Fact]
    public void ARollupWrittenBeforeBrainNotes_GathersNone()
    {
        var rollup = new RoadmapItemRollupDto([], []);

        Assert.Empty(rollup.BrainNotes);
        Assert.Empty(RoadmapItemRollupDto.Empty.BrainNotes);
    }
}
