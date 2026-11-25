using Backlog.Infrastructure.Sync;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// A phone bound to its Keystore key whose key will not open (local ADR 0058 §2):
/// it has no proof to send, so nothing syncs, and the shell says so on every tab
/// with the way back — pairing again — rather than going quiet.
/// </summary>
public sealed class KeyUnavailableTests
{
    [Theory]
    [InlineData("")]
    [InlineData("inbox")]
    public async Task A_key_the_phone_cannot_use_is_said_on_every_tab(string route)
    {
        using var host = ShellHost.KeyBound();
        var app = host.Open(route);

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='tab-bar']")));
        Assert.Empty(app.FindAll("[data-testid='sync-key-unavailable']"));

        await FetchTokenAsync(host);

        app.WaitForAssertion(() =>
        {
            var callout = app.Find("[data-testid='sync-key-unavailable']");
            Assert.Equal("alert", callout.GetAttribute("role"));
            Assert.Contains("Nothing syncs until the phone is paired again", callout.TextContent);
        });

        // Still paired: the tabs stay, and so does whatever the phone holds.
        Assert.NotNull(app.Find("[data-testid='tab-bar']"));
    }

    [Fact]
    public async Task Pairing_again_from_the_callout_asks_first_then_shows_the_pairing_box()
    {
        using var host = ShellHost.KeyBound();
        var app = host.Open();

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='tab-bar']")));
        await FetchTokenAsync(host);
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='sync-key-unavailable-action']")));

        app.Find("[data-testid='sync-key-unavailable-action']").Click();

        // The status line's own forget dialog, which says what becomes of what waits.
        app.WaitForAssertion(() => Assert.Contains(
            "Nothing is waiting to send",
            app.Find("[data-testid='forget-device-dialog']").TextContent));
        Assert.NotNull(host.Credentials.Current);

        app.Find("[data-testid='forget-device-confirm']").Click();

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='pairing-code-field'] input")));
        Assert.Null(host.Credentials.Current);
        Assert.Empty(app.FindAll("[data-testid='sync-key-unavailable']"));
    }

    [Fact]
    public async Task Pairing_again_takes_the_callout_down()
    {
        using var host = ShellHost.KeyBound();
        var app = host.Open();

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='tab-bar']")));
        await FetchTokenAsync(host);
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='sync-key-unavailable']")));

        // A new pairing replaces the credential, and the verdict was about the old one.
        await app.InvokeAsync(() => host.Credentials.SaveAsync(
            new DeviceCredential(Guid.NewGuid(), Guid.NewGuid(), "Phone", "a-new-credential"),
            TestContext.Current.CancellationToken));

        app.WaitForAssertion(() => Assert.Empty(app.FindAll("[data-testid='sync-key-unavailable']")));
        Assert.NotNull(app.Find("[data-testid='tab-bar']"));
    }

    /// <summary>What a background sync does: asks for a token, which a device with
    /// no usable key cannot have.</summary>
    private static async Task FetchTokenAsync(ShellHost host)
    {
        var tokens = host.Service<SyncTokenProvider>();

        Assert.Null(await tokens.GetAccessTokenAsync(TestContext.Current.CancellationToken));
        Assert.True(tokens.KeyUnavailable);
    }
}
