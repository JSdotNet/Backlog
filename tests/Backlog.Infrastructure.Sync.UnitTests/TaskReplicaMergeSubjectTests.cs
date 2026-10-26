using Backlog.Infrastructure.Sqlite;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Tests;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// A task ended on another device closes the notifications about it on the owner, the same
/// way a local end does: the merge writes the pulled copy through the task store every head
/// registers, and that store is where the end is noticed
/// (<c>.devbook/domain/notifications/domain.md#subject</c>).
/// </summary>
public sealed class TaskReplicaMergeSubjectTests : IDisposable
{
    private static readonly Guid Phone = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Noon = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-merge-subjects", Guid.NewGuid().ToString("n"));
    private readonly RecordingNotifications _notifications = new RecordingNotifications();

    [Fact]
    public async Task A_task_marked_done_on_the_phone_closes_its_notifications_on_the_owner()
    {
        var store = new RootedSqliteTaskRepository(() => _root, notifications: () => _notifications);
        var local = TaskChanges.Task("Answer the reviewer", Noon);
        await store.SaveAsync(local, TestContext.Current.CancellationToken);

        var done = TaskChanges.Task("Answer the reviewer", Noon.AddMinutes(5), local.Id);
        done.SetStatus(EntryStatus.Done, new DateOnly(2026, 10, 22));
        done.LoadStamps(Noon.AddMinutes(5), null);

        await new TaskReplicaMerge(store).ApplyAsync(
            [new TaskChangeRecord(TaskReplicaMerge.ToChange(done), Phone, 200)],
            TestContext.Current.CancellationToken);

        var closed = Assert.Single(_notifications.Closed);
        Assert.Equal(new NotificationSubject("tasks", "task", local.Id.ToString("D")), closed.Subject);
        Assert.Equal("Task done", closed.Reason);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
