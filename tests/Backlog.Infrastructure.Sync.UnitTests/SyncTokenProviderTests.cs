using System.Globalization;
using System.Net;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The cache in front of the token endpoint. Its whole job is to call that
/// endpoint as rarely as it can get away with and never once more than it has
/// to, so every test here counts requests.
/// </summary>
public sealed class SyncTokenProviderTests
{
    private static readonly DeviceCredential Paired =
        new(Guid.NewGuid(), Guid.NewGuid(), "Workshop PC", "a-registration-credential");

    [Fact]
    public async Task An_unpaired_device_asks_for_nothing()
    {
        using var fixture = Fixture.Create(new InMemoryDeviceCredentialStore());

        Assert.Null(await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));
        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public async Task The_token_is_fetched_once_and_reused_while_it_is_fresh()
    {
        using var fixture = Fixture.Create(new InMemoryDeviceCredentialStore(Paired));

        var first = await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken);
        var second = await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken);

        Assert.Equal("token-0", first);
        Assert.Equal("token-0", second);
        Assert.Single(fixture.Handler.Requests);
        Assert.Equal("/api/sync/devices/token", fixture.Handler.Requests[0].Path);

        // The token endpoint is anonymous, and the client it goes out on has no
        // authentication handler: a provider that authenticated its own token
        // request would need a token to get a token.
        Assert.Null(fixture.Handler.Requests[0].AuthorizationScheme);
    }

    /// <summary>
    /// Renewal happens two minutes before expiry, not at it. Twenty-nine minutes
    /// in, the cached token is still perfectly valid and is still replaced,
    /// because a request that leaves now must not arrive after it lapses.
    /// </summary>
    [Fact]
    public async Task The_token_is_renewed_inside_the_last_two_minutes_of_its_life()
    {
        using var fixture = Fixture.Create(new InMemoryDeviceCredentialStore(Paired));

        Assert.Equal("token-0", await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));

        // Twenty-seven minutes on: three minutes left, still outside the window.
        fixture.Clock.Advance(TimeSpan.FromMinutes(27));
        Assert.Equal("token-0", await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));
        Assert.Single(fixture.Handler.Requests);

        // Two more: one minute left, inside it.
        fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal("token-1", await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, fixture.Handler.Requests.Count);
    }

    /// <summary>
    /// A 401 says the credential is no longer accepted, which sending it again
    /// cannot fix (inherited ADR 0015). One attempt, no token, and the caller
    /// goes out unauthenticated to hear the service's own answer.
    /// </summary>
    [Fact]
    public async Task A_rejected_credential_costs_exactly_one_attempt()
    {
        using var fixture = Fixture.Create(
            new InMemoryDeviceCredentialStore(Paired),
            (_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        Assert.Null(await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));
        Assert.Single(fixture.Handler.Requests);
    }

    [Fact]
    public async Task A_service_that_is_not_there_yields_no_token_rather_than_an_exception()
    {
        using var fixture = Fixture.Create(
            new InMemoryDeviceCredentialStore(Paired),
            (_, _) => throw new HttpRequestException("No route to host."));

        Assert.Null(await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Several screens waking together share one request. Without the
    /// single-flight each of them would mint a token, and every one but the last
    /// would be thrown away — which is both wasteful and, on the service side,
    /// indistinguishable from a device stuck in a loop.
    /// </summary>
    [Fact]
    public async Task Concurrent_callers_share_one_request()
    {
        var inFlight = new TaskCompletionSource();
        using var fixture = Fixture.Create(new InMemoryDeviceCredentialStore(Paired), before: () => inFlight.Task);

        var callers = Enumerable
            .Range(0, 8)
            .Select(_ => fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken))
            .ToArray();

        inFlight.SetResult();
        var tokens = await Task.WhenAll(callers);

        Assert.All(tokens, token => Assert.Equal("token-0", token));
        Assert.Single(fixture.Handler.Requests);
    }

    /// <summary>
    /// Pairing, unpairing and re-registering all replace the credential the
    /// cached token was minted from. The provider hears that from the store
    /// rather than finding out when the old token expires half an hour later.
    /// </summary>
    [Fact]
    public async Task A_new_credential_drops_the_cached_token()
    {
        var store = new InMemoryDeviceCredentialStore(Paired);
        using var fixture = Fixture.Create(store);

        Assert.Equal("token-0", await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));

        store.Save(Paired with { DeviceId = Guid.NewGuid(), Credential = "a-second-credential" });

        Assert.Equal("token-1", await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, fixture.Handler.Requests.Count);
    }

    /// <summary>
    /// Unpairing while a token request is in flight. The request left under a
    /// credential this device no longer holds, so what comes back belongs to the
    /// previous owner: it is dropped rather than cached. Caching it would leave
    /// an unpaired device bearing the last owner's token for the rest of the
    /// half hour, which is the one outcome unpairing is supposed to prevent.
    /// </summary>
    [Fact]
    public async Task Unpairing_while_a_token_is_in_flight_throws_the_answer_away()
    {
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var store = new InMemoryDeviceCredentialStore(Paired);
        using var fixture = Fixture.Create(store, before: () =>
        {
            started.TrySetResult();
            return release.Task;
        });

        var pending = fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken);

        // The request is on the wire when the person unpairs the device.
        await started.Task;
        store.Clear();
        release.SetResult();

        Assert.Null(await pending);

        // And nothing was left behind for the next caller to pick up.
        Assert.Null(await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));
        Assert.Single(fixture.Handler.Requests);
    }

    /// <summary>
    /// The same race, but the device is re-paired rather than unpaired. The
    /// token in flight names the device that asked for it, which is no longer
    /// the device this store holds, so it is dropped and the next call mints one
    /// for the credential that is actually current.
    /// </summary>
    [Fact]
    public async Task Re_pairing_while_a_token_is_in_flight_throws_the_answer_away()
    {
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var store = new InMemoryDeviceCredentialStore(Paired);
        using var fixture = Fixture.Create(store, before: () =>
        {
            started.TrySetResult();
            return release.Task;
        });

        var pending = fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken);

        await started.Task;
        store.Save(Paired with { DeviceId = Guid.NewGuid(), Credential = "a-second-credential" });
        release.SetResult();

        Assert.Null(await pending);

        Assert.Equal("token-1", await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, fixture.Handler.Requests.Count);
    }

    [Fact]
    public async Task Invalidating_by_hand_drops_it_too()
    {
        using var fixture = Fixture.Create(new InMemoryDeviceCredentialStore(Paired));

        Assert.Equal("token-0", await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));
        fixture.Provider.Invalidate();

        Assert.Equal("token-1", await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The provider, its named client and a scripted service, composed through
    /// the real <c>IHttpClientFactory</c> so the "sync-token" name is exercised
    /// rather than assumed.
    /// </summary>
    internal sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _services;

        private Fixture(ServiceProvider services, StubHttpMessageHandler handler, FakeTimeProvider clock)
        {
            _services = services;
            Handler = handler;
            Clock = clock;
            Provider = services.GetRequiredService<SyncTokenProvider>();
        }

        public StubHttpMessageHandler Handler { get; }

        public FakeTimeProvider Clock { get; }

        public SyncTokenProvider Provider { get; }

        public static Fixture Create(
            IDeviceCredentialStore credentials,
            Func<HttpRequestMessage, int, HttpResponseMessage>? respond = null,
            Func<Task>? before = null)
        {
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-09-07T09:00:00Z", CultureInfo.InvariantCulture));
            var handler = new StubHttpMessageHandler(respond ?? ((_, index) => TokenResponse(index, clock)), before);

            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton(credentials);
            services.AddSingleton<SyncTokenProvider>();
            services.AddHttpClient(SyncTokenProvider.HttpClientName, client => client.BaseAddress = new Uri("https://sync.test"))
                .ConfigurePrimaryHttpMessageHandler(() => handler);

            return new Fixture(services.BuildServiceProvider(), handler, clock);
        }

        /// <summary>A thirty-minute token, numbered so a test can tell the second
        /// one from the first.</summary>
        public static HttpResponseMessage TokenResponse(int index, TimeProvider? clock = null)
        {
            var expires = (clock?.GetUtcNow() ?? DateTimeOffset.UtcNow).AddMinutes(30);

            return StubHttpMessageHandler.Json(
                HttpStatusCode.OK,
                $$"""
                {"accessToken":"token-{{index}}","expiresAt":"{{expires.ToString("O", CultureInfo.InvariantCulture)}}","tokenType":"Bearer"}
                """);
        }

        public void Dispose() => _services.Dispose();
    }
}
