using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// A paired phone letting go of its pairing, from the sync status line, and
/// pairing again — the first thing to do when the service has lost the device,
/// as a Cosmos emulator does overnight.
///
/// <para>Forgetting clears the credential and nothing else. What is waiting in
/// the outbox stays on the phone, the pairing box says so, and a new pairing
/// sends it at once rather than whenever the backoff next comes round.</para>
/// </summary>
public sealed class ForgetDeviceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 9, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("")]
    [InlineData("note")]
    [InlineData("tasks")]
    public void Forgetting_the_device_shows_the_pairing_box_on_every_tab(string route)
    {
        using var host = ShellHost.Paired();
        var app = host.Open(route);

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='tab-bar']")));

        app.Find("[data-testid='sync-status-button']").Click();
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='forget-device-dialog']")));
        app.Find("[data-testid='forget-device-confirm']").Click();

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='pairing-code-field'] input")));
        Assert.Null(host.Credentials.Current);
        Assert.Empty(app.FindAll("[data-testid='tab-bar']"));
        Assert.Equal("Not paired", app.Find("[data-testid='sync-status'] .sync-status__text").TextContent);

        // There is nothing left to forget, so the line is only a line again.
        Assert.Empty(app.FindAll("[data-testid='sync-status-button']"));
    }

    [Fact]
    public void Cancelling_keeps_the_device_paired()
    {
        using var host = ShellHost.Paired();
        var app = host.Open();

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='capture-field'] input")));

        app.Find("[data-testid='sync-status-button']").Click();
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='forget-device-dialog']")));
        app.Find("[data-testid='forget-device-cancel']").Click();

        app.WaitForAssertion(() => Assert.Empty(app.FindAll("[data-testid='forget-device-dialog']")));
        Assert.NotNull(host.Credentials.Current);
        Assert.NotNull(app.Find("[data-testid='capture-field'] input"));
        Assert.Empty(app.FindAll("[data-testid='pairing-code-field']"));
    }

    [Fact]
    public void Nothing_waiting_is_said_as_plainly_as_something_waiting()
    {
        using var host = ShellHost.Paired();
        var app = host.Open();

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='capture-field'] input")));

        app.Find("[data-testid='sync-status-button']").Click();

        app.WaitForAssertion(() => Assert.Contains(
            "Nothing is waiting to send",
            app.Find("[data-testid='forget-device-dialog']").TextContent));
    }

    [Fact]
    public void A_waiting_capture_survives_a_re_pair_and_is_sent_once_the_phone_is_paired_again()
    {
        // The clock never moves, so no backoff timer fires on its own: whatever
        // sends the capture after the new pairing is the pairing itself.
        var inbox = new ScriptedInboxService { State = InboxServiceState.Unreachable };
        using var host = ShellHost.Paired(inbox, clock: new FakeTimeProvider(Now));
        var app = host.Open();

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='inbox-pull-notice']")));
        app.Find("[data-testid='capture-field'] input").Input("Ask the speaker for the slides");
        app.Find("[data-testid='capture-submit']").Click();
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='inbox-waiting']")));

        // The question says what happens to it before anything is forgotten.
        app.Find("[data-testid='sync-status-button']").Click();
        app.WaitForAssertion(() =>
        {
            var question = app.Find("[data-testid='forget-device-dialog']").TextContent;
            Assert.Contains("1 item waiting to send stays on this phone", question);
            Assert.Contains("paired again", question);
        });
        app.Find("[data-testid='forget-device-confirm']").Click();

        // Forgotten, and the capture is still here, and the pairing box says so.
        app.WaitForAssertion(() => Assert.Contains(
            "1 item is waiting",
            app.Find("[data-testid='pairing-waiting']").TextContent));
        Assert.Null(host.Credentials.Current);
        Assert.Single(host.Outbox.Entries);

        // The service is back — as a fresh emulator is, with the device gone —
        // and a new code is redeemed.
        inbox.State = InboxServiceState.Answering;
        app.Find("[data-testid='pairing-code-field'] input").Input("K7MN-9PQR");
        app.Find("[data-testid='pairing-submit']").Click();

        app.WaitForAssertion(() =>
        {
            Assert.Empty(host.Outbox.Entries);
            Assert.Equal(1, inbox.Created);
        });
        Assert.Contains(inbox.Received, capture => capture.Title == "Ask the speaker for the slides");
        app.WaitForAssertion(() => Assert.Equal("false", app.Find("[data-testid='inbox-row']").GetAttribute("data-waiting")));
    }
}
