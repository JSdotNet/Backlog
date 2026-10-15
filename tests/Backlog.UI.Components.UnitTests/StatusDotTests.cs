namespace Backlog.UI.Components.UnitTests;

public sealed class StatusDotTests
{
    [Theory]
    [InlineData(FlowStepTone.Done, "done")]
    [InlineData(FlowStepTone.Active, "active")]
    [InlineData(FlowStepTone.Blocked, "blocked")]
    [InlineData(FlowStepTone.Pending, "pending")]
    [InlineData(FlowStepTone.Skipped, "skipped")]
    public void The_dot_wears_its_tone_and_is_hidden_from_assistive_technology(FlowStepTone tone, string slug)
    {
        using var context = new BunitContext();

        var dot = context.Render<StatusDot>(parameters => parameters.Add(p => p.Tone, tone).Add(p => p.CssClass, "row__dot"));
        var span = dot.Find("span");

        Assert.Equal($"status-dot status-dot--{slug} row__dot", span.ClassName);
        Assert.Equal(slug, span.GetAttribute("data-tone"));
        Assert.Equal("true", span.GetAttribute("aria-hidden"));
        Assert.Empty(span.TextContent);
    }
}
