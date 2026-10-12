using System.Net.Http.Json;

using Backlog.Infrastructure.Sqlite;
using Backlog.Infrastructure.Sync;
using Backlog.Mobile.UI.Outbox;
using Backlog.Mobile.UI.Services;
using Backlog.Mobile.UI.Tasks;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Services;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// A phone edit reaching the desktop's task, end to end
/// (.devbook/arc42/06-runtime-view.md#mobile-my-day-and-task-push).
/// <para>
/// Every piece is the real one: the desktop's SQLite task store and its sync
/// session push the task; the phone's task view pulls it into its own SQLite
/// file; the phone's edit service queues the edit in its SQLite outbox, which
/// posts it to this sync service; and the desktop's next pull merges it into its
/// store. Only the service's replica is the in-memory one, which keeps the same
/// whole-document last-write-wins the deployed one does.
/// </para>
/// </summary>
public sealed class PhoneTaskEditSyncTests : IDisposable
{
    private readonly SyncServiceFactory _service = new();

    private readonly string _root = Path.Combine(Path.GetTempPath(), "phone-task-edits-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _service.Dispose();

        // Best effort: the desktop's pooled connection may still hold its file,
        // and clearing every pool would reach into tests running beside this one.
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task Marking_a_task_done_on_the_phone_ticks_the_desktops_task()
    {
        await using var devices = await PairedAsync();
        var task = await devices.DesktopAddsTodaysTaskAsync();

        await devices.Phone.View.PullAsync(Cancellation);
        await devices.Phone.Edits.MarkDoneAsync(task.Id, Cancellation);
        await devices.Phone.Outbox.FlushAsync(Cancellation);

        Assert.Empty(devices.Phone.Outbox.Entries);

        var desktop = await devices.DesktopPullsAsync(task.Id);
        Assert.Equal(devices.Phone.View.Today, desktop.CompletedOn);
        Assert.Equal(EntryStatus.Done, desktop.Status);
        Assert.All(desktop.SubItems, step => Assert.Equal(SubItemStatus.Done, step.Status));
        Assert.Equal("Plan the trip", desktop.Title);
    }

    [Fact]
    public async Task Ticking_a_step_on_the_phone_ticks_it_in_the_desktops_task_and_its_text()
    {
        await using var devices = await PairedAsync();
        var task = await devices.DesktopAddsTodaysTaskAsync();

        await devices.Phone.View.PullAsync(Cancellation);
        var step = devices.Phone.View.Find(task.Id)!.Task.SubItems.OrderBy(s => s.Order).Last();
        await devices.Phone.Edits.SetStepDoneAsync(task.Id, step.Id, done: true, Cancellation);
        await devices.Phone.Outbox.FlushAsync(Cancellation);

        var desktop = await devices.DesktopPullsAsync(task.Id);
        Assert.Equal([SubItemStatus.Pending, SubItemStatus.Done], desktop.SubItems.OrderBy(s => s.Order).Select(s => s.Status));
        Assert.Equal([false, true], EntryTextParser.Parse("# t\n`!ready`\n\n" + desktop.ContentMd).SubItems.Select(s => s.Done));
        Assert.Null(desktop.CompletedOn);
    }

    [Fact]
    public async Task Moving_a_task_to_tomorrow_on_the_phone_moves_the_desktops_task()
    {
        await using var devices = await PairedAsync();
        var task = await devices.DesktopAddsTodaysTaskAsync();

        await devices.Phone.View.PullAsync(Cancellation);
        await devices.Phone.Edits.MoveToTomorrowAsync(task.Id, Cancellation);
        await devices.Phone.Outbox.FlushAsync(Cancellation);

        var desktop = await devices.DesktopPullsAsync(task.Id);
        Assert.Equal(devices.Phone.View.Today.AddDays(1), desktop.InMyDayOn);
    }

    [Fact]
    public async Task A_desktop_edit_made_after_the_phones_wins_the_task_whole()
    {
        await using var devices = await PairedAsync();
        var task = await devices.DesktopAddsTodaysTaskAsync();

        await devices.Phone.View.PullAsync(Cancellation);
        await devices.Phone.Edits.MarkDoneAsync(task.Id, Cancellation);
        await devices.Phone.Outbox.FlushAsync(Cancellation);

        // The desktop renames its own copy, not having pulled yet — a later
        // UpdatedAt than the phone's edit — and syncs.
        await Task.Delay(20, Cancellation);
        var mine = (await devices.DesktopTasks.GetAsync(task.Id, Cancellation))!;
        mine.Rename("Plan the trip to Ghent");
        await devices.DesktopTasks.SaveAsync(mine, Cancellation);
        Assert.True((await devices.DesktopSession.SyncAsync(Cancellation)).IsSuccess);

        await devices.Phone.View.PullAsync(Cancellation);

        var phone = devices.Phone.View.Find(task.Id)!;
        Assert.Equal("Plan the trip to Ghent", phone.Task.Title);
        Assert.Null(phone.Task.CompletedOn);
        Assert.Null((await devices.DesktopTasks.GetAsync(task.Id, Cancellation))!.CompletedOn);
    }

    /// <summary>
    /// A repeating task ticked off on the phone gets its next occurrence on the
    /// desktop, as the desktop's own tick gives it one
    /// (.devbook/domain/tasks/requirements.md#my-day).
    /// </summary>
    [Fact]
    public async Task Ticking_a_repeating_task_on_the_phone_gives_the_desktop_one_next_occurrence()
    {
        await using var devices = await PairedAsync();
        var task = await devices.DesktopAddsTodaysRepeatingTaskAsync();

        await devices.Phone.View.PullAsync(Cancellation);
        await devices.Phone.Edits.MarkDoneAsync(task.Id, Cancellation);
        await devices.Phone.Outbox.FlushAsync(Cancellation);

        var desktop = await devices.DesktopPullsAsync(task.Id);
        Assert.True(desktop.IsCompleted);

        var successor = Assert.Single(await devices.SuccessorsOfAsync(task.Id));
        Assert.Equal(OccurrenceSpawning.SuccessorId(task.Id), successor.Id);
        Assert.Equal(task.DueOn!.Value.AddDays(7), successor.DueOn);
        Assert.False(successor.IsCompleted);
        Assert.Equal(task.Recurrence, successor.Recurrence);

        // The successor is the desktop's own write, so its next sync sends it and
        // the phone receives it.
        Assert.True((await devices.DesktopSession.SyncAsync(Cancellation)).IsSuccess);
        await devices.Phone.View.PullAsync(Cancellation);
        Assert.NotNull(devices.Phone.View.Find(successor.Id));
    }

    /// <summary>
    /// A re-pull of the same documents makes no second successor: not the next
    /// pull, not the echo of the successor's own push, and not a pull that starts
    /// the feed over from the beginning.
    /// </summary>
    [Fact]
    public async Task Pulling_the_phones_tick_again_makes_no_second_occurrence()
    {
        await using var devices = await PairedAsync();
        var task = await devices.DesktopAddsTodaysRepeatingTaskAsync();

        await devices.Phone.View.PullAsync(Cancellation);
        await devices.Phone.Edits.MarkDoneAsync(task.Id, Cancellation);
        await devices.Phone.Outbox.FlushAsync(Cancellation);

        await devices.DesktopPullsAsync(task.Id);
        await devices.DesktopPullsAsync(task.Id);
        Assert.True((await devices.DesktopSession.SyncAsync(Cancellation)).IsSuccess);
        Assert.True((await devices.DesktopSession.SyncAsync(Cancellation)).IsSuccess);
        Assert.True((await FromTheStartOfTheFeed(devices).PullAsync(Cancellation)).IsSuccess);

        Assert.Single(await devices.SuccessorsOfAsync(task.Id));
    }

    /// <summary>
    /// Two desktops that each pull the phone's tick before the other's successor
    /// reaches them spawn the same task, so after the next syncs each holds one.
    /// </summary>
    [Fact]
    public async Task Two_desktops_that_see_the_phones_tick_hold_one_next_occurrence()
    {
        await using var devices = await PairedAsync();
        var task = await devices.DesktopAddsTodaysRepeatingTaskAsync();
        var laptop = await SecondDesktopAsync(devices);
        using var laptopHttp = laptop.Http;
        Assert.True((await laptop.Session.SyncAsync(Cancellation)).IsSuccess);
        Assert.NotNull(await laptop.Tasks.GetAsync(task.Id, Cancellation));

        await devices.Phone.View.PullAsync(Cancellation);
        await devices.Phone.Edits.MarkDoneAsync(task.Id, Cancellation);
        await devices.Phone.Outbox.FlushAsync(Cancellation);

        Assert.True((await devices.DesktopSession.PullAsync(Cancellation)).IsSuccess);
        Assert.True((await laptop.Session.PullAsync(Cancellation)).IsSuccess);

        for (var round = 0; round < 2; round++)
        {
            Assert.True((await devices.DesktopSession.SyncAsync(Cancellation)).IsSuccess);
            Assert.True((await laptop.Session.SyncAsync(Cancellation)).IsSuccess);
        }

        var onDesktop = Assert.Single(await devices.SuccessorsOfAsync(task.Id));
        var onLaptop = Assert.Single(await laptop.Tasks.ListAsync(Cancellation), t => t.RecurrenceSourceId == task.Id);
        Assert.Equal(onDesktop.Id, onLaptop.Id);
    }

    /// <summary>
    /// An untick that arrives after the spawn leaves the successor in place, as
    /// the desktop's own untick does (.devbook/domain/tasks/domain.md#completed).
    /// </summary>
    [Fact]
    public async Task Unticking_on_the_phone_after_the_spawn_leaves_the_next_occurrence()
    {
        await using var devices = await PairedAsync();
        var task = await devices.DesktopAddsTodaysRepeatingTaskAsync();

        await devices.Phone.View.PullAsync(Cancellation);
        await devices.Phone.Edits.MarkDoneAsync(task.Id, Cancellation);
        await devices.Phone.Outbox.FlushAsync(Cancellation);
        await devices.DesktopPullsAsync(task.Id);

        await devices.Phone.Edits.MarkUndoneAsync(task.Id, Cancellation);
        await devices.Phone.Outbox.FlushAsync(Cancellation);

        var desktop = await devices.DesktopPullsAsync(task.Id);
        Assert.False(desktop.IsCompleted);
        Assert.Single(await devices.SuccessorsOfAsync(task.Id));
    }

    private async Task<Devices> PairedAsync()
    {
        var desktopHttp = _service.CreateClient();
        var registration = await desktopHttp.RegisterDevice("Study desktop");
        desktopHttp.Bearing(await desktopHttp.DeviceToken(registration));

        var code = await desktopHttp.MintPairingCode();

        var phoneHttp = _service.CreateClient();
        var paired = await phoneHttp.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RedeemPairingCode),
            new RedeemPairingCodeRequest(code.Code, "Phone"),
            Cancellation);
        phoneHttp.Bearing(await phoneHttp.DeviceToken(
            (await paired.Content.ReadFromJsonAsync<DeviceRegistrationResponse>(Cancellation))!));

        var desktopRoot = Path.Combine(_root, "desktop");
        Directory.CreateDirectory(desktopRoot);
        var tasks = new SqliteTaskRepository(desktopRoot);
        var credential = new DeviceCredential(registration.OwnerId, registration.DeviceId, "Study desktop", registration.Credential);
        var session = Session(desktopHttp, tasks, credential, Path.Combine(desktopRoot, "sync-state.json"));

        var phoneDatabase = Path.Combine(_root, "phone", "device.db");
        var sync = new CloudSyncClient(phoneHttp);
        var outbox = new DeviceOutbox(new SqliteDeviceStore(phoneDatabase), [new TaskOutboxKind(sync)], TimeProvider.System);
        var view = new TaskViewProjection(new SqliteTaskViewStore(phoneDatabase), outbox, sync, TimeProvider.System);

        return new Devices(tasks, session, new Phone(view, outbox, new TaskEdits(view, outbox)), [desktopHttp, phoneHttp], credential);
    }

    /// <summary>A second desktop paired to the same owner, with its own store and
    /// session.</summary>
    private async Task<(SqliteTaskRepository Tasks, TaskSyncSession Session, HttpClient Http)> SecondDesktopAsync(Devices devices)
    {
        var code = await devices.Clients[0].MintPairingCode();

        var http = _service.CreateClient();
        var paired = await http.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RedeemPairingCode),
            new RedeemPairingCodeRequest(code.Code, "Laptop"),
            Cancellation);
        var registration = (await paired.Content.ReadFromJsonAsync<DeviceRegistrationResponse>(Cancellation))!;
        http.Bearing(await http.DeviceToken(registration));

        var root = Path.Combine(_root, "laptop");
        Directory.CreateDirectory(root);
        var tasks = new SqliteTaskRepository(root);
        var credential = new DeviceCredential(registration.OwnerId, registration.DeviceId, "Laptop", registration.Credential);

        return (tasks, Session(http, tasks, credential, Path.Combine(root, "sync-state.json")), http);
    }

    /// <summary>A session over the desktop's own store whose cursor starts at the
    /// beginning of the feed, so its pull replays every document.</summary>
    private TaskSyncSession FromTheStartOfTheFeed(Devices devices) =>
        Session(
            devices.Clients[0],
            devices.DesktopTasks,
            devices.DesktopCredential,
            Path.Combine(_root, "replay-" + Guid.NewGuid().ToString("N") + ".json"));

    private static TaskSyncSession Session(HttpClient http, SqliteTaskRepository tasks, DeviceCredential credential, string statePath) =>
        new(
            new TaskSyncClient(http),
            new TaskReplicaMerge(tasks),
            tasks,
            new FileTaskSyncStateStore(statePath),
            new InMemoryDeviceCredentialStore(credential),
            TimeProvider.System);

    private sealed record Phone(TaskViewProjection View, DeviceOutbox Outbox, TaskEdits Edits);

    private sealed record Devices(
        SqliteTaskRepository DesktopTasks,
        TaskSyncSession DesktopSession,
        Phone Phone,
        IReadOnlyList<HttpClient> Clients,
        DeviceCredential DesktopCredential) : IAsyncDisposable
    {
        /// <summary>A task the desktop picked for today that repeats weekly and
        /// falls due today.</summary>
        public async Task<TaskItem> DesktopAddsTodaysRepeatingTaskAsync()
        {
            var task = new TaskItem("Water the plants", string.Empty, EntryType.Task);
            task.SetRecurrence(new Recurrence(1, RecurrenceUnit.Week));
            task.SetDueOn(Phone.View.Today);
            task.SetInMyDayOn(Phone.View.Today);

            await DesktopTasks.SaveAsync(task, Cancellation);
            Assert.True((await DesktopSession.SyncAsync(Cancellation)).IsSuccess);

            return task;
        }

        public async Task<IReadOnlyList<TaskItem>> SuccessorsOfAsync(Guid id) =>
            [.. (await DesktopTasks.ListAsync(Cancellation)).Where(task => task.RecurrenceSourceId == id)];

        /// <summary>A task the desktop picked for today, with two steps written
        /// as the desktop writes them — chapters in the body, and the structured
        /// list beside them.</summary>
        public async Task<TaskItem> DesktopAddsTodaysTaskAsync()
        {
            var task = new TaskItem("Plan the trip", "Before Friday.\n\n## Pack\n\n## Book the train", EntryType.Task);
            task.AddSubItem("Pack");
            task.AddSubItem("Book the train");
            task.SetInMyDayOn(Phone.View.Today);

            await DesktopTasks.SaveAsync(task, Cancellation);
            Assert.True((await DesktopSession.SyncAsync(Cancellation)).IsSuccess);

            return task;
        }

        public async Task<TaskItem> DesktopPullsAsync(Guid id)
        {
            Assert.True((await DesktopSession.PullAsync(Cancellation)).IsSuccess);
            return (await DesktopTasks.GetAsync(id, Cancellation))!;
        }

        public ValueTask DisposeAsync()
        {
            Phone.View.Dispose();
            Phone.Outbox.Dispose();
            foreach (var client in Clients) client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
