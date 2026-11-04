using Backlog.Mobile.UI.Services;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The Capture shortcut on the Android launcher icon opens the capture sheet over the Inbox: the
/// head hands the request over, the layout follows it to <c>capture?from=inbox</c> — also when the
/// tap launched the app and arrived before the layout was drawn, and only once.
/// </summary>
public sealed class CaptureShortcutRequestTests
{
    [Fact]
    public void A_tap_that_launched_the_app_opens_the_capture_sheet_over_the_inbox()
    {
        using var host = ShellHost.Paired();
        host.Service<CaptureOpenRequests>().Request();

        var app = host.Open("");

        app.WaitForAssertion(() =>
        {
            Assert.Equal(host.Navigation.BaseUri + "capture?from=inbox", host.Navigation.Uri);
            Assert.Equal("true", app.Find("[data-testid='capture-sheet-target-inbox']").GetAttribute("aria-pressed"));
            Assert.Equal("Add to inbox", app.Find("[data-testid='capture-sheet-save']").TextContent.Trim());
        });
    }

    [Fact]
    public void A_tap_while_the_app_is_open_on_another_tab_opens_the_capture_sheet()
    {
        using var host = ShellHost.Paired();
        var app = host.Open("notes");

        host.Service<CaptureOpenRequests>().Request();

        app.WaitForAssertion(() =>
        {
            Assert.Equal(host.Navigation.BaseUri + "capture?from=inbox", host.Navigation.Uri);
            Assert.NotNull(app.Find("[data-testid='capture-sheet-save']"));
        });
    }

    [Fact]
    public void Cancelling_the_sheet_the_shortcut_opened_lands_on_the_inbox()
    {
        using var host = ShellHost.Paired();
        host.Service<CaptureOpenRequests>().Request();
        var app = host.Open("");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='capture-sheet-cancel']")));

        app.Find("[data-testid='capture-sheet-cancel']").Click();

        Assert.Equal(host.Navigation.BaseUri + "inbox", host.Navigation.Uri);
    }

    [Fact]
    public void An_unpaired_phone_shows_the_pairing_box()
    {
        using var host = ShellHost.Unpaired();
        host.Service<CaptureOpenRequests>().Request();

        var app = host.Open("");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='pairing-code-field'] input")));
    }

    [Fact]
    public void A_request_is_followed_once()
    {
        var requests = new CaptureOpenRequests();
        requests.Request();

        var first = 0;
        var second = 0;
        using (requests.Subscribe(() => first++)) { }
        using (requests.Subscribe(() => second++)) { }

        Assert.Equal(1, first);
        Assert.Equal(0, second);
    }

    [Fact]
    public void A_request_reaches_every_listener_and_none_that_stopped()
    {
        var requests = new CaptureOpenRequests();
        var listening = 0;
        var stopped = 0;
        using var subscription = requests.Subscribe(() => listening++);
        requests.Subscribe(() => stopped++).Dispose();

        requests.Request();

        Assert.Equal(1, listening);
        Assert.Equal(0, stopped);
    }
}
