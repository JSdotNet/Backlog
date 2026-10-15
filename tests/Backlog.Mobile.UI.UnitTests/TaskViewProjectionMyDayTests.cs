using Backlog.Mobile.UI.Outbox;
using Backlog.Mobile.UI.Tasks;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// Clear My Day overnight on the phone (.devbook/arc42/adr/0032, §4 and §5): the
/// projection keeps the preferences document a desktop synced, later stamp first, in
/// the same write as the page that carried it, and reads My Day under the one rule both
/// heads share. No document means the switch is on, which is the rule as it was.
/// </summary>
public sealed class TaskViewProjectionMyDayTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = new(2026, 9, 26);
    private static readonly DateOnly Yesterday = Today.AddDays(-1);

    /// <summary>The id the desktop writes the preferences under — persisted, so a
    /// literal here rather than the desktop's constant, which the phone cannot see.</summary>
    private static readonly Guid PreferencesId = Guid.Parse("4d7a9c2e-5b1f-4e3a-8c6d-2f0e1a3b5c01");

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly ScriptedTaskService _service = new();
    private readonly InMemoryTaskViewStore _store;
    private readonly CloudSyncClient _sync;
    private readonly DeviceOutbox _outbox;
    private readonly TaskViewProjection _view;

    public TaskViewProjectionMyDayTests() : this(new InMemoryTaskViewStore())
    {
    }

    private TaskViewProjectionMyDayTests(InMemoryTaskViewStore store)
    {
        _clock.SetLocalTimeZone(TimeZoneInfo.Utc);

        _store = store;
        _sync = new CloudSyncClient(new HttpClient(new Handler(_service)) { BaseAddress = new Uri("https://sync.test") });
        _outbox = new DeviceOutbox(new InMemoryDeviceStore(), [new TaskOutboxKind(_sync)], _clock);
        _view = new TaskViewProjection(_store, _outbox, _sync, _clock);
    }

    public void Dispose()
    {
        _view.Dispose();
        _outbox.Dispose();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task With_no_preferences_document_the_phone_clears_My_Day_overnight()
    {
        _service.Append(TestTasks.Task("Picked yesterday", Now, inMyDayOn: Yesterday));

        await _view.PullAsync(Cancellation);

        Assert.True(_view.ClearsOvernight);
        Assert.Empty(_view.MyDay(Today));
        Assert.Empty(_view.Day(Today));
        Assert.Null(_store.ReadMyDayPreferences());
    }

    /// <summary>ADR 0032 Verification 4: the document is kept in the same write as the
    /// page's cursor, so a pull cut short never moves past a document it did not keep.</summary>
    [Fact]
    public async Task A_page_carrying_the_preferences_keeps_them_in_the_same_write_as_its_cursor()
    {
        _service.Append(Preferences(clearsOvernight: false, Now));

        await _view.PullAsync(Cancellation);

        Assert.False(_view.ClearsOvernight);
        var save = Assert.Single(_store.Saves);
        Assert.Equal("pos:1", save.Cursor);
        Assert.NotNull(save.Preferences);
        Assert.False(save.Preferences.ClearsOvernight);
        Assert.Equal(Now, save.Preferences.UpdatedAt);
    }

    [Fact]
    public async Task The_document_is_kept_verbatim_unknown_keys_included()
    {
        const string content = """{ "updatedAt": "2026-09-26T08:00:00.0000000+00:00", "clearsOvernight": false, "aLaterKey": [1, 2] }""";
        _service.Append(TestTasks.Task("My Day preferences", Now, type: "my-day-preferences", id: PreferencesId, contentMd: content));

        await _view.PullAsync(Cancellation);

        Assert.Equal(content, _store.ReadMyDayPreferences()!.Content);
    }

    /// <summary>ADR 0032 §1: last write wins on the document's own stamp, whichever
    /// page brings it — a later flip on an earlier page beats an older one after it.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task The_later_stamp_wins_whichever_page_brings_it(int pageSize)
    {
        _service.PageSize = pageSize;
        _service.Append(Preferences(clearsOvernight: false, Now.AddMinutes(2)));
        _service.Append(Preferences(clearsOvernight: true, Now.AddMinutes(1)));

        await _view.PullAsync(Cancellation);

        Assert.False(_view.ClearsOvernight);
        Assert.Equal(Now.AddMinutes(2), _store.ReadMyDayPreferences()!.UpdatedAt);
    }

    [Fact]
    public async Task A_later_flip_pulled_afterwards_replaces_the_one_kept()
    {
        _service.Append(Preferences(clearsOvernight: false, Now));
        await _view.PullAsync(Cancellation);

        _service.Append(Preferences(clearsOvernight: true, Now.AddMinutes(5)));
        await _view.PullAsync(Cancellation);

        Assert.True(_view.ClearsOvernight);
    }

    [Fact]
    public async Task Taking_a_copy_raises_Changed_even_when_no_task_moved()
    {
        var raised = 0;
        _view.Changed += () => raised++;
        _service.Append(Preferences(clearsOvernight: false, Now));

        await _view.PullAsync(Cancellation);

        Assert.Equal(1, raised);
    }

    [Theory]
    [InlineData("""{ "clearsOvernight": "no" }""")]
    [InlineData("not json")]
    [InlineData("[false]")]
    public async Task A_document_this_build_cannot_read_leaves_the_one_kept_alone(string content)
    {
        _service.Append(Preferences(clearsOvernight: false, Now));
        _service.Append(TestTasks.Task("My Day preferences", Now.AddMinutes(1), type: "my-day-preferences", id: PreferencesId, contentMd: content));

        await _view.PullAsync(Cancellation);

        Assert.False(_view.ClearsOvernight);
        Assert.Equal(Now, _store.ReadMyDayPreferences()!.UpdatedAt);
    }

    [Fact]
    public void A_document_without_the_switch_reads_on()
    {
        var store = new InMemoryTaskViewStore { Preferences = new MyDayPreferencesDocument(Now, 1, """{ "updatedAt": "2026-09-26T08:00:00.0000000+00:00" }""") };
        using var test = new TaskViewProjectionMyDayTests(store);

        Assert.True(test._view.ClearsOvernight);
    }

    /// <summary>ADR 0032 Verification 4: a restarted phone reads the switch it kept,
    /// before any pull answers.</summary>
    [Fact]
    public async Task A_restarted_phone_keeps_the_switch_it_pulled()
    {
        _service.Append(Preferences(clearsOvernight: false, Now));
        _service.Append(TestTasks.Task("Carried", Now, inMyDayOn: Yesterday));
        await _view.PullAsync(Cancellation);

        using var restarted = new TaskViewProjection(_store, _outbox, _sync, _clock);

        Assert.False(restarted.ClearsOvernight);
        Assert.Equal("Carried", Assert.Single(restarted.MyDay(Today)).Task.Title);
    }

    /// <summary>ADR 0032 Verification 6, on the phone: with the switch off a task picked
    /// on an earlier day stays until it is finished, and on the day it is finished it is
    /// in the Today screen's day, ticked, but not in My Day's open list.</summary>
    [Fact]
    public async Task With_the_switch_off_My_Day_and_the_day_follow_the_rule()
    {
        _service.Append(Preferences(clearsOvernight: false, Now));
        _service.Append(TestTasks.Task("Picked today", Now, inMyDayOn: Today));
        _service.Append(TestTasks.Task("Carried, open", Now, inMyDayOn: Yesterday));
        _service.Append(Ticked("Carried, done today", Yesterday, Today));
        _service.Append(Ticked("Carried, done yesterday", Yesterday, Yesterday));
        _service.Append(TestTasks.Task("Picked for tomorrow", Now, inMyDayOn: Today.AddDays(1)));
        _service.Append(TestTasks.Task("Not picked", Now));

        await _view.PullAsync(Cancellation);

        Assert.Equal(["Carried, open", "Picked today"], _view.MyDay(Today).Select(Title).Order());
        Assert.Equal(["Carried, done today", "Carried, open", "Picked today"], _view.Day(Today).Select(Title).Order());
    }

    [Fact]
    public async Task With_the_switch_on_a_carried_task_is_not_in_today()
    {
        _service.Append(Preferences(clearsOvernight: true, Now));
        _service.Append(TestTasks.Task("Picked today", Now, inMyDayOn: Today));
        _service.Append(TestTasks.Task("Carried, open", Now, inMyDayOn: Yesterday));
        _service.Append(Ticked("Carried, done today", Yesterday, Today));

        await _view.PullAsync(Cancellation);

        Assert.Equal(["Picked today"], _view.MyDay(Today).Select(Title));
        Assert.Equal(["Picked today"], _view.Day(Today).Select(Title));
    }

    private static string Title(TaskViewRow row) => row.Task.Title;

    private static TaskChange Preferences(bool clearsOvernight, DateTimeOffset stamp) =>
        TestTasks.Task(
            "My Day preferences",
            stamp,
            type: "my-day-preferences",
            id: PreferencesId,
            contentMd: $$"""{ "updatedAt": "{{stamp:O}}", "clearsOvernight": {{(clearsOvernight ? "true" : "false")}} }""");

    private static TaskChange Ticked(string title, DateOnly pickedFor, DateOnly completedOn)
    {
        var change = TestTasks.Task(title, Now, inMyDayOn: pickedFor, status: "done");
        return change with { Task = change.Task with { CompletedOn = completedOn } };
    }

    private sealed class Handler(ScriptedTaskService service) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            service.AnswerAsync(request, cancellationToken);
    }
}
