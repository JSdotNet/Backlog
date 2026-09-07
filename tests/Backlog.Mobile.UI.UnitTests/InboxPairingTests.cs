using System.Net;
using System.Text;

using Backlog.Infrastructure.Sync;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// What the Inbox screen is before it is anything else: an unpaired phone.
///
/// <para>The inbox endpoints are bearer-only, so a device with no credential
/// cannot capture and cannot list. The screen answers that with the one thing
/// there is to do rather than with a 401 under a capture field that will not
/// work — and goes back to being the capture screen the moment a code is
/// redeemed.</para>
/// </summary>
public sealed class InboxPairingTests
{
    /// <summary>A store that already has a credential, shared with the share and
    /// dictation tests: both are about a screen that is past this point, and
    /// neither should have to know how a device gets there.</summary>
    internal static IDeviceCredentialStore PairedStore() => new InMemoryDeviceCredentialStore(
        new DeviceCredential(Guid.NewGuid(), Guid.NewGuid(), "Phone", "a-registration-credential"));

    [Fact]
    public void An_unpaired_phone_is_offered_a_pairing_code_and_nothing_else()
    {
        using var host = InboxHost.Unpaired();

        var page = host.Render();

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='pairing-code-field'] input")));

        Assert.NotNull(page.Find("[data-testid='pairing-submit']"));
        Assert.Contains("Pair this device", page.Find("[data-testid='pairing-notice']").TextContent, StringComparison.OrdinalIgnoreCase);

        // The capture flow is not merely disabled, it is not there: a field that
        // posts to an endpoint this device cannot call is a field that lies.
        Assert.Empty(page.FindAll("[data-testid='capture-field']"));
        Assert.Empty(page.FindAll("[data-testid='capture-submit']"));
        Assert.Empty(page.FindAll("[data-testid='speech-toggle']"));

        // And nothing was asked of the service, because there is nothing to ask
        // with.
        Assert.Equal(0, host.InboxRequests);
    }

    [Fact]
    public void A_paired_phone_is_the_capture_screen_it_always_was()
    {
        using var host = InboxHost.Paired();

        var page = host.Render();

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='capture-field'] input")));

        Assert.Empty(page.FindAll("[data-testid='pairing-code-field']"));
        Assert.Empty(page.FindAll("[data-testid='pairing-notice']"));
    }

    [Fact]
    public void Redeeming_a_code_turns_the_screen_into_the_capture_screen()
    {
        using var host = InboxHost.Unpaired();
        var page = host.Render();

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='pairing-code-field'] input")));

        page.Find("[data-testid='pairing-code-field'] input").Input("K7MN-9PQR");
        page.Find("[data-testid='pairing-submit']").Click();

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='capture-field'] input")));
        Assert.Empty(page.FindAll("[data-testid='pairing-code-field']"));
    }

    /// <summary>
    /// A code that has already let a device in comes back as a 409 with a code of
    /// its own, and the screen says so. bUnit swallows what an event handler
    /// throws, so this asserts on the message the failure has to produce rather
    /// than on the absence of an exception.
    /// </summary>
    [Fact]
    public void A_code_that_was_already_used_says_so_and_leaves_the_device_unpaired()
    {
        using var host = InboxHost.Unpaired(pair: (_, _) => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent(
                """
                {"type":"https://backlog.jsdotnet.dev/problems/pairing.code_used","title":"Pairing failed","status":409,"detail":"That code has already paired a device.","code":"pairing.code_used"}
                """,
                Encoding.UTF8,
                "application/problem+json")
        });

        var page = host.Render();

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='pairing-code-field'] input")));

        page.Find("[data-testid='pairing-code-field'] input").Input("K7MN9PQR");
        page.Find("[data-testid='pairing-submit']").Click();

        page.WaitForAssertion(() => Assert.Contains(
            "already paired",
            page.Find("[data-testid='pairing-error']").TextContent,
            StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(page.Find("[data-testid='pairing-code-field'] input"));
    }

    [Fact]
    public void The_pair_button_waits_for_a_code_to_be_typed()
    {
        using var host = InboxHost.Unpaired();
        var page = host.Render();

        page.WaitForAssertion(() =>
            Assert.True(page.Find("[data-testid='pairing-submit']").HasAttribute("disabled")));

        page.Find("[data-testid='pairing-code-field'] input").Input("K7MN9PQR");

        page.WaitForAssertion(() =>
            Assert.False(page.Find("[data-testid='pairing-submit']").HasAttribute("disabled")));
    }

    /// <summary>The screen, its credential store and a sync service that answers
    /// pairing from a script and the inbox with nothing.</summary>
    private sealed class InboxHost : IDisposable
    {
        private readonly BunitContext _context = new();
        private readonly ScriptedSyncHandler _handler;

        private InboxHost(IDeviceCredentialStore credentials, Func<HttpRequestMessage, int, HttpResponseMessage>? pair)
        {
            _handler = new ScriptedSyncHandler(pair);

            _context.JSInterop.Mode = JSRuntimeMode.Loose;
            _context.Services.AddSingleton<ISharedContentReceiver>(new TestSharedContentReceiver());
            _context.Services.AddSingleton<ISpeechTranscriber>(new SilentSpeechTranscriber());
            _context.Services.AddSingleton(credentials);
            _context.Services.AddSingleton(new CloudSyncClient(
                new HttpClient(_handler) { BaseAddress = new Uri("https://sync.test") }));
            _context.Services.AddSingleton(new DevicePairingClient(
                new HttpClient(_handler) { BaseAddress = new Uri("https://sync.test") },
                credentials));
        }

        public int InboxRequests => _handler.InboxRequests;

        public static InboxHost Unpaired(Func<HttpRequestMessage, int, HttpResponseMessage>? pair = null) =>
            new(new InMemoryDeviceCredentialStore(), pair);

        public static InboxHost Paired() => new(PairedStore(), pair: null);

        public IRenderedComponent<Inbox> Render() => _context.Render<Inbox>();

        public void Dispose() => _context.Dispose();
    }

    private sealed class ScriptedSyncHandler(Func<HttpRequestMessage, int, HttpResponseMessage>? pair) : HttpMessageHandler
    {
        private int _pairAttempts;

        public int InboxRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (path.Contains("/devices/pair", StringComparison.Ordinal))
            {
                var index = _pairAttempts++;

                return Task.FromResult(pair is not null
                    ? pair(request, index)
                    : new HttpResponseMessage(HttpStatusCode.Created)
                    {
                        Content = new StringContent(
                            $$"""{"ownerId":"{{Guid.NewGuid()}}","deviceId":"{{Guid.NewGuid()}}","credential":"a-registration-credential"}""",
                            Encoding.UTF8,
                            "application/json")
                    });
            }

            InboxRequests++;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });
        }
    }

    /// <summary>A device with no recogniser, so the mic never takes a turn while
    /// these tests are reading the markup around it.</summary>
    private sealed class SilentSpeechTranscriber : ISpeechTranscriber
    {
        public ValueTask<bool> IsSupportedAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);

        public Task<SpeechTranscript> ListenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(SpeechTranscript.Failed("No recogniser."));

        public ValueTask StopAsync() => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
