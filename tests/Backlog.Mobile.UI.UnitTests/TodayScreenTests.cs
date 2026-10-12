using AngleSharp.Dom;

using Backlog.Mobile.UI.Components;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// Today, as a person meets it when the app opens: the date, how far through the
/// day they are, the task on now or next, and the rest of My Day in Agenda,
/// Anytime today and Done today — each row ticking itself off through the
/// phone's task edits and opening the task's own page
/// (<c>.devbook/domain/tasks/features.md#my-day</c>).
/// </summary>
public sealed class TodayScreenTests
{
    /// <summary>Wednesday 7 October, ten in the morning, on a phone whose local
    /// zone is UTC — the fake clock's.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 7);

    [Fact]
    public void The_screen_names_the_day_and_with_nothing_picked_says_where_picking_happens()
    {
        var tasks = new ScriptedTaskService(TestTasks.Task("Picked yesterday", Now, inMyDayOn: Today.AddDays(-1)));
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open("");

        app.WaitForAssertion(() => Assert.Equal(1, tasks.Pulls));

        // "Wednesday 7 October" on an English phone; the phone's own culture
        // names the day, so the expectation is built the same way.
        Assert.Equal(
            Now.ToString("dddd d MMMM", System.Globalization.CultureInfo.CurrentCulture),
            app.Find("[data-testid='today-date']").TextContent);
        Assert.Equal("Today", app.Find("h1").TextContent);

        var empty = app.Find("[data-testid='tasks-empty']");
        Assert.Contains("Nothing picked for today", empty.TextContent);
        Assert.Empty(app.FindAll("[data-testid='today-progress']"));
        Assert.Empty(app.FindAll("[data-testid='today-card']"));

        // Adding moved to the capture button; the read-only notice went with it.
        Assert.Empty(app.FindAll("[data-testid='task-add-field']"));
        Assert.Empty(app.FindAll("[data-testid='tasks-read-only']"));
        Assert.Equal("capture", app.Find("[data-testid='today-capture']").GetAttribute("href"));
    }

    [Fact]
    public void My_Day_is_grouped_into_the_card_agenda_anytime_and_done()
    {
        var current = Timed(TestTasks.Task("Write sync conflict tests", Now, inMyDayOn: Today), "09:45", 45);
        var afternoon = Timed(TestTasks.Task("Plan sprint goals", Now, inMyDayOn: Today), "14:00", 30);
        var missed = Timed(TestTasks.Task("Stand-up", Now, inMyDayOn: Today), "08:00", 15);
        var urgent = TestTasks.Task("Reply to the design feedback", Now, inMyDayOn: Today, priority: "high");
        var later = TestTasks.Task("Book the offsite venue", Now, inMyDayOn: Today, priority: "low");
        var ticked = Ticked(TestTasks.Task("Triage the inbox", Now, inMyDayOn: Today, status: "done"));
        var yesterday = TestTasks.Task("Picked yesterday", Now, inMyDayOn: Today.AddDays(-1));
        var archived = TestTasks.Task("Archived", Now, inMyDayOn: Today, status: "archived");

        var tasks = new ScriptedTaskService(later, afternoon, ticked, current, urgent, missed, yesterday, archived);
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open("");

        app.WaitForAssertion(() =>
        {
            Assert.Equal("Now · until 10:30", app.Find("[data-testid='today-card-kicker']").TextContent);
            Assert.Equal("Write sync conflict tests", app.Find("[data-testid='today-card-title']").TextContent);

            Assert.Equal(["Stand-up", "Plan sprint goals"], Titles(app, "agenda"));
            Assert.Equal(["08:00–08:15", "14:00–14:30"], Metas(app, "agenda"));
            Assert.Equal(["Reply to the design feedback", "Book the offsite venue"], Titles(app, "anytime"));
            Assert.Equal(["Triage the inbox"], Titles(app, "done"));

            Assert.Equal("1 of 6 done", app.Find("[data-testid='today-progress-label']").TextContent.Trim());
            var bar = app.Find("[data-testid='today-progress'] [role='progressbar']");
            Assert.Equal("6", bar.GetAttribute("aria-valuemax"));
            Assert.Equal("1", bar.GetAttribute("aria-valuenow"));
        });

        Assert.DoesNotContain("Picked yesterday", app.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Archived", app.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Between_blocks_the_card_shows_the_next_one_and_turns_to_now_when_it_starts()
    {
        var clock = new FakeTimeProvider(Now.AddHours(2));
        var morning = Timed(TestTasks.Task("Stand-up", Now, inMyDayOn: Today), "09:00", 15);
        var afternoon = Timed(TestTasks.Task("Plan sprint goals", Now, inMyDayOn: Today), "14:00", 45);
        var tasks = new ScriptedTaskService(morning, afternoon);
        using var host = ShellHost.Paired(clock: clock, tasks: tasks);
        var app = host.Open("");

        app.WaitForAssertion(() =>
        {
            Assert.Equal("Next · 14:00", app.Find("[data-testid='today-card-kicker']").TextContent);
            Assert.Equal("Plan sprint goals", app.Find("[data-testid='today-card-title']").TextContent);
            Assert.DoesNotContain("now-card--current", app.Find("[data-testid='today-card']").ClassList);
            Assert.Equal(["Stand-up"], Titles(app, "agenda"));
        });

        clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(5));

        app.WaitForAssertion(() =>
        {
            Assert.Equal("Now · until 14:45", app.Find("[data-testid='today-card-kicker']").TextContent);
            Assert.Contains("now-card--current", app.Find("[data-testid='today-card']").ClassList);
        });
    }

    [Fact]
    public void With_no_timed_task_left_there_is_no_card()
    {
        var past = Timed(TestTasks.Task("Stand-up", Now, inMyDayOn: Today), "08:00", 15);
        var anytime = TestTasks.Task("Reply to the design feedback", Now, inMyDayOn: Today);
        var timedButDone = Ticked(Timed(TestTasks.Task("Plan sprint goals", Now, inMyDayOn: Today), "14:00", 30));
        var tasks = new ScriptedTaskService(past, anytime, timedButDone);
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open("");

        app.WaitForAssertion(() =>
        {
            Assert.Equal(["Stand-up"], Titles(app, "agenda"));
            Assert.Equal(["Reply to the design feedback"], Titles(app, "anytime"));
            Assert.Equal(["Plan sprint goals"], Titles(app, "done"));
        });

        Assert.Empty(app.FindAll("[data-testid='today-card']"));
    }

    [Fact]
    public void The_card_shows_the_first_open_step_and_where_the_task_is_in_its_steps()
    {
        var id = Guid.CreateVersion7();
        var current = Timed(TestTasks.Task(
            "Write sync conflict tests", Now, inMyDayOn: Today, id: id,
            subItems:
            [
                new SubItemPayload(Guid.NewGuid(), "Write the fixtures", "done", null, 0),
                new SubItemPayload(Guid.NewGuid(), "Cover the offline-edit case", "pending", null, 1),
                new SubItemPayload(Guid.NewGuid(), "Run it on CI", "pending", null, 2)
            ]), "09:45", 45);
        var tasks = new ScriptedTaskService(current);
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open("");

        app.WaitForAssertion(() =>
        {
            Assert.Equal("Step 2 of 3", app.Find("[data-testid='today-card-step']").TextContent);
            Assert.Equal("Next: Cover the offline-edit case", app.Find("[data-testid='today-card-next']").TextContent);

            var segments = app.FindAll("[data-testid='today-card-steps'] span");
            Assert.Equal(3, segments.Count);
            Assert.Contains("now-card__segment--done", segments[0].ClassList);
            Assert.Contains("now-card__segment--current", segments[1].ClassList);

            Assert.Equal($"tasks/{id}/focus", app.Find("[data-testid='today-card-focus']").GetAttribute("href"));
            Assert.Equal($"tasks/{id}", app.Find("[data-testid='today-card-details']").GetAttribute("href"));
        });
    }

    [Fact]
    public void Ticking_a_row_moves_it_to_done_and_queues_the_edit_and_unticking_brings_it_back()
    {
        var id = Guid.CreateVersion7();
        var tasks = new ScriptedTaskService(
            TestTasks.Task("Reply to the design feedback", Now, inMyDayOn: Today, id: id),
            TestTasks.Task("Book the offsite venue", Now, inMyDayOn: Today));
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open("");

        app.WaitForAssertion(() => Assert.Equal(2, Titles(app, "anytime").Count));

        // Offline from here, so each edit waits in the outbox where it can be seen.
        tasks.State = InboxServiceState.Unreachable;

        var row = Row(app, "Reply to the design feedback");
        Assert.Equal($"tasks/{id}", row.QuerySelector("[data-testid='task-row-open']")!.GetAttribute("href"));
        Assert.Equal("Mark done: Reply to the design feedback", row.QuerySelector("[data-testid='task-row-tick']")!.GetAttribute("aria-label"));

        app.Find("[data-testid='today-anytime'] [data-testid='task-row-tick']").Click();

        app.WaitForAssertion(() =>
        {
            Assert.Equal(["Reply to the design feedback"], Titles(app, "done"));
            Assert.Equal(["Book the offsite venue"], Titles(app, "anytime"));
            Assert.Equal("1 of 2 done", app.Find("[data-testid='today-progress-label']").TextContent.Trim());

            var done = Row(app, "Reply to the design feedback");
            Assert.Equal("true", done.GetAttribute("data-done"));
            Assert.Equal("true", done.GetAttribute("data-waiting"));
            Assert.NotNull(done.QuerySelector("[data-testid='task-waiting']"));
        });

        var ticked = host.Service<Backlog.Mobile.UI.Tasks.TaskViewProjection>().Find(id)!;
        Assert.Equal(Today, ticked.Task.CompletedOn);
        Assert.Equal("task", Assert.Single(host.Outbox.Entries).Kind);

        app.Find("[data-testid='today-done'] [data-testid='task-row-tick']").Click();

        app.WaitForAssertion(() =>
        {
            Assert.Empty(app.FindAll("[data-testid='today-done']"));
            Assert.Equal(["Reply to the design feedback", "Book the offsite venue"], Titles(app, "anytime"));
            Assert.Equal("0 of 2 done", app.Find("[data-testid='today-progress-label']").TextContent.Trim());
        });

        Assert.Null(host.Service<Backlog.Mobile.UI.Tasks.TaskViewProjection>().Find(id)!.Task.CompletedOn);
        Assert.Equal(2, host.Outbox.Entries.Count);
    }

    [Fact]
    public async Task Ticking_the_card_task_takes_the_card_to_the_next_block()
    {
        var current = Timed(TestTasks.Task("Write sync conflict tests", Now, inMyDayOn: Today), "09:45", 45);
        var afternoon = Timed(TestTasks.Task("Plan sprint goals", Now, inMyDayOn: Today), "14:00", 30);
        var tasks = new ScriptedTaskService(current, afternoon);
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open("");

        app.WaitForAssertion(() => Assert.Equal("Now · until 10:30", app.Find("[data-testid='today-card-kicker']").TextContent));

        // The card carries no tick of its own — Focus and Details are where the
        // task is finished — so it is ticked through the same service the rows use.
        var id = host.Service<Backlog.Mobile.UI.Tasks.TaskViewProjection>().Day(Today)
            .Single(row => row.Task.Title == "Write sync conflict tests").Id;
        await host.Service<Backlog.Mobile.UI.Tasks.TaskEdits>().MarkDoneAsync(id, TestContext.Current.CancellationToken);

        app.WaitForAssertion(() =>
        {
            Assert.Equal("Next · 14:00", app.Find("[data-testid='today-card-kicker']").TextContent);
            Assert.Empty(app.FindAll("[data-testid='today-agenda']"));
            Assert.Equal(["Write sync conflict tests"], Titles(app, "done"));
        });
    }

    [Fact]
    public void A_desktop_edit_and_delete_reach_the_phone_on_the_next_refresh()
    {
        var id = Guid.CreateVersion7();
        var tasks = new ScriptedTaskService(TestTasks.Task("Call the venue", Now, inMyDayOn: Today, id: id));
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open("");

        app.WaitForAssertion(() => Assert.Equal(["Call the venue"], Titles(app, "anytime")));

        tasks.Append(TestTasks.Task("Call the venue about parking", Now.AddMinutes(1), inMyDayOn: Today, id: id));
        app.Find("[data-testid='tasks-refresh']").Click();
        app.WaitForAssertion(() => Assert.Equal(["Call the venue about parking"], Titles(app, "anytime")));

        tasks.Append(TestTasks.Task("Call the venue about parking", Now.AddMinutes(2), inMyDayOn: Today, id: id, deletedAt: Now.AddMinutes(2)));
        app.Find("[data-testid='tasks-refresh']").Click();
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='tasks-empty']")));
    }

    [Fact]
    public void When_sync_cannot_be_reached_the_day_stays_on_screen_and_says_so()
    {
        var tasks = new ScriptedTaskService { State = InboxServiceState.Unreachable };
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open("");

        app.WaitForAssertion(() => Assert.Contains(
            "Cloud sync can't be reached.",
            app.Find("[data-testid='tasks-pull-notice']").TextContent,
            StringComparison.Ordinal));
    }

    private static TaskChange Timed(TaskChange change, string at, int minutes) =>
        change with { Task = change.Task with { AgendaAt = at, AgendaMinutes = minutes } };

    private static TaskChange Ticked(TaskChange change) =>
        change with { Task = change.Task with { CompletedOn = Today } };

    private static List<string> Titles(IRenderedComponent<Routes> app, string group) =>
        [.. app.FindAll($"[data-testid='today-{group}'] [data-testid='task-row-title']").Select(title => title.TextContent)];

    private static List<string> Metas(IRenderedComponent<Routes> app, string group) =>
        [.. app.FindAll($"[data-testid='today-{group}'] [data-testid='task-row-meta']").Select(meta => meta.TextContent)];

    private static IElement Row(IRenderedComponent<Routes> app, string title) =>
        app.FindAll("[data-testid='task-row']")
            .Single(row => row.QuerySelector("[data-testid='task-row-title']")!.TextContent == title);
}
