using Microsoft.AspNetCore.Components.Web;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The right-click on a row: what it reports, when it is the row's to answer at
/// all, and when the browser's own menu is left where it was.
/// </summary>
public sealed class TaskContextMenuTests
{
    private const string PreventDefault = "blazor:oncontextmenu:preventDefault";

    [Fact]
    public void A_right_click_reports_the_row_and_where_the_pointer_was()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        TaskContextMenu? asked = null;

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Write it down"))
            .Add(t => t.OnContextMenu, request => asked = request)
            .Add(t => t.TestId, "row"));

        view.Find("[data-testid='row']").ContextMenu(new MouseEventArgs { ClientX = 12, ClientY = 34 });

        Assert.NotNull(asked);
        Assert.Equal("a", asked.Id);
        Assert.Equal(12, asked.X);
        Assert.Equal(34, asked.Y);

        // The browser's menu is taken away only because something replaces it.
        Assert.NotNull(view.Find("[data-testid='row']").GetAttribute(PreventDefault));
    }

    [Fact]
    public void A_row_nobody_is_listening_to_keeps_the_browser_menu()
    {
        // The same bargain the pencil and the bin make: a gesture that goes nowhere
        // is not swallowed, so right-click keeps doing what it does everywhere else.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Write it down"))
            .Add(t => t.TestId, "row"));

        Assert.Null(view.Find("[data-testid='row']").GetAttribute(PreventDefault));
    }

    [Fact]
    public void A_title_that_is_a_field_keeps_the_browser_menu()
    {
        // A text field's own menu is the one with Paste on it. DirectRename keeps the
        // field open from the start, which is the plainest way to have one on screen.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        TaskContextMenu? asked = null;

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Write it down"))
            .Add(t => t.OnRename, _ => { })
            .Add(t => t.DirectRename, true)
            .Add(t => t.OnContextMenu, request => asked = request)
            .Add(t => t.TestId, "row"));

        Assert.NotEmpty(view.FindAll("[data-testid='row-rename']"));

        view.Find("[data-testid='row']").ContextMenu(new MouseEventArgs { ClientX = 12, ClientY = 34 });

        Assert.Null(asked);
        Assert.Null(view.Find("[data-testid='row']").GetAttribute(PreventDefault));
    }

    [Fact]
    public void A_list_hands_the_gesture_through_with_the_row_it_landed_on()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        TaskContextMenu? asked = null;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, [new TaskRow("a", "First"), new TaskRow("b", "Second")])
            .Add(l => l.OnContextMenu, request => asked = request)
            .Add(l => l.TestId, "list"));

        view.Find("[data-testid='list-b']").ContextMenu(new MouseEventArgs { ClientX = 5, ClientY = 6 });

        Assert.NotNull(asked);
        Assert.Equal("b", asked.Id);
        Assert.Equal(5, asked.X);
        Assert.Equal(6, asked.Y);
    }
}
