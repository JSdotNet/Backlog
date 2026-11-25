using Microsoft.AspNetCore.Components;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The quiet row the Tasks pane opts into (local ADR 0062): <see cref="TaskItem.Quiet"/>,
/// the <see cref="TaskItem.Trailing"/> slot after the copy, and the three looks of
/// <see cref="QueueButton"/>. What the quiet look paints is the stylesheet's and
/// asserted there (<c>QuietTaskRowStylesheetTests</c>); these hold the markup the
/// stylesheet reaches through.
/// </summary>
public sealed class TaskQuietRowTests
{
    private static BunitContext Context()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    [Fact]
    public void A_row_is_not_quiet_unless_its_host_asks()
    {
        using var context = Context();

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Write it down"))
            .Add(t => t.TestId, "row"));

        Assert.DoesNotContain("task-item--quiet", view.Find("li").ClassList);
        Assert.Empty(view.FindAll(".task-item__trailing"));
    }

    [Fact]
    public void A_quiet_row_ends_in_the_copy_and_then_the_hosts_trailing_control()
    {
        using var context = Context();

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Write it down"))
            .Add(t => t.Quiet, true)
            .Add(t => t.Actions, Fragment("<span data-testid='picker'>status</span>"))
            .Add(t => t.Trailing, Fragment("<span data-testid='queue'>queue</span>"))
            .Add(t => t.TestId, "row"));

        var row = view.Find("li");
        Assert.Contains("task-item--quiet", row.ClassList);

        // Actions, then the copy, then the trailing slot: copy-then-queue is how the
        // quiet row ends.
        var trailing = view.Find(".task-item__trailing");
        Assert.NotNull(trailing.QuerySelector("[data-testid='queue']"));
        Assert.Contains("task-item__copy", trailing.PreviousElementSibling!.ClassList);
        Assert.Contains("task-item__actions", trailing.PreviousElementSibling!.PreviousElementSibling!.ClassList);
    }

    [Fact]
    public void The_quiet_look_leaves_every_other_part_of_the_row_as_it_was()
    {
        using var context = Context();

        var quiet = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Write it down", Status: "Ready"))
            .Add(t => t.Quiet, true)
            .Add(t => t.LinkHandle, true)
            .Add(t => t.Badges, Fragment("<span data-testid='badge'>PR</span>"))
            .Add(t => t.CssClass, "repo-mark--1")
            .Add(t => t.TestId, "row"));

        var row = quiet.Find("li");
        Assert.Contains("repo-mark--1", row.ClassList);
        Assert.NotNull(quiet.Find("[data-testid='row-link']"));
        Assert.NotNull(quiet.Find(".task-item__badges [data-testid='badge']"));
        Assert.Contains("badge--status-ready", quiet.Find("[data-testid='row-status']").ClassList);
    }

    [Fact]
    public void A_list_hands_every_row_the_quiet_look_and_the_trailing_slot()
    {
        using var context = Context();

        var view = context.Render<TaskListView>(p => p
            .Add(t => t.Tasks, [new TaskRow("a", "First", Group: "Tasks"), new TaskRow("b", "Second", Group: "Tasks")])
            .Add(t => t.QuietRows, true)
            .Add(t => t.RowTrailing, task => builder => builder.AddMarkupContent(0, $"<span data-testid='queue-{task.Id}'></span>"))
            .Add(t => t.AriaLabel, "Tasks")
            .Add(t => t.TestId, "list"));

        Assert.All(view.FindAll("li.task-item"), row => Assert.Contains("task-item--quiet", row.ClassList));
        Assert.NotNull(view.Find(".task-item__trailing [data-testid='queue-a']"));
        Assert.NotNull(view.Find(".task-item__trailing [data-testid='queue-b']"));
    }

    [Fact]
    public void Idle_queues_on_the_press()
    {
        using var context = Context();
        var pressed = 0;

        var view = context.Render<QueueButton>(p => p
            .Add(b => b.State, QueueButtonState.Idle)
            .Add(b => b.Label, "Queue a session for Write it down")
            .Add(b => b.OnClick, () => pressed++)
            .Add(b => b.TestId, "queue"));

        var button = view.Find("[data-testid='queue']");
        Assert.Equal("BUTTON", button.TagName);
        Assert.Contains("queue-button--idle", button.ClassList);
        Assert.Equal("Queue a session for Write it down", button.GetAttribute("aria-label"));
        Assert.False(button.HasAttribute("disabled"));
        Assert.NotNull(button.QuerySelector("svg.list-end-icon"));

        button.Click();
        Assert.Equal(1, pressed);
    }

    [Fact]
    public void Queued_names_taking_it_back_and_its_press_reaches_the_host()
    {
        using var context = Context();
        var pressed = 0;

        var view = context.Render<QueueButton>(p => p
            .Add(b => b.State, QueueButtonState.Queued)
            .Add(b => b.Label, "Take Write it down off the queue")
            .Add(b => b.OnClick, () => pressed++)
            .Add(b => b.TestId, "queue"));

        var button = view.Find("[data-testid='queue']");
        Assert.Contains("queue-button--queued", button.ClassList);
        Assert.Equal("Take Write it down off the queue", button.GetAttribute("aria-label"));
        Assert.Equal("queued", button.GetAttribute("data-queue-state"));
        Assert.False(button.HasAttribute("disabled"));

        button.Click();
        Assert.Equal(1, pressed);
    }

    [Fact]
    public void A_plan_entry_is_refused_and_says_to_queue_the_plan_as_a_whole()
    {
        using var context = Context();

        var view = context.Render<QueueButton>(p => p
            .Add(b => b.State, QueueButtonState.PlanEntry)
            .Add(b => b.Label, "Queue the +plan-first-tasks plan as a whole")
            .Add(b => b.ReasonId, "queue-reason")
            .Add(b => b.TestId, "queue"));

        var button = view.Find("[data-testid='queue']");
        Assert.Contains("queue-button--plan-entry", button.ClassList);
        Assert.True(button.HasAttribute("disabled"));
        Assert.Equal(QueueButton.PlanEntryReason, button.GetAttribute("title"));
        Assert.Equal("queue-reason", button.GetAttribute("aria-describedby"));
        Assert.Equal(QueueButton.PlanEntryReason, view.Find("#queue-reason").TextContent);
        Assert.Equal("Queue the +plan-first-tasks plan as a whole", button.GetAttribute("aria-label"));
    }

    [Fact]
    public void A_plan_entry_s_button_is_lit_while_its_plan_is_queued_and_stays_refused()
    {
        using var context = Context();

        var view = context.Render<QueueButton>(p => p
            .Add(b => b.State, QueueButtonState.PlanEntry)
            .Add(b => b.PlanQueued, true)
            .Add(b => b.Label, "Queue the +plan-first-tasks plan as a whole")
            .Add(b => b.TestId, "queue"));

        var button = view.Find("[data-testid='queue']");
        Assert.Contains("queue-button--plan-entry", button.ClassList);
        Assert.Contains("queue-button--plan-queued", button.ClassList);
        Assert.True(button.HasAttribute("disabled"));
    }

    [Fact]
    public void PlanQueued_lights_no_button_but_a_plan_entry_s()
    {
        using var context = Context();

        var view = context.Render<QueueButton>(p => p
            .Add(b => b.State, QueueButtonState.Idle)
            .Add(b => b.PlanQueued, true)
            .Add(b => b.Label, "Queue a session")
            .Add(b => b.TestId, "queue"));

        Assert.DoesNotContain("queue-button--plan-queued", view.Find("[data-testid='queue']").ClassList);
    }

    [Fact]
    public void A_press_on_its_way_keeps_the_button_focusable_and_says_it_is_busy()
    {
        using var context = Context();

        var view = context.Render<QueueButton>(p => p
            .Add(b => b.State, QueueButtonState.Idle)
            .Add(b => b.Label, "Queue a session")
            .Add(b => b.Busy, true)
            .Add(b => b.TestId, "queue"));

        var button = view.Find("[data-testid='queue']");
        Assert.Equal("true", button.GetAttribute("aria-busy"));
        Assert.Equal("true", button.GetAttribute("aria-disabled"));
        Assert.False(button.HasAttribute("disabled"));
    }

    private static RenderFragment<TaskRow> Fragment(string markup) =>
        _ => builder => builder.AddMarkupContent(0, markup);
}
