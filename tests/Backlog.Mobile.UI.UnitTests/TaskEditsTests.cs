using Backlog.Mobile.UI.Outbox;
using Backlog.Mobile.UI.Tasks;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The phone's three task edits through the one service the screens call: each is
/// queued as a whole task document under an outbox entry of its own, shown in the
/// view at once, and replaced by the replica's copy on the pull after delivery.
/// </summary>
public sealed class TaskEditsTests : IDisposable
{
    /// <summary>20:00 UTC is already the 26th in a UTC+10 phone — the day the
    /// edits work from.</summary>
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 20, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = new(2026, 9, 26);

    private static readonly Guid Step = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly ScriptedTaskService _service = new();
    private readonly InMemoryTaskViewStore _store = new();
    private readonly DeviceOutbox _outbox;
    private readonly TaskViewProjection _view;
    private readonly TaskEdits _edits;

    public TaskEditsTests()
    {
        _clock.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Phone+10", TimeSpan.FromHours(10), "Phone+10", "Phone+10"));

        var sync = new CloudSyncClient(new HttpClient(new Handler(_service)) { BaseAddress = new Uri("https://sync.test") });
        _outbox = new DeviceOutbox(new InMemoryDeviceStore(), [new TaskOutboxKind(sync)], _clock);
        _view = new TaskViewProjection(_store, _outbox, sync, _clock);
        _edits = new TaskEdits(_view, _outbox);
    }

    public void Dispose()
    {
        _view.Dispose();
        _outbox.Dispose();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>A task the desktop wrote an hour ago, in today's My Day, with
    /// one step — pulled into the view, the service then gone quiet.</summary>
    private async Task<TaskChange> PulledTaskAsync()
    {
        var change = TestTasks.Task(
            "Plan the trip",
            Now.AddHours(-1),
            inMyDayOn: Today,
            contentMd: "## Pack",
            subItems: [new SubItemPayload(Step, "Pack", "pending", null, 0)]);

        _service.Append(change);
        await _view.PullAsync(Cancellation);
        _service.State = InboxServiceState.Unreachable;

        return change;
    }

    [Fact]
    public async Task An_edit_is_queued_as_the_whole_task_under_its_own_entry_and_the_tasks_id()
    {
        var task = await PulledTaskAsync();

        await _edits.MarkDoneAsync(task.Id, Cancellation);
        await _outbox.WhenIdleAsync();

        var entry = Assert.Single(_outbox.Entries);
        Assert.Equal(TaskOutboxKind.Token, entry.Kind);
        Assert.NotEqual(task.Id, entry.Id);
        Assert.Equal(7, entry.Id.Version);

        var queued = TaskOutboxKind.Read(entry);
        Assert.Equal(task.Id, queued.Id);
        Assert.Equal(Now, queued.UpdatedAt);
        Assert.Null(queued.DeletedAt);
        Assert.Equal("Plan the trip", queued.Task.Title);
        Assert.Equal(Today, queued.Task.CompletedOn);
        Assert.Equal("done", queued.Task.Status);
        Assert.Equal(Today, queued.Task.InMyDayOn);
    }

    [Fact]
    public async Task Every_send_of_an_edit_carries_the_tasks_id_not_the_entrys()
    {
        var task = await PulledTaskAsync();

        await _edits.SetStepDoneAsync(task.Id, Step, done: true, Cancellation);
        await _outbox.WhenIdleAsync();

        _service.State = InboxServiceState.Answering;
        _clock.Advance(DeviceOutbox.DelayAfter(1));
        await _outbox.WhenIdleAsync();

        Assert.Equal(2, _service.Pushed.Count);
        Assert.All(_service.Pushed, change =>
        {
            Assert.Equal(task.Id, change.Id);
            Assert.Equal("done", Assert.Single(change.Task.SubItems).Status);
        });
        Assert.Empty(_outbox.Entries);
    }

    [Fact]
    public async Task The_edited_row_is_in_the_view_and_the_store_at_once_with_server_stamp_zero()
    {
        var task = await PulledTaskAsync();
        var changed = 0;
        _view.Changed += () => changed++;

        var row = await _edits.MoveToTomorrowAsync(task.Id, Cancellation);

        Assert.NotNull(row);
        Assert.Equal(0, row.ServerTimestamp);
        Assert.Equal(Now, row.UpdatedAt);
        Assert.Equal(Today.AddDays(1), row.Task.InMyDayOn);
        Assert.Same(row, _view.Find(task.Id));
        Assert.Empty(_view.MyDay(Today));
        Assert.Equal(task.Id, Assert.Single(_view.MyDay(Today.AddDays(1))).Id);
        Assert.Equal(Today.AddDays(1), Assert.Single(_store.ReadRows(), kept => kept.Id == task.Id).Task.InMyDayOn);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task The_edited_row_is_marked_waiting_until_the_outbox_delivers_it()
    {
        var task = await PulledTaskAsync();

        await _edits.MarkDoneAsync(task.Id, Cancellation);
        await _outbox.WhenIdleAsync();

        var pending = _edits.Pending(task.Id);
        Assert.NotNull(pending);
        Assert.NotEqual(task.Id, pending.Id);

        _service.State = InboxServiceState.Answering;
        _clock.Advance(DeviceOutbox.DelayAfter(1));
        await _outbox.WhenIdleAsync();

        Assert.Null(_edits.Pending(task.Id));
    }

    [Fact]
    public async Task The_pull_after_delivery_replaces_the_phones_row_with_the_replicas_copy()
    {
        var task = await PulledTaskAsync();
        _service.State = InboxServiceState.Answering;

        await _edits.MarkDoneAsync(task.Id, Cancellation);
        await _outbox.WhenIdleAsync();
        await _view.PullAsync(Cancellation);

        var row = _view.Find(task.Id)!;
        Assert.True(row.ServerTimestamp > 0);
        Assert.Equal(Today, row.Task.CompletedOn);
        Assert.Equal("done", row.Task.Status);
    }

    [Fact]
    public async Task A_pull_of_the_older_copy_does_not_undo_an_edit_still_waiting()
    {
        var task = await PulledTaskAsync();

        await _edits.MarkDoneAsync(task.Id, Cancellation);
        await _outbox.WhenIdleAsync();

        // The feed hands the desktop's copy back again — the cursor refused, so
        // the pull restarts from the beginning — while the edit is still in the
        // outbox. Every push stays unreachable, so the edit is not in the feed.
        _service.State = InboxServiceState.Answering;
        _service.RejectCursorWith = "sync.cursor_expired";
        var pullsBefore = _service.PulledFrom.Count;
        await _view.PullAsync(Cancellation);

        Assert.Null(_service.PulledFrom[^1]);
        Assert.True(_service.PulledFrom.Count > pullsBefore);
        Assert.Equal(Today, _view.Find(task.Id)!.Task.CompletedOn);
    }

    [Fact]
    public async Task An_edit_on_a_phone_whose_clock_is_behind_is_still_later_than_the_copy_it_edits()
    {
        var ahead = TestTasks.Task("Written by a fast clock", Now.AddMinutes(5), inMyDayOn: Today);
        _service.Append(ahead);
        await _view.PullAsync(Cancellation);

        var row = await _edits.MarkDoneAsync(ahead.Id, Cancellation);

        Assert.True(row!.UpdatedAt > ahead.UpdatedAt);
    }

    [Fact]
    public async Task Two_quick_edits_are_two_entries_sent_in_the_order_they_were_made()
    {
        var task = await PulledTaskAsync();

        await _edits.SetStepDoneAsync(task.Id, Step, done: true, Cancellation);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await _edits.MarkDoneAsync(task.Id, Cancellation);
        await _outbox.WhenIdleAsync();

        Assert.Equal(2, _outbox.Entries.Count);

        _service.State = InboxServiceState.Answering;
        var pushedBefore = _service.Pushed.Count;
        _clock.Advance(DeviceOutbox.DelayAfter(1));
        await _outbox.WhenIdleAsync();

        var sent = _service.Pushed.Skip(pushedBefore).ToList();
        Assert.Equal(2, sent.Count);
        Assert.Null(sent[0].Task.CompletedOn);
        Assert.Equal(Today, sent[1].Task.CompletedOn);
        Assert.True(sent[1].UpdatedAt > sent[0].UpdatedAt);
    }

    [Fact]
    public async Task An_edit_that_changes_nothing_queues_nothing()
    {
        var task = await PulledTaskAsync();

        var row = await _edits.MarkUndoneAsync(task.Id, Cancellation);

        Assert.Same(_view.Find(task.Id), row);
        Assert.Empty(_outbox.Entries);
    }

    [Fact]
    public async Task An_edit_to_a_task_the_view_does_not_hold_answers_null_and_queues_nothing()
    {
        Assert.Null(await _edits.MarkDoneAsync(Guid.NewGuid(), Cancellation));
        Assert.Empty(_outbox.Entries);
    }

    [Fact]
    public async Task An_added_task_is_still_found_waiting_by_its_own_id()
    {
        _service.State = InboxServiceState.Unreachable;

        var row = await _view.AddAsync("Buy a charger", Cancellation);
        await _outbox.WhenIdleAsync();

        Assert.Equal(row.Id, _edits.Pending(row.Id)!.Id);
    }

    private sealed class Handler(ScriptedTaskService service) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            service.AnswerAsync(request, cancellationToken);
    }
}
