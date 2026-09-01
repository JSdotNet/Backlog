using Microsoft.AspNetCore.Components.Web;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Tabbing off a row, and being put back on one.
/// <para>
/// A list is followed in reading order by the rest of itself, so a host that shows
/// the open row's detail somewhere else cannot be reached by Tab without walking
/// every remaining row first. Only the row knows the reader has just tried, which is
/// why the hand-off is the row's event — and only the selected row is offered it,
/// because it is the only row a host has anywhere else to send the focus to.
/// </para>
/// <para>
/// Nothing is prevented and nothing is trapped: Shift+Tab off a selected row is
/// still the row above it.
/// </para>
/// </summary>
public class TaskTabHandoffTests
{
    private static readonly IReadOnlyList<TaskRow> Three =
    [
        new("a", "First"),
        new("b", "Second"),
        new("c", "Third")
    ];

    [Fact]
    public void Tab_off_the_selected_row_is_handed_to_the_host()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var handed = 0;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.SelectedId, "b")
            .Add(l => l.OnSelectedTabForward, () => handed++)
            .Add(l => l.TestId, "list"));

        view.Find("[data-testid='list-b-open']").KeyDown(new KeyboardEventArgs { Key = "Tab" });

        Assert.Equal(1, handed);
    }

    [Fact]
    public void Tab_off_any_other_row_is_left_to_the_browser()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var handed = 0;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.SelectedId, "b")
            .Add(l => l.OnSelectedTabForward, () => handed++)
            .Add(l => l.TestId, "list"));

        view.Find("[data-testid='list-a-open']").KeyDown(new KeyboardEventArgs { Key = "Tab" });
        view.Find("[data-testid='list-c-open']").KeyDown(new KeyboardEventArgs { Key = "Tab" });

        Assert.Equal(0, handed);
    }

    /// <summary>Shift+Tab is not the hand-off. Taking it would leave the reader on a
    /// row they could tab out of forwards and not backwards, which is a keyboard trap
    /// bought for a tidier hand-off.</summary>
    [Fact]
    public void Shift_tab_off_the_selected_row_is_not_the_hand_off()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var handed = 0;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.SelectedId, "b")
            .Add(l => l.OnSelectedTabForward, () => handed++)
            .Add(l => l.TestId, "list"));

        view.Find("[data-testid='list-b-open']").KeyDown(new KeyboardEventArgs { Key = "Tab", ShiftKey = true });

        Assert.Equal(0, handed);
    }

    [Fact]
    public async Task The_list_can_be_asked_to_put_the_focus_back_on_the_selected_row()
    {
        using var context = new BunitContext();
        context.JSInterop.SetupVoid("backlogFocus", _ => true);
        context.JSInterop.SetupVoid("taskListDrag.register", _ => true);
        context.JSInterop.SetupVoid("taskListDrag.unregister", _ => true);

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.SelectedId, "b")
            .Add(l => l.TestId, "list"));

        await view.InvokeAsync(() => view.Instance.FocusSelectedAsync());

        // The list names its own row elements — two lists showing the same task ids
        // must not mint the same ones — so what a host can rely on is that the id
        // ends in the task it asked for, which is also the id on the element.
        var focused = Assert.Single(context.JSInterop.Invocations["backlogFocus"]).Arguments[0] as string;

        Assert.Equal(view.Find("[data-testid='list-b-open']").Id, focused);
    }

    [Fact]
    public async Task A_list_with_nothing_selected_focuses_nothing()
    {
        using var context = new BunitContext();
        context.JSInterop.SetupVoid("backlogFocus", _ => true);
        context.JSInterop.SetupVoid("taskListDrag.register", _ => true);
        context.JSInterop.SetupVoid("taskListDrag.unregister", _ => true);

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.TestId, "list"));

        await view.InvokeAsync(() => view.Instance.FocusSelectedAsync());

        Assert.Empty(context.JSInterop.Invocations["backlogFocus"]);
    }

    /// <summary>The way back out of a row a host tabbed the reader into, raised from
    /// the row's first control — the circle, when the row has one.</summary>
    [Fact]
    public void Shift_tab_off_the_circle_hands_the_keyboard_back()
    {
        using var context = new BunitContext();
        var back = 0;

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Write it down"))
            .Add(t => t.OnToggle, _ => { })
            .Add(t => t.OnTabBackward, () => back++)
            .Add(t => t.RowElementId, "detail-row")
            .Add(t => t.TestId, "row"));

        view.Find("[data-testid='row-check']").KeyDown(new KeyboardEventArgs { Key = "Tab", ShiftKey = true });

        Assert.Equal(1, back);

        // Not from the line: with a circle in front of it, a Shift+Tab that jumped
        // out of the row here would put the circle out of the keyboard's reach.
        view.Find("[data-testid='row-open']").KeyDown(new KeyboardEventArgs { Key = "Tab", ShiftKey = true });

        Assert.Equal(1, back);
    }

    /// <summary>With no circle there is no first control but the line, so the line
    /// raises it instead. The row's front is wherever the row's front is.</summary>
    [Fact]
    public void Without_a_circle_the_line_is_the_front_of_the_row()
    {
        using var context = new BunitContext();
        var back = 0;

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Write it down"))
            .Add(t => t.OnTabBackward, () => back++)
            .Add(t => t.RowElementId, "detail-row")
            .Add(t => t.TestId, "row"));

        view.Find("[data-testid='row-open']").KeyDown(new KeyboardEventArgs { Key = "Tab", ShiftKey = true });

        Assert.Equal(1, back);
    }

    /// <summary>A host that moved the focus off a row wants a way to move it onto
    /// one, without having to know which control is at the front of it today.</summary>
    [Fact]
    public async Task A_row_can_be_asked_to_focus_its_first_control()
    {
        using var context = new BunitContext();
        context.JSInterop.SetupVoid("backlogFocus", _ => true);

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Write it down"))
            .Add(t => t.OnToggle, _ => { })
            .Add(t => t.RowElementId, "detail-row")
            .Add(t => t.TestId, "row"));

        Assert.Equal("detail-row-check", view.Find("[data-testid='row-check']").Id);

        await view.InvokeAsync(() => view.Instance.FocusFirstAsync());

        Assert.Equal(
            "detail-row-check",
            Assert.Single(context.JSInterop.Invocations["backlogFocus"]).Arguments[0] as string);
    }

    [Fact]
    public async Task An_unnamed_row_has_nothing_to_focus()
    {
        using var context = new BunitContext();
        context.JSInterop.SetupVoid("backlogFocus", _ => true);

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Write it down"))
            .Add(t => t.OnToggle, _ => { })
            .Add(t => t.TestId, "row"));

        Assert.False(view.Find("[data-testid='row-check']").HasAttribute("id"));

        await view.InvokeAsync(() => view.Instance.FocusFirstAsync());

        Assert.Empty(context.JSInterop.Invocations["backlogFocus"]);
    }
}
