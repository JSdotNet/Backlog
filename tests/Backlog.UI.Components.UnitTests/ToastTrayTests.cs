namespace Backlog.UI.Components.UnitTests;

public sealed class ToastTrayTests
{
    [Fact]
    public void The_tray_asks_to_be_raised_above_every_modal()
    {
        // A Modal opens in the browser's top layer, which paints over every
        // z-index in the page. The tray carries the marker components.js looks for
        // when a Modal opens, so it is lifted into the top layer above that modal
        // rather than left underneath it.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var tray = context.Render<ToastTray>(parameters => parameters.Add(t => t.TestId, "tray"));

        var host = tray.Find("[data-testid='tray']");
        Assert.True(host.HasAttribute("data-backlog-top-layer"));

        // Not rendered as a popover: a popover nobody has shown is display: none,
        // and a tray that only appears once a script runs is a tray that can go
        // missing. components.js adds the attribute when it raises it.
        Assert.False(host.HasAttribute("popover"));
    }
}
