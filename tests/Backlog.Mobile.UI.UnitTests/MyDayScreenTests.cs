using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The Tasks tab as a person meets it: My Day and nothing else, read-only and
/// saying so, one field that adds a task picked for today, and a row that opens
/// into the whole task.
/// </summary>
public sealed class MyDayScreenTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 25);

    [Fact]
    public void With_nothing_picked_it_says_so_and_how_to_pick()
    {
        var tasks = new ScriptedTaskService(TestTasks.Task("Due today, not picked", Now, dueOn: Today));
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open("tasks");

        app.WaitForAssertion(() => Assert.Equal(1, tasks.Pulls));

        var empty = app.Find("[data-testid='tasks-empty']");
        Assert.Contains("Nothing picked for today.", empty.TextContent);
        Assert.Contains("Add one below, or pick tasks on the desktop.", empty.TextContent);
        Assert.Equal("Only adding happens here — edit on the desktop.", app.Find("[data-testid='tasks-read-only']").TextContent.Trim());
    }

    [Fact]
    public void A_row_shows_title_priority_due_date_and_sub_item_progress()
    {
        var tasks = new ScriptedTaskService(TestTasks.Task(
            "Prepare the demo", Now, inMyDayOn: Today, dueOn: Today.AddDays(7), priority: "high",
            subItems: [new SubItemPayload(Guid.NewGuid(), "Slides", "done", null, 0), new SubItemPayload(Guid.NewGuid(), "Laptop", "pending", null, 1)]));
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open("tasks");

        app.WaitForAssertion(() =>
        {
            var row = Assert.Single(app.FindAll("[data-testid='task-row']"));
            Assert.Equal("Prepare the demo", row.QuerySelector("[data-testid='task-title']")!.TextContent);
            Assert.Equal("High", row.QuerySelector("[data-testid='task-priority']")!.TextContent.Trim());
            Assert.StartsWith("Due ", row.QuerySelector("[data-testid='task-due']")!.TextContent.Trim(), StringComparison.Ordinal);
            Assert.Equal("1/2", row.QuerySelector("[data-testid='task-progress']")!.TextContent.Trim());
            Assert.Equal("false", row.GetAttribute("data-waiting"));
        });
    }

    [Fact]
    public void Tapping_a_row_opens_the_whole_task_read_only()
    {
        var tasks = new ScriptedTaskService(TestTasks.Task(
            "Prepare the demo", Now, inMyDayOn: Today, contentMd: "Walk through **sync**.",
            tags: ["#demo"], repoIds: ["JSdotNet/Backlog"],
            subItems: [new SubItemPayload(Guid.NewGuid(), "Slides", "done", null, 0)]));
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open("tasks");

        app.WaitForAssertion(() => app.Find("[data-testid='task-open']").Click());

        app.WaitForAssertion(() =>
        {
            var sheet = app.Find("[data-testid='task-sheet']");
            Assert.Contains("Walk through", sheet.QuerySelector("[data-testid='task-sheet-body']")!.TextContent);
            Assert.Contains("demo", sheet.QuerySelector("[data-testid='task-sheet-tags']")!.TextContent);
            Assert.Equal("JSdotNet/Backlog", sheet.QuerySelector("[data-testid='task-sheet-repositories']")!.TextContent.Trim());
            Assert.Contains("Slides (done)", sheet.QuerySelector("[data-testid='task-sheet-sub-items']")!.TextContent);

            // Nothing in the sheet edits: no field, no checkbox, no save.
            Assert.Empty(sheet.QuerySelectorAll("input, textarea, select"));
        });
    }

    /// <summary>A task's body carries its sub-items as <c>##</c> chapters. The sheet
    /// lists them once, under Sub-items, and shows only the prose above them as the
    /// body — and no body at all when there is none.</summary>
    [Theory]
    [InlineData("Walk through the sync.\n\n## Slides\n\n## Laptop\n", "Walk through the sync.")]
    [InlineData("## Slides\n\n## Laptop\n", null)]
    public void The_sheet_body_is_the_prose_above_the_sub_items_not_the_sub_items_again(string contentMd, string? expectedBody)
    {
        var tasks = new ScriptedTaskService(TestTasks.Task(
            "Prepare the demo", Now, inMyDayOn: Today, contentMd: contentMd,
            subItems: [new SubItemPayload(Guid.NewGuid(), "Slides", "done", null, 0), new SubItemPayload(Guid.NewGuid(), "Laptop", "pending", null, 1)]));
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open("tasks");

        app.WaitForAssertion(() => app.Find("[data-testid='task-open']").Click());

        app.WaitForAssertion(() =>
        {
            var sheet = app.Find("[data-testid='task-sheet']");
            var body = sheet.QuerySelector("[data-testid='task-sheet-body']");

            if (expectedBody is null)
            {
                Assert.Null(body);
            }
            else
            {
                Assert.Equal(expectedBody, body!.TextContent.Trim());
            }

            Assert.Equal(2, sheet.QuerySelectorAll("[data-testid='task-sheet-sub-items'] li").Length);
        });
    }

    [Fact]
    public void A_task_added_offline_is_in_My_Day_at_once_marked_waiting()
    {
        var tasks = new ScriptedTaskService { State = InboxServiceState.Unreachable };
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open("tasks");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='tasks-pull-notice']")));

        app.Find("[data-testid='task-add-field'] input").Input("Buy a charger");
        app.Find("[data-testid='task-add-submit']").Click();

        app.WaitForAssertion(() =>
        {
            var row = Assert.Single(app.FindAll("[data-testid='task-row']"));
            Assert.Equal("true", row.GetAttribute("data-waiting"));
            Assert.Contains("Buy a charger", row.TextContent);
            Assert.NotNull(row.QuerySelector("[data-testid='task-waiting']"));
        });

        var entry = Assert.Single(host.Outbox.Entries);
        Assert.Equal("task", entry.Kind);
        Assert.Empty(host.Inbox.Received);
        Assert.Equal(string.Empty, app.Find("[data-testid='task-add-field'] input").GetAttribute("value") ?? string.Empty);
    }

    [Fact]
    public void An_added_task_stops_waiting_once_the_service_takes_it()
    {
        var clock = new FakeTimeProvider(Now);
        var tasks = new ScriptedTaskService { State = InboxServiceState.Unreachable };
        using var host = ShellHost.Paired(clock: clock, tasks: tasks);
        var app = host.Open("tasks");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='tasks-pull-notice']")));
        app.Find("[data-testid='task-add-field'] input").Input("Buy a charger");
        app.Find("[data-testid='task-add-submit']").Click();
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='task-waiting']")));

        tasks.State = InboxServiceState.Answering;
        clock.Advance(TimeSpan.FromSeconds(2));

        app.WaitForAssertion(() =>
        {
            var row = Assert.Single(app.FindAll("[data-testid='task-row']"));
            Assert.Equal("false", row.GetAttribute("data-waiting"));
            Assert.Empty(app.FindAll("[data-testid='tasks-pull-notice']"));
        });

        Assert.All(tasks.Pushed, change => Assert.Equal(Today, change.Task.InMyDayOn));
    }

    [Fact]
    public void A_desktop_edit_and_delete_reach_the_phone_on_the_next_refresh()
    {
        var id = Guid.CreateVersion7();
        var tasks = new ScriptedTaskService(TestTasks.Task("Call the venue", Now, inMyDayOn: Today, id: id));
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now), tasks: tasks);
        var app = host.Open("tasks");

        app.WaitForAssertion(() => Assert.Contains("Call the venue", app.Find("[data-testid='task-row']").TextContent));

        tasks.Append(TestTasks.Task("Call the venue about parking", Now.AddMinutes(1), inMyDayOn: Today, id: id));
        app.Find("[data-testid='tasks-refresh']").Click();
        app.WaitForAssertion(() => Assert.Contains("about parking", app.Find("[data-testid='task-row']").TextContent));

        tasks.Append(TestTasks.Task("Call the venue about parking", Now.AddMinutes(2), inMyDayOn: Today, id: id, deletedAt: Now.AddMinutes(2)));
        app.Find("[data-testid='tasks-refresh']").Click();
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='tasks-empty']")));
    }
}
