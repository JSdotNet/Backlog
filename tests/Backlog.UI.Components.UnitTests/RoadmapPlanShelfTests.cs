using Backlog.UI.Components.Roadmap;

using Bunit;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The shelf on its own: what a row says, that nothing renders when there is
/// nothing to offer, and that a press reports the row and nothing else.
/// </summary>
public sealed class RoadmapPlanShelfTests
{
    private static readonly RoadmapShelfPlan Release = new("release-q4", ["backlog", "web"], 6, 21, 2);

    private static readonly RoadmapShelfPlan Single = new("one", [], 1, 1, 0);

    [Fact]
    public void A_row_says_how_much_work_how_big_and_where_with_the_unestimated_apart()
    {
        Assert.Equal("6 tasks · 21 points · 2 unestimated · backlog, web", Release.Summary);
        Assert.Equal("1 task · 1 point", Single.Summary);
    }

    [Fact]
    public void An_empty_shelf_renders_nothing()
    {
        using var context = new BunitContext();

        var shelf = context.Render<RoadmapPlanShelf>(parameters => parameters.Add(p => p.Plans, []));

        Assert.Empty(shelf.Nodes);
    }

    [Fact]
    public void Pressing_a_rows_action_reports_that_row()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        RoadmapShelfPlan? pressed = null;

        var shelf = context.Render<RoadmapPlanShelf>(parameters => parameters
            .Add(p => p.Plans, [Release, Single])
            .Add(p => p.OnPlan, plan => pressed = plan));

        shelf.Find("[data-testid=\"roadmap-shelf-plan-one\"]").Click();

        Assert.Equal(Single, pressed);
    }

    [Fact]
    public void While_one_row_is_being_planned_every_action_waits()
    {
        using var context = new BunitContext();

        var shelf = context.Render<RoadmapPlanShelf>(parameters => parameters
            .Add(p => p.Plans, [Release, Single])
            .Add(p => p.Busy, "release-q4"));

        Assert.All(shelf.FindAll("button"), button => Assert.True(button.HasAttribute("disabled")));
        Assert.Equal("true", shelf.Find("[data-testid=\"roadmap-shelf-plan-release-q4\"]").GetAttribute("aria-busy"));
    }
}
