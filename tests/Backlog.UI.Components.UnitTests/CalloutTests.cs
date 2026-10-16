namespace Backlog.UI.Components.UnitTests;

public sealed class CalloutTests
{
    [Fact]
    public void A_callout_is_a_note_with_its_glyph_headline_and_why()
    {
        using var context = new BunitContext();

        var callout = context.Render<Callout>(parameters => parameters
            .Add(p => p.Heading, "2.1× the usual flow-code run")
            .Add(p => p.Icon, (RenderFragment)(builder => builder.AddMarkupContent(0, "<svg class=\"glyph\" aria-hidden=\"true\"></svg>")))
            .Add(p => p.ChildContent, (RenderFragment)(builder => builder.AddContent(0, "Implement explains most of it.")))
            .Add(p => p.TestId, "insight"));

        var root = callout.Find("[data-testid='insight']");

        // Standing content rather than an announcement: a note, never an alert.
        Assert.Equal("note", root.GetAttribute("role"));
        Assert.Equal("callout callout--quiet", root.ClassName);
        Assert.Equal(["callout__icon", "callout__body"], root.Children.Select(child => child.ClassName));
        Assert.Equal("2.1× the usual flow-code run", callout.Find("[data-testid='insight-heading']").TextContent);
        Assert.Equal("Implement explains most of it.", callout.Find("[data-testid='insight-text']").TextContent);

        // No action asked for, none drawn.
        Assert.Empty(callout.FindAll("button"));
    }

    [Fact]
    public void Nothing_to_say_draws_nothing()
    {
        using var context = new BunitContext();

        var callout = context.Render<Callout>(parameters => parameters.Add(p => p.TestId, "empty"));

        Assert.Empty(callout.Markup.Trim());
    }

    [Fact]
    public void The_warning_tone_wears_its_modifier_and_any_other_word_reads_quiet()
    {
        using var context = new BunitContext();

        var warning = context.Render<Callout>(parameters => parameters.Add(p => p.Heading, "Higher").Add(p => p.Tone, "warning"));
        var other = context.Render<Callout>(parameters => parameters.Add(p => p.Heading, "Other").Add(p => p.Tone, "alert"));

        Assert.Equal("warning", warning.Find(".callout").GetAttribute("data-tone"));
        Assert.Contains("callout--warning", warning.Find(".callout").ClassList);
        Assert.Equal("quiet", other.Find(".callout").GetAttribute("data-tone"));
    }

    /// <summary>The action is both or nothing, and wears the id a host returns the
    /// focus to.</summary>
    [Fact]
    public void The_action_is_a_button_with_the_hosts_id_and_needs_both_a_label_and_a_callback()
    {
        using var context = new BunitContext();
        var pressed = 0;

        var callout = context.Render<Callout>(parameters => parameters
            .Add(p => p.Heading, "Unusual")
            .Add(p => p.ActionLabel, "Compare configurations for this flow")
            .Add(p => p.ActionId, "insight-run-1")
            .Add(p => p.ActionTestId, "insight-action")
            .Add(p => p.OnAction, () => pressed++));

        var action = callout.Find("[data-testid='insight-action']");
        Assert.Equal("BUTTON", action.TagName);
        Assert.Equal("insight-run-1", action.Id);
        Assert.Equal("callout__action", action.ClassName);
        Assert.Equal("Compare configurations for this flow", action.TextContent.Trim());

        action.Click();
        Assert.Equal(1, pressed);

        var labelOnly = context.Render<Callout>(parameters => parameters
            .Add(p => p.Heading, "Unusual")
            .Add(p => p.ActionLabel, "Compare configurations for this flow"));

        Assert.Empty(labelOnly.FindAll("button"));
    }
}
