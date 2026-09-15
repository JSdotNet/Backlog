namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The column of lists a pane keeps on its edge: what it draws, what one tab
/// stop and the arrows do with it, and the three ways a row asks for something
/// — a click, a right-click, and a rename in place.
/// <para>
/// Enter and Space on a row are not asserted here because the row is a button
/// and its activation is the browser's: bUnit dispatches a keydown at the
/// element and nothing more, so a test that pressed Enter and expected a
/// selection would be testing a handler the component deliberately does not
/// have. The click is asserted instead, which is what those keys produce.
/// </para>
/// </summary>
public sealed class ListNavTests
{
    private static readonly IReadOnlyList<ListNavEntry> Fixed =
    [
        new("inbox", "Inbox", 12, "✉"),
        new("today", "Today", 0)
    ];

    private static readonly IReadOnlyList<ListNavGroup> Groups =
    [
        new("work", "Work", [new("backlog", "Backlog", 4), new("reading", "Reading", 7)]),
        new("home", "Home", [new("errands", "Errands", 2)])
    ];

    private static readonly IReadOnlyList<ListNavEntry> Ungrouped =
    [
        new("someday", "Someday", 31)
    ];

    private static IRenderedComponent<ListNav> Render(
        BunitContext context,
        Action<ComponentParameterCollectionBuilder<ListNav>>? configure = null,
        params string[] expanded)
    {
        var open = new HashSet<string>(expanded, StringComparer.Ordinal);

        return context.Render<ListNav>(parameters =>
        {
            parameters
                .Add(nav => nav.Fixed, Fixed)
                .Add(nav => nav.Groups, Groups)
                .Add(nav => nav.Ungrouped, Ungrouped)
                .Add(nav => nav.IsGroupExpanded, id => open.Contains(id))
                .Add(nav => nav.AriaLabel, "Lists")
                .Add(nav => nav.TestId, "nav");

            configure?.Invoke(parameters);
        });
    }

    private static string Tabbable(IRenderedComponent<ListNav> nav) =>
        nav.FindAll("button[tabindex='0']").Single().GetAttribute("data-testid")!;

    // --- What it draws -----------------------------------------------------

    [Fact]
    public void Fixed_rows_come_first_then_the_groups_then_the_loose_rows()
    {
        using var context = new BunitContext();

        var nav = Render(context, expanded: ["work", "home"]);

        Assert.Equal("Lists", nav.Find("nav").GetAttribute("aria-label"));
        Assert.Equal(
            ["nav-inbox", "nav-today", "nav-work", "nav-backlog", "nav-reading", "nav-home", "nav-errands", "nav-someday"],
            nav.FindAll("button").Select(button => button.GetAttribute("data-testid")).ToArray());
    }

    [Fact]
    public void Every_entry_carries_its_count_as_a_badge_in_the_navigations_own_class()
    {
        using var context = new BunitContext();

        var nav = Render(context);

        var count = nav.Find("[data-testid='nav-inbox-count']");

        Assert.Equal("12", count.TextContent);
        Assert.Equal("list-nav__count", count.GetAttribute("class"));

        // Zero is drawn, and says it is zero, so the stylesheet can quieten it
        // without the markup hiding it.
        var zero = nav.Find("[data-testid='nav-today-count']");
        Assert.Equal("0", zero.TextContent);
        Assert.Contains("list-nav__count--zero", zero.ClassList);

        // No `badge` anywhere: the count wears this component's class instead of
        // a badge family's tone.
        Assert.Empty(nav.FindAll(".badge"));
    }

    [Fact]
    public void An_icon_is_a_hidden_glyph_before_the_label_and_a_row_without_one_draws_nothing_there()
    {
        using var context = new BunitContext();

        var nav = Render(context);

        var icon = nav.Find("[data-testid='nav-inbox'] .list-nav__icon");
        Assert.Equal("✉", icon.TextContent);
        Assert.Equal("true", icon.GetAttribute("aria-hidden"));

        Assert.Empty(nav.FindAll("[data-testid='nav-today'] .list-nav__icon"));
    }

    [Fact]
    public void Nothing_to_show_is_a_nav_with_no_rows_and_no_lists()
    {
        using var context = new BunitContext();

        var nav = context.Render<ListNav>(parameters => parameters.Add(n => n.TestId, "nav"));

        Assert.NotNull(nav.Find("nav"));
        Assert.Empty(nav.FindAll("button"));
        Assert.Empty(nav.FindAll("ul"));
    }

    // --- Selection ---------------------------------------------------------

    [Fact]
    public void The_selected_row_is_current_and_wears_the_selected_class_and_no_other_does()
    {
        using var context = new BunitContext();

        var nav = Render(context, parameters => parameters.Add(n => n.SelectedId, "backlog"), "work");

        var selected = nav.Find("[data-testid='nav-backlog']");
        Assert.Equal("true", selected.GetAttribute("aria-current"));
        Assert.Contains("list-nav__row--selected", selected.ClassList);

        Assert.Single(nav.FindAll("[aria-current='true']"));
        Assert.Single(nav.FindAll(".list-nav__row--selected"));
    }

    [Fact]
    public void Clicking_a_row_asks_the_host_to_select_it_and_selects_nothing_itself()
    {
        using var context = new BunitContext();
        var selected = new List<string>();

        var nav = Render(context, parameters => parameters.Add(n => n.OnSelect, (string id) => selected.Add(id)));

        nav.Find("[data-testid='nav-someday']").Click();

        Assert.Equal(["someday"], selected);
        Assert.Empty(nav.FindAll("[aria-current='true']"));
    }

    // --- Groups ------------------------------------------------------------

    [Fact]
    public void A_closed_group_hides_its_rows_and_says_so_on_the_heading()
    {
        using var context = new BunitContext();

        var nav = Render(context, expanded: "work");

        var work = nav.Find("[data-testid='nav-work']");
        var home = nav.Find("[data-testid='nav-home']");

        Assert.Equal("true", work.GetAttribute("aria-expanded"));
        Assert.Equal("false", home.GetAttribute("aria-expanded"));

        // The heading points at the list it folds, and the folded list is still
        // in the document — hidden, not removed.
        var region = nav.Find("#" + home.GetAttribute("aria-controls"));
        Assert.True(region.HasAttribute("hidden"));
        Assert.NotNull(region.QuerySelector("[data-testid='nav-errands']"));

        Assert.False(nav.Find("#" + work.GetAttribute("aria-controls")).HasAttribute("hidden"));
    }

    [Fact]
    public void Clicking_a_heading_asks_the_host_to_toggle_the_group()
    {
        using var context = new BunitContext();
        var toggled = new List<string>();

        var nav = Render(context, parameters => parameters.Add(n => n.OnToggleGroup, (string id) => toggled.Add(id)));

        nav.Find("[data-testid='nav-home']").Click();

        Assert.Equal(["home"], toggled);
    }

    // --- One tab stop and the arrows ---------------------------------------

    [Fact]
    public void The_navigation_is_one_tab_stop_on_the_selected_row()
    {
        using var context = new BunitContext();

        var nav = Render(context, parameters => parameters.Add(n => n.SelectedId, "reading"), "work");

        Assert.Equal("nav-reading", Tabbable(nav));
    }

    [Fact]
    public void With_nothing_selected_the_tab_stop_is_the_first_row()
    {
        using var context = new BunitContext();

        var nav = Render(context);

        Assert.Equal("nav-inbox", Tabbable(nav));
    }

    [Fact]
    public void Down_and_up_walk_the_visible_rows_and_step_over_a_closed_groups_rows()
    {
        using var context = new BunitContext();

        var nav = Render(context, expanded: "home");

        nav.Find("[data-testid='nav-today']").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        Assert.Equal("nav-work", Tabbable(nav));

        // Work is closed, so its rows are not on the walk: the next row is Home.
        nav.Find("[data-testid='nav-work']").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        Assert.Equal("nav-home", Tabbable(nav));

        nav.Find("[data-testid='nav-home']").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        Assert.Equal("nav-errands", Tabbable(nav));

        nav.Find("[data-testid='nav-errands']").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        Assert.Equal("nav-home", Tabbable(nav));
    }

    [Fact]
    public void The_ends_stop_rather_than_wrap_and_home_and_end_jump_to_them()
    {
        using var context = new BunitContext();

        var nav = Render(context);

        nav.Find("[data-testid='nav-inbox']").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        Assert.Equal("nav-inbox", Tabbable(nav));

        nav.Find("[data-testid='nav-inbox']").KeyDown(new KeyboardEventArgs { Key = "End" });
        Assert.Equal("nav-someday", Tabbable(nav));

        nav.Find("[data-testid='nav-someday']").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        Assert.Equal("nav-someday", Tabbable(nav));

        nav.Find("[data-testid='nav-someday']").KeyDown(new KeyboardEventArgs { Key = "Home" });
        Assert.Equal("nav-inbox", Tabbable(nav));
    }

    [Fact]
    public void Right_opens_a_closed_group_and_left_closes_an_open_one()
    {
        using var context = new BunitContext();
        var toggled = new List<string>();

        var nav = Render(context, parameters => parameters.Add(n => n.OnToggleGroup, (string id) => toggled.Add(id)), "work");

        nav.Find("[data-testid='nav-home']").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        nav.Find("[data-testid='nav-work']").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });

        // And the keys that would do nothing, do nothing: Left on a closed
        // group, Right on an open one with rows steps in instead of toggling.
        nav.Find("[data-testid='nav-home']").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });

        Assert.Equal(["home", "work"], toggled);
    }

    [Fact]
    public void Right_on_an_open_heading_steps_onto_its_first_row_and_left_on_a_row_steps_back_out()
    {
        using var context = new BunitContext();

        var nav = Render(context, expanded: "work");

        nav.Find("[data-testid='nav-work']").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        Assert.Equal("nav-backlog", Tabbable(nav));

        nav.Find("[data-testid='nav-reading']").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        Assert.Equal("nav-work", Tabbable(nav));
    }

    [Fact]
    public void Left_on_a_row_outside_any_group_goes_nowhere()
    {
        using var context = new BunitContext();

        var nav = Render(context, parameters => parameters.Add(n => n.SelectedId, "someday"));

        nav.Find("[data-testid='nav-someday']").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });

        Assert.Equal("nav-someday", Tabbable(nav));
    }

    [Fact]
    public void F2_asks_the_host_to_rename_the_row_and_opens_no_field_on_its_own()
    {
        using var context = new BunitContext();
        var requested = new List<string>();

        var nav = Render(context, parameters => parameters.Add(n => n.OnRenameRequested, (string id) => requested.Add(id)));

        nav.Find("[data-testid='nav-work']").KeyDown(new KeyboardEventArgs { Key = "F2" });

        Assert.Equal(["work"], requested);
        Assert.Empty(nav.FindAll("input"));
    }

    // --- Context menu ------------------------------------------------------

    [Fact]
    public void A_right_click_on_a_row_names_the_entry_and_where_the_pointer_was()
    {
        using var context = new BunitContext();
        var asked = new List<(ListNavTarget Target, double X, double Y)>();

        var nav = Render(context, parameters => parameters
            .Add(n => n.OnContextMenu, ((ListNavTarget Target, double X, double Y) request) => asked.Add(request)));

        nav.Find("[data-testid='nav-inbox']").ContextMenu(new MouseEventArgs { ClientX = 40, ClientY = 90 });

        // One report, not two: the row stops the event before the root hears it.
        var request = Assert.Single(asked);
        Assert.Equal(ListNavTarget.Entry("inbox"), request.Target);
        Assert.Equal(40, request.X);
        Assert.Equal(90, request.Y);
    }

    [Fact]
    public void A_right_click_on_a_heading_names_the_group()
    {
        using var context = new BunitContext();
        var asked = new List<(ListNavTarget Target, double X, double Y)>();

        var nav = Render(context, parameters => parameters
            .Add(n => n.OnContextMenu, ((ListNavTarget Target, double X, double Y) request) => asked.Add(request)));

        nav.Find("[data-testid='nav-home']").ContextMenu(new MouseEventArgs { ClientX = 1, ClientY = 2 });

        Assert.Equal(ListNavTarget.Group("home"), Assert.Single(asked).Target);
    }

    [Fact]
    public void A_right_click_on_the_empty_area_names_the_root()
    {
        using var context = new BunitContext();
        var asked = new List<(ListNavTarget Target, double X, double Y)>();

        var nav = Render(context, parameters => parameters
            .Add(n => n.OnContextMenu, ((ListNavTarget Target, double X, double Y) request) => asked.Add(request)));

        nav.Find("nav").ContextMenu(new MouseEventArgs { ClientX = 5, ClientY = 300 });

        var request = Assert.Single(asked);
        Assert.Equal(ListNavTarget.Root, request.Target);
        Assert.Null(request.Target.Id);
        Assert.Equal(300, request.Y);
    }

    [Fact]
    public void An_empty_navigation_still_offers_its_root_menu()
    {
        // The empty state's one action — a first list — lives on that menu, so a
        // navigation with nothing in it has to be able to ask for it.
        using var context = new BunitContext();
        ListNavTarget? target = null;

        var nav = context.Render<ListNav>(parameters => parameters
            .Add(n => n.OnContextMenu, ((ListNavTarget Target, double X, double Y) request) => target = request.Target));

        nav.Find("nav").ContextMenu(new MouseEventArgs());

        Assert.Equal(ListNavTarget.Root, target);
    }

    // --- Rename in place ---------------------------------------------------

    [Fact]
    public void The_row_being_edited_is_a_field_holding_its_name_with_the_count_still_beside_it()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var nav = Render(context, parameters => parameters.Add(n => n.EditingId, "reading"), "work");

        var field = nav.Find("[data-testid='nav-rename']");
        Assert.Equal("Reading", field.GetAttribute("value"));
        Assert.Equal("Rename Reading", field.GetAttribute("aria-label"));
        Assert.Contains("list-nav__rename", field.ClassList);
        Assert.Contains("field__input--compact", field.ClassList);

        // The button is gone — an input inside a button is not markup — and the
        // count is not: it is a fact about the list, not about its name.
        Assert.Empty(nav.FindAll("[data-testid='nav-reading']"));
        Assert.Equal("7", nav.Find("[data-testid='nav-reading-count']").TextContent);

        // The field is focused with its text selected, which is what renaming
        // means everywhere else in the library.
        var focus = context.JSInterop.Invocations.Single(invocation => invocation.Identifier == "backlogFocus");
        Assert.Equal(true, focus.Arguments[1]);
    }

    [Fact]
    public void A_group_heading_renames_the_same_way()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var nav = Render(context, parameters => parameters.Add(n => n.EditingId, "work"));

        Assert.Equal("Work", nav.Find("[data-testid='nav-rename']").GetAttribute("value"));
        Assert.Empty(nav.FindAll("[data-testid='nav-work']"));
        Assert.Empty(nav.FindAll("[data-testid='nav-work-count']"));
    }

    [Fact]
    public void Enter_commits_the_trimmed_name()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var committed = new List<(string Id, string Name)>();
        var cancelled = 0;

        var nav = Render(context, parameters => parameters
            .Add(n => n.EditingId, "inbox")
            .Add(n => n.OnRenameCommitted, ((string Id, string Name) rename) => committed.Add(rename))
            .Add(n => n.OnRenameCancelled, () => cancelled++));

        var field = nav.Find("[data-testid='nav-rename']");
        field.Input("  Captured  ");
        field.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal([("inbox", "Captured")], committed);
        Assert.Equal(0, cancelled);
    }

    [Fact]
    public void Leaving_the_field_commits_too_but_not_a_second_time_after_enter()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var committed = new List<(string Id, string Name)>();

        var nav = Render(context, parameters => parameters
            .Add(n => n.EditingId, "inbox")
            .Add(n => n.OnRenameCommitted, ((string Id, string Name) rename) => committed.Add(rename)));

        var field = nav.Find("[data-testid='nav-rename']");
        field.Input("Captured");
        field.Blur();

        Assert.Equal([("inbox", "Captured")], committed);

        // The browser may send a blur as a host takes the field away after
        // Enter; that blur must not commit the same rename again.
        var again = Render(context, parameters => parameters
            .Add(n => n.EditingId, "inbox")
            .Add(n => n.OnRenameCommitted, ((string Id, string Name) rename) => committed.Add(rename)));

        var second = again.Find("[data-testid='nav-rename']");
        second.Input("Twice");
        second.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        second.Blur();

        Assert.Equal([("inbox", "Captured"), ("inbox", "Twice")], committed);
    }

    [Fact]
    public void Escape_cancels_and_the_blur_that_follows_commits_nothing()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var committed = new List<(string Id, string Name)>();
        var cancelled = 0;

        var nav = Render(context, parameters => parameters
            .Add(n => n.EditingId, "inbox")
            .Add(n => n.OnRenameCommitted, ((string Id, string Name) rename) => committed.Add(rename))
            .Add(n => n.OnRenameCancelled, () => cancelled++));

        var field = nav.Find("[data-testid='nav-rename']");
        field.Input("Thrown away");
        field.KeyDown(new KeyboardEventArgs { Key = "Escape" });
        field.Blur();

        Assert.Empty(committed);
        Assert.Equal(1, cancelled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Inbox")]
    [InlineData("  Inbox ")]
    public void A_name_that_is_empty_or_unchanged_is_a_cancel_not_a_rename(string typed)
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var committed = new List<(string Id, string Name)>();
        var cancelled = 0;

        var nav = Render(context, parameters => parameters
            .Add(n => n.EditingId, "inbox")
            .Add(n => n.OnRenameCommitted, ((string Id, string Name) rename) => committed.Add(rename))
            .Add(n => n.OnRenameCancelled, () => cancelled++));

        var field = nav.Find("[data-testid='nav-rename']");
        field.Input(typed);
        field.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Empty(committed);
        Assert.Equal(1, cancelled);
    }

    [Fact]
    public void Closing_the_field_puts_the_row_back_and_the_tab_stop_on_it()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var nav = Render(context, parameters => parameters.Add(n => n.EditingId, "someday"));

        nav.Render(parameters => parameters.Add(n => n.EditingId, null));

        Assert.Empty(nav.FindAll("input"));
        Assert.NotNull(nav.Find("[data-testid='nav-someday']"));
        Assert.Equal("nav-someday", Tabbable(nav));
    }

    /// <summary>The element references the focus moves need are kept per row
    /// id, and a row a host deletes would otherwise leave its reference behind
    /// for the life of the component — a leak, and a stale handle a focus
    /// request could still find.</summary>
    [Fact]
    public void A_deleted_rows_element_reference_is_let_go()
    {
        using var context = new BunitContext();

        var nav = Render(context, expanded: ["work", "home"]);
        Assert.Equal(8, nav.Instance.TrackedRowCount);

        nav.Render(parameters => parameters
            .Add(n => n.Groups, [new ListNavGroup("work", "Work", [new("backlog", "Backlog", 4)])])
            .Add(n => n.Ungrouped, []));

        Assert.Equal(4, nav.Instance.TrackedRowCount);
        Assert.Equal(
            ["nav-inbox", "nav-today", "nav-work", "nav-backlog"],
            nav.FindAll("button").Select(button => button.GetAttribute("data-testid")).ToArray());
    }

    [Fact]
    public void An_editing_id_that_names_no_row_opens_no_field()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var nav = Render(context, parameters => parameters.Add(n => n.EditingId, "gone"));

        Assert.Empty(nav.FindAll("input"));

        // Every row is still a button: two fixed, two headings, the three rows
        // the closed groups keep hidden, and the loose one.
        Assert.Equal(8, nav.FindAll("button").Count);
    }

    // --- Class hooks -------------------------------------------------------

    [Fact]
    public void A_host_can_dress_every_part_in_its_own_names()
    {
        using var context = new BunitContext();

        var nav = Render(context, parameters => parameters
            .Add(n => n.BaseClass, "inbox-nav")
            .Add(n => n.CssClass, "inbox-nav--narrow")
            .Add(n => n.SelectedId, "inbox")
            .Add(n => n.ListCssClass, "inbox-nav__rows")
            .Add(n => n.ItemCssClass, "inbox-nav__slot")
            .Add(n => n.RowCssClass, "inbox-nav__entry")
            .Add(n => n.SelectedRowCssClass, "inbox-nav__entry inbox-nav__entry--on")
            .Add(n => n.GroupCssClass, "inbox-nav__fold")
            .Add(n => n.GroupHeaderCssClass, "inbox-nav__heading")
            .Add(n => n.IconCssClass, "inbox-nav__glyph")
            .Add(n => n.LabelCssClass, "inbox-nav__text")
            .Add(n => n.CountCssClass, "inbox-nav__n"));

        Assert.Equal("inbox-nav inbox-nav--narrow", nav.Find("nav").GetAttribute("class"));
        Assert.Equal("inbox-nav__rows", nav.Find("ul").GetAttribute("class"));
        Assert.Equal("inbox-nav__slot", nav.Find("li").GetAttribute("class"));
        Assert.Equal("inbox-nav__entry inbox-nav__entry--on", nav.Find("[data-testid='nav-inbox']").GetAttribute("class"));
        Assert.Equal("inbox-nav__entry", nav.Find("[data-testid='nav-today']").GetAttribute("class"));
        Assert.Equal("inbox-nav__fold", nav.Find("[data-testid='nav-work-group']").GetAttribute("class"));
        Assert.Equal("inbox-nav__heading", nav.Find("[data-testid='nav-work']").GetAttribute("class"));
        Assert.Equal("inbox-nav__glyph", nav.Find("[data-testid='nav-inbox'] span:first-child").GetAttribute("class"));
        Assert.Equal("inbox-nav__text", nav.Find("[data-testid='nav-today'] span:first-child").GetAttribute("class"));
        Assert.Equal("inbox-nav__n", nav.Find("[data-testid='nav-inbox-count']").GetAttribute("class"));
        Assert.Equal("inbox-nav__n inbox-nav__n--zero", nav.Find("[data-testid='nav-today-count']").GetAttribute("class"));

        Assert.Empty(nav.FindAll("[class*='list-nav']"));
    }

    [Fact]
    public void With_only_the_base_class_replaced_every_inner_class_follows_it()
    {
        using var context = new BunitContext();

        var nav = Render(context, parameters => parameters.Add(n => n.BaseClass, "side").Add(n => n.SelectedId, "inbox"), "work");

        Assert.Equal("side", nav.Find("nav").GetAttribute("class"));
        Assert.Equal("side__row side__row--selected", nav.Find("[data-testid='nav-inbox']").GetAttribute("class"));
        Assert.Equal("side__row side__row--group", nav.Find("[data-testid='nav-work']").GetAttribute("class"));
        Assert.Equal("side__count side__count--zero", nav.Find("[data-testid='nav-today-count']").GetAttribute("class"));
        Assert.Contains("side__chevron--open", nav.Find("[data-testid='nav-work'] span:first-child").ClassList);
    }
}
