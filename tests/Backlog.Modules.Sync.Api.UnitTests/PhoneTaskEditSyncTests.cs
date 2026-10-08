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
        var session = new TaskSyncSession(
            new TaskSyncClient(desktopHttp),
            new TaskReplicaMerge(tasks),
            tasks,
            new FileTaskSyncStateStore(Path.Combine(desktopRoot, "sync-state.json")),
            new InMemoryDeviceCredentialStore(new DeviceCredential(registration.OwnerId, registration.DeviceId, "Study desktop", registration.Credential)),
            TimeProvider.System);

        var phoneDatabase = Path.Combine(_root, "phone", "device.db");
        var sync = new CloudSyncClient(phoneHttp);
        var outbox = new DeviceOutbox(new SqliteDeviceStore(phoneDatabase), [new TaskOutboxKind(sync)], TimeProvider.System);
        var view = new TaskViewProjection(new SqliteTaskViewStore(phoneDatabase), outbox, sync, TimeProvider.System);

        return new Devices(tasks, session, new Phone(view, outbox, new TaskEdits(view, outbox)), [desktopHttp, phoneHttp]);
    }

    private sealed record Phone(TaskViewProjection View, DeviceOutbox Outbox, TaskEdits Edits);

    private sealed record Devices(
        SqliteTaskRepository DesktopTasks,
        TaskSyncSession DesktopSession,
        Phone Phone,
        IReadOnlyList<HttpClient> Clients) : IAsyncDisposable
    {
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
