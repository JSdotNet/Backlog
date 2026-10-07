using Backlog.Mobile.UI.Components;
using Backlog.Mobile.UI.Tasks;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// Focus on one timed task, as a person reaches it from Today's card or the
/// task's own page: a countdown to the end of its agenda block read from the
/// clock every second, the current step ticked off through the phone's task
/// edits, and Complete task, which goes back to Today
/// (<c>.devbook/domain/tasks/features.md#my-day</c>).
/// </summary>
public sealed class FocusScreenTests
{
    /// <summary>Wednesday 7 October, ten in the morning, on a phone whose local
    /// zone is UTC — the fake clock's. The task's block runs 09:45–10:30.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 7);

    private static readonly Guid Id = Guid.Parse("0199b6a0-0000-7000-8000-0000000000fa");
    private static readonly Guid First = Guid.Parse("0199b6a0-0000-7000-8000-0000000000f1");
    private static readonly Guid Second = Guid.Parse("0199b6a0-0000-7000-8000-0000000000f2");
    private static readonly Guid Third = Guid.Parse("0199b6a0-0000-7000-8000-0000000000f3");

    [Fact]
    public void A_timed_task_shows_its_block_title_countdown_and_current_step()
    {
        var tasks = new ScriptedTaskService(Timed(Sample()));
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open($"tasks/{Id}/focus");

        app.WaitForAssertion(() =>
            Assert.Equal("Write sync conflict tests", app.Find("h1[data-testid='focus-title']").TextContent));

        Assert.Equal("Sync, until 10:30", Words(app.Find("[data-testid='focus-kicker']")));
        Assert.Equal("30:00", app.Find("[data-testid='focus-time']").TextContent);
        Assert.Equal("false", app.Find("[data-testid='focus-ring']").GetAttribute("data-over"));

        Assert.Equal("Step 2 of 3 · now", app.Find("[data-testid='focus-step-number']").TextContent);
        Assert.Equal("Cover the offline-edit case", app.Find("[data-testid='focus-step-title']").TextContent);
        Assert.Equal(
            ["done", "current", "todo"],
            app.FindAll("[data-testid='focus-step']").Select(step => step.GetAttribute("data-state")));

        Assert.Equal("Step done — next", app.Find("[data-testid='focus-step-done']").TextContent.Trim());
        Assert.Empty(app.FindAll("[data-testid='focus-complete']"));

        // Back goes to Today, Task details to the task, and there is no Pause:
        // the countdown is the block itself.
        Assert.Equal(string.Empty, app.Find("[data-testid='focus-back']").GetAttribute("href"));
        Assert.Equal($"tasks/{Id}", app.Find("[data-testid='focus-details']").GetAttribute("href"));
        Assert.DoesNotContain("Pause", app.Find("[data-testid='focus']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_countdown_moves_every_second_with_the_clock()
    {
        var clock = new FakeTimeProvider(Now);
        var tasks = new ScriptedTaskService(Timed(Sample()));
        using var host = ShellHost.Paired(clock: clock, tasks: tasks);
        var app = host.Open($"tasks/{Id}/focus");

        app.WaitForAssertion(() => Assert.Equal("30:00", app.Find("[data-testid='focus-time']").TextContent));
        var fullOffset = Offset(app);

        clock.Advance(TimeSpan.FromSeconds(1));
        app.WaitForAssertion(() => Assert.Equal("29:59", app.Find("[data-testid='focus-time']").TextContent));

        clock.Advance(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(59));
        app.WaitForAssertion(() => Assert.Equal("15:00", app.Find("[data-testid='focus-time']").TextContent));

        // A third of the block is left: the ring has emptied by the rest.
        Assert.True(Offset(app) > fullOffset);
        Assert.Equal(2 * Math.PI * 88 * 2 / 3, Offset(app), precision: 1);
    }

    [Fact]
    public void After_the_block_ends_the_countdown_reads_how_far_over()
    {
        var clock = new FakeTimeProvider(Now);
        var tasks = new ScriptedTaskService(Timed(Sample()));
        using var host = ShellHost.Paired(clock: clock, tasks: tasks);
        var app = host.Open($"tasks/{Id}/focus");

        app.WaitForAssertion(() => Assert.Equal("30:00", app.Find("[data-testid='focus-time']").TextContent));

        clock.Advance(TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(192));

        app.WaitForAssertion(() =>
        {
            Assert.Equal("Over by 03:12", app.Find("[data-testid='focus-time']").TextContent);
            Assert.Equal("true", app.Find("[data-testid='focus-ring']").GetAttribute("data-over"));
        });

        // Over is not done: the step is still there to tick.
        Assert.NotNull(app.Find("[data-testid='focus-step-done']"));
    }

    [Fact]
    public void Step_done_ticks_the_current_step_and_moves_to_the_next()
    {
        var tasks = new ScriptedTaskService(Timed(Sample()));
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open($"tasks/{Id}/focus");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='focus-step-done']")));

        // Offline from here, so the edit waits in the outbox where it can be seen.
        tasks.State = InboxServiceState.Unreachable;

        app.Find("[data-testid='focus-step-done']").Click();

        app.WaitForAssertion(() =>
        {
            Assert.Equal("Step 3 of 3 · now", app.Find("[data-testid='focus-step-number']").TextContent);
            Assert.Equal("Run the suite", app.Find("[data-testid='focus-step-title']").TextContent);
        });

        var row = host.Service<TaskViewProjection>().Find(Id)!;
        Assert.Equal("done", row.Task.SubItems.Single(step => step.Id == Second).Status);
        Assert.Equal("pending", row.Task.SubItems.Single(step => step.Id == Third).Status);
        Assert.Equal("task", Assert.Single(host.Outbox.Entries).Kind);

        // Still on Focus: a step is not the task finished.
        Assert.EndsWith($"tasks/{Id}/focus", host.Navigation.Uri, StringComparison.Ordinal);
        Assert.Null(row.Task.CompletedOn);
    }

    [Fact]
    public void With_no_step_left_Complete_task_marks_it_done_and_goes_back_to_Today()
    {
        var tasks = new ScriptedTaskService(Timed(Sample()));
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open($"tasks/{Id}/focus");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='focus-step-done']")));
        tasks.State = InboxServiceState.Unreachable;

        app.Find("[data-testid='focus-step-done']").Click();
        app.WaitForAssertion(() => Assert.Equal("Step 3 of 3 · now", app.Find("[data-testid='focus-step-number']").TextContent));

        app.Find("[data-testid='focus-step-done']").Click();

        app.WaitForAssertion(() =>
        {
            Assert.Equal("Complete task", app.Find("[data-testid='focus-complete']").TextContent.Trim());
            Assert.Equal("All 3 steps done", app.Find("[data-testid='focus-step-number']").TextContent);
        });
        Assert.Empty(app.FindAll("[data-testid='focus-step-done']"));
        Assert.Null(host.Service<TaskViewProjection>().Find(Id)!.Task.CompletedOn);

        app.Find("[data-testid='focus-complete']").Click();

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
    public async Task A_task_moved_elsewhere_while_open_leaves_Focus_for_its_own_page()
    {
        var tasks = new ScriptedTaskService(Timed(Sample()));
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open($"tasks/{Id}/focus");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='focus']")));
        tasks.State = InboxServiceState.Unreachable;

        // As if from another screen or a pull: the task is no longer today's.
        await app.InvokeAsync(() => host.Service<TaskEdits>().MoveToTomorrowAsync(Id));

        app.WaitForAssertion(() =>
        {
            Assert.EndsWith($"tasks/{Id}", host.Navigation.Uri, StringComparison.Ordinal);
            Assert.NotNull(app.Find("[data-testid='task-detail']"));
        });
    }

    [Fact]
    public void A_task_with_no_steps_offers_Complete_task_at_once()
    {
        var change = Timed(Sample());
        change = change with { Task = change.Task with { SubItems = [], ContentMd = "Just do it." } };
        var tasks = new ScriptedTaskService(change);
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open($"tasks/{Id}/focus");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='focus-complete']")));
        Assert.Empty(app.FindAll("[data-testid='focus-current']"));
        Assert.Empty(app.FindAll("[data-testid='focus-steps']"));
    }

    [Fact]
    public void Capture_a_thought_opens_the_capture_sheet_from_Focus()
    {
        var tasks = new ScriptedTaskService(Timed(Sample()));
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open($"tasks/{Id}/focus");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='focus-capture']")));

        Assert.Equal(
            $"capture?from=tasks%2F{Id}%2Ffocus",
            app.Find("[data-testid='focus-capture']").GetAttribute("href"));
    }

    [Fact]
    public void An_untimed_task_goes_to_its_own_page()
    {
        var tasks = new ScriptedTaskService(Sample());
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open($"tasks/{Id}/focus");

        app.WaitForAssertion(() =>
        {
            Assert.EndsWith($"tasks/{Id}", host.Navigation.Uri, StringComparison.Ordinal);
            Assert.NotNull(app.Find("[data-testid='task-detail']"));
        });
        Assert.Empty(app.FindAll("[data-testid='focus']"));
    }

    [Fact]
    public void A_task_ticked_off_today_goes_to_its_own_page()
    {
        var change = Timed(Sample());
        change = change with { Task = change.Task with { CompletedOn = Today, Status = "done" } };
        var tasks = new ScriptedTaskService(change);
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open($"tasks/{Id}/focus");

        app.WaitForAssertion(() => Assert.EndsWith($"tasks/{Id}", host.Navigation.Uri, StringComparison.Ordinal));
    }

    [Fact]
    public void Another_days_block_goes_to_its_own_page()
    {
        var change = Timed(Sample());
        change = change with { Task = change.Task with { InMyDayOn = Today.AddDays(-1) } };
        var tasks = new ScriptedTaskService(change);
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open($"tasks/{Id}/focus");

        app.WaitForAssertion(() => Assert.EndsWith($"tasks/{Id}", host.Navigation.Uri, StringComparison.Ordinal));
    }

    [Fact]
    public void A_task_the_phone_does_not_hold_goes_to_its_page_which_says_so()
    {
        var tasks = new ScriptedTaskService(Sample());
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var missing = Guid.CreateVersion7();
        var app = host.Open($"tasks/{missing}/focus");

        app.WaitForAssertion(() =>
        {
            Assert.EndsWith($"tasks/{missing}", host.Navigation.Uri, StringComparison.Ordinal);
            Assert.NotNull(app.Find("[data-testid='task-detail-missing']"));
        });
    }

    [Fact]
    public void Todays_card_opens_Focus()
    {
        var tasks = new ScriptedTaskService(Timed(Sample()));
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open("");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='today-card-focus']")));

        host.Navigation.NavigateTo(app.Find("[data-testid='today-card-focus']").GetAttribute("href")!);

        app.WaitForAssertion(() => Assert.Equal("30:00", app.Find("[data-testid='focus-time']").TextContent));
    }

    /// <summary>The kicker as it is read aloud: the separator dot is decoration,
    /// the comma is for the ear, and whitespace runs collapse.</summary>
    private static string Words(AngleSharp.Dom.IElement kicker)
    {
        var copy = (AngleSharp.Dom.IElement)kicker.Clone();
        foreach (var hidden in copy.QuerySelectorAll("[aria-hidden='true']").ToList()) hidden.Remove();
        return string.Join(' ', copy.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static double Offset(IRenderedComponent<Routes> app) =>
        double.Parse(
            app.Find("[data-testid='focus-ring-fill']").GetAttribute("stroke-dashoffset")!,
            System.Globalization.CultureInfo.InvariantCulture);

    private static TaskChange Sample()
    {
        var change = TestTasks.Task(
            "Write sync conflict tests", Now, inMyDayOn: Today, id: Id,
            contentMd: """
                ## List the conflict cases
                `task` `!done`

                ## Cover the offline-edit case

                ## Run the suite
                """,
            subItems:
            [
                new SubItemPayload(First, "List the conflict cases", "done", null, 0),
                new SubItemPayload(Second, "Cover the offline-edit case", "pending", null, 1),
                new SubItemPayload(Third, "Run the suite", "pending", null, 2)
            ]);

        return change with { Task = change.Task with { Area = "Sync" } };
    }

    private static TaskChange Timed(TaskChange change) =>
        change with { Task = change.Task with { AgendaAt = "09:45", AgendaMinutes = 45 } };
}
