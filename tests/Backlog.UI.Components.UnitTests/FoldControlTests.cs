namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The disclosure's heading hook. A fold that is a section of a page wants its
/// trigger inside a heading — the accordion shape — and a fold that is a detail
/// inside one wants the bare button. Both are the same control; what is pinned
/// here is that the parameter is the only difference between them.
/// </summary>
public sealed class FoldControlTests
{
    [Fact]
    public void Without_a_heading_level_the_trigger_is_the_wrappers_first_child()
    {
        using var context = new BunitContext();

        var fold = context.Render<FoldControl>(parameters => parameters
            .Add(f => f.Label, "Advanced")
            .AddChildContent("<p>body</p>"));

        var wrapper = fold.Find(".fold");

        Assert.Equal("BUTTON", wrapper.Children[0].TagName);
        Assert.Empty(fold.FindAll("h1, h2, h3, h4, h5, h6"));
    }

    [Fact]
    public void A_heading_level_wraps_the_trigger_in_that_heading()
    {
        using var context = new BunitContext();

        var fold = context.Render<FoldControl>(parameters => parameters
            .Add(f => f.Label, "Productivity")
            .Add(f => f.HeadingLevel, 3)
            .AddChildContent("<p>body</p>"));

        var heading = fold.Find("h3");

        Assert.Equal("fold__heading", heading.GetAttribute("class"));
        Assert.Equal("BUTTON", heading.Children[0].TagName);
        Assert.Equal("fold__trigger", heading.Children[0].GetAttribute("class"));
        Assert.Equal("Productivity", heading.TextContent.Trim().TrimStart('▸').Trim());
    }

    [Fact]
    public void The_heading_wears_the_class_the_host_hands_over()
    {
        using var context = new BunitContext();

        var fold = context.Render<FoldControl>(parameters => parameters
            .Add(f => f.Label, "Cost")
            .Add(f => f.HeadingLevel, 2)
            .Add(f => f.HeadingCssClass, "dashboard-section__title"));

        Assert.Equal("dashboard-section__title", fold.Find("h2").GetAttribute("class"));
    }

    /// <summary>The heading changes where the trigger sits, not what it does: it
    /// still names the region and still folds it.</summary>
    [Fact]
    public void A_trigger_inside_a_heading_still_folds_the_region()
    {
        using var context = new BunitContext();
        var expanded = true;

        var fold = context.Render<FoldControl>(parameters => parameters
            .Add(f => f.Label, "Sessions")
            .Add(f => f.HeadingLevel, 3)
            .Add(f => f.Expanded, expanded)
            .Add(f => f.ExpandedChanged, value => expanded = value)
            .AddChildContent("<p>body</p>"));

        var trigger = fold.Find("h3 > button");
        var region = fold.Find(".fold__region");

        Assert.Equal(region.Id, trigger.GetAttribute("aria-controls"));
        Assert.Equal("true", trigger.GetAttribute("aria-expanded"));

        trigger.Click();

        Assert.False(expanded);
    }

    [Fact]
    public void A_level_outside_one_to_six_is_clamped_rather_than_emitted()
    {
        using var context = new BunitContext();

        var fold = context.Render<FoldControl>(parameters => parameters
            .Add(f => f.Label, "Deep")
            .Add(f => f.HeadingLevel, 9));

        Assert.NotNull(fold.Find("h6"));
    }

    [Theory]
    [InlineData(false, "fold__chevron", "fold__label")]
    [InlineData(true, "fold__label", "fold__chevron")]
    public void The_chevron_leads_the_label_unless_it_is_asked_to_end_the_row(bool atEnd, string first, string last)
    {
        using var context = new BunitContext();

        var fold = context.Render<FoldControl>(parameters => parameters
            .Add(f => f.Label, "1 resolved")
            .Add(f => f.ChevronAtEnd, atEnd)
            .AddChildContent("<p>body</p>"));

        var parts = fold.Find(".fold__trigger").Children;
        Assert.Equal(2, parts.Length);
        Assert.Equal(first, parts[0].ClassList[0]);
        Assert.Equal(last, parts[1].ClassList[0]);
        Assert.Equal("true", fold.Find(".fold__chevron").GetAttribute("aria-hidden"));
        Assert.Equal(atEnd, fold.Find(".fold__chevron").ClassList.Contains("fold__chevron--end"));
        Assert.Equal(atEnd ? 1 : 0, fold.FindAll(".fold__chevron svg").Count);
    }
}
