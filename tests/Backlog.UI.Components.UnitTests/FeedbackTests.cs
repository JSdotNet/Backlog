namespace Backlog.UI.Components.UnitTests;

public sealed class SaveIndicatorTests
{
    [Fact]
    public void Idle_says_nothing_at_all()
    {
        using var context = new BunitContext();

        var indicator = context.Render<SaveIndicator>();

        Assert.Equal(string.Empty, indicator.Markup.Trim());
    }

    [Theory]
    [InlineData(SaveState.Saving, "Saving...", "save-indicator--saving")]
    [InlineData(SaveState.Saved, "Saved", "save-indicator--saved")]
    [InlineData(SaveState.Failed, "Could not save", "save-indicator--error")]
    public void Every_state_renders_its_own_text_and_modifier(SaveState state, string text, string modifier)
    {
        using var context = new BunitContext();

        var indicator = context.Render<SaveIndicator>(parameters => parameters.Add(i => i.State, state));

        Assert.Contains(text, indicator.Markup, StringComparison.Ordinal);
        Assert.Contains(modifier, indicator.Find("span").ClassList);
    }

    [Fact]
    public void The_indicator_announces_without_interrupting()
    {
        // Nothing here has a save button, so this line is the only confirmation a
        // reader gets - but it must never cut across what is being typed.
        using var context = new BunitContext();

        var indicator = context.Render<SaveIndicator>(parameters => parameters.Add(i => i.State, SaveState.Saved));
        var region = indicator.Find("span");

        Assert.Equal("status", region.GetAttribute("role"));
        Assert.Equal("polite", region.GetAttribute("aria-live"));
    }
}

public sealed class SkeletonTests
{
    [Theory]
    [InlineData(SkeletonShape.Text, "skeleton--text")]
    [InlineData(SkeletonShape.Heading, "skeleton--heading")]
    [InlineData(SkeletonShape.Block, "skeleton--block")]
    public void Every_shape_wears_its_own_modifier(SkeletonShape shape, string modifier)
    {
        using var context = new BunitContext();

        var bar = context.Render<Skeleton>(parameters => parameters.Add(s => s.Shape, shape));

        Assert.Contains("skeleton", bar.Find("span").ClassList);
        Assert.Contains(modifier, bar.Find("span").ClassList);
    }

    [Fact]
    public void A_bar_says_nothing_to_a_screen_reader()
    {
        // A screenful of these is one wait, not forty announcements. Whoever is
        // waiting says so once, in a live region of its own.
        using var context = new BunitContext();

        var bar = context.Render<Skeleton>();

        Assert.Equal("true", bar.Find("span").GetAttribute("aria-hidden"));
        Assert.Equal(string.Empty, bar.Find("span").TextContent);
    }

    [Fact]
    public void The_width_is_the_hosts_to_set_and_is_absent_until_it_does()
    {
        using var context = new BunitContext();

        Assert.False(context.Render<Skeleton>().Find("span").HasAttribute("style"));

        var sized = context.Render<Skeleton>(parameters => parameters.Add(s => s.Width, "62%"));

        Assert.Contains("width: 62%", sized.Find("span").GetAttribute("style"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_host_can_name_the_bar_itself_or_take_the_styling_over_entirely()
    {
        using var context = new BunitContext();

        // Its own name for the bar, with the shape still doing its one job.
        var named = context.Render<Skeleton>(parameters => parameters
            .Add(s => s.BaseClass, "tools-panel__bar")
            .Add(s => s.Shape, SkeletonShape.Block)
            .Add(s => s.CssClass, "is-wide"));

        Assert.Contains("tools-panel__bar", named.Find("span").ClassList);
        Assert.Contains("skeleton--block", named.Find("span").ClassList);
        Assert.Contains("is-wide", named.Find("span").ClassList);

        // Or none of it: a null base drops the library's height along with its
        // surface, for a host whose own rule already sizes the element.
        var bare = context.Render<Skeleton>(parameters => parameters
            .Add(s => s.BaseClass, null)
            .Add(s => s.CssClass, "tools-panel__bar"));

        Assert.Equal(["tools-panel__bar"], bare.Find("span").ClassList);
    }
}

public sealed class ToastTests
{
    [Theory]
    [InlineData(ToastSeverity.Info, "status")]
    [InlineData(ToastSeverity.Success, "status")]
    [InlineData(ToastSeverity.Warning, "alert")]
    [InlineData(ToastSeverity.Error, "alert")]
    public void Only_something_going_wrong_is_worth_an_interruption(ToastSeverity severity, string role)
    {
        using var context = new BunitContext();

        var toast = context.Render<Toast>(parameters => parameters
            .Add(t => t.Severity, severity)
            .Add(t => t.DurationMilliseconds, 0)
            .Add(t => t.Message, "Pushed to GitHub"));

        Assert.Equal(role, toast.Find(".toast").GetAttribute("role"));
    }

    [Fact]
    public void Dismissing_removes_the_toast_and_reports_it()
    {
        using var context = new BunitContext();
        var dismissed = 0;

        var toast = context.Render<Toast>(parameters => parameters
            .Add(t => t.DurationMilliseconds, 0)
            .Add(t => t.Message, "Pushed to GitHub")
            .Add(t => t.OnDismissed, () => dismissed++));

        toast.Find(".toast__dismiss").Click();

        Assert.Equal(1, dismissed);
        Assert.Equal(string.Empty, toast.Markup.Trim());
    }

    [Fact]
    public void A_toast_that_cannot_be_dismissed_has_no_dismiss_button()
    {
        using var context = new BunitContext();

        var toast = context.Render<Toast>(parameters => parameters
            .Add(t => t.DurationMilliseconds, 0)
            .Add(t => t.Dismissable, false)
            .Add(t => t.Message, "Pushed to GitHub"));

        Assert.Empty(toast.FindAll(".toast__dismiss"));
    }
}
