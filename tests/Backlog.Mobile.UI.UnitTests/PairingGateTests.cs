using System.Net;
using System.Text;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// What every tab is before it is anything else: an unpaired phone.
///
/// <para>The sync endpoints are bearer-only, so a device with no credential
/// cannot capture and cannot list. The shell answers that with the one thing
/// there is to do, on whichever route the app opened on, rather than with a 401
/// under a field that will not work — and goes back to being the app the moment
/// a code is redeemed.</para>
/// </summary>
public sealed class PairingGateTests
{
    [Theory]
    [InlineData("")]
    [InlineData("inbox")]
    [InlineData("notes")]
    public void An_unpaired_phone_is_offered_a_pairing_code_and_nothing_else_on_every_route(string route)
    {
        using var host = ShellHost.Unpaired();

        var app = host.Open(route);

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='pairing-code-field'] input")));

        Assert.Equal("Pair this device", app.Find("h1").TextContent);
        Assert.NotNull(app.Find("[data-testid='pairing-submit']"));
        Assert.Contains("already paired", app.Find("[data-testid='pairing-notice']").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Not paired", app.Find("[data-testid='sync-status'] .sync-status__text").TextContent);

        // No page is drawn behind the gate, and no tabs lead to one: every one of
        // them would come back to this box.
        Assert.Empty(app.FindAll("[data-testid='capture-field']"));
        Assert.Empty(app.FindAll("[data-testid='notes']"));
        Assert.Empty(app.FindAll("[data-testid='tasks-empty']"));
        Assert.Empty(app.FindAll("[data-testid='tab-bar']"));

        // And nothing was asked of the service, because there is nothing to ask
        // with.
        Assert.Equal(0, host.InboxRequests);
    }

    [Fact]
    public void Redeeming_a_code_opens_the_page_the_app_was_opened_on()
    {
        using var host = ShellHost.Unpaired();
        var app = host.Open("");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='pairing-code-field'] input")));

        app.Find("[data-testid='pairing-code-field'] input").Input("K7MN-9PQR");
        app.Find("[data-testid='pairing-submit']").Click();

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='tasks-empty']")));
        Assert.Empty(app.FindAll("[data-testid='pairing-code-field']"));
        Assert.NotNull(app.Find("[data-testid='tab-bar']"));

        // My Day pulls the task feed the moment it opens, so the newly paired
        // phone has already heard from the service.
        app.WaitForAssertion(() =>
            Assert.Equal("Synced just now", app.Find("[data-testid='sync-status'] .sync-status__text").TextContent));
    }

    [Fact]
    public void Redeeming_a_code_on_the_inbox_turns_it_into_the_capture_screen()
    {
        using var host = ShellHost.Unpaired();
        var app = host.Open();

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='pairing-code-field'] input")));

        app.Find("[data-testid='pairing-code-field'] input").Input("K7MN-9PQR");
        app.Find("[data-testid='pairing-submit']").Click();

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='capture-field'] input")));
        app.WaitForAssertion(() => Assert.Equal(1, host.InboxRequests));
        Assert.Empty(app.FindAll("[data-testid='pairing-code-field']"));
    }

    /// <summary>
    /// A code that has already let a device in comes back as a 409 with a code of
    /// its own, and the gate says so. bUnit swallows what an event handler
    /// throws, so this asserts on the message the failure has to produce rather
    /// than on the absence of an exception.
    /// </summary>
    [Fact]
    public void A_code_that_was_already_used_says_so_and_leaves_the_device_unpaired()
    {
        using var host = ShellHost.Unpaired(pair: (_, _) => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent(
                """
                {"type":"https://backlog.jsdotnet.dev/problems/pairing.code_used","title":"Pairing failed","status":409,"detail":"That code has already paired a device.","code":"pairing.code_used"}
                """,
                Encoding.UTF8,
                "application/problem+json")
        });

        var app = host.Open();

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='pairing-code-field'] input")));

        app.Find("[data-testid='pairing-code-field'] input").Input("K7MN9PQR");
        app.Find("[data-testid='pairing-submit']").Click();

        app.WaitForAssertion(() => Assert.Contains(
            "already paired",
            app.Find("[data-testid='pairing-error']").TextContent,
            StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(app.Find("[data-testid='pairing-code-field'] input"));
        Assert.Null(host.Credentials.Current);
    }

    /// <summary>
    /// A pair attempt takes a round trip, and on a phone that can be seconds. The
    /// button says it is working for as long as the attempt is, and cannot be
    /// pressed a second time underneath it; once the answer is in, it is the Pair
    /// button again.
    /// </summary>
    [Fact]
    public void The_pair_button_is_busy_while_an_attempt_is_in_flight()
    {
        var answer = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = ShellHost.Unpaired(pairAsync: (_, _, _) => answer.Task);
        var app = host.Open();

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='pairing-code-field'] input")));

        app.Find("[data-testid='pairing-code-field'] input").Input("K7MN9PQR");
        app.Find("[data-testid='pairing-submit']").Click();

        app.WaitForAssertion(() =>
        {
            var button = app.Find("[data-testid='pairing-submit']");
            Assert.Equal("true", button.GetAttribute("aria-busy"));
            // Blocked, but still in the tab order: aria-disabled, not the native attribute.
            Assert.Equal("true", button.GetAttribute("aria-disabled"));
            // The visible words change, not only the spinner's hidden label, and the
            // two say different things so a screen reader does not hear one twice.
            var spinnerLabel = button.QuerySelector(".spinner .sr-only")?.TextContent;
            Assert.Equal("Waiting for the sync service", spinnerLabel);
            Assert.Equal("Pairing…", button.TextContent.Replace(spinnerLabel!, string.Empty).Trim());
        });

        answer.SetResult(new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent(
                """
                {"type":"https://backlog.jsdotnet.dev/problems/pairing.code_used","title":"Pairing failed","status":409,"detail":"That code has already paired a device.","code":"pairing.code_used"}
                """,
                Encoding.UTF8,
                "application/problem+json")
        });

        app.WaitForAssertion(() =>
        {
            var button = app.Find("[data-testid='pairing-submit']");
            Assert.False(button.HasAttribute("aria-busy"));
            Assert.False(button.HasAttribute("disabled"));
            Assert.Null(button.QuerySelector(".spinner"));
            Assert.Equal("Pair", button.TextContent.Trim());
        });
    }

    /// <summary>
    /// A service that never answers — an address the phone cannot route to — is
    /// not left to the HTTP client's hundred seconds. The gate gives up on its own
    /// clock and says why in words, not as "A task was canceled.", and the button
    /// can be pressed again.
    /// </summary>
    [Fact]
    public void A_service_that_never_answers_is_reported_within_the_gates_timeout()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-13T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        using var host = ShellHost.Unpaired(
            pairAsync: async (_, _, cancellationToken) =>
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new InvalidOperationException("Unreachable: the delay only ends by cancellation.");
            },
            clock);
        var app = host.Open();

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='pairing-code-field'] input")));

        app.Find("[data-testid='pairing-code-field'] input").Input("K7MN9PQR");
        app.Find("[data-testid='pairing-submit']").Click();

        app.WaitForAssertion(() =>
            Assert.Equal("true", app.Find("[data-testid='pairing-submit']").GetAttribute("aria-busy")));
        Assert.Empty(app.FindAll("[data-testid='pairing-error']"));

        clock.Advance(TimeSpan.FromSeconds(15));

        app.WaitForAssertion(() => Assert.Equal(
            "The sync service did not answer in time.",
            app.Find("[data-testid='pairing-error']").TextContent.Trim()));

        var button = app.Find("[data-testid='pairing-submit']");
        Assert.False(button.HasAttribute("aria-busy"));
        Assert.False(button.HasAttribute("disabled"));
        Assert.Null(host.Credentials.Current);
    }

    [Fact]
    public void The_pair_button_waits_for_a_code_to_be_typed()
    {
        using var host = ShellHost.Unpaired();
        var app = host.Open();

        app.WaitForAssertion(() =>
            Assert.True(app.Find("[data-testid='pairing-submit']").HasAttribute("disabled")));

        app.Find("[data-testid='pairing-code-field'] input").Input("K7MN9PQR");

        app.WaitForAssertion(() =>
            Assert.False(app.Find("[data-testid='pairing-submit']").HasAttribute("disabled")));
    }
}
