using Backlog.Mobile.UI.Components;
using Backlog.Mobile.UI.Tasks;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// A task's own page, as a person reaches it from Today: its chips, its steps
/// ticking through the phone's task edits, its own text read-only, and Move to
/// tomorrow and Mark done, which both go back to Today
/// (<c>.devbook/domain/tasks/features.md#my-day</c>).
/// </summary>
public sealed class TaskScreenTests
{
    /// <summary>Wednesday 7 October, ten in the morning, on a phone whose local
    /// zone is UTC — the fake clock's.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 7);

    private static readonly Guid Id = Guid.Parse("0199b6a0-0000-7000-8000-0000000000aa");
    private static readonly Guid First = Guid.Parse("0199b6a0-0000-7000-8000-0000000000a1");
    private static readonly Guid Second = Guid.Parse("0199b6a0-0000-7000-8000-0000000000a2");
    private static readonly Guid Third = Guid.Parse("0199b6a0-0000-7000-8000-0000000000a3");

    private const string Content = """
        Last write wins for the title; a **delete** beats an edit.

        ## List the conflict cases
        `task` `!done`

        ## Cover the offline-edit case

        ## Run the suite
        """;

    [Fact]
    public void A_timed_task_shows_its_chips_steps_body_and_the_way_into_Focus()
    {
        var tasks = new ScriptedTaskService(Timed(Sample()));
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open($"tasks/{Id}");

        app.WaitForAssertion(() =>
            Assert.Equal("Write sync conflict tests", app.Find("h1[data-testid='task-detail-title']").TextContent));

        Assert.Equal(string.Empty, app.Find("[data-testid='task-detail-back']").GetAttribute("href"));
        Assert.Equal("Area: Sync", app.Find("[data-testid='task-detail-area']").TextContent.Trim());
        Assert.Equal("When: Today · 09:45–10:30", app.Find("[data-testid='task-detail-time']").TextContent);
        Assert.Equal("Effort: 3 points", app.Find("[data-testid='task-detail-effort']").TextContent);
        Assert.Equal($"tasks/{Id}/focus", app.Find("[data-testid='task-detail-focus']").GetAttribute("href"));

        Assert.Equal("1 of 3", app.Find("[data-testid='task-detail-steps-count']").TextContent);
        Assert.Equal(
            ["List the conflict cases", "Cover the offline-edit case", "Run the suite"],
            app.FindAll("[data-testid='task-detail-step-box'] .checkbox__label").Select(label => label.TextContent));

        // The body is the task's own prose; its steps are the checklist above it
        // and are not drawn a second time as headings.
        var body = app.Find("[data-testid='task-detail-body']");
        Assert.Equal("delete", body.QuerySelector("strong")!.TextContent);
        Assert.DoesNotContain("Run the suite", body.TextContent, StringComparison.Ordinal);

        Assert.NotNull(app.Find("[data-testid='task-detail-tomorrow']"));
        Assert.NotNull(app.Find("[data-testid='task-detail-done']"));
    }

    [Fact]
    public void An_untimed_task_has_no_time_chip_and_no_Focus()
    {
        var tasks = new ScriptedTaskService(Sample());
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open($"tasks/{Id}");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='task-detail']")));

        Assert.Empty(app.FindAll("[data-testid='task-detail-time']"));
        Assert.Empty(app.FindAll("[data-testid='task-detail-focus']"));
        Assert.Equal("Area: Sync", app.Find("[data-testid='task-detail-area']").TextContent.Trim());
    }

    [Fact]
    public void With_every_step_done_the_count_is_full()
    {
        var change = Timed(Sample());
        change = change with
        {
            Task = change.Task with
            {
                SubItems = [.. change.Task.SubItems.Select(step => step with { Status = "done" })]
            }
        };
        var tasks = new ScriptedTaskService(change);
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open($"tasks/{Id}");

        app.WaitForAssertion(() => Assert.Equal("3 of 3", app.Find("[data-testid='task-detail-steps-count']").TextContent));
        Assert.All(app.FindAll("[data-testid='task-detail-step']"), row => Assert.Equal("true", row.GetAttribute("data-done")));
    }

    [Fact]
    public void Ticking_a_step_queues_the_edit_and_moves_the_count()
    {
        var tasks = new ScriptedTaskService(Timed(Sample()));
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open($"tasks/{Id}");

        app.WaitForAssertion(() => Assert.Equal("1 of 3", app.Find("[data-testid='task-detail-steps-count']").TextContent));

        // Offline from here, so the edit waits in the outbox where it can be seen.
        tasks.State = InboxServiceState.Unreachable;

        app.FindAll("[data-testid='task-detail-step-box'] input")[1].Change(true);

        app.WaitForAssertion(() =>
        {
            Assert.Equal("2 of 3", app.Find("[data-testid='task-detail-steps-count']").TextContent);
            Assert.NotNull(app.Find("[data-testid='task-detail-waiting']"));
        });

        var row = host.Service<TaskViewProjection>().Find(Id)!;
        Assert.Equal("done", row.Task.SubItems.Single(step => step.Id == Second).Status);
        Assert.Equal("task", Assert.Single(host.Outbox.Entries).Kind);

        // Still on the task: a step is not the task finished.
        Assert.EndsWith($"tasks/{Id}", host.Navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void Move_to_tomorrow_takes_the_task_out_of_today_and_goes_back_to_Today()
    {
        var tasks = new ScriptedTaskService(Timed(Sample()));
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open($"tasks/{Id}");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='task-detail-tomorrow']")));
        tasks.State = InboxServiceState.Unreachable;

        app.Find("[data-testid='task-detail-tomorrow']").Click();

        app.WaitForAssertion(() =>
        {
            Assert.Equal(host.Navigation.BaseUri, host.Navigation.Uri);
            Assert.NotNull(app.Find("[data-testid='today']"));
        });

        var row = host.Service<TaskViewProjection>().Find(Id)!;
        Assert.Equal(Today.AddDays(1), row.Task.InMyDayOn);
        Assert.Null(row.Task.AgendaAt);
        Assert.Equal("task", Assert.Single(host.Outbox.Entries).Kind);
    }

    [Fact]
    public void Mark_done_ticks_the_task_off_and_goes_back_to_Today()
    {
        var tasks = new ScriptedTaskService(Sample());
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open($"tasks/{Id}");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='task-detail-done']")));
        tasks.State = InboxServiceState.Unreachable;

        app.Find("[data-testid='task-detail-done']").Click();

        app.WaitForAssertion(() =>
        {
            Assert.Equal(host.Navigation.BaseUri, host.Navigation.Uri);
            Assert.Equal(
                ["Write sync conflict tests"],
                app.FindAll("[data-testid='today-done'] [data-testid='task-row-title']").Select(title => title.TextContent));
        });

        Assert.Equal(Today, host.Service<TaskViewProjection>().Find(Id)!.Task.CompletedOn);
    }

    [Fact]
    public void A_task_not_in_today_is_shown_and_left_alone()
    {
        var change = Timed(Sample());
        change = change with { Task = change.Task with { InMyDayOn = Today.AddDays(-1) } };
        var tasks = new ScriptedTaskService(change);
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open($"tasks/{Id}");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='task-detail']")));

        // Yesterday's block is not today's: no time chip, and no Focus.
        Assert.Empty(app.FindAll("[data-testid='task-detail-time']"));
        Assert.Empty(app.FindAll("[data-testid='task-detail-focus']"));
        Assert.Empty(app.FindAll("[data-testid='task-detail-footer']"));
        Assert.All(
            app.FindAll("[data-testid='task-detail-step-box'] input"),
            box => Assert.True(box.HasAttribute("disabled")));
    }

    [Fact]
    public void A_task_ticked_off_today_has_no_footer_and_no_Focus()
    {
        var change = Timed(Sample());
        change = change with { Task = change.Task with { CompletedOn = Today, Status = "done" } };
        var tasks = new ScriptedTaskService(change);
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open($"tasks/{Id}");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='task-detail']")));

        Assert.Empty(app.FindAll("[data-testid='task-detail-focus']"));
        Assert.Empty(app.FindAll("[data-testid='task-detail-footer']"));
        Assert.All(
            app.FindAll("[data-testid='task-detail-step-box'] input"),
            box => Assert.True(box.HasAttribute("disabled")));
    }

    [Fact]
    public void A_task_the_phone_does_not_hold_says_so()
    {
        var tasks = new ScriptedTaskService(Sample());
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open($"tasks/{Guid.CreateVersion7()}");

        app.WaitForAssertion(() =>
            Assert.Contains("This task isn't on this phone", app.Find("[data-testid='task-detail-missing']").TextContent, StringComparison.Ordinal));
        Assert.Equal(string.Empty, app.Find("[data-testid='task-detail-missing-back']").GetAttribute("href"));
    }

    [Fact]
    public void A_row_on_Today_opens_the_task()
    {
        var tasks = new ScriptedTaskService(Sample());
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open("");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='today-anytime']")));

        host.Navigation.NavigateTo(app.Find("[data-testid='task-row-open']").GetAttribute("href")!);

        app.WaitForAssertion(() =>
            Assert.Equal("Write sync conflict tests", app.Find("[data-testid='task-detail-title']").TextContent));
    }

    private static TaskChange Sample()
    {
        var change = TestTasks.Task(
            "Write sync conflict tests", Now, inMyDayOn: Today, id: Id, contentMd: Content,
            subItems:
            [
                new SubItemPayload(First, "List the conflict cases", "done", null, 0),
                new SubItemPayload(Second, "Cover the offline-edit case", "pending", null, 1),
                new SubItemPayload(Third, "Run the suite", "pending", null, 2)
            ]);

        return change with { Task = change.Task with { Area = "Sync", Effort = 3 } };
    }

    private static TaskChange Timed(TaskChange change) =>
        change with { Task = change.Task with { AgendaAt = "09:45", AgendaMinutes = 45 } };
}
