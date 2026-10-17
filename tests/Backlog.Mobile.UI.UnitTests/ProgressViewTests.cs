using Backlog.Mobile.UI.Outbox;
using Backlog.Mobile.UI.Progress;
using Backlog.Mobile.UI.Tasks;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// Goals, impediments and the stand-up on the phone (local ADR 0035 §8,
/// <c>.devbook/domain/progress/features.md#goals-on-the-phone</c>): the task feed's goal,
/// impediment and stand-up context documents folded into their own read model with their
/// own cursor, the goals' pull requests carrying the statuses a desktop read, the stand-up
/// read from them and the synced tasks by the desktop's own rules, and the three impediment
/// edits queued through the outbox as <c>impediment-edit</c> documents.
/// </summary>
public sealed class ProgressViewTests : IDisposable
{
    /// <summary>Monday 19 October 2026: by Week, last is 12 to 16 October and next 19 to 23.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid GoalId = Guid.Parse("0f6e2c3a-1b4d-4e5f-8a9b-0c1d2e3f4a5b");
    private static readonly Guid ImpedimentId = Guid.Parse("6a7b8c9d-0e1f-4a2b-8c3d-4e5f6a7b8c9d");
    private static readonly Guid ContextId = Guid.Parse("8b3f2d61-7c4e-4a95-b0d8-5e1c9a7f3b02");

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly ScriptedTaskService _service = new();
    private readonly InMemoryProgressViewStore _store = new();
    private readonly DeviceOutbox _outbox;
    private readonly ProgressViewProjection _progress;
    private readonly TaskViewProjection _tasks;

    public ProgressViewTests()
    {
        _clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        var sync = new CloudSyncClient(new HttpClient(new Handler(_service)) { BaseAddress = new Uri("https://sync.test") });
        _outbox = new DeviceOutbox(new InMemoryDeviceStore(), [new ImpedimentEditOutboxKind(sync), new TaskOutboxKind(sync)], _clock);
        _progress = new ProgressViewProjection(_store, _outbox, sync, _clock);
        _tasks = new TaskViewProjection(new InMemoryTaskViewStore(), _outbox, sync, _clock);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _progress.Dispose();
        _tasks.Dispose();
        _outbox.Dispose();
    }

    [Fact]
    public async Task A_pull_keeps_goals_and_impediments_and_the_tasks_tab_lists_neither()
    {
        var task = TestTasks.Task("Map the bank's fields", Now.AddDays(-3), status: "in_progress");
        _service.Append(task);
        _service.Append(GoalDocument(Now.AddDays(-2), $$"""{"kind":"Task","taskId":"{{task.Id}}","attachedOn":"2026-10-17"}"""));
        _service.Append(ImpedimentDocument(Now.AddDays(-1), state: "Reported"));
        _service.Append(ContextDocument(Now.AddHours(-1)));

        await _progress.PullAsync(Cancellation);
        await _tasks.PullAsync(Cancellation);

        var goal = Assert.Single(_progress.Goals());
        Assert.Equal("Customers get paid faster", goal.Goal.Title);
        Assert.Equal(ImpedimentState.Reported, Assert.Single(_progress.Impediments()).State);
        Assert.Equal(18m, _progress.Context()!.PointsPerWeek);
        Assert.Equal(task.Id, Assert.Single(_tasks.Live()).Id);
        Assert.Equal(3, _store.ReadRows().Count);
        Assert.NotNull(_store.ReadCursor());
    }

    [Fact]
    public async Task A_goal_attachment_carries_the_checks_and_approvals_a_desktop_read()
    {
        _service.Append(GoalDocument(Now.AddDays(-2),
            """{"kind":"PullRequest","repository":"JSdotNet/Backlog","number":1364,"attachedOn":"2026-10-17"}""",
            """{"kind":"PullRequest","repository":"JSdotNet/Backlog","number":1999,"attachedOn":"2026-10-17"}"""));
        _service.Append(ContextDocument(Now.AddHours(-1)));

        await _progress.PullAsync(Cancellation);

        var pulls = Assert.Single(_progress.Goals()).PullRequests;
        Assert.Equal(2, pulls.Count);
        Assert.Equal(LinkedPullRequestState.Open, pulls[0].State);
        Assert.Equal(new GoalPullRequestChecksDto(9, 0, 5, []).Total, pulls[0].Checks!.Total);
        Assert.Equal(9, pulls[0].Checks!.Passed);
        Assert.Equal(new GoalPullRequestApprovalsDto(1, 2), pulls[0].Approvals);
        Assert.Equal(LinkedPullRequestState.Unknown, pulls[1].State);
        Assert.Null(pulls[1].Checks);
    }

    [Fact]
    public async Task A_deleted_goal_and_an_older_copy_arriving_after_it_stay_gone()
    {
        _service.Append(GoalDocument(Now.AddDays(-2)));
        _service.Append(new TaskChange(GoalId, Now.AddDays(-1), Now.AddDays(-1), GoalDocument(Now.AddDays(-1)).Task with
        {
            ContentMd = $$"""{"id":"{{GoalId}}","title":"Customers get paid faster","deletedAt":"2026-10-18T09:00:00+00:00"}"""
        }));
        _service.Append(GoalDocument(Now.AddDays(-3)));

        await _progress.PullAsync(Cancellation);

        Assert.Empty(_progress.Goals());
    }

    [Fact]
    public async Task The_stand_up_is_read_on_the_phone_by_the_desktop_rules()
    {
        var finished = TestTasks.Task("Bank file import", Now.AddDays(-5), status: "done", tags: ["+bank-integration"]) with { };
        finished = finished with { Task = finished.Task with { CompletedOn = new DateOnly(2026, 10, 14), Effort = 3 } };
        var doing = TestTasks.Task("Map the bank's fields", Now.AddDays(-5), status: "in_progress", tags: ["+bank-integration"]);
        doing = doing with { Task = doing.Task with { Effort = 5 } };
        var next = TestTasks.Task("Reconcile payments", Now.AddDays(-5), status: "ready", tags: ["+bank-integration"], order: 3);
        next = next with { Task = next.Task with { Effort = 8 } };
        var blocked = TestTasks.Task("Statement format", Now.AddDays(-5), status: "ready", tags: ["+bank-integration"], order: 1);
        blocked = blocked with { Task = blocked.Task with { BlockedSince = new DateOnly(2026, 10, 15) } };
        var dueNoGoal = TestTasks.Task("Renew the certificate", Now.AddDays(-5), dueOn: new DateOnly(2026, 10, 21));
        foreach (var task in new[] { finished, doing, next, blocked, dueNoGoal }) _service.Append(task);

        _service.Append(GoalDocument(Now.AddDays(-6), """{"kind":"Plan","plan":"bank-integration","attachedOn":"2026-10-12"}"""));
        _service.Append(ImpedimentDocument(Now.AddDays(-4), holdsBack: blocked.Id));
        _service.Append(ContextDocument(Now.AddHours(-1)));

        await _progress.PullAsync(Cancellation);
        await _tasks.PullAsync(Cancellation);

        var standUp = new PhoneStandUp(_progress, _tasks).Read();

        Assert.Equal(new IterationWindowDto(IterationLength.Week, new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 16)), standUp.Last);
        var goal = Assert.Single(standUp.Goals);
        Assert.Equal(new GoalProgressDto(Done: 1, Total: 4, Doing: 1, Blocked: 1, ToDo: 1, DoneInWindow: 1), goal.Progress);
        Assert.Equal(GoalStanding.AtRisk, goal.Standing);
        Assert.Equal(["Bank file import"], Assert.Single(standUp.Finished).Items.Select(item => item.Title));
        Assert.Equal(
            ["Map the bank's fields", "Reconcile payments"],
            standUp.Planned[0].Items.Select(item => item.Title));
        Assert.Equal(["Renew the certificate"], standUp.Planned[1].Items.Select(item => item.Title));
        Assert.Null(standUp.Planned[1].GoalId);
        Assert.Equal(13, standUp.PlannedEffort);
        Assert.Equal(18, standUp.Capacity);
        var impediment = Assert.Single(standUp.Impediments);
        Assert.Equal(GoalId, impediment.GoalId);
        Assert.Equal("bank support", impediment.With);
    }

    /// <summary>The phone's goal counts what the roadmap item gathers by tag: an entry
    /// tagged <c>#bank-integration</c> is stored bare, and the attached plan counts it beside
    /// the <c>+bank-integration</c> one.</summary>
    [Fact]
    public async Task A_goals_plan_counts_an_entry_carrying_its_bare_tag()
    {
        _service.Append(TestTasks.Task("Map the bank's fields", Now.AddDays(-5), status: "in_progress", tags: ["+bank-integration"]));
        _service.Append(TestTasks.Task("Statement format", Now.AddDays(-5), status: "ready", tags: ["bank-integration"]));
        _service.Append(TestTasks.Task("Ask Ana", Now.AddDays(-5), status: "ready", tags: ["@bank-integration"]));
        _service.Append(GoalDocument(Now.AddDays(-6), """{"kind":"Plan","plan":"bank-integration","attachedOn":"2026-10-12"}"""));

        await _progress.PullAsync(Cancellation);
        await _tasks.PullAsync(Cancellation);

        var goal = Assert.Single(new PhoneStandUp(_progress, _tasks).Read().Goals);
        Assert.Equal(new GoalProgressDto(Done: 0, Total: 2, Doing: 1, Blocked: 0, ToDo: 1, DoneInWindow: 0), goal.Progress);
    }

    [Fact]
    public async Task The_stand_up_matches_the_desktop_reading_of_the_same_work()
    {
        var doing = TestTasks.Task("Map the bank's fields", Now.AddDays(-5), status: "in_progress", tags: ["+bank-integration"]);
        _service.Append(doing);
        _service.Append(GoalDocument(Now.AddDays(-6), """{"kind":"Plan","plan":"bank-integration","attachedOn":"2026-10-12"}"""));
        await _progress.PullAsync(Cancellation);
        await _tasks.PullAsync(Cancellation);

        var phone = new PhoneStandUp(_progress, _tasks).Read(IterationLength.Day);
        var rules = StandUpReading.Read(new StandUpInputs(
            new DateOnly(2026, 10, 19), IterationLength.Day, null, WorkingHours.Default,
            [.. _progress.Goals().Select(view => view.Goal)], [], PhoneStandUp.Work(_tasks.Live()), [], null));

        Assert.Equivalent(rules, phone, strict: true);
        Assert.Equal(IterationLength.Day, phone.Next.Length);
    }

    [Fact]
    public async Task Each_impediment_edit_is_queued_as_its_own_document_and_pushed_beside_the_tasks()
    {
        _service.Append(ImpedimentDocument(Now.AddDays(-1)));
        await _progress.PullAsync(Cancellation);

        var moved = await _progress.MoveImpedimentAsync(ImpedimentId, ImpedimentState.Escalated, " account manager ", Cancellation);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await _progress.SetCheckAgainAsync(ImpedimentId, new DateOnly(2026, 10, 21), Cancellation);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await _progress.EditNoteAsync(ImpedimentId, "  ", Cancellation);
        await _outbox.WhenIdleAsync();

        Assert.Equal("account manager", moved!.With);
        var pushed = _service.Pushed;
        Assert.Equal(3, pushed.Count);
        Assert.All(pushed, change => Assert.Equal("impediment-edit", change.Task.Type));
        Assert.All(pushed, change => Assert.Equal("Bank sample files missing", change.Task.Title));
        Assert.Equal(3, pushed.Select(change => change.Id).Distinct().Count());
        var edits = pushed.Select(change => ProgressDocuments.ReadEdit(change.Task.ContentMd)!).ToList();
        Assert.Equal([ImpedimentEditKind.State, ImpedimentEditKind.CheckAgain, ImpedimentEditKind.Note], edits.Select(edit => edit.Kind));
        Assert.Equal(ImpedimentState.Escalated, edits[0].State);
        Assert.Equal(new DateOnly(2026, 10, 21), edits[1].CheckAgain);
        Assert.Null(edits[2].Note);
        Assert.Equal(Now, pushed[0].UpdatedAt);
        Assert.Empty(_outbox.Entries);

        // The phone owns nothing: its copy changes only when a desktop has applied the edit.
        Assert.Equal(ImpedimentState.New, _progress.FindImpediment(ImpedimentId)!.State);
    }

    [Fact]
    public async Task Each_edit_says_what_the_phone_saw_and_a_second_edit_of_a_field_names_the_first_ones_value()
    {
        _service.Append(ImpedimentDocument(Now.AddDays(-1), state: "Reported"));
        await _progress.PullAsync(Cancellation);

        var escalated = await _progress.MoveImpedimentAsync(ImpedimentId, ImpedimentState.Escalated, cancellationToken: Cancellation);
        var handled = await _progress.MoveImpedimentAsync(ImpedimentId, ImpedimentState.BeingHandled, "Priya", Cancellation);
        var note = await _progress.EditNoteAsync(ImpedimentId, "Asked twice", Cancellation);

        Assert.Equal(ImpedimentState.Reported, escalated!.WasState);
        Assert.Equal(ImpedimentState.Escalated, handled!.WasState);
        Assert.True(handled.AskedAt > escalated.AskedAt);
        Assert.Null(note!.WasNote);

        // Once a desktop has written the impediment back, what it holds is what the phone saw.
        _service.Append(ImpedimentDocument(Now.AddMinutes(5), state: "BeingHandled"));
        await _progress.PullAsync(Cancellation);
        _clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(ImpedimentState.BeingHandled, (await _progress.MoveImpedimentAsync(ImpedimentId, ImpedimentState.Resolved, cancellationToken: Cancellation))!.WasState);
    }

    /// <summary>An edit that has left the phone stands until a newer copy of its impediment
    /// answers it. One that does not bear it out — the desktop refused it or found it stale —
    /// is forgotten, so the next edit says the phone saw what the impediment holds.</summary>
    [Fact]
    public async Task A_newer_copy_that_does_not_bear_out_a_delivered_edit_ends_its_chain()
    {
        _service.Append(ImpedimentDocument(Now.AddDays(-1), state: "Reported"));
        await _progress.PullAsync(Cancellation);

        await _progress.MoveImpedimentAsync(ImpedimentId, ImpedimentState.Escalated, cancellationToken: Cancellation);
        await _outbox.WhenIdleAsync();
        Assert.Empty(_outbox.Entries);

        // Left the phone, and nothing about this impediment has come back: it still stands.
        _service.Append(ContextDocument(Now.AddMinutes(1)));
        await _progress.PullAsync(Cancellation);
        Assert.Equal(ImpedimentState.Escalated, _progress.AskedFor(ImpedimentId, ImpedimentEditKind.State)!.State);

        // A newer copy still saying Reported is the desktop's answer.
        _service.Append(ImpedimentDocument(Now.AddMinutes(5), state: "Reported"));
        await _progress.PullAsync(Cancellation);
        Assert.Null(_progress.AskedFor(ImpedimentId, ImpedimentEditKind.State));

        _clock.Advance(TimeSpan.FromMinutes(10));
        var retried = await _progress.MoveImpedimentAsync(ImpedimentId, ImpedimentState.BeingHandled, cancellationToken: Cancellation);
        Assert.Equal(ImpedimentState.Reported, retried!.WasState);
    }

    /// <summary>A newer copy that bears out only the first of two delivered moves is progress,
    /// not an answer: the second still stands until a later copy applies it.</summary>
    [Fact]
    public async Task A_newer_copy_that_bears_out_part_of_a_delivered_chain_keeps_the_rest()
    {
        _service.Append(ImpedimentDocument(Now.AddDays(-1)));
        await _progress.PullAsync(Cancellation);

        await _progress.MoveImpedimentAsync(ImpedimentId, ImpedimentState.Reported, cancellationToken: Cancellation);
        await _progress.MoveImpedimentAsync(ImpedimentId, ImpedimentState.Escalated, cancellationToken: Cancellation);
        await _outbox.WhenIdleAsync();
        Assert.Empty(_outbox.Entries);

        _service.Append(ImpedimentDocument(Now.AddMinutes(5), state: "Reported"));
        await _progress.PullAsync(Cancellation);
        Assert.Equal(ImpedimentState.Escalated, _progress.AskedFor(ImpedimentId, ImpedimentEditKind.State)!.State);

        // The copy after it says Escalated: the chain is applied whole.
        _service.Append(ImpedimentDocument(Now.AddMinutes(10), state: "Escalated"));
        await _progress.PullAsync(Cancellation);
        Assert.Null(_progress.AskedFor(ImpedimentId, ImpedimentEditKind.State));
    }

    /// <summary>A desktop that applies the first of two delivered moves and refuses the second
    /// retires both: once every edit of the chain is retired, the copy the phone holds is the
    /// answer, and the move it does not bear out gives way as a refused first move does.</summary>
    [Fact]
    public async Task A_desktop_that_applies_the_first_move_and_refuses_the_second_ends_the_chain()
    {
        _service.Append(ImpedimentDocument(Now.AddDays(-1)));
        await _progress.PullAsync(Cancellation);

        await _progress.MoveImpedimentAsync(ImpedimentId, ImpedimentState.Reported, cancellationToken: Cancellation);
        await _progress.MoveImpedimentAsync(ImpedimentId, ImpedimentState.Escalated, cancellationToken: Cancellation);
        await _outbox.WhenIdleAsync();
        Assert.Empty(_outbox.Entries);
        var sent = _service.Pushed;

        // The desktop's answer: the impediment moved once, then both edits retired.
        _service.Append(ImpedimentDocument(Now.AddMinutes(5), state: "Reported"));
        foreach (var edit in sent) _service.Append(Retired(edit, Now.AddMinutes(5)));
        await _progress.PullAsync(Cancellation);

        Assert.Null(_progress.AskedFor(ImpedimentId, ImpedimentEditKind.State));
        Assert.False(_progress.IsWaiting(ImpedimentId, ImpedimentEditKind.State));

        _clock.Advance(TimeSpan.FromMinutes(10));
        var retried = await _progress.MoveImpedimentAsync(ImpedimentId, ImpedimentState.Escalated, cancellationToken: Cancellation);
        Assert.Equal(ImpedimentState.Reported, retried!.WasState);
    }

    /// <summary>Only a retired chain is answered whole: while the second move is still live on
    /// the feed, a copy bearing out the first is progress, as before.</summary>
    [Fact]
    public async Task A_chain_whose_second_move_is_not_yet_retired_keeps_waiting_for_it()
    {
        _service.Append(ImpedimentDocument(Now.AddDays(-1)));
        await _progress.PullAsync(Cancellation);

        await _progress.MoveImpedimentAsync(ImpedimentId, ImpedimentState.Reported, cancellationToken: Cancellation);
        await _progress.MoveImpedimentAsync(ImpedimentId, ImpedimentState.Escalated, cancellationToken: Cancellation);
        await _outbox.WhenIdleAsync();
        var sent = _service.Pushed;

        _service.Append(ImpedimentDocument(Now.AddMinutes(5), state: "Reported"));
        _service.Append(Retired(sent[0], Now.AddMinutes(5)));
        await _progress.PullAsync(Cancellation);

        Assert.Equal(ImpedimentState.Escalated, _progress.AskedFor(ImpedimentId, ImpedimentEditKind.State)!.State);
    }

    /// <summary>
    /// An edit that has left the phone is not forgotten when the phone restarts before a
    /// desktop applies it: the next edit of the field names what that one asked, so the
    /// desktop applies the two in order rather than dropping the second as stale.
    /// </summary>
    [Fact]
    public async Task An_edit_that_left_the_phone_before_a_restart_is_what_the_next_edit_saw()
    {
        _service.Append(ImpedimentDocument(Now.AddDays(-1), state: "Reported"));
        await _progress.PullAsync(Cancellation);

        await _progress.MoveImpedimentAsync(ImpedimentId, ImpedimentState.Escalated, cancellationToken: Cancellation);
        await _outbox.WhenIdleAsync();
        Assert.Empty(_outbox.Entries);

        using var restarted = new ProgressViewProjection(_store, _outbox, new CloudSyncClient(new HttpClient(new Handler(_service)) { BaseAddress = new Uri("https://sync.test") }), _clock);
        var second = await restarted.MoveImpedimentAsync(ImpedimentId, ImpedimentState.BeingHandled, "Priya", Cancellation);

        Assert.Equal(ImpedimentState.Escalated, second!.WasState);
        Assert.Equal(ImpedimentState.BeingHandled, restarted.AskedFor(ImpedimentId, ImpedimentEditKind.State)!.State);
    }

    /// <summary>The same, after the phone's pull has brought its own edit back from the feed
    /// and a second restart: the edit row it keeps is still the chain, until a tombstone
    /// retires it.</summary>
    [Fact]
    public async Task An_edit_the_feed_returned_is_kept_across_restarts_until_it_is_retired()
    {
        _service.Append(ImpedimentDocument(Now.AddDays(-1), state: "Reported"));
        await _progress.PullAsync(Cancellation);

        await _progress.MoveImpedimentAsync(ImpedimentId, ImpedimentState.Escalated, cancellationToken: Cancellation);
        await _outbox.WhenIdleAsync();
        await _progress.PullAsync(Cancellation);

        using (var once = NewProjection())
        {
            Assert.Equal(ImpedimentState.Escalated, once.AskedFor(ImpedimentId, ImpedimentEditKind.State)!.State);
        }

        _service.Append(Retired(Assert.Single(_service.Pushed), Now.AddMinutes(5)));
        await _progress.PullAsync(Cancellation);

        using var twice = NewProjection();
        Assert.Null(twice.AskedFor(ImpedimentId, ImpedimentEditKind.State));
    }

    /// <summary>A chain given way to a newer copy stays given way after a restart, though no
    /// desktop ever retired its edit (one closed before it pushed the tombstone): the phone
    /// marks the edit settled, so it is not seeded again as a ghost still waiting.</summary>
    [Fact]
    public async Task A_chain_a_newer_copy_answered_is_not_seeded_again_after_a_restart()
    {
        _service.Append(ImpedimentDocument(Now.AddDays(-1), state: "Reported"));
        await _progress.PullAsync(Cancellation);

        await _progress.MoveImpedimentAsync(ImpedimentId, ImpedimentState.Escalated, cancellationToken: Cancellation);
        await _outbox.WhenIdleAsync();
        await _progress.PullAsync(Cancellation);
        Assert.NotNull(_progress.AskedFor(ImpedimentId, ImpedimentEditKind.State));

        _service.Append(ImpedimentDocument(Now.AddMinutes(5), state: "Reported"));
        await _progress.PullAsync(Cancellation);

        using var restarted = NewProjection();
        Assert.Null(restarted.AskedFor(ImpedimentId, ImpedimentEditKind.State));
        _clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(ImpedimentState.Reported, (await restarted.MoveImpedimentAsync(ImpedimentId, ImpedimentState.BeingHandled, cancellationToken: Cancellation))!.WasState);
    }

    private ProgressViewProjection NewProjection() =>
        new(_store, _outbox, new CloudSyncClient(new HttpClient(new Handler(_service)) { BaseAddress = new Uri("https://sync.test") }), _clock);

    /// <summary>The tombstone a desktop writes for an edit it answered.</summary>
    private static TaskChange Retired(TaskChange edit, DateTimeOffset at) => edit with { UpdatedAt = at, DeletedAt = at };

    /// <summary>A pull holds the projection for as long as the network takes; queuing an edit
    /// needs nothing it is doing, so a tap is kept at once while a slow pull is in flight.</summary>
    [Fact]
    public async Task A_move_queues_while_a_pull_is_in_flight()
    {
        _service.Append(ImpedimentDocument(Now.AddDays(-1)));
        var gated = new GatedHandler(_service);
        using var progress = new ProgressViewProjection(
            _store, _outbox, new CloudSyncClient(new HttpClient(gated) { BaseAddress = new Uri("https://sync.test") }), _clock);
        await progress.PullAsync(Cancellation);

        gated.Hold();
        var pull = progress.PullAsync(Cancellation);
        await gated.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

        var moved = await progress.MoveImpedimentAsync(ImpedimentId, ImpedimentState.Reported, cancellationToken: Cancellation)
            .WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

        Assert.False(pull.IsCompleted);
        Assert.Equal(ImpedimentState.Reported, moved!.State);
        Assert.Equal(ImpedimentState.New, moved.WasState);

        gated.Release();
        await pull;
    }

    [Fact]
    public async Task Edits_still_waiting_in_the_outbox_are_what_the_phone_saw_after_a_restart()
    {
        _service.Append(ImpedimentDocument(Now.AddDays(-1)));
        await _progress.PullAsync(Cancellation);
        _service.State = InboxServiceState.Unreachable;

        await _progress.EditNoteAsync(ImpedimentId, "First", Cancellation);
        using var restarted = new ProgressViewProjection(_store, _outbox, new CloudSyncClient(new HttpClient(new Handler(_service)) { BaseAddress = new Uri("https://sync.test") }), _clock);

        var second = await restarted.EditNoteAsync(ImpedimentId, "Second", Cancellation);

        Assert.Equal("First", second!.WasNote);
    }

    [Fact]
    public async Task An_edit_for_an_impediment_the_phone_does_not_hold_queues_nothing()
    {
        Assert.Null(await _progress.EditNoteAsync(Guid.NewGuid(), "x", Cancellation));
        Assert.Empty(_outbox.Entries);
    }

    [Fact]
    public void The_fold_takes_the_later_copy_and_keeps_a_tombstone_over_an_older_one()
    {
        var rows = new Dictionary<Guid, ProgressViewRow>();
        var later = GoalDocument(Now);
        var older = GoalDocument(Now.AddHours(-1));
        var tombstone = later with { UpdatedAt = Now.AddHours(1), DeletedAt = Now.AddHours(1) };

        ProgressFold.Apply(rows, [Record(later, 2), Record(older, 3)]);
        Assert.Equal(Now, rows[GoalId].UpdatedAt);

        ProgressFold.Apply(rows, [Record(tombstone, 4), Record(later, 5)]);
        Assert.NotNull(rows[GoalId].DeletedAt);

        var tasks = new Dictionary<Guid, TaskViewRow>();
        Assert.Empty(TaskFold.Apply(tasks, [Record(later, 1), Record(ContextDocument(Now), 2), Record(EditDocument(), 3)]));
        Assert.Empty(ProgressFold.Apply(rows, [Record(TestTasks.Task("A task", Now), 7)]));

        // An impediment edit is kept, live or retired: its tombstone is a desktop's answer.
        var edit = EditDocument();
        Assert.Equal(ProgressFold.ImpedimentEditType, Assert.Single(ProgressFold.Apply(rows, [Record(edit, 6)])).Kind);
        ProgressFold.Apply(rows, [Record(edit with { UpdatedAt = Now.AddHours(2), DeletedAt = Now.AddHours(2) }, 8)]);
        Assert.NotNull(rows[edit.Id].DeletedAt);
    }

    private static TaskChangeRecord Record(TaskChange change, long serverTimestamp) => new(change, Guid.NewGuid(), serverTimestamp);

    private static TaskChange GoalDocument(DateTimeOffset at, params string[] attachments) =>
        Whole(GoalId, "goal", "Customers get paid faster", at, $$"""
            {"version":1,"id":"{{GoalId}}","title":"Customers get paid faster","dueDate":"2026-10-23",
             "attachments":[{{string.Join(",", attachments)}}],"createdAt":"2026-10-12T09:00:00+00:00"}
            """);

    private static TaskChange ImpedimentDocument(DateTimeOffset at, string state = "New", Guid? holdsBack = null) =>
        Whole(ImpedimentId, "impediment", "Bank sample files missing", at, $$"""
            {"version":1,"id":"{{ImpedimentId}}","title":"Bank sample files missing","reportedTo":"bank support",
             "history":[{"state":"New","on":"2026-10-14"}{{(state == "New" ? "" : $$""",{"state":"{{state}}","on":"2026-10-15","with":"bank support"}""")}}],
             "holdsBack":[{{(holdsBack is { } held ? $"\"{held}\"" : "")}}]}
            """);

    private static TaskChange ContextDocument(DateTimeOffset at) =>
        Whole(ContextId, "stand-up-context", "Stand-up context", at, ProgressDocuments.WriteContext(new StandUpContextDto(
            at,
            [new GoalPullRequestDto("JSdotNet/Backlog", "1364", LinkedPullRequestState.Open, null, new GoalPullRequestChecksDto(9, 0, 5, []), new GoalPullRequestApprovalsDto(1, 2))],
            18m,
            null,
            IterationLength.Week,
            null)));

    private static TaskChange EditDocument() =>
        ProgressViewProjection.NewEdit(Guid.NewGuid(), new ImpedimentEditDto(ImpedimentId, ImpedimentEditKind.Note, null, null, null, "x", Now), "Bank");

    private static TaskChange Whole(Guid id, string type, string title, DateTimeOffset at, string content)
    {
        var task = TestTasks.Task(title, at, type: type, id: id, contentMd: content, status: "draft");
        return task;
    }

    /// <summary>The task feed with a pull that waits until the test lets it answer.</summary>
    private sealed class GatedHandler(ScriptedTaskService service) : HttpMessageHandler
    {
        private TaskCompletionSource? _open;

        public TaskCompletionSource Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Hold()
        {
            _open = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void Release() => _open?.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get && _open is { } open)
            {
                Entered.TrySetResult();
                await open.Task.WaitAsync(cancellationToken);
            }

            return await service.AnswerAsync(request, cancellationToken);
        }
    }

    private sealed class Handler(ScriptedTaskService service) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            service.AnswerAsync(request, cancellationToken);
    }
}

/// <summary><c>progress_view</c> survives the app closing: rows, tombstones and its own cursor
/// come back from the file as they went in, beside the task view in the same file, and
/// neither cursor moves the other.</summary>
public sealed class SqliteProgressViewStoreTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"progress-view-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public async Task Rows_and_the_cursor_survive_a_new_store_over_the_same_file()
    {
        var goal = new ProgressViewRow(Guid.NewGuid(), "goal", T0, null, 3, """{"title":"Goal"}""");
        var gone = new ProgressViewRow(Guid.NewGuid(), "impediment", T0.AddTicks(1), T0.AddMinutes(1), 4, "{}");

        await new SqliteProgressViewStore(_path).SaveAsync([goal, gone], "pos:7", TestContext.Current.CancellationToken);

        var tasks = new SqliteTaskViewStore(_path);
        var reopened = new SqliteProgressViewStore(_path);

        var rows = reopened.ReadRows().ToDictionary(row => row.Id);
        Assert.Equal(goal, rows[goal.Id]);
        Assert.Equal(gone, rows[gone.Id]);
        Assert.Equal("pos:7", reopened.ReadCursor());
        Assert.Null(tasks.ReadCursor());

        await reopened.SaveAsync([goal with { UpdatedAt = T0.AddHours(1), Content = "{}" }], cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("{}", new SqliteProgressViewStore(_path).ReadRows().Single(row => row.Id == goal.Id).Content);

        await reopened.ResetCursorAsync(TestContext.Current.CancellationToken);
        Assert.Null(reopened.ReadCursor());
        Assert.Equal(2, reopened.ReadRows().Count);
    }

    /// <summary>The file keeps the later of two copies by the fold's own order, whichever is
    /// written last, so two writers racing — a pull and a queued edit — never put an older
    /// copy, or a live one over its tombstone, back on disk.</summary>
    [Fact]
    public async Task A_save_never_writes_an_older_copy_over_a_later_one()
    {
        var store = new SqliteProgressViewStore(_path);
        var edit = new ProgressViewRow(Guid.NewGuid(), "impediment-edit", T0, null, 9, "{}");
        var retired = edit with { UpdatedAt = T0.AddMinutes(5), DeletedAt = T0.AddMinutes(5), ServerTimestamp = 12 };

        await store.SaveAsync([retired], cancellationToken: TestContext.Current.CancellationToken);
        await store.SaveAsync([edit], cancellationToken: TestContext.Current.CancellationToken);
        await store.SaveAsync([edit with { UpdatedAt = T0.AddMinutes(5), ServerTimestamp = 12 }], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(retired, Assert.Single(new SqliteProgressViewStore(_path).ReadRows()));
    }
}

/// <summary>The Progress view in memory, for the tests about the fold and the reads.</summary>
internal sealed class InMemoryProgressViewStore : IProgressViewStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, ProgressViewRow> _rows = [];
    private string? _cursor;

    public IReadOnlyList<ProgressViewRow> ReadRows()
    {
        lock (_lock) return [.. _rows.Values];
    }

    public string? ReadCursor()
    {
        lock (_lock) return _cursor;
    }

    public Task SaveAsync(IReadOnlyList<ProgressViewRow> rows, string? cursor = null, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            foreach (var row in rows)
            {
                if (!_rows.TryGetValue(row.Id, out var held) || ProgressFold.Wins(row, held)) _rows[row.Id] = row;
            }

            if (cursor is not null) _cursor = cursor;
        }

        return Task.CompletedTask;
    }

    public Task ResetCursorAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock) _cursor = null;
        return Task.CompletedTask;
    }
}
