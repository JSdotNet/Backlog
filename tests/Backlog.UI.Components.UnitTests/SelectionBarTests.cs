namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The bar a surface puts up once a reader has picked more than one of
/// something. Every assertion here is about the chrome —
/// <c>.design/interaction-guidelines.md#focus-and-selection</c> asks for a live
/// count, a clear-selection control and an indeterminate select-all, and this is
/// what says the bar has all three.
/// <para>
/// Nothing here is about tasks. The bar knows a number and a total and holds
/// whatever the host puts in its slot, which is what lets one implementation
/// serve every list in the product rather than one per list.
/// </para>
/// </summary>
public sealed class SelectionBarTests
{
    private static IRenderedComponent<SelectionBar> Render(
        BunitContext context,
        Action<ComponentParameterCollectionBuilder<SelectionBar>> parameters) =>
        context.Render<SelectionBar>(builder =>
        {
            builder.Add(bar => bar.TestId, "bulk");
            parameters(builder);
        });

    [Fact]
    public void One_item_reads_in_the_singular()
    {
        // "1 items selected" is the shape of a count nobody proof-read, and the
        // count is the one thing on this bar a reader is reading.
        using var context = new BunitContext();

        var bar = Render(context, p => p
            .Add(b => b.Count, 1)
            .Add(b => b.Total, 4));

        Assert.Equal("1 item selected", bar.Find("[data-testid='bulk-count']").TextContent.Trim());
    }

    [Fact]
    public void More_than_one_reads_in_the_plural()
    {
        using var context = new BunitContext();

        var bar = Render(context, p => p
            .Add(b => b.Count, 3)
            .Add(b => b.Total, 4));

        Assert.Equal("3 items selected", bar.Find("[data-testid='bulk-count']").TextContent.Trim());
    }

    [Fact]
    public void The_noun_is_the_hosts_to_choose()
    {
        // The library has no vocabulary of its own: a list of entries says
        // "tasks" and a list of files says "files".
        using var context = new BunitContext();

        var bar = Render(context, p => p
            .Add(b => b.Count, 2)
            .Add(b => b.Total, 2)
            .Add(b => b.ItemNoun, "task"));

        Assert.Equal("2 tasks selected", bar.Find("[data-testid='bulk-count']").TextContent.Trim());
    }

    [Fact]
    public void The_count_sits_in_a_live_region()
    {
        // A count that changes silently is a count only somebody watching it
        // can read. It is in the DOM from the first render for the same reason
        // the task list's announcement is: a live region has to exist before it
        // changes or the first change is the one nobody hears.
        using var context = new BunitContext();

        var count = Render(context, p => p
            .Add(b => b.Count, 2)
            .Add(b => b.Total, 5))
            .Find("[data-testid='bulk-count']");

        Assert.Equal("status", count.GetAttribute("role"));
        Assert.Equal("polite", count.GetAttribute("aria-live"));
    }

    [Fact]
    public void A_partial_selection_shows_the_select_all_box_as_mixed()
    {
        using var context = new BunitContext();

        var bar = Render(context, p => p
            .Add(b => b.Count, 2)
            .Add(b => b.Total, 5));

        Assert.Equal("mixed", bar.Find("[data-testid='bulk-select-all'] input").GetAttribute("aria-checked"));

        // And visibly, not only to a screen reader. The bar's select-all carries
        // no visible label, so the third state has to land on the box itself —
        // asserted through the selector the stylesheet uses, because a bar whose
        // partial state looked plainly unchecked is what
        // `.design/interaction-guidelines.md#focus-and-selection` forbids and what
        // an aria-only assertion cannot see.
        Assert.Single(bar.FindAll(".checkbox--mixed .checkbox__input"));
    }

    [Fact]
    public void A_whole_selection_shows_the_select_all_box_as_checked()
    {
        using var context = new BunitContext();

        var bar = Render(context, p => p
            .Add(b => b.Count, 5)
            .Add(b => b.Total, 5));

        Assert.Equal("true", bar.Find("[data-testid='bulk-select-all'] input").GetAttribute("aria-checked"));

        // Checked, and so not wearing the mixed paint. "All of them" and "some of
        // them" have to look different or the box is decoration.
        Assert.Empty(bar.FindAll(".checkbox--mixed .checkbox__input"));
    }

    [Fact]
    public void The_select_all_box_reports_which_way_it_was_pressed()
    {
        using var context = new BunitContext();
        bool? reported = null;

        var bar = Render(context, p => p
            .Add(b => b.Count, 5)
            .Add(b => b.Total, 5)
            .Add(b => b.SelectAllChanged, (bool value) => reported = value));

        bar.Find("[data-testid='bulk-select-all'] input").Change(false);

        Assert.False(reported);
    }

    [Fact]
    public void The_clear_control_says_what_it_is_and_reports_being_pressed()
    {
        using var context = new BunitContext();
        var cleared = 0;

        var bar = Render(context, p => p
            .Add(b => b.Count, 3)
            .Add(b => b.Total, 5)
            .Add(b => b.OnClear, () => cleared++));

        var clear = bar.Find("[data-testid='bulk-clear']");
        Assert.Equal("Clear selection", clear.TextContent.Trim());

        clear.Click();

        Assert.Equal(1, cleared);
    }

    [Fact]
    public void What_the_host_can_do_to_the_selection_is_the_hosts_own()
    {
        // A slot rather than a set of parameters: what a reader does to twenty
        // selected things is not a list this library can finish.
        using var context = new BunitContext();

        var bar = Render(context, p => p
            .Add(b => b.Count, 2)
            .Add(b => b.Total, 2)
            .Add(b => b.Actions, builder => builder.AddMarkupContent(0, "<span data-testid='mine'>Retag</span>")));

        Assert.NotNull(bar.Find("[data-testid='mine']"));
    }

    [Fact]
    public void An_unfilled_actions_slot_draws_no_group_at_all()
    {
        using var context = new BunitContext();

        var bar = Render(context, p => p
            .Add(b => b.Count, 1)
            .Add(b => b.Total, 2));

        Assert.Empty(bar.FindAll("[data-testid='bulk-actions']"));
    }

    [Fact]
    public void The_bar_names_itself_to_a_screen_reader()
    {
        using var context = new BunitContext();

        var bar = Render(context, p => p
            .Add(b => b.Count, 1)
            .Add(b => b.Total, 2)
            .Add(b => b.AriaLabel, "Bulk edit selected tasks"));

        var root = bar.Find("[data-testid='bulk']");

        Assert.Equal("group", root.GetAttribute("role"));
        Assert.Equal("Bulk edit selected tasks", root.GetAttribute("aria-label"));
    }

    [Fact]
    public void The_class_hooks_are_the_ones_every_other_component_offers()
    {
        using var context = new BunitContext();

        var appended = Render(context, p => p
            .Add(b => b.Count, 1)
            .Add(b => b.Total, 2)
            .Add(b => b.CssClass, "backlog-bulk-bar"));

        Assert.Contains("selection-bar", appended.Find("[data-testid='bulk']").ClassList);
        Assert.Contains("backlog-bulk-bar", appended.Find("[data-testid='bulk']").ClassList);

        var replaced = Render(context, p => p
            .Add(b => b.Count, 1)
            .Add(b => b.Total, 2)
            .Add(b => b.BaseClass, "pane-bulk"));

        Assert.DoesNotContain("selection-bar", replaced.Find("[data-testid='bulk']").ClassList);
        Assert.Contains("pane-bulk", replaced.Find("[data-testid='bulk']").ClassList);
    }
}
