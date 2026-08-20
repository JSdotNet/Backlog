namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The bin at the end of a row: when it is there, what it reports, and — the half
/// most easily got wrong — where the focus goes once the row it was on has gone.
/// </summary>
public sealed class TaskDeleteTests
{
    private static readonly IReadOnlyList<TaskRow> Three =
    [
        new("a", "First", Group: "Tasks"),
        new("b", "Second", Group: "Tasks"),
        new("c", "Third", Group: "Tasks")
    ];

    [Fact]
    public void A_row_nobody_is_listening_to_offers_no_bin()
    {
        // The same bargain the pencil makes. A control whose press goes nowhere is
        // this library's definition of one that lies — and here it would look like
        // a delete that silently failed.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Write it down"))
            .Add(t => t.TestId, "row"));

        Assert.Empty(view.FindAll("[data-testid='row-delete']"));
    }

    [Fact]
    public void A_row_with_a_host_listening_offers_a_bin_that_names_what_it_deletes()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Write it down"))
            .Add(t => t.OnDelete, _ => { })
            .Add(t => t.TestId, "row"));

        var bin = view.Find("[data-testid='row-delete']");

        // A real button, named after the row it acts on — the way the pencil names
        // itself — with the glyph hidden so the label is what carries the meaning.
        Assert.Equal("BUTTON", bin.TagName);
        Assert.Equal("Delete Write it down", bin.GetAttribute("aria-label"));
        Assert.Equal("🗑", bin.TextContent.Trim());
        Assert.Equal("true", bin.QuerySelector("span")?.GetAttribute("aria-hidden"));
    }

    [Fact]
    public void The_bin_is_the_last_thing_on_the_row()
    {
        // After the pencil and after the copy button, because it is the one control
        // here that ends the row rather than doing something to it.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Write it down"))
            .Add(t => t.OnRename, _ => { })
            .Add(t => t.OnDelete, _ => { })
            .Add(t => t.TestId, "row"));

        var order = view.FindAll("button")
            .Select(button => button.GetAttribute("data-testid") ?? string.Empty)
            .ToArray();

        Assert.Equal(["row-open", "row-edit", "row-copy", "row-delete"], order);
    }

    [Fact]
    public void The_bin_reports_the_row_it_is_on_and_does_not_open_it()
    {
        // Deleting a task is not opening one, so the click stops here — the same
        // thing the pencil and the copy button already do.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        string? deleted = null;
        var opened = 0;

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Write it down"))
            .Add(t => t.OnDelete, id => deleted = id)
            .Add(t => t.OnSelected, _ => opened++)
            .Add(t => t.TestId, "row"));

        view.Find("[data-testid='row-delete']").Click();

        Assert.Equal("a", deleted);
        Assert.Equal(0, opened);
    }

    [Fact]
    public void A_list_nobody_is_listening_to_for_deletes_grows_no_bins()
    {
        // The case that matters outside this file: the same component draws the
        // desktop pane's entry list, which deletes an entry from the footer of the
        // open one and passes no OnDelete — so it must grow no bins.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.OnToggle, _ => { })
            .Add(l => l.OnRename, _ => { })
            .Add(l => l.TestId, "list"));

        Assert.Empty(view.FindAll(".task-item__delete"));
    }

    [Fact]
    public void A_list_with_a_host_listening_puts_a_bin_on_every_row()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.OnDelete, _ => { })
            .Add(l => l.TestId, "list"));

        Assert.Equal(3, view.FindAll(".task-item__delete").Count);
        Assert.Equal("Delete Second", view.Find("[data-testid='list-b-delete']").GetAttribute("aria-label"));
    }

    [Fact]
    public void The_add_row_has_no_bin_on_it()
    {
        // There is nothing there to delete. The row is a composer for a task that
        // does not exist yet, which is why it carries no circle and no pencil either.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.QuickEdit, true)
            .Add(l => l.OnRename, _ => { })
            .Add(l => l.OnAdd, _ => { })
            .Add(l => l.OnDelete, _ => { })
            .Add(l => l.TestId, "list"));

        Assert.Empty(view.Find("[data-testid='list-add']").QuerySelectorAll("button"));
    }

    [Fact]
    public void A_row_that_goes_is_said_out_loud()
    {
        // A row vanishing is invisible to a screen reader: the visible answer to
        // "what happened" is the row not being there any more, which is no answer
        // at all to somebody who cannot see the list.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskDeleteHarness>();

        view.Find("[data-testid='list-b-delete']").Click();

        Assert.Equal(
            "Deleted Second. 2 remaining.",
            view.Find("[data-testid='list-announcement']").TextContent.Trim());
    }

    [Fact]
    public void The_last_row_going_is_announced_as_an_empty_list()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskDeleteHarness>(p => p
            .Add(h => h.Seed, new TaskRow[] { new("only", "The only one") }));

        view.Find("[data-testid='list-only-delete']").Click();

        Assert.Equal(
            "Deleted The only one. The list is empty.",
            view.Find("[data-testid='list-announcement']").TextContent.Trim());
    }

    [Fact]
    public void The_focus_lands_on_the_row_that_took_the_deleted_ones_place()
    {
        // The focused element goes with the row, so the ring would land on the
        // document body and a reader using the keyboard would have to tab in from
        // the top of the page to get back to where they were standing.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskDeleteHarness>();

        view.Find("[data-testid='list-a-delete']").Click();

        // Resolved after the host applied the delete, not before it: the row that
        // slides up into the gap is named from the rows as they now stand, which is
        // what makes this correct for a list whose ids are positions.
        var landed = view.Find("[data-testid='list-b-open']");

        Assert.Equal(landed.Id, context.JSInterop.Invocations["backlogFocus"].Last().Arguments[0]);
    }

    [Fact]
    public void Deleting_the_bottom_row_puts_the_focus_on_the_one_above_it()
    {
        // Nothing slid up, because nothing was below it. Upwards is the only
        // direction left, and it is where the reader's eye already is.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskDeleteHarness>();

        view.Find("[data-testid='list-c-delete']").Click();

        var landed = view.Find("[data-testid='list-b-open']");

        Assert.Equal(landed.Id, context.JSInterop.Invocations["backlogFocus"].Last().Arguments[0]);
    }

    [Fact]
    public void Deleting_the_only_row_leaves_the_focus_in_the_add_row()
    {
        // There is no row left to hold it, and the add row is the one control still
        // on screen that the reader can do anything with — including typing the step
        // back if the delete was a mistake.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskDeleteHarness>(p => p
            .Add(h => h.WithAddRow, true)
            .Add(h => h.Seed, new TaskRow[] { new("only", "The only one") }));

        view.Find("[data-testid='list-only-delete']").Click();

        var field = view.Find("[data-testid='list-add-input']");

        Assert.Empty(view.FindAll(".task-item__body"));
        Assert.Equal(field.Id, context.JSInterop.Invocations["backlogFocus"].Last().Arguments[0]);
    }

    [Fact]
    public void A_finished_row_is_deleted_from_the_section_it_is_in()
    {
        // The Completed section renders the same rows through the same fragment, so
        // it grows the same bin — and a delete pressed there has to report, rather
        // than being quietly dropped because the row was not in the open list.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskDeleteHarness>(p => p
            .Add(h => h.GroupCompleted, true)
            .Add(h => h.Seed, new TaskRow[]
            {
                new("a", "Still to do"),
                new("d1", "Finished", Done: true),
                new("d2", "Finished too", Done: true)
            }));

        view.Find("[data-testid='list-completed-toggle']").Click();
        view.Find("[data-testid='list-d1-delete']").Click();

        Assert.Equal(["d1"], view.Instance.Deleted);
        Assert.Equal(
            "Deleted Finished. 1 remaining.",
            view.Find("[data-testid='list-announcement']").TextContent.Trim());
    }
}
