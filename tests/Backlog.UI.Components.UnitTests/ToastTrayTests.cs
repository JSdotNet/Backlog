using Microsoft.Extensions.DependencyInjection;

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

    [Fact]
    public async Task An_offer_draws_its_button_runs_it_on_the_press_and_goes()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var channel = new ToastChannel();
        context.Services.AddSingleton<IToastChannel>(channel);
        var pressed = 0;
        channel.Publish(new ToastMessage
        {
            Message = "House move is done.",
            TestId = "offer",
            ActionLabel = "Move the project note to the archive",
            Action = () =>
            {
                pressed++;
                return Task.CompletedTask;
            },
        });

        var tray = context.Render<ToastTray>();
        var button = tray.Find("[data-testid='offer-action']");
        Assert.Equal("Move the project note to the archive", button.TextContent.Trim());

        await button.ClickAsync(new());

        Assert.Equal(1, pressed);
        Assert.Empty(channel.Visible);
    }

    [Fact]
    public void A_toast_with_no_action_draws_no_button()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var channel = new ToastChannel();
        context.Services.AddSingleton<IToastChannel>(channel);
        channel.Publish(ToastMessage.Info("Saved.", "plain"));

        var tray = context.Render<ToastTray>();

        Assert.NotNull(tray.Find("[data-testid='plain']"));
        Assert.Empty(tray.FindAll("[data-testid='plain-action']"));
    }
}
