using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Services;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// A pulled copy that ticks off a repeating task runs Occurrence Spawning, as the
/// desktop's own tick does, and runs it once
/// (<c>.devbook/arc42/06-runtime-view.md#mobile-my-day-and-task-push</c>).
/// </summary>
public sealed class TaskReplicaMergeRecurrenceTests
{
    private static readonly Guid Phone = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Friday = new(2026, 9, 11);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_pulled_tick_of_a_repeating_task_spawns_its_next_occurrence()
    {
        var store = new InMemoryTaskStore();
        var local = Weekly(Noon, ticked: false);
        store.Seed(local);

        await new TaskReplicaMerge(store).ApplyAsync([Ticked(local.Id, Noon.AddMinutes(5))], Cancellation);

        var successor = Assert.Single(store.Tasks.Values, task => task.Id != local.Id);
        Assert.Equal(OccurrenceSpawning.SuccessorId(local.Id), successor.Id);
        Assert.Equal(local.Id, successor.RecurrenceSourceId);
        Assert.Equal(Friday.AddDays(7), successor.DueOn);
        Assert.False(successor.IsCompleted);
        Assert.Equal(new Recurrence(1, RecurrenceUnit.Week), successor.Recurrence);
    }

    /// <summary>A replayed page never reaches the spawn: the merge refuses the
    /// version it already holds. The successor's id is what stops the cases
    /// below, where the tick is written again.</summary>
    [Fact]
    public async Task A_replayed_page_spawns_nothing_more()
    {
        var store = new InMemoryTaskStore();
        var local = Weekly(Noon, ticked: false);
        store.Seed(local);
        var merge = new TaskReplicaMerge(store);
        var page = new[] { Ticked(local.Id, Noon.AddMinutes(5)) };

        await merge.ApplyAsync(page, Cancellation);
        await merge.ApplyAsync(page, Cancellation);

        Assert.Equal(2, store.Tasks.Count);
    }

    /// <summary>A second desktop that already pulled the successor the first one
    /// spawned does not make another when the tick reaches it afterwards: the
    /// successor's id is the one it would have made.</summary>
    [Fact]
    public async Task A_successor_already_pulled_is_not_spawned_again()
    {
        var store = new InMemoryTaskStore();
        var local = Weekly(Noon, ticked: false);
        store.Seed(local);
        var merge = new TaskReplicaMerge(store);

        var elsewhere = new InMemoryTaskStore();
        elsewhere.Seed(Weekly(Noon, ticked: false, local.Id));
        await new TaskReplicaMerge(elsewhere).ApplyAsync([Ticked(local.Id, Noon.AddMinutes(5))], Cancellation);
        var spawnedElsewhere = elsewhere.Tasks[OccurrenceSpawning.SuccessorId(local.Id)];

        await merge.ApplyAsync(
            [new TaskChangeRecord(TaskReplicaMerge.ToChange(spawnedElsewhere), Phone, 150)],
            Cancellation);
        await merge.ApplyAsync([Ticked(local.Id, Noon.AddMinutes(5))], Cancellation);

        Assert.Equal(2, store.Tasks.Count);
    }

    /// <summary>A task never seen here that arrives already ticked is a record of
    /// something done, not an occurrence finishing now — a fresh device pulling a
    /// whole history spawns nothing.</summary>
    [Fact]
    public async Task A_task_that_arrives_already_ticked_spawns_nothing()
    {
        var store = new InMemoryTaskStore();

        await new TaskReplicaMerge(store).ApplyAsync([Ticked(Guid.NewGuid(), Noon)], Cancellation);

        Assert.Single(store.Tasks);
    }

    [Fact]
    public async Task A_later_copy_of_a_task_ticked_here_already_spawns_nothing()
    {
        var store = new InMemoryTaskStore();
        var local = Weekly(Noon, ticked: true);
        store.Seed(local);

        await new TaskReplicaMerge(store).ApplyAsync([Ticked(local.Id, Noon.AddMinutes(5))], Cancellation);

        Assert.Single(store.Tasks);
    }

    [Fact]
    public async Task A_pulled_tick_of_a_task_without_a_repeat_spawns_nothing()
    {
        var store = new InMemoryTaskStore();
        var local = Weekly(Noon, ticked: false);
        store.Seed(local);
        var ticked = Weekly(Noon.AddMinutes(5), ticked: true, local.Id);
        ticked.SetRecurrence(null);
        ticked.LoadStamps(Noon.AddMinutes(5), null);

        await new TaskReplicaMerge(store).ApplyAsync(
            [new TaskChangeRecord(TaskReplicaMerge.ToChange(ticked), Phone, 200)],
            Cancellation);

        Assert.Single(store.Tasks);
    }

    /// <summary>An untick arriving after the spawn leaves the successor in place,
    /// as the desktop's own untick does: unticking clears the day and nothing
    /// else (<c>.devbook/domain/tasks/domain.md#completed</c>).</summary>
    [Fact]
    public async Task An_untick_after_the_spawn_leaves_the_successor_in_place()
    {
        var store = new InMemoryTaskStore();
        var local = Weekly(Noon, ticked: false);
        store.Seed(local);
        var merge = new TaskReplicaMerge(store);

        await merge.ApplyAsync([Ticked(local.Id, Noon.AddMinutes(5))], Cancellation);

        var unticked = Weekly(Noon.AddMinutes(10), ticked: false, local.Id);
        await merge.ApplyAsync([new TaskChangeRecord(TaskReplicaMerge.ToChange(unticked), Phone, 300)], Cancellation);

        Assert.False(store.Tasks[local.Id].IsCompleted);
        var successor = store.Tasks[OccurrenceSpawning.SuccessorId(local.Id)];
        Assert.Null(successor.DeletedAt);

        // And a second tick finds the successor and makes no other.
        await merge.ApplyAsync([Ticked(local.Id, Noon.AddMinutes(15), serverTimestamp: 400)], Cancellation);
        Assert.Equal(2, store.Tasks.Count);
    }

    private static TaskChangeRecord Ticked(Guid id, DateTimeOffset at, long serverTimestamp = 200) =>
        new(TaskReplicaMerge.ToChange(Weekly(at, ticked: true, id)), Phone, serverTimestamp);

    /// <summary>A weekly task due on Friday, stamped <paramref name="updatedAt"/>
    /// last so the setters' wall clock does not decide the merge.</summary>
    private static TaskItem Weekly(DateTimeOffset updatedAt, bool ticked, Guid? id = null)
    {
        var task = TaskChanges.Task("Weekly review", updatedAt, id);
        task.SetRecurrence(new Recurrence(1, RecurrenceUnit.Week));
        task.SetDueOn(Friday);
        if (ticked) task.SetCompletedOn(new DateOnly(2026, 9, 7));
        task.LoadStamps(updatedAt, null);

        return task;
    }
}
