using System.Net;
using System.Text;

using Backlog.Infrastructure.Sync;
using Backlog.Mobile.UI.Components;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The whole app as the hosts render it — the Router, the layout and whichever
/// page the address names — over a sync service that answers pairing from a
/// script and the inbox with nothing.
/// </summary>
internal sealed class ShellHost : IDisposable
{
    private readonly BunitContext _context = new();
    private readonly ScriptedSyncHandler _handler;

    private ShellHost(IDeviceCredentialStore credentials, Func<HttpRequestMessage, int, HttpResponseMessage>? pair)
    {
        _handler = new ScriptedSyncHandler(pair);
        Credentials = credentials;

        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddSingleton<ISharedContentReceiver>(new TestSharedContentReceiver());
        _context.Services.AddSingleton<ISpeechTranscriber>(new SilentSpeechTranscriber());
        _context.Services.AddSingleton(credentials);
        _context.Services.AddSingleton(new CloudSyncClient(
            new HttpClient(_handler) { BaseAddress = new Uri("https://sync.test") }));
        _context.Services.AddSingleton(new DevicePairingClient(
            new HttpClient(_handler) { BaseAddress = new Uri("https://sync.test") },
            credentials));
        _context.Services.AddMobileShell();
    }

    public IDeviceCredentialStore Credentials { get; }

    public int InboxRequests => _handler.InboxRequests;

    public NavigationManager Navigation => _context.Services.GetRequiredService<NavigationManager>();

    public static ShellHost Unpaired(Func<HttpRequestMessage, int, HttpResponseMessage>? pair = null) =>
        new(TestDevices.Unpaired(), pair);

    public static ShellHost Paired() => new(TestDevices.Paired(), pair: null);

    /// <summary>Renders the app opened on <paramref name="route"/>, relative to
    /// the base address — "" is the Inbox.</summary>
    public IRenderedComponent<Routes> Open(string route = "")
    {
        Navigation.NavigateTo(route);
        return _context.Render<Routes>();
    }

    public void Dispose() => _context.Dispose();

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
