using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The iteration settings document is not a task (local ADR 0036, §4). It goes to the
/// iteration settings port whole — its text and its stamp — before any task is read, never
/// to the task store, and how it is counted follows what the port said.
/// </summary>
public sealed class TaskReplicaMergeIterationTests
{
    private static readonly Guid OtherPc = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Morning = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    private const string SettingsJson = """{"updatedAt":"2026-10-20T08:00:00Z","length":"two-weeks","twoWeekStart":"2026-10-19"}""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static IterationSettingsCopyDto Copy(DateTimeOffset? at = null) => new(SettingsJson, at ?? Morning);

    [Fact]
    public async Task The_settings_go_to_the_port_whole_and_never_to_the_task_store()
    {
        var tasks = new InMemoryTaskStore();
        var iteration = new RecordingIterationSettingsReplication();
        var sent = Copy();

        var merged = await new TaskReplicaMerge(tasks, iteration: iteration)
            .ApplyAsync([TaskChanges.Record(IterationReplicaDocuments.ToChange(sent), OtherPc, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(1, 0), merged);
        Assert.Empty(tasks.Tasks);
        Assert.Empty(tasks.Writes);
        Assert.Equal(sent, Assert.Single(iteration.Offered));
    }

    /// <summary>The My Day preferences port never sees the iteration's document, nor the
    /// iteration's port the preferences'.</summary>
    [Fact]
    public async Task The_settings_and_the_my_day_preferences_each_reach_their_own_port()
    {
        var iteration = new RecordingIterationSettingsReplication();
        var myDay = new RecordingMyDayPreferencesReplication();

        await new TaskReplicaMerge(new InMemoryTaskStore(), myDay: myDay, iteration: iteration)
            .ApplyAsync(
                [
                    TaskChanges.Record(IterationReplicaDocuments.ToChange(Copy()), OtherPc, 100),
                    TaskChanges.Record(
                        MyDayReplicaDocuments.ToChange(new Backlog.Modules.Tasks.Abstractions.DataTransferObjects.MyDayPreferencesCopyDto("{}", Morning)),
                        OtherPc,
                        101),
                ],
                Cancellation);

        Assert.Single(iteration.Offered);
        Assert.Single(myDay.Offered);
    }

    [Fact]
    public async Task Taken_settings_are_recorded_as_received()
    {
        var activity = new SyncActivityLog();

        await new TaskReplicaMerge(new InMemoryTaskStore(), activity: activity, iteration: new RecordingIterationSettingsReplication())
            .ApplyAsync([TaskChanges.Record(IterationReplicaDocuments.ToChange(Copy()), OtherPc, 100)], Cancellation);

        var entry = Assert.Single(activity.Snapshot());
        Assert.Equal(SyncDirection.Received, entry.Direction);
        Assert.Equal(SyncItemKind.IterationSettings, entry.Kind);
        Assert.Equal(IterationReplicaDocuments.SettingsId.ToString("D"), entry.Id);
        Assert.Equal("Iteration settings", entry.Title);
    }

    [Theory]
    [InlineData(IterationSettingsReplicaOutcome.Echo)]
    [InlineData(IterationSettingsReplicaOutcome.Refused)]
    public async Task An_echo_or_an_older_copy_counts_as_nothing_applied(IterationSettingsReplicaOutcome answer)
    {
        var iteration = new RecordingIterationSettingsReplication { Answer = answer };
        var activity = new SyncActivityLog();

        var merged = await new TaskReplicaMerge(new InMemoryTaskStore(), activity: activity, iteration: iteration)
            .ApplyAsync([TaskChanges.Record(IterationReplicaDocuments.ToChange(Copy()), OtherPc, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(0, 0), merged);
        Assert.Empty(activity.Snapshot());
    }

    [Fact]
    public async Task A_copy_the_port_cannot_read_counts_skipped()
    {
        var iteration = new RecordingIterationSettingsReplication { Answer = IterationSettingsReplicaOutcome.Unreadable };

        var merged = await new TaskReplicaMerge(new InMemoryTaskStore(), iteration: iteration)
            .ApplyAsync([TaskChanges.Record(IterationReplicaDocuments.ToChange(Copy()), OtherPc, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(0, 1), merged);
    }

    /// <summary>ADR 0036 Verification 7: an older desktop build — a merge without the
    /// port — skips the kind rather than turning it into a draft task, and the page, and
    /// so the cursor, moves on.</summary>
    [Fact]
    public async Task Without_the_port_the_settings_are_skipped_and_never_a_task()
    {
        var tasks = new InMemoryTaskStore();
        var task = TaskChanges.Change("Plan the iteration", Morning);

        var merged = await new TaskReplicaMerge(tasks)
            .ApplyAsync(
                [
                    TaskChanges.Record(IterationReplicaDocuments.ToChange(Copy()), OtherPc, 100),
                    TaskChanges.Record(task, OtherPc, 101),
                ],
                Cancellation);

        Assert.Equal(new TaskMergeOutcome(1, 1), merged);
        Assert.Equal(task.Id, Assert.Single(tasks.Writes));
    }
}
