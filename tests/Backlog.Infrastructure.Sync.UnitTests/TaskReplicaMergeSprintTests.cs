using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// A sprint series document is not a task. It goes to Progress's replication port whole —
/// its id, its text and its stamp — never to the task store, and how it is counted
/// follows what the port said.
/// </summary>
public sealed class TaskReplicaMergeSprintTests
{
    private static readonly Guid OtherPc = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Morning = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private const string SeriesJson = """{"version":1,"name":"Platform","sprints":[]}""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static SprintSeriesReplicaCopyDto Copy(string name = "Platform", DateTimeOffset? at = null) =>
        new(Guid.NewGuid(), name, SeriesJson, at ?? Morning);

    [Fact]
    public async Task A_series_goes_to_progress_whole_and_never_to_the_task_store()
    {
        var tasks = new InMemoryTaskStore();
        var sprints = new RecordingSprintSeriesReplication();
        var sent = Copy();

        var merged = await new TaskReplicaMerge(tasks, sprints: sprints)
            .ApplyAsync([TaskChanges.Record(SprintReplicaDocuments.ToChange(sent), OtherPc, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(1, 0), merged);
        Assert.Empty(tasks.Tasks);
        Assert.Empty(tasks.Writes);
        Assert.Equal(sent, Assert.Single(sprints.Offered));
    }

    [Fact]
    public async Task A_taken_series_is_recorded_as_received_by_its_name()
    {
        var sprints = new RecordingSprintSeriesReplication();
        var activity = new SyncActivityLog();
        var sent = Copy("Platform sprints");

        await new TaskReplicaMerge(new InMemoryTaskStore(), activity: activity, sprints: sprints)
            .ApplyAsync([TaskChanges.Record(SprintReplicaDocuments.ToChange(sent), OtherPc, 100)], Cancellation);

        var entry = Assert.Single(activity.Snapshot());
        Assert.Equal(SyncDirection.Received, entry.Direction);
        Assert.Equal(SyncItemKind.SprintSeries, entry.Kind);
        Assert.Equal(sent.Id.ToString("D"), entry.Id);
        Assert.Equal("Platform sprints", entry.Title);
    }

    [Theory]
    [InlineData(SprintSeriesReplicaOutcome.Echo)]
    [InlineData(SprintSeriesReplicaOutcome.Refused)]
    public async Task An_echo_or_an_older_copy_counts_as_nothing_applied(SprintSeriesReplicaOutcome answer)
    {
        var sprints = new RecordingSprintSeriesReplication();
        sprints.Answer = answer;
        var activity = new SyncActivityLog();

        var merged = await new TaskReplicaMerge(new InMemoryTaskStore(), activity: activity, sprints: sprints)
            .ApplyAsync([TaskChanges.Record(SprintReplicaDocuments.ToChange(Copy()), OtherPc, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(0, 0), merged);
        Assert.Empty(activity.Snapshot());
    }

    [Fact]
    public async Task A_copy_progress_cannot_read_counts_skipped()
    {
        var sprints = new RecordingSprintSeriesReplication();
        sprints.Answer = SprintSeriesReplicaOutcome.Unreadable;

        var merged = await new TaskReplicaMerge(new InMemoryTaskStore(), sprints: sprints)
            .ApplyAsync([TaskChanges.Record(SprintReplicaDocuments.ToChange(Copy()), OtherPc, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(0, 1), merged);
    }

    /// <summary>A head without Progress — or a build from before the port — skips the
    /// series rather than turning it into a draft task.</summary>
    [Fact]
    public async Task Without_the_progress_port_a_series_is_skipped_and_never_a_task()
    {
        var tasks = new InMemoryTaskStore();

        var merged = await new TaskReplicaMerge(tasks)
            .ApplyAsync([TaskChanges.Record(SprintReplicaDocuments.ToChange(Copy()), OtherPc, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(0, 1), merged);
        Assert.Empty(tasks.Writes);
    }
}
