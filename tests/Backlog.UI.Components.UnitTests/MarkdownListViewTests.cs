namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The list as its own component. <c>MarkdownNestingTests</c> and
/// <c>MarkdownToggleTests</c> already hold what a list means through the read
/// view; what is new here is that it can be reached and dressed on its own — and
/// that a nested run is dressed the same as the run it sits in, which is the one
/// thing a recursive renderer can get wrong without anybody noticing.
/// </summary>
public sealed class MarkdownListViewTests
{
    private static MdList Nested =>
        new(false,
        [
            new MdListItem(null, MarkdownPreview.ParseInlines("A bullet"), null,
            [
                new MdList(false, [new MdListItem(null, MarkdownPreview.ParseInlines("Nested under it"), null)])
            ])
        ]);

    [Fact]
    public void A_nested_run_wears_what_the_run_it_sits_in_wears()
    {
        using var context = new BunitContext();

        var list = context.Render<MarkdownListView>(parameters => parameters
            .Add(view => view.List, Nested)
            .Add(view => view.ListCssClass, "knowledge-list")
            .Add(view => view.ItemCssClass, null));

        var lists = list.FindAll("ul");

        Assert.Equal(2, lists.Count);
        Assert.All(lists, element => Assert.Equal("knowledge-list", element.GetAttribute("class")));
        Assert.All(list.FindAll("li"), element => Assert.Null(element.GetAttribute("class")));
    }

    [Fact]
    public void A_checklist_item_is_a_control_only_where_somebody_is_listening()
    {
        using var context = new BunitContext();

        var list = new MdList(false, [new MdListItem(false, MarkdownPreview.ParseInlines("An unfinished one"), 3)]);

        var state = context.Render<MarkdownListView>(parameters => parameters
            .Add(view => view.List, list));

        // An img with a label rather than a button, so Tab walks past it instead
        // of stopping on a checkbox that would do nothing.
        Assert.Equal("img", state.Find("[data-testid='entry-checkbox']").GetAttribute("role"));
        Assert.Empty(state.FindAll("button"));

        var toggled = -1;

        var control = context.Render<MarkdownListView>(parameters => parameters
            .Add(view => view.List, list)
            .Add(view => view.OnTaskItemToggled, (int index) => toggled = index));

        control.Find("button[role='checkbox']").Click();

        // The task index of the line, not the position in this list: the parser
        // numbers task lines in the order they were written, which is the order
        // the rewriter walks them in.
        Assert.Equal(3, toggled);
    }

    [Fact]
    public void A_nested_checkbox_still_reports_its_own_line()
    {
        using var context = new BunitContext();

        var toggled = -1;

        var list = context.Render<MarkdownListView>(parameters => parameters
            .Add(view => view.List, new MdList(false,
            [
                new MdListItem(null, MarkdownPreview.ParseInlines("A bullet"), null,
                [
                    new MdList(false, [new MdListItem(false, MarkdownPreview.ParseInlines("Nested task"), 7)])
                ])
            ]))
            .Add(view => view.OnTaskItemToggled, (int index) => toggled = index));

        list.Find("button[role='checkbox']").Click();

        Assert.Equal(7, toggled);
    }

    [Fact]
    public void The_check_keeps_one_class_whether_it_is_a_control_or_a_state()
    {
        using var context = new BunitContext();

        var done = new MdList(false, [new MdListItem(true, MarkdownPreview.ParseInlines("Finished"), 0)]);

        var state = context.Render<MarkdownListView>(parameters => parameters
            .Add(view => view.List, done));

        var control = context.Render<MarkdownListView>(parameters => parameters
            .Add(view => view.List, done)
            .Add(view => view.OnTaskItemToggled, (int _) => { }));

        // The two are the same box to a reader and must stay the same box to a
        // stylesheet, however differently a screen reader is told to treat them.
        Assert.Equal(
            state.Find("[data-testid='entry-checkbox']").GetAttribute("class"),
            control.Find("[data-testid='entry-checkbox']").GetAttribute("class"));
    }

    [Fact]
    public void An_ordered_item_never_carries_a_class_at_all()
    {
        using var context = new BunitContext();

        var list = context.Render<MarkdownListView>(parameters => parameters
            .Add(view => view.List, new MdList(true, [new MdListItem(null, MarkdownPreview.ParseInlines("First"), null)])));

        // The number is the marker and there is no task state to say anything
        // about, so there has never been anything for a class to carry.
        Assert.Null(list.Find("li").GetAttribute("class"));
    }
}
