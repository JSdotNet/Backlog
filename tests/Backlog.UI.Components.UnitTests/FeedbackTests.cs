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
