using Backlog.Modules.Dashboard.UI.Parts;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The frame's Ask AI: drawn only where the pane cascades a handler, and pressing it
/// hands the handler the part's title.
/// </summary>
public sealed class DashboardPartFrameTests
{
    [Fact]
    public void Without_a_handler_cascaded_the_frame_draws_no_ask_ai()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var frame = context.Render<PartFrame>(parameters => parameters
            .Add(p => p.Title, "Throughput")
            .Add(p => p.TestId, "dashboard-headline"));

        Assert.Empty(frame.FindAll("[data-testid='dashboard-headline-ask-ai']"));
        Assert.NotEmpty(frame.FindAll("[data-testid='dashboard-headline-refresh']"));
    }

    [Fact]
    public void With_a_handler_cascaded_the_frame_draws_ask_ai_and_a_press_hands_over_its_title()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var asked = new List<string>();

        var frame = context.Render<PartFrame>(parameters => parameters
            .Add(p => p.Title, "Throughput")
            .Add(p => p.TestId, "dashboard-headline")
            .AddCascadingValue(new DashboardAskAi(title =>
            {
                asked.Add(title);
                return Task.CompletedTask;
            })));

        var button = frame.Find("[data-testid='dashboard-headline-ask-ai']");
        Assert.Equal("Ask AI about Throughput", button.GetAttribute("aria-label"));

        button.Click();

        Assert.Equal(["Throughput"], asked);
    }
}
