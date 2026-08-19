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

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Array.Empty<TaskRow>())
            .Add(l => l.EmptyMessage, "Nothing due today."));

        Assert.Equal("Nothing due today.", view.Find(".task-list__empty").TextContent);
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
}
