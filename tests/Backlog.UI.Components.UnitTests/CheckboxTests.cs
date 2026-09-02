namespace Backlog.UI.Components.UnitTests;

public sealed class CheckboxTests
{
    [Fact]
    public void Indeterminate_reads_as_mixed_rather_than_checked()
    {
        // `indeterminate` is a DOM property with no attribute, so aria-checked is
        // the only place the third state can be seen from the markup.
        using var context = new BunitContext();

        var checkbox = context.Render<Checkbox>(parameters => parameters
            .Add(c => c.Indeterminate, true)
            .Add(c => c.Checked, true));

        Assert.Equal("mixed", checkbox.Find("input").GetAttribute("aria-checked"));
        Assert.Contains("checkbox--mixed", checkbox.Find("label").ClassList);
    }

    [Fact]
    public void A_settled_checkbox_reads_true_or_false()
    {
        using var context = new BunitContext();

        var off = context.Render<Checkbox>();
        var on = context.Render<Checkbox>(parameters => parameters.Add(c => c.Checked, true));

        Assert.Equal("false", off.Find("input").GetAttribute("aria-checked"));
        Assert.Equal("true", on.Find("input").GetAttribute("aria-checked"));
    }

    [Fact]
    public void Changing_the_box_reports_the_new_state()
    {
        using var context = new BunitContext();
        bool? reported = null;

        var checkbox = context.Render<Checkbox>(parameters => parameters
            .Add(c => c.CheckedChanged, (bool value) => reported = value));

        checkbox.Find("input").Change(true);

        Assert.True(reported);
    }

    /// <summary>
    /// The mixed state reaches the box, not just the label.
    /// <para>
    /// Asserted through the selector the stylesheet actually uses, because that
    /// is the thing that was broken: the class was on the root and the only rule
    /// keyed off it styled <c>.checkbox__label</c>, which a box with no visible
    /// label does not render. Everything about the third state was correct except
    /// that nobody could see it. A test that asks only for
    /// <c>aria-checked="mixed"</c> passes in exactly that situation, which is why
    /// this one asks whether the paint has somewhere to land.
    /// </para>
    /// </summary>
    [Fact]
    public void The_mixed_state_is_drawn_on_the_box_rather_than_on_a_label()
    {
        using var context = new BunitContext();

        var checkbox = context.Render<Checkbox>(parameters => parameters
            .Add(c => c.Indeterminate, true));

        Assert.Single(checkbox.FindAll(".checkbox--mixed .checkbox__input"));
    }

    /// <summary>A box with no label at all still shows the third state. This is
    /// the shape SelectionBar's select-all and a task row's gutter are in — named
    /// by aria-label, with the label span hidden — and it is the shape the old
    /// italics could say nothing about.</summary>
    [Fact]
    public void A_box_with_no_visible_label_still_shows_the_mixed_state()
    {
        using var context = new BunitContext();

        var checkbox = context.Render<Checkbox>(parameters => parameters
            .Add(c => c.Indeterminate, true));

        Assert.Equal(string.Empty, checkbox.Find(".checkbox__label").TextContent.Trim());
        Assert.Single(checkbox.FindAll(".checkbox--mixed .checkbox__input"));
    }

    /// <summary>A settled box is the native control it has always been. The
    /// mixed rule takes the platform's appearance away to draw a dash the
    /// platform cannot draw, so it has to stay scoped to the one state that needs
    /// it — a checked box picking up our paint would be a redesign of every
    /// checkbox in the product smuggled in behind a bug fix.</summary>
    [Fact]
    public void A_settled_box_is_not_dressed_by_the_mixed_rule()
    {
        using var context = new BunitContext();

        var on = context.Render<Checkbox>(parameters => parameters.Add(c => c.Checked, true));
        var off = context.Render<Checkbox>();

        Assert.Empty(on.FindAll(".checkbox--mixed .checkbox__input"));
        Assert.Empty(off.FindAll(".checkbox--mixed .checkbox__input"));
    }
}
