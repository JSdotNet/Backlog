using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Replication;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The two single settings documents are not tasks — the iteration settings (local ADR
/// 0036, §4) and the My Day preferences (local ADR 0032, §3). Each goes to its own port
/// whole — its text and its stamp — before any task is read, never to the task store, and
/// how it is counted follows what the port said.
/// </summary>
public sealed class TaskReplicaMergeSettingsTests
{
    private static readonly Guid OtherPc = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Morning = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>One settings document as the merge meets it: the change it travels as, the
    /// merge composed with a recording port for it, and what the activity log calls it.</summary>
    private sealed record Setting(
        object Sent,
        TaskChange Change,
        Func<InMemoryTaskStore, SyncActivityLog?, ReplicaOutcome, (TaskReplicaMerge Merge, IReadOnlyList<object> Offered)> Compose,
        SyncItemKind Kind,
        Guid Id,
        string Title);

    private static Setting Of(string kind)
    {
        switch (kind)
        {
            case "iteration-settings":
            {
                var sent = new IterationSettingsCopyDto(
                    """{"updatedAt":"2026-10-20T08:00:00Z","length":"two-weeks","twoWeekStart":"2026-10-19"}""", Morning);

                return new Setting(
                    sent,
                    IterationReplicaDocuments.ToChange(sent),
                    (tasks, activity, answer) =>
                    {
                        var port = new RecordingIterationSettingsReplication { Answer = answer };
                        return (new TaskReplicaMerge(tasks, activity: activity, iteration: port), port.Offered);
                    },
                    SyncItemKind.IterationSettings,
                    IterationReplicaDocuments.SettingsId,
                    "Iteration settings");
            }

            case "my-day-preferences":
            {
                var sent = new MyDayPreferencesCopyDto("""{"updatedAt":"2026-10-15T08:00:00Z","clearsOvernight":false}""", Morning);

                return new Setting(
                    sent,
                    MyDayReplicaDocuments.ToChange(sent),
                    (tasks, activity, answer) =>
                    {
                        var port = new RecordingMyDayPreferencesReplication { Answer = answer };
                        return (new TaskReplicaMerge(tasks, activity: activity, myDay: port), port.Offered);
                    },
                    SyncItemKind.MyDayPreferences,
                    MyDayReplicaDocuments.PreferencesId,
                    "My Day preferences");
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    public static TheoryData<string> Kinds => ["iteration-settings", "my-day-preferences"];

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task The_document_goes_to_its_port_whole_and_never_to_the_task_store(string kind)
    {
        var setting = Of(kind);
        var tasks = new InMemoryTaskStore();
        var (merge, offered) = setting.Compose(tasks, null, ReplicaOutcome.Taken);

        var merged = await merge.ApplyAsync([TaskChanges.Record(setting.Change, OtherPc, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(1, 0), merged);
        Assert.Empty(tasks.Tasks);
        Assert.Empty(tasks.Writes);
        Assert.Equal(setting.Sent, Assert.Single(offered));
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
                    TaskChanges.Record(Of("iteration-settings").Change, OtherPc, 100),
                    TaskChanges.Record(MyDayReplicaDocuments.ToChange(new MyDayPreferencesCopyDto("{}", Morning)), OtherPc, 101),
                ],
                Cancellation);

        Assert.Single(iteration.Offered);
        Assert.Single(myDay.Offered);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_taken_document_is_recorded_as_received(string kind)
    {
        var setting = Of(kind);
        var activity = new SyncActivityLog();
        var (merge, _) = setting.Compose(new InMemoryTaskStore(), activity, ReplicaOutcome.Taken);

        await merge.ApplyAsync([TaskChanges.Record(setting.Change, OtherPc, 100)], Cancellation);

        var entry = Assert.Single(activity.Snapshot());
        Assert.Equal(SyncDirection.Received, entry.Direction);
        Assert.Equal(setting.Kind, entry.Kind);
        Assert.Equal(setting.Id.ToString("D"), entry.Id);
        Assert.Equal(setting.Title, entry.Title);
    }

    public static TheoryData<string, ReplicaOutcome> KindsAndStaleAnswers => new()
    {
        { "iteration-settings", ReplicaOutcome.Echo },
        { "iteration-settings", ReplicaOutcome.Refused },
        { "my-day-preferences", ReplicaOutcome.Echo },
        { "my-day-preferences", ReplicaOutcome.Refused },
    };

    [Theory]
    [MemberData(nameof(KindsAndStaleAnswers))]
    public async Task An_echo_or_an_older_copy_counts_as_nothing_applied(string kind, ReplicaOutcome answer)
    {
        var setting = Of(kind);
        var activity = new SyncActivityLog();
        var (merge, _) = setting.Compose(new InMemoryTaskStore(), activity, answer);

        var merged = await merge.ApplyAsync([TaskChanges.Record(setting.Change, OtherPc, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(0, 0), merged);
        Assert.Empty(activity.Snapshot());
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_copy_the_port_cannot_read_counts_skipped(string kind)
    {
        var setting = Of(kind);
        var (merge, _) = setting.Compose(new InMemoryTaskStore(), null, ReplicaOutcome.Unreadable);

        var merged = await merge.ApplyAsync([TaskChanges.Record(setting.Change, OtherPc, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(0, 1), merged);
    }

    /// <summary>ADR 0036 Verification 7 and ADR 0032 Verification 9: an older desktop build
    /// — a merge without the port — skips the kind rather than turning it into a draft
    /// task, and the page, and so the cursor, moves on.</summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Without_the_port_the_document_is_skipped_and_never_a_task(string kind)
    {
        var tasks = new InMemoryTaskStore();
        var task = TaskChanges.Change("Plan the iteration", Morning);

        var merged = await new TaskReplicaMerge(tasks)
            .ApplyAsync(
                [
                    TaskChanges.Record(Of(kind).Change, OtherPc, 100),
                    TaskChanges.Record(task, OtherPc, 101),
                ],
                Cancellation);

        Assert.Equal(new TaskMergeOutcome(1, 1), merged);
        Assert.Equal(task.Id, Assert.Single(tasks.Writes));
    }
}
