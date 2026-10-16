using Backlog.Mobile.UI.Components;
using Backlog.Mobile.UI.Tasks;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The phone's screens with Clear My Day overnight turned off on a desktop
/// (.devbook/arc42/adr/0032, §4 and §5; <c>.devbook/domain/tasks/requirements.md#my-day</c>):
/// a task picked on an earlier day and not yet finished is in Today, under Anytime today
/// with no block; it can be finished or moved to tomorrow from its page; and Focus, which
/// needs today's block, does not open on it.
/// </summary>
public sealed class CarriedTaskScreenTests
{
    /// <summary>Wednesday 7 October, ten in the morning, on a phone whose local zone is
    /// UTC — the fake clock's.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 7);
    private static readonly DateOnly Yesterday = Today.AddDays(-1);

    private static readonly Guid Id = Guid.Parse("0199b6a0-0000-7000-8000-0000000000ca");
    private static readonly Guid PreferencesId = Guid.Parse("4d7a9c2e-5b1f-4e3a-8c6d-2f0e1a3b5c01");

    [Fact]
    public void With_the_switch_off_a_carried_task_is_under_Anytime_today_without_its_block()
    {
        var tasks = new ScriptedTaskService(
            Preferences(clearsOvernight: false),
            Carried("Finish the review", "09:45", 45),
            Ticked(TestTasks.Task("Carried, done today", Now, inMyDayOn: Yesterday, status: "done"), Today),
            Ticked(TestTasks.Task("Carried, done yesterday", Now, inMyDayOn: Yesterday, status: "done"), Yesterday));
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open("");

        app.WaitForAssertion(() =>
        {
            Assert.Equal(["Finish the review"], Titles(app, "anytime"));
            Assert.Equal(["Carried, done today"], Titles(app, "done"));
        });

        Assert.Empty(app.FindAll("[data-testid='today-card']"));
        Assert.Empty(app.FindAll("[data-testid='today-agenda'] [data-testid='task-row-title']"));
        Assert.DoesNotContain("09:45", app.Find("[data-testid='today-anytime']").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Carried, done yesterday", app.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void With_the_switch_on_a_carried_task_is_not_in_Today()
    {
        var tasks = new ScriptedTaskService(Preferences(clearsOvernight: true), Carried("Finish the review"));
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open("");

        app.WaitForAssertion(() => Assert.Contains("Nothing picked for today", app.Find("[data-testid='tasks-empty']").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain("Finish the review", app.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_carried_tasks_page_offers_Mark_done_and_Move_to_tomorrow_but_no_time_or_Focus()
    {
        var tasks = new ScriptedTaskService(Preferences(clearsOvernight: false), Carried("Finish the review", "09:45", 45));
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open($"tasks/{Id}");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='task-detail-done']")));

        Assert.NotNull(app.Find("[data-testid='task-detail-tomorrow']"));
        Assert.Empty(app.FindAll("[data-testid='task-detail-time']"));
        Assert.Empty(app.FindAll("[data-testid='task-detail-focus']"));
    }

    [Fact]
    public void Mark_done_on_a_carried_task_puts_it_under_Done_today()
    {
        var tasks = new ScriptedTaskService(Preferences(clearsOvernight: false), Carried("Finish the review"));
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open($"tasks/{Id}");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='task-detail-done']")));
        tasks.State = InboxServiceState.Unreachable;

        app.Find("[data-testid='task-detail-done']").Click();

        app.WaitForAssertion(() =>
        {
            Assert.Equal(host.Navigation.BaseUri, host.Navigation.Uri);
            Assert.Equal(["Finish the review"], Titles(app, "done"));
        });

        Assert.Equal(Today, host.Service<TaskViewProjection>().Find(Id)!.Task.CompletedOn);
    }

    [Fact]
    public void Move_to_tomorrow_on_a_carried_task_takes_it_out_of_today()
    {
        var tasks = new ScriptedTaskService(Preferences(clearsOvernight: false), Carried("Finish the review"));
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

        Assert.DoesNotContain("Finish the review", app.Markup, StringComparison.Ordinal);
        Assert.Equal(Today.AddDays(1), host.Service<TaskViewProjection>().Find(Id)!.Task.InMyDayOn);
    }

    [Fact]
    public void Focus_on_a_carried_task_with_a_block_goes_to_its_own_page()
    {
        var tasks = new ScriptedTaskService(Preferences(clearsOvernight: false), Carried("Finish the review", "09:45", 45));
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open($"tasks/{Id}/focus");

        app.WaitForAssertion(() => Assert.EndsWith($"tasks/{Id}", host.Navigation.Uri, StringComparison.Ordinal));
        Assert.Empty(app.FindAll("[data-testid='focus']"));
    }

    private static TaskChange Preferences(bool clearsOvernight) =>
        TestTasks.Task(
            "My Day preferences",
            Now,
            type: "my-day-preferences",
            id: PreferencesId,
            contentMd: $$"""{ "updatedAt": "{{Now:O}}", "clearsOvernight": {{(clearsOvernight ? "true" : "false")}} }""");

    private static TaskChange Carried(string title, string? at = null, int? minutes = null)
    {
        var change = TestTasks.Task(title, Now, inMyDayOn: Yesterday, id: Id);
        return change with { Task = change.Task with { AgendaAt = at, AgendaMinutes = minutes } };
    }

    private static TaskChange Ticked(TaskChange change, DateOnly completedOn) =>
        change with { Task = change.Task with { CompletedOn = completedOn } };

    private static List<string> Titles(IRenderedComponent<Routes> app, string group) =>
        [.. app.FindAll($"[data-testid='today-{group}'] [data-testid='task-row-title']").Select(title => title.TextContent)];
}
