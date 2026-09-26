using System.Net;
using System.Text;

using Backlog.Infrastructure.Sync;
using Backlog.Mobile.UI.Components;
using Backlog.Mobile.UI.Outbox;
using Backlog.Mobile.UI.Tasks;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The whole app as the hosts render it — the Router, the layout and whichever
/// page the address names — over a sync service that answers pairing from a
/// script and the inbox from a <see cref="ScriptedInboxService"/>.
/// </summary>
internal sealed class ShellHost : IDisposable
{
    private readonly BunitContext _context = new();
    private readonly ScriptedSyncHandler _handler;

    private ShellHost(
        IDeviceCredentialStore credentials,
        Func<HttpRequestMessage, int, HttpResponseMessage>? pair,
        ScriptedInboxService? inbox,
        IDeviceStore? store,
        TimeProvider? clock,
        ScriptedTaskService? tasks = null,
        ITaskViewStore? taskView = null)
    {
        Inbox = inbox ?? new ScriptedInboxService();
        Tasks = tasks ?? new ScriptedTaskService();
        _handler = new ScriptedSyncHandler(pair, Inbox, Tasks);
        Credentials = credentials;

        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        if (clock is not null) _context.Services.AddSingleton(clock);
        _context.Services.AddSingleton<ISharedContentReceiver>(new TestSharedContentReceiver());
        _context.Services.AddSingleton<ISpeechTranscriber>(new SilentSpeechTranscriber());
        _context.Services.AddSingleton(credentials);
        _context.Services.AddSingleton(new CloudSyncClient(
            new HttpClient(_handler) { BaseAddress = new Uri("https://sync.test") }));
        _context.Services.AddSingleton(new DevicePairingClient(
            new HttpClient(_handler) { BaseAddress = new Uri("https://sync.test") },
            credentials));
        _context.Services.AddMobileShell();
        _context.Services.AddTestDeviceOutbox(store, taskView);
    }

    public IDeviceCredentialStore Credentials { get; }

    /// <summary>The service's side of the inbox: what it holds, whether it
    /// answers, and every capture that reached it.</summary>
    public ScriptedInboxService Inbox { get; }

    public int InboxRequests => Inbox.Requests;

    /// <summary>The service's side of the task feed.</summary>
    public ScriptedTaskService Tasks { get; }

    public NavigationManager Navigation => _context.Services.GetRequiredService<NavigationManager>();

    public DeviceOutbox Outbox => _context.Services.GetRequiredService<DeviceOutbox>();

    public static ShellHost Unpaired(Func<HttpRequestMessage, int, HttpResponseMessage>? pair = null) =>
        new(TestDevices.Unpaired(), pair, inbox: null, store: null, clock: null);

    public static ShellHost Paired(
        ScriptedInboxService? inbox = null,
        IDeviceStore? store = null,
        TimeProvider? clock = null,
        ScriptedTaskService? tasks = null,
        ITaskViewStore? taskView = null) =>
        new(TestDevices.Paired(), pair: null, inbox, store, clock, tasks, taskView);

    /// <summary>Renders the app opened on <paramref name="route"/>, relative to
    /// the base address — "" is the Inbox.</summary>
    public IRenderedComponent<Routes> Open(string route = "")
    {
        Navigation.NavigateTo(route);
        return _context.Render<Routes>();
    }

    public void Dispose() => _context.Dispose();

    private sealed class ScriptedSyncHandler(
        Func<HttpRequestMessage, int, HttpResponseMessage>? pair,
        ScriptedInboxService inbox,
        ScriptedTaskService tasks) : HttpMessageHandler
    {
        private int _pairAttempts;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (path.Contains("/devices/pair", StringComparison.Ordinal))
            {
                var index = _pairAttempts++;

                return pair is not null
                    ? pair(request, index)
                    : new HttpResponseMessage(HttpStatusCode.Created)
                    {
                        Content = new StringContent(
                            $$"""{"ownerId":"{{Guid.NewGuid()}}","deviceId":"{{Guid.NewGuid()}}","credential":"a-registration-credential"}""",
                            Encoding.UTF8,
                            "application/json")
                    };
            }

            if (path.EndsWith("/api/sync/tasks", StringComparison.Ordinal))
            {
                return await tasks.AnswerAsync(request, cancellationToken);
            }

            return await inbox.AnswerAsync(request, cancellationToken);
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
