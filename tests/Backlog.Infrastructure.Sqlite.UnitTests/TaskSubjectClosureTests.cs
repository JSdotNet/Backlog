using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Tests;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// The Tasks context closing the notifications about a task when it ends — marked Done,
/// archived or deleted — and answering the startup probe about it
/// (<c>.devbook/domain/notifications/domain.md#subject</c>). The task store is the one
/// place every write passes, so it is where the end is noticed.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class TaskSubjectClosureTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 22);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-task-subjects", Guid.NewGuid().ToString("n"));
    private readonly RecordingNotifications _notifications = new RecordingNotifications();

    [Fact]
    public async Task Marking_a_task_done_closes_the_notifications_about_it()
    {
        var repository = Repository();
        var task = await SavedAsync(repository, "Answer the reviewer");

        task.SetStatus(EntryStatus.Done, Today);
        await repository.SaveAsync(task, TestContext.Current.CancellationToken);

        var closed = Assert.Single(_notifications.Closed);
        Assert.Equal(new NotificationSubject("tasks", "task", task.Id.ToString("D")), closed.Subject);
        Assert.Equal("Task done", closed.Reason);
    }

    [Fact]
    public async Task Deleting_or_archiving_a_task_closes_them_too_and_saving_it_again_closes_nothing_more()
    {
        var repository = Repository();
        var deleted = await SavedAsync(repository, "Deleted");
        var archived = await SavedAsync(repository, "Archived");

        deleted.MarkDeleted();
        archived.SetStatus(EntryStatus.Archived, Today);
        await repository.SaveManyAsync([deleted, archived], TestContext.Current.CancellationToken);
        await repository.SaveManyAsync([deleted, archived], TestContext.Current.CancellationToken);

        Assert.Equal(
            [deleted.Id.ToString("D"), archived.Id.ToString("D")],
            _notifications.Closed.Select(closed => closed.Subject.Id));
    }

    [Fact]
    public async Task A_task_that_comes_back_closes_nothing_and_ending_it_again_closes_again()
    {
        // The reopen itself reopens nothing — Notifications keeps a closed notification
        // closed — and the next end is a new end for whatever was published since.
        var repository = Repository();
        var task = await SavedAsync(repository, "Comes back");
        task.SetStatus(EntryStatus.Done, Today);
        await repository.SaveAsync(task, TestContext.Current.CancellationToken);

        task.SetStatus(EntryStatus.Ready, Today);
        await repository.SaveAsync(task, TestContext.Current.CancellationToken);
        Assert.Single(_notifications.Closed);

        task.SetStatus(EntryStatus.Done, Today);
        await repository.SaveAsync(task, TestContext.Current.CancellationToken);
        Assert.Equal(2, _notifications.Closed.Count);
    }

    [Fact]
    public async Task A_task_new_to_this_device_or_still_open_closes_nothing()
    {
        var repository = Repository();
        var arrivedDone = new TaskItem("Done elsewhere first", string.Empty, EntryType.Task);
        arrivedDone.SetStatus(EntryStatus.Done, Today);
        var progressing = await SavedAsync(repository, "Still going");
        progressing.SetStatus(EntryStatus.InProgress, Today);

        await repository.SaveManyAsync([arrivedDone, progressing], TestContext.Current.CancellationToken);

        Assert.Empty(_notifications.Closed);
    }

    [Fact]
    public async Task The_write_lands_when_closing_fails_or_no_notifications_are_composed()
    {
        _notifications.Fails = new InvalidOperationException("locked");
        var failing = Repository();
        var absent = new RootedSqliteTaskRepository(() => _root, notifications: () => null);
        var task = await SavedAsync(failing, "Lands anyway");

        task.SetStatus(EntryStatus.Done, Today);
        await failing.SaveAsync(task, TestContext.Current.CancellationToken);

        Assert.Equal(EntryStatus.Done, (await absent.GetAsync(task.Id, TestContext.Current.CancellationToken))!.Status);
    }

    [Fact]
    public async Task The_probe_calls_a_task_live_only_while_it_is_there_and_not_done()
    {
        var repository = Repository();
        var open = await SavedAsync(repository, "Open");
        var done = await SavedAsync(repository, "Done");
        var deleted = await SavedAsync(repository, "Deleted");
        done.SetStatus(EntryStatus.Done, Today);
        deleted.MarkDeleted();
        await repository.SaveManyAsync([done, deleted], TestContext.Current.CancellationToken);
        var probe = new TaskSubjectProbe(repository);

        Assert.Equal("tasks", probe.Context);
        Assert.True(await probe.IsLiveAsync(Subject(open.Id), TestContext.Current.CancellationToken));
        Assert.False(await probe.IsLiveAsync(Subject(done.Id), TestContext.Current.CancellationToken));
        Assert.False(await probe.IsLiveAsync(Subject(deleted.Id), TestContext.Current.CancellationToken));
        Assert.False(await probe.IsLiveAsync(Subject(Guid.NewGuid()), TestContext.Current.CancellationToken));
        Assert.True(await probe.IsLiveAsync(new NotificationSubject("tasks", "task", "not-a-guid"), TestContext.Current.CancellationToken));
        Assert.True(await probe.IsLiveAsync(new NotificationSubject("tasks", "goal", open.Id.ToString("D")), TestContext.Current.CancellationToken));
    }

    private RootedSqliteTaskRepository Repository() => new(() => _root, notifications: () => _notifications);

    private static async Task<TaskItem> SavedAsync(RootedSqliteTaskRepository repository, string title)
    {
        var task = new TaskItem(title, string.Empty, EntryType.Task);
        await repository.SaveAsync(task, TestContext.Current.CancellationToken);
        return task;
    }

    private static NotificationSubject Subject(Guid id) => new("tasks", "task", id.ToString("D"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
