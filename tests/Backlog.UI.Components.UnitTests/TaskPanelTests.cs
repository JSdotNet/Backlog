namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The side panel, and the detail pane inside it.
///
/// <para>The panel's only decisions are the order of its parts and the heading
/// line, so that is what most of these assert. The pane's is that it lays out
/// groups rather than rows — two things side by side read as related whether or
/// not they are — with one row across the top that is not a fact about the task at
/// all.</para>
/// </summary>
public sealed class TaskPanelTests
{
    [Fact]
    public void The_panel_puts_its_parts_in_one_order()
    {
        // Identity, classification, settings, content. What a reader opened the
        // panel to check is at the top; what they came to write is last, because
        // it is the only part with no natural height.
        using var context = new BunitContext();

        var view = context.Render<TaskPanel>(p => p
            .Add(t => t.Title, "Wire the pane into the shell")
            .Add(t => t.Status, "In progress")
            .Add(t => t.Tags, (IReadOnlyList<string>)["ui"])
            .Add(t => t.Filing, "<p>the badges</p>")
            .Add(t => t.Details, "<p>the rows</p>")
            .Add(t => t.Body, "<p>the sub-items</p>")
            .Add(t => t.Footer, "<p>close and delete</p>")
            .Add(t => t.TestId, "panel"));

        var panel = view.Find("[data-testid='panel']");

        // Filing is with the tags rather than after the settings: "what kind of
        // thing is this" and "what is it tagged" are the same question asked twice.
        // The footer is after the body, so nothing that acts on the whole task
        // stands between the reader and the part of it they came to write.
        Assert.Equal(
            [
                "task-panel__header",
                "task-panel__tags",
                "task-panel__filing",
                "task-panel__details",
                "task-panel__body",
                "task-panel__footer"
            ],
            panel.Children.Select(child => child.ClassName));
    }

    [Fact]
    public void The_heading_line_is_a_circle_then_the_title_then_the_status()
    {
        // The same three things a row in the list is, in the same order. Sharing a
        // line is only real if they share a parent.
        using var context = new BunitContext();

        var view = context.Render<TaskPanel>(p => p
            .Add(t => t.Title, "Wire the pane into the shell")
            .Add(t => t.Status, "In progress")
            .Add(t => t.OnToggle, () => { })
            .Add(t => t.TestId, "panel"));

        var header = view.Find(".task-panel__header");

        Assert.Equal(["button", "h2", "span"], header.Children.Select(child => child.LocalName));
        Assert.Contains("task-panel__check", header.Children[0].ClassList);
        Assert.Contains("badge--status-inprogress", header.Children[2].ClassList);
    }

    [Fact]
    public void The_circle_is_a_checkbox_to_anything_that_is_listening()
    {
        // Round is this shape's one convention and it is not worth losing the
        // semantics over — the same bargain TaskItem makes, drawn by the same rule.
        using var context = new BunitContext();
        var toggles = 0;

        var view = context.Render<TaskPanel>(p => p
            .Add(t => t.Title, "Wire the pane into the shell")
            .Add(t => t.OnToggle, () => toggles++)
            .Add(t => t.TestId, "panel"));

        var check = view.Find("[data-testid='panel-check']");

        Assert.Equal("checkbox", check.GetAttribute("role"));
        Assert.Equal("false", check.GetAttribute("aria-checked"));
        Assert.Equal("Wire the pane into the shell", check.GetAttribute("aria-label"));

        check.Click();

        Assert.Equal(1, toggles);
    }

    [Fact]
    public void A_finished_task_says_so_without_offering_a_control_nobody_is_behind()
    {
        // Done and nothing listening: the circle is state rather than a button, and
        // the title goes quiet. Not done and nothing listening, and there is no
        // circle at all — a checkbox that silently does nothing is worse than none.
        using var context = new BunitContext();

        var done = context.Render<TaskPanel>(p => p
            .Add(t => t.Title, "Already finished")
            .Add(t => t.Done, true)
            .Add(t => t.TestId, "panel"));

        var check = done.Find("[data-testid='panel-check']");

        Assert.Equal("span", check.LocalName);
        Assert.Equal("img", check.GetAttribute("role"));
        Assert.Equal("Done", check.GetAttribute("aria-label"));
        Assert.Contains("task-panel--done", done.Find("[data-testid='panel']").ClassList);

        var open = context.Render<TaskPanel>(p => p
            .Add(t => t.Title, "Not finished")
            .Add(t => t.TestId, "panel"));

        Assert.Empty(open.FindAll("[data-testid='panel-check']"));
    }

    [Fact]
    public void The_title_is_the_way_into_the_editor_and_there_is_no_pencil()
    {
        // A heading this size is the biggest target on the panel, so a pencil beside
        // it would be a second, smaller target for what the heading already is.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskPanel>(p => p
            .Add(t => t.Title, "Wire the pane into the shell")
            .Add(t => t.OnRename, _ => { })
            .Add(t => t.TestId, "panel"));

        var title = view.Find("[data-testid='panel-title']");

        Assert.Equal("button", title.LocalName);

        // The title is the only control on the line. Anything else here would be a
        // second, smaller target for what the heading already is.
        Assert.Single(view.FindAll(".task-panel__header button"));

        title.Click();

        var field = view.Find("[data-testid='panel-rename']");

        // The caret is in it: an editor that opened somewhere the typing does not
        // go is worse than not opening one.
        Assert.Equal(field.Id, context.JSInterop.Invocations["backlogFocus"].Last().Arguments[0]);
    }

    [Fact]
    public void The_panel_keeps_its_name_while_the_title_is_being_typed()
    {
        // The panel is named by its heading, so replacing that heading with a field
        // would take the name away mid-edit and leave a hole in the page's outline.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskPanel>(p => p
            .Add(t => t.Title, "Wire the pane into the shell")
            .Add(t => t.OnRename, _ => { })
            .Add(t => t.TestId, "panel"));

        var panel = view.Find("[data-testid='panel']");
        var heading = view.Find(".task-panel__title");

        Assert.Equal("h2", heading.LocalName);
        Assert.Equal(heading.Id, panel.GetAttribute("aria-labelledby"));

        view.Find("[data-testid='panel-title']").Click();

        var editing = view.Find(".task-panel__title");

        Assert.Equal("h2", editing.LocalName);
        Assert.Equal(editing.Id, panel.GetAttribute("aria-labelledby"));
        Assert.NotNull(view.Find(".task-panel__title [data-testid='panel-rename']"));
    }

    [Fact]
    public void Enter_settles_the_title_and_reports_it_once()
    {
        // No Save button: a title is one short string, so a second thing to reach
        // for after the one that already finished the job would be one too many.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var renames = new List<string>();

        var view = context.Render<TaskPanel>(p => p
            .Add(t => t.Title, "Wire the pane into the shell")
            .Add(t => t.OnRename, title => renames.Add(title))
            .Add(t => t.TestId, "panel"));

        view.Find("[data-testid='panel-title']").Click();
        view.Find("[data-testid='panel-rename']").Input("  Wire it into the shell  ");
        view.Find("[data-testid='panel-rename']").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        // Trimmed, and reported when it settled rather than per keystroke.
        Assert.Equal("Wire it into the shell", Assert.Single(renames));
        Assert.Empty(view.FindAll("[data-testid='panel-rename']"));
    }

    [Fact]
    public void Escape_abandons_the_title_and_hands_the_focus_back()
    {
        // A dismissal owes its trigger the focus ring. Without it the field that
        // had the focus is simply gone and a keyboard reader starts the page again.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var renames = new List<string>();

        var view = context.Render<TaskPanel>(p => p
            .Add(t => t.Title, "Wire the pane into the shell")
            .Add(t => t.OnRename, title => renames.Add(title))
            .Add(t => t.TestId, "panel"));

        view.Find("[data-testid='panel-title']").Click();
        view.Find("[data-testid='panel-rename']").Input("Thrown away");
        view.Find("[data-testid='panel-rename']").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(renames);

        var title = view.Find("[data-testid='panel-title']");

        Assert.Equal("Wire the pane into the shell", title.TextContent);
        Assert.Equal(title.Id, context.JSInterop.Invocations["backlogFocus"].Last().Arguments[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Wire the pane into the shell")]
    public void An_empty_or_unchanged_title_is_not_a_rename(string typed)
    {
        // A reader who cleared the field wants the title they had, not a task with
        // no name; and a title that comes back the same is not a change to save.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var renames = new List<string>();

        var view = context.Render<TaskPanel>(p => p
            .Add(t => t.Title, "Wire the pane into the shell")
            .Add(t => t.OnRename, title => renames.Add(title))
            .Add(t => t.TestId, "panel"));

        view.Find("[data-testid='panel-title']").Click();
        view.Find("[data-testid='panel-rename']").Input(typed);
        view.Find("[data-testid='panel-rename']").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Empty(renames);
    }

    [Fact]
    public void With_nobody_listening_the_title_is_a_heading_and_not_a_button()
    {
        using var context = new BunitContext();

        var view = context.Render<TaskPanel>(p => p
            .Add(t => t.Title, "Read only")
            .Add(t => t.TestId, "panel"));

        Assert.Empty(view.FindAll("[data-testid='panel-title']"));
        Assert.Equal("Read only", view.Find(".task-panel__title").TextContent);
    }

    [Fact]
    public void An_absent_part_is_drawn_as_nothing_rather_than_as_something_empty()
    {
        // A task typed into the add row a second ago has a title and nothing else,
        // and it should not open into a form of empty fields telling its author
        // how much they have not filled in yet.
        using var context = new BunitContext();

        var view = context.Render<TaskPanel>(p => p
            .Add(t => t.Title, "Ask about the Heerlen features")
            .Add(t => t.TestId, "panel"));

        Assert.Empty(view.FindAll("[data-testid='panel-status']"));
        Assert.Empty(view.FindAll(".task-panel__status"));
        Assert.Empty(view.FindAll(".task-panel__tags"));
        Assert.Empty(view.FindAll(".task-panel__filing"));
        Assert.Empty(view.FindAll(".task-panel__details"));
        Assert.Empty(view.FindAll(".task-panel__body"));
        Assert.Empty(view.FindAll(".task-panel__footer"));

        // And the heading survives on its own.
        Assert.Equal("Ask about the Heerlen features", view.Find(".task-panel__title").TextContent);
    }

    [Fact]
    public void An_empty_tag_list_leaves_no_strip_behind()
    {
        // A row of blank space is where a reader learns there is nothing to learn.
        using var context = new BunitContext();

        var view = context.Render<TaskPanel>(p => p
            .Add(t => t.Title, "Untagged")
            .Add(t => t.Tags, (IReadOnlyList<string>)[]));

        Assert.Empty(view.FindAll(".task-panel__tags"));
    }

    [Fact]
    public void The_body_carries_no_label_of_the_panels_own()
    {
        // What is in it is plainly a list or plainly prose, and a word over it would
        // be a word to keep true: "Steps" over a paragraph is worse than nothing
        // over either.
        using var context = new BunitContext();

        var view = context.Render<TaskPanel>(p => p
            .Add(t => t.Title, "With a body")
            .Add(t => t.Body, "<p>the body</p>"));

        var body = view.Find(".task-panel__body");

        Assert.Equal(["p"], body.Children.Select(child => child.LocalName));
    }

    [Fact]
    public void A_tag_is_text_until_somebody_is_listening()
    {
        // The library's standing rule: an affordance nobody is behind is worse
        // than no affordance. A chip with no click is a label, and a chip with no
        // removal has no ✕.
        using var context = new BunitContext();

        var view = context.Render<TaskPanel>(p => p
            .Add(t => t.Title, "Filed under two things")
            .Add(t => t.Tags, (IReadOnlyList<string>)["ui", "desktop"]));

        Assert.Equal(
            ["span", "span"],
            view.FindAll(".tag-chip__label").Select(chip => chip.LocalName));
        Assert.Empty(view.FindAll("[aria-label='Remove ui']"));
    }

    [Fact]
    public void Dropping_a_tag_says_which_one_it_was()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskPanelHarness>(p => p
            .Add(h => h.ListenForRemoval, true));

        view.Find("[aria-label='Remove ui']").Click();

        Assert.Equal("ui", view.Instance.RemovedTag);

        // And the host is the one that dropped it, so the chip goes when the host's
        // list does.
        Assert.Equal(["desktop"], view.FindAll(".tag-chip__label").Select(chip => chip.TextContent));
    }

    [Fact]
    public void Clicking_a_tag_says_which_one_it_was()
    {
        // Filtering by a tag and dropping it are different intents, so they are
        // separately opted into — this panel has the click and not the ✕.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskPanelHarness>(p => p
            .Add(h => h.ListenForClick, true));

        var chips = view.FindAll(".tag-chip__label");
        Assert.Equal(["button", "button"], chips.Select(chip => chip.LocalName));

        chips[1].Click();

        Assert.Equal("desktop", view.Instance.ClickedTag);
        Assert.Null(view.Instance.RemovedTag);
        Assert.Empty(view.FindAll("[aria-label='Remove desktop']"));
    }

    [Fact]
    public void The_pane_leads_with_one_row_across_it_then_lays_out_groups()
    {
        // The lead is the row that is a decision rather than a fact, so it is not in
        // a column with the facts. Rows poured straight into two tracks would break
        // "due date, reminder, repeat" across the gap and stand a due date next to a
        // priority.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskPanelHarness>();

        var pane = view.Find("[data-testid='pane']");

        // The lead is outside the columns, or it would be balanced along with the
        // groups and end up as the top of one of them rather than across both. The
        // trailing row is outside them for the mirror reason: a group whose value is
        // a list of other tasks needs the width, and leaving it in the balancing
        // would stop the two columns ending level.
        Assert.Equal(
            ["task-action-pane__lead", "task-action-pane__columns", "task-action-pane__trailing"],
            pane.Children.Select(child => child.ClassName));

        Assert.Equal(
            ["task-action-group", "task-action-group", "task-action-group"],
            view.Find(".task-action-pane__columns").Children.Select(child => child.ClassName));

        Assert.Equal(
            "dependencies",
            view.Find(".task-action-pane__trailing .task-action-group").GetAttribute("data-testid"));

        // The caption, then the rows it names.
        var scheduling = view.Find("[data-testid='scheduling']");

        Assert.Equal("p", scheduling.Children[0].LocalName);
        Assert.Equal("Scheduling", scheduling.Children[0].TextContent);
        Assert.Equal(
            ["task-action task-action--set", "task-action"],
            scheduling.Children.Skip(1).Select(child => child.ClassName));
    }

    [Fact]
    public void The_row_across_the_top_is_the_one_that_carries_weight()
    {
        // Prominence is a decision the host makes once. Two prominent rows is a pane
        // with no emphasis in it, so nothing else in the pane has the modifier.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskPanelHarness>();

        var lead = view.Find(".task-action-pane__lead .task-action");

        Assert.Contains("task-action--prominent", lead.ClassList);
        Assert.Equal("Add to My Day", view.Find("[data-testid='myday'] .task-action__label").TextContent.Trim());

        Assert.Single(view.FindAll(".task-action--prominent"));

        // Togglable, so pressing it again is how it comes off and there is no ✕.
        Assert.Equal("false", view.Find("[data-testid='myday-set']").GetAttribute("aria-pressed"));
        Assert.Empty(view.FindAll("[data-testid='myday-clear']"));
    }

    [Fact]
    public void Attachments_is_one_pointer_at_one_place_rather_than_a_row_per_file()
    {
        // The pane's height is the one measurement in a side panel everything else
        // has to live with, so a row per attached file would make it a function of
        // how many files somebody dropped on the task. One row, one folder, a count.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskPanelHarness>();

        var rows = view.FindAll("[data-testid='attachments'] .task-action");

        Assert.Single(rows);
        Assert.StartsWith("Folder", view.Find("[data-testid='files'] .task-action__label").TextContent.Trim());
        Assert.Equal("panel-review · 4 files", view.Find("[data-testid='files'] .task-action__value").TextContent);
    }

    [Fact]
    public void A_group_is_named_by_a_caption_nobody_draws()
    {
        // The grouping a sighted reader gets is the layout — rows together, no
        // break across the column gap, air between one group and the next — so the
        // caption is not drawn. It is still the group's accessible name, which is
        // the only grouping a reader who cannot see that layout gets, and that is
        // why the label is still required.
        //
        // A paragraph rather than a heading, for when it is read: a panel does not
        // know how deep in a page it is, and a group that guessed its own level
        // would put a hole in the document outline for the sake of one word.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskPanelHarness>();

        var group = view.Find("[data-testid='ranking']");
        var caption = view.Find("[data-testid='ranking'] .task-action-group__caption");

        Assert.Equal("group", group.GetAttribute("role"));
        Assert.Equal(caption.Id, group.GetAttribute("aria-labelledby"));
        Assert.Equal("Ranking", caption.TextContent);

        // Hidden from sight, not from the accessibility tree. aria-hidden or
        // display:none would take the name away with the picture.
        Assert.Contains("sr-only", caption.ClassList);
        Assert.Null(caption.GetAttribute("aria-hidden"));
    }

    [Fact]
    public void Two_panes_on_a_page_do_not_share_a_caption_id()
    {
        // Two panels open beside each other is an ordinary thing for a compare
        // view to do, and a duplicated id would point both groups at whichever
        // caption the browser found first.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var first = context.Render<TaskPanelHarness>();
        var second = context.Render<TaskPanelHarness>();

        Assert.NotEqual(
            first.Find("[data-testid='ranking'] .task-action-group__caption").Id,
            second.Find("[data-testid='ranking'] .task-action-group__caption").Id);
    }

    [Fact]
    public void The_priority_row_says_its_ranking_and_offers_to_clear_it()
    {
        // Set, with a value and an ✕ — the same state model the reminder and the
        // due date have, which is the reason priority is this control and not a
        // new one. And nowhere near the dates: a ranking and a deadline are the two
        // facts most often mistaken for each other.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskPanelHarness>();

        Assert.Equal("High", view.Find("[data-testid='priority'] .task-action__value").TextContent);
        Assert.Equal("Clear Priority", view.Find("[data-testid='priority-clear']").GetAttribute("aria-label"));

        // Its own group, not the scheduling one.
        Assert.Empty(view.FindAll("[data-testid='scheduling'] [data-testid='priority']"));

        // A row that is not set says what it would do, and has no ✕ to press.
        Assert.Empty(view.FindAll("[data-testid='remind'] .task-action__value"));
        Assert.Empty(view.FindAll("[data-testid='remind-clear']"));
    }

    [Fact]
    public void Sub_items_carry_no_pencil_because_the_title_is_already_the_field()
    {
        // A sub-item has no pane to open, so the click a pencil protects was never
        // going anywhere — and a pencil on every one of them is a column of the
        // smallest target on the panel down the side of one-line rows.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskPanelHarness>();

        Assert.Empty(view.FindAll("[data-testid='item-s1-edit']"));
        Assert.Empty(view.FindAll("[data-testid='item-s1-title']"));

        Assert.Equal(
            "First sub-item",
            view.Find("[data-testid='item-s1-rename']").GetAttribute("value"));
    }

    [Fact]
    public void Sub_items_quick_edit_down_the_list()
    {
        // Retitling them is one thing done many times over, so the keystroke that
        // finishes one opens the next. Being in a slot changes nothing: it is the
        // same list.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskPanelHarness>();

        view.Find("[data-testid='item-s1-rename']").Input("First sub-item, renamed");
        view.Find("[data-testid='item-s1-rename']").KeyDown(new KeyboardEventArgs { Key = "Tab" });

        var rename = Assert.Single(view.Instance.Renames);
        Assert.Equal("s1", rename.Id);
        Assert.Equal("First sub-item, renamed", rename.Title);

        // Both are fields throughout — every row's title is one — so what says the
        // edit moved on is which one the list is holding open, not which one exists.
        Assert.Equal("First sub-item, renamed", view.Find("[data-testid='item-s1-rename']").GetAttribute("value"));
        Assert.NotNull(view.Find("[data-testid='item-s2-rename']"));
    }

    [Fact]
    public void Ticking_the_panels_circle_and_retitling_it_both_reach_the_host()
    {
        // The panel stores neither: it reports what the reader did, exactly as a row
        // does, because only the host knows where either fact is kept.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskPanelHarness>();

        view.Find("[data-testid='panel-check']").Click();

        Assert.Equal(1, view.Instance.Toggles);
        Assert.True(view.Instance.Done);
        Assert.Contains("task-panel--done", view.Find("[data-testid='panel']").ClassList);

        view.Find("[data-testid='panel-title']").Click();
        view.Find("[data-testid='panel-rename']").Input("Wire it in");
        view.Find("[data-testid='panel-rename']").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal("Wire it in", view.Instance.Title);
        Assert.Equal("Wire it in", view.Find("[data-testid='panel-title']").TextContent);
    }

    // --- The status on the heading line ------------------------------------

    [Fact]
    public void A_host_with_a_status_control_puts_it_on_the_heading_line()
    {
        // A product whose status can be changed from the panel has a picker rather
        // than a label, and the alternative was the host putting that picker
        // somewhere else — which would move the fact off the line the reader just
        // read it on in the list.
        using var context = new BunitContext();

        var view = context.Render<TaskPanel>(p => p
            .Add(t => t.Title, "Wire the pane into the shell")
            .Add(t => t.StatusContent, "<select data-testid='picker'></select>")
            .Add(t => t.TestId, "panel"));

        var header = view.Find(".task-panel__header");

        // Third on the line, where the badge would have been: title, then state.
        Assert.Equal(["h2", "div"], header.Children.Select(child => child.LocalName));
        Assert.Contains("task-panel__status", header.Children[1].ClassList);
        Assert.NotNull(view.Find("[data-testid='panel-status'] [data-testid='picker']"));
    }

    [Fact]
    public void The_slot_wins_over_the_word()
    {
        // Two statuses on one heading line is a question about which of them is the
        // real one. The host that passed a control meant the control.
        using var context = new BunitContext();

        var view = context.Render<TaskPanel>(p => p
            .Add(t => t.Title, "Wire the pane into the shell")
            .Add(t => t.Status, "In progress")
            .Add(t => t.StatusContent, "<select data-testid='picker'></select>")
            .Add(t => t.TestId, "panel"));

        Assert.NotNull(view.Find("[data-testid='panel-status'] [data-testid='picker']"));
        Assert.DoesNotContain("In progress", view.Markup, StringComparison.Ordinal);
        Assert.Empty(view.FindAll(".badge--status-inprogress"));
    }

    // --- The way in and the way back out -----------------------------------
    //
    // A panel sits beside a list, and reading order puts it after the whole of that
    // list. Without these two a reader who tabs off the row they just opened walks
    // every remaining row before reaching the panel about it.

    [Fact]
    public async Task The_panel_focuses_its_circle_when_a_host_tabs_into_it()
    {
        // By id, because the element to focus is a different node after every
        // render — the same reason a row is focused by id rather than held.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskPanel>(p => p
            .Add(t => t.Title, "Tab into me")
            .Add(t => t.OnToggle, () => { })
            .Add(t => t.OnRename, (string _) => { })
            .Add(t => t.TestId, "panel"));

        await view.Instance.FocusFirstAsync();

        var focused = context.JSInterop.Invocations["backlogFocus"].Single();

        Assert.Equal(view.Find("[data-testid='panel-check']").Id, focused.Arguments[0]);
    }

    [Fact]
    public async Task With_no_circle_the_title_is_the_front_of_the_panel()
    {
        // Which control the front of the panel is, is the panel's business. A host
        // that had to name one would be a host that knew whether this task can be
        // ticked today.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskPanel>(p => p
            .Add(t => t.Title, "Nothing to tick")
            .Add(t => t.OnRename, (string _) => { })
            .Add(t => t.TestId, "panel"));

        await view.Instance.FocusFirstAsync();

        var focused = context.JSInterop.Invocations["backlogFocus"].Single();

        Assert.Equal(view.Find("[data-testid='panel-title']").Id, focused.Arguments[0]);
    }

    [Fact]
    public async Task A_panel_with_nothing_to_focus_asks_for_nothing()
    {
        // A heading and that is all it is. Aiming the focus at an element that is
        // not a control would be worse than leaving it where the reader put it.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskPanel>(p => p.Add(t => t.Title, "Just a heading"));

        await view.Instance.FocusFirstAsync();

        Assert.Empty(context.JSInterop.Invocations);
    }

    [Fact]
    public void Shift_tab_off_the_circle_is_the_way_back_out()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var left = 0;

        var view = context.Render<TaskPanel>(p => p
            .Add(t => t.Title, "Shift+Tab off me")
            .Add(t => t.OnToggle, () => { })
            .Add(t => t.OnRename, (string _) => { })
            .Add(t => t.OnTabBackward, () => left++)
            .Add(t => t.TestId, "panel"));

        view.Find("[data-testid='panel-check']").KeyDown(new KeyboardEventArgs { Key = "Tab", ShiftKey = true });

        Assert.Equal(1, left);

        // Plain Tab is the browser's, and so is Shift+Tab from the title: raising it
        // there too would put the circle behind a Shift+Tab that no longer goes
        // there, which is a circle no keyboard can reach.
        view.Find("[data-testid='panel-check']").KeyDown(new KeyboardEventArgs { Key = "Tab" });
        view.Find("[data-testid='panel-title']").KeyDown(new KeyboardEventArgs { Key = "Tab", ShiftKey = true });

        Assert.Equal(1, left);
    }

    [Fact]
    public void With_no_circle_the_title_is_what_reports_the_way_out()
    {
        // The rule is "the first control", not "the circle" — a panel with nothing
        // to tick still has to be leavable.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var left = 0;

        var view = context.Render<TaskPanel>(p => p
            .Add(t => t.Title, "Nothing to tick")
            .Add(t => t.OnRename, (string _) => { })
            .Add(t => t.OnTabBackward, () => left++)
            .Add(t => t.TestId, "panel"));

        view.Find("[data-testid='panel-title']").KeyDown(new KeyboardEventArgs { Key = "Tab", ShiftKey = true });

        Assert.Equal(1, left);
    }
}
