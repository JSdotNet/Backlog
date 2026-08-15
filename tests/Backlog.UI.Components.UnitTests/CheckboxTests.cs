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
}
