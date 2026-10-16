using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The My Day preferences document is not a task (local ADR 0032, §3). It goes to the
/// preferences port whole — its text and its stamp — before any task is read, never to
/// the task store, and how it is counted follows what the port said.
/// </summary>
public sealed class TaskReplicaMergeMyDayTests
{
    private static readonly Guid OtherPc = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Morning = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    private const string PreferencesJson = """{"updatedAt":"2026-10-15T08:00:00Z","clearsOvernight":false}""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static MyDayPreferencesCopyDto Copy(DateTimeOffset? at = null) => new(PreferencesJson, at ?? Morning);

    [Fact]
    public async Task The_preferences_go_to_the_port_whole_and_never_to_the_task_store()
    {
        var tasks = new InMemoryTaskStore();
        var myDay = new RecordingMyDayPreferencesReplication();
        var sent = Copy();

        var merged = await new TaskReplicaMerge(tasks, myDay: myDay)
            .ApplyAsync([TaskChanges.Record(MyDayReplicaDocuments.ToChange(sent), OtherPc, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(1, 0), merged);
        Assert.Empty(tasks.Tasks);
        Assert.Empty(tasks.Writes);
        Assert.Equal(sent, Assert.Single(myDay.Offered));
    }

    [Fact]
    public async Task Taken_preferences_are_recorded_as_received()
    {
        var activity = new SyncActivityLog();
        var sent = Copy();

        await new TaskReplicaMerge(new InMemoryTaskStore(), activity: activity, myDay: new RecordingMyDayPreferencesReplication())
            .ApplyAsync([TaskChanges.Record(MyDayReplicaDocuments.ToChange(sent), OtherPc, 100)], Cancellation);

        var entry = Assert.Single(activity.Snapshot());
        Assert.Equal(SyncDirection.Received, entry.Direction);
        Assert.Equal(SyncItemKind.MyDayPreferences, entry.Kind);
        Assert.Equal(MyDayReplicaDocuments.PreferencesId.ToString("D"), entry.Id);
        Assert.Equal("My Day preferences", entry.Title);
    }

    [Theory]
    [InlineData(MyDayPreferencesReplicaOutcome.Echo)]
    [InlineData(MyDayPreferencesReplicaOutcome.Refused)]
    public async Task An_echo_or_an_older_copy_counts_as_nothing_applied(MyDayPreferencesReplicaOutcome answer)
    {
        var myDay = new RecordingMyDayPreferencesReplication { Answer = answer };
        var activity = new SyncActivityLog();

        var merged = await new TaskReplicaMerge(new InMemoryTaskStore(), activity: activity, myDay: myDay)
            .ApplyAsync([TaskChanges.Record(MyDayReplicaDocuments.ToChange(Copy()), OtherPc, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(0, 0), merged);
        Assert.Empty(activity.Snapshot());
    }

    [Fact]
    public async Task A_copy_the_port_cannot_read_counts_skipped()
    {
        var myDay = new RecordingMyDayPreferencesReplication { Answer = MyDayPreferencesReplicaOutcome.Unreadable };

        var merged = await new TaskReplicaMerge(new InMemoryTaskStore(), myDay: myDay)
            .ApplyAsync([TaskChanges.Record(MyDayReplicaDocuments.ToChange(Copy()), OtherPc, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(0, 1), merged);
    }

    /// <summary>ADR 0032 Verification 9: an older desktop build — a merge without the
    /// port — skips the kind rather than turning it into a draft task, and the page,
    /// and so the cursor, moves on.</summary>
    [Fact]
    public async Task Without_the_port_the_preferences_are_skipped_and_never_a_task()
    {
        var tasks = new InMemoryTaskStore();
        var task = TaskChanges.Change("Plan the day", Morning);

        var merged = await new TaskReplicaMerge(tasks)
            .ApplyAsync(
                [
                    TaskChanges.Record(MyDayReplicaDocuments.ToChange(Copy()), OtherPc, 100),
                    TaskChanges.Record(task, OtherPc, 101),
                ],
                Cancellation);

        Assert.Equal(new TaskMergeOutcome(1, 1), merged);
        Assert.Equal(task.Id, Assert.Single(tasks.Writes));
    }
}
