using Backlog.Mobile.UI.Outbox;
using Backlog.Mobile.UI.Tasks;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The Tasks tab's model: My Day is the day's pick on the phone's own calendar,
/// a cursor the service will not take starts the pull over, and a task added here
/// is picked for today, on the list at once, and sent through the outbox under one
/// id however many attempts it takes.
/// </summary>
public sealed class TaskViewProjectionTests : IDisposable
{
    /// <summary>20:00 UTC is already the 26th in a UTC+10 phone — which is the
    /// date My Day has to use.</summary>
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 20, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = new(2026, 9, 26);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly ScriptedTaskService _service = new();
    private readonly InMemoryTaskViewStore _store;
    private readonly DeviceOutbox _outbox;
    private readonly TaskViewProjection _view;

    public TaskViewProjectionTests() : this(new InMemoryTaskViewStore())
    {
    }

    private TaskViewProjectionTests(InMemoryTaskViewStore store)
    {
        _clock.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Phone+10", TimeSpan.FromHours(10), "Phone+10", "Phone+10"));

        _store = store;
        var sync = new CloudSyncClient(new HttpClient(new Handler(_service)) { BaseAddress = new Uri("https://sync.test") });
        _outbox = new DeviceOutbox(new InMemoryDeviceStore(), [new TaskOutboxKind(sync)], _clock);
        _view = new TaskViewProjection(_store, _outbox, sync, _clock);
    }

    public void Dispose()
    {
        _view.Dispose();
        _outbox.Dispose();
    }

    [Fact]
    public void Today_is_the_phones_local_date_not_UTCs()
    {
        Assert.Equal(Today, _view.Today);
    }

    [Fact]
    public async Task Yesterdays_pick_is_not_in_My_Day_today()
    {
        _service.Append(TestTasks.Task("Picked yesterday", Now, inMyDayOn: Today.AddDays(-1)));

        await _view.PullAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_view.MyDay(Today));
    }

    [Fact]
    public async Task A_task_due_next_week_but_picked_today_is_in_My_Day()
    {
        _service.Append(TestTasks.Task("Picked, due next week", Now, inMyDayOn: Today, dueOn: Today.AddDays(7)));

        await _view.PullAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Picked, due next week", Assert.Single(_view.MyDay(Today)).Task.Title);
    }

    [Fact]
    public async Task A_task_due_today_but_not_picked_is_not_in_My_Day()
    {
        _service.Append(TestTasks.Task("Due today, not picked", Now, dueOn: Today));

        await _view.PullAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_view.MyDay(Today));
    }

    [Theory]
    [InlineData("done")]
    [InlineData("archived")]
    public async Task A_closed_task_is_not_in_My_Day_even_when_picked(string status)
    {
        _service.Append(TestTasks.Task("Finished", Now, inMyDayOn: Today, status: status));

        await _view.PullAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_view.MyDay(Today));
    }

    [Fact]
    public async Task A_pull_follows_the_feed_until_the_service_says_it_is_drained_and_keeps_the_cursor()
    {
        _service.PageSize = 2;
        for (var i = 0; i < 5; i++) _service.Append(TestTasks.Task($"Task {i}", Now, inMyDayOn: Today));

        await _view.PullAsync(TestContext.Current.CancellationToken);

        Assert.Equal(5, _view.MyDay(Today).Count);
        Assert.Equal([null, "pos:2", "pos:4"], _service.PulledFrom);
        Assert.Equal("pos:5", _store.ReadCursor());
    }

    [Theory]
    [InlineData("sync.cursor_expired")]
    [InlineData("sync.cursor_malformed")]
    public async Task A_cursor_the_service_will_not_take_starts_the_pull_over_instead_of_failing(string code)
    {
        var store = new InMemoryTaskViewStore(cursor: "pos:999");
        using var test = new TaskViewProjectionTests(store);
        test._service.Append(TestTasks.Task("Still here", Now, inMyDayOn: Today));
        test._service.RejectCursorWith = code;

        await test._view.PullAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["pos:999", null], test._service.PulledFrom);
        Assert.Equal("Still here", Assert.Single(test._view.MyDay(Today)).Task.Title);
        Assert.Equal("pos:1", store.ReadCursor());
    }

    [Fact]
    public async Task An_added_task_carries_todays_date_and_is_in_My_Day_at_once()
    {
        _service.State = InboxServiceState.Unreachable;

        var row = await _view.AddAsync("Buy a charger", TestContext.Current.CancellationToken);

        Assert.Equal(Today, row.Task.InMyDayOn);
        Assert.Equal("task", row.Task.Type);
        Assert.Equal("draft", row.Task.Status);
        Assert.Equal("medium", row.Task.Priority);
        Assert.Equal(Now, row.Task.CreatedAt);
        Assert.Equal(7, row.Id.Version);
        Assert.Empty(row.Task.RepoIds);
        Assert.Null(row.Task.DueOn);

        Assert.Equal(row.Id, Assert.Single(_view.MyDay(Today)).Id);
        Assert.Contains(_store.ReadRows(), kept => kept.Id == row.Id);
    }

    /// <summary>Queued, not sent: the task is in the outbox under its own id and
    /// kind before any answer, so a phone with no network keeps it.</summary>
    [Fact]
    public async Task An_added_task_is_queued_in_the_outbox_rather_than_sent_directly()
    {
        _service.State = InboxServiceState.Unreachable;

        var row = await _view.AddAsync("Buy a charger", TestContext.Current.CancellationToken);
        await _outbox.WhenIdleAsync();

        var entry = Assert.Single(_outbox.Entries);
        Assert.Equal(TaskOutboxKind.Token, entry.Kind);
        Assert.Equal(row.Id, entry.Id);
        Assert.Equal(Today, TaskOutboxKind.Read(entry).Task.InMyDayOn);
        Assert.Equal(1, entry.Attempts);
    }

    [Fact]
    public async Task A_retried_push_sends_the_same_id_every_time()
    {
        _service.State = InboxServiceState.Unreachable;

        var row = await _view.AddAsync("Buy a charger", TestContext.Current.CancellationToken);
        await _outbox.WhenIdleAsync();

        _clock.Advance(DeviceOutbox.DelayAfter(1));
        await _outbox.WhenIdleAsync();

        _service.State = InboxServiceState.Answering;
        _clock.Advance(DeviceOutbox.DelayAfter(2));
        await _outbox.WhenIdleAsync();

        Assert.Equal(3, _service.Pushed.Count);
        Assert.All(_service.Pushed, change => Assert.Equal(row.Id, change.Id));
        Assert.Empty(_outbox.Entries);
    }

    [Fact]
    public async Task The_next_pull_replaces_the_phones_copy_with_the_replicas()
    {
        var row = await _view.AddAsync("Buy a charger", TestContext.Current.CancellationToken);
        await _outbox.WhenIdleAsync();

        await _view.PullAsync(TestContext.Current.CancellationToken);

        var kept = Assert.Single(_view.MyDay(Today));
        Assert.Equal(row.Id, kept.Id);
        Assert.True(kept.ServerTimestamp > 0);
    }

    [Fact]
    public void The_task_the_phone_builds_round_trips_the_wire_unchanged()
    {
        var change = TaskViewProjection.NewTask("Buy a charger", Now, Today);

        var json = TaskOutboxKind.Write(change);
        var entry = new OutboxEntry(change.Id, TaskOutboxKind.Token, json, 0, null, Now);

        Assert.Equal(json, TaskOutboxKind.Write(TaskOutboxKind.Read(entry)));
        Assert.Contains("\"inMyDayOn\":\"2026-09-26\"", json, StringComparison.Ordinal);
    }

    private sealed class Handler(ScriptedTaskService service) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            service.AnswerAsync(request, cancellationToken);
    }
}
