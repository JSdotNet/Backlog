namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The key to the scenario dot: each state once, in words a screen reader hears
/// once, and the dot everywhere else still named on its own.
/// </summary>
public sealed class ScenarioStateLegendTests
{
    [Fact]
    public void Each_state_is_its_word_and_its_dot_is_hidden_from_a_screen_reader()
    {
        using var context = new BunitContext();

        var legend = context.Render<ScenarioStateLegend>();

        var entries = legend.FindAll("[data-testid='scenario-legend-item']");
        Assert.Equal(["Passed", "Failed", "Stale", "Never run"], entries.Select(entry => entry.TextContent.Trim()));
        Assert.All(entries, entry =>
        {
            var dot = entry.QuerySelector(".devbook-scenario-dot")!;
            Assert.Equal("true", dot.GetAttribute("aria-hidden"));
            Assert.Null(dot.GetAttribute("role"));
            Assert.Null(dot.GetAttribute("aria-label"));
        });
    }

    /// <summary>A dot standing alone — a tree row, a checklist row — has no word
    /// beside it, so it keeps its own name.</summary>
    [Fact]
    public void A_dot_on_its_own_keeps_its_name()
    {
        using var context = new BunitContext();

        var dot = context.Render<ScenarioStateDot>(parameters => parameters.Add(p => p.State, "failed")).Find("span");

        Assert.Equal("img", dot.GetAttribute("role"));
        Assert.Equal("Scenario failed", dot.GetAttribute("aria-label"));
        Assert.Null(dot.GetAttribute("aria-hidden"));
    }
}
