namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// A <c>sprint-series</c> document an older desktop left on the replica is read and
/// dropped (local ADR 0035): counted as nothing to do, never written, never a task, and
/// never logged as received — on a head with Progress or without it.
/// </summary>
public sealed class TaskReplicaMergeRetiredTests
{
    private static readonly Guid OtherPc = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Morning = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private const string SeriesJson = """{"version":1,"name":"Platform","sprints":[]}""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static Backlog.Modules.Sync.Abstractions.DataTransferObjects.TaskChange Series() =>
        WholeDocumentChange.Of(Guid.NewGuid(), "Platform", RetiredReplicaDocuments.SprintSeriesType, SeriesJson, Morning);

    [Fact]
    public async Task A_sprint_series_is_dropped_and_never_reaches_the_task_store()
    {
        var tasks = new InMemoryTaskStore();
        var activity = new SyncActivityLog();

        var merged = await new TaskReplicaMerge(tasks, activity: activity)
            .ApplyAsync([TaskChanges.Record(Series(), OtherPc, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(0, 0), merged);
        Assert.Empty(tasks.Tasks);
        Assert.Empty(tasks.Writes);
        Assert.Empty(activity.Snapshot());
    }

    [Fact]
    public async Task A_sprint_series_beside_a_task_leaves_the_task_applied()
    {
        var tasks = new InMemoryTaskStore();
        var task = TaskChanges.Record(TaskChanges.Change("Ship it", Morning), OtherPc, 101);

        var merged = await new TaskReplicaMerge(tasks)
            .ApplyAsync([TaskChanges.Record(Series(), OtherPc, 100), task], Cancellation);

        Assert.Equal(new TaskMergeOutcome(1, 0), merged);
        Assert.Single(tasks.Tasks);
    }
}
