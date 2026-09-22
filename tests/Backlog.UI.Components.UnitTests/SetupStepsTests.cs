using Backlog.UI.Components.Feedback;

namespace Backlog.UI.Components.UnitTests;

public sealed class SetupStepsTests
{
    private static readonly SetupStep[] Steps =
    [
        new("Paste a key", true, "Only an admin can create one.", "https://example.test/keys", "Open the keys page"),
        new("Name the account", false),
        new("Pick a workspace", false, Optional: true)
    ];

    [Fact]
    public void Each_step_is_a_list_item_that_says_whether_it_is_done()
    {
        using var context = new BunitContext();

        var list = context.Render<SetupSteps>(parameters => parameters
            .Add(p => p.Steps, Steps)
            .Add(p => p.AriaLabel, "Setting up"));

        var ol = list.Find("ol.setup-steps");
        Assert.Equal("Setting up", ol.GetAttribute("aria-label"));

        var items = list.FindAll("[data-testid='setup-step']");
        Assert.Equal(3, items.Count);
        Assert.Equal("true", items[0].GetAttribute("data-done"));
        Assert.Contains("setup-step--done", items[0].ClassName, StringComparison.Ordinal);
        Assert.Equal("false", items[1].GetAttribute("data-done"));
        Assert.DoesNotContain("setup-step--done", items[1].ClassName, StringComparison.Ordinal);
    }

    /// <summary>The tick is a glyph, and a glyph carries nothing on its own: the
    /// state is in text for a reader that never sees the mark.</summary>
    [Fact]
    public void Done_is_said_in_text_as_well_as_drawn()
    {
        using var context = new BunitContext();

        var list = context.Render<SetupSteps>(parameters => parameters.Add(p => p.Steps, Steps));

        var items = list.FindAll("[data-testid='setup-step']");
        Assert.Equal("✓", items[0].QuerySelector(".setup-step__mark")!.TextContent);
        Assert.Equal("true", items[0].QuerySelector(".setup-step__mark")!.GetAttribute("aria-hidden"));
        Assert.Contains("done", items[0].QuerySelector(".sr-only")!.TextContent, StringComparison.Ordinal);
        Assert.Equal(string.Empty, items[1].QuerySelector(".setup-step__mark")!.TextContent);
        Assert.Contains("to do", items[1].QuerySelector(".sr-only")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_step_with_somewhere_to_go_links_there_beside_the_app()
    {
        using var context = new BunitContext();

        var list = context.Render<SetupSteps>(parameters => parameters.Add(p => p.Steps, Steps));

        var link = list.Find("[data-testid='setup-step'] a.setup-step__link");
        Assert.Equal("https://example.test/keys", link.GetAttribute("href"));
        Assert.Equal("Open the keys page", link.TextContent);
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Contains("noopener", link.GetAttribute("rel"), StringComparison.Ordinal);
        Assert.Contains("Only an admin can create one.", list.FindAll("[data-testid='setup-step']")[0].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void An_optional_step_says_so()
    {
        using var context = new BunitContext();

        var list = context.Render<SetupSteps>(parameters => parameters.Add(p => p.Steps, Steps));

        var items = list.FindAll("[data-testid='setup-step']");
        Assert.NotNull(items[2].QuerySelector(".setup-step__optional"));
        Assert.Null(items[1].QuerySelector(".setup-step__optional"));
    }
}
