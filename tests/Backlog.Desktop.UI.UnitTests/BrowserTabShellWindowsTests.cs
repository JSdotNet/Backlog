using Backlog.Desktop.UI.Shell;
using Bunit;
using Microsoft.JSInterop;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A surface served as a web page (local ADR 0041 §7): its pop-out opens a new browser
/// tab on <c>/window/{surfaceId}</c>, its window title is its tab's, and the page reads
/// the surface id off its own address.
/// </summary>
public sealed class BrowserTabShellWindowsTests
{
    [Fact]
    public void Open_opens_the_surface_route_in_a_new_tab()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var windows = new BrowserTabShellWindows(context.JSInterop.JSRuntime);

        windows.Open("devbook", new Dictionary<string, string> { ["chapter"] = "arc42/adr/0040.md", ["scope"] = "JSdotNet/Backlog" });

        var call = Assert.Single(context.JSInterop.Invocations);
        Assert.Equal("backlogShellWindow.open", call.Identifier);
        Assert.Equal(["/window/devbook?chapter=arc42%2Fadr%2F0040.md&scope=JSdotNet%2FBacklog"], call.Arguments);
    }

    /// <summary>OpenAsync answers what the browser did: true for a tab it opened, false
    /// for a pop-up it blocked.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OpenAsync_answers_whether_the_browser_opened_the_tab(bool opened)
    {
        using var context = new BunitContext();
        context.JSInterop.Setup<bool>("backlogShellWindow.open", _ => true).SetResult(opened);
        var windows = new BrowserTabShellWindows(context.JSInterop.JSRuntime);

        Assert.Equal(opened, await windows.OpenAsync("tasks"));
    }

    [Fact]
    public void A_title_is_the_tabs_and_close_closes_the_tab()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var windows = new BrowserTabShellWindows(context.JSInterop.JSRuntime);

        windows.SetTitle("tab", "Devbook");
        windows.Close("tab");
        windows.ActivateMainWindow();

        Assert.Equal(
            ["backlogShellWindow.setTitle", "backlogShellWindow.close"],
            context.JSInterop.Invocations.Select(call => call.Identifier));
        Assert.Equal(["Devbook - Backlog"], Assert.Single(context.JSInterop.Invocations["backlogShellWindow.setTitle"]).Arguments);
    }

    [Fact]
    public void A_refused_or_disconnected_open_is_never_thrown_into_the_renderer()
    {
        using var context = new BunitContext();
        context.JSInterop.Setup<bool>("backlogShellWindow.open", _ => true).SetException(new JSDisconnectedException("The circuit went."));
        var windows = new BrowserTabShellWindows(context.JSInterop.JSRuntime);

        windows.Open("devbook");

        Assert.Equal(["/window/devbook"], Assert.Single(context.JSInterop.Invocations).Arguments);
    }

    [Fact]
    public async Task A_disconnected_open_answers_not_opened()
    {
        using var context = new BunitContext();
        context.JSInterop.Setup<bool>("backlogShellWindow.open", _ => true).SetException(new JSDisconnectedException("The circuit went."));
        var windows = new BrowserTabShellWindows(context.JSInterop.JSRuntime);

        Assert.False(await windows.OpenAsync("devbook"));
    }

    [Theory]
    [InlineData("window/devbook", "devbook")]
    [InlineData("window/devbook?chapter=a.md", "devbook")]
    [InlineData("window/devbook#top", "devbook")]
    [InlineData("Window/pull%20requests/extra", "pull requests")]
    [InlineData("", "")]
    [InlineData("settings", "")]
    public void The_page_reads_its_surface_off_the_address(string baseRelativePath, string surfaceId) =>
        Assert.Equal(surfaceId, SurfaceWindowHost.SurfaceIdOf(baseRelativePath));
}
