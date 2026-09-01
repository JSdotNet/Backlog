namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A row whose title is a field from the start, and the collision that comes with it.
/// <para>
/// A natively draggable element swallows the pointer inside its own inputs: press and
/// move to select a word and the browser started dragging the row instead of selecting
/// text. That is why a row with a field open used to turn its own <c>draggable</c> off,
/// and why the grip was draggable in its own right so a list of always-open fields kept
/// a pointer route at all.
/// </para>
/// <para>
/// The collision is gone now that reordering is a pointer gesture rather than a native
/// drag: the row keeps <c>data-draggable</c> open field or not, and a press that lands
/// in the field is declined in components.js instead. What is asserted below is the
/// new arrangement — the field keeps the pointer, the grip still starts a drag, and
/// Alt+Arrow is still heard inside the field.
/// </para>
/// <para>
/// All three are asserted together on purpose. Each one on its own is satisfiable by
/// dropping reordering, which is the cheap way through and the wrong one.
/// </para>
/// </summary>
public sealed class TaskDirectRenameTests
{
    private static readonly IReadOnlyList<TaskRow> Three =
    [
        new("a", "First", Group: "Tasks"),
        new("b", "Second", Group: "Tasks"),
        new("c", "Third", Group: "Tasks")
    ];

    // --- The field ---------------------------------------------------------

    [Fact]
    public void The_title_is_a_field_with_no_pencil_to_press_first()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Wire up the store"))
            .Add(t => t.OnRename, (TaskRename _) => { })
            .Add(t => t.DirectRename, true)
            .Add(t => t.TestId, "row"));

        Assert.Equal("Wire up the store", view.Find("[data-testid='row-rename']").GetAttribute("value"));

        // No pencil, and no line of text pretending to be one: there is nothing left
        // for either to do.
        Assert.Empty(view.FindAll("[data-testid='row-edit']"));
        Assert.Empty(view.FindAll("[data-testid='row-title']"));
    }

    /// <summary>Nothing without a listener, on the same terms as the pencil. A field
    /// nobody is listening to is a control that lies, and here it is one the reader
    /// cannot even avoid opening.</summary>
    [Fact]
    public void With_nobody_listening_the_title_stays_text()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Wire up the store"))
            .Add(t => t.DirectRename, true)
            .Add(t => t.TestId, "row"));

        Assert.Empty(view.FindAll("[data-testid='row-rename']"));
        Assert.Equal("Wire up the store", view.Find("[data-testid='row-title']").TextContent);
    }

    [Fact]
    public void Enter_commits_and_blur_commits()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var renames = new List<string>();

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "First"))
            .Add(t => t.OnRename, (TaskRename rename) => renames.Add(rename.Title))
            .Add(t => t.DirectRename, true)
            .Add(t => t.TestId, "row"));

        var field = view.Find("[data-testid='row-rename']");
        field.Input("Second");
        field.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        field.Input("Third");
        field.Blur();

        Assert.Equal(["Second", "Third"], renames);
    }

    /// <summary>Escape puts the title back rather than emptying the field. In this
    /// mode the field is the only place the title appears, so there is nothing to
    /// close to — abandoning has to mean showing what the row still says.</summary>
    [Fact]
    public void Escape_puts_the_title_back_and_raises_nothing()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var renames = new List<string>();

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "First"))
            .Add(t => t.OnRename, (TaskRename rename) => renames.Add(rename.Title))
            .Add(t => t.DirectRename, true)
            .Add(t => t.TestId, "row"));

        var field = view.Find("[data-testid='row-rename']");
        field.Input("Something else");
        field.KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(renames);
        Assert.Equal("First", view.Find("[data-testid='row-rename']").GetAttribute("value"));
    }

    /// <summary>The existing rule, unchanged: neither an empty title nor an unchanged
    /// one is a rename. An empty one also goes back to what the row says, because a
    /// field left blank would read as a row that had lost its name.</summary>
    [Fact]
    public void An_empty_or_unchanged_title_raises_nothing()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var renames = new List<string>();

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "First"))
            .Add(t => t.OnRename, (TaskRename rename) => renames.Add(rename.Title))
            .Add(t => t.DirectRename, true)
            .Add(t => t.TestId, "row"));

        var field = view.Find("[data-testid='row-rename']");

        field.Input("   ");
        field.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        Assert.Equal("First", view.Find("[data-testid='row-rename']").GetAttribute("value"));

        field = view.Find("[data-testid='row-rename']");
        field.Input("First");
        field.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Empty(renames);
    }

    /// <summary>The metadata line is beside the field rather than gone. It used to
    /// live inside the line the field replaces, so a row whose title became a field
    /// would have quietly stopped saying when it is due.</summary>
    [Fact]
    public void The_row_still_says_what_it_knows_about_itself()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "First", Due: "Friday", Tags: ["ui"]))
            .Add(t => t.OnRename, (TaskRename _) => { })
            .Add(t => t.DirectRename, true)
            .Add(t => t.TestId, "row"));

        var meta = view.Find(".task-item__meta").TextContent;

        Assert.Contains("Friday", meta, StringComparison.Ordinal);
        Assert.Contains("#ui", meta, StringComparison.Ordinal);
    }

    // --- The collision: still reorderable ----------------------------------

    [Fact]
    public void A_row_being_edited_still_offers_the_gesture()
    {
        // This used to be the opposite assertion, and the change is the point of
        // moving off native drag. The browser's own draggable had to be taken away
        // while a field was open, because a draggable row swallowed the pointer
        // inside its own input: press and move to select a word and the browser
        // dragged the row instead. The pointer gesture has no such conflict — a
        // press that lands in the rename field is declined in components.js — so
        // the row keeps the gesture and the reader keeps their text selection.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.OnRename, (TaskRename _) => { })
            .Add(l => l.DirectRename, true)
            .Add(l => l.Reorderable, true));

        foreach (var row in view.FindAll("li.task-item"))
        {
            Assert.Equal("true", row.GetAttribute("data-draggable"));

            // Never the browser's kind of draggable, open field or not.
            Assert.False(row.HasAttribute("draggable"));

            // Still described as pick-up-able, because it is: the row is what moves,
            // whichever element the pointer has to start the gesture on.
            Assert.Equal("false", row.GetAttribute("aria-grabbed"));
        }
    }

    /// <summary>The same list without the field marks its rows the same way, so the
    /// assertion above is about the open field rather than about reordering having
    /// been switched off somewhere.</summary>
    [Fact]
    public void A_row_with_no_field_open_is_draggable_as_before()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.OnRename, (TaskRename _) => { })
            .Add(l => l.Reorderable, true));

        Assert.All(view.FindAll("li.task-item"), row => Assert.Equal("true", row.GetAttribute("data-draggable")));
    }

    [Fact]
    public async Task The_grip_is_the_drag_handle_and_the_drop_still_reorders()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        TaskMove? move = null;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.OnRename, (TaskRename _) => { })
            .Add(l => l.DirectRename, true)
            .Add(l => l.Reorderable, true)
            .Add(l => l.OnReorder, (TaskMove reorder) => move = reorder));

        var grips = view.FindAll(".task-item__grip");
        Assert.Equal(3, grips.Count);

        // The grip carries no draggable of its own any more: it is an affordance
        // and the place a finger must start, and components.js finds it by class.
        Assert.All(grips, grip => Assert.False(grip.HasAttribute("draggable")));

        // The rest of the drag is the list's, unchanged.
        await view.InvokeAsync(() => view.Instance.PointerDragStart("a"));
        await view.InvokeAsync(() => view.Instance.PointerDragOver("c"));
        await view.InvokeAsync(view.Instance.PointerDragEnd);

        Assert.Equal(new TaskMove("a", "c"), move);
    }

    /// <summary>
    /// Alt+Arrow reorders from inside the field.
    /// <para>
    /// It has to: with the title always a field there is no line left to press it on,
    /// and the row's own draggable is off. Alt+Up and Alt+Down mean nothing to a caret
    /// on Windows, so nothing is taken away to get this — which is exactly why the
    /// keyboard route is the one that survives an always-open input.
    /// </para>
    /// </summary>
    [Fact]
    public void Alt_arrow_still_reorders_from_inside_the_field()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var moves = new List<TaskMove>();

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.OnRename, (TaskRename _) => { })
            .Add(l => l.DirectRename, true)
            .Add(l => l.Reorderable, true)
            .Add(l => l.OnReorder, (TaskMove move) => moves.Add(move)));

        var fields = view.FindAll(".task-item__rename-input");

        fields[0].KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });
        fields[2].KeyDown(new KeyboardEventArgs { Key = "ArrowUp", AltKey = true });

        Assert.Equal([new TaskMove("a", "b"), new TaskMove("c", "b")], moves);
    }

    /// <summary>A bare arrow is a caret move and stays one. Swallowing it would take
    /// away moving through a title to add moving a row.</summary>
    [Fact]
    public void A_bare_arrow_inside_the_field_moves_nothing()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var moves = new List<TaskMove>();

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.OnRename, (TaskRename _) => { })
            .Add(l => l.DirectRename, true)
            .Add(l => l.Reorderable, true)
            .Add(l => l.OnReorder, (TaskMove move) => moves.Add(move)));

        var field = view.FindAll(".task-item__rename-input")[0];
        field.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        field.KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });

        Assert.Empty(moves);
    }

    /// <summary>A list nobody can reorder hears nothing from inside the field either.
    /// One predicate covers every gesture, so a row can never be movable by one and
    /// not the other.</summary>
    [Fact]
    public void A_list_that_is_not_reorderable_moves_nothing_from_the_field()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var moves = new List<TaskMove>();

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.OnRename, (TaskRename _) => { })
            .Add(l => l.DirectRename, true)
            .Add(l => l.OnReorder, (TaskMove move) => moves.Add(move)));

        Assert.Empty(view.FindAll(".task-item__grip"));

        view.FindAll(".task-item__rename-input")[0]
            .KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });

        Assert.Empty(moves);
    }
}
