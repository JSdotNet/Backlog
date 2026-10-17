using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// A <c>goal</c> document is not a task (local ADR 0035 §9). It goes to Progress's goal
/// replication port whole — its id, its text and its stamp — never to the task store, and
/// how it is counted follows what the port said.
/// </summary>
public sealed class TaskReplicaMergeGoalTests
{
    private static readonly Guid OtherPc = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Morning = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    private const string GoalJson = """{"version":1,"title":"Customers get paid faster","dueDate":"2026-10-31","attachments":[]}""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static GoalReplicaCopyDto Copy(string title = "Customers get paid faster") =>
        new(Guid.NewGuid(), title, GoalJson, Morning);

    [Fact]
    public async Task A_goal_goes_to_progress_whole_and_never_to_the_task_store()
    {
        var tasks = new InMemoryTaskStore();
        var goals = new RecordingGoalReplication();
        var sent = Copy();

        var merged = await new TaskReplicaMerge(tasks, goals: goals)
            .ApplyAsync([TaskChanges.Record(GoalReplicaDocuments.ToChange(sent), OtherPc, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(1, 0), merged);
        Assert.Empty(tasks.Writes);
        Assert.Equal(sent, Assert.Single(goals.Offered));
    }

    [Fact]
    public async Task A_taken_goal_is_recorded_as_a_goal_received_by_its_title()
    {
        var goals = new RecordingGoalReplication();
        var activity = new SyncActivityLog();
        var sent = Copy("Devbook search ships");

        await new TaskReplicaMerge(new InMemoryTaskStore(), activity: activity, goals: goals)
            .ApplyAsync([TaskChanges.Record(GoalReplicaDocuments.ToChange(sent), OtherPc, 100)], Cancellation);

        var entry = Assert.Single(activity.Snapshot());
        Assert.Equal(SyncDirection.Received, entry.Direction);
        Assert.Equal(SyncItemKind.Goal, entry.Kind);
        Assert.Equal("Devbook search ships", entry.Title);
    }

    [Theory]
    [InlineData(GoalReplicaOutcome.Echo, 0, 0)]
    [InlineData(GoalReplicaOutcome.Refused, 0, 0)]
    [InlineData(GoalReplicaOutcome.Unreadable, 0, 1)]
    public async Task An_echo_an_older_copy_or_an_unreadable_one_counts_as_the_port_said(GoalReplicaOutcome answer, int applied, int skipped)
    {
        var goals = new RecordingGoalReplication { Answer = answer };

        var merged = await new TaskReplicaMerge(new InMemoryTaskStore(), goals: goals)
            .ApplyAsync([TaskChanges.Record(GoalReplicaDocuments.ToChange(Copy()), OtherPc, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(applied, skipped), merged);
    }

    /// <summary>A head without the goal port skips the document rather than turning it
    /// into a draft task.</summary>
    [Fact]
    public async Task Without_the_goal_port_a_goal_is_skipped_and_never_a_task()
    {
        var tasks = new InMemoryTaskStore();

        var merged = await new TaskReplicaMerge(tasks)
            .ApplyAsync([TaskChanges.Record(GoalReplicaDocuments.ToChange(Copy()), OtherPc, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(0, 1), merged);
        Assert.Empty(tasks.Writes);
    }

    [Fact]
    public void The_kind_token_is_goal_and_the_mark_is_keyed_per_goal()
    {
        var copy = Copy();
        var change = GoalReplicaDocuments.ToChange(copy);

        Assert.Equal("goal", change.Task.Type);
        Assert.Equal(GoalJson, change.Task.ContentMd);
        Assert.Equal(copy.Id, change.Id);
        Assert.Equal($"goal/{copy.Id:D}", GoalReplicaDocuments.MarkOf(copy.Id));
        Assert.Equal(copy, GoalReplicaDocuments.ToCopy(change));
    }
}
