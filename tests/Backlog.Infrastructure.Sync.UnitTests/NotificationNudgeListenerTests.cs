using System.Net;
using System.Threading.Channels;

using Backlog.Infrastructure.Sync.Notifications;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sync.Abstractions;
using Backlog.SharedKernel;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The desktop's nudge socket (local ADR 0038 §4) against a scripted negotiate and a fake hub:
/// it opens the socket and tells the loop so, has the loop run on every "pull now", hands the
/// replies back to the two-minute poll the moment the socket drops, reconnects with a growing
/// wait, and against a service with no hub never opens a socket at all.
/// </summary>
public sealed class NotificationNudgeListenerTests
{
    private static readonly Uri HubUrl = new("wss://wps-test.webpubsub.azure.com/client/hubs/notifications?access_token=minted");

    private static readonly DeviceCredential Paired = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "Workshop PC",
        "a-registration-credential");

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_pull_nudge_runs_the_loop_and_the_socket_being_open_is_said()
    {
        using var fixture = Fixture.Start(negotiate: _ => Url(HubUrl));

        var socket = await fixture.Connector.NextSocket.WaitAsync(Patience, Cancellation);
        await fixture.Target.WaitForAsync(t => t.OpenStates.SequenceEqual([true]));

        Assert.Equal(HubUrl, Assert.Single(fixture.Connector.Urls));
        Assert.True(fixture.Listener.IsOpen);

        socket.Send("""{"type":"pull","kind":"reply"}""");
        socket.Send("""{"type":"pull","kind":"notification"}""");
        await fixture.Target.WaitForAsync(t => t.SyncRequests == 2);
    }

    /// <summary>The socket only ever wakes the loop: anything that is not a pull is ignored.</summary>
    [Fact]
    public async Task A_message_that_is_not_a_pull_is_ignored()
    {
        using var fixture = Fixture.Start(negotiate: _ => Url(HubUrl));

        var socket = await fixture.Connector.NextSocket.WaitAsync(Patience, Cancellation);

        socket.Send("""{"type":"push","kind":"reply"}""");
        socket.Send("not json");
        socket.Send("""{"type":"pull","kind":"reply"}""");
        await fixture.Target.WaitForAsync(t => t.SyncRequests == 1);

        Assert.Equal(1, fixture.Target.SyncRequests);
    }

    /// <summary>The fallback: the moment the socket drops, the loop is told, so its two-minute
    /// tick lists the replies again; the listener waits the first retry delay, negotiates afresh
    /// and opens a new socket.</summary>
    [Fact]
    public async Task A_dropped_socket_hands_the_replies_back_to_the_poll_and_reconnects()
    {
        using var fixture = Fixture.Start(negotiate: _ => Url(HubUrl));

        var first = await fixture.Connector.NextSocket.WaitAsync(Patience, Cancellation);
        await fixture.Target.WaitForAsync(t => t.OpenStates.Count == 1);

        fixture.Clock.Advance(NotificationNudgeListener.StableConnection);
        first.Drop();

        await fixture.Target.WaitForAsync(t => t.OpenStates.SequenceEqual([true, false]));
        Assert.False(fixture.Listener.IsOpen);
        Assert.Equal(NotificationNudgeListener.FirstRetryDelay, await fixture.Waits.Reader.ReadAsync(Cancellation));

        fixture.Clock.Advance(NotificationNudgeListener.FirstRetryDelay);

        await fixture.Connector.NextSocket.WaitAsync(Patience, Cancellation);
        await fixture.Target.WaitForAsync(t => t.OpenStates.SequenceEqual([true, false, true]));
        Assert.Equal(2, fixture.NegotiateCalls);
    }

    /// <summary>A socket the hub closes as soon as it opened did not work, so the wait grows as
    /// it does for a refusal, rather than renegotiating — and waking the sync service — every
    /// five seconds.</summary>
    [Fact]
    public async Task A_socket_closed_at_once_is_retried_with_a_growing_wait()
    {
        using var fixture = Fixture.Start(negotiate: _ => Url(HubUrl));

        (await fixture.Connector.NextSocket.WaitAsync(Patience, Cancellation)).Drop();
        var first = await fixture.Waits.Reader.ReadAsync(Cancellation).AsTask().WaitAsync(Patience, Cancellation);
        fixture.Clock.Advance(first);

        (await fixture.Connector.NextSocket.WaitAsync(Patience, Cancellation)).Drop();
        var second = await fixture.Waits.Reader.ReadAsync(Cancellation).AsTask().WaitAsync(Patience, Cancellation);

        Assert.Equal(NotificationNudgeListener.FirstRetryDelay, first);
        Assert.Equal(NotificationNudgeListener.FirstRetryDelay * 2, second);
    }

    /// <summary>A hub that refuses the socket is tried again after a wait that doubles each
    /// time, up to the cap; the loop never hears the socket was open.</summary>
    [Fact]
    public async Task A_socket_that_will_not_open_is_retried_with_a_growing_wait()
    {
        using var fixture = Fixture.Start(negotiate: _ => Url(HubUrl), refuseConnects: int.MaxValue);

        var waits = new List<TimeSpan>();
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var wait = await fixture.Waits.Reader.ReadAsync(Cancellation).AsTask().WaitAsync(Patience, Cancellation);
            waits.Add(wait);
            fixture.Clock.Advance(wait);
        }

        Assert.Equal(
            [
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(40),
                TimeSpan.FromSeconds(80), TimeSpan.FromSeconds(160), NotificationNudgeListener.MaxRetryDelay, NotificationNudgeListener.MaxRetryDelay,
            ],
            waits);
        Assert.Empty(fixture.Target.OpenStates);
    }

    /// <summary>A failing negotiate — the sync service unreachable — is retried the same way.</summary>
    [Fact]
    public async Task A_failed_negotiate_is_retried_and_opens_no_socket()
    {
        using var fixture = Fixture.Start(negotiate: _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        Assert.Equal(NotificationNudgeListener.FirstRetryDelay, await fixture.Waits.Reader.ReadAsync(Cancellation).AsTask().WaitAsync(Patience, Cancellation));
        fixture.Clock.Advance(NotificationNudgeListener.FirstRetryDelay);
        Assert.Equal(NotificationNudgeListener.FirstRetryDelay * 2, await fixture.Waits.Reader.ReadAsync(Cancellation).AsTask().WaitAsync(Patience, Cancellation));

        Assert.Empty(fixture.Connector.Urls);
        Assert.Empty(fixture.Target.OpenStates);
    }

    /// <summary>The Aspire run: the service has no hub, so no socket is ever opened, the loop
    /// keeps polling, and the listener asks again only after the long wait.</summary>
    [Fact]
    public async Task A_service_without_a_hub_leaves_the_desktop_polling()
    {
        using var fixture = Fixture.Start(negotiate: _ => Url(null));

        Assert.Equal(NotificationNudgeListener.UnavailableRetry, await fixture.Waits.Reader.ReadAsync(Cancellation).AsTask().WaitAsync(Patience, Cancellation));

        Assert.Empty(fixture.Connector.Urls);
        Assert.Empty(fixture.Target.OpenStates);
        Assert.Equal(1, fixture.NegotiateCalls);
    }

    /// <summary>Closing a gate — notifications switched off — closes the socket and tells the
    /// loop; the listener does not reconnect while the gate stays closed.</summary>
    [Fact]
    public async Task Closing_a_gate_closes_the_socket()
    {
        using var fixture = Fixture.Start(negotiate: _ => Url(HubUrl));

        await fixture.Connector.NextSocket.WaitAsync(Patience, Cancellation);
        await fixture.Target.WaitForAsync(t => t.OpenStates.Count == 1);

        fixture.Features.Set(NotificationsFeatures.Notifications, false);

        await fixture.Target.WaitForAsync(t => t.OpenStates.SequenceEqual([true, false]));
        await fixture.Listener.Loop.WaitAsync(Patience, Cancellation);
        Assert.Single(fixture.Connector.Urls);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task Nothing_is_negotiated_while_a_gate_is_closed(bool syncOn, bool notificationsOn, bool paired)
    {
        using var fixture = Fixture.Start(negotiate: _ => Url(HubUrl), syncOn, notificationsOn, paired);

        await Task.Delay(TimeSpan.FromMilliseconds(100), Cancellation);

        Assert.Equal(0, fixture.NegotiateCalls);
        Assert.Empty(fixture.Connector.Urls);
    }

    private static HttpResponseMessage Url(Uri? url) =>
        StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            url is null ? """{"url":null}""" : $$"""{"url":"{{url}}"}""");

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _http;
        private int _negotiateCalls;

        private Fixture(Func<HttpRequestMessage, HttpResponseMessage> negotiate, int refuseConnects, bool syncOn, bool notificationsOn, bool paired)
        {
            var handler = new StubHttpMessageHandler((request, _) =>
            {
                Interlocked.Increment(ref _negotiateCalls);
                Assert.Equal(SyncRoutes.Absolute(SyncRoutes.NotificationNudge), request.RequestUri!.AbsolutePath);
                return negotiate(request);
            });

            _http = new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") };
            Connector = new FakeConnector(refuseConnects);
            Features = new Gates(syncOn, notificationsOn);

            var credentials = paired ? new InMemoryDeviceCredentialStore(Paired) : new InMemoryDeviceCredentialStore();
            var client = new NotificationSyncClient(_http);

            // The notifications gate starts closed so the waits can be replaced before the loop
            // starts; then it opens, unless the test is about it staying closed.
            var startOpen = notificationsOn;
            Features.SetQuietly(NotificationsFeatures.Notifications, false);

            Listener = new NotificationNudgeListener(new ClientProvider(client), Target, Features, credentials, Connector, Clock)
            {
                // The timer is armed before the wait is reported, so a test that advances the
                // clock as soon as it reads the wait always moves past it.
                Delay = async (delay, cancellationToken) =>
                {
                    var waiting = Task.Delay(delay, Clock, cancellationToken);
                    await Waits.Writer.WriteAsync(delay, cancellationToken).ConfigureAwait(false);
                    await waiting.ConfigureAwait(false);
                },
            };

            Features.Set(NotificationsFeatures.Notifications, startOpen);
        }

        public NotificationNudgeListener Listener { get; }

        public RecordingTarget Target { get; } = new();

        public FakeConnector Connector { get; }

        public Gates Features { get; }

        public FakeTimeProvider Clock { get; } = new();

        /// <summary>Every wait the loop starts, in order.</summary>
        public Channel<TimeSpan> Waits { get; } = Channel.CreateUnbounded<TimeSpan>();

        public int NegotiateCalls => Volatile.Read(ref _negotiateCalls);

        public static Fixture Start(
            Func<HttpRequestMessage, HttpResponseMessage> negotiate,
            bool syncOn = true,
            bool notificationsOn = true,
            bool paired = true,
            int refuseConnects = 0) =>
            new(negotiate, refuseConnects, syncOn, notificationsOn, paired);

        public void Dispose()
        {
            Listener.Dispose();
            _http.Dispose();
        }
    }

    private sealed class ClientProvider(NotificationSyncClient client) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(NotificationSyncClient) ? client : null;
    }

    internal sealed class RecordingTarget : INotificationNudgeTarget
    {
        private readonly Lock _gate = new();
        private readonly List<bool> _openStates = [];
        private int _syncRequests;

        public int SyncRequests => Volatile.Read(ref _syncRequests);

        public IReadOnlyList<bool> OpenStates
        {
            get
            {
                lock (_gate) return [.. _openStates];
            }
        }

        public void RequestSync() => Interlocked.Increment(ref _syncRequests);

        public void SetNudgeOpen(bool open)
        {
            lock (_gate) _openStates.Add(open);
        }

        public async Task WaitForAsync(Func<RecordingTarget, bool> condition)
        {
            var deadline = DateTime.UtcNow + Patience;

            while (!condition(this))
            {
                Assert.True(DateTime.UtcNow < deadline, "The listener never reached the expected state.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
        }
    }

    internal sealed class FakeConnector(int refuseConnects) : INotificationNudgeSocketConnector
    {
        private readonly Channel<FakeSocket> _sockets = Channel.CreateUnbounded<FakeSocket>();
        private readonly List<Uri> _urls = [];
        private int _refusalsLeft = refuseConnects;

        public IReadOnlyList<Uri> Urls
        {
            get
            {
                lock (_urls) return [.. _urls];
            }
        }

        /// <summary>The next socket the listener opens.</summary>
        public Task<FakeSocket> NextSocket => _sockets.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask();

        public Task<INotificationNudgeSocket> ConnectAsync(Uri url, CancellationToken cancellationToken)
        {
            lock (_urls) _urls.Add(url);

            if (Interlocked.Decrement(ref _refusalsLeft) >= 0)
            {
                throw new System.Net.WebSockets.WebSocketException("The hub refused the connection.");
            }

            var socket = new FakeSocket();
            _sockets.Writer.TryWrite(socket);
            return Task.FromResult<INotificationNudgeSocket>(socket);
        }
    }

    internal sealed class FakeSocket : INotificationNudgeSocket
    {
        private readonly Channel<string?> _messages = Channel.CreateUnbounded<string?>();
        private Exception? _drop;

        public void Send(string message) => _messages.Writer.TryWrite(message);

        /// <summary>The connection goes away underneath the listener.</summary>
        public void Drop()
        {
            _drop = new System.Net.WebSockets.WebSocketException("The connection dropped.");
            _messages.Writer.TryComplete();
        }

        public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await _messages.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ChannelClosedException) when (_drop is not null)
            {
                throw _drop;
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal sealed class Gates(bool syncOn, bool notificationsOn) : IAppFeatureSettings
    {
        private readonly Dictionary<string, bool> _on = new(StringComparer.Ordinal)
        {
            [SyncFeatures.Sync] = syncOn,
            [NotificationsFeatures.Notifications] = notificationsOn,
        };

        public event Action? Changed;

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => "in memory";

        public bool IsEnabled(string key)
        {
            lock (_on) return _on.TryGetValue(key, out var on) && on;
        }

        public string? SetEnabled(string key, bool enabled)
        {
            Set(key, enabled);
            return null;
        }

        public void Set(string key, bool enabled)
        {
            SetQuietly(key, enabled);
            Changed?.Invoke();
        }

        public void SetQuietly(string key, bool enabled)
        {
            lock (_on) _on[key] = enabled;
        }
    }
}
