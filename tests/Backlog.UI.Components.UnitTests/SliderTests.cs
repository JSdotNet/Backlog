namespace Backlog.UI.Components.UnitTests;

public sealed class SliderTests
{
    [Fact]
    public void It_is_a_native_range_input_with_its_bounds_and_its_name()
    {
        using var context = new BunitContext();

        var slider = context.Render<Slider>(parameters => parameters
            .Add(c => c.Min, 1.5m)
            .Add(c => c.Max, 9m)
            .Add(c => c.Step, 0.25m)
            .Add(c => c.Value, 4m)
            .Add(c => c.AriaLabel, "Pace in backlog")
            .Add(c => c.ValueText, "4 points a week")
            .Add(c => c.TestId, "pace-slider"));

        var input = slider.Find("input[data-testid='pace-slider']");

        Assert.Equal("range", input.GetAttribute("type"));
        Assert.Equal("1.5", input.GetAttribute("min"));
        Assert.Equal("9", input.GetAttribute("max"));
        Assert.Equal("0.25", input.GetAttribute("step"));
        Assert.Equal("4", input.GetAttribute("value"));
        Assert.Equal("Pace in backlog", input.GetAttribute("aria-label"));
        Assert.Equal("4 points a week", input.GetAttribute("aria-valuetext"));
        Assert.Contains("slider", input.ClassList);
    }

    [Fact]
    public void Letting_go_commits_the_value_once()
    {
        using var context = new BunitContext();
        var committed = new List<decimal>();

        var slider = context.Render<Slider>(parameters => parameters
            .Add(c => c.Min, 0m)
            .Add(c => c.Max, 10m)
            .Add(c => c.Step, 0.25m)
            .Add(c => c.Value, 2m)
            .Add(c => c.AriaLabel, "Pace")
            .Add(c => c.ValueChanged, (decimal value) => committed.Add(value)));

        slider.Find("input").Change("6.25");

        Assert.Equal([6.25m], committed);
    }

    [Fact]
    public void Dragging_reports_each_movement_as_live_input_and_no_commit()
    {
        using var context = new BunitContext();
        var live = new List<decimal>();
        var committed = new List<decimal>();

        var slider = context.Render<Slider>(parameters => parameters
            .Add(c => c.Max, 10m)
            .Add(c => c.AriaLabel, "Pace")
            .Add(c => c.ValueInput, (decimal value) => live.Add(value))
            .Add(c => c.ValueChanged, (decimal value) => committed.Add(value)));

        slider.Find("input").Input("3");
        slider.Find("input").Input("4");

        Assert.Equal([3m, 4m], live);
        Assert.Empty(committed);
    }

    [Fact]
    public void Nothing_is_listening_for_live_input_unless_a_host_asks()
    {
        using var context = new BunitContext();

        var slider = context.Render<Slider>(parameters => parameters.Add(c => c.AriaLabel, "Pace"));

        // A circuit round trip per pixel of a drag is what the absent handler saves.
        Assert.DoesNotContain("oninput", slider.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("99", 10)]
    [InlineData("-4", 0)]
    public void A_committed_value_is_held_inside_the_range(string typed, int expected)
    {
        using var context = new BunitContext();
        decimal? committed = null;

        var slider = context.Render<Slider>(parameters => parameters
            .Add(c => c.Max, 10m)
            .Add(c => c.AriaLabel, "Pace")
            .Add(c => c.ValueChanged, (decimal value) => committed = value));

        slider.Find("input").Change(typed);

        Assert.Equal(expected, committed);
    }

    [Fact]
    public void A_value_that_is_not_a_number_is_ignored()
    {
        using var context = new BunitContext();
        var committed = 0;

        var slider = context.Render<Slider>(parameters => parameters
            .Add(c => c.AriaLabel, "Pace")
            .Add(c => c.ValueChanged, (decimal _) => committed++));

        slider.Find("input").Change("fast");

        Assert.Equal(0, committed);
    }

    [Fact]
    public void A_value_outside_the_range_is_drawn_at_the_nearer_end_and_a_backwards_range_collapses()
    {
        using var context = new BunitContext();

        var high = context.Render<Slider>(parameters => parameters
            .Add(c => c.Max, 5m)
            .Add(c => c.Value, 40m)
            .Add(c => c.AriaLabel, "Pace"));
        var backwards = context.Render<Slider>(parameters => parameters
            .Add(c => c.Min, 8m)
            .Add(c => c.Max, 2m)
            .Add(c => c.Value, 1m)
            .Add(c => c.AriaLabel, "Pace"));

        Assert.Equal("5", high.Find("input").GetAttribute("value"));
        Assert.Equal("8", backwards.Find("input").GetAttribute("max"));
        Assert.Equal("8", backwards.Find("input").GetAttribute("value"));
    }

    [Fact]
    public void A_disabled_slider_says_so_and_takes_a_modifier_class()
    {
        using var context = new BunitContext();

        var slider = context.Render<Slider>(parameters => parameters
            .Add(c => c.Disabled, true)
            .Add(c => c.CssClass, "roadmap-pace__slider")
            .Add(c => c.AriaLabel, "Pace"));

        var input = slider.Find("input");
        Assert.True(input.HasAttribute("disabled"));
        Assert.Contains("slider", input.ClassList);
        Assert.Contains("roadmap-pace__slider", input.ClassList);
    }

    [Fact]
    public void BaseClass_replaces_the_class_the_slider_carries()
    {
        using var context = new BunitContext();

        var slider = context.Render<Slider>(parameters => parameters
            .Add(c => c.BaseClass, "host-range")
            .Add(c => c.AriaLabel, "Pace"));

        Assert.Equal("host-range", slider.Find("input").ClassName);
    }

    [Fact]
    public void A_host_that_moves_the_value_moves_the_thumb()
    {
        using var context = new BunitContext();

        var slider = context.Render<Slider>(parameters => parameters
            .Add(c => c.Max, 10m)
            .Add(c => c.Value, 2m)
            .Add(c => c.AriaLabel, "Pace"));

        slider.Render(parameters => parameters
            .Add(c => c.Max, 10m)
            .Add(c => c.Value, 7.5m)
            .Add(c => c.AriaLabel, "Pace"));

        Assert.Equal("7.5", slider.Find("input").GetAttribute("value"));
    }
}
