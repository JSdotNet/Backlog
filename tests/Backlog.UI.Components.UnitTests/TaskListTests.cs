namespace Backlog.UI.Components.UnitTests;

public sealed class TaskListTests
{
    private static readonly IReadOnlyList<TaskRow> Three =
    [
        new("a", "First", Group: "Tasks"),
        new("b", "Second", Group: "Tasks"),
        new("c", "Third", Group: "Tasks")
    ];

    [Fact]
    public void The_circle_is_a_checkbox_to_anything_that_is_listening()
    {
        // Round is this shape's one convention, and it is not worth losing the
        // semantics over.
        using var context = new BunitContext();

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Write it down"))
            .Add(t => t.OnToggle, _ => { })
            .Add(t => t.TestId, "row"));

        var check = view.Find("[data-testid='row-check']");

        Assert.Equal("checkbox", check.GetAttribute("role"));
        Assert.Equal("false", check.GetAttribute("aria-checked"));
        Assert.Equal("Write it down", check.GetAttribute("aria-label"));
    }

    [Fact]
    public void With_nothing_listening_the_circle_is_state_rather_than_a_control()
    {
        using var context = new BunitContext();

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Already done", Done: true))
            .Add(t => t.TestId, "row"));

        var check = view.Find("[data-testid='row-check']");

        Assert.Equal("img", check.GetAttribute("role"));
        Assert.Equal("Done", check.GetAttribute("aria-label"));
        Assert.Empty(view.FindAll("button[role='checkbox']"));
    }

    [Fact]
    public void A_row_offers_to_copy_its_own_text()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Write it down", Group: "Tasks", Due: "Today"))
            .Add(t => t.TestId, "row"));

        view.Find("[data-testid='row-copy']").Click();

        // The title, not the row. "Tasks · Today" is this list's furniture, and
        // pasting it into a message says nothing.
        var invocation = Assert.Single(context.JSInterop.Invocations["backlogClipboard.copy"]);
        Assert.Equal("Write it down", invocation.Arguments[0]);
    }

    [Fact]
    public void What_a_row_copies_can_be_replaced_by_the_host()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Write it down"))
            .Add(t => t.CopyValue, "- [ ] Write it down")
            .Add(t => t.TestId, "row"));

        view.Find("[data-testid='row-copy']").Click();

        Assert.Equal("- [ ] Write it down", Assert.Single(context.JSInterop.Invocations["backlogClipboard.copy"]).Arguments[0]);
    }

    [Fact]
    public void A_row_can_be_told_not_to_offer_a_copy_at_all()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "No copy"))
            .Add(t => t.AllowCopy, false)
            .Add(t => t.TestId, "row"));

        Assert.Empty(view.FindAll("[data-testid='row-copy']"));
    }

    [Fact]
    public void Everything_known_about_a_task_rides_on_one_metadata_line_in_order()
    {
        var row = new TaskRow("a", "T", Group: "Tasks", Due: "Today", Reminder: "09:00", StepsDone: 2, StepCount: 5);

        Assert.Equal(["Tasks", "2 of 5", "Today", "09:00"], row.Details.Select(d => d.Text));
        Assert.Equal(
            [TaskDetailKind.Group, TaskDetailKind.Steps, TaskDetailKind.Due, TaskDetailKind.Reminder],
            row.Details.Select(d => d.Kind));
    }

    [Fact]
    public void What_is_not_known_is_left_out_rather_than_filled_in()
    {
        Assert.Empty(new TaskRow("a", "T").Details);
        Assert.DoesNotContain(new TaskRow("a", "T", Group: "Tasks").Details, d => d.Text.Contains("0 of 0", StringComparison.Ordinal));
    }

    [Fact]
    public void A_due_date_and_a_reminder_are_told_apart_by_more_than_their_words()
    {
        // "Friday" and "09:00" side by side are two dates until something says
        // one is a deadline and the other an alarm.
        var row = new TaskRow("a", "T", Due: "Friday", Reminder: "09:00", Repeats: true);

        var due = row.Details.Single(d => d.Kind is TaskDetailKind.Due);
        var reminder = row.Details.Single(d => d.Kind is TaskDetailKind.Reminder);
        var repeat = row.Details.Single(d => d.Kind is TaskDetailKind.Repeat);

        Assert.Equal("🗓", due.Glyph);
        Assert.Equal("⏰", reminder.Glyph);
        Assert.Equal("🔁", repeat.Glyph);
        Assert.Equal(["Due", "Reminder", "Repeats"], new[] { due, reminder, repeat }.Select(d => d.Name));
    }

    [Fact]
    public void A_repeat_says_how_often_when_the_caller_knows()
    {
        Assert.Equal("Repeats", new TaskRow("a", "T", Repeats: true).Details.Single().Text);
        Assert.Equal("Weekly", (new TaskRow("a", "T", Repeats: true) { RepeatLabel = "Weekly" }).Details.Single().Text);
    }

    [Fact]
    public void The_glyph_is_decoration_and_the_name_is_the_part_that_is_read_out()
    {
        // A screen reader announcing "alarm clock emoji" says less than
        // "Reminder".
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "T", Reminder: "09:00")));

        Assert.Equal("true", view.Find(".task-item__glyph").GetAttribute("aria-hidden"));
        Assert.Contains(view.FindAll(".sr-only"), e => e.TextContent == "Reminder");
    }

    [Fact]
    public void Tags_are_chips_rather_than_more_of_the_metadata_line()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "T", Tags: ["ui", "storybook"])));

        Assert.Equal(["#ui", "#storybook"], view.FindAll(".task-item__tag").Select(t => t.TextContent));
        Assert.Empty(new TaskRow("a", "T").TagList);
    }

    [Fact]
    public void My_day_reads_as_one_more_fact_on_the_same_line()
    {
        using var context = new BunitContext();

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "T", InMyDay: true, Group: "Tasks")));

        var meta = view.Find(".task-item__meta").TextContent;

        Assert.Contains("My Day", meta, StringComparison.Ordinal);
        Assert.Contains("Tasks", meta, StringComparison.Ordinal);
    }

    [Fact]
    public void Toggling_and_opening_report_the_row_they_happened_on()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        string? toggled = null;
        string? opened = null;

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "T"))
            .Add(t => t.OnToggle, id => toggled = id)
            .Add(t => t.OnSelected, id => opened = id)
            .Add(t => t.TestId, "row"));

        view.Find("[data-testid='row-check']").Click();
        view.Find("[data-testid='row-open']").Click();

        Assert.Equal("a", toggled);
        Assert.Equal("a", opened);
    }

    [Fact]
    public void While_a_row_is_dragged_the_list_shows_where_it_would_land()
    {
        // Previewing rather than drawing an insertion line: the reader is
        // already looking at the list, so moving it is the cheapest possible
        // answer to "where would this go".
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.Reorderable, true));

        var rows = view.FindAll("li.task-item");
        rows[0].DragStart();
        view.FindAll("li.task-item")[2].DragOver();

        Assert.Equal(["Second", "Third", "First"], view.FindAll(".task-item__title").Select(t => t.TextContent));

        // A preview and nothing more: nothing was reported, and the source list
        // is untouched.
        Assert.Equal(["First", "Second", "Third"], Three.Select(t => t.Title));
    }

    [Fact]
    public void Abandoning_a_drag_puts_the_preview_back()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.Reorderable, true));

        view.FindAll("li.task-item")[0].DragStart();
        view.FindAll("li.task-item")[2].DragOver();
        view.FindAll("li.task-item")[0].DragEnd();

        Assert.Equal(["First", "Second", "Third"], view.FindAll(".task-item__title").Select(t => t.TextContent));
    }

    [Fact]
    public void Dragging_over_the_moving_row_itself_is_ignored()
    {
        // In the previewed order that row is already under the pointer, so
        // honouring it would mean "put it back" — and the two would trade places
        // for as long as the reader held still.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.Reorderable, true));

        view.FindAll("li.task-item")[0].DragStart();
        view.FindAll("li.task-item")[2].DragOver();

        // The moved row now sits last; dragging over it must not undo the move.
        view.FindAll("li.task-item")[2].DragOver();

        Assert.Equal(["Second", "Third", "First"], view.FindAll(".task-item__title").Select(t => t.TextContent));
    }

    [Fact]
    public void The_list_says_when_a_drag_is_in_flight_so_the_rest_can_make_room()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.Reorderable, true));

        Assert.DoesNotContain("task-list--dragging", view.Find("ul.task-list").ClassList);

        view.FindAll("li.task-item")[0].DragStart();

        Assert.Contains("task-list--dragging", view.Find("ul.task-list").ClassList);
        Assert.Contains("task-item--dragging", view.FindAll("li.task-item")[0].ClassList);
    }

    [Fact]
    public void A_list_reports_where_a_row_was_dropped_and_reorders_nothing_itself()
    {
        // Only the host knows whether the new order is saved anywhere.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        TaskMove? move = null;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.Reorderable, true)
            .Add(l => l.OnReorder, m => move = m)
            .Add(l => l.TestId, "list"));

        view.FindAll("li.task-item")[0].DragStart();
        view.FindAll("li.task-item")[2].DragOver();
        view.FindAll("li.task-item")[2].Drop();

        Assert.Equal("a", move?.Id);
        Assert.Equal("c", move?.TargetId);

        // The component reordered nothing: with the drag over, it is back to
        // showing exactly what it was given.
        Assert.Equal(["First", "Second", "Third"], view.FindAll(".task-item__title").Select(t => t.TextContent));
    }

    [Fact]
    public void A_row_dropped_on_itself_is_a_drag_thought_better_of_not_a_move()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var raised = 0;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.Reorderable, true)
            .Add(l => l.OnReorder, _ => raised++));

        var rows = view.FindAll("li.task-item");
        rows[1].DragStart();
        rows[1].Drop();

        Assert.Equal(0, raised);
    }

    [Fact]
    public void A_list_that_is_not_reorderable_offers_no_grip()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p.Add(l => l.Tasks, Three));

        Assert.Empty(view.FindAll(".task-item__grip"));
        Assert.All(view.FindAll("li.task-item"), row => Assert.False(row.HasAttribute("draggable")));
    }

    [Theory]
    [InlineData("a", "c", new[] { "Second", "Third", "First" })]
    [InlineData("c", "a", new[] { "Third", "First", "Second" })]
    [InlineData("b", "c", new[] { "First", "Third", "Second" })]
    public void ApplyTo_puts_the_moved_row_where_the_target_was(string id, string target, string[] expected)
    {
        var moved = new TaskMove(id, target).ApplyTo(Three, t => t.Id);

        Assert.Equal(expected, moved.Select(t => t.Title));
    }

    [Fact]
    public void ApplyTo_does_nothing_when_either_row_has_gone()
    {
        // The list moved under the drag. Doing nothing is honest; guessing a
        // position is not.
        Assert.Same(Three, new TaskMove("a", "gone").ApplyTo(Three, t => t.Id));
        Assert.Same(Three, new TaskMove("gone", "a").ApplyTo(Three, t => t.Id));
    }

    [Fact]
    public void A_rename_is_reported_once_when_it_settles_and_not_per_keystroke()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var renames = new List<TaskRename>();

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Before"))
            .Add(t => t.OnRename, r => renames.Add(r))
            .Add(t => t.TestId, "row"));

        view.Find("[data-testid='row-edit']").Click();
        view.Find("input").Input("After");

        // Typing has raised nothing yet.
        Assert.Empty(renames);

        view.Find("input").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        var rename = Assert.Single(renames);
        Assert.Equal("a", rename.Id);
        Assert.Equal("After", rename.Title);
    }

    [Fact]
    public void Escape_abandons_a_rename_and_reports_nothing()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var renames = 0;

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Before"))
            .Add(t => t.OnRename, _ => renames++)
            .Add(t => t.TestId, "row"));

        view.Find("[data-testid='row-edit']").Click();
        view.Find("input").Input("After");
        view.Find("input").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal(0, renames);
        Assert.Equal("Before", view.Find(".task-item__title").TextContent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Before")]
    public void An_empty_or_unchanged_title_is_not_a_rename(string typed)
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var renames = 0;

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Before"))
            .Add(t => t.OnRename, _ => renames++)
            .Add(t => t.TestId, "row"));

        view.Find("[data-testid='row-edit']").Click();
        view.Find("input").Input(typed);
        view.Find("input").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal(0, renames);
    }

    [Fact]
    public void A_row_with_nothing_listening_offers_no_way_to_rename_it()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "T"))
            .Add(t => t.TestId, "row"));

        Assert.Empty(view.FindAll("[data-testid='row-edit']"));
    }

    [Fact]
    public void Tab_commits_the_rename_and_opens_the_field_on_the_next_row()
    {
        // The spreadsheet bargain: the keystroke that finishes one rename is the
        // one that starts the next, so retitling six rows costs six titles rather
        // than six trips back to the pencil.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var renames = new List<TaskRename>();

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.QuickEdit, true)
            .Add(l => l.OnRename, r => renames.Add(r))
            .Add(l => l.TestId, "list"));

        view.Find("[data-testid='list-a-edit']").Click();
        view.Find("[data-testid='list-a-rename']").Input("First, renamed");
        view.Find("[data-testid='list-a-rename']").KeyDown(new KeyboardEventArgs { Key = "Tab" });

        var rename = Assert.Single(renames);
        Assert.Equal("a", rename.Id);
        Assert.Equal("First, renamed", rename.Title);

        // The row it left is a row again, and the next one is the field.
        Assert.Empty(view.FindAll("[data-testid='list-a-rename']"));
        var field = view.Find("[data-testid='list-b-rename']");

        // And the caret is in it: an editor that opened somewhere the typing does
        // not go is worse than not opening one.
        Assert.Equal(field.Id, context.JSInterop.Invocations["backlogFocus"].Last().Arguments[0]);
        Assert.Equal(field.Id, context.JSInterop.Invocations["backlogGuardTab"].Last().Arguments[0]);
    }

    [Fact]
    public void Shift_Tab_commits_the_rename_and_walks_back_up_the_list()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var renames = new List<TaskRename>();

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.QuickEdit, true)
            .Add(l => l.OnRename, r => renames.Add(r))
            .Add(l => l.TestId, "list"));

        view.Find("[data-testid='list-b-edit']").Click();
        view.Find("[data-testid='list-b-rename']").Input("Second, renamed");
        view.Find("[data-testid='list-b-rename']").KeyDown(new KeyboardEventArgs { Key = "Tab", ShiftKey = true });

        Assert.Equal("Second, renamed", Assert.Single(renames).Title);
        Assert.Empty(view.FindAll("[data-testid='list-b-rename']"));

        var field = view.Find("[data-testid='list-a-rename']");
        Assert.Equal(field.Id, context.JSInterop.Invocations["backlogFocus"].Last().Arguments[0]);
    }

    [Theory]
    [InlineData("c", false, "Third, renamed")]
    [InlineData("a", true, "First, renamed")]
    public void Tab_off_either_end_commits_and_stops_rather_than_wrapping(string id, bool back, string typed)
    {
        // Coming out of the bottom of the list is what says the list is finished.
        // Starting again from the top would retitle the row the reader began with.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var renames = new List<TaskRename>();

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.QuickEdit, true)
            .Add(l => l.OnRename, r => renames.Add(r))
            .Add(l => l.TestId, "list"));

        view.Find($"[data-testid='list-{id}-edit']").Click();
        view.Find($"[data-testid='list-{id}-rename']").Input(typed);
        view.Find($"[data-testid='list-{id}-rename']").KeyDown(new KeyboardEventArgs { Key = "Tab", ShiftKey = back });

        Assert.Equal(typed, Assert.Single(renames).Title);
        Assert.Empty(view.FindAll("input"));
    }

    [Fact]
    public void Escape_in_a_rename_abandons_the_title_and_ends_the_chain()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var renames = new List<TaskRename>();

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.QuickEdit, true)
            .Add(l => l.OnRename, r => renames.Add(r))
            .Add(l => l.TestId, "list"));

        view.Find("[data-testid='list-a-edit']").Click();
        view.Find("[data-testid='list-a-rename']").Input("First, renamed");
        view.Find("[data-testid='list-a-rename']").KeyDown(new KeyboardEventArgs { Key = "Tab" });

        view.Find("[data-testid='list-b-rename']").Input("Second, abandoned");
        view.Find("[data-testid='list-b-rename']").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        // Only the row that settled is reported, and nothing is left open.
        Assert.Equal(["First, renamed"], renames.Select(r => r.Title));
        Assert.Empty(view.FindAll("input"));
    }

    [Fact]
    public void Enter_commits_the_row_it_is_in_and_moves_on_to_nothing()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var renames = new List<TaskRename>();

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.QuickEdit, true)
            .Add(l => l.OnRename, r => renames.Add(r))
            .Add(l => l.TestId, "list"));

        view.Find("[data-testid='list-b-edit']").Click();
        view.Find("[data-testid='list-b-rename']").Input("Second, renamed");
        view.Find("[data-testid='list-b-rename']").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal("Second, renamed", Assert.Single(renames).Title);
        Assert.Empty(view.FindAll("input"));
    }

    [Fact]
    public void Finished_rows_are_not_part_of_the_chain()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var renames = new List<TaskRename>();

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, new TaskRow[]
            {
                new("a", "Open"),
                new("b", "Finished", Done: true),
                new("c", "Also open")
            })
            .Add(l => l.QuickEdit, true)
            .Add(l => l.OnRename, r => renames.Add(r))
            .Add(l => l.TestId, "list"));

        view.Find("[data-testid='list-a-edit']").Click();
        view.Find("[data-testid='list-a-rename']").Input("Open, renamed");
        view.Find("[data-testid='list-a-rename']").KeyDown(new KeyboardEventArgs { Key = "Tab" });

        // Straight past the finished row, which is not in the list the reader is
        // tabbing down at all.
        Assert.Empty(view.FindAll("[data-testid='list-b-rename']"));
        Assert.NotNull(view.Find("[data-testid='list-c-rename']"));
    }

    [Fact]
    public void Without_quick_edit_Tab_is_still_the_browsers_to_answer()
    {
        // Opt-in, because Tab means "the next control" everywhere else.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var renames = new List<TaskRename>();

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.OnRename, r => renames.Add(r))
            .Add(l => l.TestId, "list"));

        view.Find("[data-testid='list-a-edit']").Click();
        view.Find("[data-testid='list-a-rename']").Input("First, renamed");
        view.Find("[data-testid='list-a-rename']").KeyDown(new KeyboardEventArgs { Key = "Tab" });

        // Nothing was committed by the key and nothing opened: leaving the field
        // is what commits, exactly as it did before quick edit existed.
        Assert.Empty(renames);
        Assert.NotNull(view.Find("[data-testid='list-a-rename']"));
        Assert.Empty(view.FindAll("[data-testid='list-b-rename']"));
        Assert.Empty(context.JSInterop.Invocations["backlogGuardTab"]);
    }

    [Fact]
    public void Quick_edit_offers_no_editor_on_a_row_nobody_is_listening_to()
    {
        // A field whose rename nobody would hear is worse than no field, and a
        // list asking for one does not change that.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "T"))
            .Add(t => t.QuickEdit, true)
            .Add(t => t.Editing, true)
            .Add(t => t.TestId, "row"));

        Assert.Empty(view.FindAll("input"));
        Assert.Empty(view.FindAll("[data-testid='row-edit']"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("First")]
    public void Tab_moves_the_edit_on_even_when_there_was_no_rename_to_report(string typed)
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var renames = 0;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.QuickEdit, true)
            .Add(l => l.OnRename, _ => renames++)
            .Add(l => l.TestId, "list"));

        view.Find("[data-testid='list-a-edit']").Click();
        view.Find("[data-testid='list-a-rename']").Input(typed);
        view.Find("[data-testid='list-a-rename']").KeyDown(new KeyboardEventArgs { Key = "Tab" });

        // Tabbing through a row is how a reader skips it, so it reports nothing —
        // and still lands where they were going.
        Assert.Equal(0, renames);
        Assert.Empty(view.FindAll("[data-testid='list-a-rename']"));
        Assert.NotNull(view.Find("[data-testid='list-b-rename']"));
    }

    [Fact]
    public void The_add_row_is_on_the_list_before_anybody_has_started_a_chain()
    {
        // The decision this round turns on. A field that only exists part-way
        // through a keystroke chain is a field nobody who is not already mid-chain
        // can find, so the place you add tasks is drawn from the start and sits in
        // the tab order like any other control.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.QuickEdit, true)
            .Add(l => l.OnRename, _ => { })
            .Add(l => l.OnAdd, _ => { })
            .Add(l => l.TestId, "list"));

        var field = view.Find("[data-testid='list-add-input']");

        // Named twice over, because it has no title to borrow: the label is what a
        // screen reader says and the placeholder is what everybody else reads.
        Assert.Equal("New task", field.GetAttribute("aria-label"));
        Assert.Equal("New task", field.GetAttribute("placeholder"));
        Assert.True(string.IsNullOrEmpty(field.GetAttribute("value")));

        // Last in the open rows, so Tab from the row before it arrives here.
        var rows = view.FindAll(".task-list > li");
        Assert.Equal("list-add", rows[^1].GetAttribute("data-testid"));
    }

    [Fact]
    public void A_list_nobody_is_listening_to_for_new_tasks_has_no_add_row()
    {
        // Whether a list offers to add tasks is the host's answer, and a field whose
        // title nobody would hear is the same broken promise as a pencil with no
        // OnRename behind it.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.QuickEdit, true)
            .Add(l => l.OnRename, _ => { })
            .Add(l => l.TestId, "list"));

        Assert.Empty(view.FindAll("[data-testid='list-add']"));
        Assert.Empty(view.FindAll("input"));
    }

    [Fact]
    public void Tab_off_the_last_row_lands_in_the_add_row_that_was_already_there()
    {
        // Nothing opens — the field was there before the chain started. This is only
        // the list doing by hand what the browser would have done had the rename not
        // taken the key off it.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var added = new List<string>();

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.QuickEdit, true)
            .Add(l => l.OnRename, _ => { })
            .Add(l => l.OnAdd, title => added.Add(title))
            .Add(l => l.TestId, "list"));

        view.Find("[data-testid='list-c-edit']").Click();
        view.Find("[data-testid='list-c-rename']").KeyDown(new KeyboardEventArgs { Key = "Tab" });

        var field = view.Find("[data-testid='list-add-input']");

        Assert.Equal(field.Id, context.JSInterop.Invocations["backlogFocus"].Last().Arguments[0]);

        // Arriving is not adding: nothing has been reported, and the row the reader
        // left is a row again.
        Assert.Empty(added);
        Assert.Empty(view.FindAll("[data-testid='list-c-rename']"));
    }

    [Fact]
    public void Tab_off_the_last_row_still_ends_the_chain_when_nobody_is_listening_for_new_tasks()
    {
        // Whether the list offers to add tasks is the host's answer, and a list that
        // was not asked does not start offering.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.QuickEdit, true)
            .Add(l => l.OnRename, _ => { })
            .Add(l => l.TestId, "list"));

        view.Find("[data-testid='list-c-edit']").Click();
        view.Find("[data-testid='list-c-rename']").KeyDown(new KeyboardEventArgs { Key = "Tab" });

        Assert.Empty(view.FindAll("[data-testid='list-add']"));
        Assert.Empty(view.FindAll("input"));
    }

    [Fact]
    public void A_title_and_Tab_adds_it_and_leaves_the_field_empty_and_still_focused()
    {
        // Type a title, Tab, type a title, Tab. One report per title, and the field
        // is waiting for the next one before the reader has stopped typing.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var added = new List<string>();

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.QuickEdit, true)
            .Add(l => l.OnRename, _ => { })
            .Add(l => l.OnAdd, title => added.Add(title))
            .Add(l => l.TestId, "list"));

        var before = context.JSInterop.Invocations["backlogFocus"].Count;

        view.Find("[data-testid='list-add-input']").Input("  Fourth  ");
        view.Find("[data-testid='list-add-input']").KeyDown(new KeyboardEventArgs { Key = "Tab" });

        // Trimmed on the way out, on the rule the rename already follows.
        Assert.Equal("Fourth", Assert.Single(added));

        var field = view.Find("[data-testid='list-add-input']");
        Assert.True(string.IsNullOrEmpty(field.GetAttribute("value")));

        // And the caret is asked for again. The render that emptied the field is the
        // same render that grew the list above it, so staying put is stated rather
        // than left to the diff.
        Assert.True(context.JSInterop.Invocations["backlogFocus"].Count > before);
        Assert.Equal(field.Id, context.JSInterop.Invocations["backlogFocus"].Last().Arguments[0]);
    }

    [Fact]
    public void Tab_on_an_empty_add_field_is_left_to_the_browser()
    {
        // The way out of the chain, and the reason it is not a dead end any more:
        // nothing is suppressed and nothing is handled, so the browser moves the
        // focus on to whatever follows the list. It is also what stops a held-down
        // Tab from filling the list with rows nobody named.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var added = new List<string>();

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.QuickEdit, true)
            .Add(l => l.OnRename, _ => { })
            .Add(l => l.OnAdd, title => added.Add(title))
            .Add(l => l.TestId, "list"));

        var before = context.JSInterop.Invocations["backlogFocus"].Count;

        view.Find("[data-testid='list-add-input']").KeyDown(new KeyboardEventArgs { Key = "Tab" });

        Assert.Empty(added);

        // Still there, still empty, and the list did not chase the caret back into
        // it — the reader is on their way somewhere else.
        var field = view.Find("[data-testid='list-add-input']");
        Assert.True(string.IsNullOrEmpty(field.GetAttribute("value")));
        Assert.Equal(before, context.JSInterop.Invocations["backlogFocus"].Count);
    }

    [Fact]
    public void Enter_adds_the_task_and_leaves_the_field_open_for_the_next_one()
    {
        // A permanent field has nothing to close, so the only honest difference
        // between Enter and Tab here is that Tab is also allowed to leave.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var added = new List<string>();

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.QuickEdit, true)
            .Add(l => l.OnRename, _ => { })
            .Add(l => l.OnAdd, title => added.Add(title))
            .Add(l => l.TestId, "list"));

        view.Find("[data-testid='list-add-input']").Input("Fourth");
        view.Find("[data-testid='list-add-input']").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal("Fourth", Assert.Single(added));

        var field = view.Find("[data-testid='list-add-input']");
        Assert.True(string.IsNullOrEmpty(field.GetAttribute("value")));
    }

    [Fact]
    public void Enter_on_an_empty_add_field_does_nothing_at_all()
    {
        // There is no task in an empty field, and Enter is not a way out of anywhere.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var added = new List<string>();

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.QuickEdit, true)
            .Add(l => l.OnRename, _ => { })
            .Add(l => l.OnAdd, title => added.Add(title))
            .Add(l => l.TestId, "list"));

        view.Find("[data-testid='list-add-input']").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Empty(added);
        Assert.NotNull(view.Find("[data-testid='list-add-input']"));
    }

    [Fact]
    public void Escape_clears_the_add_field_and_reports_nothing()
    {
        // Nothing to close, so Escape means the one thing left for it to mean. The
        // reader stays where they were: abandoning a title is not leaving the list.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var added = new List<string>();

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.QuickEdit, true)
            .Add(l => l.OnRename, _ => { })
            .Add(l => l.OnAdd, title => added.Add(title))
            .Add(l => l.TestId, "list"));

        view.Find("[data-testid='list-add-input']").Input("Never mind");
        view.Find("[data-testid='list-add-input']").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(added);

        var field = view.Find("[data-testid='list-add-input']");
        Assert.True(string.IsNullOrEmpty(field.GetAttribute("value")));
    }

    [Fact]
    public void Leaving_the_add_field_settles_what_was_typed_into_it()
    {
        // The same promise the rename directly above it makes. Two fields side by
        // side with opposite blur rules would be worse than either rule, and a
        // permanent composer never closes — so there is no moment you could point at
        // and call abandonment, only "parked while I check something".
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var added = new List<string>();

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.QuickEdit, true)
            .Add(l => l.OnRename, _ => { })
            .Add(l => l.OnAdd, title => added.Add(title))
            .Add(l => l.TestId, "list"));

        view.Find("[data-testid='list-add-input']").Input("Fourth");
        view.Find("[data-testid='list-add-input']").Blur();

        Assert.Equal("Fourth", Assert.Single(added));

        var field = view.Find("[data-testid='list-add-input']");
        Assert.True(string.IsNullOrEmpty(field.GetAttribute("value")));
    }

    [Fact]
    public void A_title_settled_by_a_key_is_not_settled_again_by_the_blur_behind_it()
    {
        // The keys clear the field before they report, so a blur that follows a
        // commit finds nothing left to commit. One title, one task.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var added = new List<string>();

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.QuickEdit, true)
            .Add(l => l.OnRename, _ => { })
            .Add(l => l.OnAdd, title => added.Add(title))
            .Add(l => l.TestId, "list"));

        view.Find("[data-testid='list-add-input']").Input("Fourth");
        view.Find("[data-testid='list-add-input']").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        view.Find("[data-testid='list-add-input']").Blur();

        Assert.Equal(["Fourth"], added);
    }

    [Fact]
    public void The_add_row_offers_nothing_to_complete_copy_or_rename()
    {
        // There is no task on it yet. A circle would offer to finish something that
        // does not exist, the copy button would copy nothing, and the pencil would
        // open the editor that is already open.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.QuickEdit, true)
            .Add(l => l.OnRename, _ => { })
            .Add(l => l.OnAdd, _ => { })
            .Add(l => l.TestId, "list"));

        var row = view.Find("[data-testid='list-add']");

        Assert.Empty(row.QuerySelectorAll("button"));
        Assert.Single(row.QuerySelectorAll("input"));
    }

    [Fact]
    public void The_row_a_Tab_lands_on_arrives_with_its_title_selected()
    {
        // Arriving in the field has to be arriving ready to type. Focus alone puts
        // the caret after the old title, so the first keystroke would extend the
        // title the reader came to replace.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.QuickEdit, true)
            .Add(l => l.OnRename, _ => { })
            .Add(l => l.TestId, "list"));

        view.Find("[data-testid='list-a-edit']").Click();
        view.Find("[data-testid='list-a-rename']").KeyDown(new KeyboardEventArgs { Key = "Tab" });

        var field = view.Find("[data-testid='list-b-rename']");
        var focus = context.JSInterop.Invocations["backlogFocus"].Last();

        Assert.Equal(field.Id, focus.Arguments[0]);
        Assert.Equal(true, focus.Arguments[1]);
    }

    [Fact]
    public void A_rename_takes_every_Tab_and_the_add_field_takes_only_a_filled_one()
    {
        // The browser has to be told before the first keystroke, because a keydown
        // that has reached .NET is one it has already acted on — so the two fields
        // ask for different guards rather than answering per key.
        //
        // A rename owns Tab outright, both ways: forward hands the editor down the
        // list and Shift+Tab hands it back up. The add field owns far less, because
        // Tab out of an empty one is how a reader leaves the list at all.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.QuickEdit, true)
            .Add(l => l.OnRename, _ => { })
            .Add(l => l.OnAdd, _ => { })
            .Add(l => l.TestId, "list"));

        // Armed as soon as there is a field to arm, chain or no chain: a reader who
        // clicks straight into the composer gets the same bargain as one who tabbed
        // down to it.
        var add = view.Find("[data-testid='list-add-input']");
        var guardAdd = context.JSInterop.Invocations["backlogGuardTab"].Single();

        Assert.Equal(add.Id, guardAdd.Arguments[0]);
        Assert.Equal("filled", guardAdd.Arguments[1]);

        view.Find("[data-testid='list-a-edit']").Click();

        var rename = view.Find("[data-testid='list-a-rename']");
        var guardRename = context.JSInterop.Invocations["backlogGuardTab"].Last();

        Assert.Equal(rename.Id, guardRename.Arguments[0]);
        Assert.Single(guardRename.Arguments);
    }

    [Fact]
    public void Escape_in_a_rename_puts_the_focus_ring_back_on_that_rows_pencil()
    {
        // A dismissal restores the focus to its trigger. Without it the field that
        // had the focus is simply gone, the ring lands on the document body, and a
        // keyboard reader has to tab in from the top of the page to get back to the
        // row they were standing on.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.QuickEdit, true)
            .Add(l => l.OnRename, _ => { })
            .Add(l => l.OnAdd, _ => { })
            .Add(l => l.TestId, "list"));

        view.Find("[data-testid='list-a-edit']").Click();
        view.Find("[data-testid='list-a-rename']").KeyDown(new KeyboardEventArgs { Key = "Tab" });

        // Escaped from the row the chain handed the editor to, which is the case the
        // pencil was never clicked in — the trigger is still the pencil.
        view.Find("[data-testid='list-b-rename']").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        var pencil = view.Find("[data-testid='list-b-edit']");
        var focus = context.JSInterop.Invocations["backlogFocus"].Last();

        Assert.Equal(pencil.Id, focus.Arguments[0]);

        // Nothing selected: it is a button, and there is nothing on it to select.
        Assert.Equal(false, focus.Arguments[1]);
    }

    [Fact]
    public void Finished_rows_move_to_a_section_of_their_own()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, new TaskRow[]
            {
                new("a", "Open"),
                new("b", "Finished", Done: true),
                new("c", "Also finished", Done: true)
            })
            .Add(l => l.TestId, "list"));

        // The open list holds only the unfinished one.
        Assert.Equal(["Open"], view.Find("ul.task-list").QuerySelectorAll(".task-item__title").Select(t => t.TextContent));

        var completed = view.Find("[data-testid='list-completed']");
        Assert.Contains("2", completed.QuerySelector(".task-list__completed-count")!.TextContent);

        // Folded to start with: the reason to open it is usually to undo one.
        Assert.Equal("false", view.Find("[data-testid='list-completed-toggle']").GetAttribute("aria-expanded"));

        view.Find("[data-testid='list-completed-toggle']").Click();

        Assert.Equal("true", view.Find("[data-testid='list-completed-toggle']").GetAttribute("aria-expanded"));
        Assert.Equal(2, view.Find("[data-testid='list-completed']").QuerySelectorAll(".task-item").Length);
    }

    /// <summary>
    /// The host's own content goes between the open rows and the Completed section.
    /// <para>
    /// It exists because "add one more" belongs at the end of the work, and the list
    /// owns the Completed section: a control the host rendered after the whole
    /// component landed underneath a fold of things already done — the one place in
    /// the list where nothing new is ever going to appear. Asserted by document
    /// position rather than by presence, because presence is what was already true
    /// and wrong.
    /// </para>
    /// </summary>
    [Fact]
    public void The_hosts_own_content_sits_above_the_completed_section()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, new TaskRow[] { new("a", "Open"), new("b", "Finished", Done: true) })
            .Add(l => l.AfterOpenRows, (RenderFragment)(builder =>
            {
                builder.OpenElement(0, "button");
                builder.AddAttribute(1, "data-testid", "add-one-more");
                builder.AddContent(2, "New entry");
                builder.CloseElement();
            }))
            .Add(l => l.TestId, "list"));

        // Read off the rendered markup, because the assertion is about order rather
        // than about presence — presence was already true and still wrong.
        var markup = view.Markup;
        var slot = markup.IndexOf("add-one-more", StringComparison.Ordinal);
        var completed = markup.IndexOf("list-completed", StringComparison.Ordinal);

        Assert.True(slot >= 0 && completed >= 0);
        Assert.True(slot < completed, "The Completed section should come after the host's own content.");
    }

    /// <summary>An unfilled slot renders nothing, so the markup is exactly what it was
    /// before the slot existed.</summary>
    [Fact]
    public void An_unfilled_slot_leaves_the_list_as_it_was()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, new TaskRow[] { new("a", "Open") })
            .Add(l => l.TestId, "list"));

        Assert.Empty(view.FindAll("[data-testid='add-one-more']"));
        Assert.Single(view.FindAll("li.task-item"));
    }

    [Fact]
    public void A_finished_row_cannot_be_dragged_because_its_place_stopped_meaning_anything()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, new TaskRow[] { new("a", "Open"), new("b", "Finished", Done: true) })
            .Add(l => l.Reorderable, true)
            .Add(l => l.TestId, "list"));

        view.Find("[data-testid='list-completed-toggle']").Click();

        var rows = view.FindAll("li.task-item");
        Assert.Equal("true", rows[0].GetAttribute("draggable"));
        Assert.False(rows[1].HasAttribute("draggable"));
    }

    [Fact]
    public void Grouping_can_be_turned_off_for_a_list_that_wants_them_in_place()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, new TaskRow[] { new("a", "Open"), new("b", "Finished", Done: true) })
            .Add(l => l.GroupCompleted, false)
            .Add(l => l.TestId, "list"));

        Assert.Empty(view.FindAll("[data-testid='list-completed']"));
        Assert.Equal(["Open", "Finished"], view.FindAll(".task-item__title").Select(t => t.TextContent));
    }

    [Fact]
    public void An_empty_list_says_so_in_whatever_words_the_caller_chose()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Array.Empty<TaskRow>())
            .Add(l => l.EmptyMessage, "Nothing due today."));

        Assert.Equal("Nothing due today.", view.Find(".task-list__empty").TextContent);
    }

    /// <summary>And says nothing when the caller has nothing to say. A list that
    /// draws its own add row already shows empty as an empty field, and the line
    /// above it would be the second answer to a question nobody asked.</summary>
    [Fact]
    public void An_empty_list_says_nothing_when_the_caller_left_the_words_out()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Array.Empty<TaskRow>())
            .Add(l => l.EmptyMessage, string.Empty));

        Assert.Empty(view.FindAll(".task-list__empty"));
    }

    [Fact]
    public void An_action_that_is_set_says_what_to_and_offers_to_clear_it()
    {
        using var context = new BunitContext();
        var cleared = false;

        var view = context.Render<TaskAction>(p => p
            .Add(a => a.Icon, "⏰")
            .Add(a => a.Label, "Remind me")
            .Add(a => a.Value, "Monday · 09:00")
            .Add(a => a.Set, true)
            .Add(a => a.OnClear, () => cleared = true)
            .Add(a => a.TestId, "remind"));

        Assert.Equal("Monday · 09:00", view.Find(".task-action__value").TextContent);

        view.Find("[data-testid='remind-clear']").Click();
        Assert.True(cleared);
    }

    [Fact]
    public void An_action_that_is_not_set_says_what_it_would_do_and_offers_no_clear()
    {
        using var context = new BunitContext();

        var view = context.Render<TaskAction>(p => p
            .Add(a => a.Icon, "⏰")
            .Add(a => a.Label, "Remind me")
            .Add(a => a.Value, "Monday · 09:00")
            .Add(a => a.OnClear, () => { })
            .Add(a => a.TestId, "remind"));

        Assert.Empty(view.FindAll(".task-action__value"));
        Assert.Empty(view.FindAll("[data-testid='remind-clear']"));
    }

    [Fact]
    public void A_toggling_action_says_which_way_it_is_and_never_offers_a_clear()
    {
        // Pressing it again is how it comes off, so an ✕ would be a second way
        // to do the one thing the button already does.
        using var context = new BunitContext();

        var view = context.Render<TaskAction>(p => p
            .Add(a => a.Icon, "☀")
            .Add(a => a.Label, "Added to My Day")
            .Add(a => a.Set, true)
            .Add(a => a.Togglable, true)
            .Add(a => a.TestId, "myday"));

        Assert.Equal("true", view.Find("[data-testid='myday-set']").GetAttribute("aria-pressed"));
        Assert.Empty(view.FindAll("[data-testid='myday-clear']"));
    }

    [Fact]
    public void Alt_arrow_down_moves_the_focused_row_and_reports_where_it_went()
    {
        // A drag is a pointer gesture with no keyboard equivalent, so a list that
        // only dragged would be a list some people cannot reorder at all.
        using var context = new BunitContext();
        context.JSInterop.SetupVoid("backlogFocus", _ => true);
        context.JSInterop.SetupVoid("taskListDrag.register", _ => true);
        context.JSInterop.SetupVoid("taskListDrag.unregister", _ => true);
        TaskMove? move = null;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.Reorderable, true)
            .Add(l => l.OnReorder, m => move = m)
            .Add(l => l.TestId, "list"));

        view.FindAll(".task-item__body")[0].KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });

        Assert.Equal("a", move?.Id);
        Assert.Equal("b", move?.TargetId);

        // Reported, not applied — the host owns the order, exactly as with a drop.
        Assert.Equal(["First", "Second", "Third"], view.FindAll(".task-item__title").Select(t => t.TextContent));
    }

    [Fact]
    public void Alt_arrow_up_moves_the_row_the_other_way()
    {
        using var context = new BunitContext();
        context.JSInterop.SetupVoid("backlogFocus", _ => true);
        context.JSInterop.SetupVoid("taskListDrag.register", _ => true);
        context.JSInterop.SetupVoid("taskListDrag.unregister", _ => true);
        TaskMove? move = null;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.Reorderable, true)
            .Add(l => l.OnReorder, m => move = m));

        view.FindAll(".task-item__body")[2].KeyDown(new KeyboardEventArgs { Key = "ArrowUp", AltKey = true });

        Assert.Equal("c", move?.Id);
        Assert.Equal("b", move?.TargetId);
    }

    [Fact]
    public void A_plain_arrow_is_left_alone_so_a_long_list_can_still_be_read()
    {
        // Swallowing ArrowDown to reorder would take away scrolling to add moving.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var raised = 0;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.Reorderable, true)
            .Add(l => l.OnReorder, _ => raised++));

        view.FindAll(".task-item__body")[0].KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        view.FindAll(".task-item__body")[0].KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });

        Assert.Equal(0, raised);
    }

    [Fact]
    public void A_row_at_either_end_does_not_wrap_around()
    {
        // A row at the top that jumped to the bottom on one more press would be a
        // move nobody asked for.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var raised = 0;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.Reorderable, true)
            .Add(l => l.OnReorder, _ => raised++));

        view.FindAll(".task-item__body")[0].KeyDown(new KeyboardEventArgs { Key = "ArrowUp", AltKey = true });
        view.FindAll(".task-item__body")[2].KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });

        Assert.Equal(0, raised);
    }

    [Fact]
    public void A_list_that_is_not_reorderable_does_not_move_on_the_keyboard_either()
    {
        // Parity runs both ways: no grip, no Alt+Arrow.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var raised = 0;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.OnReorder, _ => raised++));

        view.FindAll(".task-item__body")[0].KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });

        Assert.Equal(0, raised);
    }

    [Fact]
    public void A_finished_row_cannot_be_moved_by_key_any_more_than_it_can_be_dragged()
    {
        // Its place in the order stopped meaning anything when it left the list.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var raised = 0;

        IReadOnlyList<TaskRow> tasks =
        [
            new("a", "First"),
            new("done", "Finished", Done: true)
        ];

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, tasks)
            .Add(l => l.Reorderable, true)
            .Add(l => l.GroupCompleted, false)
            .Add(l => l.OnReorder, _ => raised++));

        var finished = view.FindAll("li.task-item")[1];
        Assert.Contains("task-item--done", finished.ClassList);

        finished.QuerySelector(".task-item__body")!.KeyDown(new KeyboardEventArgs { Key = "ArrowUp", AltKey = true });

        Assert.Equal(0, raised);
    }

    [Fact]
    public void The_focus_follows_the_row_that_moved()
    {
        // After the host applies the move the row is a different element, so the
        // focus ring is gone and the next press would move whatever slid into the
        // old slot. The id is the only handle that survives the re-render.
        using var context = new BunitContext();
        context.JSInterop.SetupVoid("backlogFocus", _ => true);
        context.JSInterop.SetupVoid("taskListDrag.register", _ => true);
        context.JSInterop.SetupVoid("taskListDrag.unregister", _ => true);

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.Reorderable, true)
            .Add(l => l.OnReorder, _ => { }));

        var ids = view.FindAll(".task-item__body").Select(b => b.GetAttribute("id")).ToList();

        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.Equal(3, ids.Distinct(StringComparer.Ordinal).Count());

        view.FindAll(".task-item__body")[0].KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });

        // The row that moved, not the position it left.
        var invocation = Assert.Single(context.JSInterop.Invocations["backlogFocus"]);
        Assert.Equal(ids[0], Assert.Single(invocation.Arguments) as string);
    }

    [Fact]
    public void A_move_that_goes_nowhere_does_not_disturb_the_focus()
    {
        // Nothing moved, so there is nothing to follow — and stealing the focus
        // back to a row the reader is already on would be a change they did not ask
        // for.
        using var context = new BunitContext();
        context.JSInterop.SetupVoid("backlogFocus", _ => true);
        context.JSInterop.SetupVoid("taskListDrag.register", _ => true);
        context.JSInterop.SetupVoid("taskListDrag.unregister", _ => true);

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.Reorderable, true)
            .Add(l => l.OnReorder, _ => { }));

        view.FindAll(".task-item__body")[0].KeyDown(new KeyboardEventArgs { Key = "ArrowUp", AltKey = true });

        Assert.Empty(context.JSInterop.Invocations["backlogFocus"]);
    }

    [Fact]
    public void Two_lists_on_one_page_do_not_mint_the_same_row_ids()
    {
        // An open list and an archive of the same work can hold the same task ids,
        // and an id belongs to the document rather than to the component. Sharing
        // them would let a move in one list put the focus in the other.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var first = context.Render<TaskListView>(p => p.Add(l => l.Tasks, Three).Add(l => l.Reorderable, true));
        var second = context.Render<TaskListView>(p => p.Add(l => l.Tasks, Three).Add(l => l.Reorderable, true));

        var firstIds = first.FindAll(".task-item__body").Select(b => b.GetAttribute("id")!).ToList();
        var secondIds = second.FindAll(".task-item__body").Select(b => b.GetAttribute("id")!).ToList();

        Assert.Empty(firstIds.Intersect(secondIds, StringComparer.Ordinal));
    }

    [Fact]
    public void A_keyboard_move_says_where_the_row_landed()
    {
        // The visible answer to "where did it go" is the row having moved, which
        // is no answer at all to somebody who cannot see the list.
        using var context = new BunitContext();
        context.JSInterop.SetupVoid("backlogFocus", _ => true);
        context.JSInterop.SetupVoid("taskListDrag.register", _ => true);
        context.JSInterop.SetupVoid("taskListDrag.unregister", _ => true);

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.Reorderable, true)
            .Add(l => l.OnReorder, _ => { })
            .Add(l => l.TestId, "list"));

        var live = view.Find("[data-testid='list-announcement']");

        // In the DOM before it changes, or the first announcement is the one
        // nobody hears.
        Assert.Equal("status", live.GetAttribute("role"));
        Assert.Equal("polite", live.GetAttribute("aria-live"));
        Assert.Equal(string.Empty, live.TextContent.Trim());

        view.FindAll(".task-item__body")[0].KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });

        Assert.Equal(
            "Moved First to 2 of 3.",
            view.Find("[data-testid='list-announcement']").TextContent.Trim());
    }

    [Fact]
    public void A_row_moved_by_key_swaps_with_the_row_the_reader_can_see()
    {
        // The neighbour comes from the order on screen, not from Tasks. With
        // finished rows folded away those two differ, and using the underlying one
        // would move a row past something invisible.
        using var context = new BunitContext();
        context.JSInterop.SetupVoid("backlogFocus", _ => true);
        context.JSInterop.SetupVoid("taskListDrag.register", _ => true);
        context.JSInterop.SetupVoid("taskListDrag.unregister", _ => true);
        TaskMove? move = null;

        IReadOnlyList<TaskRow> tasks =
        [
            new("a", "First"),
            new("done", "Finished", Done: true),
            new("c", "Third")
        ];

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, tasks)
            .Add(l => l.Reorderable, true)
            .Add(l => l.OnReorder, m => move = m));

        // The open list is First then Third; the finished row is in its own
        // section, so moving First down must land it on Third.
        view.FindAll("ul.task-list")[0].QuerySelectorAll(".task-item__body")[0]
            .KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });

        Assert.Equal("a", move?.Id);
        Assert.Equal("c", move?.TargetId);
    }


    // --- The status on a row -----------------------------------------------



    /// <summary>

    /// A status reads on the title's line rather than in the metadata line under

    /// it, drawn by the same <c>StatusBadge</c> the panel puts beside its heading.

    /// <para>

    /// The line under a title says when the task happens and how far through it

    /// is; a status says what the task currently <em>is</em>, which is what the

    /// title is doing. Same fact, same shape, same place in a row and in the panel

    /// that row opens into — so a reader is not made to look for it twice.

    /// </para>

    /// </summary>

    [Fact]

    public void A_status_is_a_badge_beside_the_title_and_not_a_detail()

    {

        using var context = new BunitContext();



        var view = context.Render<TaskItem>(p => p

            .Add(t => t.Task, new TaskRow("a", "Ship it", Group: "Tasks", Status: "in progress"))
            .Add(t => t.TestId, "row"));



        var badge = view.Find("[data-testid='row-status']");



        Assert.Contains("task-item__status", badge.ClassList);

        Assert.Contains("badge--status-inprogress", badge.ClassList);

        Assert.Equal("in progress", badge.TextContent);



        // Not on the metadata line, which is a different kind of fact.

        Assert.DoesNotContain("in progress", view.Find(".task-item__meta").TextContent, StringComparison.Ordinal);

    }



    [Fact]

    public void A_row_with_no_status_draws_no_badge()

    {

        // Not every list of tasks has a lifecycle — a checklist of sub-items has

        // none — and a row drawing an empty badge would claim a state nobody set.

        using var context = new BunitContext();



        var view = context.Render<TaskItem>(p => p

            .Add(t => t.Task, new TaskRow("a", "Ship it", Group: "Tasks"))
            .Add(t => t.TestId, "row"));



        Assert.Empty(view.FindAll(".task-item__status"));

        Assert.Null(new TaskRow("a", "Ship it").Status);

    }



    /// <summary>A row whose title is a field keeps its status, for the reason it

    /// keeps its metadata line: the same facts must not be dropped by a decision

    /// about how the title is edited.</summary>

    [Fact]

    public void A_row_being_renamed_still_says_where_it_has_got_to()

    {

        using var context = new BunitContext();

        context.JSInterop.Mode = JSRuntimeMode.Loose;



        var view = context.Render<TaskItem>(p => p

            .Add(t => t.Task, new TaskRow("a", "Ship it", Status: "ready"))
            .Add(t => t.OnRename, (TaskRename _) => { })

            .Add(t => t.DirectRename, true)
            .Add(t => t.TestId, "row"));



        Assert.NotNull(view.Find("[data-testid='row-rename']"));

        Assert.Equal("ready", view.Find("[data-testid='row-status']").TextContent);

    }

    /// <summary>What a list's rows copy, decided per row by the host.
    /// <para>
    /// The same shape as <c>RowCssClass</c>, and for the same reason: a
    /// <c>TaskRow</c> is a task, so the text a host would rather hand over — an
    /// entry's whole markdown, a step's chapter — is not on it and can only come
    /// from the surface that has the document.
    /// </para></summary>
    [Fact]
    public void A_list_can_say_what_each_of_its_rows_copies()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.RowCopyValue, task => $"{task.Title}\n\nas a prompt")
            .Add(l => l.TestId, "list"));

        view.Find("[data-testid='list-b-copy']").Click();

        Assert.Equal(
            "Second\n\nas a prompt",
            Assert.Single(context.JSInterop.Invocations["backlogClipboard.copy"]).Arguments[0]);
    }

    /// <summary>A row the host said nothing about copies what it always copied.
    /// The hook is per row, so a list that answers for some rows and not others
    /// must not lose the default on the rest.</summary>
    [Fact]
    public void A_row_the_host_has_no_answer_for_still_copies_itself()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Three)
            .Add(l => l.RowCopyValue, task => task.Id == "a" ? "replaced" : null)
            .Add(l => l.TestId, "list"));

        view.Find("[data-testid='list-c-copy']").Click();

        Assert.Equal(
            "Third",
            Assert.Single(context.JSInterop.Invocations["backlogClipboard.copy"]).Arguments[0]);
    }

    /// <summary>Where the copy button sits on the row: after the state, after the
    /// host's own slot, and still ahead of the bin.
    /// <para>
    /// The order is the assertion, not the presence. Everything from the circle to
    /// the actions slot says what the task is and where it has got to — a host puts
    /// its status picker in that slot — and copying is the one act that takes the
    /// task away with you, so it reads once the row has finished answering. The bin
    /// stays last, because it is the only control whose result is that there is no
    /// row.
    /// </para></summary>
    [Fact]
    public void The_copy_button_reads_after_the_state_and_before_the_bin()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Ship it"))
            .Add(t => t.OnDelete, (string _) => { })
            .Add(t => t.Actions, (RenderFragment<TaskRow>)(task => builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "data-testid", "host-state");
                builder.AddContent(2, "ready");
                builder.CloseElement();
            }))
            .Add(t => t.TestId, "row"));

        var row = view.Find(".task-item");
        var order = row.Children.Select(c => c.ClassName ?? string.Empty).ToList();

        int IndexOf(string cssClass) => order.FindIndex(c => c.Split(' ').Contains(cssClass));

        var state = IndexOf("task-item__actions");
        var copy = IndexOf("task-item__copy");
        var bin = IndexOf("task-item__delete");

        var drawn = string.Join(", ", order);
        Assert.True(state >= 0 && copy >= 0 && bin >= 0, $"Missing a control: {drawn}");
        Assert.True(state < copy, $"Copy must follow the state, but the order was: {drawn}");
        Assert.True(copy < bin, $"The bin must stay last, but the order was: {drawn}");
    }

    // --- A wider universe to resolve against --------------------------------

    /// <summary>A row this list draws can wait on an id that names no row in
    /// <c>Tasks</c> at all — a host scoping the drawn rows to one repository,
    /// say — and still say the true thing about it, provided the host also hands
    /// over a wider <c>Universe</c> to look the id up in.</summary>
    [Fact]
    public void A_dependency_outside_tasks_resolves_against_the_universe()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        IReadOnlyList<TaskRow> tasks = [new("here", "In view", DependsOn: ["elsewhere"])];
        IReadOnlyList<TaskRow> universe = [.. tasks, new("elsewhere", "Out of view", Done: true)];

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, tasks)
            .Add(l => l.Universe, universe)
            .Add(l => l.GroupCompleted, false)
            .Add(l => l.TestId, "list"));

        // Ready rather than blocked, because the row it named turned out to be
        // finished — not merely absent from this narrower view.
        Assert.Empty(view.FindAll(".task-item__detail--blocked"));
        Assert.NotNull(view.Find("[data-testid='list-here-next']"));
    }

    /// <summary>The other half of the same proof: unfinished in the universe
    /// still blocks, and is named by the title the universe carries for it
    /// rather than by the bare id — the row is unknown to <c>Tasks</c>, not
    /// unknown altogether.</summary>
    [Fact]
    public void An_outstanding_dependency_in_the_universe_still_blocks_and_is_named()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        IReadOnlyList<TaskRow> tasks = [new("here", "In view", DependsOn: ["elsewhere"])];
        IReadOnlyList<TaskRow> universe = [.. tasks, new("elsewhere", "Out of view, unfinished")];

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, tasks)
            .Add(l => l.Universe, universe)
            .Add(l => l.GroupCompleted, false)
            .Add(l => l.TestId, "list"));

        var waiting = view.Find("[data-testid='list-here'] .task-item__detail--blocked");

        Assert.Contains("Out of view, unfinished", waiting.TextContent, StringComparison.Ordinal);
    }

    /// <summary>Left unset, a list resolves exactly as it always did — against
    /// the rows it was handed and nothing wider. The parameter is additive, and
    /// every list rendered before it existed must keep reading the way it always
    /// has.</summary>
    [Fact]
    public void With_no_universe_supplied_a_dependency_outside_tasks_is_unknown()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        IReadOnlyList<TaskRow> tasks = [new("here", "In view", DependsOn: ["elsewhere"])];

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, tasks)
            .Add(l => l.GroupCompleted, false)
            .Add(l => l.TestId, "list"));

        var waiting = view.Find("[data-testid='list-here'] .task-item__detail--blocked");

        Assert.Contains("elsewhere", waiting.TextContent, StringComparison.Ordinal);
    }
}
